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

    public class LabWorldTests
    {
        private static readonly World W = LabWorld.Build();

        [Test]
        public void HasTwoLanesInAndOutOfEveryArm()
        {
            for (int arm = 0; arm < 4; arm++)
            {
                Assert.AreEqual(2, W.Inbound[arm].Length);
                Assert.AreEqual(2, W.Outbound[arm].Length);
            }
        }

        [Test]
        public void KeepsNeighboringLanesOneLaneWidthApart()
        {
            Lane left = W.Lanes[W.Inbound[0][0]];
            Lane right = W.Lanes[W.Inbound[0][1]];
            Pose mid = left.Path.At(left.Length / 2);
            Assert.AreEqual(LabWorld.LaneW, right.Path.Project(mid.X, mid.Y).D, 0.05);
        }

        [Test]
        public void LeftTurnsConflictWithOncomingThroughTrafficButNotTheOppositeLeftTurn()
        {
            Lane northIn = W.Lanes[W.Inbound[0][0]];
            Lane southIn = W.Lanes[W.Inbound[2][0]];
            Lane northLeft = W.Lanes[northIn.Connectors[(int)Turn.Left]];
            var foes = northLeft.Conflicts.Select(c => c.Foe).ToList();
            Assert.Contains(southIn.Connectors[(int)Turn.Straight], foes);
            Assert.IsFalse(foes.Contains(southIn.Connectors[(int)Turn.Left]));
        }
    }

    public class SimulationTests
    {
        private static readonly World W = LabWorld.Build();

        private static void Run(Simulation sim, double seconds, Action each = null)
        {
            int steps = (int)Math.Ceiling(seconds / Simulation.Dt - 1e-9);
            for (int i = 0; i < steps; i++)
            {
                sim.Step();
                if (each != null) each();
            }
        }

        /// <summary>The [left, right] lanes arriving at an arm (0 north, 1 east, 2 south, 3 west).</summary>
        private static Lane[] Inbound(int arm)
        {
            return W.Inbound[arm].Select(id => W.Lanes[id]).ToArray();
        }

        [Test]
        public void SlowsForASlowerCarAheadWithoutHittingIt()
        {
            var sim = new Simulation(W, 1);
            Lane lane = Inbound(1)[0];
            DriverParams slowDriver = DriverParams.Normal;
            slowDriver.Speed = 0.6;
            Car slow = sim.AddCar(lane.Id, 90, 5, slowDriver, Turn.Straight);
            Car fast = sim.AddCar(lane.Id, 30, 13, null, Turn.Straight);
            // Keep both in their lane so this only tests following.
            slow.LcCooldownUntil = fast.LcCooldownUntil = 1e9;
            double minGap = double.PositiveInfinity;
            Run(sim, 12, () => minGap = Math.Min(minGap, fast.Gap));
            Assert.AreEqual(0, sim.Crashes);
            Assert.Greater(minGap, 1.5);
        }

        [Test]
        public void StopsAtARedLightShortOfTheLineAndGoesOnGreen()
        {
            var sim = new Simulation(W, 1);
            sim.Junction.Hold = true;
            sim.Junction.SetPhase(3, 0); // east-west green: north arm is red
            Lane lane = Inbound(0)[0];
            Car car = sim.AddCar(lane.Id, lane.Length - 80, 12, null, Turn.Straight);
            Run(sim, 20);
            Assert.AreEqual(lane.Id, car.Lane);
            Assert.Less(car.V, 0.3);
            double toLine = lane.Length - car.S - car.Len / 2;
            Assert.Greater(toLine, 0);
            Assert.Less(toLine, 3);

            sim.Junction.SetPhase(0, sim.Time);
            Run(sim, 10);
            Assert.AreNotEqual(lane.Id, car.Lane);
            Assert.AreEqual(1, sim.Passes);
        }

        [Test]
        public void MakesALeftTurnWaitForOncomingTraffic()
        {
            var sim = new Simulation(W, 1);
            sim.Junction.Hold = true; // north-south green
            Lane north = Inbound(0)[0];
            Lane south = Inbound(2)[0];
            Car turner = sim.AddCar(north.Id, north.Length - 30, 8, null, Turn.Left);
            Car oncoming = sim.AddCar(south.Id, south.Length - 45, 12, null, Turn.Straight);
            bool yielded = false;
            Run(sim, 14, () => yielded |= turner.State == CarState.Yield);
            Assert.IsTrue(yielded);
            Assert.AreEqual(0, sim.Crashes);
            Assert.AreEqual(2, sim.Passes);
            Assert.IsFalse(oncoming.Crashed);
        }

        [Test]
        public void ComesToAFullStopAtAnAllWayStopEvenWithNobodyAround()
        {
            var sim = new Simulation(W, 1);
            sim.SetMode(JunctionMode.AllWay);
            Lane lane = Inbound(3)[0];
            Car car = sim.AddCar(lane.Id, lane.Length - 70, 12, null, Turn.Straight);
            double slowest = double.PositiveInfinity;
            Run(sim, 25, () =>
            {
                if (car.Lane == lane.Id && lane.Length - car.S < 8) slowest = Math.Min(slowest, car.V);
            });
            Assert.Less(slowest, 0.4);
            Assert.AreEqual(1, sim.Passes);
        }

        [Test]
        public void MergesOutOfALaneThatEnds()
        {
            var sim = new Simulation(W, 1);
            Lane lane = W.Lanes.First(l => l.Kind == LaneKind.Road && l.Connectors == null);
            Car car = sim.AddCar(lane.Id, 20, 10);
            Run(sim, 20);
            Assert.AreNotEqual(lane.Id, car.Lane);
            Assert.AreEqual(0, sim.Crashes);
        }

        [Test]
        public void StartlesTheDriverBehindWhenCutOff()
        {
            var sim = new Simulation(W, 1);
            Lane[] lanes = Inbound(1);
            Car victim = sim.AddCar(lanes[0].Id, 60, 12, DriverParams.Calm, Turn.Straight);
            Car cutter = sim.AddCar(lanes[1].Id, 69, 9, null, Turn.Straight);
            victim.LcCooldownUntil = 1e9;
            Run(sim, 0.5);
            Assert.AreSame(cutter, sim.ForceCutIn(cutter.Id));
            Assert.AreEqual(lanes[0].Id, cutter.Lane);
            bool startled = false;
            double hardest = 0;
            Run(sim, 5, () =>
            {
                startled |= victim.State == CarState.Startled;
                hardest = Math.Min(hardest, victim.A);
            });
            Assert.IsTrue(startled);
            Assert.Less(hardest, -3);
        }

        [TestCase(JunctionMode.Signal)]
        [TestCase(JunctionMode.AllWay)]
        public void RunsFortyMixedDriversForEightMinutesWithoutACrashOrGridlock(JunctionMode mode)
        {
            var sim = new Simulation(W, 11);
            sim.SetMode(mode);
            for (int i = 0; i < 40; i++) sim.Spawn(DriverParams.Normal, 0.4);
            // Everyone obeys the rules here; rule-breaking is tested by hand in the lab.
            foreach (var c in sim.Cars) c.Drv.Compliance = 1;
            Assert.AreEqual(40, sim.Cars.Count);
            Run(sim, 240);
            int halfway = sim.Passes;
            Run(sim, 240);
            Assert.AreEqual(0, sim.Crashes);
            Assert.Greater(halfway, 40);
            Assert.Greater(sim.Passes - halfway, 40);
        }

        [Test]
        public void ReplaysExactlyFromTheSameSeed()
        {
            Assert.AreEqual(Snapshot(), Snapshot());
        }

        private static string Snapshot()
        {
            var sim = new Simulation(W, 5);
            for (int i = 0; i < 25; i++) sim.Spawn(DriverParams.Normal, 0.5);
            Run(sim, 90);
            return string.Join("|", sim.Cars.Select(c => c.Id + ":" + c.Lane + ":" + c.S.ToString("F6") + ":" + c.V.ToString("F6")));
        }
    }
}
