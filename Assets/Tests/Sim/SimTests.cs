using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Traffic.Sim;

namespace Traffic.Tests
{
    public class PathTests
    {
        private static readonly LanePath P = new LanePath(new[] { new Vec(0, 0), new Vec(10, 0), new Vec(10, 10) });

        [Test]
        public void MeasuresLengthAndSamplesAlongIt()
        {
            Assert.AreEqual(20, P.Length, 1e-9);
            Assert.AreEqual(5, P.At(5).X, 1e-9);
            Assert.AreEqual(10, P.At(15).X, 1e-9);
            Assert.AreEqual(5, P.At(15).Y, 1e-9);
            Assert.AreEqual(Math.PI / 2, P.At(15).H, 1e-9);
        }

        [Test]
        public void ProjectsAPointOntoTheClosestSpot()
        {
            Projection pr = P.Project(4, 3);
            Assert.AreEqual(4, pr.S, 1e-9);
            Assert.AreEqual(3, pr.D, 1e-9);
        }

        [Test]
        public void OffsetsToTheRightOfTravel()
        {
            Vec[] east = Geo.Offset(new[] { new Vec(0, 0), new Vec(10, 0) }, 2);
            Assert.AreEqual(-2, east[0].Y, 1e-9); // heading east, right is south
        }
    }

    /// <summary>Shared helpers for finding places on the lab grid.</summary>
    public static class Grid
    {
        public static readonly World W = LabWorld.Build();

        /// <summary>The intersection at a grid position: row 0 is south, column 0 is west.</summary>
        public static Node NodeAt(int row, int col)
        {
            return W.Nodes.First(n => Math.Abs(n.Pos.X - (col - 1) * LabWorld.Block) < 1 && Math.Abs(n.Pos.Y - (row - 1) * LabWorld.Block) < 1);
        }

        public static Node Center => NodeAt(1, 1);

        /// <summary>The road arriving at a node from the given compass side ('N', 'E', 'S' or 'W').</summary>
        public static Approach Arm(Node node, char side)
        {
            double want = side == 'E' ? 0 : side == 'N' ? Math.PI / 2 : side == 'W' ? Math.PI : -Math.PI / 2;
            return node.Arms.First(a => Math.Abs(Math.Cos(a.Angle) - Math.Cos(want)) < 0.01 && Math.Abs(Math.Sin(a.Angle) - Math.Sin(want)) < 0.01);
        }

        /// <summary>Lanes arriving at a node from a compass side, left to right.</summary>
        public static Lane[] Inbound(Node node, char side)
        {
            return Arm(node, side).Inbound.Select(id => W.Lanes[id]).ToArray();
        }

        public static void Run(Simulation sim, double seconds, Action each = null)
        {
            int steps = (int)Math.Ceiling(seconds / Simulation.Dt - 1e-9);
            for (int i = 0; i < steps; i++)
            {
                sim.Step();
                if (each != null) each();
            }
        }
    }

    public class LabWorldTests
    {
        [Test]
        public void HasNineIntersectionsOfWhichFiveAreControlled()
        {
            Assert.AreEqual(9, Grid.W.Nodes.Count);
            Assert.AreEqual(5, Grid.W.Nodes.Count(n => n.Controlled));
        }

        [Test]
        public void MainStreetArrivesAtTheCenterWithTurnPockets()
        {
            Lane[] lanes = Grid.Inbound(Grid.Center, 'W');
            Assert.AreEqual(4, lanes.Length);
            Assert.IsTrue(lanes[0].Allows(Turn.Left) && lanes[0].Allows(Turn.UTurn) && !lanes[0].Allows(Turn.Straight));
            Assert.IsTrue(lanes[1].Allows(Turn.Straight) && !lanes[1].Allows(Turn.Left));
            Assert.IsTrue(lanes[3].Allows(Turn.Right) && !lanes[3].Allows(Turn.Straight));
            Assert.Less(lanes[0].Length, lanes[1].Length); // the pocket is short
        }

