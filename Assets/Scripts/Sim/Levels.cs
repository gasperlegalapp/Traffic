using System;
using System.Collections.Generic;

namespace Traffic.Sim
{
    /// <summary>
    /// A puzzle: roads come in at the edges of a square of open ground, traffic wants to get
    /// from some of them to others, and the player has a budget to join them up. It is solved
    /// when every journey asked for is possible, the roads cost no more than the budget, and
    /// enough vehicles have finished their journeys. How little they were held up gives the stars.
    /// </summary>
    public sealed class Level
    {
        public string Name = "", Blurb = "";
        public double Budget;
        /// <summary>Roads may be built this far from the middle, east-west and north-south.</summary>
        public double Half = 260;
        /// <summary>Journeys that must be finished to complete the level.</summary>
        public int TripsToFinish = 60;
        /// <summary>New vehicles a minute on each lane that enters the map.</summary>
        public double Arrivals = 6;
        /// <summary>How many times longer than on an empty road the average journey may take, for two stars and for three.</summary>
        public double TwoStars = 1.8, ThreeStars = 1.4;

        private readonly List<string[]> wanted = new List<string[]>();
        private readonly List<double> weights = new List<double>();
        private readonly Action<EditableMap> lay;

        public Level(Action<EditableMap> lay)
        {
            this.lay = lay;
        }

        /// <summary>Traffic wants to get from one named place to another. A heavier weight means more of it.</summary>
        public Level Wants(string from, string to, double weight = 1)
        {
            wanted.Add(new[] { from, to });
            weights.Add(weight);
            return this;
        }

        /// <summary>The same both ways.</summary>
        public Level Between(string one, string other, double weight = 1)
        {
            return Wants(one, other, weight).Wants(other, one, weight);
        }

        public int RouteCount => wanted.Count;

        /// <summary>The map as the level begins: its own roads, which cannot be removed, and nothing else.</summary>
        public EditableMap Start()
        {
            var map = new EditableMap();
            lay(map);
            map.Bound = Half;
            return map;
        }

        private static int NodeNamed(World world, string name)
        {
            foreach (Node node in world.Nodes) if (node.Name == name) return node.Id;
            return -1;
        }

        /// <summary>The journeys asked for, as the simulation wants them.</summary>
        public List<Trip> DemandFor(World world)
        {
            var demand = new List<Trip>();
            for (int i = 0; i < wanted.Count; i++)
            {
                int from = NodeNamed(world, wanted[i][0]), to = NodeNamed(world, wanted[i][1]);
                if (from >= 0 && to >= 0) demand.Add(new Trip { From = from, To = to, Weight = weights[i] });
            }
            return demand;
        }

        /// <summary>How many of the journeys asked for can be driven on these roads. Those that cannot are listed by name.</summary>
        public int Connected(World world, List<string> missing = null)
        {
            var routes = new Routes(world);
            int count = 0;
            foreach (string[] pair in wanted)
            {
                if (routes.Connected(NodeNamed(world, pair[0]), NodeNamed(world, pair[1]))) count++;
                else if (missing != null) missing.Add(pair[0] + " to " + pair[1]);
            }
            return count;
        }

        /// <summary>Stars earned: none until the level is complete, then one to three by how little traffic was held up.</summary>
        public int Stars(Simulation sim, double cost, World world)
        {
            if (sim.Arrived < TripsToFinish || cost > Budget + 0.5 || Connected(world) < RouteCount) return 0;
            double slowdown = sim.TripIdeal > 0 ? sim.TripSeconds / sim.TripIdeal : 99;
            return slowdown <= ThreeStars ? 3 : slowdown <= TwoStars ? 2 : 1;
        }

        // ---------------------------------------------------------------- laying out a level

        private static readonly int[] Colors = { 0x2f9e8f, 0xc2476b, 0x7a5bc7, 0xc98a2b, 0x3f8ed0, 0x5a9e3f };

