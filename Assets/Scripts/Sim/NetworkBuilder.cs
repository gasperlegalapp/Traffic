using System;
using System.Collections.Generic;

namespace Traffic.Sim
{
    /// <summary>
    /// One lane of a road, for part or all of its length. From and To are
    /// positions along the road: 0 to 1 is a fraction of its length, and a
    /// negative From counts back in meters from the far end (a turn pocket).
    /// </summary>
    public struct TrackSpec
    {
        public int Slot;
        public double From;
        public double To;
        /// <summary>Turns allowed from this lane at the far end. Null means work it out.</summary>
        public Turn[] Turns;

        public TrackSpec(int slot, double from = 0, double to = 1, params Turn[] turns)
        {
            Slot = slot;
            From = from;
            To = to;
            Turns = turns != null && turns.Length > 0 ? turns : null;
        }
    }

    /// <summary>
    /// Builds a road network from intersections joined by straight roads. Each
    /// direction of a road is a set of lanes in numbered slots (0 next to the
    /// centerline); lanes can start late or end early. The builder works out the
    /// paths through each intersection, the pavement and the paint.
    /// </summary>
    public sealed class NetworkBuilder
    {
        /// <summary>A standard 12-foot US lane.</summary>
        public const double LaneW = 3.66;
        /// <summary>Lane changes are not allowed this close to a stop line.</summary>
        public const double SolidLine = 20;
        private const double Taper = 18;
        private const double CornerRadius = 5;
        /// <summary>25 mph: the default, for a side street.</summary>
        public const double CitySpeed = 11.2;
        /// <summary>35 mph: a main street, or a ramp.</summary>
        public const double ArterialSpeed = 15.6;
        /// <summary>65 mph: an urban interstate.</summary>
        public const double HighwaySpeed = 29.1;
        /// <summary>US lane lines: a 10-foot dash and a 30-foot gap.</summary>
        private const double DashOn = 3.05, DashOff = 9.15;
        /// <summary>How high a bridge stands over the road beneath it.</summary>
        private const double BridgeHeight = 6.5;
        /// <summary>How far the stop lines sit from the center where roads split or join without a light.</summary>
        private const double FreeRadius = 8;

        private sealed class RoadSpec
        {
            public int A, B;
            public TrackSpec[] Fwd, Back;
            public Vec[] Via;
            public double Speed;
            public double ShiftA, ShiftB;
            public bool Bridge;
            public bool Traced;
        }

        private readonly List<Vec> nodePos = new List<Vec>();
        private readonly List<JunctionMode> nodeMode = new List<JunctionMode>();
        private readonly List<RoadSpec> roads = new List<RoadSpec>();
        private readonly List<int[]> uTurns = new List<int[]>();

        private World world;
        private readonly Dictionary<int, Turn[]> explicitTurns = new Dictionary<int, Turn[]>();

        public int AddNode(double x, double y, JunctionMode mode)
        {
            nodePos.Add(new Vec(x, y));
            nodeMode.Add(mode);
            return nodePos.Count - 1;
        }

        /// <summary>A road from node a to node b. fwd are the lanes going a to b, back the lanes going b to a.</summary>
        /// <summary>
        /// Either set of lanes may be empty for a one-way road. via are points the centerline passes
        /// through between the two nodes, for a curved road; a and b may then be the same node.
        /// speed is the limit in meters per second. shiftA and shiftB move the road sideways where it
        /// meets each node, to the right of travel from a to b, so that a ramp can leave from or
        /// join at the outside lane of a highway instead of its centerline.
        /// </summary>
        /// <remarks>A bridge rises over whatever it crosses; traffic on it and under it does not interact.</remarks>
        public void AddRoad(int a, int b, TrackSpec[] fwd, TrackSpec[] back, Vec[] via = null, double speed = CitySpeed, double shiftA = 0, double shiftB = 0, bool bridge = false)
        {
            roads.Add(new RoadSpec
            {
                A = a, B = b, Fwd = fwd ?? new TrackSpec[0], Back = back ?? new TrackSpec[0], Via = via ?? new Vec[0], Speed = speed, ShiftA = shiftA, ShiftB = shiftB, Bridge = bridge,
            });
        }

        /// <summary>
        /// A curved road whose centerline is traced by points running all the way from node a to
        /// node b. The builder drops the points that fall inside either intersection and aims the
        /// road's ends at the curve, so the caller does not need to know how big they are.
        /// </summary>
        public void AddCurvedRoad(int a, int b, TrackSpec[] fwd, TrackSpec[] back, Vec[] trace, double speed)
        {
            roads.Add(new RoadSpec { A = a, B = b, Fwd = fwd ?? new TrackSpec[0], Back = back ?? new TrackSpec[0], Via = trace, Speed = speed, Traced = true });
        }

