using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Traffic.Sim;

namespace Traffic.Tests
{
    /// <summary>
    /// Rules of the road that are about how drivers behave among themselves, and the drivers
    /// who break them: keeping right, and the left-lane camper.
    /// </summary>
    public class RulesTests
    {
        private static Vec P(double x, double y)
        {
            return new Vec(x, y);
        }

        /// <summary>A long straight one-way road running east, two lanes wide: its left lane and its right lane.</summary>
        private static World TwoLanes(out Lane left, out Lane right, string road = "a1x2")
        {
            var map = new EditableMap();
            Assert.IsNull(map.TryAdd(P(-900, 0), P(900, 0), RoadType.ById(road), true));
            World world = map.ToWorld();
            List<Lane> lanes = world.Lanes.Where(l => l.Kind == LaneKind.Road).OrderByDescending(l => l.Path.At(10).Y).ToList();
            Assert.AreEqual(2, lanes.Count);
            left = lanes[0];
            right = lanes[1];
            return world;
        }

        private static DriverParams Driver(double speed)
        {
            DriverParams drv = DriverParams.Normal;
            drv.Speed = speed;
            return drv;
        }

        /// <summary>An avenue crossed by a street, with a light: the map, and where its lanes arriving from the south are.</summary>
        private static EditableMap Crossing()
        {
            var map = new EditableMap();
            Assert.IsNull(map.TryAdd(P(-250, 0), P(250, 0), RoadType.ById("a4"), true));
            Assert.IsNull(map.TryAdd(P(0, -250), P(0, 250), RoadType.ById("s2"), true));
            return map;
        }

        [Test]
        public void ANoTurnOnRedSignKeepsRightTurnersAtTheLine()
        {
            // Ohio Revised Code 4511.13(C)(4): a turn on red may be forbidden by a sign.
            EditableMap map = Crossing();
            Vec northbound = P(2, -120);
            Assert.IsFalse(map.NoTurnOnRedAt(northbound));
            Assert.IsNull(map.TrySetNoTurnOnRed(northbound, true, false), "trying it changes nothing");
            Assert.IsFalse(map.NoTurnOnRedAt(northbound));
            Assert.IsNull(map.TrySetNoTurnOnRed(northbound, true, true));
            Assert.IsTrue(map.NoTurnOnRedAt(northbound));
            Assert.IsFalse(map.NoTurnOnRedAt(P(-2, 120)), "the street the other way has no such sign");
            // The sign stays up when the light is changed for another kind of light, and is saved with the map.
            Assert.IsNull(map.TrySetControl(northbound, Control.LightWithArrows, true));
            Assert.IsTrue(map.NoTurnOnRedAt(northbound));
            Assert.IsNull(map.TrySetControl(northbound, Control.Light, true));
            EditableMap loaded = EditableMap.Parse(map.Serialize());
            Assert.IsTrue(loaded.NoTurnOnRedAt(northbound));
            Assert.AreEqual(map.Serialize(), loaded.Serialize());

            // A car arriving from the south to turn right at a red light: with the sign it waits for the green; without, it stops and goes.
            foreach (bool sign in new[] { true, false })
            {
                if (!sign) Assert.IsNull(map.TrySetNoTurnOnRed(northbound, false, true));
                World world = map.ToWorld();
                Node light = world.Nodes.Single(n => n.Arms.Count == 4);
                Lane lane = world.Lanes.Single(l => l.Kind == LaneKind.Road && l.Node == light.Id && l.Path.At(0).Y < -100 && l.Allows(Turn.Right));
                Assert.AreEqual(sign, light.Arms[lane.Arm].NoTurnOnRed);
                var sim = new Simulation(world, 2);
                sim.ArrivalsPerMinute = 0;
                Junction junction = sim.Junctions[light.Id];
                // Hold the avenue's green, so the street stays red.
                junction.ShowGreen(light.Arms[lane.Arm].Street == 0 ? 1 : 0, false, 0);
                junction.Hold = true;
                Assert.AreEqual(SignalLight.Red, junction.State(lane.Arm, Turn.Right));
                Car car = sim.AddCar(lane.Id, lane.Length - 40, 8, DriverParams.Normal, Turn.Right);
                Grid.Run(sim, 30, () => car.Drv.Compliance = 1);
                bool through = car.Lane != lane.Id;
                Assert.AreEqual(!sign, through, sign ? "it waits at the line for the green" : "it turns on red after stopping");
                if (!sign) continue;
                junction.Hold = false;
                junction.ShowGreen(light.Arms[lane.Arm].Street, false, sim.Time);
                Grid.Run(sim, 15, () => car.Drv.Compliance = 1);
                Assert.AreNotEqual(lane.Id, car.Lane, "and goes when it comes");
            }
        }