        /// <summary>
        /// A road that comes in from off the map and stops at the edge of the building area,
        /// where the player carries it on. Flow is "both", "in" or "out" for a one-way road.
        /// </summary>
        public static void End(EditableMap map, string name, double x, double y, string road, string flow = "both")
        {
            // It runs straight in from 90 m beyond the edge, along whichever axis it is nearest the end of.
            bool sideways = Math.Abs(x) >= Math.Abs(y);
            var at = new Vec(x, y);
            var outside = new Vec(x + (sideways ? Math.Sign(x) * 90 : 0), y + (sideways ? 0 : Math.Sign(y) * 90));
            RoadType type = RoadType.ById(road);
            if (flow == "out") map.AddLocked(at, outside, type);
            else map.AddLocked(outside, at, type);
            map.Ends.Add(new MapEnd { Name = name, At = outside, Color = Colors[map.Ends.Count % Colors.Length] });
        }

        /// <summary>A road that is already there, running right across the map from one named place to another.</summary>
        public static void Through(EditableMap map, string from, string to, Vec a, Vec b, string road)
        {
            map.AddLocked(a, b, RoadType.ById(road));
            map.Ends.Add(new MapEnd { Name = from, At = a, Color = Colors[map.Ends.Count % Colors.Length] });
            map.Ends.Add(new MapEnd { Name = to, At = b, Color = Colors[map.Ends.Count % Colors.Length] });
        }
    }

    public static class Levels
    {
        public static readonly Level[] All =
        {
            new Level(map =>
            {
                Level.End(map, "West St", -260, 0, "s2");
                Level.End(map, "East St", 260, 60, "s2");
            })
            {
                Name = "First Link",
                Blurb = "Two streets stop at the edge of an empty field. Join them up.",
                Budget = 1300, TripsToFinish = 30,
            }.Between("West St", "East St"),

            new Level(map =>
            {
                Level.End(map, "North Rd", 0, 260, "s2");
                Level.End(map, "South Rd", 0, -260, "s2");
                Level.End(map, "West Rd", -260, 0, "s2");
                Level.End(map, "East Rd", 260, 0, "s2");
            })
            {
                Name = "Four Corners",
                Blurb = "Four roads, and everyone wants to get everywhere. A plain crossroads will do it. Is there something that keeps traffic moving better?",
                Budget = 2600, TripsToFinish = 100, Arrivals = 7,
            }.Between("North Rd", "South Rd").Between("West Rd", "East Rd").Between("North Rd", "West Rd").Between("North Rd", "East Rd").Between("South Rd", "West Rd").Between("South Rd", "East Rd"),

            new Level(map =>
            {
                Level.Through(map, "Highway in", "Highway out", new Vec(-350, 130), new Vec(350, 130), "h2");
                Level.End(map, "Mill Rd", 0, -260, "s2");
            })
            {
                Name = "On and Off",
                Blurb = "A highway runs past the end of Mill Road without touching it. Traffic from the highway wants to reach Mill Road, and traffic from Mill Road wants to get on. A one-way road brought in alongside the highway merges into it.",
                Budget = 1600, TripsToFinish = 100, Arrivals = 5,
            }.Wants("Highway in", "Highway out", 3).Wants("Highway in", "Mill Rd").Wants("Mill Rd", "Highway out"),

            new Level(map =>
            {
                Level.Through(map, "West Ave", "East Ave", new Vec(-350, 0), new Vec(350, 0), "a4");
                Level.End(map, "North Rd", 60, 260, "s2");
                Level.End(map, "South Rd", -60, -260, "s2");
            })
            {
                Name = "Over or Through",
                Blurb = "A busy avenue lies between North Road and South Road. A crossing with a traffic light would join them, and hold everybody up. A bridge or a tunnel costs more and stops nobody.",
                Budget = 2600, TripsToFinish = 120, Arrivals = 6,
            }.Between("West Ave", "East Ave", 4).Between("North Rd", "South Rd"),
        };
    }
}
