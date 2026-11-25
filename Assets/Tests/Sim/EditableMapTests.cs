using System;
using System.Linq;
using NUnit.Framework;
using Traffic.Sim;

namespace Traffic.Tests
{
    public class EditableMapTests
    {
        private static Vec P(double x, double y)
        {
            return new Vec(x, y);
        }

        /// <summary>Two roads crossing at the origin.</summary>
        private static EditableMap Cross(int lanes = 2, int mph = 40)
        {
            var map = new EditableMap();
            Assert.IsNull(map.TryAdd(P(-200, 0), P(200, 0), lanes, mph, true));
            Assert.IsNull(map.TryAdd(P(0, -200), P(0, 200), lanes, mph, true));
            return map;
        }

        [Test]
        public void DrawingAcrossARoadMakesAnIntersection()
        {
            EditableMap map = Cross();
            Assert.AreEqual(5, map.Nodes.Count, "four ends and the crossing");
            Assert.AreEqual(4, map.Segments.Count);
            World world = map.ToWorld();
            Node crossing = world.Nodes.Single(n => n.Arms.Count == 4);
            Assert.AreEqual(JunctionMode.Signal, crossing.DefaultMode);
            Assert.AreEqual(4, world.Entries.Count, "each open road end brings traffic in");
        }

        [Test]
        public void QuietStreetsGetStopSignsAndFasterRoadsGetALight()
        {
            Assert.AreEqual(JunctionMode.AllWay, Cross(2, 25).ToWorld().Nodes.Single(n => n.Arms.Count == 4).DefaultMode);
            Assert.AreEqual(JunctionMode.Signal, Cross(4, 40).ToWorld().Nodes.Single(n => n.Arms.Count == 4).DefaultMode);
        }

        [Test]
        public void DrawingFromTheMiddleOfARoadSplitsIt()
        {
            var map = new EditableMap();
            Assert.IsNull(map.TryAdd(P(-200, 0), P(200, 0), 2, 40, true));
            Assert.IsNull(map.TryAdd(P(0, 3), P(0, 180), 2, 40, true)); // starts a few meters off the road: snaps onto it
            Assert.AreEqual(4, map.Nodes.Count);
            Assert.AreEqual(3, map.Segments.Count);
            Assert.AreEqual(1, map.ToWorld().Nodes.Count(n => n.Arms.Count == 3));
        }

        [Test]
        public void EndsSnapTogetherSoRoadsJoinUp()
        {
            var map = new EditableMap();
            Assert.IsNull(map.TryAdd(P(0, 0), P(150, 0), 2, 25, true));
            Assert.IsNull(map.TryAdd(P(154, 4), P(150, 160), 2, 25, true)); // starts near the first road's end
            Assert.AreEqual(3, map.Nodes.Count);
            World world = map.ToWorld();
            Assert.AreEqual(2, world.Nodes.Count, "the two stretches are one road, from one open end to the other");
            Assert.AreEqual(2, world.Lanes.Count(l => l.Kind == LaneKind.Road));
        }

        [Test]
        public void RefusesRoadsThatAreTooShortTooSharpOrDoubled()
        {
            var map = new EditableMap();
            Assert.IsNull(map.TryAdd(P(0, 0), P(200, 0), 2, 40, true));
            Assert.IsNotNull(map.TryAdd(P(300, 300), P(310, 300), 2, 40, true), "too short");
            Assert.IsNotNull(map.TryAdd(P(0, 0), P(200, 30), 2, 40, true), "too sharp an angle with the first road");
            Assert.IsNotNull(map.TryAdd(P(0, 0), P(200, 0), 2, 40, true), "already there");
            Assert.AreEqual(1, map.Segments.Count, "refused roads leave the map unchanged");
            Assert.AreEqual(2, map.Nodes.Count);
        }

        [Test]
        public void PreviewingDoesNotChangeTheMap()
        {
            EditableMap map = Cross();
            string before = map.Serialize();
            Assert.IsNull(map.TryAdd(P(200, 0), P(200, 200), 2, 40, false));
            Assert.AreEqual(before, map.Serialize());
        }

        [Test]
        public void BulldozingARoadRemovesItAndAnyPointLeftUnused()
        {
            EditableMap map = Cross();
            Assert.IsTrue(map.RemoveNear(P(100, 2)));
            Assert.AreEqual(3, map.Segments.Count);
            Assert.AreEqual(4, map.Nodes.Count);
            Assert.IsFalse(map.RemoveNear(P(500, 500)));
            map.ToWorld();
        }

        [Test]
        public void SavesAndLoads()
        {
            EditableMap map = Cross(4, 55);
            EditableMap loaded = EditableMap.Parse(map.Serialize());
            Assert.AreEqual(map.Serialize(), loaded.Serialize());
            Assert.AreEqual(0, EditableMap.Parse("nonsense").Segments.Count);
            Assert.AreEqual(0, EditableMap.Parse("").Segments.Count);
        }

        [Test]
        public void ACurveIsPartOfACircleThroughThePointGiven()
        {
            var map = new EditableMap();
            // Half a circle centered on (100, 100): from the south-west, round by the south-east, to the north-east.
            Assert.IsNull(map.TryAdd(P(0, 0), P(200, 200), 2, 40, true, P(200, 0)));
            Assert.AreEqual(1, map.Segments.Count);
            Assert.IsTrue(map.Segments[0].Curved);
            World world = map.ToWorld();
            double radius = Math.Sqrt(2) * 100, furthest = 0;
            foreach (Lane lane in world.Lanes.Where(l => l.Kind == LaneKind.Road))
            {
                for (double d = 0; d <= lane.Length; d += 5)
                {
                    PathPose at = lane.Path.At(d);
                    // Each lane keeps half a lane's width from the middle of the road, all the way round.
                    Assert.AreEqual(radius, Geo.Hypot(at.X - 100, at.Y - 100), NetworkBuilder.LaneW / 2 + 0.3);
                    furthest = Math.Max(furthest, (at.X - at.Y) / Math.Sqrt(2));
                }
            }
            Assert.Greater(furthest, 130, "the road should reach the point it was pulled through");
        }

        [Test]
        public void APointOnTheLineJustMakesAStraightRoad()
        {
            var map = new EditableMap();
            Assert.IsNull(map.TryAdd(P(0, 0), P(200, 0), 2, 40, true, P(100, 0.3)));
            Assert.IsFalse(map.Segments[0].Curved);
        }

        [Test]
        public void ACurveThatCrossesARoadMakesAnIntersectionAndStaysCurved()
        {
            var map = new EditableMap();
            Assert.IsNull(map.TryAdd(P(-250, 60), P(250, 60), 2, 40, true));
            Assert.IsNull(map.TryAdd(P(-200, -150), P(200, -150), 2, 40, true, P(0, 150))); // an arch that crosses the road twice
            Assert.AreEqual(6, map.Nodes.Count, "four road ends and the two places they cross");
            Assert.AreEqual(6, map.Segments.Count);
            Assert.AreEqual(3, map.Segments.Count(s => s.Curved));
            World world = map.ToWorld();
            Assert.AreEqual(2, world.Nodes.Count(n => n.Arms.Count == 4));
        }

        [Test]
        public void DrawingFromTheMiddleOfACurveSplitsItIntoTwoCurves()
        {
            var map = new EditableMap();
            Assert.IsNull(map.TryAdd(P(0, 0), P(300, 300), 2, 25, true, P(300, 0)));
            Vec onCurve = EditableMap.CurvePoint(P(0, 0), P(300, 0), P(300, 300), 0.5);
            Assert.IsNull(map.TryAdd(onCurve, P(onCurve.X + 120, onCurve.Y - 120), 2, 25, true));
            Assert.AreEqual(3, map.Segments.Count);
            Assert.AreEqual(2, map.Segments.Count(s => s.Curved));
            Assert.AreEqual(1, map.ToWorld().Nodes.Count(n => n.Arms.Count == 3));
            // Both halves still lie on the circle the whole curve was drawn on.
            foreach (EditSegment s in map.Segments.Where(s => s.Curved))
            {
                foreach (Vec pt in map.Trace(s)) Assert.AreEqual(150 * Math.Sqrt(2), Geo.Hypot(pt.X - 150, pt.Y - 150), 0.5);
            }
        }

        [Test]
        public void RefusesACurveThatIsTooTight()
        {
            var map = new EditableMap();
            StringAssert.StartsWith("That curve is too tight", map.TryAdd(P(0, 0), P(40, 0), 2, 25, true, P(20, 20)));
            Assert.AreEqual(0, map.Segments.Count);
        }

        [Test]
        public void RefusesACurveThatGoesNearlyAllTheWayRound()
        {
            var map = new EditableMap();
            Assert.IsNotNull(map.TryAdd(P(0, 0), P(50, 0), 2, 25, true, P(25, 200)));
            Assert.AreEqual(0, map.Segments.Count);
        }

        [Test]
        public void SavesAndLoadsCurves()
        {
            var map = new EditableMap();
            Assert.IsNull(map.TryAdd(P(0, 0), P(200, 200), 4, 55, true, P(200, 0)));
            EditableMap loaded = EditableMap.Parse(map.Serialize());
            Assert.AreEqual(map.Serialize(), loaded.Serialize());
            Assert.IsTrue(loaded.Segments[0].Curved);
        }

        [Test]
        public void StillLoadsACurveSavedBeforeCurvesBecameRound()
        {
            // The older kind of curve bent toward a corner point, here (200, 0), and passed halfway to it.
            EditableMap loaded = EditableMap.Parse("0.0,0.0;200.0,200.0;|0,1,4,55,200.0,0.0;");
            Assert.AreEqual(1, loaded.Segments.Count);
            Assert.IsTrue(loaded.Segments[0].Curved);
            Assert.AreEqual(150, loaded.Segments[0].Mid.X, 0.01);
            Assert.AreEqual(50, loaded.Segments[0].Mid.Y, 0.01);
        }

        /// <summary>A drawn map made mostly of curves: a winding main road, a loop off it, and a curved one-way cut-through.</summary>
        public static EditableMap CurvyTown()
        {
            var map = new EditableMap();
            Action<Vec, Vec, int, int, Vec?> add = (p, q, lanes, mph, c) => Assert.IsNull(map.TryAdd(p, q, lanes, mph, true, c), "from " + p.X + "," + p.Y + " to " + q.X + "," + q.Y);
            // The main road winds across the map in two bends.
            Vec west = P(-350, -80), middle = P(-60, 40), east = P(350, -40);
            Vec bendOne = P(-200, 80), bendTwo = P(130, -60);
            add(west, middle, 4, 40, bendOne);
            add(middle, east, 4, 40, bendTwo);
            // A street loops north from the middle of it and comes back down further east.
            Vec top = P(60, 300), corner = P(300, 120), loopBend = P(230, 265);
            add(middle, top, 2, 25, P(40, 150));
            add(top, corner, 2, 25, loopBend);
            add(corner, EditableMap.CurvePoint(middle, bendTwo, east, 0.7), 2, 25, null);
            // A curved road comes in from the south-west and joins the first bend.
            add(P(-250, -300), EditableMap.CurvePoint(west, bendOne, middle, 0.45), 2, 40, P(-165, -125));
            // A one-way street leaves the loop, crosses the main road and runs off the map to the south.
            add(EditableMap.CurvePoint(top, loopBend, corner, 0.45), P(140, -300), 1, 25, P(115, 0));
            return map;
        }