        [Test]
        public void UTurnsExistOnlyWhereAllowed()
        {
            Assert.IsTrue(Grid.Inbound(Grid.Center, 'E')[0].Allows(Turn.UTurn));
            Assert.IsFalse(Grid.Inbound(Grid.Center, 'N').Any(l => l.Allows(Turn.UTurn)));
            Assert.IsFalse(Grid.Inbound(Grid.Center, 'S').Any(l => l.Allows(Turn.UTurn)));
        }

        [Test]
        public void EveryLaneLeadsSomewhere()
        {
            foreach (Lane lane in Grid.W.Lanes)
            {
                if (lane.Kind == LaneKind.Connector) Assert.GreaterOrEqual(lane.Next, 0);
                else if (lane.Connectors == null) Assert.AreNotEqual(0, lane.MergeSide);
                else Assert.IsTrue(lane.Connectors.Any(c => c >= 0), "lane " + lane.Id + " reaches a stop line with no way through");
            }
        }

        [Test]
        public void LeftTurnsConflictWithOncomingThroughTrafficButNotTheOppositeLeftTurn()
        {
            Lane[] west = Grid.Inbound(Grid.Center, 'W');
            Lane[] east = Grid.Inbound(Grid.Center, 'E');
            Lane westLeft = Grid.W.Lanes[west[0].Connectors[(int)Turn.Left]];
            var foes = westLeft.Conflicts.Select(c => c.Foe).ToList();
            Assert.Contains(east[1].Connectors[(int)Turn.Straight], foes);
            Assert.IsFalse(foes.Contains(east[0].Connectors[(int)Turn.Left]));
        }
    }

    public class SimulationTests
    {
        private static World W => Grid.W;

        [Test]
        public void SlowsForASlowerCarAheadWithoutHittingIt()
        {
            var sim = new Simulation(W, 1);
            Lane lane = Grid.Inbound(Grid.NodeAt(1, 0), 'S')[0];
            DriverParams slowDriver = DriverParams.Normal;
            slowDriver.Speed = 0.6;
            Car slow = sim.AddCar(lane.Id, 70, 5, slowDriver, Turn.Straight);
            Car fast = sim.AddCar(lane.Id, 15, 13, null, Turn.Straight);
            // Keep both in their lane so this only tests following.
            slow.LcCooldownUntil = fast.LcCooldownUntil = 1e9;
            double minGap = double.PositiveInfinity;
            Grid.Run(sim, 8, () => minGap = Math.Min(minGap, fast.Gap));
            Assert.AreEqual(0, sim.Crashes);
            Assert.Greater(minGap, 1.5);
        }

        [Test]
        public void StopsAtARedLightShortOfTheLineAndGoesOnGreen()
        {
            var sim = new Simulation(W, 1);
            Node node = Grid.NodeAt(1, 0); // plain light
            Junction jn = sim.Junctions[node.Id];
            jn.Hold = true;
            jn.ShowGreen(1, false, 0); // east-west green: the south arm is red
            Lane lane = Grid.Inbound(node, 'S')[0];
            Car car = sim.AddCar(lane.Id, lane.Length - 80, 12, null, Turn.Straight);
            Grid.Run(sim, 20);
            Assert.AreEqual(lane.Id, car.Lane);
            Assert.Less(car.V, 0.3);
            double toLine = lane.Length - car.S - car.Len / 2;
            Assert.Greater(toLine, 0);
            Assert.Less(toLine, 3);

            jn.ShowGreen(0, false, sim.Time);
            Grid.Run(sim, 10);
            Assert.AreNotEqual(lane.Id, car.Lane);
            Assert.AreEqual(1, sim.Passes);
        }