        /// <summary>Allow U-turns at a node for traffic arriving from a neighboring node.</summary>
        public void AllowUTurn(int node, int arrivingFrom)
        {
            uTurns.Add(new[] { node, arrivingFrom });
        }

        private static double Resolve(double v, double length)
        {
            return v < 0 ? length + v : v <= 1 ? v * length : v;
        }

        private static int MaxSlot(TrackSpec[] tracks, bool atEnd)
        {
            int max = -1;
            foreach (TrackSpec t in tracks)
            {
                bool touches = atEnd ? t.To == 1 : t.From == 0;
                if (touches) max = Math.Max(max, t.Slot);
            }
            return max;
        }

        public World Build()
        {
            world = new World { LaneWidth = LaneW };
            double extent = 0;

            // How far each intersection's stop lines sit from its center.
            var radius = new double[nodePos.Count];
            // For each node, the direction and half-width of every road end that meets it.
            var endsAt = new List<double[]>[nodePos.Count];
            for (int i = 0; i < radius.Length; i++)
            {
                radius[i] = 12;
                endsAt[i] = new List<double[]>();
            }
            foreach (RoadSpec r in roads)
            {
                int atA = Math.Max(MaxSlot(r.Back, true), MaxSlot(r.Fwd, false)) + 1;
                int atB = Math.Max(MaxSlot(r.Fwd, true), MaxSlot(r.Back, false)) + 1;
                radius[r.A] = Math.Max(radius[r.A], atA * LaneW + CornerRadius);
                radius[r.B] = Math.Max(radius[r.B], atB * LaneW + CornerRadius);

                // Roughly which way the road leaves each node: for a traced curve, toward where it is a little way out.
                Vec a = nodePos[r.A], b = nodePos[r.B];
                Vec fromA = b, fromB = a;
                if (r.Traced)
                {
                    foreach (Vec v in r.Via) if (Geo.Hypot(v.X - a.X, v.Y - a.Y) > 15) { fromA = v; break; }
                    for (int i = r.Via.Length - 1; i >= 0; i--) if (Geo.Hypot(r.Via[i].X - b.X, r.Via[i].Y - b.Y) > 15) { fromB = r.Via[i]; break; }
                    endsAt[r.A].Add(new[] { Math.Atan2(fromA.Y - a.Y, fromA.X - a.X), atA * LaneW });
                    endsAt[r.B].Add(new[] { Math.Atan2(fromB.Y - b.Y, fromB.X - b.X), atB * LaneW });
                }
                else if (r.Via.Length >= 2)
                {
                    int m = r.Via.Length;
                    endsAt[r.A].Add(new[] { Math.Atan2(r.Via[1].Y - r.Via[0].Y, r.Via[1].X - r.Via[0].X), atA * LaneW });
                    endsAt[r.B].Add(new[] { Math.Atan2(r.Via[m - 2].Y - r.Via[m - 1].Y, r.Via[m - 2].X - r.Via[m - 1].X), atB * LaneW });
                }
                else
                {
                    endsAt[r.A].Add(new[] { Math.Atan2(b.Y - a.Y, b.X - a.X), atA * LaneW });
                    endsAt[r.B].Add(new[] { Math.Atan2(a.Y - b.Y, a.X - b.X), atB * LaneW });
                }
            }
            for (int i = 0; i < nodePos.Count; i++)
            {
                int ends = EndsAt(i);
                if (ends == 1) radius[i] = 0; // the edge of the map
                else if (ends > 2 && nodeMode[i] == JunctionMode.Free) radius[i] = FreeRadius; // a ramp leaving or joining
                else
                {
                    // Roads that meet at a narrow angle need a bigger intersection, so that they
                    // have come apart by the time they reach their stop lines.
                    List<double[]> around = endsAt[i];
                    around.Sort((x, y) => x[0].CompareTo(y[0]));
                    for (int k = 0; k < around.Count; k++)
                    {
                        double[] one = around[k], next = around[(k + 1) % around.Count];
                        double between = next[0] - one[0];
                        if (between <= 0) between += 2 * Math.PI;
                        if (between < Math.PI) radius[i] = Math.Max(radius[i], (one[1] + next[1] + 1) / (2 * Math.Sin(between / 2)));
                    }
                }
                world.Nodes.Add(new Node { Id = i, Pos = nodePos[i], Radius = radius[i], DefaultMode = nodeMode[i] });
                extent = Math.Max(extent, Math.Max(Math.Abs(nodePos[i].X), Math.Abs(nodePos[i].Y)));
            }

            foreach (RoadSpec r in roads)
            {
                Vec a = nodePos[r.A];
                Vec b = nodePos[r.B];
                // A straight road runs node to node. A curved one leaves a in the direction its via
                // points start out in, and arrives at b in the direction they end in.
                Vec[] via = r.Via;
                if (r.Traced)
                {
                    // Keep only the part of the trace that lies clear of both intersections.
                    var kept = new List<Vec>();
                    foreach (Vec v in via)
                    {
                        if (Geo.Hypot(v.X - a.X, v.Y - a.Y) > radius[r.A] + 3 && Geo.Hypot(v.X - b.X, v.Y - b.Y) > radius[r.B] + 3) kept.Add(v);
                    }
                    via = kept.ToArray();
                }
                int m = via.Length;
                // A traced road aims straight from each node at the nearest point kept; any other
                // curved road sets its own direction with its first and last two points.
                bool own = m >= 2 && !r.Traced;
                Vec from0 = own ? via[0] : a, from1 = own ? via[1] : m >= 1 ? via[0] : b;
                Vec to0 = own ? via[m - 2] : m >= 1 ? via[m - 1] : a, to1 = own ? via[m - 1] : b;
                double la = Geo.Hypot(from1.X - from0.X, from1.Y - from0.Y);
                double lb = Geo.Hypot(to1.X - to0.X, to1.Y - to0.Y);
                double ax = (from1.X - from0.X) / la, ay = (from1.Y - from0.Y) / la;
                double bx = (to1.X - to0.X) / lb, by = (to1.Y - to0.Y) / lb;
                var line = new List<Vec> { new Vec(a.X + ax * radius[r.A] + ay * r.ShiftA, a.Y + ay * radius[r.A] - ax * r.ShiftA) };
                line.AddRange(via);
                line.Add(new Vec(b.X - bx * radius[r.B] + by * r.ShiftB, b.Y - by * radius[r.B] - bx * r.ShiftB));
                // Resample to about a meter between points.
                var path = new LanePath(line);
                if (path.Length < 15) throw new InvalidOperationException("Two intersections are too close together for the road between them.");
                int n = (int)Math.Ceiling(path.Length);
                var center = new List<Vec>();
                for (int i = 0; i <= n; i++)
                {
                    Vec p = path.At(path.Length * i / n).Point;
                    center.Add(p);
                    extent = Math.Max(extent, Math.Max(Math.Abs(p.X), Math.Abs(p.Y)));
                }
                var reversed = new List<Vec>(center);
                reversed.Reverse();

                Approach armA = AddArm(r.A, Math.Atan2(ay, ax), r.B);
                Approach armB = AddArm(r.B, Math.Atan2(-by, -bx), r.A);
                armA.Shift = r.ShiftA;
                armB.Shift = -r.ShiftB;
                double peak = r.Bridge ? BridgeHeight : 0;
                BuildDirection(center, r.Fwd, armB, armA, r.Speed, peak);
                BuildDirection(reversed, r.Back, armA, armB, r.Speed, peak);
            }
            world.Extent = extent + 30;

            foreach (Node node in world.Nodes) BuildNode(node);
            return world;
        }

