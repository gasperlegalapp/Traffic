using System;
using System.Collections.Generic;

namespace Traffic.Sim
{
    /// <summary>A journey traffic wants to make: from one edge of the map to another.</summary>
    public struct Trip
    {
        /// <summary>The nodes at the edge of the map where it starts and ends.</summary>
        public int From, To;
        /// <summary>How much of the traffic from that start wants this journey, next to its other trips.</summary>
        public double Weight;
    }

    /// <summary>
    /// Works out how to get to each way off the map: for every lane, how long the rest of the
    /// drive is, and so which turn to take at each junction. It needs only the road network,
    /// so it can also say whether a journey is possible before any traffic runs.
    /// </summary>
    public sealed class Routes
    {
        /// <summary>Seconds a driver allows for a lane change when comparing routes.</summary>
        private const double ChangeLane = 4;

        private readonly World world;
        private readonly Dictionary<int, double[]> costs = new Dictionary<int, double[]>();
        private readonly Dictionary<long, List<int>> byApproach = new Dictionary<long, List<int>>();
        /// <summary>The nodes where vehicles can leave the map.</summary>
        public readonly List<int> Exits = new List<int>();

        public Routes(World world)
        {
            this.world = world;
            foreach (Lane lane in world.Lanes)
            {
                if (lane.Kind != LaneKind.Road) continue;
                long key = Key(lane.ApproachNode, lane.ApproachArm);
                List<int> list;
                if (!byApproach.TryGetValue(key, out list)) byApproach[key] = list = new List<int>();
                list.Add(lane.Id);
                if (lane.IsExit && !Exits.Contains(lane.ApproachNode)) Exits.Add(lane.ApproachNode);
            }
        }

        private static long Key(int node, int arm)
        {
            return ((long)node << 20) + arm;
        }

        private static double Time(Lane lane)
        {
            return lane.Length / Math.Max(lane.SpeedLimit, 1);
        }

        /// <summary>For every lane, the seconds from its start to driving off the map at a node. Infinity where there is no way.</summary>
        public double[] CostTo(int exitNode)
        {
            double[] cost;
            if (costs.TryGetValue(exitNode, out cost)) return cost;
            cost = new double[world.Lanes.Count];
            for (int i = 0; i < cost.Length; i++) cost[i] = double.PositiveInfinity;
            foreach (Lane lane in world.Lanes) if (lane.Kind == LaneKind.Road && lane.IsExit && lane.ApproachNode == exitNode) cost[lane.Id] = Time(lane);

            // Work back from the exit until nothing gets any shorter.
            bool changed = true;
            for (int pass = 0; changed && pass < world.Lanes.Count + 2; pass++)
            {
                changed = false;
                foreach (Lane lane in world.Lanes)
                {
                    if (lane.Kind != LaneKind.Road) continue;
                    double best = cost[lane.Id];
                    if (lane.Connectors != null)
                    {
                        foreach (int c in lane.Connectors)
                        {
                            if (c < 0) continue;
                            Lane conn = world.Lanes[c];
                            best = Math.Min(best, Time(lane) + Time(conn) + cost[conn.Next]);
                        }
                    }
                    // Or by moving over to the lane alongside.
                    foreach (int n in lane.Lefts) best = Math.Min(best, cost[n] + ChangeLane);
                    foreach (int n in lane.Rights) best = Math.Min(best, cost[n] + ChangeLane);
                    if (best < cost[lane.Id] - 1e-9)
                    {
                        cost[lane.Id] = best;
                        changed = true;
                    }
                }
            }
            costs[exitNode] = cost;
            return cost;
        }

        /// <summary>Can traffic arriving at one edge of the map drive to another?</summary>
        public bool Connected(int fromNode, int toNode)
        {
            return !double.IsInfinity(Best(fromNode, toNode));
        }

        /// <summary>The quickest drive from one edge of the map to another, in seconds. Infinity if there is no way.</summary>
        public double Best(int fromNode, int toNode)
        {
            if (fromNode < 0 || toNode < 0 || fromNode >= world.Nodes.Count || toNode >= world.Nodes.Count) return double.PositiveInfinity;
            double[] cost = CostTo(toNode);
            double best = double.PositiveInfinity;
            foreach (Approach arm in world.Nodes[fromNode].Arms) foreach (int id in arm.Outbound) best = Math.Min(best, cost[id]);
            return best;
        }

        /// <summary>
        /// The turn to take at the end of a lane's road to reach an exit soonest. Any lane of
        /// the road may be used for it: the driver changes lanes to suit. False if no turn leads there.
        /// </summary>
        public bool TurnToward(int exitNode, Lane lane, bool allowUTurn, out Turn turn)
        {
            turn = Turn.Straight;
            if (lane.Connectors == null && lane.ApproachTurns == null) return false;
            double[] cost = CostTo(exitNode);
            List<int> lanes;
            if (!byApproach.TryGetValue(Key(lane.ApproachNode, lane.ApproachArm), out lanes)) return false;
            double best = double.PositiveInfinity;
            foreach (int id in lanes)
            {
                Lane other = world.Lanes[id];
                if (other.Connectors == null) continue;
                for (int t = 0; t < Lane.Turns; t++)
                {
                    int c = other.Connectors[t];
                    if (c < 0 || (!allowUTurn && t == (int)Turn.UTurn)) continue;
                    Lane conn = world.Lanes[c];
                    double total = Time(conn) + cost[conn.Next] + (id == lane.Id ? 0 : 0.5);
                    if (total < best)
                    {
                        best = total;
                        turn = (Turn)t;
                    }
                }
            }
            return !double.IsInfinity(best);
        }
    }
}