        [Test]
        public void ACurvyTownBuildsAndCarriesTraffic()
        {
            EditableMap map = CurvyTown();
            Assert.Greater(map.Segments.Count(s => s.Curved), 6);
            World world = map.ToWorld();
            Assert.GreaterOrEqual(world.Nodes.Count(n => n.Controlled), 4);
            var sim = new Simulation(world, 4);
            sim.ArrivalsPerMinute = 5;
            Action obey = () =>
            {
                foreach (Car c in sim.Cars) c.Drv.Compliance = 1;
            };
            Grid.Run(sim, 300, obey);
            int leftByHalfway = sim.Left;
            Grid.Run(sim, 300, obey);
            Assert.AreEqual(0, sim.Crashes);
            Assert.Greater(leftByHalfway, 30);
            Assert.Greater(sim.Left - leftByHalfway, 30);
        }

        // ---------------------------------------------------------------- road types

        private static RoadType T(string id)
        {
            return RoadType.ById(id);
        }

        [Test]
        public void EveryRoadTypeBuildsWithItsLanesCenteredOnTheLineDrawn()
        {
            foreach (RoadType type in RoadType.All)
            {
                var map = new EditableMap();
                Assert.IsNull(map.TryAdd(P(-150, 0), P(150, 0), type, true), type.Name);
                World world = map.ToWorld();
                var lanes = world.Lanes.Where(l => l.Kind == LaneKind.Road).ToList();
                Assert.AreEqual(type.Lanes, lanes.Count, type.Name);
                // Eastbound lanes lie south of the line and westbound lanes north, or all around it when one-way.
                double lowest = lanes.Min(l => l.Path.At(l.Length / 2).Y), highest = lanes.Max(l => l.Path.At(l.Length / 2).Y);
                Assert.AreEqual(0, (lowest + highest) / 2, 0.01, type.Name + " is not centered");
                Assert.AreEqual(type.Width - NetworkBuilder.LaneW, highest - lowest, 0.01, type.Name + " is not as wide as it says");
                Assert.AreEqual(type.OneWay ? 0 : type.Backward, lanes.Count(l => Math.Cos(l.Path.At(l.Length / 2).H) < 0), type.Name);
                Assert.AreEqual(type.Median > 0 && !type.OneWay ? 1 : 0, world.Medians.Count, type.Name);
            }
        }

        [Test]
        public void CostIsLengthTimesThePriceOfEachRoad()
        {
            var map = new EditableMap();
            Assert.IsNull(map.TryAdd(P(0, 0), P(200, 0), T("a4"), true));
            Assert.IsNull(map.TryAdd(P(200, 0), P(200, 100), T("s2"), true));
            Assert.AreEqual(200 * 6 + 100 * 2, map.Cost(), 0.5);
            double length;
            Assert.AreEqual(300, EditableMap.CostOf(P(0, 0), P(0, 100), T("h1"), null, out length), 0.5);
            Assert.AreEqual(100, length, 0.01);
        }

        [Test]
        public void AFastRoadNeedsAWiderCurveThanASlowOne()
        {
            // A quarter circle of radius 40 m: fine for a street, too tight for a highway.
            var map = new EditableMap();
            Vec mid = P(40 - 40 * Math.Cos(Math.PI / 4), 40 * Math.Sin(Math.PI / 4));
            Assert.IsNull(map.TryAdd(P(0, 0), P(40, 40), T("s2"), false, mid));
            Assert.IsNotNull(map.TryAdd(P(0, 0), P(40, 40), T("h2"), false, mid));
        }

        [Test]
        public void SavesAndLoadsRoadTypesAndStillLoadsPlainRoads()
        {
            var map = new EditableMap();
            Assert.IsNull(map.TryAdd(P(0, 0), P(200, 0), T("a4"), true));
            Assert.IsNull(map.TryAdd(P(200, 0), P(300, 200), T("h3"), true, P(290, 80)));
            Assert.IsNull(map.TryAdd(P(0, 0), P(0, 200), 2, 25, true));
            EditableMap loaded = EditableMap.Parse(map.Serialize());
            Assert.AreEqual(map.Serialize(), loaded.Serialize());
            Assert.AreEqual("a4", loaded.Segments[0].Type.Id);
            Assert.AreEqual(3, loaded.Segments[1].Type.Forward);
            Assert.IsTrue(loaded.Segments[1].Curved);
            Assert.AreEqual(25, loaded.Segments[2].Mph);
        }

        /// <summary>A town drawn with the listed road types: a divided avenue, streets, a one-way pair and a curved highway.</summary>
        public static EditableMap TypedTown()
        {
            var map = new EditableMap();
            Action<Vec, Vec, string, Vec?> add = (p, q, id, via) => Assert.IsNull(map.TryAdd(p, q, T(id), true, via), id + " from " + p.X + "," + p.Y + " to " + q.X + "," + q.Y);
            add(P(-380, 0), P(380, 0), "a4", null);             // a wide divided avenue across the middle
            add(P(-200, -280), P(-200, 280), "a2", null);       // a divided avenue crossing it
            add(P(120, -280), P(120, 280), "s2", null);         // a street crossing it
            add(P(-200, 160), P(120, 160), "s2", null);         // a street joining the two, north of the avenue
            add(P(120, -160), P(-200, -160), "a1x2", null);     // two lanes one-way, westbound, south of it
            add(P(-380, 250), P(-200, 280), "h2", P(-290, 285)); // a curved one-way highway arriving from the north-west
            add(P(120, 280), P(380, 200), "s1", P(260, 270));   // a one-way street curving away to the east
            return map;
        }

        [TestCase(2u)]
        [TestCase(7u)]
        public void TrafficRunsOnATownOfEveryKindOfRoad(uint seed)
        {
            EditableMap map = TypedTown();
            World world = map.ToWorld();
            Assert.GreaterOrEqual(world.Nodes.Count(n => n.Controlled), 5);
            Assert.GreaterOrEqual(world.Medians.Count, 2, "the avenues should have medians");
            foreach (Lane lane in world.Lanes)
            {
                if (lane.Kind != LaneKind.Road || lane.IsExit) continue;
                Assert.IsTrue(lane.Connectors != null && lane.Connectors.Any(c => c >= 0), "lane " + lane.Id + " leads nowhere");
            }
            var sim = new Simulation(world, seed);
            sim.ArrivalsPerMinute = 5;
            Action obey = () =>
            {
                foreach (Car c in sim.Cars) c.Drv.Compliance = 1;
            };
            Grid.Run(sim, 300, obey);
            int leftByHalfway = sim.Left;
            Grid.Run(sim, 300, obey);
            Assert.AreEqual(0, sim.Crashes);
            Assert.Greater(leftByHalfway, 40);
            Assert.Greater(sim.Left - leftByHalfway, 40);
        }

        // ---------------------------------------------------------------- merges and exits

        /// <summary>A two-lane highway running east, with a ramp curving in from the south-west and another peeling off to the south-east.</summary>
        public static EditableMap HighwayWithRamps(string road = "h2", string ramp = "h1")
        {
            var map = new EditableMap();
            Assert.IsNull(map.TryAdd(P(-420, 0), P(420, 0), T(road), true));
            // The on-ramp arrives almost alongside; the off-ramp leaves the same way.
            Assert.IsNull(map.TryAdd(P(-380, -150), P(-150, 0), T(ramp), true, P(-250, -45)), "on-ramp");
            Assert.IsNull(map.TryAdd(P(150, 0), P(380, -150), T(ramp), true, P(250, -45)), "off-ramp");
            return map;
        }

        [Test]
        public void AOneWayRoadJoiningAlongsideMakesAMergeNotAnIntersection()
        {
            World world = HighwayWithRamps().ToWorld();
            Assert.AreEqual(0, world.Nodes.Count(n => n.Controlled), "a merge has no lights or stop signs");
            Assert.AreEqual(2, world.Nodes.Count(n => n.Arms.Count == 3));
            // Past the merge the highway has a third lane for a while, which then ends.
            Node merge = world.Nodes.First(n => n.Arms.Count == 3 && n.Pos.X < 0);
            var leaving = world.Lanes.Where(l => l.Kind == LaneKind.Road && Math.Abs(l.Path.At(0).X - (merge.Pos.X + NetworkBuilder.FreeRadius)) < 1.5 && Math.Abs(l.Path.At(0).Y) < 12).ToList();
            Assert.AreEqual(3, leaving.Count, "two through lanes and the lane the ramp brings");
            Lane added = leaving.OrderBy(l => l.Path.At(0).Y).First();
            Assert.IsNull(added.Connectors, "the added lane ends before the next junction");
            Assert.AreEqual(1, added.Preds.Count, "only the ramp feeds the added lane");
            // The ramp comes in beside the highway's lanes, not into the middle of them.
            Lane ramp = world.Lanes[world.Lanes[added.Preds[0]].From];
            PathPose end = ramp.Path.At(ramp.Length);
            Assert.AreEqual(added.Path.At(0).Y, end.Y, 1.0, "the ramp should line up with the lane it feeds");
            Assert.Less(Math.Abs(end.H), 0.3, "the ramp should arrive nearly alongside");
        }

        [Test]
        public void TrafficMergesInAndExitsWithoutCrashing()
        {
            foreach (string road in new[] { "h2", "h3", "a4" })
            {
                var sim = new Simulation(HighwayWithRamps(road).ToWorld(), 5);
                sim.ArrivalsPerMinute = 7;
                var seenOnOffRamp = new System.Collections.Generic.HashSet<int>();
                Action watch = () =>
                {
                    foreach (Car c in sim.Cars)
                    {
                        c.Drv.Compliance = 1;
                        if (c.X > 260 && c.Y < -40) seenOnOffRamp.Add(c.Id);
                    }
                };
                Grid.Run(sim, 300, watch);
                int leftByHalfway = sim.Left;
                Grid.Run(sim, 300, watch);
                Assert.AreEqual(0, sim.Crashes, road);
                Assert.Greater(leftByHalfway, 60, road);
                Assert.Greater(sim.Left - leftByHalfway, 60, road);
                Assert.Greater(seenOnOffRamp.Count, 8, road + ": some traffic should take the exit");
            }
        }

        [Test]
        public void ARoadRunningInAgainstTheFlowIsRefusedAndATwoWayRoadCannotMerge()
        {
            var map = new EditableMap();
            Assert.IsNull(map.TryAdd(P(-420, 0), P(420, 0), T("h2"), true));
            // Running in against the traffic.
            Assert.IsNotNull(map.TryAdd(P(380, -150), P(150, 0), T("h1"), true, P(250, -45)));
            // A two-way road cannot merge at all.
            Assert.IsNotNull(map.TryAdd(P(-380, -150), P(-150, 0), T("s2"), true, P(-250, -45)));
            Assert.AreEqual(1, map.Segments.Count);
        }

