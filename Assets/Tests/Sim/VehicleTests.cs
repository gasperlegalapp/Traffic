using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Traffic.Sim;

namespace Traffic.Tests
{
    /// <summary>The big vehicles: the box truck, and the semi, which bends in the middle.</summary>
    public class VehicleTests
    {
        [Test]
        public void ASemiBendsWhereItsTrailerIsHitched()
        {
            var sim = new Simulation(LabWorld.Build(), 4);
            Car semi = sim.Spawn(DriverParams.Normal, 0, VehicleKind.Semi);
            Assert.IsNotNull(semi);
            Assert.AreEqual(14, semi.Len, 1e-9);
            double cab = semi.Len - semi.Vehicle.Trailer, mostBent = 0, widestGap = 0;
            Grid.Run(sim, 240, () =>
            {
                semi.Drv.Compliance = 1;
                // The back of the tractor and the front of the trailer are the same place: where they are joined.
                double bx = semi.CabX - Math.Cos(semi.CabH) * cab / 2, by = semi.CabY - Math.Sin(semi.CabH) * cab / 2;
                double fx = semi.TowX + Math.Cos(semi.TowH) * semi.Vehicle.Trailer / 2, fy = semi.TowY + Math.Sin(semi.TowH) * semi.Vehicle.Trailer / 2;
                if (!semi.ChangingLanes) widestGap = Math.Max(widestGap, Geo.Hypot(bx - fx, by - fy));
                mostBent = Math.Max(mostBent, Math.Abs(Math.Atan2(Math.Sin(semi.CabH - semi.TowH), Math.Cos(semi.CabH - semi.TowH))));
            });
            Assert.Less(widestGap, 0.3, "the two never come apart");
            Assert.Greater(mostBent, 0.4, "and going round a corner it bends");
            Assert.Greater(sim.Passes, 2, "it got through some intersections");
            // On a straight road the two lie in one line, the tractor in front.
            var map = new EditableMap();
            Assert.IsNull(map.TryAdd(new Vec(-300, 0), new Vec(300, 0), RoadType.ById("s1"), true));
            var straight = new Simulation(map.ToWorld(), 1);
            straight.ArrivalsPerMinute = 0;
            Car one = straight.AddCar(straight.Lanes.First(l => l.Kind == LaneKind.Road).Id, 100, 8, DriverParams.Normal, null, VehicleKind.Semi);
            Grid.Run(straight, 2);
            Assert.AreEqual(one.CabH, one.TowH, 1e-6);
            Assert.Greater(one.CabX, one.X);
            Assert.Less(one.TowX, one.X);
        }

        [Test]
        public void TheGridKeepsMovingWithBigTrucksInIt()
        {
            var sim = new Simulation(LabWorld.Build(), 11);
            for (int i = 0; i < 45; i++) sim.Spawn(DriverParams.Normal, 0.3);
            for (int i = 0; i < 6; i++)
            {
                Assert.IsNotNull(sim.Spawn(DriverParams.Normal, 0.2, VehicleKind.Semi));
                Assert.IsNotNull(sim.Spawn(DriverParams.Normal, 0.2, VehicleKind.BoxTruck));
            }
            var stopped = new Dictionary<int, double>();
            double longestStop = 0;
            Grid.Run(sim, 600, () =>
            {
                foreach (Car c in sim.Cars)
                {
                    c.Drv.Compliance = 1;
                    double t;
                    stopped.TryGetValue(c.Id, out t);
                    t = c.V < 0.1 && !c.Crashed ? t + Simulation.Dt : 0;
                    stopped[c.Id] = t;
                    longestStop = Math.Max(longestStop, t);
                }
            });
            Assert.AreEqual(0, sim.Crashes, "a trailer cuts the corner without hitting anyone");
            Assert.Greater(sim.Passes, 550);
            Assert.Less(longestStop, 90);
        }

        [Test]
        public void BigTrucksArriveOnlyWhenAskedFor()
        {
            var map = new EditableMap();
            Assert.IsNull(map.TryAdd(new Vec(-600, 0), new Vec(600, 0), RoadType.ById("a1x2"), true));
            World world = map.ToWorld();
            var sim = new Simulation(world, 2);
            sim.ArrivalsPerMinute = 14;
            sim.ArrivalTrucks = 0.5;
            sim.BigTrucks = 1;
            var kinds = new HashSet<VehicleKind>();
            Grid.Run(sim, 240, () =>
            {
                foreach (Car c in sim.Cars) kinds.Add(c.Vehicle.Kind);
            });
            Assert.IsTrue(kinds.Contains(VehicleKind.Semi) && kinds.Contains(VehicleKind.BoxTruck));
            Assert.IsFalse(kinds.Contains(VehicleKind.Truck), "every truck was a big one");
            Assert.AreEqual(0, sim.Crashes);
            Assert.Greater(sim.Left, 30);

            sim = new Simulation(world, 2);
            sim.ArrivalsPerMinute = 14;
            sim.ArrivalTrucks = 0.5;
            kinds.Clear();
            Grid.Run(sim, 120, () =>
            {
                foreach (Car c in sim.Cars) kinds.Add(c.Vehicle.Kind);
            });
            Assert.IsFalse(kinds.Contains(VehicleKind.Semi) || kinds.Contains(VehicleKind.BoxTruck));
        }
    }
}
