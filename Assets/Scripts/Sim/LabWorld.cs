namespace Traffic.Sim
{
    /// <summary>
    /// The driver lab test area: a three-by-three grid of streets, built so that
    /// each road and intersection tests something different.
    ///
    /// Intersections (rows south to north, columns west to east):
    ///   center        traffic light with left-turn arrows; turn pockets on every side
    ///   west, south   plain traffic lights (left turns give way)
    ///   east, north   all-way stops
    ///   corners       plain bends
    ///
    /// Roads:
    ///   Main St (east-west through the center): two lanes each way, with a
    ///     left-turn pocket and a right-turn pocket at the center. U-turns allowed.
    ///   Center Ave (north-south through the center): a left-turn pocket, a through
    ///     lane and a through-or-right lane at the center. No U-turns.
    ///   South side: two lanes; eastbound the right lane ends on the west block and
    ///     the left lane ends on the east block.
    ///   West side: two lanes; on the upper block the northbound right lane ends
    ///     and the southbound road gains a lane.
    ///   North and east sides: one lane each way.
    ///
    /// Traffic drives on the right. Plane: x east, y north, meters.
    /// </summary>
    public static class LabWorld
    {
        /// <summary>Distance between neighboring intersections.</summary>
        public const double Block = 140;
        private const double Pocket = -55;

        private static TrackSpec T(int slot, double from = 0, double to = 1, params Turn[] turns)
        {
            return new TrackSpec(slot, from, to, turns);
        }

        private static TrackSpec[] One()
        {
            return new[] { T(0) };
        }

        private static TrackSpec[] Two()
        {
            return new[] { T(0), T(1) };
        }

        public static World Build()
        {
            var b = new NetworkBuilder();
            var n = new int[3, 3];
            for (int row = 0; row < 3; row++)
            {
                for (int col = 0; col < 3; col++)
                {
                    JunctionMode mode = JunctionMode.Free;
                    if (row == 1 && col == 1) mode = JunctionMode.ProtectedLeft;
                    else if ((row == 1 && col == 0) || (row == 0 && col == 1)) mode = JunctionMode.Signal;
                    else if ((row == 1 && col == 2) || (row == 2 && col == 1)) mode = JunctionMode.AllWay;
                    n[row, col] = b.AddNode((col - 1) * Block, (row - 1) * Block, mode);
                }
            }
            int center = n[1, 1];

            // Main St: pockets for left (and U-turns) and for right, two through lanes between them.
            TrackSpec[] mainIntoCenter =
            {
                T(0, Pocket, 1, Turn.Left, Turn.UTurn), T(1, 0, 1, Turn.Straight), T(2, 0, 1, Turn.Straight), T(3, Pocket, 1, Turn.Right),
            };
            b.AddRoad(n[1, 0], center, mainIntoCenter, Two());
            b.AddRoad(center, n[1, 2], Two(), mainIntoCenter);
            b.AllowUTurn(center, n[1, 0]);
            b.AllowUTurn(center, n[1, 2]);

            // Center Ave: left-turn pocket, through lane, through-or-right lane.
            TrackSpec[] avenueIntoCenter =
            {
                T(0, Pocket, 1, Turn.Left), T(1, 0, 1, Turn.Straight), T(2, 0, 1, Turn.Straight, Turn.Right),
            };
            b.AddRoad(n[0, 1], center, avenueIntoCenter, Two());
            b.AddRoad(center, n[2, 1], Two(), avenueIntoCenter);

            // South side: eastbound loses its right lane on the first block and its left lane on the second.
            b.AddRoad(n[0, 0], n[0, 1], new[] { T(0), T(1, 0, 0.55) }, Two());
            b.AddRoad(n[0, 1], n[0, 2], new[] { T(0, 0, 0.55), T(1) }, Two());
            b.AllowUTurn(n[0, 1], n[0, 0]);

            // North side: one lane each way.
            b.AddRoad(n[2, 0], n[2, 1], One(), One());
            b.AddRoad(n[2, 1], n[2, 2], One(), One());

            // West side: two lanes; the upper block has a lane ending one way and a lane starting the other.
            b.AddRoad(n[0, 0], n[1, 0], Two(), Two());
            b.AddRoad(n[1, 0], n[2, 0], new[] { T(0), T(1, 0, 0.6) }, new[] { T(0), T(1, 0.3, 1) });

            // East side: one lane each way.
            b.AddRoad(n[0, 2], n[1, 2], One(), One());
            b.AddRoad(n[1, 2], n[2, 2], One(), One());

            return b.Build();
        }
    }
}
