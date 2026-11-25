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

        // Quirks: all zero for an ordinary driver.

        /// <summary>0 to 1: darts between lanes for the slightest gain, into gaps others would not take.</summary>
        public double Weave;
        /// <summary>0 to 1: cannot hold the lane or a steady speed (drunk).</summary>
        public double Swerve;
        /// <summary>0 to 1: how much of the time the driver is not looking at the road (on the phone).</summary>
        public double Distraction;
        /// <summary>
        /// 0 to 1: sits in the left lane whoever is behind. Such a driver makes for the leftmost
        /// lane that goes their way and stays in it, where anyone else would move over for
        /// faster traffic (see Simulation.KeepRight).
        /// </summary>
        public double Camp;

        /// <summary>The six main dials.</summary>
        public const int Count = 6;
        /// <summary>Main dials plus quirks.</summary>
        public const int Total = 10;

        /// <summary>Lowest and highest value of each dial, in the order used by Get and Set.</summary>
        public static readonly double[,] Ranges = { { 0.1, 1.6 }, { 0.5, 1.6 }, { 0.3, 2.6 }, { 0, 1 }, { 0, 1 }, { 0, 1 }, { 0, 1 }, { 0, 1 }, { 0, 1 }, { 0, 1 } };

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
                case 5: return Compliance;
                case 6: return Weave;
                case 7: return Swerve;
                case 8: return Distraction;
                default: return Camp;
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
                case 5: Compliance = v; break;
                case 6: Weave = v; break;
                case 7: Swerve = v; break;
                case 8: Distraction = v; break;
                default: Camp = v; break;
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

    /// <summary>A driver who stands out: unusual dials, a loud color, and a name shown over the car.</summary>
    public sealed class SpecialDriver
    {
        public string Name;
        public string Blurb;
        /// <summary>Paint color as 0xRRGGBB.</summary>
        public int Color;
        public bool Flash;
        public Func<DriverParams> Make;

        public static readonly SpecialDriver[] All =
        {
            new SpecialDriver
            {
                Name = "MANIAC", Blurb = "Speeds, tailgates, weaves, and pushes through red lights", Color = 0xc02bff, Flash = true,
                Make = () => new DriverParams(0.25, 1.45, 0.35, 1, 0, 0) { Weave = 1 },
            },
            new SpecialDriver
            {
                Name = "SLOWPOKE", Blurb = "Far under the limit with a huge gap", Color = 0x00e5ff,
                Make = () => new DriverParams(0.9, 0.55, 2.6, 0, 1, 1),
            },
            new SpecialDriver
            {
                Name = "ON PHONE", Blurb = "Keeps looking away: reacts very late and sits at green lights", Color = 0xffee00,
                Make = () => new DriverParams(1.5, 0.95, 1.2, 0.4, 0.5, 0.9) { Distraction = 0.8, Swerve = 0.12 },
            },
            new SpecialDriver
            {
                Name = "DRUNK", Blurb = "Cannot hold a lane or a speed, slow to react, careless about lights", Color = 0xff5a00, Flash = true,
                Make = () => new DriverParams(1.4, 0.9, 1.0, 0.6, 0.3, 0.5) { Swerve = 1, Distraction = 0.2 },
            },
            new SpecialDriver
            {
                // About 5 mph under on a street, 7 on an avenue and 12 on a highway, and in nobody's way as far as they can tell.
                Name = "LEFT-LANE CAMPER", Blurb = "Sits in the left lane a few mph under the limit, so everyone else has to go round", Color = 0xff66c4,
                Make = () => new DriverParams(0.7, 0.82, 1.8, 0.2, 0, 1) { Camp = 1 },
            },
        };

        /// <summary>The driver who sits in the left lane under the speed limit.</summary>
        public static SpecialDriver Camper => All[4];
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
        /// <summary>Seconds the driver waits after one lane change before considering another.</summary>
        public double LcCooldown;
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
                LcTime = (3.6 - 1.6 * g) * (1 - 0.4 * p.Weave),
                LcThreshold = (0.15 + 0.45 * (1 - g)) * (1 - 0.9 * p.Weave),
                BSafe = 2.5 + 2.5 * g + 3 * p.Weave,
                Nerves = 1 - g,
                LcCooldown = (2 + 3 * (1 - g)) * (1 - 0.85 * p.Weave),
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
