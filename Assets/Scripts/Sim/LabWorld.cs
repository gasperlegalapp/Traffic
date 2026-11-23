using System;
using System.Collections.Generic;

namespace Traffic.Sim
{
    /// <summary>
    /// The driver lab test track: one four-way intersection whose arms are joined
    /// in pairs by two loops, so cars circulate forever and every turn is possible.
    ///
    /// North and east arms are joined by a plain loop with two lanes each way.
    /// South and west arms are joined by a loop where the right lane ends, the
    /// road runs single-lane for a stretch, then the right lane opens again.
    ///
    /// Traffic drives on the right. Plane: x east, y north, meters.
    /// </summary>
    public static class LabWorld
    {
        public const double LaneW = 3.5;
        /// <summary>Distance from the center of the intersection to each stop line.</summary>
        public const double Box = 12;
        /// <summary>Radius of the loops, and how far the arms reach from the center.</summary>
        private const double Loop = 48;
        /// <summary>30 mph.</summary>
        private const double SpeedLimit = 13.4;
        /// <summary>Share of the loop where the right lane ends, and where it opens again.</summary>
        private const double DropAt = 0.36;
        private const double ReopenAt = 0.62;
        private const double Taper = 18;
        /// <summary>Lane changes are not allowed this close to the stop line.</summary>
        public const double SolidLine = 20;

        private static List<Vec> LoopCenterline(int sign)
        {
            var pts = new List<Vec>();
            for (double y = Box; y < Loop; y += 1) pts.Add(new Vec(0, y));
            int steps = (int)Math.Ceiling(1.5 * Math.PI * Loop);
            for (int i = 0; i <= steps; i++)
            {
                double ang = Math.PI - 1.5 * Math.PI * i / steps;
                pts.Add(new Vec(Loop + Loop * Math.Cos(ang), Loop + Loop * Math.Sin(ang)));
            }
            for (double x = Loop - 1; x >= Box; x -= 1) pts.Add(new Vec(x, 0));
            for (int i = 0; i < pts.Count; i++) pts[i] = new Vec(pts[i].X * sign, pts[i].Y * sign);
            return pts;
        }

        private static double Lerp(double a, double b, double t)
        {
            return a + (b - a) * Math.Min(1, Math.Max(0, t));
        }

