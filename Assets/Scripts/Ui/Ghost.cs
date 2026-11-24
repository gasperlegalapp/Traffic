using System;
using System.Collections.Generic;
using Traffic.Sim;

namespace Traffic.Ui
{
    /// <summary>
    /// The see-through picture laid on the ground while building: the road about to be placed
    /// at its real width, the points it will join, the handles that can be dragged. It is a
    /// list of flat colored triangles, drawn in the order they were added, so later shapes
    /// lie over earlier ones. It has no Unity references: the game copies it into a mesh, and
    /// Tools/UiPreview draws it to a web page.
    /// </summary>
    public sealed class Ghost
    {
        // Colors are 0xAARRGGBB, as on the toolbar.
        public const uint GoodFill = 0x8C3D9BFF, GoodEdge = 0xF5FFFFFF, GoodInk = 0xFF1F7FE0;
        public const uint BadFill = 0x8CFF4A3A, BadEdge = 0xF5FFD0C8, BadInk = 0xFFE5392B;
        public const uint Faint = 0x99FFFFFF, White = 0xFFFFFFFF;

        public readonly List<Vec> Points = new List<Vec>();
        public readonly List<uint> Colors = new List<uint>();
        public readonly List<int> Tris = new List<int>();

        /// <summary>
        /// How many meters of ground one pixel of screen covers. Outlines, pins and handles
        /// are kept at least a few pixels across, so they stay visible however far out the view is.
        /// </summary>
        public double Pixel = 0.4;

        public void Clear()
        {
            Points.Clear();
            Colors.Clear();
            Tris.Clear();
        }

        /// <summary>A size in meters, or that many pixels if that is larger.</summary>
        public double AtLeast(double meters, double pixels)
        {
            return Math.Max(meters, pixels * Pixel);
        }

        private int Add(Vec p, uint color)
        {
            Points.Add(p);
            Colors.Add(color);
            return Points.Count - 1;
        }

        public void Triangle(Vec a, Vec b, Vec c, uint color)
        {
            Tris.Add(Add(a, color));
            Tris.Add(Add(b, color));
            Tris.Add(Add(c, color));
        }

        /// <summary>A band along a line, from one distance to the right of it to another (negative is to the left).</summary>
        public void Band(IReadOnlyList<Vec> line, double from, double to, uint color)
        {
            if (line.Count < 2) return;
            Vec[] a = Geo.Offset(line, from), b = Geo.Offset(line, to);
            int first = Points.Count;
            for (int i = 0; i < line.Count; i++)
            {
                Add(a[i], color);
                Add(b[i], color);
            }
            for (int i = 0; i + 1 < line.Count; i++)
            {
                int k = first + i * 2;
                Tris.Add(k); Tris.Add(k + 1); Tris.Add(k + 2);
                Tris.Add(k + 1); Tris.Add(k + 3); Tris.Add(k + 2);
            }
        }

        /// <summary>A line of the given width.</summary>
        public void Stroke(IReadOnlyList<Vec> line, double width, uint color)
        {
            Band(line, -width / 2, width / 2, color);
        }

        /// <summary>The spot a distance along a line, and the way the line is heading there.</summary>
        public static Vec Along(IReadOnlyList<Vec> line, double s, out double heading)
        {
            heading = 0;
            for (int i = 0; i + 1 < line.Count; i++)
            {
                double dx = line[i + 1].X - line[i].X, dy = line[i + 1].Y - line[i].Y, len = Geo.Hypot(dx, dy);
                if (len < 1e-9) continue;
                heading = Math.Atan2(dy, dx);
                if (s <= len || i + 2 == line.Count)
                {
                    double f = Math.Min(s / len, 1);
                    return new Vec(line[i].X + dx * f, line[i].Y + dy * f);
                }
                s -= len;
            }
            return line[line.Count - 1];
        }

        public static double Length(IReadOnlyList<Vec> line)
        {
            double total = 0;
            for (int i = 0; i + 1 < line.Count; i++) total += Geo.Hypot(line[i + 1].X - line[i].X, line[i + 1].Y - line[i].Y);
            return total;
        }

        /// <summary>The part of a line between two distances along it.</summary>
        public static List<Vec> Part(IReadOnlyList<Vec> line, double from, double to)
        {
            var part = new List<Vec>();
            double heading, run = 0;
            part.Add(Along(line, from, out heading));
            for (int i = 0; i + 1 < line.Count; i++)
            {
                run += Geo.Hypot(line[i + 1].X - line[i].X, line[i + 1].Y - line[i].Y);
                if (run > from + 1e-6 && run < to - 1e-6) part.Add(line[i + 1]);
            }
            part.Add(Along(line, to, out heading));
            return part;
        }