        [Test]
        public void ALightThatHasGoneOutIsTreatedAsStopSigns()
        {
            // Ohio Revised Code 4511.132: at a signal that is not working, stop, and go on as at a stop sign.
            World world = Crossing().ToWorld();
            Node light = world.Nodes.Single(n => n.Arms.Count == 4);
            var sim = new Simulation(world, 7);
            sim.ArrivalsPerMinute = 9;
            Junction junction = sim.Junctions[light.Id];
            Assert.AreEqual(JunctionMode.Signal, junction.Mode);
            Grid.Run(sim, 60, () => { foreach (Car c in sim.Cars) c.Drv.Compliance = 1; });
            sim.FailSignal(light.Id, true);
            Assert.IsTrue(junction.Failed);
            Assert.AreEqual(JunctionMode.AllWay, junction.Mode);
            foreach (Approach arm in light.Arms) if (arm.Inbound.Count > 0) Assert.AreEqual(SignalLight.Stop, junction.State(arm.Arm, Turn.Straight));
            int before = sim.Passes;
            // Everyone who goes through from now on has stopped first.
            var seenStopped = new HashSet<int>();
            var onRoad = new Dictionary<int, bool>();
            int rolled = 0;
            Grid.Run(sim, 240, () =>
            {
                foreach (Car c in sim.Cars)
                {
                    c.Drv.Compliance = 1;
                    Lane lane = sim.Lanes[c.Lane];
                    bool approaching = lane.Kind == LaneKind.Road && lane.Node == light.Id;
                    if (approaching && c.V < 0.3 && lane.Length - c.S < 30) seenStopped.Add(c.Id);
                    bool was;
                    onRoad.TryGetValue(c.Id, out was);
                    if (was && lane.Kind == LaneKind.Connector && lane.Node == light.Id && !seenStopped.Contains(c.Id) && sim.Time > 75) rolled++;
                    onRoad[c.Id] = approaching;
                }
            });
            Assert.AreEqual(0, sim.Crashes);
            Assert.Greater(sim.Passes - before, 25, "traffic still gets through, in turn");
            Assert.AreEqual(0, rolled, "nobody drove through without stopping");
            // Mended, it is a light again, and any change of what runs the intersection mends it too.
            sim.FailSignal(light.Id, false);
            Assert.IsFalse(junction.Failed);
            Assert.AreEqual(JunctionMode.Signal, junction.Mode);
            sim.FailSignal(light.Id, true);
            sim.SetMode(light.Id, JunctionMode.ProtectedLeft);
            Assert.IsFalse(junction.Failed);
            Assert.AreEqual(JunctionMode.ProtectedLeft, junction.Mode);
            // Stop signs cannot go out.
            sim.SetMode(light.Id, JunctionMode.AllWay);
            sim.FailSignal(light.Id, true);
            Assert.IsFalse(junction.Failed);
        }