        public static World Build()
        {
            var world = new World { LaneWidth = LaneW, Extent = 2 * Loop + 2 * LaneW + 8 };
            var lanes = world.Lanes;

            Lane AddRoadLane(IEnumerable<Vec> pts)
            {
                var path = new LanePath(pts);
                var lane = new Lane { Id = lanes.Count, Kind = LaneKind.Road, Path = path, Length = path.Length, SpeedLimit = SpeedLimit };
                lanes.Add(lane);
                return lane;
            }

            // One direction of a loop: lanes, pavement and paint.
            void BuildDirection(List<Vec> center, bool hasDrop, int fromArm, int toArm, bool paintCenter)
            {
                var st = new double[center.Count];
                for (int i = 1; i < center.Count; i++) st[i] = st[i - 1] + Geo.Hypot(center[i].X - center[i - 1].X, center[i].Y - center[i - 1].Y);
                double total = st[st.Length - 1];
                double drop = total * DropAt;
                double reopen = total * ReopenAt;
                double Edge(int i)
                {
                    if (!hasDrop) return 2 * LaneW;
                    double s = st[i];
                    if (s <= drop) return 2 * LaneW;
                    if (s < drop + Taper) return Lerp(2 * LaneW, LaneW, (s - drop) / Taper);
                    if (s < reopen - Taper) return LaneW;
                    if (s < reopen) return Lerp(LaneW, 2 * LaneW, (s - (reopen - Taper)) / Taper);
                    return 2 * LaneW;
                }
                Vec[] Slice(Vec[] pts, double from, double to)
                {
                    var part = new List<Vec>();
                    for (int i = 0; i < pts.Length; i++) if (st[i] >= from - 1e-6 && st[i] <= to + 1e-6) part.Add(pts[i]);
                    return part.ToArray();
                }

                Vec[] leftPts = Geo.Offset(center, 0.5 * LaneW);
                Vec[] rightPts = Geo.Offset(center, 1.5 * LaneW);
                Lane left = AddRoadLane(leftPts);
                Lane rightStart, rightEnd;
                if (hasDrop)
                {
                    rightStart = AddRoadLane(Slice(rightPts, 0, drop));
                    rightEnd = AddRoadLane(Slice(rightPts, reopen, total));
                    left.Rights.Add(rightStart.Id);
                    left.Rights.Add(rightEnd.Id);
                    rightStart.Lefts.Add(left.Id);
                    rightEnd.Lefts.Add(left.Id);
                }
                else
                {
                    rightStart = rightEnd = AddRoadLane(rightPts);
                    left.Rights.Add(rightStart.Id);
                    rightStart.Lefts.Add(left.Id);
                }
                world.Outbound[fromArm] = new[] { left.Id, rightStart.Id };
                world.Inbound[toArm] = new[] { left.Id, rightEnd.Id };
                left.Arm = toArm;
                rightEnd.Arm = toArm;
                left.Connectors = new[] { -1, -1, -1 };
                rightEnd.Connectors = new[] { -1, -1, -1 };

                world.Surfaces.Add(new Strip { Left = center.ToArray(), Right = Geo.Offset(center, Edge) });
                world.Markings.Add(new Marking { Pts = Geo.Offset(center, i => Edge(i) - 0.25), Width = 0.14 });
                if (paintCenter)
                {
                    world.Markings.Add(new Marking { Pts = Geo.Offset(center, 0.16), Width = 0.12, Yellow = true });
                    world.Markings.Add(new Marking { Pts = Geo.Offset(center, -0.16), Width = 0.12, Yellow = true });
                }
                Vec[] divider = Geo.Offset(center, LaneW);
                void Dashed(double from, double to)
                {
                    world.Markings.Add(new Marking { Pts = Slice(divider, from, to), Width = 0.14, DashOn = 3, DashOff = 6 });
                }
                if (hasDrop)
                {
                    Dashed(0, drop);
                    Dashed(reopen, total - SolidLine);
                }
                else
                {
                    Dashed(0, total - SolidLine);
                }
                world.Markings.Add(new Marking { Pts = Slice(divider, total - SolidLine, total), Width = 0.14 });

                Vec end = center[center.Count - 1];
                Vec before = center[center.Count - 2];
                double h = Math.Atan2(end.Y - before.Y, end.X - before.X);
                double rx = Math.Sin(h);
                double ry = -Math.Cos(h);
                world.StopLines.Add(new StopLine
                {
                    Arm = toArm,
                    A = new Vec(end.X + rx * 0.3, end.Y + ry * 0.3),
                    B = new Vec(end.X + rx * (2 * LaneW - 0.3), end.Y + ry * (2 * LaneW - 0.3)),
                    Post = new Vec(end.X + rx * (2 * LaneW + 1.6), end.Y + ry * (2 * LaneW + 1.6)),
                    H = h,
                });
            }

            // Arms: 0 north, 1 east, 2 south, 3 west.
            List<Vec> ne = LoopCenterline(1);
            List<Vec> sw = LoopCenterline(-1);
            var neBack = new List<Vec>(ne);
            neBack.Reverse();
            var swBack = new List<Vec>(sw);
            swBack.Reverse();
            BuildDirection(ne, false, 0, 1, true);
            BuildDirection(neBack, false, 1, 0, false);
            BuildDirection(sw, true, 2, 3, true);
            BuildDirection(swBack, true, 3, 2, false);

            // Paths through the intersection.
            void Connect(int fromId, int toId, Turn turn)
            {
                Lane from = lanes[fromId];
                Lane to = lanes[toId];
                Pose a = from.Path.At(from.Length);
                Pose b = to.Path.At(0);
                double chord = Geo.Hypot(b.X - a.X, b.Y - a.Y);
                Vec[] pts = turn == Turn.Straight ? new[] { a.Point, b.Point } : Geo.Bezier(a, b, 0.5523 * chord / Math.Sqrt(2));
                var path = new LanePath(pts);
                var lane = new Lane
                {
                    Id = lanes.Count,
                    Kind = LaneKind.Connector,
                    Path = path,
                    Length = path.Length,
                    SpeedLimit = SpeedLimit,
                    From = fromId,
                    Next = toId,
                    Turn = turn,
                    FromArm = from.Arm,
                };
                lane.Preds.Add(fromId);
                lanes.Add(lane);
                from.Connectors[(int)turn] = lane.Id;
                to.Preds.Add(lane.Id);
            }
            for (int arm = 0; arm < 4; arm++)
            {
                int in0 = world.Inbound[arm][0];
                int in1 = world.Inbound[arm][1];
                Connect(in0, world.Outbound[(arm + 1) % 4][0], Turn.Left);
                Connect(in0, world.Outbound[(arm + 2) % 4][0], Turn.Straight);
                Connect(in1, world.Outbound[(arm + 2) % 4][1], Turn.Straight);
                Connect(in1, world.Outbound[(arm + 3) % 4][1], Turn.Right);
            }
            Connectors.Link(lanes);

            // Pavement inside the intersection, with rounded corners.
            double half = 2 * LaneW;
            double r = Box - half;
            double[,] corners = { { 1, 1, Math.PI }, { 1, -1, Math.PI / 2 }, { -1, -1, 0 }, { -1, 1, -Math.PI / 2 } };
            for (int c = 0; c < 4; c++)
            {
                for (int i = 0; i <= 8; i++)
                {
                    double ang = corners[c, 2] + Math.PI / 2 * (i / 8.0);
                    world.Box.Add(new Vec(corners[c, 0] * Box + r * Math.Cos(ang), corners[c, 1] * Box + r * Math.Sin(ang)));
                }
            }
            return world;
        }
    }
}
