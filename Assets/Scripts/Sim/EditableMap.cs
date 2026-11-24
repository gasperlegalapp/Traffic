using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Traffic.Sim
{
    /// <summary>One drawn stretch of road between two points, straight or curved.</summary>
    public sealed class EditSegment
    {
        public int A, B;
        /// <summary>1 is a single one-way lane from A to B; 2 is one lane each way; 4 is two lanes each way.</summary>
        public int Lanes;
        /// <summary>Speed limit in miles per hour.</summary>
        public int Mph;
        /// <summary>A curved stretch bends toward Control: it leaves A heading for it and arrives at B coming from it.</summary>
        public bool Curved;
        public Vec Control;
    }

    /// <summary>Where a point lands when it is dropped on the map.</summary>
    public struct Snap
    {
        public Vec Point;
        /// <summary>Index of the road end or intersection it snapped to, or -1.</summary>
        public int Node;
        /// <summary>Index of the road it landed on, or -1, and how far along that road (0 to 1).</summary>
        public int Segment;
        public double T;
    }

    /// <summary>
    /// A road map the player draws: points joined by stretches of road, straight or curved,
    /// at any angle. Drawing across an existing road makes an intersection, and drawing to
    /// or from the middle of one splits it. ToWorld turns the drawing into a network that
    /// traffic can drive on, choosing stop signs or traffic lights from the roads that meet.
    /// </summary>
    public sealed class EditableMap
    {
        /// <summary>The shortest stretch of road allowed between two points, meters.</summary>
        public const double MinLength = 45;
        /// <summary>How close a click has to be to an existing point, or to a road, to land on it.</summary>
        public const double SnapNode = 13;
        public const double SnapRoad = 9;
        /// <summary>The sharpest angle allowed between two roads at one point, radians.</summary>
        public const double MinAngle = 40 * Math.PI / 180;
        /// <summary>The tightest curve allowed, as a turning radius in meters.</summary>
        public const double MinRadius = 22;
        public const int MaxArms = 4;
        public static readonly int[] LaneChoices = { 1, 2, 4 };
        public static readonly int[] SpeedChoices = { 25, 40, 55, 65 };

        /// <summary>How many straight pieces stand in for a curve when measuring and testing it.</summary>
        private const int CurveSteps = 32;

        public List<Vec> Nodes = new List<Vec>();
        public List<EditSegment> Segments = new List<EditSegment>();

        public EditableMap Clone()
        {
            var copy = new EditableMap();
            copy.Nodes.AddRange(Nodes);
            foreach (EditSegment s in Segments) copy.Segments.Add(new EditSegment { A = s.A, B = s.B, Lanes = s.Lanes, Mph = s.Mph, Curved = s.Curved, Control = s.Control });
            return copy;
        }

        // ---------------------------------------------------------------- shapes

        private static Vec Lerp(Vec a, Vec b, double t)
        {
            return new Vec(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);
        }

        private static double Dist(Vec a, Vec b)
        {
            return Geo.Hypot(a.X - b.X, a.Y - b.Y);
        }

        /// <summary>A point on the curve from p0 to p1 that bends toward c: t runs 0 to 1.</summary>
        public static Vec CurvePoint(Vec p0, Vec c, Vec p1, double t)
        {
            double u = 1 - t;
            return new Vec(u * u * p0.X + 2 * u * t * c.X + t * t * p1.X, u * u * p0.Y + 2 * u * t * c.Y + t * t * p1.Y);
        }

        /// <summary>The line a stretch of road follows, as points from one end to the other.</summary>
        public static Vec[] Trace(Vec p0, Vec p1, bool curved, Vec control, int steps = CurveSteps)
        {
            if (!curved) return new[] { p0, p1 };
            var pts = new Vec[steps + 1];
            for (int i = 0; i <= steps; i++) pts[i] = CurvePoint(p0, control, p1, (double)i / steps);
            return pts;
        }

        public Vec[] Trace(EditSegment s)
        {
            return Trace(Nodes[s.A], Nodes[s.B], s.Curved, s.Control);
        }

        private Vec PointAt(EditSegment s, double t)
        {
            return s.Curved ? CurvePoint(Nodes[s.A], s.Control, Nodes[s.B], t) : Lerp(Nodes[s.A], Nodes[s.B], t);
        }

        private static double LengthOf(Vec[] pts)
        {
            double len = 0;
            for (int i = 1; i < pts.Length; i++) len += Dist(pts[i - 1], pts[i]);
            return len;
        }

        /// <summary>Length of the part of a stretch between two positions along it.</summary>
        private double LengthBetween(EditSegment s, double t0, double t1)
        {
            if (!s.Curved) return Dist(Nodes[s.A], Nodes[s.B]) * Math.Abs(t1 - t0);
            double len = 0;
            Vec prev = PointAt(s, t0);
            for (int i = 1; i <= CurveSteps; i++)
            {
                Vec next = PointAt(s, t0 + (t1 - t0) * i / CurveSteps);
                len += Dist(prev, next);
                prev = next;
            }
            return len;
        }

        /// <summary>The closest spot on a traced line to a point: how far along (0 to 1) and how far away.</summary>
        private static double Project(Vec[] pts, Vec p, out double t, out Vec on)
        {
            double best = double.PositiveInfinity;
            t = 0;
            on = pts[0];
            int steps = pts.Length - 1;
            for (int i = 0; i < steps; i++)
            {
                Vec a = pts[i], b = pts[i + 1];
                double dx = b.X - a.X, dy = b.Y - a.Y;
                double len2 = dx * dx + dy * dy;
                double f = len2 < 1e-12 ? 0 : Geo.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / len2, 0, 1);
                var q = new Vec(a.X + dx * f, a.Y + dy * f);
                double d = Dist(q, p);
                if (d < best)
                {
                    best = d;
                    t = (i + f) / steps;
                    on = q;
                }
            }
            return best;
        }

        /// <summary>The tightest turn in a traced line, as a radius.</summary>
        private static double TightestRadius(Vec[] pts)
        {
            double r = double.PositiveInfinity;
            for (int i = 1; i < pts.Length - 1; i++) r = Math.Min(r, Geo.Circumradius(pts[i - 1], pts[i], pts[i + 1]));
            return r;
        }

        /// <summary>The direction a stretch sets off in from one of its ends.</summary>
        private double HeadingFrom(EditSegment s, int node)
        {
            Vec from = Nodes[node];
            Vec toward = s.Curved ? s.Control : Nodes[s.A == node ? s.B : s.A];
            return Math.Atan2(toward.Y - from.Y, toward.X - from.X);
        }

        // ---------------------------------------------------------------- looking things up

        /// <summary>Find what a point would land on: an existing point, a road, or open ground.</summary>
        public Snap SnapTo(Vec p)
        {
            int node = -1;
            double best = SnapNode;
            for (int i = 0; i < Nodes.Count; i++)
            {
                double d = Dist(Nodes[i], p);
                if (d < best)
                {
                    best = d;
                    node = i;
                }
            }
            if (node >= 0) return new Snap { Point = Nodes[node], Node = node, Segment = -1 };

            Vec on;
            double t;
            int seg = NearestSegment(p, SnapRoad, out on, out t);
            if (seg >= 0) return new Snap { Point = on, Node = -1, Segment = seg, T = t };
            return new Snap { Point = p, Node = -1, Segment = -1 };
        }

        /// <summary>The road nearest a point, if within reach, and the closest spot on it.</summary>
        public int NearestSegment(Vec p, double reach, out Vec on)
        {
            double t;
            return NearestSegment(p, reach, out on, out t);
        }

        private int NearestSegment(Vec p, double reach, out Vec on, out double t)
        {
            int seg = -1;
            double best = reach;
            on = p;
            t = 0;
            for (int i = 0; i < Segments.Count; i++)
            {
                double ti;
                Vec q;
                double d = Project(Trace(Segments[i]), p, out ti, out q);
                if (d < best)
                {
                    best = d;
                    seg = i;
                    on = q;
                    t = ti;
                }
            }
            return seg;
        }

        // ---------------------------------------------------------------- drawing

        /// <summary>
        /// Draw a road from p to q, straight, or curved toward a control point. Returns null if
        /// it was added, or a short reason why it cannot be. With commit false nothing is
        /// changed: use that to preview.
        /// </summary>
        public string TryAdd(Vec p, Vec q, int lanes, int mph, bool commit, Vec? control = null)
        {
            EditableMap work = Clone();
            string error = work.Add(p, q, lanes, mph, control);
            if (error != null) return error;
            // Make sure the result can actually be built before accepting it.
            try
            {
                work.ToWorld();
            }
            catch (Exception)
            {
                return "That puts two intersections too close together.";
            }
            if (commit)
            {
                Nodes = work.Nodes;
                Segments = work.Segments;
            }
            return null;
        }

        private struct Stop
        {
            public double T;
            public int Node;
        }

        private struct Crossing
        {
            /// <summary>How far along the new road, and how far along the road it crosses.</summary>
            public double T, U;
            public Vec At;
        }

        private string Add(Vec p, Vec q, int lanes, int mph, Vec? control)
        {
            string error;
            int a = Resolve(p, out error);
            if (a < 0) return error;
            int b = Resolve(q, out error);
            if (b < 0) return error;
            if (a == b) return "Too short.";

            Vec pa = Nodes[a], pb = Nodes[b];
            // A control point that is almost on the straight line makes no real curve.
            bool curved = false;
            Vec c = default(Vec);
            if (control.HasValue)
            {
                double t0;
                Vec on;
                curved = Project(new[] { pa, pb }, control.Value, out t0, out on) > 3;
                c = control.Value;
            }
            Vec[] path = Trace(pa, pb, curved, c);

            // Everything the new road meets on the way becomes a point along it.
            var stops = new List<Stop>();
            for (int i = 0; i < Nodes.Count; i++)
            {
                if (i == a || i == b) continue;
                double t;
                Vec on;
                if (Project(path, Nodes[i], out t, out on) < SnapRoad && t > 0 && t < 1) stops.Add(new Stop { T = t, Node = i });
            }
            int existing = Segments.Count;
            for (int i = 0; i < existing; i++)
            {
                EditSegment s = Segments[i];
                if (stops.Exists(k => k.Node == s.A || k.Node == s.B)) continue;
                List<Crossing> crossings = Crossings(path, Trace(s));
                // Touching at a shared end is not a crossing.
                crossings.RemoveAll(x => ((s.A == a || s.B == a) && Dist(x.At, pa) < SnapNode * 1.5) || ((s.A == b || s.B == b) && Dist(x.At, pb) < SnapNode * 1.5));
                // Cut the far end first, so each cut leaves the near part of the road in place.
                crossings.Sort((m, n) => n.U.CompareTo(m.U));
                double upTo = 1;
                foreach (Crossing x in crossings)
                {
                    double u = x.U / upTo;
                    if (Dist(x.At, Nodes[s.A]) < SnapNode) stops.Add(new Stop { T = x.T, Node = s.A });
                    else if (Dist(x.At, Nodes[s.B]) < SnapNode) stops.Add(new Stop { T = x.T, Node = s.B });
                    else if (LengthBetween(s, 0, u) < MinLength || LengthBetween(s, u, 1) < MinLength) return "That crosses another road too close to an intersection.";
                    else
                    {
                        stops.Add(new Stop { T = x.T, Node = Split(i, u) });
                        upTo = x.U;
                    }
                }
            }
            stops.Sort((m, n) => m.T.CompareTo(n.T));

            var chain = new List<Stop> { new Stop { T = 0, Node = a } };
            foreach (Stop stop in stops)
            {
                if (stop.Node == a || stop.Node == b || chain.Exists(k => k.Node == stop.Node)) continue;
                chain.Add(stop);
            }
            chain.Add(new Stop { T = 1, Node = b });

            var touched = new HashSet<int>();
            for (int i = 0; i + 1 < chain.Count; i++)
            {
                int from = chain[i].Node, to = chain[i + 1].Node;
                touched.Add(from);
                touched.Add(to);
                var piece = new EditSegment { A = from, B = to, Lanes = lanes, Mph = mph };
                if (curved)
                {
                    // The part of the curve between two positions is itself a curve of the same kind.
                    double t0 = chain[i].T, t1 = chain[i + 1].T;
                    Vec start = CurvePoint(pa, c, pb, t0);
                    double dx = 2 * ((1 - t0) * (c.X - pa.X) + t0 * (pb.X - c.X));
                    double dy = 2 * ((1 - t0) * (c.Y - pa.Y) + t0 * (pb.Y - c.Y));
                    piece.Curved = true;
                    piece.Control = new Vec(start.X + dx * (t1 - t0) / 2, start.Y + dy * (t1 - t0) / 2);
                }
                Vec[] shape = Trace(Nodes[from], Nodes[to], piece.Curved, piece.Control);
                if (LengthOf(shape) < MinLength) return "Too short: each stretch of road needs to be at least " + Math.Round(MinLength * 3.28084 / 10) * 10 + " ft.";
                if (piece.Curved && TightestRadius(shape) < MinRadius) return "That curve is too tight.";
                foreach (EditSegment s in Segments)
                {
                    bool same = (s.A == from && s.B == to) || (s.A == to && s.B == from);
                    if (same && !s.Curved && !piece.Curved) return "There is already a road there.";
                }
                Segments.Add(piece);
            }

            foreach (int node in touched)
            {
                var headings = new List<double>();
                foreach (EditSegment s in Segments) if (s.A == node || s.B == node) headings.Add(HeadingFrom(s, node));
                if (headings.Count > MaxArms) return "No more than " + MaxArms + " roads can meet at one point.";
                for (int i = 0; i < headings.Count; i++)
                {
                    for (int k = i + 1; k < headings.Count; k++)
                    {
                        double between = Math.Abs(Math.Atan2(Math.Sin(headings[i] - headings[k]), Math.Cos(headings[i] - headings[k])));
                        if (between < MinAngle) return "Those roads meet at too sharp an angle.";
                    }
                }
            }
            return null;
        }

        /// <summary>Turn a dropped point into a point of the map, splitting a road if it lands on one.</summary>
        private int Resolve(Vec p, out string error)
        {
            error = null;
            Snap snap = SnapTo(p);
            if (snap.Node >= 0) return snap.Node;
            if (snap.Segment < 0)
            {
                Nodes.Add(p);
                return Nodes.Count - 1;
            }
            EditSegment s = Segments[snap.Segment];
            if (LengthBetween(s, 0, snap.T) < MinLength || LengthBetween(s, snap.T, 1) < MinLength)
            {
                error = "Too close to the end of that road. Join at the end, or further along.";
                return -1;
            }
            return Split(snap.Segment, snap.T);
        }

        /// <summary>Cut a road in two at a position along it (0 to 1). Returns the new point.</summary>
        private int Split(int segment, double t)
        {
            EditSegment s = Segments[segment];
            Vec at = PointAt(s, t);
            Nodes.Add(at);
            int mid = Nodes.Count - 1;
            var rest = new EditSegment { A = mid, B = s.B, Lanes = s.Lanes, Mph = s.Mph };
            if (s.Curved)
            {
                // Each half of a curve is a curve of the same kind.
                rest.Curved = true;
                rest.Control = Lerp(s.Control, Nodes[s.B], t);
                s.Control = Lerp(Nodes[s.A], s.Control, t);
            }
            Segments.Add(rest);
            s.B = mid;
            return mid;
        }

        /// <summary>Every place two traced lines cross, strictly inside both.</summary>
        private static List<Crossing> Crossings(Vec[] path, Vec[] other)
        {
            var found = new List<Crossing>();
            int n = path.Length - 1, m = other.Length - 1;
            for (int i = 0; i < n; i++)
            {
                for (int k = 0; k < m; k++)
                {
                    Vec p = path[i], p2 = path[i + 1], q = other[k], q2 = other[k + 1];
                    double rx = p2.X - p.X, ry = p2.Y - p.Y;
                    double sx = q2.X - q.X, sy = q2.Y - q.Y;
                    double denom = rx * sy - ry * sx;
                    if (Math.Abs(denom) < 1e-12) continue;
                    double f = ((q.X - p.X) * sy - (q.Y - p.Y) * sx) / denom;
                    double g = ((q.X - p.X) * ry - (q.Y - p.Y) * rx) / denom;
                    if (f < 0 || f >= 1 || g < 0 || g >= 1) continue;
                    double t = (i + f) / n, u = (k + g) / m;
                    if (t <= 1e-6 || t >= 1 - 1e-6 || u <= 1e-6 || u >= 1 - 1e-6) continue;
                    found.Add(new Crossing { T = t, U = u, At = new Vec(p.X + rx * f, p.Y + ry * f) });
                }
            }
            return found;
        }

        // ---------------------------------------------------------------- removing

        /// <summary>Remove the road nearest a point. Returns false if none is within reach.</summary>
        public bool RemoveNear(Vec p)
        {
            Vec on;
            int seg = NearestSegment(p, SnapRoad * 1.5, out on);
            if (seg < 0) return false;
            Segments.RemoveAt(seg);
            DropUnusedNodes();
            return true;
        }

        private void DropUnusedNodes()
        {
            var used = new bool[Nodes.Count];
            foreach (EditSegment s in Segments) used[s.A] = used[s.B] = true;
            var renumber = new int[Nodes.Count];
            var kept = new List<Vec>();
            for (int i = 0; i < Nodes.Count; i++)
            {
                renumber[i] = kept.Count;
                if (used[i]) kept.Add(Nodes[i]);
            }
            Nodes = kept;
            foreach (EditSegment s in Segments)
            {
                s.A = renumber[s.A];
                s.B = renumber[s.B];
            }
        }

        public void Clear()
        {
            Nodes.Clear();
            Segments.Clear();
        }

        // ---------------------------------------------------------------- building

        /// <summary>
        /// Turn the drawing into a network. Where roads meet, the control is chosen from the
        /// roads themselves: two roads are just a bend; three or more quiet streets (25 mph)
        /// get stop signs all round; anything faster gets a traffic light. A road end with
        /// nothing joined to it is the edge of the map, where traffic comes in and leaves.
        /// </summary>
        public World ToWorld()
        {
            var b = new NetworkBuilder();
            for (int i = 0; i < Nodes.Count; i++)
            {
                int arms = 0, fastest = 0;
                foreach (EditSegment s in Segments)
                {
                    if (s.A != i && s.B != i) continue;
                    arms++;
                    fastest = Math.Max(fastest, s.Mph);
                }
                JunctionMode mode = arms <= 2 ? JunctionMode.Free : fastest <= 25 ? JunctionMode.AllWay : JunctionMode.Signal;
                b.AddNode(Nodes[i].X, Nodes[i].Y, mode);
            }
            foreach (EditSegment s in Segments)
            {
                TrackSpec[] one = { new TrackSpec(0) };
                TrackSpec[] two = { new TrackSpec(0), new TrackSpec(1) };
                TrackSpec[] fwd = s.Lanes == 4 ? two : one;
                TrackSpec[] back = s.Lanes == 1 ? null : fwd;
                double speed = s.Mph * 0.44704;
                if (!s.Curved)
                {
                    b.AddRoad(s.A, s.B, fwd, back, null, speed);
                    continue;
                }
                // Trace the curve with a point every few meters.
                int steps = Math.Max(8, (int)Math.Ceiling(LengthOf(Trace(s)) / 3));
                b.AddCurvedRoad(s.A, s.B, fwd, back, Trace(Nodes[s.A], Nodes[s.B], true, s.Control, steps), speed);
            }
            World world = b.Build();
            // Keep the surroundings the same size however much has been drawn.
            world.Extent = Math.Max(world.Extent, 400);
            return world;
        }

        // ---------------------------------------------------------------- saving

        private static string Num(double v)
        {
            return v.ToString("F1", CultureInfo.InvariantCulture);
        }

        /// <summary>The whole map as one line of text.</summary>
        public string Serialize()
        {
            var sb = new StringBuilder();
            foreach (Vec n in Nodes) sb.Append(Num(n.X)).Append(',').Append(Num(n.Y)).Append(';');
            sb.Append('|');
            foreach (EditSegment s in Segments)
            {
                sb.Append(s.A).Append(',').Append(s.B).Append(',').Append(s.Lanes).Append(',').Append(s.Mph);
                if (s.Curved) sb.Append(',').Append(Num(s.Control.X)).Append(',').Append(Num(s.Control.Y));
                sb.Append(';');
            }
            return sb.ToString();
        }

        /// <summary>Read a map written by Serialize. Text that does not parse gives an empty map.</summary>
        public static EditableMap Parse(string text)
        {
            var map = new EditableMap();
            if (string.IsNullOrEmpty(text)) return map;
            try
            {
                string[] halves = text.Split('|');
                foreach (string item in halves[0].Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    string[] f = item.Split(',');
                    map.Nodes.Add(new Vec(double.Parse(f[0], CultureInfo.InvariantCulture), double.Parse(f[1], CultureInfo.InvariantCulture)));
                }
                foreach (string item in halves[1].Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    string[] f = item.Split(',');
                    var s = new EditSegment { A = int.Parse(f[0]), B = int.Parse(f[1]), Lanes = int.Parse(f[2]), Mph = int.Parse(f[3]) };
                    if (s.A < 0 || s.B < 0 || s.A >= map.Nodes.Count || s.B >= map.Nodes.Count) return new EditableMap();
                    if (f.Length >= 6)
                    {
                        s.Curved = true;
                        s.Control = new Vec(double.Parse(f[4], CultureInfo.InvariantCulture), double.Parse(f[5], CultureInfo.InvariantCulture));
                    }
                    map.Segments.Add(s);
                }
                map.ToWorld();
                return map;
            }
            catch (Exception)
            {
                return new EditableMap();
            }
        }
    }
}