        /// <summary>A road running east with a school bus on it that has a stop halfway along: the simulation, the bus, and the lanes each way (the outside one first).</summary>
        private static Simulation BusOn(string road, out Car bus, out List<Lane> east, out List<Lane> west)
        {
            var map = new EditableMap();
            Assert.IsNull(map.TryAdd(P(-400, 0), P(400, 0), RoadType.ById(road), true));
            World world = map.ToWorld();
            east = world.Lanes.Where(l => l.Kind == LaneKind.Road && l.Path.At(l.Length).X > l.Path.At(0).X).OrderBy(l => l.Path.At(10).Y).ToList();
            west = world.Lanes.Where(l => l.Kind == LaneKind.Road && l.Path.At(l.Length).X < l.Path.At(0).X).OrderByDescending(l => l.Path.At(10).Y).ToList();
            var sim = new Simulation(world, 5);
            sim.ArrivalsPerMinute = 0;
            bus = sim.AddCar(east[0].Id, 300, 9, DriverParams.Normal, null, VehicleKind.SchoolBus);
            bus.BusLane = east[0].Id;
            bus.BusStop = 400;
            return sim;
        }

        [Test]
        public void TrafficStopsBothWaysForASchoolBusOnATwoLaneRoad()
        {
            // Ohio Revised Code 4511.75(A): stop at least ten feet from a school bus that is taking children on, and wait until it moves off.
            Car bus;
            List<Lane> east, west;
            Simulation sim = BusOn("s2", out bus, out east, out west);
            Car behind = sim.AddCar(east[0].Id, 240, 9);
            // Coming the other way, timed to meet the bus while it is stopped.
            Car oncoming = sim.AddCar(west[0].Id, 180, 9);
            bool loaded = false, passedWhileLoading = false;
            double nearestBehind = 99, nearestOncoming = 99, stoodFor = 0;
            Car busCar = bus;
            Grid.Run(sim, 60, () =>
            {
                if (!busCar.Loading) return;
                loaded = true;
                stoodFor += Simulation.Dt;
                Assert.Less(busCar.V, 0.5, "the bus stands still while it loads");
                nearestBehind = Math.Min(nearestBehind, busCar.X - busCar.Len / 2 - (behind.X + behind.Len / 2));
                nearestOncoming = Math.Min(nearestOncoming, oncoming.X - oncoming.Len / 2 - (busCar.X + busCar.Len / 2));
                if (oncoming.X < busCar.X) passedWhileLoading = true;
            });
            Assert.IsTrue(loaded, "the bus made its stop");
            Assert.Greater(stoodFor, 7);
            Assert.Less(stoodFor, 14);
            Assert.GreaterOrEqual(nearestBehind, Simulation.BusClearance - 0.2, "the car behind kept ten feet back");
            Assert.IsFalse(passedWhileLoading, "the car coming the other way waited");
            Assert.GreaterOrEqual(nearestOncoming, Simulation.BusClearance - 0.2);
            // Then everyone moves off.
            Assert.IsFalse(bus.Loading);
            Assert.Greater(bus.V + (bus.X > 150 ? 5 : 0), 3);
            Assert.Less(oncoming.X, bus.X, "and the two have passed each other since");
            Assert.AreEqual(0, sim.Crashes);
        }

        [Test]
        public void OnAFourLaneRoadOnlyTrafficBehindTheBusStops()
        {
            // 4511.75(C): on a road of four or more lanes, traffic coming the other way need not stop. Traffic overtaking the bus still must.
            Car bus;
            List<Lane> east, west;
            Simulation sim = BusOn("a4", out bus, out east, out west);
            Assert.AreEqual(4, east[0].RoadLanes);
            // One in the next lane, far enough back to come up on the bus once it has stopped; one the other way, timed to go by while it stands.
            Car inside = sim.AddCar(east[1].Id, 60, 12);
            Car oncoming = sim.AddCar(west[0].Id, 120, 12);
            bool loaded = false, overtook = false, oncomingPassed = false;
            double slowestOncoming = 99;
            Car busCar = bus;
            Grid.Run(sim, 45, () =>
            {
                if (!busCar.Loading) return;
                loaded = true;
                if (inside.X - inside.Len / 2 > busCar.X - busCar.Len / 2 + 1) overtook = true;
                if (oncoming.X < busCar.X) oncomingPassed = true;
                if (Math.Abs(oncoming.X - busCar.X) < 60) slowestOncoming = Math.Min(slowestOncoming, oncoming.V);
            });
            Assert.IsTrue(loaded);
            Assert.IsFalse(overtook, "the car in the next lane did not pass the bus");
            Assert.IsTrue(oncomingPassed, "the car coming the other way drove by");
            Assert.Greater(slowestOncoming, 8, "without slowing for it");
        }

