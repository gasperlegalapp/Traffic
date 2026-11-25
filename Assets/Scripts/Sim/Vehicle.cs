namespace Traffic.Sim
{
    public enum VehicleKind
    {
        Car,
        Truck,
        Motorcycle,
        /// <summary>A big box truck: one rigid body, half as long again as a delivery truck.</summary>
        BoxTruck,
        /// <summary>A tractor pulling a trailer, which may be a box or a tank: it bends in the middle.</summary>
        Semi,
        /// <summary>A school bus: it stops along the way to take children on, and while it does the traffic round it has to stop too.</summary>
        SchoolBus,
    }

    /// <summary>What a kind of vehicle can do, as multiples of what the driver would do in a car.</summary>
    public sealed class VehicleSpec
    {
        public VehicleKind Kind;
        public string Name;
        /// <summary>Meters.</summary>
        public double Len, Wid;
        /// <summary>How hard it can accelerate and how hard it brakes in normal driving.</summary>
        public double Accel = 1, Brake = 1;
        /// <summary>Cruising speed.</summary>
        public double Speed = 1;
        /// <summary>Speed through curves and turns.</summary>
        public double Corner = 1;
        /// <summary>How long a lane change takes.</summary>
        public double LaneChange = 1;
        public bool CanUTurn = true;
        /// <summary>
        /// For a vehicle that pulls a trailer: how much of its length, at the back, is the
        /// trailer. The tractor in front and the trailer behind each keep to the road, joined
        /// where they meet, so the whole bends round a corner. 0 for a vehicle in one piece.
        /// </summary>
        public double Trailer;
        /// <summary>Paint colors as 0xRRGGBB.</summary>
        public int[] Colors;

        private static readonly VehicleSpec[] All =
        {
            new VehicleSpec
            {
                Kind = VehicleKind.Car, Name = "Car", Len = 4.5, Wid = 1.9,
                Colors = new[] { 0xf25f5c, 0xffe066, 0x70c1b3, 0x4ea8de, 0xf4a261, 0xe9ecef, 0xb388eb, 0x8ac926, 0xff8fab, 0x90e0ef },
            },
            new VehicleSpec
            {
                // A delivery truck: slow off the line, gentle on the brakes, wide and slow through turns.
                Kind = VehicleKind.Truck, Name = "Truck", Len = 5.0, Wid = 2.3,
                Accel = 0.5, Brake = 0.8, Speed = 0.92, Corner = 0.6, LaneChange = 1.5, CanUTurn = false,
                Colors = new[] { 0xd9534f, 0x3a7ca5, 0xf2a541, 0x4f9d69, 0x8d99ae, 0xe6e6e6 },
            },
            new VehicleSpec
            {
                // Small and quick: fits gaps a car cannot, and is gone from the lights first.
                Kind = VehicleKind.Motorcycle, Name = "Motorcycle", Len = 2.2, Wid = 0.8,
                Accel = 1.5, Brake = 1.1, Speed = 1.05, Corner = 1.1, LaneChange = 0.7,
                Colors = new[] { 0xff3b30, 0x1f1f1f, 0x2e86de, 0xf5f5f5, 0x27ae60, 0xff9f1a },
            },
            new VehicleSpec
            {
                // Heavier than the delivery truck in every way.
                Kind = VehicleKind.BoxTruck, Name = "Box truck", Len = 7.6, Wid = 2.4,
                Accel = 0.42, Brake = 0.75, Speed = 0.9, Corner = 0.5, LaneChange = 1.7, CanUTurn = false,
                Colors = new[] { 0xe9ecef, 0xd9534f, 0x3a7ca5, 0xf2a541, 0x4f9d69, 0x8d99ae },
            },
            new VehicleSpec
            {
                // Shorter than a real one, as everything here is, but still three cars long: slow to get going, slow round a corner.
                Kind = VehicleKind.Semi, Name = "Semi", Len = 14, Wid = 2.5, Trailer = 9.6,
                Accel = 0.32, Brake = 0.7, Speed = 0.88, Corner = 0.42, LaneChange = 2.0, CanUTurn = false,
                Colors = new[] { 0xc1121f, 0x1d3557, 0x2a9d8f, 0xe9ecef, 0xf77f00, 0x33363b, 0x6a4c93 },
            },
            new VehicleSpec
            {
                Kind = VehicleKind.SchoolBus, Name = "School bus", Len = 9, Wid = 2.4,
                Accel = 0.4, Brake = 0.75, Speed = 0.85, Corner = 0.5, LaneChange = 1.8, CanUTurn = false,
                Colors = new[] { 0xf6b200 },
            },
        };

        public static VehicleSpec Of(VehicleKind kind)
        {
            return All[(int)kind];
        }
    }
}