        private Approach AddArm(int node, double angle, int otherNode)
        {
            Node n = world.Nodes[node];
            var arm = new Approach
            {
                Node = node,
                Arm = n.Arms.Count,
                Angle = angle,
                Street = Math.Abs(Math.Cos(angle)) > Math.Abs(Math.Sin(angle)) ? 1 : 0,
            };
            foreach (int[] u in uTurns) if (u[0] == node && u[1] == otherNode) arm.AllowUTurn = true;
            n.Arms.Add(arm);
            return arm;
        }

        // ------------------------------------------------------------ roads

        private void BuildDirection(List<Vec> center, TrackSpec[] specs, Approach to, Approach from, double speed, double peak)
        {
            if (specs.Length == 0) return;
            int n = center.Count - 1;
            var st = new double[n + 1];
            for (int i = 1; i <= n; i++) st[i] = st[i - 1] + Geo.Hypot(center[i].X - center[i - 1].X, center[i].Y - center[i - 1].Y);
            double len = st[n];
            Vec p1 = center[n];

            var sorted = new List<TrackSpec>(specs);
            sorted.Sort((x, y) => x.Slot != y.Slot ? x.Slot.CompareTo(y.Slot) : x.From.CompareTo(y.From));
            int count = sorted.Count;
            var lanes = new Lane[count];
            var fromSt = new double[count];
            var toSt = new double[count];
            int maxSlot = 0;
            int maxReaching = 0;
            for (int k = 0; k < count; k++)
            {
                TrackSpec spec = sorted[k];
                fromSt[k] = Resolve(spec.From, len);
                toSt[k] = Resolve(spec.To, len);
                maxSlot = Math.Max(maxSlot, spec.Slot);
                Vec[] offset = Geo.Offset(center, (spec.Slot + 0.5) * LaneW);
                var pts = new List<Vec>();
                for (int i = 0; i <= n; i++) if (st[i] >= fromSt[k] - 1e-6 && st[i] <= toSt[k] + 1e-6) pts.Add(offset[i]);
                var path = new LanePath(pts);
                var lane = new Lane
                {
                    Id = world.Lanes.Count,
                    Kind = LaneKind.Road,
                    Path = path,
                    Length = path.Length,
                    SpeedLimit = speed,
                    Peak = peak,
                    Slot = spec.Slot,
                    ApproachNode = to.Node,
                    ApproachArm = to.Arm,
                };
                world.Lanes.Add(lane);
                lanes[k] = lane;
                if (toSt[k] >= len - 1e-6)
                {
                    lane.Node = to.Node;
                    lane.Arm = to.Arm;
                    lane.Connectors = new[] { -1, -1, -1, -1 };
                    to.Inbound.Add(lane.Id);
                    maxReaching = Math.Max(maxReaching, spec.Slot);
                    if (spec.Turns != null) explicitTurns[lane.Id] = spec.Turns;
                }
                if (fromSt[k] <= 1e-6) from.Outbound.Add(lane.Id);
            }

            // Neighbors, and which way a lane that ends has to merge.
            for (int i = 0; i < count; i++)
            {
                for (int k = 0; k < count; k++)
                {
                    if (sorted[k].Slot != sorted[i].Slot + 1) continue;
                    if (fromSt[i] < toSt[k] && fromSt[k] < toSt[i])
                    {
                        lanes[i].Rights.Add(lanes[k].Id);
                        lanes[k].Lefts.Add(lanes[i].Id);
                    }
                }
            }
            for (int i = 0; i < count; i++)
            {
                if (lanes[i].Connectors != null) continue;
                lanes[i].MergeSide = 1;
                for (int k = 0; k < count; k++)
                {
                    bool carriesOn = fromSt[k] <= toSt[i] && toSt[k] > toSt[i];
                    if (carriesOn && sorted[k].Slot == sorted[i].Slot - 1) lanes[i].MergeSide = -1;
                }
            }

            double h = Math.Atan2(p1.Y - center[n - 1].Y, p1.X - center[n - 1].X);
            double rx = Math.Sin(h);
            double ry = -Math.Cos(h);
            to.H = h;
            double postOff = (maxReaching + 1) * LaneW + 1.6;
            to.Post = new Vec(p1.X + rx * postOff, p1.Y + ry * postOff);

            // Pavement and paint. The road narrows gradually past the end of a lane.
            double Cover(int k, double s)
            {
                if (s < fromSt[k]) return fromSt[k] > 1e-6 ? Geo.Clamp(1 - (fromSt[k] - s) / Taper, 0, 1) : 0;
                if (s > toSt[k]) return toSt[k] < len - 1e-6 ? Geo.Clamp(1 - (s - toSt[k]) / Taper, 0, 1) : 0;
                return 1;
            }
            var right = new double[n + 1];
            var left = new double[n + 1];
            for (int i = 0; i <= n; i++)
            {
                double r = 0;
                double l = double.PositiveInfinity;
                for (int k = 0; k < count; k++)
                {
                    double c = Cover(k, st[i]);
                    if (c <= 0) continue;
                    r = Math.Max(r, (sorted[k].Slot + c) * LaneW);
                    l = Math.Min(l, (sorted[k].Slot + 1 - c) * LaneW);
                }
                right[i] = r;
                left[i] = double.IsInfinity(l) ? 0 : l;
            }
            to.InLeft = left[n];
            to.InRight = right[n];
            from.OutLeft = left[0];
            from.OutRight = right[0];
            world.Surfaces.Add(new Strip { Left = center.ToArray(), Right = Geo.Offset(center, i => right[i]), Peak = peak });
            world.Markings.Add(new Marking { Pts = Geo.Offset(center, i => right[i] - 0.25), Width = 0.14, Peak = peak });
            world.Markings.Add(new Marking { Pts = Geo.Offset(center, i => left[i] + 0.16), Width = 0.12, Yellow = true, Peak = peak });

            bool controlled = EndsAt(to.Node) > 2 && nodeMode[to.Node] != JunctionMode.Free;
            for (int slot = 0; slot < maxSlot; slot++)
            {
                Vec[] divider = Geo.Offset(center, (slot + 1) * LaneW);
                int runStart = -1;
                for (int i = 0; i <= n + 1; i++)
                {
                    bool both = i <= n && Occupied(sorted, fromSt, toSt, slot, st[i]) && Occupied(sorted, fromSt, toSt, slot + 1, st[i]);
                    if (both && runStart < 0) runStart = i;
                    if (!both && runStart >= 0)
                    {
                        int runEnd = i - 1;
                        int solidFrom = runEnd + 1;
                        if (runEnd == n && controlled)
                        {
                            solidFrom = runStart;
                            while (solidFrom < runEnd && st[solidFrom] < len - SolidLine) solidFrom++;
                        }
                        AddLine(divider, runStart, Math.Min(solidFrom, runEnd), true);
                        AddLine(divider, solidFrom, runEnd, false);
                        runStart = -1;
                    }
                }
            }
        }