        [Test]
        public void ARoadCanJoinFromTheLeftAndItsTrafficGivesWay()
        {
            // An eastbound highway, and a one-lane road that comes down from the north to join it on its left.
            var map = new EditableMap();
            Assert.IsNull(map.TryAdd(P(-420, 0), P(420, 0), T("h2"), true));
            Assert.IsNull(map.TryAdd(P(-380, 150), P(-150, 0), T("h1"), true, P(-250, 45)));
            Assert.AreEqual(0, map.Faults().Count);
            World world = map.ToWorld();
            Assert.AreEqual(0, world.Nodes.Count(n => n.Controlled), "no light: the roads just come together");
            Node join = world.Nodes.Single(n => n.Arms.Count == 3);
            var paths = world.Lanes.Where(l => l.Kind == LaneKind.Connector && l.Node == join.Id).ToList();
            Assert.AreEqual(3, paths.Count);
            Lane fromSide = paths.Single(c => world.Lanes[c.From].Path.At(0).Y > 50);
            Assert.IsTrue(fromSide.GiveWay, "the road joining gives way");
            Assert.AreEqual(0, paths.Count(c => c != fromSide && c.GiveWay), "and the highway does not");
            Lane leftLane = world.Lanes.Where(l => l.Kind == LaneKind.Road && l.Path.At(0).X > join.Pos.X && l.Preds.Count > 0).OrderByDescending(l => l.Path.At(0).Y).First();
            Assert.AreEqual(leftLane.Id, fromSide.Next, "it comes into the left lane, the one on its own side");

            var sim = new Simulation(world, 2);
            sim.ArrivalsPerMinute = 26;
            Grid.Run(sim, 420);
            Assert.AreEqual(0, sim.Crashes);
            Assert.Greater(sim.Left, 80);
        }

        [Test]
        public void ASteepJoinIsStillAnIntersection()
        {
            var map = new EditableMap();
            Assert.IsNull(map.TryAdd(P(-300, 0), P(300, 0), T("a4"), true));
            Assert.IsNull(map.TryAdd(P(0, -250), P(0, 0), T("s1"), true));
            Assert.AreEqual(1, map.ToWorld().Nodes.Count(n => n.Controlled));
        }

        // ---------------------------------------------------------------- roundabouts

        /// <summary>Two streets crossing, with a roundabout dropped on the crossing.</summary>
        public static EditableMap CrossroadsRoundabout(string road = "s2", double radius = 28)
        {
            var map = new EditableMap();
            Assert.IsNull(map.TryAdd(P(-300, 0), P(300, 0), T(road), true));
            Assert.IsNull(map.TryAdd(P(0, -300), P(0, 300), T(road), true));
            Assert.IsNull(map.TryAddRoundabout(P(0, 0), radius, T(road), true));
            return map;
        }

        [Test]
        public void ARoundaboutCutsTheRoadsThatCrossItAndJoinsThemToARing()
        {
            EditableMap map = CrossroadsRoundabout();
            Assert.AreEqual(4, map.Segments.Count(s => s.Ring));
            Assert.AreEqual(4, map.Segments.Count(s => !s.Ring), "the four roads outside; the crossing inside is gone");
            foreach (EditSegment s in map.Segments.Where(s => s.Ring))
            {
                Assert.IsTrue(s.Type.OneWay);
                // Counter-clockwise, as traffic that drives on the right goes round.
                Vec a = map.Nodes[s.A], b = map.Nodes[s.B];
                Assert.Greater(a.X * b.Y - a.Y * b.X, 0);
                foreach (Vec pt in map.Trace(s)) Assert.AreEqual(28, Geo.Hypot(pt.X, pt.Y), 0.05);
            }
            World world = map.ToWorld();
            Assert.AreEqual(0, world.Nodes.Count(n => n.Controlled), "no lights or stop signs on a roundabout");
            Assert.AreEqual(4, world.Nodes.Count(n => n.Arms.Count == 3));
            EditableMap loaded = EditableMap.Parse(map.Serialize());
            Assert.AreEqual(map.Serialize(), loaded.Serialize());
            Assert.AreEqual(4, loaded.Segments.Count(s => s.Ring));
        }

        [Test]
        public void ARoundaboutOnOpenGroundIsAClosedRingARoadCanJoinLater()
        {
            var map = new EditableMap();
            Assert.IsNull(map.TryAddRoundabout(P(0, 0), 40, T("a2"), true));
            Assert.GreaterOrEqual(map.Segments.Count, 3);
            Assert.IsTrue(map.Segments.All(s => s.Ring));
            Assert.IsNull(map.TryAdd(P(300, 0), P(40, 0), T("a2"), true));
            Assert.AreEqual(1, map.Segments.Count(s => !s.Ring));
            Assert.AreEqual(0, map.ToWorld().Nodes.Count(n => n.Controlled));
            Assert.IsNotNull(map.TryAddRoundabout(P(0, 0), 5, T("s2"), false), "too small");
            Assert.IsNotNull(map.TryAddRoundabout(P(30, 30), 40, T("s2"), false), "on top of another");
        }

        [TestCase("s2", 28.0)]
        [TestCase("a2", 40.0)]
        [TestCase("a4", 45.0)]
        public void TrafficGoesRoundARoundaboutGivingWayToThoseOnIt(string road, double radius)
        {
            var sim = new Simulation(CrossroadsRoundabout(road, radius).ToWorld(), 3);
            sim.ArrivalsPerMinute = 5;
            double longestStop = 0;
            var stopped = new System.Collections.Generic.Dictionary<int, double>();
            Action watch = () =>
            {
                foreach (Car c in sim.Cars)
                {
                    c.Drv.Compliance = 1;
                    double t;
                    stopped.TryGetValue(c.Id, out t);
                    t = c.V < 0.1 ? t + Simulation.Dt : 0;
                    stopped[c.Id] = t;
                    longestStop = Math.Max(longestStop, t);
                }
            };
            Grid.Run(sim, 300, watch);
            int leftByHalfway = sim.Left;
            Grid.Run(sim, 300, watch);
            Assert.AreEqual(0, sim.Crashes);
            Assert.Greater(leftByHalfway, 40);
            Assert.Greater(sim.Left - leftByHalfway, 40);
            Assert.Less(longestStop, 120, "a vehicle sat still for two minutes");
        }

        // ---------------------------------------------------------------- bridges and tunnels

        /// <summary>An avenue running east-west, with a street carried over it on a bridge and another taken under it in a tunnel.</summary>
        public static EditableMap OverAndUnder()
        {
            var map = new EditableMap();
            Assert.IsNull(map.TryAdd(P(-350, 0), P(350, 0), T("a4"), true));
            Assert.IsNull(map.TryAdd(P(-150, -200), P(-150, 200), T("s2"), true, null, 1), "bridge");
            Assert.IsNull(map.TryAdd(P(150, -200), P(150, 200), T("s2"), true, null, -1), "tunnel");
            return map;
        }

        [Test]
        public void ABridgeOrTunnelCrossesARoadWithoutMeetingIt()
        {
            EditableMap map = OverAndUnder();
            Assert.AreEqual(3, map.Segments.Count, "nothing was cut");
            World world = map.ToWorld();
            Assert.AreEqual(0, world.Nodes.Count(n => n.Arms.Count > 2), "no intersections");
            Lane up = world.Lanes.First(l => l.Kind == LaneKind.Road && l.Peak > 0), down = world.Lanes.First(l => l.Kind == LaneKind.Road && l.Peak < 0);
            Assert.AreEqual(5, up.HeightAt(up.Length / 2), 0.01);
            Assert.AreEqual(-5, down.HeightAt(down.Length / 2), 0.01);
            Assert.AreEqual(0, up.HeightAt(0), 0.01);
            Assert.AreEqual(5, up.HeightAt(45), 0.01, "level after a 40 m ramp");
            Assert.IsTrue(world.Surfaces.Any(s => s.Heights != null && s.Heights.Max() > 4.9));
            Assert.IsTrue(world.Surfaces.Any(s => s.Heights != null && s.Heights.Min() < -4.9));
            EditableMap loaded = EditableMap.Parse(map.Serialize());
            Assert.AreEqual(map.Serialize(), loaded.Serialize());
            Assert.AreEqual(1, loaded.Segments.Count(s => s.Level == 1));
            Assert.AreEqual(1, loaded.Segments.Count(s => s.Level == -1));
        }

        [Test]
        public void HeightHasRules()
        {
            var map = new EditableMap();
            Assert.IsNull(map.TryAdd(P(-350, 0), P(350, 0), T("a4"), true));
            // Too short to get up and down again.
            StringAssert.StartsWith("Too short to climb", map.TryAdd(P(0, -40), P(0, 40), T("s2"), true, null, 1));
            // Crossing while still on the ramp, too near the ground.
            StringAssert.StartsWith("Not enough room", map.TryAdd(P(0, -10), P(0, 200), T("s2"), true, null, 1));
            // A higher bridge needs a longer run.
            Assert.IsNotNull(map.TryAdd(P(0, -100), P(0, 100), T("s2"), true, null, 3));
            Assert.IsNull(map.TryAdd(P(0, -130), P(0, 130), T("s2"), true, null, 3));
            // Nothing may join a bridge part-way along.
            StringAssert.Contains("only join a bridge or tunnel at its ends", map.TryAdd(P(200, 60), P(0, 60), T("s2"), true));
            // A second bridge over the same spot must be a level higher or lower than the first.
            Assert.IsNotNull(map.TryAdd(P(-130, 10), P(130, 10), T("s2"), true, null, 3));
            Assert.IsNull(map.TryAdd(P(-130, 10), P(130, 10), T("s2"), false, null, 2));
            // A bridge costs more than the same road on the ground, and a tunnel more again.
            double length;
            Assert.AreEqual(2 * 200 * 2, EditableMap.CostOf(P(0, 0), P(0, 200), T("s2"), null, out length, 1), 0.5);
            Assert.AreEqual(3 * 200 * 2, EditableMap.CostOf(P(0, 0), P(0, 200), T("s2"), null, out length, -1), 0.5);
        }

        [Test]
        public void TrafficPassesOverAndUnderWithoutCrashing()
        {
            var sim = new Simulation(OverAndUnder().ToWorld(), 4);
            sim.ArrivalsPerMinute = 6;
            bool sawOver = false, sawUnder = false;
            Action watch = () =>
            {
                foreach (Car c in sim.Cars)
                {
                    c.Drv.Compliance = 1;
                    if (c.Height > 4) sawOver = true;
                    if (c.Height < -4) sawUnder = true;
                }
            };
            Grid.Run(sim, 300, watch);
            Assert.AreEqual(0, sim.Crashes);
            Assert.Greater(sim.Left, 80);
            Assert.IsTrue(sawOver && sawUnder);
        }

        // ---------------------------------------------------------------- routes

        [Test]
        public void RoutesKnowWhichEdgesOfTheMapCanReachWhichOthers()
        {
            // A highway running east with an exit but no way on: the side road can be reached, but nothing can come from it.
            var map = new EditableMap();
            Assert.IsNull(map.TryAdd(P(-420, 0), P(420, 0), T("h2"), true));
            Assert.IsNull(map.TryAdd(P(150, 0), P(380, -150), T("h1"), true, P(250, -45)));
            World world = map.ToWorld();
            int west = world.Nodes.First(n => n.Terminal && n.Pos.X < -400).Id, east = world.Nodes.First(n => n.Terminal && n.Pos.X > 400).Id, side = world.Nodes.First(n => n.Terminal && n.Pos.Y < -100).Id;
            var routes = new Routes(world);
            Assert.IsTrue(routes.Connected(west, east));
            Assert.IsTrue(routes.Connected(west, side));
            Assert.IsFalse(routes.Connected(east, west), "the highway is one-way");
            Assert.IsFalse(routes.Connected(side, east), "the ramp only leads away");
            Assert.AreEqual(840 / 29.1, routes.Best(west, east), 1.5, "seconds along the highway at 65 mph");
        }

