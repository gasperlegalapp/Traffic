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
        public void ACurveBendsTowardItsControlPoint()
        {
            var map = new EditableMap();
            Assert.IsNull(map.TryAdd(P(0, 0), P(200, 200), 2, 40, true, P(200, 0)));
            Assert.AreEqual(1, map.Segments.Count);
            Assert.IsTrue(map.Segments[0].Curved);
            World world = map.ToWorld();
            // The road should pass well to the lower right of the straight line between its ends.
            Lane lane = world.Lanes.First(l => l.Kind == LaneKind.Road);
            double furthest = 0;
            for (double d = 0; d <= lane.Length; d += 5)
            {
                PathPose at = lane.Path.At(d);
                furthest = Math.Max(furthest, (at.X - at.Y) / Math.Sqrt(2));
            }
            Assert.Greater(furthest, 40);
        }

        [Test]
        public void AControlPointOnTheLineJustMakesAStraightRoad()
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
            Assert.IsNull(map.TryAdd(P(-200, -150), P(200, -150), 2, 40, true, P(0, 400))); // an arch that crosses the road twice
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
        }

        [Test]
        public void RefusesACurveThatIsTooTight()
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

        /// <summary>A drawn map made mostly of curves: a winding main road, a loop off it, and a curved one-way cut-through.</summary>
        public static EditableMap CurvyTown()
        {
            var map = new EditableMap();
            Action<Vec, Vec, int, int, Vec?> add = (p, q, lanes, mph, c) => Assert.IsNull(map.TryAdd(p, q, lanes, mph, true, c), "from " + p.X + "," + p.Y + " to " + q.X + "," + q.Y);
            // The main road winds across the map in two bends.
            Vec west = P(-350, -80), middle = P(-60, 40), east = P(350, -40);
            Vec bendOne = P(-200, 120), bendTwo = P(120, -120);
            add(west, middle, 4, 40, bendOne);
            add(middle, east, 4, 40, bendTwo);
            // A street loops north from the middle of it and comes back down further east.
            Vec top = P(60, 300), corner = P(300, 120), loopBend = P(280, 320);
            add(middle, top, 2, 25, P(-120, 220));
            add(top, corner, 2, 25, loopBend);
            add(corner, EditableMap.CurvePoint(middle, bendTwo, east, 0.7), 2, 25, null);
            // A curved road comes in from the south-west and joins the first bend.
            add(P(-250, -300), EditableMap.CurvePoint(west, bendOne, middle, 0.45), 2, 40, P(-150, -150));
            // A one-way street leaves the loop, crosses the main road and runs off the map to the south.
            add(EditableMap.CurvePoint(top, loopBend, corner, 0.45), P(140, -300), 1, 25, P(60, -40));
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
