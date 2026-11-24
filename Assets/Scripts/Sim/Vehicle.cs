namespace Traffic.Sim
{
    public enum VehicleKind
    {
        Car,
        Truck,
        Motorcycle,
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
        };

        public static VehicleSpec Of(VehicleKind kind)
        {
            return All[(int)kind];
        }
    }
}