        [TestCase(1u)]
        [TestCase(2u)]
        public void DriversWithSomewhereToGoGetThere(uint seed)
        {
            var sim = new Simulation(TypedTown().ToWorld(), seed);
            sim.UseRoutes = true;
            sim.ArrivalsPerMinute = 5;
            Action obey = () =>
            {
                foreach (Car c in sim.Cars) c.Drv.Compliance = 1;
            };
            Grid.Run(sim, 600, obey);
            Assert.AreEqual(0, sim.Crashes);
            Assert.Greater(sim.Arrived, 150);
            // A few miss a turn they could not get over for in time, and on this map of one-way roads cannot get back.
            Assert.Less(sim.Strays, sim.Arrived / 8, "nearly everyone should leave where they meant to");
            Assert.Greater(sim.TripSeconds, sim.TripIdeal, "a real drive is never quicker than an empty road");
        }

        [Test]
        public void TrafficFollowsTheDemandItIsGiven()
        {
            World world = CrossroadsRoundabout().ToWorld();
            int west = world.Nodes.First(n => n.Terminal && n.Pos.X < -200).Id, north = world.Nodes.First(n => n.Terminal && n.Pos.Y > 200).Id;
            var sim = new Simulation(world, 6);
            sim.UseRoutes = true;
            sim.Demand.Add(new Trip { From = west, To = north, Weight = 1 });
            sim.ArrivalsPerMinute = 8;
            int elsewhere = 0;
            Action watch = () =>
            {
                foreach (Car c in sim.Cars) if (c.Dest != north) elsewhere++;
            };
            Grid.Run(sim, 240, watch);
            Assert.AreEqual(0, elsewhere, "only the west to north trip was asked for");
            Assert.Greater(sim.Arrived, 15);
            Assert.AreEqual(0, sim.Strays);
        }

        [Test]
        public void ARoadCanBeReplacedWithAnotherKind()
        {
            var map = new EditableMap();
            Assert.IsNull(map.TryAdd(P(-200, 0), P(200, 0), T("s2"), true));
            Assert.IsNull(map.TryAdd(P(0, -200), P(0, 0), T("s2"), true));
            double before = map.Cost();
            // Widen the western half of the main road into an avenue with two lanes each way.
            Assert.IsNull(map.TryReplace(P(-100, 0), T("a4"), true));
            Assert.AreEqual(1, map.Segments.Count(s => s.Type.Id == "a4"));
            Assert.AreEqual(before + 200 * (6 - 2), map.Cost(), 0.5);
            Assert.AreEqual(4, map.ToWorld().Lanes.Count(l => l.Kind == LaneKind.Road && l.SpeedLimit > 15));
            Assert.IsNotNull(map.TryReplace(P(-100, 0), T("a4"), true), "already that kind");
            Assert.IsNotNull(map.TryReplace(P(300, 300), T("a4"), true), "no road there");
            // A tight curve cannot become a highway.
            Assert.IsNull(map.TryAdd(P(200, 0), P(240, 40), T("s2"), true, P(228.3, 11.7)));
            Assert.IsNotNull(map.TryReplace(P(228, 12), T("h2"), true));
        }

        // ---------------------------------------------------------------- flowing curves, moving, reshaping

        private static double Heading(Vec from, Vec to)
        {
            return Math.Atan2(to.Y - from.Y, to.X - from.X);
        }

        private static double Apart(double a, double b)
        {
            return Math.Abs(Math.Atan2(Math.Sin(a - b), Math.Cos(a - b)));
        }

        [Test]
        public void ABendLeavesTheWayItIsPointedAndArrivesWhereItSays()
        {
            // Setting off east and ending up to the north-east: a quarter of a circle of radius 100, turning left.
            Vec via;
            Assert.IsTrue(EditableMap.Bend(P(0, 0), 0, P(100, 100), out via));
            Assert.AreEqual(70.71, via.X, 0.01);
            Assert.AreEqual(29.29, via.Y, 0.01);
            Assert.AreEqual(100, EditableMap.CurveRadius(P(0, 0), via, P(100, 100)), 0.01);
            Assert.AreEqual(Math.PI / 2, EditableMap.BendArrives(P(0, 0), 0, P(100, 100)), 1e-9);

            // Whatever the direction and wherever the end, the curve sets off the way it was pointed.
            var rng = new Random(5);
            for (int i = 0; i < 200; i++)
            {
                Vec from = P(rng.NextDouble() * 400 - 200, rng.NextDouble() * 400 - 200), to = P(rng.NextDouble() * 400 - 200, rng.NextDouble() * 400 - 200);
                double heading = rng.NextDouble() * 2 * Math.PI;
                if (Apart(Heading(from, to), heading) > 2 || !EditableMap.Bend(from, heading, to, out via)) continue;
                Vec[] trace = EditableMap.Trace(from, to, true, via, 400);
                Assert.Less(Apart(Heading(trace[0], trace[1]), heading), 0.02, "sets off as pointed");
                Assert.Less(Apart(Heading(trace[399], trace[400]), EditableMap.BendArrives(from, heading, to)), 0.02, "arrives as promised");
            }

            Assert.IsFalse(EditableMap.Bend(P(0, 0), 0, P(100, 0), out via), "dead ahead is a straight road");
            Assert.IsFalse(EditableMap.Bend(P(0, 0), 0, P(-100, 0), out via), "dead behind cannot be reached");
        }

        [Test]
        public void CurvesDrawnOneAfterAnotherFlowWithoutACorner()
        {
            // A road that sets off east and winds its way north-east, each piece carrying on from the last.
            var map = new EditableMap();
            Vec at = P(-250, -150);
            double heading = 0;
            foreach (Vec next in new[] { P(-130, -110), P(-40, -10), P(90, 20), P(170, 120), P(260, 150) })
            {
                Vec via;
                bool curved = EditableMap.Bend(at, heading, next, out via);
                Assert.IsNull(map.TryAdd(at, next, T("s2"), true, curved ? via : (Vec?)null));
                heading = EditableMap.BendArrives(at, heading, next);
                at = next;
            }
            Assert.AreEqual(5, map.Segments.Count);
            Assert.AreEqual(5, map.Segments.Count(s => s.Curved));
            // Where one piece meets the next they point the same way.
            for (int i = 0; i + 1 < map.Segments.Count; i++)
            {
                Vec[] a = map.Trace(map.Segments[i]), b = map.Trace(map.Segments[i + 1]);
                Assert.Less(Apart(Heading(a[a.Length - 2], a[a.Length - 1]), Heading(b[0], b[1])), 0.12, "pieces " + i + " and " + (i + 1));
            }
            // The end of the road knows which way it is going, so the next piece can carry on from it.
            double flow;
            Assert.IsTrue(map.FlowAt(P(262, 151), out flow));
            Assert.Less(Apart(flow, heading), 0.06);
            Assert.IsFalse(map.FlowAt(P(-40, -10), out flow), "two roads meet there, so there is no one way on");
            Assert.IsFalse(map.FlowAt(P(0, 200), out flow), "open ground");

            var sim = new Simulation(map.ToWorld(), 3);
            sim.ArrivalsPerMinute = 8;
            Grid.Run(sim, 240);
            Assert.AreEqual(0, sim.Crashes);
            Assert.Greater(sim.Left, 15);
        }

        [Test]
        public void ARoadKnowsWhichWayItRunsWhereANewOneStarts()
        {
            var map = new EditableMap();
            Assert.IsNull(map.TryAdd(P(-200, 0), P(200, 0), T("s2"), true));
            Assert.IsNull(map.TryAdd(P(0, 0), P(0, 200), T("s2"), true));
            Assert.AreEqual(0, map.HeadingsAt(P(300, 300)).Count);
            Assert.AreEqual(2, map.HeadingsAt(P(100, 4)).Count, "both ways along the road");
            Assert.AreEqual(3, map.HeadingsAt(P(2, 2)).Count, "each road at the junction");
            Assert.IsTrue(map.HeadingsAt(P(100, 4)).Any(h => Apart(h, 0) < 0.01));
            Assert.IsTrue(map.HeadingsAt(P(2, 2)).Any(h => Apart(h, Math.PI / 2) < 0.01));
        }

        [Test]
        public void APointCanBeMovedAndItsRoadsFollow()
        {
            EditableMap map = Cross(2, 25);
            int middle = map.NodeAt(P(3, 3), 10);
            Assert.GreaterOrEqual(middle, 0);
            Assert.AreEqual(-1, map.NodeAt(P(80, 80), 10));
            double before = map.Cost();

            Assert.IsNull(map.TryMove(middle, P(40, 30), false), "trying it changes nothing");
            Assert.AreEqual(0, map.Nodes[middle].X, 1e-9);
            Assert.IsNull(map.TryMove(middle, P(40, 30), true));
            Assert.AreEqual(40, map.Nodes[middle].X, 1e-9);
            Assert.AreEqual(4, map.Segments.Count);
            Assert.Greater(map.Cost(), before, "the roads are longer now");
            Assert.AreEqual(1, map.ToWorld().Nodes.Count(n => n.Arms.Count == 4));

            // It cannot be moved so far that a road becomes too short, onto another point, or out of a level's building area.
            Assert.IsNotNull(map.TryMove(middle, P(180, 0), true));
            Assert.IsNotNull(map.TryMove(middle, P(198, 3), true));
            map.Bound = 150;
            Assert.IsNotNull(map.TryMove(middle, P(0, 160), true));
            map.Bound = 0;
            Assert.AreEqual(40, map.Nodes[middle].X, 1e-9, "a refused move leaves everything where it was");
        }

        [Test]
        public void MovingDoesNotJoinOrCrossRoads()
        {
            var map = new EditableMap();
            Assert.IsNull(map.TryAdd(P(-200, 0), P(200, 0), T("s2"), true));
            Assert.IsNull(map.TryAdd(P(-100, 60), P(100, 160), T("s2"), true));
            int end = map.NodeAt(P(-100, 60), 5);
            Assert.IsNotNull(map.TryMove(end, P(-100, 4), true), "onto the other road");
            Assert.IsNotNull(map.TryMove(end, P(-100, -80), true), "across the other road");
            Assert.IsNull(map.TryMove(end, P(-140, 90), true));
            // Roads that came with a level stay where they are.
            map.AddLocked(P(-200, -150), P(200, -150), T("s2"));
            Assert.IsNotNull(map.TryMove(map.NodeAt(P(200, -150), 5), P(200, -200), true));
        }

        [Test]
        public void ACurveKeepsItsShapeWhenItsEndIsMoved()
        {
            var map = new EditableMap();
            Vec via;
            Assert.IsTrue(EditableMap.Bend(P(0, 0), 0, P(100, 100), out via));
            Assert.IsNull(map.TryAdd(P(0, 0), P(100, 100), T("s2"), true, via));
            double turn = EditableMap.CurveTurn(P(0, 0), via, P(100, 100));
            // Pull the far end out to twice the distance, and round to the north.
            Assert.IsNull(map.TryMove(map.NodeAt(P(100, 100), 5), P(0, 283), true));
            EditSegment s = map.Segments[0];
            Assert.IsTrue(s.Curved);
            Assert.AreEqual(turn, EditableMap.CurveTurn(map.Nodes[s.A], s.Mid, map.Nodes[s.B]), 1e-6, "still a quarter turn");
            Assert.AreEqual(200, EditableMap.CurveRadius(map.Nodes[s.A], s.Mid, map.Nodes[s.B]), 0.5, "twice the size");
        }

