using System;

namespace Traffic.Ui
{
    /// <summary>
    /// A small software canvas that the icons are drawn on. It holds coverage only (how much
    /// of each pixel is inked), so one icon can be tinted any color. Shapes are given in a
    /// 100 by 100 box whatever the pixel size, with y down, and their edges are smoothed.
    /// It has no Unity references, so the icons can be drawn and checked outside the editor.
    /// </summary>
    public sealed class Painter
    {
        public readonly int Size;
        /// <summary>Coverage per pixel, 0 to 1, rows from the top.</summary>
        public readonly float[] Ink;
        /// <summary>When true, shapes rub ink out instead of laying it down.</summary>
        public bool Erase;

        private readonly double scale;

        public Painter(int size)
        {
            Size = size;
            Ink = new float[size * size];
            scale = size / 100.0;
        }

        /// <summary>Ink every pixel inside a shape, given its bounds and its signed distance (negative inside), both in pixels.</summary>
        private void Shape(double x0, double y0, double x1, double y1, Func<double, double, double> distance)
        {
            int left = Math.Max(0, (int)Math.Floor(x0) - 1), right = Math.Min(Size - 1, (int)Math.Ceiling(x1) + 1);
            int top = Math.Max(0, (int)Math.Floor(y0) - 1), bottom = Math.Min(Size - 1, (int)Math.Ceiling(y1) + 1);
            for (int y = top; y <= bottom; y++)
            {
                for (int x = left; x <= right; x++)
                {
                    double cover = 0.5 - distance(x + 0.5, y + 0.5);
                    if (cover <= 0) continue;
                    float c = (float)Math.Min(cover, 1);
                    int i = y * Size + x;
                    Ink[i] = Erase ? Ink[i] * (1 - c) : Math.Max(Ink[i], c);
                }
            }
        }

        /// <summary>A line with round ends. With both ends at one point it is a dot.</summary>
        public void Line(double x1, double y1, double x2, double y2, double thickness)
        {
            double ax = x1 * scale, ay = y1 * scale, bx = x2 * scale, by = y2 * scale, r = thickness * scale / 2;
            double dx = bx - ax, dy = by - ay, len2 = dx * dx + dy * dy;
            Shape(Math.Min(ax, bx) - r, Math.Min(ay, by) - r, Math.Max(ax, bx) + r, Math.Max(ay, by) + r, (px, py) =>
            {
                double t = len2 > 0 ? ((px - ax) * dx + (py - ay) * dy) / len2 : 0;
                t = t < 0 ? 0 : t > 1 ? 1 : t;
                double qx = px - ax - dx * t, qy = py - ay - dy * t;
                return Math.Sqrt(qx * qx + qy * qy) - r;
            });
        }

        public void Dot(double x, double y, double diameter)
        {
            Line(x, y, x, y, diameter);
        }

        /// <summary>A line through several points.</summary>
        public void Path(double thickness, params double[] xy)
        {
            for (int i = 0; i + 3 < xy.Length; i += 2) Line(xy[i], xy[i + 1], xy[i + 2], xy[i + 3], thickness);
        }

        /// <summary>A curve from one point to another, bending toward a control point.</summary>
        public void Curve(double x0, double y0, double cx, double cy, double x1, double y1, double thickness)
        {
            const int steps = 16;
            double px = x0, py = y0;
            for (int i = 1; i <= steps; i++)
            {
                double t = (double)i / steps, u = 1 - t;
                double x = u * u * x0 + 2 * u * t * cx + t * t * x1;
                double y = u * u * y0 + 2 * u * t * cy + t * t * y1;
                Line(px, py, x, y, thickness);
                px = x;
                py = y;
            }
        }

        /// <summary>Part of a circle, from one angle to another in degrees (0 is east, 90 is down).</summary>
        public void Arc(double cx, double cy, double radius, double from, double to, double thickness)
        {
            const int steps = 20;
            double px = 0, py = 0;
            for (int i = 0; i <= steps; i++)
            {
                double a = (from + (to - from) * i / steps) * Math.PI / 180;
                double x = cx + Math.Cos(a) * radius, y = cy + Math.Sin(a) * radius;
                if (i > 0) Line(px, py, x, y, thickness);
                px = x;
                py = y;
            }
        }

        public void Box(double x, double y, double w, double h, double radius)
        {
            double cx = (x + w / 2) * scale, cy = (y + h / 2) * scale, hw = w * scale / 2, hh = h * scale / 2;
            double r = Math.Min(radius * scale, Math.Min(hw, hh));
            Shape(cx - hw, cy - hh, cx + hw, cy + hh, (px, py) =>
            {
                double qx = Math.Abs(px - cx) - (hw - r), qy = Math.Abs(py - cy) - (hh - r);
                double outside = Math.Sqrt(Math.Max(qx, 0) * Math.Max(qx, 0) + Math.Max(qy, 0) * Math.Max(qy, 0));
                return outside + Math.Min(Math.Max(qx, qy), 0) - r;
            });
        }

        /// <summary>A filled shape with no dents, its corners given in order (either way round).</summary>
        public void Solid(params double[] xy)
        {
            int n = xy.Length / 2;
            var p = new double[xy.Length];
            double x0 = double.MaxValue, y0 = double.MaxValue, x1 = double.MinValue, y1 = double.MinValue, area = 0;
            for (int i = 0; i < n; i++)
            {
                p[i * 2] = xy[i * 2] * scale;
                p[i * 2 + 1] = xy[i * 2 + 1] * scale;
                x0 = Math.Min(x0, p[i * 2]);
                x1 = Math.Max(x1, p[i * 2]);
                y0 = Math.Min(y0, p[i * 2 + 1]);
                y1 = Math.Max(y1, p[i * 2 + 1]);
                int k = (i + 1) % n;
                area += xy[i * 2] * xy[k * 2 + 1] - xy[k * 2] * xy[i * 2 + 1];
            }
            double turn = area >= 0 ? 1 : -1;
            Shape(x0, y0, x1, y1, (px, py) =>
            {
                double worst = double.NegativeInfinity;
                for (int i = 0; i < n; i++)
                {
                    int k = (i + 1) % n;
                    double ex = p[k * 2] - p[i * 2], ey = p[k * 2 + 1] - p[i * 2 + 1];
                    double len = Math.Sqrt(ex * ex + ey * ey);
                    if (len == 0) continue;
                    // How far outside this edge the pixel is.
                    double d = ((px - p[i * 2]) * ey - (py - p[i * 2 + 1]) * ex) / len * turn;
                    worst = Math.Max(worst, d);
                }
                return worst;
            });
        }
    }
}