        /// <summary>How many road ends meet at a node. A loop that starts and ends there counts twice.</summary>
        private int EndsAt(int node)
        {
            int count = 0;
            foreach (RoadSpec r in roads)
            {
                if (r.A == node) count++;
                if (r.B == node) count++;
            }
            return count;
        }

        private static bool Occupied(List<TrackSpec> specs, double[] fromSt, double[] toSt, int slot, double s)
        {
            for (int k = 0; k < specs.Count; k++)
            {
                if (specs[k].Slot == slot && s >= fromSt[k] - 1e-6 && s <= toSt[k] + 1e-6) return true;
            }
            return false;
        }

        private void AddLine(Vec[] pts, int from, int to, bool dashed)
        {
            if (to - from < 1) return;
            var part = new Vec[to - from + 1];
            Array.Copy(pts, from, part, 0, part.Length);
            world.Markings.Add(new Marking { Pts = part, Width = 0.14, DashOn = dashed ? DashOn : 0, DashOff = dashed ? DashOff : 0 });
        }

        // ------------------------------------------------------------ intersections

        private static double Wrap(double a)
        {
            while (a > Math.PI) a -= 2 * Math.PI;
            while (a < -Math.PI) a += 2 * Math.PI;
            return a;
        }

        private void BuildNode(Node node)
        {
            if (node.Terminal)
            {
                // The edge of the map: arriving lanes run off it, leaving lanes bring new vehicles in.
                Approach edge = node.Arms[0];
                foreach (int id in edge.Inbound)
                {
                    world.Lanes[id].Connectors = null;
                    world.Lanes[id].IsExit = true;
                }
                foreach (int id in edge.Outbound) world.Lanes[id].IsEntry = true;
                if (edge.Outbound.Count > 0) world.Entries.Add(edge);
                return;
            }

            // Which arms share a green: those lying along the most north-south road through the node
            // are one street, and those across it are the other. This holds for roads at any angle.
            double axis = 0, best = -1;
            foreach (Approach arm in node.Arms)
            {
                if (Math.Abs(Math.Sin(arm.Angle)) > best)
                {
                    best = Math.Abs(Math.Sin(arm.Angle));
                    axis = arm.Angle;
                }
            }
            foreach (Approach arm in node.Arms) arm.Street = Math.Abs(Math.Cos(arm.Angle - axis)) >= Math.Cos(Math.PI / 4) - 1e-9 ? 0 : 1;

            var conns = new List<Lane>();
            foreach (Approach arm in node.Arms)
            {
                if (arm.Inbound.Count == 0) continue; // nothing arrives on this arm

                // Which arm each turn leads to. The way most nearly straight on is "straight";
                // anything else is a left or a right, however slight.
                var target = new int[Lane.Turns];
                var bend = new double[Lane.Turns];
                for (int t = 0; t < Lane.Turns; t++) target[t] = -1;
                double heading = arm.Angle + Math.PI;
                Approach straight = null;
                foreach (Approach other in node.Arms)
                {
                    if (other == arm || other.Outbound.Count == 0) continue;
                    double d = Math.Abs(Wrap(other.Angle - heading));
                    bool ahead = node.Arms.Count == 2 || d < Math.PI / 4; // with two arms it is a bend in the road, not a turn
                    if (ahead && (straight == null || d < Math.Abs(Wrap(straight.Angle - heading)))) straight = other;
                }
                foreach (Approach other in node.Arms)
                {
                    if (other == arm || other.Outbound.Count == 0) continue;
                    double d = Wrap(other.Angle - heading);
                    Turn turn = other == straight ? Turn.Straight : d > 0 ? Turn.Left : Turn.Right;
                    if (target[(int)turn] < 0)
                    {
                        target[(int)turn] = other.Arm;
                        bend[(int)turn] = d;
                    }
                }
                if (arm.AllowUTurn && node.Controlled) target[(int)Turn.UTurn] = arm.Arm;

                bool anyWayOut = false;
                for (int t = 0; t < Lane.Turns; t++) if (target[t] >= 0) anyWayOut = true;
                if (!anyWayOut)
                {
                    // A road that leads here with nowhere to go on: treat it as leaving the map.
                    foreach (int id in arm.Inbound)
                    {
                        world.Lanes[id].Connectors = null;
                        world.Lanes[id].IsExit = true;
                    }
                    continue;
                }

                List<Turn>[] sets = AssignTurns(arm, target);

                for (int t = 0; t < Lane.Turns; t++)
                {
                    if (target[t] < 0) continue;
                    var turn = (Turn)t;
                    var src = new List<int>();
                    for (int i = 0; i < arm.Inbound.Count; i++) if (sets[i].Contains(turn)) src.Add(arm.Inbound[i]);
                    List<int> outs = node.Arms[target[t]].Outbound;
                    for (int k = 0; k < src.Count; k++)
                    {
                        // Where more lanes arrive than leave, the extra ones share a lane on the far side
                        // and take turns going into it.
                        int to;
                        if (turn == Turn.UTurn)
                        {
                            if (k > 0) break;
                            to = outs[outs.Count - 1];
                        }
                        else if (turn == Turn.Right || (turn == Turn.Straight && node.Arms.Count > 2 && bend[t] < -0.1)) to = outs[Math.Max(0, outs.Count - src.Count + k)]; // joining from the right: keep right
                        else to = outs[Math.Min(k, outs.Count - 1)];
                        conns.Add(Connect(node, src[k], to, turn));
                    }
                }

                // Record what the approach offers, and which way to move for each turn.
                var offered = new bool[Lane.Turns];
                foreach (int id in arm.Inbound) for (int t = 0; t < Lane.Turns; t++) if (world.Lanes[id].Connectors[t] >= 0) offered[t] = true;
                foreach (Lane lane in world.Lanes)
                {
                    if (lane.Kind != LaneKind.Road || lane.ApproachNode != node.Id || lane.ApproachArm != arm.Arm) continue;
                    lane.ApproachTurns = offered;
                    for (int t = 0; t < Lane.Turns; t++)
                    {
                        int min = int.MaxValue, max = int.MinValue;
                        foreach (int id in arm.Inbound)
                        {
                            if (world.Lanes[id].Connectors[t] < 0) continue;
                            min = Math.Min(min, world.Lanes[id].Slot);
                            max = Math.Max(max, world.Lanes[id].Slot);
                        }
                        lane.TurnSide[t] = min == int.MaxValue ? 0 : lane.Slot < min ? 1 : lane.Slot > max ? -1 : 0;
                    }
                }

                if (!node.Controlled) continue;
                for (int i = 0; i < arm.Inbound.Count; i++)
                {
                    Lane lane = world.Lanes[arm.Inbound[i]];
                    PathPose end = lane.Path.At(lane.Length);
                    double rx = Math.Sin(end.H) * (LaneW / 2 - 0.15);
                    double ry = -Math.Cos(end.H) * (LaneW / 2 - 0.15);
                    world.StopLines.Add(new StopLine { Lane = lane.Id, A = new Vec(end.X - rx, end.Y - ry), B = new Vec(end.X + rx, end.Y + ry), H = end.H });
                    var turns = new bool[Lane.Turns];
                    for (int t = 0; t < Lane.Turns; t++) turns[t] = lane.Connectors[t] >= 0;
                    world.Arrows.Add(new ArrowMark { At = end, Turns = turns, NoUTurn = i == 0 && turns[(int)Turn.Left] && !arm.AllowUTurn });
                }
            }
            Connectors.Link(conns);
            BuildBox(node);
            if (node.Arms.Count == 2) PaintBend(node);
            else if (!node.Controlled) PaintJunction(conns);
        }

