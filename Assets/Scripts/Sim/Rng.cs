using System.Collections.Generic;

namespace Traffic.Sim
{
    /// <summary>Small seeded random generator (mulberry32) so every run can be replayed exactly.</summary>
    public sealed class Rng
    {
        private uint a;

        public Rng(uint seed)
        {
            a = seed;
        }

        /// <summary>Uniform in [0, 1).</summary>
        public double Next()
        {
            unchecked
            {
                a += 0x6d2b79f5u;
                uint t = a;
                t = (t ^ (t >> 15)) * (t | 1u);
                t ^= t + (t ^ (t >> 7)) * (t | 61u);
                return (t ^ (t >> 14)) / 4294967296.0;
            }
        }

        public double Range(double lo, double hi)
        {
            return lo + (hi - lo) * Next();
        }

        public int Int(int n)
        {
            return (int)System.Math.Floor(Next() * n);
        }

        public T Pick<T>(IReadOnlyList<T> items)
        {
            return items[Int(items.Count)];
        }
    }
}
