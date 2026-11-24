using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Traffic.Sim
{
    /// <summary>
    /// A kind of road the player can draw: how many lanes run each way, whether a median
    /// divides them, its speed limit, how tight it may curve and what a meter of it costs.
    /// </summary>
    public sealed class RoadType
    {
        public const int Street = 0, Avenue = 1, Highway = 2;
        public static readonly string[] GroupNames = { "Street", "Avenue", "Highway" };

        /// <summary>A short code, used when a drawing is saved.</summary>
        public string Id;
        /// <summary>Street, Avenue or Highway.</summary>
        public int Group;
        /// <summary>Lanes running the way the road is drawn, and lanes running back. None back makes it one-way.</summary>
        public int Forward, Backward;
        /// <summary>Width of the strip of ground between the two directions, meters. 0 for none.</summary>
        public double Median;
        /// <summary>Speed limit in miles per hour.</summary>
        public int Mph;
        /// <summary>The tightest curve it may be drawn with, meters.</summary>
        public double MinRadius = 22;
        /// <summary>Dollars for each meter built.</summary>
        public double CostPerMeter;
        /// <summary>A one-way road sits centered on the line it is drawn along. (A road from an older drawing lies to the right of it.)</summary>
        public bool Centered = true;

        public bool OneWay => Backward == 0;
        public int Lanes => Forward + Backward;
        /// <summary>From one edge of the pavement to the other, meters.</summary>
        public double Width => Lanes * NetworkBuilder.LaneW + (OneWay ? 0 : Median);
        /// <summary>How far every lane is moved sideways from the line drawn: see NetworkBuilder.AddRoad.</summary>
        public double Inner => OneWay ? (Centered ? -Forward * NetworkBuilder.LaneW / 2 : 0) : Median / 2;

        public string Name => GroupNames[Group] + ", " + (OneWay
            ? (Forward == 1 ? "1 lane" : Forward + " lanes") + " one-way"
            : (Forward == 1 ? "1 lane" : Forward + " lanes") + " each way");

        /// <summary>The roads on offer, in the order the toolbar shows them.</summary>
        public static readonly RoadType[] All =
        {
            new RoadType { Id = "s2", Group = Street, Forward = 1, Backward = 1, Mph = 25, MinRadius = 22, CostPerMeter = 2 },
            new RoadType { Id = "s1", Group = Street, Forward = 1, Mph = 25, MinRadius = 22, CostPerMeter = 1 },
            new RoadType { Id = "a2", Group = Avenue, Forward = 1, Backward = 1, Median = 3, Mph = 40, MinRadius = 30, CostPerMeter = 3 },
            new RoadType { Id = "a4", Group = Avenue, Forward = 2, Backward = 2, Median = 3, Mph = 40, MinRadius = 30, CostPerMeter = 6 },
            new RoadType { Id = "a1", Group = Avenue, Forward = 1, Mph = 40, MinRadius = 30, CostPerMeter = 2 },
            new RoadType { Id = "a1x2", Group = Avenue, Forward = 2, Mph = 40, MinRadius = 30, CostPerMeter = 4 },
            new RoadType { Id = "h1", Group = Highway, Forward = 1, Mph = 65, MinRadius = 60, CostPerMeter = 3 },
            new RoadType { Id = "h2", Group = Highway, Forward = 2, Mph = 65, MinRadius = 60, CostPerMeter = 6 },
            new RoadType { Id = "h3", Group = Highway, Forward = 3, Mph = 65, MinRadius = 60, CostPerMeter = 9 },
            new RoadType { Id = "h4", Group = Highway, Forward = 4, Mph = 65, MinRadius = 60, CostPerMeter = 12 },
        };

        public static RoadType ById(string id)
        {
            foreach (RoadType t in All) if (t.Id == id) return t;
            return null;
        }

        /// <summary>
        /// A road given only by its lane count (1 one-way, 2, or 4) and speed limit, the way
        /// roads were chosen before there were types. Drawings saved then are made of these.
        /// </summary>
        public static RoadType Plain(int lanes, int mph)
        {
            return new RoadType
            {
                Id = lanes + "@" + mph, Group = mph <= 25 ? Street : mph <= 45 ? Avenue : Highway,
                Forward = lanes == 4 ? 2 : 1, Backward = lanes == 1 ? 0 : lanes == 4 ? 2 : 1,
                Mph = mph, MinRadius = 22, CostPerMeter = lanes, Centered = false,
            };
        }
    }

    /// <summary>One drawn stretch of road between two points, straight or curved.</summary>
    public sealed class EditSegment
    {
        public int A, B;
        public RoadType Type;
        /// <summary>1 is a single one-way lane from A to B; 2 is one lane each way; 4 is two lanes each way.</summary>
        public int Lanes => Type.Lanes;
        /// <summary>Speed limit in miles per hour.</summary>
        public int Mph => Type.Mph;
        /// <summary>A curved stretch is part of a circle, from A to B, passing through Mid halfway along.</summary>
        public bool Curved;
        public Vec Mid;
        /// <summary>Part of a roundabout. Where a road meets one there are no lights or signs: traffic going round has the right of way.</summary>
        public bool Ring;
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
        /// <summary>The furthest round one curve may turn: two thirds of a circle.</summary>
        public const double MaxTurn = 240 * Math.PI / 180;
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
            foreach (EditSegment s in Segments) copy.Segments.Add(new EditSegment { A = s.A, B = s.B, Type = s.Type, Curved = s.Curved, Mid = s.Mid, Ring = s.Ring });
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

        /// <summary>
        /// The circle through three points, as the arc that runs from a through via to b: its
        /// center and radius, the angle it starts at and how far round it turns (positive is
        /// counter-clockwise). False if the points are in a line, which has no circle.
        /// </summary>
        private static bool Arc(Vec a, Vec via, Vec b, out Vec center, out double radius, out double start, out double sweep)
        {
            center = default(Vec);
            radius = start = sweep = 0;
            double d = 2 * (a.X * (via.Y - b.Y) + via.X * (b.Y - a.Y) + b.X * (a.Y - via.Y));
            double chord = Dist(a, b);
            if (Math.Abs(d) < 1e-9 * Math.Max(1, chord * chord)) return false;
            double a2 = a.X * a.X + a.Y * a.Y, v2 = via.X * via.X + via.Y * via.Y, b2 = b.X * b.X + b.Y * b.Y;
            center = new Vec((a2 * (via.Y - b.Y) + v2 * (b.Y - a.Y) + b2 * (a.Y - via.Y)) / d, (a2 * (b.X - via.X) + v2 * (a.X - b.X) + b2 * (via.X - a.X)) / d);
            radius = Dist(center, a);
            if (radius > 1e6) return false;
            start = Math.Atan2(a.Y - center.Y, a.X - center.X);
            sweep = Math.Atan2(b.Y - center.Y, b.X - center.X) - start;
            // Going a, via, b is counter-clockwise when b lies to the left of the line from a to via.
            bool left = (via.X - a.X) * (b.Y - a.Y) - (via.Y - a.Y) * (b.X - a.X) > 0;
            if (left) while (sweep <= 0) sweep += 2 * Math.PI;
            else while (sweep >= 0) sweep -= 2 * Math.PI;
            return true;
        }

        /// <summary>A point on the round curve from p0 to p1 that passes through via: t runs 0 to 1.</summary>
        public static Vec CurvePoint(Vec p0, Vec via, Vec p1, double t)
        {
            Vec c;
            double r, start, sweep;
            if (!Arc(p0, via, p1, out c, out r, out start, out sweep)) return Lerp(p0, p1, t);
            return new Vec(c.X + Math.Cos(start + sweep * t) * r, c.Y + Math.Sin(start + sweep * t) * r);
        }

        /// <summary>How far round a curve through three points turns, in radians. A straight line turns 0.</summary>
        public static double CurveTurn(Vec p0, Vec via, Vec p1)
        {
            Vec c;
            double r, start, sweep;
            return Arc(p0, via, p1, out c, out r, out start, out sweep) ? Math.Abs(sweep) : 0;
        }

        /// <summary>
        /// The line a stretch of road follows, as points from one end to the other. A curved
        /// stretch is the part of a circle from p0 to p1 that passes through via.
        /// </summary>
        public static Vec[] Trace(Vec p0, Vec p1, bool curved, Vec via, int steps = CurveSteps)
        {
            Vec c;
            double r, start, sweep;
            if (!curved || !Arc(p0, via, p1, out c, out r, out start, out sweep)) return new[] { p0, p1 };
            var pts = new Vec[steps + 1];
            for (int i = 0; i <= steps; i++) pts[i] = new Vec(c.X + Math.Cos(start + sweep * i / steps) * r, c.Y + Math.Sin(start + sweep * i / steps) * r);
            // The ends are exactly the points given, whatever the rounding.
            pts[0] = p0;
            pts[steps] = p1;
            return pts;
        }

        public Vec[] Trace(EditSegment s)
        {
            return Trace(Nodes[s.A], Nodes[s.B], s.Curved, s.Mid);
        }

        private Vec PointAt(EditSegment s, double t)
        {
            return s.Curved ? CurvePoint(Nodes[s.A], s.Mid, Nodes[s.B], t) : Lerp(Nodes[s.A], Nodes[s.B], t);
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
            // A curve sets off along the circle, not toward its far end.
            Vec toward = s.Curved ? PointAt(s, s.A == node ? 0.02 : 0.98) : Nodes[s.A == node ? s.B : s.A];
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
        /// Draw a road from p to q: straight, or, given a third point, a round curve that passes
        /// through it. Returns null if it was added, or a short reason why it cannot be. With
        /// commit false nothing is changed: use that to preview.
        /// </summary>
        public string TryAdd(Vec p, Vec q, RoadType type, bool commit, Vec? through = null)
        {
            EditableMap work = Clone();
            string error = work.Add(p, q, type, through);
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

        /// <summary>The same, for a road given by its lane count (1 one-way, 2, or 4) and speed limit.</summary>
        public string TryAdd(Vec p, Vec q, int lanes, int mph, bool commit, Vec? through = null)
        {
            return TryAdd(p, q, RoadType.Plain(lanes, mph), commit, through);
        }

        /// <summary>What a road from p to q would cost, and how long it would be, before any snapping.</summary>
        public static double CostOf(Vec p, Vec q, RoadType type, Vec? through, out double length)
        {
            length = LengthOf(Trace(p, q, through.HasValue, through ?? default(Vec)));
            return length * type.CostPerMeter;
        }

        /// <summary>What everything drawn so far costs.</summary>
        public double Cost()
        {
            double total = 0;
            foreach (EditSegment s in Segments) total += LengthOf(Trace(s)) * s.Type.CostPerMeter;
            return total;
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

        private string Add(Vec p, Vec q, RoadType type, Vec? through)
        {
            string error;
            int a = Resolve(p, out error);
            if (a < 0) return error;
            int b = Resolve(q, out error);
            if (b < 0) return error;
            if (a == b) return "Too short.";

            Vec pa = Nodes[a], pb = Nodes[b];
            // A point that is almost on the straight line makes no real curve.
            bool curved = false;
            Vec c = default(Vec);
            if (through.HasValue)
            {
                double t0;
                Vec on;
                curved = Project(new[] { pa, pb }, through.Value, out t0, out on) > 3;
                c = through.Value;
                if (curved && CurveTurn(pa, c, pb) > MaxTurn) return "That curve goes too far round. Draw it in two parts.";
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
                    else if (LengthBetween(s, 0, u) < Shortest(s) || LengthBetween(s, u, 1) < Shortest(s)) return "That crosses another road too close to an intersection.";
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
                var piece = new EditSegment { A = from, B = to, Type = type };
                if (curved)
                {
                    // The part of the curve between two positions is a piece of the same circle.
                    piece.Curved = true;
                    piece.Mid = CurvePoint(pa, c, pb, (chain[i].T + chain[i + 1].T) / 2);
                }
                Vec[] shape = Trace(Nodes[from], Nodes[to], piece.Curved, piece.Mid);
                if (LengthOf(shape) < MinLength) return "Too short: each stretch of road needs to be at least " + Math.Round(MinLength * 3.28084 / 10) * 10 + " ft.";
                if (piece.Curved && TightestRadius(shape) < type.MinRadius) return "That curve is too tight for this kind of road.";
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
                // A one-way road may run in alongside another road, or peel off it: that is a merge, not an intersection.
                if (JoinAt(node) != null) continue;
                for (int i = 0; i < headings.Count; i++)
                {
                    for (int k = i + 1; k < headings.Count; k++)
                    {
                        double between = Math.Abs(Math.Atan2(Math.Sin(headings[i] - headings[k]), Math.Cos(headings[i] - headings[k])));
                        if (between < MinAngle) return "Too sharp an angle. Only a one-way road can join that closely, on the right of the traffic it joins.";
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
            if (LengthBetween(s, 0, snap.T) < Shortest(s) || LengthBetween(s, snap.T, 1) < Shortest(s))
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
            var rest = new EditSegment { A = mid, B = s.B, Type = s.Type, Ring = s.Ring };
            if (s.Curved)
            {
                // Each part of a curve is a piece of the same circle.
                rest.Curved = true;
                rest.Mid = PointAt(s, (1 + t) / 2);
                s.Mid = PointAt(s, t / 2);
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

        // ---------------------------------------------------------------- roundabouts

        /// <summary>The smallest and largest a roundabout may be, as the radius of the middle of its ring.</summary>
        public const double MinRing = 16, MaxRing = 150;
        /// <summary>The shortest stretch of ring allowed between two roads that join it.</summary>
        public const double RingPiece = 24;

        /// <summary>The shortest piece a road may be cut into: shorter on a roundabout, whose junctions are small.</summary>
        private static double Shortest(EditSegment s)
        {
            return s.Ring ? RingPiece : MinLength;
        }

        /// <summary>The road a roundabout's ring is made of: one lane, one-way, of the same class as the road chosen.</summary>
        public static RoadType RingType(RoadType chosen)
        {
            if (chosen.OneWay && chosen.Forward == 1 && chosen.Group != RoadType.Highway && RoadType.ById(chosen.Id) != null) return chosen;
            return RoadType.ById(chosen.Group == RoadType.Street ? "s1" : "a1");
        }

        /// <summary>
        /// Build a roundabout: a one-way ring that traffic goes round counter-clockwise. Roads
        /// that cross the ring are cut where they meet it and join it there; whatever lies
        /// inside is removed. Returns null if it was built, or a short reason why it cannot be.
        /// </summary>
        public string TryAddRoundabout(Vec center, double radius, RoadType chosen, bool commit)
        {
            if (radius < MinRing) return "Too small for a roundabout.";
            if (radius > MaxRing) return "Too big for a roundabout.";
            EditableMap work = Clone();
            string error = work.AddRing(center, radius, RingType(chosen));
            if (error != null) return error;
            try
            {
                work.ToWorld();
            }
            catch (Exception)
            {
                return "Roads join the roundabout too close together.";
            }
            if (commit)
            {
                Nodes = work.Nodes;
                Segments = work.Segments;
            }
            return null;
        }

        private string AddRing(Vec c, double r, RoadType type)
        {
            foreach (EditSegment s in Segments) if (s.Ring && Dist(PointAt(s, 0.5), c) < r + 40) return "Too close to another roundabout.";

            // Cut every road where it crosses the ring.
            var onRing = new List<int>();
            int existing = Segments.Count;
            for (int i = 0; i < existing; i++)
            {
                List<Crossing> crossings = CircleCrossings(Trace(Segments[i]), c, r);
                crossings.Sort((m, n) => n.T.CompareTo(m.T));
                double upTo = 1;
                foreach (Crossing x in crossings)
                {
                    EditSegment s = Segments[i];
                    if (Dist(x.At, Nodes[s.A]) < 4 || Dist(x.At, Nodes[s.B]) < 4) return "A road ends right on the ring. Move the roundabout a little.";
                    // The road should come in across the ring, not along it.
                    Vec before = PointAt(s, Math.Max(0, x.T / upTo - 0.02)), after = PointAt(s, Math.Min(1, x.T / upTo + 0.02));
                    double along = Math.Atan2(after.Y - before.Y, after.X - before.X), outward = Math.Atan2(x.At.Y - c.Y, x.At.X - c.X);
                    if (Math.Abs(Math.Cos(along - outward)) < Math.Cos(Math.PI / 2 - MinAngle)) return "A road meets the ring at too shallow an angle.";
                    int node = Split(i, x.T / upTo);
                    double d = Dist(Nodes[node], c);
                    Nodes[node] = new Vec(c.X + (Nodes[node].X - c.X) / d * r, c.Y + (Nodes[node].Y - c.Y) / d * r);
                    onRing.Add(node);
                    upTo = x.T;
                }
            }
            // Take away what is inside, and check that what is left outside is long enough to be a road.
            Segments.RemoveAll(s => Dist(PointAt(s, 0.5), c) < r);
            foreach (EditSegment s in Segments)
            {
                if ((onRing.Contains(s.A) || onRing.Contains(s.B)) && LengthOf(Trace(s)) < MinLength) return "That would leave a road too short beside the roundabout.";
            }

            // The ring needs at least three points round it, and no stretch of more than half a turn.
            var angles = new List<KeyValuePair<double, int>>();
            foreach (int node in onRing) angles.Add(new KeyValuePair<double, int>(Math.Atan2(Nodes[node].Y - c.Y, Nodes[node].X - c.X), node));
            if (angles.Count == 0)
            {
                Nodes.Add(new Vec(c.X + r, c.Y));
                angles.Add(new KeyValuePair<double, int>(0, Nodes.Count - 1));
            }
            while (true)
            {
                angles.Sort((m, n) => m.Key.CompareTo(n.Key));
                int widest = 0;
                double gap = 0;
                for (int i = 0; i < angles.Count; i++)
                {
                    double g = angles[(i + 1) % angles.Count].Key - angles[i].Key;
                    if (g <= 0) g += 2 * Math.PI;
                    if (g > gap)
                    {
                        gap = g;
                        widest = i;
                    }
                }
                if (angles.Count >= 3 && gap <= Math.PI + 0.01) break;
                double at = angles[widest].Key + gap / 2;
                if (at > Math.PI) at -= 2 * Math.PI;
                Nodes.Add(new Vec(c.X + Math.Cos(at) * r, c.Y + Math.Sin(at) * r));
                angles.Add(new KeyValuePair<double, int>(at, Nodes.Count - 1));
            }
            for (int i = 0; i < angles.Count; i++)
            {
                double from = angles[i].Key, to = angles[(i + 1) % angles.Count].Key;
                if (to <= from) to += 2 * Math.PI;
                if ((to - from) * r < RingPiece) return "Roads join the roundabout too close together.";
                double mid = (from + to) / 2;
                Segments.Add(new EditSegment
                {
                    A = angles[i].Value, B = angles[(i + 1) % angles.Count].Value, Type = type, Curved = true, Ring = true,
                    Mid = new Vec(c.X + Math.Cos(mid) * r, c.Y + Math.Sin(mid) * r),
                });
            }
            DropUnusedNodes();
            return null;
        }

        /// <summary>Every place a traced line crosses a circle.</summary>
        private static List<Crossing> CircleCrossings(Vec[] path, Vec c, double r)
        {
            var found = new List<Crossing>();
            int n = path.Length - 1;
            for (int i = 0; i < n; i++)
            {
                // Where along this piece is it exactly r from the center? Solve |p + d f - c| = r for f.
                Vec p = path[i];
                double dx = path[i + 1].X - p.X, dy = path[i + 1].Y - p.Y, px = p.X - c.X, py = p.Y - c.Y;
                double qa = dx * dx + dy * dy, qb = 2 * (px * dx + py * dy), qc = px * px + py * py - r * r;
                double disc = qb * qb - 4 * qa * qc;
                if (qa < 1e-12 || disc <= 0) continue;
                foreach (double sign in new[] { -1.0, 1.0 })
                {
                    double f = (-qb + sign * Math.Sqrt(disc)) / (2 * qa);
                    if (f < 0 || f >= 1) continue;
                    double t = (i + f) / n;
                    if (t <= 1e-6 || t >= 1 - 1e-6) continue;
                    found.Add(new Crossing { T = t, At = new Vec(p.X + dx * f, p.Y + dy * f) });
                }
            }
            return found;
        }

        // ---------------------------------------------------------------- merging

        /// <summary>
        /// A place where a one-way road runs into another road alongside it (a merge), or peels
        /// off it (an exit), with no intersection: the road carries straight on through.
        /// </summary>
        private sealed class Join
        {
            /// <summary>The one-way road joining or leaving.</summary>
            public EditSegment Ramp;
            /// <summary>The road it joins, before and after the join in the direction of the traffic concerned.</summary>
            public EditSegment Up, Down;
            /// <summary>True where the ramp's traffic flows in; false where it flows out.</summary>
            public bool Merge;
            /// <summary>The way that traffic is heading on the road at the join.</summary>
            public Vec Along;
            /// <summary>How many lanes the road has in that direction, and how far their outside edge is from its centerline.</summary>
            public int Lanes;
            public double Edge;
        }

        /// <summary>Roads may join as a merge when they meet at less than this angle; steeper and they make an intersection.</summary>
        public static double MergeAngle => MinAngle;
        /// <summary>The slight angle a ramp keeps to the road where it meets it.</summary>
        private const double RampTilt = 0.14;

        /// <summary>The merge or exit at a point, if the roads there make one.</summary>
        private Join JoinAt(int node)
        {
            var here = new List<EditSegment>();
            foreach (EditSegment s in Segments) if (s.A == node || s.B == node) here.Add(s);
            if (here.Count != 3) return null;
            // On a roundabout every way in is a give-way junction, never a merge.
            foreach (EditSegment s in here) if (s.Ring) return null;
            for (int i = 0; i < 3; i++)
            {
                EditSegment ramp = here[i], x = here[(i + 1) % 3], y = here[(i + 2) % 3];
                if (!ramp.Type.OneWay || ramp.A == ramp.B || x.Type.Id != y.Type.Id) continue;
                // The other two must be one road carrying straight on.
                double hx = HeadingFrom(x, node), hy = HeadingFrom(y, node);
                if (Math.Abs(Math.Atan2(Math.Sin(hx - hy), Math.Cos(hx - hy))) < Math.PI - 0.26) continue;
                double hr = HeadingFrom(ramp, node);
                bool merge = ramp.B == node;
                // The way the ramp's traffic is moving at the join.
                double ux = Math.Cos(hr) * (merge ? -1 : 1), uy = Math.Sin(hr) * (merge ? -1 : 1);
                bool alongX = ux * Math.Cos(hx) + uy * Math.Sin(hx) > 0;
                EditSegment down = alongX ? x : y, up = alongX ? y : x;
                double h = alongX ? hx : hy;
                var along = new Vec(Math.Cos(h), Math.Sin(h));
                if (Math.Acos(Geo.Clamp(ux * along.X + uy * along.Y, -1, 1)) >= MergeAngle) continue;
                // The ramp must lie to the right of the traffic it joins or leaves.
                if (along.X * Math.Sin(hr) - along.Y * Math.Cos(hr) >= 0) continue;
                // A one-way road only runs one way: it must be running the way the ramp needs.
                if (down.Type.OneWay && (up.B != node || down.A != node)) continue;
                int lanes = down.Type.OneWay || down.A == node ? down.Type.Forward : down.Type.Backward;
                return new Join { Ramp = ramp, Up = up, Down = down, Merge = merge, Along = along, Lanes = lanes, Edge = lanes * NetworkBuilder.LaneW + down.Type.Inner };
            }
            return null;
        }

        /// <summary>
        /// Reshape the end of a ramp so that it comes in beside the lanes of the road it joins,
        /// not at the middle of it. The trace is given running toward the join. Returns the
        /// trace to build (stopping just short of the join) and the sideways shift to build it with.
        /// </summary>
        private static Vec[] BesideTheRoad(Vec[] trace, Vec node, Join join, double rampWidth, out double shift)
        {
            double tilt = RampTilt, r = NetworkBuilder.FreeRadius;
            double side = join.Edge + rampWidth / 2;
            shift = (side - r * Math.Sin(tilt)) / Math.Cos(tilt);
            // Along the road and to its right, as seen by the traffic the ramp joins.
            Vec h = join.Along, right = new Vec(h.Y, -h.X);
            // A merge arrives a little before the join, heading slightly in; an exit is the same thing seen backward.
            double back = join.Merge ? 1 : -1;
            double along = (-r * Math.Cos(tilt) + shift * Math.Sin(tilt)) * back;
            var end = new Vec(node.X + h.X * along + right.X * side, node.Y + h.Y * along + right.Y * side);
            double heading = Math.Atan2(h.Y, h.X) + tilt * back + (join.Merge ? 0 : Math.PI);

            // Keep the ramp as drawn up to a point some way back, then sweep in to the new end.
            double total = LengthOf(trace), from = total - Math.Min(70, total * 0.55), run = 0;
            var path = new List<Vec>();
            int cut = 0;
            for (int i = 0; i + 1 < trace.Length && run + Dist(trace[i], trace[i + 1]) < from; i++)
            {
                run += Dist(trace[i], trace[i + 1]);
                path.Add(trace[i]);
                cut = i + 1;
            }
            Vec before = trace[Math.Max(cut - 1, 0)], at = trace[cut];
            if (cut == 0) before = new Vec(at.X - (trace[1].X - at.X), at.Y - (trace[1].Y - at.Y));
            var start = new PathPose { X = at.X, Y = at.Y, H = Math.Atan2(at.Y - before.Y, at.X - before.X) };
            var finish = new PathPose { X = end.X, Y = end.Y, H = heading };
            Vec[] sweep = Geo.Bezier(start, finish, Dist(at, end) * 0.4, 24);
            // Stop a little short: the builder adds the last point itself, from the shift.
            foreach (Vec p in sweep) if (Dist(p, end) > 2) path.Add(p);
            return path.ToArray();
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
            var joins = new Join[Nodes.Count];
            for (int i = 0; i < Nodes.Count; i++)
            {
                int arms = 0, fastest = 0;
                foreach (EditSegment s in Segments)
                {
                    if (s.A != i && s.B != i) continue;
                    arms++;
                    fastest = Math.Max(fastest, s.Mph);
                }
                joins[i] = JoinAt(i);
                // On a roundabout nobody stops for a light or a sign: whoever is going round goes first.
                bool onRing = false;
                foreach (EditSegment s in Segments) if (s.Ring && (s.A == i || s.B == i)) onRing = true;
                JunctionMode mode = arms <= 2 || joins[i] != null || onRing ? JunctionMode.Free : fastest <= 25 ? JunctionMode.AllWay : JunctionMode.Signal;
                b.AddNode(Nodes[i].X, Nodes[i].Y, mode);
            }
            foreach (EditSegment s in Segments)
            {
                TrackSpec[] fwd = Tracks(s, true, joins), back = s.Type.OneWay ? null : Tracks(s, false, joins);
                double speed = s.Mph * 0.44704;
                bool rampAtA = joins[s.A] != null && joins[s.A].Ramp == s, rampAtB = joins[s.B] != null && joins[s.B].Ramp == s;
                if (rampAtA || rampAtB)
                {
                    // A ramp: bring each joining end in beside the lanes of the road it joins.
                    Vec[] trace = Trace(Nodes[s.A], Nodes[s.B], s.Curved, s.Mid, Math.Max(8, (int)Math.Ceiling(LengthOf(Trace(s)) / 3)));
                    double shiftA = 0, shiftB = 0;
                    if (rampAtB) trace = BesideTheRoad(trace, Nodes[s.B], joins[s.B], s.Type.Width, out shiftB);
                    if (rampAtA)
                    {
                        Array.Reverse(trace);
                        trace = BesideTheRoad(trace, Nodes[s.A], joins[s.A], s.Type.Width, out shiftA);
                        Array.Reverse(trace);
                    }
                    b.AddRamp(s.A, s.B, fwd, trace, speed, s.Type.Inner, rampAtA, shiftA, rampAtB, shiftB);
                    continue;
                }
                if (!s.Curved)
                {
                    b.AddRoad(s.A, s.B, fwd, back, null, speed, 0, 0, false, s.Type.Inner);
                    continue;
                }
                // Trace the curve with a point every few meters.
                int steps = Math.Max(8, (int)Math.Ceiling(LengthOf(Trace(s)) / 3));
                b.AddCurvedRoad(s.A, s.B, fwd, back, Trace(Nodes[s.A], Nodes[s.B], true, s.Mid, steps), speed, s.Type.Inner);
            }
            World world = b.Build();
            // Keep the surroundings the same size however much has been drawn.
            world.Extent = Math.Max(world.Extent, 400);
            return world;
        }

        /// <summary>
        /// The lanes of a stretch of road in one direction. Where it ends at a merge or an exit
        /// its lanes are told which way they may go, and where it leaves a merge it gains a lane
        /// for each lane of the ramp, which ends further on so that traffic has room to move over.
        /// </summary>
        private TrackSpec[] Tracks(EditSegment s, bool forward, Join[] joins)
        {
            int count = forward ? s.Type.Forward : s.Type.Backward;
            Join ahead = joins[forward ? s.B : s.A], behind = joins[forward ? s.A : s.B];
            var tracks = new List<TrackSpec>();
            for (int i = 0; i < count; i++)
            {
                if (ahead == null) tracks.Add(new TrackSpec(i));
                // Traffic arriving at an exit may take it from the outside lane; nobody else may turn there.
                else if (!ahead.Merge && s == ahead.Up && i == count - 1) tracks.Add(new TrackSpec(i, 0, 1, Turn.Straight, Turn.Right));
                else tracks.Add(new TrackSpec(i, 0, 1, Turn.Straight));
            }
            if (behind != null && behind.Merge && s == behind.Down)
            {
                double length = LengthOf(Trace(s));
                double until = Math.Min(0.75, 150 / Math.Max(length, 1));
                for (int i = 0; i < behind.Ramp.Type.Forward; i++) tracks.Add(new TrackSpec(count + i, 0, until));
            }
            return tracks.ToArray();
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
                // A road of one of the listed types is saved by its code; an older, plain one by lanes and speed.
                sb.Append(s.A).Append(',').Append(s.B).Append(',');
                if (RoadType.ById(s.Type.Id) != null) sb.Append(s.Type.Id);
                else sb.Append(s.Lanes).Append(',').Append(s.Mph);
                if (s.Curved) sb.Append(',').Append(Num(s.Mid.X)).Append(',').Append(Num(s.Mid.Y)).Append(s.Ring ? ",ring" : ",arc");
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
                    var s = new EditSegment { A = int.Parse(f[0]), B = int.Parse(f[1]) };
                    if (s.A < 0 || s.B < 0 || s.A >= map.Nodes.Count || s.B >= map.Nodes.Count) return new EditableMap();
                    // The kind of road is either a type's code, or (in older drawings) a lane count and a speed limit.
                    int lanes, rest;
                    if (int.TryParse(f[2], out lanes))
                    {
                        s.Type = RoadType.Plain(lanes, int.Parse(f[3]));
                        rest = 4;
                    }
                    else
                    {
                        s.Type = RoadType.ById(f[2]);
                        if (s.Type == null) return new EditableMap();
                        rest = 3;
                    }
                    if (f.Length >= rest + 2)
                    {
                        s.Curved = true;
                        s.Mid = new Vec(double.Parse(f[rest], CultureInfo.InvariantCulture), double.Parse(f[rest + 1], CultureInfo.InvariantCulture));
                        // Curves saved before they became round arcs bent toward a corner point;
                        // the middle of that older curve is close to where the arc should pass.
                        s.Ring = f.Length > rest + 2 && f[rest + 2] == "ring";
                        if (f.Length == rest + 2) s.Mid = new Vec((map.Nodes[s.A].X + 2 * s.Mid.X + map.Nodes[s.B].X) / 4, (map.Nodes[s.A].Y + 2 * s.Mid.Y + map.Nodes[s.B].Y) / 4);
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