        /// <summary>
        /// Carry the lane lines through a place where a ramp leaves or joins: a dashed line
        /// between lanes that run on side by side, and solid lines along the outside of each
        /// stream, which meet in a point where the ramp splits off or comes in.
        /// </summary>
        private void PaintJunction(List<Lane> conns)
        {
            foreach (Lane c in conns)
            {
                Lane from = world.Lanes[c.From];
                Lane to = world.Lanes[c.Next];
                bool leftNeighbor = false, rightNeighbor = false;
                foreach (Lane o in conns)
                {
                    if (o == c || o.FromArm != c.FromArm) continue;
                    Lane oFrom = world.Lanes[o.From];
                    Lane oTo = world.Lanes[o.Next];
                    if (oTo.ApproachNode != to.ApproachNode || oTo.ApproachArm != to.ApproachArm) continue;
                    if (oFrom.Slot == from.Slot - 1 && oTo.Slot == to.Slot - 1) leftNeighbor = true;
                    if (oFrom.Slot == from.Slot + 1 && oTo.Slot == to.Slot + 1) rightNeighbor = true;
                }
                // The leftmost lane of the road it joins gets a yellow left edge, like the road itself.
                bool leftmost = true;
                foreach (Lane o in world.Lanes)
                {
                    if (o.Kind == LaneKind.Road && o.ApproachNode == to.ApproachNode && o.ApproachArm == to.ApproachArm && o.Preds.Count > 0 && o.Slot < to.Slot) leftmost = false;
                }
                Vec[] pts = c.Path.Pts;
                if (leftNeighbor) world.Markings.Add(new Marking { Pts = Geo.Offset(pts, -LaneW / 2), Width = 0.14, DashOn = DashOn, DashOff = DashOff });
                else if (leftmost) world.Markings.Add(new Marking { Pts = Geo.Offset(pts, -LaneW / 2 + 0.16), Width = 0.12, Yellow = true });
                else world.Markings.Add(new Marking { Pts = Geo.Offset(pts, -LaneW / 2 + 0.2), Width = 0.14 });
                if (!rightNeighbor) world.Markings.Add(new Marking { Pts = Geo.Offset(pts, LaneW / 2 - 0.25), Width = 0.14 });
            }
        }

