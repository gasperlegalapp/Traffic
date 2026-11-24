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
            Assert.AreEqual(1, map.ToWorld().Nodes.Count(n => n.Arms.Count == 2), "the shared end is a bend");
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
            Assert.IsNull(map.TryAdd(P(0, 0), P(200, 0), 2, 40, true, P(100, 1)));
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
        public void ARampOnTheWrongSideOrAgainstTheFlowIsRefused()
        {
            var map = new EditableMap();
            Assert.IsNull(map.TryAdd(P(-420, 0), P(420, 0), T("h2"), true));
            // Joining an eastbound highway from the north would mean merging from the left.
            Assert.IsNotNull(map.TryAdd(P(-380, 150), P(-150, 0), T("h1"), true, P(-250, 45)));
            // Running in against the traffic.
            Assert.IsNotNull(map.TryAdd(P(380, -150), P(150, 0), T("h1"), true, P(250, -45)));
            // A two-way road cannot merge at all.
            Assert.IsNotNull(map.TryAdd(P(-380, -150), P(-150, 0), T("s2"), true, P(-250, -45)));
            Assert.AreEqual(1, map.Segments.Count);
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