        [Test]
        public void SomeoneWhoRunsRedLightsRunsTheBusToo()
        {
            Car bus;
            List<Lane> east, west;
            Simulation sim = BusOn("s2", out bus, out east, out west);
            Car maniac = sim.AddCar(west[0].Id, 250, 12, SpecialDriver.All[0].Make());
            bool passedWhileLoading = false;
            Car busCar = bus;
            Grid.Run(sim, 40, () => passedWhileLoading |= busCar.Loading && maniac.X < busCar.X - busCar.Len);
            Assert.IsTrue(passedWhileLoading);
        }

        [Test]
        public void SchoolBusesMakeTheirStopsAndTheGridKeepsMoving()
        {
            var sim = new Simulation(LabWorld.Build(), 13);
            for (int i = 0; i < 40; i++) sim.Spawn(DriverParams.Normal, 0.3);
            var buses = new List<Car>();
            for (int i = 0; i < 4; i++) buses.Add(sim.Spawn(DriverParams.Normal, 0, VehicleKind.SchoolBus));
            Assert.IsTrue(buses.All(b => b != null));
            int stops = 0;
            var wasLoading = new HashSet<int>();
            var stopped = new Dictionary<int, double>();
            double longestStop = 0;
            Grid.Run(sim, 600, () =>
            {
                foreach (Car c in sim.Cars)
                {
                    c.Drv.Compliance = 1;
                    if (c.Loading && wasLoading.Add(c.Id)) stops++;
                    if (!c.Loading) wasLoading.Remove(c.Id);
                    double t;
                    stopped.TryGetValue(c.Id, out t);
                    t = c.V < 0.1 && !c.Crashed ? t + Simulation.Dt : 0;
                    stopped[c.Id] = t;
                    longestStop = Math.Max(longestStop, t);
                }
            });
            Assert.Greater(stops, 6, "the buses made their stops");
            Assert.AreEqual(0, sim.Crashes);
            Assert.Greater(sim.Passes, 450);
            Assert.Less(longestStop, 90);

            // They arrive from the edge of the map when asked for, and not otherwise.
            var map = new EditableMap();
            Assert.IsNull(map.TryAdd(P(-400, 0), P(400, 0), RoadType.ById("s2"), true));
            var edge = new Simulation(map.ToWorld(), 3);
            edge.ArrivalsPerMinute = 10;
            edge.ArrivalTrucks = 0;
            edge.ArrivalMotorcycles = 0;
            edge.ArrivalBuses = 0.3;
            bool any = false;
            Grid.Run(edge, 200, () => any |= edge.Cars.Any(c => c.Vehicle.Kind == VehicleKind.SchoolBus));
            Assert.IsTrue(any);
            Assert.AreEqual(0, edge.Crashes);
            Assert.Greater(edge.Left, 15);
        }

        [Test]
        public void ASlowerDriverMovesRightForAFasterOne()
        {
            // Ohio Revised Code 4511.25(B): a vehicle going slower than the traffic around it keeps to the right-hand lane.
            Lane left, right;
            World world = TwoLanes(out left, out right);
            var sim = new Simulation(world, 3);
            sim.ArrivalsPerMinute = 0;
            Car slow = sim.AddCar(left.Id, 120, 13, Driver(0.8));
            Car fast = sim.AddCar(left.Id, 90, 17, Driver(1.15));
            Grid.Run(sim, 40);
            Assert.AreEqual(right.Id, slow.Lane, "the slower driver has moved over");
            Assert.Greater(fast.S, slow.S, "and the faster one has gone by");
            Assert.AreEqual(left.Id, fast.Lane, "without having to leave the left lane to do it");
            Assert.AreEqual(0, sim.Crashes);

            // With the rule switched off the slower driver stays put, and the faster one goes round on the right.
            sim = new Simulation(world, 3);
            sim.ArrivalsPerMinute = 0;
            sim.KeepRight = false;
            slow = sim.AddCar(left.Id, 120, 13, Driver(0.8));
            fast = sim.AddCar(left.Id, 90, 17, Driver(1.15));
            bool passedOnRight = false;
            Grid.Run(sim, 40, () => passedOnRight |= fast.Lane == right.Id);
            Assert.AreEqual(left.Id, slow.Lane);
            Assert.IsTrue(passedOnRight);
            Assert.Greater(fast.S, slow.S);
        }