        /// <summary>A broken line: so much drawn, so much left out.</summary>
        public void Dashes(IReadOnlyList<Vec> line, double width, double on, double off, uint color)
        {
            double total = Length(line);
            for (double s = off / 2; s < total; s += on + off) Stroke(Part(line, s, Math.Min(s + on, total)), width, color);
        }

        public void Disc(Vec at, double radius, uint color)
        {
            const int sides = 24;
            int center = Add(at, color), first = Points.Count;
            for (int i = 0; i < sides; i++) Add(new Vec(at.X + Math.Cos(i * 2 * Math.PI / sides) * radius, at.Y + Math.Sin(i * 2 * Math.PI / sides) * radius), color);
            for (int i = 0; i < sides; i++)
            {
                Tris.Add(center); Tris.Add(first + i); Tris.Add(first + (i + 1) % sides);
            }
        }

        public void Ring(Vec at, double radius, double width, uint color)
        {
            const int sides = 40;
            int first = Points.Count;
            for (int i = 0; i <= sides; i++)
            {
                double a = i * 2 * Math.PI / sides;
                Add(new Vec(at.X + Math.Cos(a) * (radius - width / 2), at.Y + Math.Sin(a) * (radius - width / 2)), color);
                Add(new Vec(at.X + Math.Cos(a) * (radius + width / 2), at.Y + Math.Sin(a) * (radius + width / 2)), color);
            }
            for (int i = 0; i < sides; i++)
            {
                int k = first + i * 2;
                Tris.Add(k); Tris.Add(k + 1); Tris.Add(k + 2);
                Tris.Add(k + 1); Tris.Add(k + 3); Tris.Add(k + 2);
            }
        }

        /// <summary>An arrowhead with its point at a spot, heading a given way.</summary>
        public void Arrowhead(Vec tip, double heading, double size, uint color)
        {
            double cx = Math.Cos(heading), cy = Math.Sin(heading);
            Vec Back(double along, double across)
            {
                return new Vec(tip.X - cx * along - cy * across, tip.Y - cy * along + cx * across);
            }
            Triangle(tip, Back(size, size * 0.62), Back(size, -size * 0.62), color);
        }

        /// <summary>
        /// A road that is not there yet: its full width tinted, a firm line down each edge, and
        /// down the middle either a broken line or, on a one-way road, arrows showing which way
        /// traffic will run. Blue when it can be built and red when it cannot.
        /// </summary>
        public void Road(IReadOnlyList<Vec> line, double width, bool oneWay, bool good)
        {
            if (line.Count < 2 || Length(line) < 0.5) return;
            uint fill = good ? GoodFill : BadFill, edge = good ? GoodEdge : BadEdge;
            double rim = AtLeast(0.4, 2.2);
            Band(line, -width / 2, width / 2, fill);
            Band(line, -width / 2 - rim / 2, -width / 2 + rim / 2, edge);
            Band(line, width / 2 - rim / 2, width / 2 + rim / 2, edge);
            double total = Length(line);
            if (oneWay)
            {
                double size = Math.Max(Math.Min(width * 0.36, 3.2), AtLeast(1.4, 7));
                double gap = AtLeast(26, 70);
                int count = Math.Max(1, (int)(total / gap));
                for (int i = 0; i < count; i++)
                {
                    double heading;
                    Vec at = Along(line, (i + 0.5) * total / count + size / 2, out heading);
                    Arrowhead(at, heading, size, edge);
                }
            }
            else
            {
                Dashes(line, AtLeast(0.3, 1.6), AtLeast(3, 9), AtLeast(4.5, 12), edge);
            }
        }

        /// <summary>
        /// A point of the road being drawn: a white dot with a colored rim. Where it has caught
        /// on a road or a point that is already there, it gets a wider ring round it as well.
        /// </summary>
        public void Pin(Vec at, bool good, bool caught)
        {
            double r = AtLeast(1.7, 6.5);
            uint ink = good ? GoodInk : BadInk;
            if (caught) Ring(at, r * 1.9, AtLeast(0.45, 2.2), good ? GoodEdge : BadEdge);
            Disc(at, r, ink);
            Disc(at, r * 0.62, White);
        }

        /// <summary>A small mark on a point of the map: a place a new road can start from, or be dragged by.</summary>
        public void Dot(Vec at, double meters, double pixels, uint color)
        {
            Disc(at, AtLeast(meters, pixels), color);
        }
    }
}
