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
        /// A small town of six intersections south of a divided highway, joined to it by a
        /// diamond interchange: a crossroad from the middle of town goes over the highway on
        /// a bridge, and on each side of the highway an off-ramp comes up to the crossroad
        /// and an on-ramp goes back down, meeting it at a traffic light. Vehicles arrive at
        /// both ends of the highway and on two roads at the south edge of town, and leave
        /// the map the same ways.
        /// </summary>
        public static World HighwayTown()
        {
            const double highwayY = 215;
            const double highwaySpeed = NetworkBuilder.HighwaySpeed;
            const double rampSpeed = NetworkBuilder.ArterialSpeed;
            const double main = NetworkBuilder.ArterialSpeed;
            var b = new NetworkBuilder();

            // Town: columns at x = -140, 0, 140; rows at y = -30 (north) and -170 (south).
            int nw = b.AddNode(-140, -30, JunctionMode.Free);
            int n = b.AddNode(0, -30, JunctionMode.Signal);
            int ne = b.AddNode(140, -30, JunctionMode.Free);
            int sw = b.AddNode(-140, -170, JunctionMode.AllWay);
            int s = b.AddNode(0, -170, JunctionMode.Signal);
            int se = b.AddNode(140, -170, JunctionMode.AllWay);
            b.AddRoad(nw, n, Two(), Two(), null, main);
            b.AddRoad(n, ne, Two(), Two(), null, main);
            b.AddRoad(sw, s, One(), One());
            b.AddRoad(s, se, One(), One());
            // The two lanes of the north street carry on round each corner, then narrow to one.
            b.AddRoad(sw, nw, One(), new[] { T(0), T(1, 0, 0.6) });
            b.AddRoad(se, ne, One(), new[] { T(0), T(1, 0, 0.6) });
            b.AddRoad(s, n, One(), One());

            // Two roads out of town to the south.
            int southWest = b.AddNode(-140, -300, JunctionMode.Free);
            int southEast = b.AddNode(140, -300, JunctionMode.Free);
            b.AddRoad(southWest, sw, One(), One());
            b.AddRoad(southEast, se, One(), One());

            // The crossroad: north out of town, over the highway on a bridge. It meets the
            // ramps at a traffic light on each side of the highway.
            int southRamps = b.AddNode(0, highwayY - 85, JunctionMode.Signal);
            int northRamps = b.AddNode(0, highwayY + 85, JunctionMode.Signal);
            b.AddRoad(n, southRamps, One(), One(), null, main);
            b.AddRoad(southRamps, northRamps, One(), One(), null, main, 0, 0, true);

            // The highway. Each direction keeps the slot next to the centerline empty, which
            // leaves a median. Through lanes may only go straight on, so that they are never
            // offered the ramps on the far side; an extra lane opens on the right before each
            // exit, and traffic joining gets a lane of its own before it has to merge.
            int westEnd = b.AddNode(-430, highwayY, JunctionMode.Free);
            int west = b.AddNode(-250, highwayY, JunctionMode.Free);
            int east = b.AddNode(250, highwayY, JunctionMode.Free);
            int eastEnd = b.AddNode(430, highwayY, JunctionMode.Free);
            TrackSpec[] through = { T(1, 0, 1, Turn.Straight), T(2, 0, 1, Turn.Straight) };
            TrackSpec[] toExit = { T(1, 0, 1, Turn.Straight), T(2, 0, 1, Turn.Straight), T(3, -110, 1, Turn.Right) };
            TrackSpec[] fromEntry = { T(1), T(2), T(3, 0, 0.75) };
            b.AddRoad(westEnd, west, toExit, fromEntry, null, highwaySpeed);
            b.AddRoad(west, east, through, through, null, highwaySpeed);
            b.AddRoad(east, eastEnd, fromEntry, toExit, null, highwaySpeed);

            // Ramps hang off the outside of the through lanes, not the highway's centerline.
            const double outside = 3 * NetworkBuilder.LaneW;
            const double tilt = 12 * Math.PI / 180;
            TrackSpec[] ramp = { T(0) };
            TrackSpec[] rampOn = { T(0, 0, 1, Turn.Straight) };
            // A point 24 m from a highway node along a heading, moved to the right of that heading by the ramp offset.
            PathPose Off(double x, double heading, double along)
            {
                return new PathPose
                {
                    X = x + along * Math.Cos(heading) + outside * Math.Sin(heading),
                    Y = highwayY + along * Math.Sin(heading) - outside * Math.Cos(heading),
                    H = heading,
                };
            }
            PathPose At(double x, double y, double heading)
            {
                return new PathPose { X = x, Y = y, H = heading };
            }

            // Eastbound side, south of the highway.
            b.AddRoad(west, southRamps, ramp, null, Geo.Bezier(Off(-250, -tilt, 24), At(-34, highwayY - 85, 0), 70, 24), rampSpeed, outside, 0);
            b.AddRoad(southRamps, east, rampOn, null, Geo.Bezier(At(34, highwayY - 85, 0), Off(250, tilt, -24), 70, 24), rampSpeed, 0, outside);
            // Westbound side, north of the highway.
            b.AddRoad(east, northRamps, ramp, null, Geo.Bezier(Off(250, Math.PI - tilt, 24), At(34, highwayY + 85, Math.PI), 70, 24), rampSpeed, outside, 0);
            b.AddRoad(northRamps, west, rampOn, null, Geo.Bezier(At(-34, highwayY + 85, Math.PI), Off(-250, Math.PI + tilt, -24), 70, 24), rampSpeed, 0, outside);

            return b.Build();
        }
    }
}