        /// <summary>Carry the edge lines, the center line and the lane lines round a bend.</summary>
        private void PaintBend(Node node)
        {
            foreach (Approach arm in node.Arms)
            {
                // Traffic arrives on this arm and leaves on the other one.
                Approach other = node.Arms[1 - arm.Arm];
                if (arm.Inbound.Count == 0 || other.Outbound.Count == 0) continue;
                double hIn = arm.H;
                double hOut = other.Angle;
                double inX = node.Pos.X + Math.Cos(arm.Angle) * node.Radius, inY = node.Pos.Y + Math.Sin(arm.Angle) * node.Radius;
                double outX = node.Pos.X + Math.Cos(other.Angle) * node.Radius, outY = node.Pos.Y + Math.Sin(other.Angle) * node.Radius;
                // A curve that starts offIn to the right of the arriving road's centerline
                // and ends offOut to the right of the leaving road's centerline.
                Vec[] Curve(double offIn, double offOut)
                {
                    var a = new PathPose { X = inX + Math.Sin(hIn) * offIn, Y = inY - Math.Cos(hIn) * offIn, H = hIn };
                    var b = new PathPose { X = outX + Math.Sin(hOut) * offOut, Y = outY - Math.Cos(hOut) * offOut, H = hOut };
                    double chord = Geo.Hypot(b.X - a.X, b.Y - a.Y);
                    return Geo.Bezier(a, b, 0.5523 * chord / Math.Sqrt(2));
                }
                world.Markings.Add(new Marking { Pts = Curve(arm.InRight - 0.25, other.OutRight - 0.25), Width = 0.14 });
                world.Markings.Add(new Marking { Pts = Curve(arm.InLeft + 0.16, other.OutLeft + 0.16), Width = 0.12, Yellow = true });
                // A solid line between lanes that go round side by side: no lane changes in the bend.
                for (int i = 0; i + 1 < arm.Inbound.Count; i++)
                {
                    Lane inner = world.Lanes[arm.Inbound[i]];
                    Lane outer = world.Lanes[arm.Inbound[i + 1]];
                    int innerConn = inner.Connectors[(int)Turn.Straight];
                    int outerConn = outer.Connectors[(int)Turn.Straight];
                    if (innerConn < 0 || outerConn < 0) continue;
                    Lane innerOut = world.Lanes[world.Lanes[innerConn].Next];
                    Lane outerOut = world.Lanes[world.Lanes[outerConn].Next];
                    if (outer.Slot != inner.Slot + 1 || outerOut.Slot != innerOut.Slot + 1) continue;
                    world.Markings.Add(new Marking { Pts = Curve(outer.Slot * LaneW, outerOut.Slot * LaneW), Width = 0.14 });
                }
            }
        }

