using System;
using System.Collections.Generic;

namespace Traffic.Sim
{
    /// <summary>A named map the lab can load.</summary>
    public sealed class MapInfo
    {
        public string Name;
        public string Blurb;
        public Func<World> Build;
        /// <summary>How many vehicles to start with.</summary>
        public int StartingCars;
    }

    public static class Maps
    {
        public static readonly MapInfo[] All =
        {
            new MapInfo { Name = "Street grid", Blurb = "Nine blocks with turn lanes, arrows, stop signs and merges", Build = LabWorld.Build, StartingCars = 20 },
            new MapInfo { Name = "Figure eight", Blurb = "One intersection and two loops", Build = FigureEight, StartingCars = 12 },
            new MapInfo { Name = "City and highway", Blurb = "A small town beside a highway, with traffic arriving and leaving", Build = HighwayTown, StartingCars = 30 },
        };

        private static TrackSpec T(int slot, double from = 0, double to = 1, params Turn[] turns)
        {
            return new TrackSpec(slot, from, to, turns);
        }

        private static TrackSpec[] One()
        {
            return new[] { T(0) };
        }

        private static TrackSpec[] Two()
        {
            return new[] { T(0), T(1) };
        }

        /// <summary>
        /// One four-way intersection whose arms are joined in pairs by two loops, so
        /// traffic circulates for ever. On the south-west loop the right lane ends, the
        /// road runs single-lane for a stretch, then the lane opens again.
        /// </summary>
        public static World FigureEight()
        {
            const double loop = 48;
            const double start = 16;
            var b = new NetworkBuilder();
            int center = b.AddNode(0, 0, JunctionMode.Signal);

            // Out along the north arm, three quarters of the way round a circle, back in along the east arm.
            Vec[] Loop(int sign)
            {
                var pts = new List<Vec>();
                for (double y = start; y < loop; y += 4) pts.Add(new Vec(0, y));
                int steps = 48;
                for (int i = 0; i <= steps; i++)
                {
                    double ang = Math.PI - 1.5 * Math.PI * i / steps;
                    pts.Add(new Vec(loop + loop * Math.Cos(ang), loop + loop * Math.Sin(ang)));
                }
                for (double x = loop - 4; x >= start; x -= 4) pts.Add(new Vec(x, 0));
                for (int i = 0; i < pts.Count; i++) pts[i] = new Vec(pts[i].X * sign, pts[i].Y * sign);
                return pts.ToArray();
            }

            b.AddRoad(center, center, Two(), Two(), Loop(1));
            TrackSpec[] pinched = { T(0), T(1, 0, 0.36), T(1, 0.62, 1) };
            b.AddRoad(center, center, pinched, pinched, Loop(-1));
            return b.Build();
        }

        /// <summary>
        /// A small town of six intersections south of a one-way highway running west to
        /// east. An off-ramp brings highway traffic into the north-west corner of town
        /// and an on-ramp takes it back out from the north-east corner. Vehicles arrive
        /// at the west end of the highway and at two roads on the south edge of town,
        /// and leave the map at the east end of the highway or by those same two roads.
        /// </summary>
        public static World HighwayTown()
        {
            const double highwayY = 215;
            const double highwaySpeed = 26.8; // 60 mph
            const double rampSpeed = 15.6; // 35 mph
            var b = new NetworkBuilder();

            // Town: columns at x = -140, 0, 140; rows at y = 70 (north) and -70 (south).
            int nw = b.AddNode(-140, 70, JunctionMode.Signal);
            int n = b.AddNode(0, 70, JunctionMode.Signal);
            int ne = b.AddNode(140, 70, JunctionMode.Signal);
            int sw = b.AddNode(-140, -70, JunctionMode.AllWay);
            int s = b.AddNode(0, -70, JunctionMode.Signal);
            int se = b.AddNode(140, -70, JunctionMode.AllWay);
            b.AddRoad(nw, n, Two(), Two());
            b.AddRoad(n, ne, Two(), Two());
            b.AddRoad(sw, s, One(), One());
            b.AddRoad(s, se, One(), One());
            // Both westbound lanes of the north street turn down this one, so it starts with two lanes and narrows.
            b.AddRoad(sw, nw, One(), new[] { T(0), T(1, 0, 0.6) });
            b.AddRoad(s, n, One(), One());
            b.AddRoad(se, ne, One(), One());

            // Two roads out of town to the south.
            int southWest = b.AddNode(-140, -215, JunctionMode.Free);
            int southEast = b.AddNode(140, -215, JunctionMode.Free);
            b.AddRoad(southWest, sw, One(), One());
            b.AddRoad(southEast, se, One(), One());

            // The highway: in at the west end, past the off-ramp, past the on-ramp, out at the east end.
            int westEnd = b.AddNode(-430, highwayY, JunctionMode.Free);
            int offRamp = b.AddNode(-250, highwayY, JunctionMode.Free);
            int onRamp = b.AddNode(250, highwayY, JunctionMode.Free);
            int eastEnd = b.AddNode(430, highwayY, JunctionMode.Free);
            // A third lane opens before the off-ramp for traffic leaving.
            b.AddRoad(westEnd, offRamp, new[] { T(0, 0, 1, Turn.Straight), T(1, 0, 1, Turn.Straight), T(2, -110, 1, Turn.Right) }, null, null, highwaySpeed);
            b.AddRoad(offRamp, onRamp, Two(), null, null, highwaySpeed);
            // Traffic from the on-ramp gets a lane of its own to get up to speed, then has to merge.
            b.AddRoad(onRamp, eastEnd, new[] { T(0), T(1), T(2, 0, 0.75) }, null, null, highwaySpeed);

            // Off-ramp: peels away to the right, then curves down into the north-west corner of town.
            // Both ramps hang off the outside of the two through lanes, not the highway's centerline.
            double tilt = 12 * Math.PI / 180;
            const double outside = 2 * NetworkBuilder.LaneW;
            double cos = Math.Cos(tilt), sin = Math.Sin(tilt);
            var leave = new PathPose { X = -250 + 24 * cos - outside * sin, Y = highwayY - 24 * sin - outside * cos, H = -tilt };
            var arrive = new PathPose { X = -140, Y = 70 + 32, H = -Math.PI / 2 };
            b.AddRoad(offRamp, nw, One(), null, Geo.Bezier(leave, arrive, 70, 24), rampSpeed, outside, 0);

            // On-ramp: north out of the north-east corner, then curves right to join the highway.
            var depart = new PathPose { X = 140, Y = 70 + 32, H = Math.PI / 2 };
            var join = new PathPose { X = 250 - 24 * cos + outside * sin, Y = highwayY - 24 * sin - outside * cos, H = tilt };
            b.AddRoad(ne, onRamp, One(), null, Geo.Bezier(depart, join, 70, 24), rampSpeed, 0, outside);

            return b.Build();
        }
    }
}