        [Test]
        public void ARoadCanBeBentAndStraightenedAgain()
        {
            EditableMap map = Cross(2, 25);
            Vec on;
            int east = map.NearestSegment(P(100, 0), 5, out on);
            Assert.IsFalse(map.Segments[east].Curved);
            Assert.IsNull(map.TryBend(east, P(100, 40), true));
            Assert.IsTrue(map.Segments[east].Curved);
            Vec[] trace = map.Trace(map.Segments[east]);
            Assert.IsTrue(trace.Any(v => Math.Abs(v.X - 100) < 4 && Math.Abs(v.Y - 40) < 1), "it passes through the point it was pulled to");
            Assert.AreEqual(4, map.Segments.Count);
            map.ToWorld();

            Assert.IsNotNull(map.TryBend(east, P(100, 190), true), "too far round, or too tight");
            Assert.IsTrue(map.Segments[east].Curved);
            Assert.IsNull(map.TryBend(east, P(100, 1), true), "pulled back into line it is straight again");
            Assert.IsFalse(map.Segments[east].Curved);

            // Bending one road across another is refused: crossing is what drawing is for.
            Assert.IsNull(map.TryAdd(P(40, 60), P(160, 60), T("s2"), true));
            Assert.IsNotNull(map.TryBend(east, P(100, 90), true));
        }

        // ---------------------------------------------------------------- pieces that make one road

        [Test]
        public void ARoadCanBeDrawnInShortPiecesAndIsStillOneRoad()
        {
            // Nine pieces of about 80 ft each, wandering a little: far shorter than a stretch used to have to be.
            var map = new EditableMap();
            var points = new[] { P(-110, 0), P(-86, 3), P(-62, 2), P(-37, 7), P(-12, 9), P(13, 6), P(37, 9), P(62, 15), P(86, 16), P(110, 22) };
            for (int i = 0; i + 1 < points.Length; i++) Assert.IsNull(map.TryAdd(points[i], points[i + 1], T("s2"), true), "piece " + i);
            Assert.AreEqual(9, map.Segments.Count);
            World world = map.ToWorld();
            Assert.AreEqual(2, world.Nodes.Count, "only its two ends are points of the network");
            Assert.AreEqual(2, world.Lanes.Count(l => l.Kind == LaneKind.Road), "one lane each way, the whole length");
            Assert.Greater(world.Lanes.First(l => l.Kind == LaneKind.Road).Length, 200);
            Assert.AreEqual(0, map.SharpCorners().Count);

            var sim = new Simulation(world, 2);
            sim.ArrivalsPerMinute = 10;
            Grid.Run(sim, 180);
            Assert.AreEqual(0, sim.Crashes);
            Assert.Greater(sim.Left, 15);
        }

        [Test]
        public void ACornerBetweenTwoPiecesIsRounded()
        {
            var map = new EditableMap();
            Assert.IsNull(map.TryAdd(P(-150, 0), P(0, 0), T("s2"), true));
            Assert.IsNull(map.TryAdd(P(0, 0), P(0, 150), T("s2"), true));
            World world = map.ToWorld();
            Assert.AreEqual(2, world.Nodes.Count);
            // The road cuts the corner: no lane comes within a few meters of the point where the pieces meet.
            double nearest = double.PositiveInfinity, tightest = double.PositiveInfinity;
            foreach (Lane lane in world.Lanes)
            {
                for (double d = 0; d <= lane.Length; d += 1) nearest = Math.Min(nearest, Geo.Hypot(lane.Path.At(d).X, lane.Path.At(d).Y));
                for (double d = 6; d + 6 <= lane.Length; d += 2) tightest = Math.Min(tightest, Geo.Circumradius(lane.Path.At(d - 6).Point, lane.Path.At(d).Point, lane.Path.At(d + 6).Point));
            }
            Assert.Greater(nearest, 7);
            Assert.Greater(tightest, 22, "nowhere tighter than a street may turn");
        }

        [Test]
        public void ACornerWithNoRoomToRoundItIsRefused()
        {
            // A square turn with only 60 ft either side of it: a street cannot get round that.
            var map = new EditableMap();
            Assert.IsNull(map.TryAdd(P(0, 0), P(18, 0), T("s2"), true), "a short stretch on its own is fine now");
            string refused = map.TryAdd(P(18, 0), P(18, 18), T("s2"), true);
            Assert.IsNotNull(refused);
            StringAssert.Contains("sharp", refused);
            // A gentle turn in the same space is fine, and so is the square turn given room.
            Assert.IsNull(map.TryAdd(P(18, 0), P(38, 4), T("s2"), true));
            var roomy = new EditableMap();
            Assert.IsNull(roomy.TryAdd(P(0, 0), P(60, 0), T("s2"), true));
            Assert.IsNull(roomy.TryAdd(P(60, 0), P(60, 60), T("s2"), true));
            // A highway needs far more room than a street for the same turn.
            var fast = new EditableMap();
            Assert.IsNull(fast.TryAdd(P(0, 0), P(45, 0), T("h2"), true));
            Assert.IsNotNull(fast.TryAdd(P(45, 0), P(45, 45), T("h2"), true));
            Assert.IsNull(fast.TryAdd(P(45, 0), P(140, 20), T("h2"), true));
        }

        [Test]
        public void MovingAPointCannotMakeACornerTooSharp()
        {
            var map = new EditableMap();
            Assert.IsNull(map.TryAdd(P(-100, 0), P(0, 0), T("s2"), true));
            Assert.IsNull(map.TryAdd(P(0, 0), P(100, 20), T("s2"), true));
            int far = map.NodeAt(P(100, 20), 5);
            string refused = map.TryMove(far, P(3, 16), true);
            Assert.IsNotNull(refused, "a square turn with 50 ft to make it in");
            StringAssert.Contains("sharp", refused);
            Assert.IsNull(map.TryMove(far, P(5, 30), true), "with 100 ft there is just room to round it");
            Assert.IsNull(map.TryMove(far, P(90, 60), true));
        }

        [Test]
        public void PiecesOnlyMakeOneRoadWhenTheyAreTheSameKindRunningTheSameWay()
        {
            // Two-way pieces drawn toward each other still carry on as one road.
            var map = new EditableMap();
            Assert.IsNull(map.TryAdd(P(-150, 0), P(0, 0), T("s2"), true));
            Assert.IsNull(map.TryAdd(P(150, 10), P(0, 0), T("s2"), true));
            Assert.AreEqual(2, map.ToWorld().Nodes.Count);

            // A street that becomes an avenue changes at a point of its own.
            var change = new EditableMap();
            Assert.IsNull(change.TryAdd(P(-150, 0), P(0, 0), T("s2"), true));
            Assert.IsNull(change.TryAdd(P(0, 0), P(150, 10), T("a2p"), true));
            Assert.AreEqual(1, change.ToWorld().Nodes.Count(n => n.Arms.Count == 2));

            // One-way pieces carry on only if traffic runs the same way through.
            var oneWay = new EditableMap();
            Assert.IsNull(oneWay.TryAdd(P(-150, 0), P(0, 0), T("s1"), true));
            Assert.IsNull(oneWay.TryAdd(P(0, 0), P(150, 10), T("s1"), true));
            World through = oneWay.ToWorld();
            Assert.AreEqual(2, through.Nodes.Count);
            Assert.AreEqual(1, through.Entries.Count, "traffic comes in at the end it was drawn from");
            Assert.AreEqual(0, oneWay.Faults().Count);
            var headOn = new EditableMap();
            Assert.IsNull(headOn.TryAdd(P(-150, 0), P(0, 0), T("s1"), true));
            Assert.IsNull(headOn.TryAdd(P(150, 10), P(0, 0), T("s1"), true));
            Assert.AreEqual(3, headOn.ToWorld().Nodes.Count);
            Assert.AreEqual(1, headOn.Faults().Count, "two one-way roads running into each other");
        }

        [Test]
        public void ARingOfPiecesWithNoJunctionStillBuildsAndCarriesTraffic()
        {
            // A closed circuit: no road end anywhere, and no junction either.
            var map = new EditableMap();
            var corners = new[] { P(-120, -80), P(120, -80), P(120, 80), P(-120, 80) };
            for (int i = 0; i < 4; i++) Assert.IsNull(map.TryAdd(corners[i], corners[(i + 1) % 4], T("s2"), true), "side " + i);
            World world = map.ToWorld();
            Assert.AreEqual(1, world.Nodes.Count, "one point for the road to begin and end at");
            var sim = new Simulation(world, 6);
            for (int i = 0; i < 8; i++) sim.Spawn(DriverParams.Normal, 0.3, VehicleKind.Car);
            Assert.Greater(sim.Cars.Count, 4);
            Grid.Run(sim, 180);
            Assert.AreEqual(0, sim.Crashes);
            Assert.IsTrue(sim.Cars.All(c => c.V > 2), "everyone is still going round");

            // A loop that leaves a road and comes back to it at the same junction.
            var lollipop = new EditableMap();
            Assert.IsNull(lollipop.TryAdd(P(0, -200), P(0, 0), T("s2"), true));
            Assert.IsNull(lollipop.TryAdd(P(0, 0), P(90, 90), T("s2"), true));
            Assert.IsNull(lollipop.TryAdd(P(90, 90), P(0, 180), T("s2"), true));
            Assert.IsNull(lollipop.TryAdd(P(0, 180), P(-90, 90), T("s2"), true));
            Assert.IsNull(lollipop.TryAdd(P(-90, 90), P(0, 0), T("s2"), true));
            World loop = lollipop.ToWorld();
            Assert.AreEqual(2, loop.Nodes.Count);
            var round = new Simulation(loop, 3);
            round.ArrivalsPerMinute = 6;
            Grid.Run(round, 240);
            Assert.AreEqual(0, round.Crashes);
            Assert.Greater(round.Left, 6, "traffic goes round the loop and back out the way it came");
        }

        // ---------------------------------------------------------------- extra lanes

        private static int RoadLanes(World world)
        {
            return world.Lanes.Count(l => l.Kind == LaneKind.Road);
        }

        [Test]
        public void ALaneCanBeAddedAlongPartOfARoad()
        {
            var map = new EditableMap();
            Assert.IsNull(map.TryAdd(P(-200, 0), P(200, 0), T("s2"), true));
            double before = map.Cost();
            // On the south side, which is the right of the traffic heading east, from 100 m in to 220 m in.
            Assert.IsNull(map.TryAddLane(P(-100, -5), P(20, -5), false), "trying it changes nothing");
            Assert.AreEqual(0, map.Segments[0].Extras.Count);
            Assert.IsNull(map.TryAddLane(P(-100, -5), P(20, -5), true));
            Assert.AreEqual(1, map.Segments[0].Extras.Count);
            Assert.IsTrue(map.Segments[0].Extras[0].Forward);
            Assert.AreEqual(before + 120 * 1, map.Cost(), 1, "a lane of a street is half the price of the street");

            World world = map.ToWorld();
            Assert.AreEqual(3, RoadLanes(world));
            Lane extra = world.Lanes.Single(l => l.Kind == LaneKind.Road && l.Slot == 1);
            Assert.AreEqual(120, extra.Length, 3);
            Assert.Greater(Math.Cos(extra.Path.At(10).H), 0.9, "it runs east, with the traffic on that side");
            Assert.Less(extra.Path.At(10).Y, -4, "outside the lane that was there");
            Assert.IsNull(extra.Connectors, "it starts and ends by itself, joined to nothing");
            Assert.AreEqual(1, extra.Lefts.Count, "traffic moves over into it and back from the lane beside it");

            // The line shown before it is built runs where the lane then does.
            Vec[] shown = map.LaneLine(P(-100, -5), P(20, -5));
            Assert.AreEqual(extra.Path.At(60).Y, shown[shown.Length / 2].Y, 0.3);

            // The other side of the same road, the other way.
            Assert.IsNull(map.TryAddLane(P(150, 6), P(60, 6), true));
            Assert.AreEqual(4, RoadLanes(map.ToWorld()));
            Assert.AreEqual(2, map.Segments[0].Extras.Count);

            Assert.IsNotNull(map.TryAddLane(P(150, -5), P(160, -5), true), "too short");
            Assert.IsNotNull(map.TryAddLane(P(-100, -5), P(-100, 300), true), "not on a road at the far end");
            Assert.IsNotNull(map.TryAddLane(P(0, 300), P(50, 300), true), "not on a road at all");
        }