        /// <summary>Decide which turns each arriving lane may make.</summary>
        private List<Turn>[] AssignTurns(Approach arm, int[] target)
        {
            int n = arm.Inbound.Count;
            bool hasL = target[(int)Turn.Left] >= 0;
            bool hasS = target[(int)Turn.Straight] >= 0;
            bool hasR = target[(int)Turn.Right] >= 0;
            bool hasU = target[(int)Turn.UTurn] >= 0;
            var sets = new List<Turn>[n];
            for (int i = 0; i < n; i++)
            {
                var set = new List<Turn>();
                sets[i] = set;
                Turn[] given;
                if (explicitTurns.TryGetValue(arm.Inbound[i], out given))
                {
                    foreach (Turn t in given) if (target[(int)t] >= 0) set.Add(t);
                    if (set.Count > 0) continue;
                }
                bool first = i == 0;
                bool last = i == n - 1;
                if (n == 1)
                {
                    if (hasL) set.Add(Turn.Left);
                    if (hasS) set.Add(Turn.Straight);
                    if (hasR) set.Add(Turn.Right);
                }
                else if (!hasS)
                {
                    // The stem of a T: left lanes turn left, right lanes turn right.
                    bool leftHalf = hasL && (!hasR || i < (n + 1) / 2);
                    set.Add(leftHalf ? Turn.Left : Turn.Right);
                }
                else
                {
                    if (first && hasL) set.Add(Turn.Left);
                    if (last && hasR) set.Add(Turn.Right);
                    bool leftOnly = first && hasL && n >= 3;
                    bool rightOnly = last && hasR && n >= 4;
                    if (!leftOnly && !rightOnly) set.Add(Turn.Straight);
                }
                if (first && hasU && set.Contains(Turn.Left)) set.Add(Turn.UTurn);
            }
            if (n > 0 && hasU && !sets[0].Contains(Turn.UTurn) && sets[0].Contains(Turn.Left)) sets[0].Add(Turn.UTurn);
            return sets;
        }