        [Test]
        public void TwoDriversAtTheSameSpeedDoNotShuffleAbout()
        {
            // Nobody is holding anybody up, so nobody has to move: the rule is for slower traffic, not for everyone in a left lane.
            Lane left, right;
            World world = TwoLanes(out left, out right);
            var sim = new Simulation(world, 3);
            sim.ArrivalsPerMinute = 0;
            Car ahead = sim.AddCar(left.Id, 140, 16, Driver(1));
            Car behind = sim.AddCar(left.Id, 90, 16, Driver(1));
            int changes = 0, last = ahead.Lane;
            Grid.Run(sim, 40, () =>
            {
                if (ahead.Lane != last) changes++;
                last = ahead.Lane;
            });
            Assert.LessOrEqual(changes, 1);
            Assert.AreEqual(0, sim.Crashes);
        }

        [Test]
        public void ACamperTakesTheLeftLaneAndEveryoneGoesRound()
        {
            Lane left, right;
            World world = TwoLanes(out left, out right);
            var sim = new Simulation(world, 5);
            sim.ArrivalsPerMinute = 0;
            Car camper = sim.AddCar(right.Id, 150, 12);
            sim.MakeSpecial(camper, SpecialDriver.Camper);
            Assert.AreEqual("LEFT-LANE CAMPER", camper.Special);
            Assert.AreEqual(1, camper.Drv.Camp);
            // Left alone, they drift over to the left lane and settle there, under the limit.
            Grid.Run(sim, 25);
            Assert.AreEqual(left.Id, camper.Lane);
            Assert.Less(camper.V, left.SpeedLimit * 0.9);
            Assert.Greater(camper.V, left.SpeedLimit * 0.7);

            // Three ordinary drivers come up behind in the left lane. The camper does not move over; they go round on the right.
            var others = new List<Car>();
            for (int i = 0; i < 3; i++) others.Add(sim.AddCar(left.Id, camper.S - 40 - i * 25, 16, Driver(1 + 0.05 * i)));
            bool moved = false;
            Grid.Run(sim, 60, () => moved |= camper.Lane != left.Id && !camper.ChangingLanes);
            Assert.IsFalse(moved, "the camper stays in the left lane");
            foreach (Car other in others) Assert.Greater(other.S, camper.S, "everyone else got past");
            Assert.AreEqual(0, sim.Crashes);
        }

        [Test]
        public void CampersArriveWhenAskedForAndTheRoadStillFlows()
        {
            Lane left, right;
            World world = TwoLanes(out left, out right);
            var sim = new Simulation(world, 9);
            sim.ArrivalsPerMinute = 14;
            sim.ArrivalCampers = 0.2;
            int campers = 0;
            var seen = new HashSet<int>();
            Grid.Run(sim, 300, () =>
            {
                foreach (Car c in sim.Cars) if (c.Special == "LEFT-LANE CAMPER" && seen.Add(c.Id)) campers++;
            });
            Assert.Greater(campers, 3);
            Assert.AreEqual(0, sim.Crashes);
            Assert.Greater(sim.Left, 60, "traffic gets past them");

            // None come unless they are asked for.
            sim = new Simulation(world, 9);
            sim.ArrivalsPerMinute = 14;
            Grid.Run(sim, 120);
            Assert.IsTrue(sim.Cars.All(c => c.Special == null));
        }
    }
}