        [Test]
        public void MakesALeftTurnWaitForOncomingTrafficAtAPlainLight()
        {
            var sim = new Simulation(W, 1);
            Node node = Grid.NodeAt(1, 0); // west T: arms north, south and east
            Junction jn = sim.Junctions[node.Id];
            jn.Hold = true;
            jn.ShowGreen(0, false, 0); // north-south green
            Lane north = Grid.Inbound(node, 'N')[0]; // southbound, left turn goes east
            Lane south = Grid.Inbound(node, 'S')[0]; // northbound, straight on
            Car turner = sim.AddCar(north.Id, north.Length - 30, 8, null, Turn.Left);
            Car oncoming = sim.AddCar(south.Id, south.Length - 45, 12, null, Turn.Straight);
            turner.LcCooldownUntil = oncoming.LcCooldownUntil = 1e9;
            bool yielded = false;
            Grid.Run(sim, 14, () => yielded |= turner.State == CarState.Yield);
            Assert.IsTrue(yielded);
            Assert.AreEqual(0, sim.Crashes);
            Assert.AreEqual(2, sim.Passes);
        }

        [Test]
        public void HoldsLeftTurnsForTheArrowAtTheCenter()
        {
            var sim = new Simulation(W, 1);
            Junction jn = sim.Junctions[Grid.Center.Id];
            jn.Hold = true;
            jn.ShowGreen(1, false, 0); // east-west through green, left arrow red
            Lane[] west = Grid.Inbound(Grid.Center, 'W');
            Car turner = sim.AddCar(west[0].Id, 10, 6, null, Turn.Left);
            Car through = sim.AddCar(west[1].Id, west[1].Length - 40, 10, null, Turn.Straight);
            through.LcCooldownUntil = 1e9;
            Grid.Run(sim, 15);
            Assert.AreEqual(west[0].Id, turner.Lane, "the left-turner should still be waiting");
            Assert.Less(turner.V, 0.3);
            Assert.AreEqual(1, sim.Passes, "the through car should have gone");

            jn.ShowGreen(1, true, sim.Time); // left arrow
            Grid.Run(sim, 8);
            Assert.AreNotEqual(west[0].Id, turner.Lane, "the left-turner should have gone on the arrow");
            Assert.AreEqual(0, sim.Crashes);
        }

        [Test]
        public void MakesAUTurnWhereAllowed()
        {
            var sim = new Simulation(W, 1);
            Junction jn = sim.Junctions[Grid.Center.Id];
            jn.Hold = true;
            jn.ShowGreen(1, true, 0);
            Lane pocket = Grid.Inbound(Grid.Center, 'W')[0];
            Car car = sim.AddCar(pocket.Id, 10, 6, null, Turn.UTurn);
            for (int i = 0; i < 600 && sim.Passes == 0; i++) sim.Step();
            Assert.AreEqual(1, sim.Passes);
            // Back on Main St heading west, toward the west T.
            Assert.AreEqual(Grid.NodeAt(1, 0).Id, W.Lanes[car.Lane].ApproachNode);
            Assert.AreEqual(0, sim.Crashes);
        }

        [Test]
        public void GetsIntoTheTurnPocketForALeftTurn()
        {
            var sim = new Simulation(W, 1);
            sim.Junctions[Grid.Center.Id].Hold = true;
            sim.Junctions[Grid.Center.Id].ShowGreen(0, false, 0); // keep east-west red so the car has to wait
            Lane[] west = Grid.Inbound(Grid.Center, 'W');
            Car car = sim.AddCar(west[2].Id, 10, 10, null, Turn.Left); // starts two lanes away from the pocket
            Grid.Run(sim, 25);
            Assert.AreEqual(west[0].Id, car.Lane);
            Assert.AreEqual(Turn.Left, car.Turn);
        }

