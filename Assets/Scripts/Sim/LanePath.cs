using System;
using System.Collections.Generic;

namespace Traffic.Sim
{
    public struct Vec
    {
        public double X;
        public double Y;

        public Vec(double x, double y)
        {
            X = x;
            Y = y;
        }
    }

    public struct Pose
    {
        public double X;
        public double Y;
        /// <summary>Heading in radians, counter-clockwise from +x (east).</summary>
        public double H;

        public Vec Point => new Vec(X, Y);
    }

    public struct Projection
    {
        /// <summary>Distance along the path of the closest point.</summary>
        public double S;
        /// <summary>Distance from the point to the path.</summary>
        public double D;
    }

    /// <summary>A polyline measured by distance along it. Plane: x east, y north, meters.</summary>
    public sealed class LanePath
    {
        public readonly Vec[] Pts;
        public readonly double[] Cum;
        /// <summary>Turn radius at each vertex (infinity on straights).</summary>
        public readonly double[] Rad;
        public readonly double Length;

        public LanePath(IEnumerable<Vec> points)
        {
            var pts = new List<Vec>();
            foreach (var p in points)
            {
                if (pts.Count == 0 || Geo.Hypot(p.X - pts[pts.Count - 1].X, p.Y - pts[pts.Count - 1].Y) > 1e-6) pts.Add(p);
            }
            if (pts.Count < 2) throw new ArgumentException("A path needs at least two distinct points");
            Pts = pts.ToArray();
            int n = Pts.Length;
            Cum = new double[n];
            for (int i = 1; i < n; i++) Cum[i] = Cum[i - 1] + Geo.Hypot(Pts[i].X - Pts[i - 1].X, Pts[i].Y - Pts[i - 1].Y);
            Length = Cum[n - 1];
            Rad = new double[n];
            for (int i = 0; i < n; i++) Rad[i] = double.PositiveInfinity;
            for (int i = 1; i < n - 1; i++) Rad[i] = Geo.Circumradius(Pts[i - 1], Pts[i], Pts[i + 1]);
            if (n > 2)
            {
                Rad[0] = Rad[1];
                Rad[n - 1] = Rad[n - 2];
            }
        }

        /// <summary>Index i of the segment [i, i+1] that contains distance s.</summary>
        private int Segment(double s)
        {
            int n = Pts.Length;
            if (s <= 0) return 0;
            if (s >= Length) return n - 2;
            int lo = 0;
            int hi = n - 1;
            while (hi - lo > 1)
            {
                int mid = (lo + hi) >> 1;
                if (Cum[mid] <= s) lo = mid;
                else hi = mid;
            }
            return lo;
        }

        /// <summary>Position and heading at distance s. Outside the path it continues straight.</summary>
        public Pose At(double s)
        {
            int i = Segment(s);
            Vec a = Pts[i];
            Vec b = Pts[i + 1];
            double len = Cum[i + 1] - Cum[i];
            double t = (s - Cum[i]) / len;
            return new Pose { X = a.X + (b.X - a.X) * t, Y = a.Y + (b.Y - a.Y) * t, H = Math.Atan2(b.Y - a.Y, b.X - a.X) };
        }

        public double RadiusAt(double s)
        {
            int i = Segment(s);
            return Math.Min(Rad[i], Rad[i + 1]);
        }

        public double MinRadius()
        {
            double r = double.PositiveInfinity;
            for (int i = 0; i < Rad.Length; i++) r = Math.Min(r, Rad[i]);
            return r;
        }

        /// <summary>Closest point on the path to (x, y).</summary>
        public Projection Project(double x, double y)
        {
            double bestD2 = double.PositiveInfinity;
            double bestS = 0;
            for (int i = 0; i < Pts.Length - 1; i++)
            {
                Vec a = Pts[i];
                Vec b = Pts[i + 1];
                double dx = b.X - a.X;
                double dy = b.Y - a.Y;
                double len2 = dx * dx + dy * dy;
                double t = ((x - a.X) * dx + (y - a.Y) * dy) / len2;
                t = t < 0 ? 0 : t > 1 ? 1 : t;
                double px = a.X + dx * t;
                double py = a.Y + dy * t;
                double d2 = (x - px) * (x - px) + (y - py) * (y - py);
                if (d2 < bestD2)
                {
                    bestD2 = d2;
                    bestS = Cum[i] + Math.Sqrt(len2) * t;
                }
            }
            return new Projection { S = bestS, D = Math.Sqrt(bestD2) };
        }
    }

    public static class Geo
    {
        public static double Hypot(double x, double y)
        {
            return Math.Sqrt(x * x + y * y);
        }

        public static double Clamp(double x, double lo, double hi)
        {
            return x < lo ? lo : x > hi ? hi : x;
        }

        public static double Circumradius(Vec a, Vec b, Vec c)
        {
            double ab = Hypot(b.X - a.X, b.Y - a.Y);
            double bc = Hypot(c.X - b.X, c.Y - b.Y);
            double ca = Hypot(a.X - c.X, a.Y - c.Y);
            double cross = Math.Abs((b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X));
            if (cross < 1e-9) return double.PositiveInfinity;
            return ab * bc * ca / (2 * cross);
        }

        /// <summary>
        /// Shift a polyline sideways. A positive offset is to the right of the
        /// direction of travel. The offset can vary by vertex.
        /// </summary>
        public static Vec[] Offset(IReadOnlyList<Vec> pts, Func<int, double> offset)
        {
            var result = new Vec[pts.Count];
            for (int i = 0; i < pts.Count; i++)
            {
                Vec a = pts[Math.Max(0, i - 1)];
                Vec b = pts[Math.Min(pts.Count - 1, i + 1)];
                double tx = b.X - a.X;
                double ty = b.Y - a.Y;
                double len = Hypot(tx, ty);
                if (len == 0) len = 1;
                double off = offset(i);
                result[i] = new Vec(pts[i].X + ty / len * off, pts[i].Y - tx / len * off);
            }
            return result;
        }

        public static Vec[] Offset(IReadOnlyList<Vec> pts, double offset)
        {
            return Offset(pts, _ => offset);
        }

        /// <summary>Cubic Bezier from pose a to pose b, sampled as a polyline.</summary>
        public static Vec[] Bezier(Pose a, Pose b, double k, int samples = 24)
        {
            double p1x = a.X + Math.Cos(a.H) * k, p1y = a.Y + Math.Sin(a.H) * k;
            double p2x = b.X - Math.Cos(b.H) * k, p2y = b.Y - Math.Sin(b.H) * k;
            var result = new Vec[samples + 1];
            for (int i = 0; i <= samples; i++)
            {
                double t = (double)i / samples;
                double u = 1 - t;
                double w0 = u * u * u, w1 = 3 * u * u * t, w2 = 3 * u * t * t, w3 = t * t * t;
                result[i] = new Vec(w0 * a.X + w1 * p1x + w2 * p2x + w3 * b.X, w0 * a.Y + w1 * p1y + w2 * p2y + w3 * b.Y);
            }
            return result;
        }
    }
}
