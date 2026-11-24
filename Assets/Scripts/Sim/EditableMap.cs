using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Traffic.Sim
{
    /// <summary>One drawn stretch of road between two points.</summary>
    public sealed class EditSegment
    {
        public int A, B;
        /// <summary>1 is a single one-way lane from A to B; 2 is one lane each way; 4 is two lanes each way.</summary>
        public int Lanes;
        /// <summary>Speed limit in miles per hour.</summary>
        public int Mph;
    }

    /// <summary>Where a point lands when it is dropped on the map.</summary>
    public struct Snap
    {
        public Vec Point;
        /// <summary>Index of the road end or intersection it snapped to, or -1.</summary>
        public int Node;
        /// <summary>Index of the road it landed on, or -1.</summary>
        public int Segment;
    }

    /// <summary>
    /// A road map the player draws: points joined by straight stretches of road at any
    /// angle. Drawing across an existing road makes an intersection, and drawing to or
    /// from the middle of one splits it. ToWorld turns the drawing into a network that
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
        public const double MinAngle = 28 * Math.PI / 180;
        public const int MaxArms = 4;
        public static readonly int[] LaneChoices = { 1, 2, 4 };
        public static readonly int[] SpeedChoices = { 25, 40, 55, 65 };

        public List<Vec> Nodes = new List<Vec>();
        public List<EditSegment> Segments = new List<EditSegment>();

        public EditableMap Clone()
        {
            var copy = new EditableMap();
            copy.Nodes.AddRange(Nodes);
            foreach (EditSegment s in Segments) copy.Segments.Add(new EditSegment { A = s.A, B = s.B, Lanes = s.Lanes, Mph = s.Mph });
            return copy;
        }

        // ---------------------------------------------------------------- looking things up

        /// <summary>Find what a point would land on: an existing point, a road, or open ground.</summary>
        public Snap SnapTo(Vec p)
        {
            int node = -1;
            double best = SnapNode;
            for (int i = 0; i < Nodes.Count; i++)
            {
                double d = Geo.Hypot(Nodes[i].X - p.X, Nodes[i].Y - p.Y);
                if (d < best)
                {
                    best = d;
                    node = i;
                }
            }
            if (node >= 0) return new Snap { Point = Nodes[node], Node = node, Segment = -1 };

            int seg = NearestSegment(p, SnapRoad, out Vec on);
            if (seg >= 0) return new Snap { Point = on, Node = -1, Segment = seg };
            return new Snap { Point = p, Node = -1, Segment = -1 };
        }

        /// <summary>The road nearest a point, if within reach, and the closest spot on it.</summary>
        public int NearestSegment(Vec p, double reach, out Vec on)
        {
            int seg = -1;
            double best = reach;
            on = p;
            for (int i = 0; i < Segments.Count; i++)
            {
                Vec a = Nodes[Segments[i].A], b = Nodes[Segments[i].B];
                double t = Along(a, b, p);
                var q = new Vec(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);
                double d = Geo.Hypot(q.X - p.X, q.Y - p.Y);
                if (d < best)
                {
                    best = d;
                    seg = i;
                    on = q;
                }
            }
            return seg;
        }

        /// <summary>How far along the line from a to b the closest point to p is, from 0 to 1.</summary>
        private static double Along(Vec a, Vec b, Vec p)
        {
            double dx = b.X - a.X, dy = b.Y - a.Y;
            double len2 = dx * dx + dy * dy;
            if (len2 < 1e-9) return 0;
            return Geo.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / len2, 0, 1);
        }

        private double Dist(int a, int b)
        {
            return Geo.Hypot(Nodes[a].X - Nodes[b].X, Nodes[a].Y - Nodes[b].Y);
        }

        // ---------------------------------------------------------------- drawing

        /// <summary>
        /// Draw a road from p to q. Returns null if it was added, or a short reason why it
        /// cannot be. With commit false nothing is changed: use that to preview.
        /// </summary>
        public string TryAdd(Vec p, Vec q, int lanes, int mph, bool commit)
        {
            EditableMap work = Clone();
            string error = work.Add(p, q, lanes, mph);
            if (error != null) return error;
            // Make sure the result can actually be built before accepting it.
            try
            {
                work.ToWorld();
            }
            catch (Exception)
            {
                return "That road cannot be built there.";
            }
            if (commit)
            {
                Nodes = work.Nodes;
                Segments = work.Segments;
            }
            return null;
        }

        private string Add(Vec p, Vec q, int lanes, int mph)
        {
            string error;
            int a = Resolve(p, out error);
            if (a < 0) return error;
            int b = Resolve(q, out error);
            if (b < 0) return error;
            if (a == b) return "Too short.";

            // Everything the new road meets on the way becomes a point along it.
            Vec pa = Nodes[a], pb = Nodes[b];
            var stops = new List<KeyValuePair<double, int>>();
            for (int i = 0; i < Nodes.Count; i++)
            {
                if (i == a || i == b) continue;
                double t = Along(pa, pb, Nodes[i]);
                if (t <= 0 || t >= 1) continue;
                double d = Geo.Hypot(pa.X + (pb.X - pa.X) * t - Nodes[i].X, pa.Y + (pb.Y - pa.Y) * t - Nodes[i].Y);
                if (d < SnapRoad) stops.Add(new KeyValuePair<double, int>(t, i));
            }
            int existing = Segments.Count;
            for (int i = 0; i < existing; i++)
            {
                EditSegment s = Segments[i];
                if (s.A == a || s.B == a || s.A == b || s.B == b) continue;
                if (stops.Exists(k => k.Value == s.A || k.Value == s.B)) continue;
                double t, u;
                if (!Cross(pa, pb, Nodes[s.A], Nodes[s.B], out t, out u)) continue;
                var x = new Vec(pa.X + (pb.X - pa.X) * t, pa.Y + (pb.Y - pa.Y) * t);
                double toA = Geo.Hypot(x.X - Nodes[s.A].X, x.Y - Nodes[s.A].Y);
                double toB = Geo.Hypot(x.X - Nodes[s.B].X, x.Y - Nodes[s.B].Y);
                if (toA < SnapNode) stops.Add(new KeyValuePair<double, int>(t, s.A));
                else if (toB < SnapNode) stops.Add(new KeyValuePair<double, int>(t, s.B));
                else if (toA < MinLength || toB < MinLength) return "That crosses another road too close to an intersection.";
                else stops.Add(new KeyValuePair<double, int>(t, Split(i, x)));
            }
            stops.Sort((m, n) => m.Key.CompareTo(n.Key));

            var chain = new List<int> { a };
            foreach (var stop in stops) if (chain[chain.Count - 1] != stop.Value) chain.Add(stop.Value);
            if (chain[chain.Count - 1] != b) chain.Add(b);

            var touched = new HashSet<int>(chain);
            for (int i = 0; i + 1 < chain.Count; i++)
            {
                int from = chain[i], to = chain[i + 1];
                if (Dist(from, to) < MinLength) return "Too short: each stretch of road needs to be at least " + Math.Round(MinLength * 3.28084 / 10) * 10 + " ft.";
                foreach (EditSegment s in Segments)
                {
                    if ((s.A == from && s.B == to) || (s.A == to && s.B == from)) return "There is already a road there.";
                }
                Segments.Add(new EditSegment { A = from, B = to, Lanes = lanes, Mph = mph });
            }

            foreach (int node in touched)
            {
                var angles = new List<double>();
                foreach (EditSegment s in Segments)
                {
                    if (s.A == node) angles.Add(Math.Atan2(Nodes[s.B].Y - Nodes[node].Y, Nodes[s.B].X - Nodes[node].X));
                    if (s.B == node) angles.Add(Math.Atan2(Nodes[s.A].Y - Nodes[node].Y, Nodes[s.A].X - Nodes[node].X));
                }
                if (angles.Count > MaxArms) return "No more than " + MaxArms + " roads can meet at one point.";
                for (int i = 0; i < angles.Count; i++)
                {
                    for (int k = i + 1; k < angles.Count; k++)
                    {
                        double between = Math.Abs(Math.Atan2(Math.Sin(angles[i] - angles[k]), Math.Cos(angles[i] - angles[k])));
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
            double toA = Geo.Hypot(snap.Point.X - Nodes[s.A].X, snap.Point.Y - Nodes[s.A].Y);
            double toB = Geo.Hypot(snap.Point.X - Nodes[s.B].X, snap.Point.Y - Nodes[s.B].Y);
            if (toA < MinLength || toB < MinLength)
            {
                error = "Too close to the end of that road. Join at the end, or further along.";
                return -1;
            }
            return Split(snap.Segment, snap.Point);
        }

        /// <summary>Cut a road in two at a point on it. Returns the new point.</summary>
        private int Split(int segment, Vec at)
        {
            EditSegment s = Segments[segment];
            Nodes.Add(at);
            int mid = Nodes.Count - 1;
            Segments.Add(new EditSegment { A = mid, B = s.B, Lanes = s.Lanes, Mph = s.Mph });
            s.B = mid;
            return mid;
        }

        /// <summary>Where two stretches cross, if they do: t along the first, u along the second, both strictly inside.</summary>
        private static bool Cross(Vec p, Vec p2, Vec q, Vec q2, out double t, out double u)
        {
            t = u = 0;
            double rx = p2.X - p.X, ry = p2.Y - p.Y;
            double sx = q2.X - q.X, sy = q2.Y - q.Y;
            double denom = rx * sy - ry * sx;
            if (Math.Abs(denom) < 1e-9) return false;
            t = ((q.X - p.X) * sy - (q.Y - p.Y) * sx) / denom;
            u = ((q.X - p.X) * ry - (q.Y - p.Y) * rx) / denom;
            return t > 1e-6 && t < 1 - 1e-6 && u > 1e-6 && u < 1 - 1e-6;
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
                double speed = s.Mph * 0.44704;
                if (s.Lanes == 1) b.AddRoad(s.A, s.B, one, null, null, speed);
                else if (s.Lanes == 2) b.AddRoad(s.A, s.B, one, one, null, speed);
                else b.AddRoad(s.A, s.B, two, two, null, speed);
            }
            World world = b.Build();
            // Keep the surroundings the same size however much has been drawn.
            world.Extent = Math.Max(world.Extent, 400);
            return world;
        }

        // ---------------------------------------------------------------- saving

        /// <summary>The whole map as one line of text.</summary>
        public string Serialize()
        {
            var sb = new StringBuilder();
            foreach (Vec n in Nodes) sb.Append(n.X.ToString("F1", CultureInfo.InvariantCulture)).Append(',').Append(n.Y.ToString("F1", CultureInfo.InvariantCulture)).Append(';');
            sb.Append('|');
            foreach (EditSegment s in Segments) sb.Append(s.A).Append(',').Append(s.B).Append(',').Append(s.Lanes).Append(',').Append(s.Mph).Append(';');
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
