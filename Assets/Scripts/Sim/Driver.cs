using System;
using System.Collections.Generic;

namespace Traffic.Sim
{
    /// <summary>The six dials that describe a driver. Everything else is derived from them.</summary>
    public sealed class DriverParams
    {
        /// <summary>Seconds before the driver acts on what they see.</summary>
        public double Reaction = 0.5;
        /// <summary>Desired speed as a multiple of the speed limit.</summary>
        public double Speed = 1.0;
        /// <summary>Following gap in seconds.</summary>
        public double Headway = 1.4;
        /// <summary>0 calm to 1 pushy: harder acceleration and braking, tighter gaps, bolder lane changes.</summary>
        public double Aggression = 0.4;
        /// <summary>0 selfish to 1 courteous: how much slowing others down counts against a lane change.</summary>
        public double Politeness = 0.5;
        /// <summary>0 to 1: chance of obeying each red light or stop sign.</summary>
        public double Compliance = 1;

        public const int Count = 6;

        /// <summary>Lowest and highest value of each dial, in the order used by Get and Set.</summary>
        public static readonly double[,] Ranges = { { 0.1, 1.6 }, { 0.6, 1.4 }, { 0.5, 2.6 }, { 0, 1 }, { 0, 1 }, { 0, 1 } };

        public DriverParams()
        {
        }

        public DriverParams(double reaction, double speed, double headway, double aggression, double politeness, double compliance)
        {
            Reaction = reaction;
            Speed = speed;
            Headway = headway;
            Aggression = aggression;
            Politeness = politeness;
            Compliance = compliance;
        }

        public DriverParams Clone()
        {
            return (DriverParams)MemberwiseClone();
        }

        public double Get(int i)
        {
            switch (i)
            {
                case 0: return Reaction;
                case 1: return Speed;
                case 2: return Headway;
                case 3: return Aggression;
                case 4: return Politeness;
                default: return Compliance;
            }
        }

        public void Set(int i, double v)
        {
            switch (i)
            {
                case 0: Reaction = v; break;
                case 1: Speed = v; break;
                case 2: Headway = v; break;
                case 3: Aggression = v; break;
                case 4: Politeness = v; break;
                default: Compliance = v; break;
            }
        }

        public static DriverParams Calm => new DriverParams(0.7, 0.9, 2.0, 0.15, 0.85, 1);
        public static DriverParams Normal => new DriverParams(0.5, 1.0, 1.4, 0.4, 0.5, 1);
        public static DriverParams Pushy => new DriverParams(0.35, 1.2, 0.8, 0.9, 0.1, 0.9);
        public static DriverParams Sleepy => new DriverParams(1.3, 0.85, 1.5, 0.3, 0.5, 0.95);

        public static readonly KeyValuePair<string, Func<DriverParams>>[] Presets =
        {
            new KeyValuePair<string, Func<DriverParams>>("Calm", () => Calm),
            new KeyValuePair<string, Func<DriverParams>>("Normal", () => Normal),
            new KeyValuePair<string, Func<DriverParams>>("Pushy", () => Pushy),
            new KeyValuePair<string, Func<DriverParams>>("Sleepy", () => Sleepy),
        };
    }

    /// <summary>Values worked out from a driver's dials.</summary>
    public struct Derived
    {
        /// <summary>Maximum acceleration, m/s².</summary>
        public double A;
        /// <summary>Comfortable braking, m/s².</summary>
        public double B;
        /// <summary>Gap kept when stopped, m.</summary>
        public double S0;
        /// <summary>Sideways acceleration tolerated in curves, m/s².</summary>
        public double ALat;
        /// <summary>Seconds a lane change takes.</summary>
        public double LcTime;
        /// <summary>Gain needed before a lane change is worth it, m/s².</summary>
        public double LcThreshold;
        /// <summary>Braking the driver is willing to force on the car behind when changing lanes, m/s².</summary>
        public double BSafe;
        /// <summary>0 to 1: how hard the driver overreacts when startled.</summary>
        public double Nerves;
    }

    public static class DriverModel
    {
        /// <summary>Hardest braking a car can physically do, m/s².</summary>
        public const double MaxBrake = 8;

        public static Derived Derive(DriverParams p)
        {
            double g = p.Aggression;
            return new Derived
            {
                A = 1.4 + 1.6 * g,
                B = 1.8 + 1.7 * g,
                S0 = 2.4 - 1.0 * g,
                ALat = 1.8 + 1.6 * g,
                LcTime = 3.6 - 1.6 * g,
                LcThreshold = 0.15 + 0.45 * (1 - g),
                BSafe = 2.5 + 2.5 * g,
                Nerves = 1 - g,
            };
        }

        /// <summary>Acceleration on an empty road toward desired speed v0.</summary>
        public static double FreeAccel(double v, double v0, Derived d)
        {
            if (v0 <= 0.01) return -d.B;
            if (v <= v0)
            {
                double r = v / v0;
                return d.A * (1 - r * r * r * r);
            }
            return -d.B * (1 - v0 / v * (v0 / v));
        }

        /// <summary>
        /// Intelligent Driver Model: acceleration given the bumper-to-bumper gap to
        /// whatever is ahead and the closing speed dv (own speed minus theirs).
        /// </summary>
        public static double Idm(double v, double v0, double gap, double dv, double headway, Derived d)
        {
            double want = d.S0 + Math.Max(0, v * headway + v * dv / (2 * Math.Sqrt(d.A * d.B)));
            double g = Math.Max(gap, 0.05);
            double acc = FreeAccel(v, v0, d) - d.A * (want / g) * (want / g);
            return Math.Max(acc, -MaxBrake);
        }

        /// <summary>Seconds to cover distance dist starting at v, accelerating at a up to vCap.</summary>
        public static double TravelTime(double dist, double v, double a, double vCap)
        {
            if (dist <= 0) return 0;
            v = Math.Max(v, 0);
            if (v >= vCap) return dist / Math.Max(v, 0.1);
            double ramp = (vCap * vCap - v * v) / (2 * a);
            if (dist <= ramp) return (-v + Math.Sqrt(v * v + 2 * a * dist)) / a;
            return (vCap - v) / a + (dist - ramp) / vCap;
        }
    }
}
