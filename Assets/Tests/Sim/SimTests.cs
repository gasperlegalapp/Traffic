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

        [TestCase(true)]
        [TestCase(false)]
        public void TurnsRightOnRedOnlyWhenAllowedAndOnlyAfterStopping(bool allowed)
        {
            var sim = new Simulation(W, 1);
            sim.RightOnRed = allowed;
            Node node = Grid.NodeAt(1, 0); // west T
            Junction jn = sim.Junctions[node.Id];
            jn.Hold = true;
            jn.ShowGreen(1, false, 0); // east-west green: the south arm stays red
            Lane lane = Grid.Inbound(node, 'S')[1]; // right lane: straight or right
            Car car = sim.AddCar(lane.Id, lane.Length - 60, 11, null, Turn.Right);
            car.LcCooldownUntil = 1e9;
            double slowest = double.PositiveInfinity;
            Grid.Run(sim, 25, () =>
            {
                if (car.Lane == lane.Id && lane.Length - car.S < 8) slowest = Math.Min(slowest, car.V);
            });
            Assert.Less(slowest, 0.4, "should stop at the red first");
            Assert.AreEqual(allowed ? 1 : 0, sim.Passes);
            Assert.AreEqual(0, sim.Crashes);
        }

        [Test]
        public void WaitsForCrossTrafficBeforeTurningRightOnRed()
        {
            var sim = new Simulation(W, 1);
            Node node = Grid.NodeAt(1, 0); // west T: Main St leaves to the east
            Junction jn = sim.Junctions[node.Id];
            jn.Hold = true;
            jn.ShowGreen(0, false, 0); // north-south green: the east arm (Main St westbound) is red
            Lane right = Grid.Inbound(node, 'E')[1]; // turns right, to the north
            Lane through = Grid.Inbound(node, 'S')[1]; // northbound through traffic, into the same lane
            Car turner = sim.AddCar(right.Id, right.Length - 12, 4, null, Turn.Right);
            for (int i = 0; i < 4; i++) sim.AddCar(through.Id, through.Length - 25 - i * 14, 10, null, Turn.Straight).LcCooldownUntil = 1e9;
            int passesWhenTurnerWent = -1;
            Grid.Run(sim, 30, () =>
            {
                bool onNewRoad = turner.Lane != right.Id && W.Lanes[turner.Lane].Kind == LaneKind.Road;
                if (passesWhenTurnerWent < 0 && onNewRoad) passesWhenTurnerWent = sim.Passes;
            });
            Assert.AreEqual(0, sim.Crashes);
            Assert.AreEqual(5, sim.Passes);
            Assert.GreaterOrEqual(passesWhenTurnerWent, 3, "the turner should let most of the stream go first");
        }

        [Test]
        public void AManiacPushesThroughARedLightAndFrightensCrossTraffic()
        {
            var sim = new Simulation(W, 1);
            Node node = Grid.NodeAt(1, 0);
            Junction jn = sim.Junctions[node.Id];
            jn.Hold = true;
            jn.ShowGreen(0, false, 0); // north-south green: Main St westbound is red
            Lane red = Grid.Inbound(node, 'E')[0]; // turns left, across the northbound stream
            Lane green = Grid.Inbound(node, 'S')[0];
            Car maniac = sim.AddCar(red.Id, red.Length - 25, 8, SpecialDriver.All[0].Make(), Turn.Left);
            var others = new List<Car>();
            for (int i = 0; i < 5; i++) others.Add(sim.AddCar(green.Id, green.Length - 45 - i * 15, 10, null, Turn.Straight));
            foreach (Car c in others) c.LcCooldownUntil = 1e9;
            bool scared = false;
            Grid.Run(sim, 30, () => scared |= others.Any(c => c.State == CarState.Scared));
            Assert.AreNotEqual(red.Id, maniac.Lane, "the maniac should not have waited for green");
            Assert.IsTrue(scared, "someone should have been frightened");
            Assert.AreEqual(0, sim.Crashes);
        }

        [Test]
        public void ADriverOnThePhoneLooksAwayAndADrunkDriverDrifts()
        {
            var sim = new Simulation(W, 3);
            Car phone = sim.SpawnSpecial(SpecialDriver.All[2]);
            Car drunk = sim.SpawnSpecial(SpecialDriver.All[3]);
            bool lookedAway = false;
            double drift = 0;
            Grid.Run(sim, 60, () =>
            {
                lookedAway |= phone.State == CarState.Distracted;
                drift = Math.Max(drift, Math.Abs(drunk.Wobble));
            });
            Assert.IsTrue(lookedAway);
            Assert.Greater(drift, 0.8, "the drunk driver should stray over the lane line");
            Assert.AreEqual("ON PHONE", phone.Special);
        }

        [Test]
        public void OrdinaryDriversHangBackFromADrunkDriverInTheNextLane()
        {
            var sim = new Simulation(W, 1);
            Lane[] lanes = Grid.Inbound(Grid.NodeAt(1, 0), 'S');
            DriverParams slowDrunk = SpecialDriver.All[3].Make();
            slowDrunk.Speed = 0.6;
            Car drunk = sim.AddCar(lanes[0].Id, 40, 7, slowDrunk, Turn.Straight);
            Car other = sim.AddCar(lanes[1].Id, 10, 12, null, Turn.Straight);
            drunk.LcCooldownUntil = other.LcCooldownUntil = 1e9;
            bool wary = false;
            double furthestAhead = double.NegativeInfinity;
            Grid.Run(sim, 6, () =>
            {
                wary |= other.State == CarState.Wary;
                furthestAhead = Math.Max(furthestAhead, other.S - drunk.S);
            });
            Assert.IsTrue(wary);
            Assert.Less(furthestAhead, 0, "should not pass the drunk driver");
        }

        [Test]
        public void WithCrashesOffACarThatReactsTooLateIsHeldBehindTheOneItHit()
        {
            var sim = new Simulation(W, 1);
            sim.CrashesEnabled = false;
            Node node = Grid.NodeAt(1, 0);
            sim.Junctions[node.Id].Hold = true;
            sim.Junctions[node.Id].ShowGreen(1, false, 0); // the south arm is red
            Lane lane = Grid.Inbound(node, 'S')[0];
            Car stopped = sim.AddCar(lane.Id, lane.Length - 6, 0, null, Turn.Straight);
            var blind = new DriverParams(1.6, 1.3, 0.3, 1, 0, 1) { Distraction = 1 };
            Car late = sim.AddCar(lane.Id, lane.Length - 60, 14, blind, Turn.Straight);
            stopped.LcCooldownUntil = late.LcCooldownUntil = 1e9;
            late.LapseUntil = 20; // staring at the phone the whole way
            double closest = double.PositiveInfinity;
            Grid.Run(sim, 10, () => closest = Math.Min(closest, stopped.S - late.S));
            Assert.AreEqual(0, sim.Crashes);
            Assert.GreaterOrEqual(sim.Bumps, 1);
            Assert.Greater(closest, late.Len - 0.6, "the cars should not end up inside each other");
        }

        [Test]
        public void TheGridKeepsMovingWithSpecialDriversInTheMix()
        {
            var sim = new Simulation(W, 21);
            sim.CrashesEnabled = false;
            for (int i = 0; i < 60; i++) sim.Spawn(DriverParams.Normal, 0.4);
            foreach (SpecialDriver kind in SpecialDriver.All)
            {
                Assert.IsNotNull(sim.SpawnSpecial(kind));
                Assert.IsNotNull(sim.SpawnSpecial(kind));
            }
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
            Assert.Greater(halfway, 150);
            Assert.Greater(sim.Passes - halfway, 150);
            Assert.Less(longestStop, 180, "a car sat still for three minutes");
        }

        [Test]
        public void ATruckIsSlowerOffTheLineThanACarAndAMotorcycleIsQuicker()
        {
            var sim = new Simulation(W, 1);
            Lane lane = Grid.Inbound(Grid.NodeAt(1, 0), 'S')[0];
            Lane other = Grid.Inbound(Grid.NodeAt(1, 0), 'N')[0];
            Lane third = Grid.Inbound(Grid.NodeAt(0, 1), 'E')[0];
            Car truck = sim.AddCar(lane.Id, 10, 0, null, Turn.Straight, VehicleKind.Truck);
            Car car = sim.AddCar(other.Id, 10, 0, null, Turn.Straight);
            Car bike = sim.AddCar(third.Id, 10, 0, null, Turn.Straight, VehicleKind.Motorcycle);
            truck.LcCooldownUntil = car.LcCooldownUntil = bike.LcCooldownUntil = 1e9;
            Grid.Run(sim, 4);
            Assert.Less(truck.V, car.V - 1);
            Assert.Greater(bike.V, car.V + 1);
            Assert.Greater(truck.Len, car.Len);
            Assert.Less(bike.Len, car.Len);
        }

        [Test]
        public void ATruckNeverPicksAUTurn()
        {
            var sim = new Simulation(W, 2);
            Lane pocket = Grid.Inbound(Grid.Center, 'W')[0]; // left turn or U-turn
            for (int i = 0; i < 200; i++)
            {
                Car truck = sim.AddCar(pocket.Id, 10, 0, null, null, VehicleKind.Truck);
                Assert.AreNotEqual(Turn.UTurn, truck.Turn);
                sim.RemoveCar(truck.Id);
            }
        }

        [Test]
        public void AMixOfCarsTrucksAndMotorcyclesRunsWithoutACrashOrGridlock()
        {
            var sim = new Simulation(W, 31);
            for (int i = 0; i < 60; i++)
            {
                VehicleKind kind = i % 5 == 0 ? VehicleKind.Truck : i % 7 == 0 ? VehicleKind.Motorcycle : VehicleKind.Car;
                Assert.IsNotNull(sim.Spawn(DriverParams.Normal, 0.4, kind));
            }
            foreach (var c in sim.Cars) c.Drv.Compliance = 1;
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
            Assert.Greater(halfway, 150);
            Assert.Greater(sim.Passes - halfway, 150);
            Assert.Less(longestStop, 120, "a vehicle sat still for two minutes");
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