        [Test]
        public void ThePointerPicksOutTheLaneItIsOver()
        {
            // An avenue with two lanes each way and a median, drawn as two pieces.
            var map = new EditableMap();
            Assert.IsNull(map.TryAdd(P(-200, 0), P(0, 0), T("a4"), true));
            Assert.IsNull(map.TryAdd(P(0, 0), P(200, 0), T("a4"), true));
            Vec[] inner = map.LaneUnder(P(50, -3)), outer = map.LaneUnder(P(50, -7)), other = map.LaneUnder(P(50, 7));
            Assert.AreEqual(-(1.5 + 0.5 * NetworkBuilder.LaneW), inner[inner.Length / 2].Y, 0.01, "the eastbound lane next to the median");
            Assert.AreEqual(-(1.5 + 1.5 * NetworkBuilder.LaneW), outer[outer.Length / 2].Y, 0.01, "the eastbound outside lane");
            Assert.AreEqual(1.5 + 1.5 * NetworkBuilder.LaneW, other[other.Length / 2].Y, 0.01, "the westbound outside lane");
            Assert.Less(inner[0].X, -190, "the whole road, not just the piece the pointer is on");
            Assert.Greater(inner[inner.Length - 1].X, 190);
            Assert.Greater(other[0].X, 190, "and each runs the way its traffic does");
            Assert.IsNull(map.LaneUnder(P(50, 80)));

            // A one-way road lies across its line: the left lane is left of it.
            var oneWay = new EditableMap();
            Assert.IsNull(oneWay.TryAdd(P(-200, 0), P(200, 0), T("h2"), true));
            Assert.AreEqual(NetworkBuilder.LaneW / 2, oneWay.LaneUnder(P(0, 2))[5].Y, 0.01);
            Assert.AreEqual(-NetworkBuilder.LaneW / 2, oneWay.LaneUnder(P(0, -2))[5].Y, 0.01);
            // An added lane is picked out by itself.
            Assert.IsNull(oneWay.TryAddLane(P(-50, -3), P(50, -3), true));
            Vec[] added = oneWay.LaneUnder(P(0, -5.5));
            Assert.AreEqual(-1.5 * NetworkBuilder.LaneW, added[2].Y, 0.01);
            Assert.AreEqual(100, added[added.Length - 1].X - added[0].X, 1);
            Assert.IsNull(oneWay.ExtraLaneAt(P(0, 5.5)), "the far side of a one-way road is not the added lane");
        }

        [Test]
        public void ALaneRunningToAJunctionBecomesALaneOfIt()
        {
            EditableMap map = Cross(2, 40);
            World plain = map.ToWorld();
            // Along the last 80 m of the western arm, eastbound: a lane to turn from at the light.
            Assert.IsNull(map.TryAddLane(P(-80, -5), P(-6, -5), true));
            World world = map.ToWorld();
            Assert.AreEqual(RoadLanes(plain) + 1, RoadLanes(world));
            Node crossing = world.Nodes.Single(n => n.Arms.Count == 4);
            Assert.AreEqual(5, crossing.Arms.Sum(a => a.Inbound.Count), "five lanes now arrive at the light");

            var sim = new Simulation(world, 5);
            sim.ArrivalsPerMinute = 14;
            Grid.Run(sim, 300);
            Assert.AreEqual(0, sim.Crashes);
            Assert.Greater(sim.Left, 30);
        }

        [Test]
        public void TrafficUsesAnAddedLaneAndGetsBackOutOfIt()
        {
            var map = new EditableMap();
            Assert.IsNull(map.TryAdd(P(-400, 0), P(400, 0), T("s1"), true));
            Assert.IsNull(map.TryAddLane(P(-150, -3), P(150, -3), true));
            World world = map.ToWorld();
            Lane extra = world.Lanes.Single(l => l.Kind == LaneKind.Road && l.Slot == 1);
            var sim = new Simulation(world, 9);
            sim.ArrivalsPerMinute = 22;
            int inIt = 0;
            Grid.Run(sim, 400, () =>
            {
                foreach (Car c in sim.Cars) if (c.Lane == extra.Id) inIt++;
            });
            Assert.AreEqual(0, sim.Crashes);
            Assert.Greater(sim.Left, 60, "nobody is left stuck at the end of the lane");
            Assert.Greater(inIt, 0, "and some did use it");
        }

        [Test]
        public void ALaneCarriesOnAcrossThePiecesOfARoadAndComesOffInOne()
        {
            // A road in three pieces, the middle one drawn the other way round.
            var map = new EditableMap();
            Assert.IsNull(map.TryAdd(P(-200, 0), P(-60, 0), T("s2"), true));
            Assert.IsNull(map.TryAdd(P(60, 10), P(-60, 0), T("s2"), true));
            Assert.IsNull(map.TryAdd(P(60, 10), P(200, 10), T("s2"), true));
            Assert.IsNull(map.TryAddLane(P(-150, -5), P(150, 5), true));
            Assert.AreEqual(3, map.Segments.Count(s => s.Extras.Count == 1));
            World world = map.ToWorld();
            Assert.AreEqual(3, RoadLanes(world), "one lane, not three end to end");
            Assert.AreEqual(300, world.Lanes.Single(l => l.Kind == LaneKind.Road && l.Slot == 1).Length, 8);

            Assert.IsNotNull(map.ExtraLaneAt(P(0, -2.5)), "the pointer over the added lane finds it");
            Assert.IsNull(map.ExtraLaneAt(P(0, 6)), "but not from the other side of the road");
            Assert.IsNotNull(map.TryRemoveLane(P(0, 9), true));
            Assert.IsNull(map.TryRemoveLane(P(0, -2.5), true));
            Assert.AreEqual(0, map.Segments.Sum(s => s.Extras.Count), "all three parts go together");
        }

        [Test]
        public void ExtraLanesSurviveSavingSplittingAndReplacing()
        {
            var map = new EditableMap();
            Assert.IsNull(map.TryAdd(P(-200, 0), P(200, 0), T("s2"), true));
            Assert.IsNull(map.TryAddLane(P(-100, -5), P(100, -5), true));
            Assert.IsNull(map.TryAddLane(P(120, 5), P(40, 5), true));
            EditableMap loaded = EditableMap.Parse(map.Serialize());
            Assert.AreEqual(map.Serialize(), loaded.Serialize());
            Assert.AreEqual(2, loaded.Segments[0].Extras.Count);

            // A road drawn to the middle cuts the lane in two parts that are still one lane on the ground... until the junction divides them.
            Assert.IsNull(map.TryAdd(P(0, 0), P(0, 200), T("s2"), true));
            Assert.AreEqual(3, map.Segments.Sum(s => s.Extras.Count), "the south lane is on both halves, the north lane on one");
            World world = map.ToWorld();
            Assert.AreEqual(6 + 3, RoadLanes(world));
            double south = world.Lanes.Where(l => l.Kind == LaneKind.Road && l.Slot == 1 && l.Path.At(1).Y < 0).Sum(l => l.Length);
            Assert.AreEqual(200 - 2 * world.Nodes.Single(n => n.Arms.Count == 3).Radius, south, 6);

            // Made one-way, a road keeps only the lane on the side its traffic is on.
            Assert.IsNull(map.TryReplace(P(100, 0), T("s1"), true));
            EditSegment east = map.Segments.Single(s => s.Type.Id == "s1");
            Assert.IsTrue(east.Extras.All(x => x.Forward));
        }

        [Test]
        public void ALaneAddedUpToAnExitIsForTheExitAlone()
        {
            // A two-lane highway with a ramp peeling off, and a third lane added over the 200 m before the ramp.
            EditableMap map = HighwayWithRamps();
            World before = map.ToWorld();
            Assert.IsNull(map.TryAddLane(P(-50, -5), P(145, -5), true));
            World world = map.ToWorld();
            Node exit = world.Nodes.First(n => n.Arms.Count == 3 && n.Pos.X > 0);
            var arriving = world.Lanes.Where(l => l.Kind == LaneKind.Road && l.Node == exit.Id).OrderByDescending(l => l.Path.At(l.Length).Y).ToList();
            Assert.AreEqual(3, arriving.Count, "the two highway lanes and the added one");
            Assert.IsTrue(arriving[0].Allows(Turn.Straight) && !arriving[0].Allows(Turn.Right), "the inside lane goes on");
            Assert.IsTrue(arriving[1].Allows(Turn.Straight) && !arriving[1].Allows(Turn.Right), "so does the old outside lane, which no longer exits");
            Assert.IsTrue(arriving[2].Allows(Turn.Right) && !arriving[2].Allows(Turn.Straight), "the added lane leads only to the ramp");
            // Before the lane was added the outside lane did both.
            Node old = before.Nodes.First(n => n.Arms.Count == 3 && n.Pos.X > 0);
            Assert.IsTrue(before.Lanes.Any(l => l.Kind == LaneKind.Road && l.Node == old.Id && l.Allows(Turn.Straight) && l.Allows(Turn.Right)));

            var sim = new Simulation(world, 4);
            sim.ArrivalsPerMinute = 26;
            int offRamp = 0;
            Grid.Run(sim, 420, () =>
            {
                foreach (Car c in sim.Cars) if (c.X > 300 && c.Y < -60) offRamp = Math.Max(offRamp, 1);
            });
            Assert.AreEqual(0, sim.Crashes);
            Assert.AreEqual(1, offRamp, "traffic still takes the exit");
            Assert.Greater(sim.Left, 80);
        }

        [Test]
        public void ALaneAddedFromAMergeIsTheRampsLaneCarryingOn()
        {
            EditableMap map = HighwayWithRamps();
            // From the merge at x = -150 on for 250 m: well past where the ramp's lane used to end.
            Assert.IsNull(map.TryAddLane(P(-145, -5), P(100, -5), true));
            World world = map.ToWorld();
            Node merge = world.Nodes.First(n => n.Arms.Count == 3 && n.Pos.X < 0);
            var leaving = world.Lanes.Where(l => l.Kind == LaneKind.Road && Math.Abs(l.Path.At(0).X - (merge.Pos.X + NetworkBuilder.FreeRadius)) < 1.5 && Math.Abs(l.Path.At(0).Y) < 12).ToList();
            Assert.AreEqual(3, leaving.Count, "still one lane from the ramp, not two");
            Lane added = leaving.OrderBy(l => l.Path.At(0).Y).First();
            Assert.Greater(added.Length, 220, "and it now runs as far as the lane that was added");
            Assert.AreEqual(1, added.Preds.Count);

            var sim = new Simulation(world, 8);
            sim.ArrivalsPerMinute = 26;
            Grid.Run(sim, 420);
            Assert.AreEqual(0, sim.Crashes);
            Assert.Greater(sim.Left, 80);
        }

        // ---------------------------------------------------------------- one lane more, one lane fewer