        private Lane Connect(Node node, int fromId, int toId, Turn turn)
        {
            Lane from = world.Lanes[fromId];
            Lane to = world.Lanes[toId];
            PathPose a = from.Path.At(from.Length);
            PathPose b = to.Path.At(0);
            double chord = Geo.Hypot(b.X - a.X, b.Y - a.Y);
            var pts = new List<Vec>();
            if (turn == Turn.UTurn)
            {
                // Pull forward, swing round to the left, and come back.
                const double forward = 2.5;
                double r = chord / 2;
                double fx = Math.Cos(a.H), fy = Math.Sin(a.H);
                double cx = a.X + fx * forward - fy * r;
                double cy = a.Y + fy * forward + fx * r;
                pts.Add(a.Point);
                for (int i = 0; i <= 16; i++)
                {
                    double ang = a.H - Math.PI / 2 + Math.PI * i / 16;
                    pts.Add(new Vec(cx + r * Math.Cos(ang), cy + r * Math.Sin(ang)));
                }
                pts.Add(b.Point);
            }
            else if (Math.Abs(Wrap(b.H - a.H)) < 0.05)
            {
                double sideways = Math.Abs(Math.Cos(a.H) * (b.Y - a.Y) - Math.Sin(a.H) * (b.X - a.X));
                if (sideways < 0.1)
                {
                    pts.Add(a.Point);
                    pts.Add(b.Point);
                }
                else pts.AddRange(Geo.Bezier(a, b, chord * 0.4));
            }
            else pts.AddRange(Geo.Bezier(a, b, 0.5523 * chord / Math.Sqrt(2)));

            var path = new LanePath(pts);
            var lane = new Lane
            {
                Id = world.Lanes.Count,
                Kind = LaneKind.Connector,
                Path = path,
                Length = path.Length,
                SpeedLimit = Math.Min(from.SpeedLimit, to.SpeedLimit),
                Node = node.Id,
                From = fromId,
                Next = toId,
                Turn = turn,
                FromArm = from.Arm,
            };
            lane.Preds.Add(fromId);
            world.Lanes.Add(lane);
            from.Connectors[(int)turn] = lane.Id;
            to.Preds.Add(lane.Id);
            return lane;
        }

        /// <summary>The outline of the pavement inside an intersection, with rounded corners between roads.</summary>
        private void BuildBox(Node node)
        {
            var arms = new List<Approach>(node.Arms);
            arms.Sort((x, y) => Wrap(x.Angle).CompareTo(Wrap(y.Angle)));
            int n = arms.Count;
            var cw = new Vec[n];
            var ccw = new Vec[n];
            for (int i = 0; i < n; i++)
            {
                Approach arm = arms[i];
                double dx = Math.Cos(arm.Angle), dy = Math.Sin(arm.Angle);
                double wIn = arm.Inbound.Count == 0 ? 0 : (MaxLaneSlot(arm.Inbound) + 1) * LaneW;
                double wOut = arm.Outbound.Count == 0 ? 0 : (MaxLaneSlot(arm.Outbound) + 1) * LaneW;
                double mx = node.Pos.X + dx * node.Radius, my = node.Pos.Y + dy * node.Radius;
                double toCw = wOut + arm.Shift;
                double toCcw = wIn - arm.Shift;
                cw[i] = new Vec(mx + dy * toCw, my - dx * toCw);
                ccw[i] = new Vec(mx - dy * toCcw, my + dx * toCcw);
            }
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                node.Box.Add(cw[i]);
                node.Box.Add(ccw[i]);
                if (n < 2) continue;
                Vec p = ccw[i];
                Vec q = cw[j];
                double d1x = Math.Cos(arms[i].Angle), d1y = Math.Sin(arms[i].Angle);
                double d2x = Math.Cos(arms[j].Angle), d2y = Math.Sin(arms[j].Angle);
                double cross = d1x * d2y - d1y * d2x;
                if (Math.Abs(cross) < 0.05) continue; // the two edges run nearly side by side: join them straight
                double t = ((q.X - p.X) * d2y - (q.Y - p.Y) * d2x) / cross;
                var c = new Vec(p.X + d1x * t, p.Y + d1y * t);
                for (int k = 1; k < 8; k++)
                {
                    double u = k / 8.0;
                    double w0 = (1 - u) * (1 - u), w1 = 2 * u * (1 - u), w2 = u * u;
                    node.Box.Add(new Vec(w0 * p.X + w1 * c.X + w2 * q.X, w0 * p.Y + w1 * c.Y + w2 * q.Y));
                }
            }
        }

        private int MaxLaneSlot(List<int> ids)
        {
            int max = 0;
            foreach (int id in ids) max = Math.Max(max, world.Lanes[id].Slot);
            return max;
        }
    }
}
