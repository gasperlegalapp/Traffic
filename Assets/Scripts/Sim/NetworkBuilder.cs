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
        public const double LaneW = 3.5;
        /// <summary>Lane changes are not allowed this close to a stop line.</summary>
        public const double SolidLine = 20;
        private const double Taper = 18;
        private const double CornerRadius = 5;
        /// <summary>30 mph.</summary>
        private const double SpeedLimit = 13.4;

        private sealed class RoadSpec
        {
            public int A, B;
            public TrackSpec[] Fwd, Back;
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
        public void AddRoad(int a, int b, TrackSpec[] fwd, TrackSpec[] back)
        {
            roads.Add(new RoadSpec { A = a, B = b, Fwd = fwd, Back = back });
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
            int max = 0;
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
            for (int i = 0; i < radius.Length; i++) radius[i] = 12;
            foreach (RoadSpec r in roads)
            {
                int atA = Math.Max(MaxSlot(r.Back, true), MaxSlot(r.Fwd, false)) + 1;
                int atB = Math.Max(MaxSlot(r.Fwd, true), MaxSlot(r.Back, false)) + 1;
                radius[r.A] = Math.Max(radius[r.A], atA * LaneW + CornerRadius);
                radius[r.B] = Math.Max(radius[r.B], atB * LaneW + CornerRadius);
            }
            for (int i = 0; i < nodePos.Count; i++)
            {
                world.Nodes.Add(new Node { Id = i, Pos = nodePos[i], Radius = radius[i], DefaultMode = nodeMode[i] });
                extent = Math.Max(extent, Math.Max(Math.Abs(nodePos[i].X), Math.Abs(nodePos[i].Y)));
            }
            world.Extent = extent + 30;

            foreach (RoadSpec r in roads)
            {
                Vec a = nodePos[r.A];
                Vec b = nodePos[r.B];
                double len = Geo.Hypot(b.X - a.X, b.Y - a.Y);
                double dx = (b.X - a.X) / len;
                double dy = (b.Y - a.Y) / len;
                var p0 = new Vec(a.X + dx * radius[r.A], a.Y + dy * radius[r.A]);
                var p1 = new Vec(b.X - dx * radius[r.B], b.Y - dy * radius[r.B]);
                Approach armA = AddArm(r.A, Math.Atan2(dy, dx), r.B);
                Approach armB = AddArm(r.B, Math.Atan2(-dy, -dx), r.A);
                BuildDirection(p0, p1, r.Fwd, armB, armA);
                BuildDirection(p1, p0, r.Back, armA, armB);
            }

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

        private void BuildDirection(Vec p0, Vec p1, TrackSpec[] specs, Approach to, Approach from)
        {
            double len = Geo.Hypot(p1.X - p0.X, p1.Y - p0.Y);
            int n = (int)Math.Ceiling(len);
            var center = new List<Vec>();
            var st = new double[n + 1];
            for (int i = 0; i <= n; i++)
            {
                double t = (double)i / n;
                center.Add(new Vec(p0.X + (p1.X - p0.X) * t, p0.Y + (p1.Y - p0.Y) * t));
                st[i] = len * t;
            }

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
                    SpeedLimit = SpeedLimit,
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

            double h = Math.Atan2(p1.Y - p0.Y, p1.X - p0.X);
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
            world.Surfaces.Add(new Strip { Left = center.ToArray(), Right = Geo.Offset(center, i => right[i]) });
            world.Markings.Add(new Marking { Pts = Geo.Offset(center, i => right[i] - 0.25), Width = 0.14 });
            world.Markings.Add(new Marking { Pts = Geo.Offset(center, i => left[i] + 0.16), Width = 0.12, Yellow = true });

            bool controlled = roadsAt(to.Node) > 2;
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

        private int roadsAt(int node)
        {
            int count = 0;
            foreach (RoadSpec r in roads) if (r.A == node || r.B == node) count++;
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
            world.Markings.Add(new Marking { Pts = part, Width = 0.14, DashOn = dashed ? 3 : 0, DashOff = dashed ? 6 : 0 });
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
            var conns = new List<Lane>();
            foreach (Approach arm in node.Arms)
            {
                // Which arm each turn leads to.
                var target = new int[Lane.Turns];
                for (int t = 0; t < Lane.Turns; t++) target[t] = -1;
                double heading = arm.Angle + Math.PI;
                foreach (Approach other in node.Arms)
                {
                    if (other == arm) continue;
                    Turn turn;
                    if (node.Arms.Count == 2) turn = Turn.Straight; // a bend in the road, not a turn
                    else
                    {
                        double d = Wrap(other.Angle - heading);
                        turn = Math.Abs(d) < Math.PI / 4 ? Turn.Straight : d > 0 ? Turn.Left : Turn.Right;
                    }
                    if (target[(int)turn] < 0) target[(int)turn] = other.Arm;
                }
                if (arm.AllowUTurn && node.Controlled) target[(int)Turn.UTurn] = arm.Arm;

                List<Turn>[] sets = AssignTurns(arm, target);

                for (int t = 0; t < Lane.Turns; t++)
                {
                    if (target[t] < 0) continue;
                    var turn = (Turn)t;
                    var src = new List<int>();
                    for (int i = 0; i < arm.Inbound.Count; i++) if (sets[i].Contains(turn)) src.Add(arm.Inbound[i]);
                    List<int> outs = node.Arms[target[t]].Outbound;
                    if (src.Count > outs.Count && turn != Turn.UTurn)
                    {
                        throw new InvalidOperationException("Node " + node.Id + " arm " + arm.Arm + ": " + src.Count + " lanes turn " + turn + " into " + outs.Count);
                    }
                    for (int k = 0; k < src.Count; k++)
                    {
                        int to;
                        if (turn == Turn.UTurn)
                        {
                            if (k > 0) break;
                            to = outs[outs.Count - 1];
                        }
                        else if (turn == Turn.Right) to = outs[outs.Count - src.Count + k];
                        else to = outs[k];
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
                SpeedLimit = SpeedLimit,
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
                double wIn = (MaxLaneSlot(arm.Inbound) + 1) * LaneW;
                double wOut = (MaxLaneSlot(arm.Outbound) + 1) * LaneW;
                double mx = node.Pos.X + dx * node.Radius, my = node.Pos.Y + dy * node.Radius;
                cw[i] = new Vec(mx + dy * wOut, my - dx * wOut);
                ccw[i] = new Vec(mx - dy * wIn, my + dx * wIn);
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
                if (Math.Abs(cross) < 1e-6) continue;
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