        [Test]
        public void ARoadCanGainOrLoseASingleLaneOnOneSide()
        {
            var map = new EditableMap();
            Assert.IsNull(map.TryAdd(P(-200, 0), P(0, 0), T("s2"), true));
            Assert.IsNull(map.TryAdd(P(200, 10), P(0, 0), T("s2"), true)); // the second piece is drawn the other way round
            double before = map.Cost();
            // One more lane for the traffic heading east, which keeps to the south side.
            Assert.IsNull(map.TryChangeLanes(P(-100, -3), 1, false), "trying it changes nothing");
            Assert.AreEqual("s2", map.Segments[0].Type.Id);
            Assert.IsNull(map.TryChangeLanes(P(-100, -3), 1, true));
            Assert.IsTrue(map.Segments.All(s => s.Type.Forward + s.Type.Backward == 3), "both pieces change: it is one road");
            World world = map.ToWorld();
            Assert.AreEqual(2, world.Nodes.Count, "and still one road");
            var lanes = world.Lanes.Where(l => l.Kind == LaneKind.Road).ToList();
            Assert.AreEqual(2, lanes.Count(l => Math.Cos(l.Path.At(50).H) > 0), "two lanes east");
            Assert.AreEqual(1, lanes.Count(l => Math.Cos(l.Path.At(50).H) < 0), "one lane west");
            Assert.AreEqual(before * 1.5, map.Cost(), 1, "a third lane costs half as much again");

            // It saves and loads as what it is.
            EditableMap loaded = EditableMap.Parse(map.Serialize());
            Assert.AreEqual(map.Serialize(), loaded.Serialize());
            Assert.AreEqual(3, loaded.Segments[0].Type.Lanes);
            StringAssert.Contains("2 lanes one way and 1 the other", loaded.Segments[0].Type.Name);

            // Take the westbound lane away and it is a one-way road, two lanes east.
            Assert.IsNull(map.TryChangeLanes(P(100, 9), -1, true));
            Assert.IsTrue(map.Segments.All(s => s.Type.OneWay && s.Type.Forward == 2));
            World oneWay = map.ToWorld();
            Assert.AreEqual(2, oneWay.Lanes.Count(l => l.Kind == LaneKind.Road));
            Assert.IsTrue(oneWay.Lanes.Where(l => l.Kind == LaneKind.Road).All(l => Math.Cos(l.Path.At(50).H) > 0));
            Assert.AreEqual(1, oneWay.Entries.Count);
            // Down to one lane, and no further.
            Assert.IsNull(map.TryChangeLanes(P(-100, 0), -1, true));
            Assert.AreEqual("s1", map.Segments[0].Type.Id, "which is one of the roads on the toolbar");
            Assert.IsNotNull(map.TryChangeLanes(P(-100, 0), -1, true));
            // Up to four each way, and no further.
            for (int i = 0; i < 3; i++) Assert.IsNull(map.TryChangeLanes(P(-100, 0), 1, true));
            Assert.IsNotNull(map.TryChangeLanes(P(-100, 0), 1, true));
        }

        [Test]
        public void TheWestboundSideCanBeTheOneLeftStanding()
        {
            // Remove the eastbound lane of a street drawn west to east: what is left runs west.
            var map = new EditableMap();
            Assert.IsNull(map.TryAdd(P(-200, 0), P(200, 0), T("s2"), true));
            Assert.IsNull(map.TryAddLane(P(100, 4), P(-50, 4), true)); // an added lane on the westbound side
            Assert.IsNull(map.TryChangeLanes(P(0, -2), -1, true));
            EditSegment s = map.Segments[0];
            Assert.IsTrue(s.Type.OneWay);
            Assert.AreEqual(200, map.Nodes[s.A].X, 1e-9, "it now runs from its east end to its west");
            Assert.AreEqual(1, s.Extras.Count, "and keeps the lane added on that side");
            World world = map.ToWorld();
            Assert.IsTrue(world.Lanes.Where(l => l.Kind == LaneKind.Road).All(l => Math.Cos(l.Path.At(l.Length / 2).H) < 0));
            Assert.AreEqual(2, world.Lanes.Count(l => l.Kind == LaneKind.Road));
        }

        [Test]
        public void AnAddedLaneCanBeLengthenedAndAGapFilled()
        {
            var map = new EditableMap();
            Assert.IsNull(map.TryAdd(P(-300, 0), P(300, 0), T("s2"), true));
            Assert.IsNull(map.TryAddLane(P(-200, -5), P(-80, -5), true));
            Assert.IsNull(map.TryAddLane(P(-60, -5), P(60, -5), true));
            Assert.AreEqual(2, map.Segments[0].Extras.Count);
            // A short stretch across the gap between them, far shorter than a lane may be by itself, joins them into one.
            Assert.IsNull(map.TryAddLane(P(-85, -5), P(-55, -5), true));
            Assert.AreEqual(1, map.Segments[0].Extras.Count);
            // And a short stretch on the end makes it longer.
            Assert.IsNull(map.TryAddLane(P(55, -5), P(75, -5), true));
            Assert.AreEqual(1, map.Segments[0].Extras.Count);
            Lane lane = map.ToWorld().Lanes.Single(l => l.Kind == LaneKind.Road && l.Slot == 1);
            Assert.AreEqual(275, lane.Length, 3);
            Assert.IsNotNull(map.TryAddLane(P(150, -5), P(160, -5), true), "by itself it is still too short");
        }

        // ---------------------------------------------------------------- forks, and roads coming together

        /// <summary>A two-lane one-way road that splits into two single lanes, which go their own ways and come back together.</summary>
        public static EditableMap SplitAndRejoin(string wide = "a1x2", string narrow = "a1")
        {
            var map = new EditableMap();
            Assert.IsNull(map.TryAdd(P(-320, 0), P(-110, 0), T(wide), true));
            Assert.IsNull(map.TryAdd(P(-110, 0), P(0, 46), T(narrow), true), "the left branch out");
            Assert.IsNull(map.TryAdd(P(-110, 0), P(0, -46), T(narrow), true), "the right branch out");
            Assert.IsNull(map.TryAdd(P(0, 46), P(110, 0), T(narrow), true), "the left branch back");
            Assert.IsNull(map.TryAdd(P(0, -46), P(110, 0), T(narrow), true), "the right branch back");
            Assert.IsNull(map.TryAdd(P(110, 0), P(320, 0), T(wide), true));
            return map;
        }

        [Test]
        public void ATwoLaneRoadSplitsIntoTwoLanesThatGoTheirOwnWaysAndComeBack()
        {
            EditableMap map = SplitAndRejoin();
            Assert.AreEqual(0, map.Faults().Count);
            World world = map.ToWorld();
            Assert.AreEqual(0, world.Nodes.Count(n => n.Controlled), "no light and no stop sign: the lanes just part and close up");
            Node fork = world.Nodes.Single(n => n.Arms.Count == 3 && n.Pos.X < 0), join = world.Nodes.Single(n => n.Arms.Count == 3 && n.Pos.X > 0);

            // At the fork the left lane can only take the left branch and the right lane the right one.
            var arriving = world.Lanes.Where(l => l.Kind == LaneKind.Road && l.Node == fork.Id).OrderByDescending(l => l.Path.At(l.Length).Y).ToList();
            Assert.AreEqual(2, arriving.Count);
            foreach (Lane lane in arriving)
            {
                var ways = Enumerable.Range(0, Lane.Turns).Where(t => lane.Connectors[t] >= 0).ToList();
                Assert.AreEqual(1, ways.Count, "each lane has one way to go");
                Lane onto = world.Lanes[world.Lanes[lane.Connectors[ways[0]]].Next];
                Assert.AreEqual(lane == arriving[0], onto.Path.At(onto.Length / 2).Y > 0, "and it is the branch on its own side");
            }
            // Where they come back, each branch has a lane of the wide road to itself.
            var feeding = world.Lanes.Where(l => l.Kind == LaneKind.Connector && l.Node == join.Id).ToList();
            Assert.AreEqual(2, feeding.Count);
            Assert.AreNotEqual(feeding[0].Next, feeding[1].Next);
            foreach (Lane c in feeding)
            {
                Lane from = world.Lanes[c.From], onto = world.Lanes[c.Next];
                Assert.AreEqual(from.Path.At(from.Length - 30).Y > 0, onto.Path.At(5).Y > 0, "the left branch feeds the left lane");
            }

            var sim = new Simulation(world, 4);
            sim.ArrivalsPerMinute = 24;
            int left = 0, right = 0;
            Grid.Run(sim, 400, () =>
            {
                foreach (Car c in sim.Cars)
                {
                    if (Math.Abs(c.X) < 20 && c.Y > 30) left++;
                    if (Math.Abs(c.X) < 20 && c.Y < -30) right++;
                }
            });
            Assert.AreEqual(0, sim.Crashes);
            Assert.Greater(left, 0, "traffic uses the left branch");
            Assert.Greater(right, 0, "and the right one");
            Assert.Greater(sim.Left, 80);
        }

        [Test]
        public void OneLaneCanForkIntoTwoRoadsAndRoadsMayPartNarrowly()
        {
            // One lane in, two roads out only 28 degrees apart: closer than roads may meet at an intersection.
            var map = new EditableMap();
            Assert.IsNull(map.TryAdd(P(-300, 0), P(0, 0), T("h1"), true));
            Assert.IsNull(map.TryAdd(P(0, 0), P(290, 72), T("h1"), true));
            Assert.IsNull(map.TryAdd(P(0, 0), P(290, -72), T("h1"), true));
            World world = map.ToWorld();
            Assert.AreEqual(0, world.Nodes.Count(n => n.Controlled));
            Lane arriving = world.Lanes.Single(l => l.Kind == LaneKind.Road && l.Node == world.Nodes.Single(n => n.Arms.Count == 3).Id);
            Assert.AreEqual(2, Enumerable.Range(0, Lane.Turns).Count(t => arriving.Connectors[t] >= 0), "the one lane serves both roads");
            // Two lanes parting into two roads must part by more than a few degrees.
            var tight = new EditableMap();
            Assert.IsNull(tight.TryAdd(P(-300, 0), P(0, 0), T("a1x2"), true));
            Assert.IsNull(tight.TryAdd(P(0, 0), P(300, 15), T("a1"), true));
            Assert.IsNotNull(tight.TryAdd(P(0, 0), P(300, -15), T("a1"), true));

            var sim = new Simulation(world, 6);
            sim.ArrivalsPerMinute = 16;
            Grid.Run(sim, 300);
            Assert.AreEqual(0, sim.Crashes);
            Assert.Greater(sim.Left, 40);
        }

        [Test]
        public void TwoWayRoadsThatMeetAtAnAngleAreStillAnIntersection()
        {
            var map = new EditableMap();
            Assert.IsNull(map.TryAdd(P(-300, 0), P(0, 0), T("a2p"), true));
            Assert.IsNull(map.TryAdd(P(0, 0), P(250, 130), T("a2p"), true));
            Assert.IsNull(map.TryAdd(P(0, 0), P(250, -130), T("a2p"), true));
            Assert.AreEqual(1, map.ToWorld().Nodes.Count(n => n.Controlled), "a light, as before");
            // And two-way roads still may not meet as closely as a fork may.
            Assert.IsNotNull(map.TryAdd(P(0, 0), P(300, -40), T("a2p"), true));
        }

        // ---------------------------------------------------------------- the edge of the map, and dead ends