        [Test]
        public void ComesToAFullStopAtAnAllWayStopEvenWithNobodyAround()
        {
            var sim = new Simulation(W, 1);
            Node node = Grid.NodeAt(1, 2); // east T
            Lane lane = Grid.Inbound(node, 'S')[0];
            Car car = sim.AddCar(lane.Id, lane.Length - 70, 12, null, Turn.Straight);
            double slowest = double.PositiveInfinity;
            Grid.Run(sim, 25, () =>
            {
                if (car.Lane == lane.Id && lane.Length - car.S < 8) slowest = Math.Min(slowest, car.V);
            });
            Assert.Less(slowest, 0.4);
            Assert.AreEqual(1, sim.Passes);
        }

        [TestCase(1)]
        [TestCase(-1)]
        public void MergesOutOfALaneThatEnds(int mergeSide)
        {
            var sim = new Simulation(W, 1);
            Lane lane = W.Lanes.First(l => l.Kind == LaneKind.Road && l.Connectors == null && l.MergeSide == mergeSide);
            Car car = sim.AddCar(lane.Id, 8, 10);
            Grid.Run(sim, 15);
            Assert.AreNotEqual(lane.Id, car.Lane);
            Assert.AreEqual(0, sim.Crashes);
        }

        [Test]
        public void StartlesTheDriverBehindWhenCutOff()
        {
            var sim = new Simulation(W, 1);
            Lane[] lanes = Grid.Inbound(Grid.NodeAt(1, 0), 'S');
            Car victim = sim.AddCar(lanes[0].Id, 30, 12, DriverParams.Calm, Turn.Straight);
            Car cutter = sim.AddCar(lanes[1].Id, 39, 9, null, Turn.Straight);
            victim.LcCooldownUntil = 1e9;
            Grid.Run(sim, 0.5);
            Assert.AreSame(cutter, sim.ForceCutIn(cutter.Id));
            Assert.AreEqual(lanes[0].Id, cutter.Lane);
            bool startled = false;
            double hardest = 0;
            Grid.Run(sim, 5, () =>
            {
                startled |= victim.State == CarState.Startled;
                hardest = Math.Min(hardest, victim.A);
            });
            Assert.IsTrue(startled);
            Assert.Less(hardest, -3);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void RunsSixtyMixedDriversForEightMinutesWithoutACrashOrGridlock(bool allStopSigns)
        {
            var sim = new Simulation(W, 11);
            if (allStopSigns) sim.SetAllModes(JunctionMode.AllWay);
            for (int i = 0; i < 60; i++) sim.Spawn(DriverParams.Normal, 0.4);
            // Everyone obeys the rules here; rule-breaking is tested by hand in the lab.
            foreach (var c in sim.Cars) c.Drv.Compliance = 1;
            Assert.AreEqual(60, sim.Cars.Count);
            var stopped = new Dictionary<int, double>();
            double longestStop = 0;
            Action watch = () =>
            {
                foreach (Car c in sim.Cars)
                {
                    double t;
                    stopped.TryGetValue(c.Id, out t);
                    t = c.V < 0.1 ? t + Simulation.Dt : 0;
                    stopped[c.Id] = t;
                    longestStop = Math.Max(longestStop, t);
                }
            };
            Grid.Run(sim, 240, watch);
            int halfway = sim.Passes;
            Grid.Run(sim, 240, watch);
            Assert.AreEqual(0, sim.Crashes);
            Assert.Less(longestStop, 120, "a car sat still for two minutes");
            Assert.Greater(halfway, 200);
            Assert.Greater(sim.Passes - halfway, 200);
        }

        [Test]
        public void ReplaysExactlyFromTheSameSeed()
        {
            Assert.AreEqual(Snapshot(), Snapshot());
        }

        private static string Snapshot()
        {
            var sim = new Simulation(W, 5);
            for (int i = 0; i < 40; i++) sim.Spawn(DriverParams.Normal, 0.5);
            Grid.Run(sim, 90);
            return string.Join("|", sim.Cars.Select(c => c.Id + ":" + c.Lane + ":" + c.S.ToString("F6") + ":" + c.V.ToString("F6")));
        }
    }
}