        [Test]
        public void ARoadThatStopsShortOfTheEdgeJustEndsAndTrafficTurnsRoundThere()
        {
            var map = new EditableMap { Bound = 300 };
            Assert.IsNull(map.TryAdd(P(-300, 0), P(120, 0), T("s2"), true));
            Assert.IsNotNull(map.TryAdd(P(0, 0), P(0, 400), T("s2"), false), "nothing can be built beyond the edge");
            World world = map.ToWorld();
            Assert.AreEqual(1, world.Entries.Count, "traffic comes in only where the road reaches the edge");
            Node end = world.Nodes.Single(n => n.DeadEnd);
            Assert.IsFalse(end.Terminal);
            Assert.AreEqual(120, end.Pos.X, 0.01);
            Lane arriving = world.Lanes.Single(l => l.Kind == LaneKind.Road && l.Node == end.Id);
            Assert.IsTrue(arriving.Allows(Turn.Straight), "the way on is round the turning place");
            Lane round = world.Lanes[arriving.Connectors[(int)Turn.Straight]];
            // It turns within the road: no wider than the road is, and not far past where the lanes stop.
            double furthest = 0, widest = 0;
            for (double d = 0; d <= round.Length; d += 0.5)
            {
                furthest = Math.Max(furthest, round.Path.At(d).X);
                widest = Math.Max(widest, Math.Abs(round.Path.At(d).Y));
            }
            Assert.Less(widest, NetworkBuilder.LaneW, "across the double yellow line and back, not out onto the grass");
            Assert.Less(furthest, 120.5, "and no further than the end of the road");
            Assert.AreEqual(4, end.Box.Count, "the pavement just stops, square");
            Assert.AreEqual(120, end.Box[1].X, 0.6);

            var sim = new Simulation(world, 3);
            sim.ArrivalsPerMinute = 8;
            int roundIt = 0;
            Grid.Run(sim, 300, () =>
            {
                foreach (Car c in sim.Cars) if (c.Lane == round.Id) roundIt++;
            });
            Assert.AreEqual(0, sim.Crashes);
            Assert.Greater(roundIt, 0, "cars go round");
            Assert.Greater(sim.Left, 10, "and leave the way they came");

            // With no edge to the map, an open road end is still where traffic comes and goes.
            map.Bound = 0;
            World open = map.ToWorld();
            Assert.AreEqual(2, open.Entries.Count);
            Assert.AreEqual(0, open.Nodes.Count(n => n.DeadEnd));
        }

        [Test]
        public void AWideRoadAndAShortStubCanBothBeDeadEnds()
        {
            var map = new EditableMap { Bound = 300 };
            Assert.IsNull(map.TryAdd(P(-300, 0), P(150, 0), T("a4"), true));
            // A stub too short to give up any room: its traffic turns just beyond its end.
            Assert.IsNull(map.TryAdd(P(0, 0), P(0, 40), T("s2"), true));
            World world = map.ToWorld();
            Assert.AreEqual(2, world.Nodes.Count(n => n.DeadEnd));
            Node wide = world.Nodes.Single(n => n.DeadEnd && n.Pos.X > 100);
            Assert.AreEqual(2, world.Lanes.Count(l => l.Kind == LaneKind.Connector && l.Node == wide.Id), "each lane has its own way round");
            var sim = new Simulation(world, 5);
            sim.ArrivalsPerMinute = 14;
            // There is a light where the stub meets the avenue: have everyone obey it, so that any crash is the dead ends' doing.
            Grid.Run(sim, 400, () =>
            {
                foreach (Car c in sim.Cars) c.Drv.Compliance = 1;
            });
            Assert.AreEqual(0, sim.Crashes);
            Assert.Greater(sim.Left, 20);
        }

        [Test]
        public void AOneWayRoadCannotEndInADeadEnd()
        {
            var map = new EditableMap { Bound = 300 };
            Assert.IsNull(map.TryAdd(P(-300, 0), P(100, 0), T("s1"), true));
            Assert.AreEqual(1, map.Faults().Count);
            StringAssert.Contains("nowhere to go", map.Faults()[0].What);
            map.ToWorld();
            // Run on to the edge it is a through road, and nothing is wrong.
            Assert.IsNull(map.TryAdd(P(100, 0), P(300, 40), T("s1"), true));
            Assert.AreEqual(0, map.Faults().Count);
            // A two-way dead end is no fault at all.
            Assert.IsNull(map.TryAdd(P(0, 0), P(0, 150), T("s2"), true));
            Assert.AreEqual(0, map.Faults().Count);
        }

        // ---------------------------------------------------------------- levels

        /// <summary>One way of solving each level, in order.</summary>
        private static void Solve(int level, EditableMap map)
        {
            Action<Vec, Vec, string, Vec?, int> add = (p, q, id, via, height) => Assert.IsNull(map.TryAdd(p, q, T(id), true, via, height), "level " + level + ": " + id + " from " + p.X + "," + p.Y + " to " + q.X + "," + q.Y);
            switch (level)
            {
                case 0:
                    add(P(-260, 0), P(260, 60), "s2", null, 0);
                    break;
                case 1:
                    add(P(-260, 0), P(260, 0), "s2", null, 0);
                    add(P(0, -260), P(0, 260), "s2", null, 0);
                    Assert.IsNull(map.TryAddRoundabout(P(0, 0), 30, T("s2"), true));
                    break;
                case 2:
                    // Mill Road carries on north. A ramp leaves the highway alongside it and curves down to
                    // Mill Road; another leaves the end of Mill Road and curves up to run in alongside the highway.
                    add(P(0, -260), P(0, 20), "s2", null, 0);
                    add(P(-220, 130), P(0, -60), "s1", P(-75, 76), 0);
                    add(P(0, 20), P(220, 130), "s1", P(97, 101), 0);
                    break;
                case 3:
                    add(P(-60, -260), P(60, 260), "s2", null, 1);
                    break;
            }
        }

        [Test]
        public void EveryLevelStartsUnsolvedAndCanBeSolvedWithinItsBudget()
        {
            for (int i = 0; i < Levels.All.Length; i++)
            {
                Level level = Levels.All[i];
                EditableMap map = level.Start();
                Assert.AreEqual(0, map.Cost(), 0.01, level.Name + ": its own roads are free");
                Assert.IsTrue(map.Segments.All(s => s.Locked));
                var missing = new System.Collections.Generic.List<string>();
                Assert.Less(level.Connected(map.ToWorld(), missing), level.RouteCount, level.Name + " should not start solved");
                Assert.Greater(missing.Count, 0);
                // The level's own roads cannot be bulldozed, or built outside the area.
                Assert.IsFalse(map.RemoveNear(map.Nodes[map.Segments[0].A]));
                Assert.IsNotNull(map.TryAdd(P(level.Half + 40, 100), P(level.Half + 40, 200), T("s2"), false));

                Solve(i, map);
                World world = map.ToWorld();
                Assert.AreEqual(level.RouteCount, level.Connected(world), level.Name + ": every route should be joined up");
                Assert.LessOrEqual(map.Cost(), level.Budget, level.Name + ": the worked solution should be affordable");
                Assert.AreEqual(level.RouteCount, level.DemandFor(world).Count);
                // What the player built can be cleared; the level's own roads stay.
                EditableMap again = EditableMap.Parse(map.Serialize());
                again.Clear();
                Assert.IsTrue(again.Segments.Count > 0 && again.Segments.All(s => s.Locked), level.Name);
            }
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public void ASolvedLevelCarriesItsTrafficToTheEnd(int index)
        {
            Level level = Levels.All[index];
            EditableMap map = level.Start();
            Solve(index, map);
            World world = map.ToWorld();
            var sim = new Simulation(world, 11) { UseRoutes = true, ArrivalsPerMinute = level.Arrivals };
            sim.Demand = level.DemandFor(world);
            Action obey = () =>
            {
                foreach (Car c in sim.Cars) c.Drv.Compliance = 1;
            };
            Assert.AreEqual(0, level.Stars(sim, map.Cost(), world), "no stars before any traffic has run");
            for (int minutes = 0; minutes < 12 && sim.Arrived < level.TripsToFinish; minutes++) Grid.Run(sim, 60, obey);
            Assert.AreEqual(0, sim.Crashes, level.Name);
            Assert.GreaterOrEqual(sim.Arrived, level.TripsToFinish, level.Name + ": not enough journeys finished in twelve minutes");
            Assert.GreaterOrEqual(level.Stars(sim, map.Cost(), world), 1, level.Name);
            TestContext.WriteLine(level.Name + ": cost " + map.Cost().ToString("F0") + " of " + level.Budget + ", slowdown " + (sim.TripSeconds / sim.TripIdeal).ToString("F2") + ", stars " + level.Stars(sim, map.Cost(), world) + ", time " + sim.Time.ToString("F0") + ", strays " + sim.Strays);
        }

        /// <summary>A small drawn town: roads at odd angles, every lane count, a one-way street, quiet and fast roads.</summary>
        private static EditableMap Town()
        {
            var map = new EditableMap();
            Action<Vec, Vec, int, int> add = (p, q, lanes, mph) => Assert.IsNull(map.TryAdd(p, q, lanes, mph, true), "from " + p.X + "," + p.Y + " to " + q.X + "," + q.Y);
            add(P(-320, 0), P(320, 20), 4, 40);          // main road, slightly off level
            add(P(-150, -260), P(-120, 260), 2, 40);     // crosses the main road
            add(P(140, -250), P(100, 250), 2, 25);       // crosses it at a slant
            add(P(-135, 150), P(108, 160), 2, 25);       // joins the two side roads north of the main road
            add(P(-140, -150), P(128, -140), 1, 25);     // one-way street joining them south of it
            add(P(108, 160), P(300, 250), 2, 25);        // carries on from an existing intersection
            return map;
        }

        [Test]
        public void ADrawnTownBuildsWithSensibleIntersections()
        {
            World world = Town().ToWorld();
            Assert.GreaterOrEqual(world.Nodes.Count(n => n.Controlled), 6);
            Assert.GreaterOrEqual(world.Entries.Count, 6);
            foreach (Lane lane in world.Lanes)
            {
                if (lane.Kind != LaneKind.Road || lane.IsExit) continue;
                Assert.IsTrue(lane.Connectors != null && lane.Connectors.Any(c => c >= 0), "lane " + lane.Id + " leads nowhere");
            }
        }

        [TestCase(3u)]
        [TestCase(8u)]
        public void TrafficRunsOnADrawnTownWithoutCrashingOrSeizingUp(uint seed)
        {
            var sim = new Simulation(Town().ToWorld(), seed);
            sim.ArrivalsPerMinute = 5;
            double longestStop = 0;
            var stopped = new System.Collections.Generic.Dictionary<int, double>();
            Action watch = () =>
            {
                foreach (Car c in sim.Cars)
                {
                    c.Drv.Compliance = 1; // everyone obeys the lights here; running reds causes crashes of its own
                    double t;
                    stopped.TryGetValue(c.Id, out t);
                    t = c.V < 0.1 ? t + Simulation.Dt : 0;
                    stopped[c.Id] = t;
                    longestStop = Math.Max(longestStop, t);
                }
            };
            Grid.Run(sim, 300, watch);
            int leftByHalfway = sim.Left;
            Grid.Run(sim, 300, watch);
            Assert.AreEqual(0, sim.Crashes);
            Assert.Greater(leftByHalfway, 40);
            Assert.Greater(sim.Left - leftByHalfway, 40);
            // The four-way stop where the side streets meet backs up at this traffic level; a long wait is fine, a standstill is not.
            Assert.Less(longestStop, 300, "a vehicle sat still for five minutes");
        }
    }
}
