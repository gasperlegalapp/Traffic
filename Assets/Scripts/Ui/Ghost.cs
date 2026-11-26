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
        public const uint GoodFill = 0x8C35D6B4, GoodEdge = 0xF5FFFFFF, GoodInk = 0xFF12A88A;
        public const uint BadFill = 0x8CFF4A3A, BadEdge = 0xF5FFD0C8, BadInk = 0xFFE5392B;
        public const uint Faint = 0x99FFFFFF, White = 0xFFFFFFFF;
        /// <summary>The road about to be built: its surface, the light along its edges, and the line down its middle.</summary>
        public const uint Tar = 0xEB2C2F34, TarBad = 0xE04A2623, Lit = 0xFF8DF7E2, LitBad = 0xFFFF8E7E, Paint = 0xF0EFBD2E;

        public readonly List<Vec> Points = new List<Vec>();
        public readonly List<uint> Colors = new List<uint>();
        public readonly List<int> Tris = new List<int>();

        /// <summary>
        /// The arrows that run along the road about to be built, the way its traffic will go.
        /// They move, so they are kept apart from the rest: Run fills them in for a moment in time.
        /// </summary>
        public readonly List<Vec> RunPoints = new List<Vec>();
        public readonly List<uint> RunColors = new List<uint>();
        public readonly List<int> RunTris = new List<int>();

        /// <summary>A marker standing up at an end of the road being drawn, so the ends show from any distance.</summary>
        public struct Post
        {
            public Vec At;
            public bool Good;
            /// <summary>True for the end in the player's hand, which carries the price.</summary>
            public bool Held;
        }

        /// <summary>A few words to show at a place on the map, such as "New intersection".</summary>
        public struct Label
        {
            public Vec At;
            public string Text;
            public bool Good;
        }

        public readonly List<Post> Posts = new List<Post>();
        public readonly List<Label> Labels = new List<Label>();

        /// <summary>One line of traffic on the road about to be built: where it runs and how big its arrows are.</summary>
        private struct Flow
        {
            public Vec[] Line;
            public double Size;
        }

        private readonly List<Flow> flows = new List<Flow>();

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
            RunPoints.Clear();
            RunColors.Clear();
            RunTris.Clear();
            Posts.Clear();
            Labels.Clear();
            flows.Clear();
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

        /// <summary>A band along a line that fades from one color at one side to another at the other.</summary>
        public void Fade(IReadOnlyList<Vec> line, double from, double to, uint colorFrom, uint colorTo)
        {
            if (line.Count < 2) return;
            Vec[] a = Geo.Offset(line, from), b = Geo.Offset(line, to);
            int first = Points.Count;
            for (int i = 0; i < line.Count; i++)
            {
                Add(a[i], colorFrom);
                Add(b[i], colorTo);
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

        /// <summary>A round patch of light: solid out to one radius, then fading away to nothing at another.</summary>
        public void Glow(Vec at, double solid, double radius, uint color)
        {
            const int sides = 32;
            uint clear = color & 0x00FFFFFF;
            int center = Add(at, color), first = Points.Count;
            for (int i = 0; i < sides; i++)
            {
                double a = i * 2 * Math.PI / sides;
                Add(new Vec(at.X + Math.Cos(a) * solid, at.Y + Math.Sin(a) * solid), color);
                Add(new Vec(at.X + Math.Cos(a) * radius, at.Y + Math.Sin(a) * radius), clear);
            }
            for (int i = 0; i < sides; i++)
            {
                int k = first + i * 2, n = first + (i + 1) % sides * 2;
                Tris.Add(center); Tris.Add(k); Tris.Add(n);
                Tris.Add(k); Tris.Add(k + 1); Tris.Add(n);
                Tris.Add(k + 1); Tris.Add(n + 1); Tris.Add(n);
            }
        }

        /// <summary>
        /// The place where the road being drawn will meet another: a pad of light on the ground
        /// there with a firm ring round it, since an intersection will be made.
        /// </summary>
        public void Pad(Vec at, double radius, bool good)
        {
            uint lit = good ? Lit : LitBad;
            Glow(at, radius * 0.5, radius * 1.45, (lit & 0x00FFFFFF) | 0xA6000000);
            Ring(at, radius, AtLeast(0.5, 2.4), good ? GoodEdge : BadEdge);
        }

        /// <summary>The blue of a lane that can be built on from.</summary>
        public const uint LaneBlue = 0xFF2F86FF;

        /// <summary>
        /// The mark on a lane that can be built on from: a blue disc with an arrowhead showing
        /// the way the lane's traffic runs. Under the pointer it is drawn the other way about,
        /// white with a blue arrow, and a little larger.
        /// </summary>
        public void Knob(Vec at, double heading, bool hot)
        {
            // No wider than its lane, so that the arrows of lanes side by side stand clear of each other, until from far out it would vanish.
            double r = Math.Max(Math.Min(AtLeast(1.4, 6), 1.55), Pixel * 3.5) * (hot ? 1.2 : 1);
            Disc(at, r + Math.Min(AtLeast(0.3, 1.4), 0.3), hot ? LaneBlue : White);
            Disc(at, r, hot ? White : LaneBlue);
            Arrowhead(new Vec(at.X + Math.Cos(heading) * r * 0.55, at.Y + Math.Sin(heading) * r * 0.55), heading, r * 0.95, hot ? LaneBlue : White);
        }

        /// <summary>
        /// The mark just beyond the open end of a road with several lanes, for building on from
        /// the road as a whole and not from one lane of it: a white ring, larger than a lane's arrow.
        /// </summary>
        public void Whole(Vec at, double radius, bool hot)
        {
            double width = Math.Max(Math.Min(AtLeast(0.45, 2.2), 0.6), Pixel);
            Disc(at, radius, hot ? White : 0x66FFFFFF);
            Ring(at, radius, width, hot ? LaneBlue : White);
            Disc(at, radius * 0.3, hot ? LaneBlue : White);
        }

        /// <summary>The mark on a lane that a road can be brought in to: a broken blue ring.</summary>
        public void Socket(Vec at, bool hot)
        {
            double r = Math.Max(Math.Min(AtLeast(1.5, 6.5), 1.6), Pixel * 3.5) * (hot ? 1.2 : 1), width = Math.Max(Math.Min(AtLeast(0.4, 2), 0.45), Pixel);
            const int pieces = 8, steps = 4;
            for (int i = 0; i < pieces; i++)
            {
                var arc = new List<Vec>();
                for (int k = 0; k <= steps; k++)
                {
                    double a = (i + 0.62 * k / steps) * 2 * Math.PI / pieces;
                    arc.Add(new Vec(at.X + Math.Cos(a) * r, at.Y + Math.Sin(a) * r));
                }
                Stroke(arc, width, hot ? White : LaneBlue);
            }
            if (hot) Disc(at, r * 0.35, LaneBlue);
        }

        /// <summary>Stand a marker up at an end of the road being drawn.</summary>
        public void Stand(Vec at, bool good, bool held)
        {
            Posts.Add(new Post { At = at, Good = good, Held = held });
        }

        public void Say(Vec at, string text, bool good)
        {
            Labels.Add(new Label { At = at, Text = text, Good = good });
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
        /// A road that is not there yet, shown as itself: dark pavement at its full width with
        /// a line of light along each edge and a soft glow beyond, a yellow line down the middle
        /// of a two-way road, and arrows running along it the way its traffic will (see Run).
        /// Red, and still, when it cannot be built.
        /// </summary>
        public void Road(IReadOnlyList<Vec> line, double width, bool oneWay, bool good)
        {
            if (line.Count < 2 || Length(line) < 0.5) return;
            uint lit = good ? Lit : LitBad, soft = (lit & 0x00FFFFFF) | 0x66000000, clear = lit & 0x00FFFFFF;
            double half = width / 2, rim = AtLeast(0.55, 2.6), halo = AtLeast(2.4, 8);
            Fade(line, half, half + halo, soft, clear);
            Fade(line, -half, -half - halo, soft, clear);
            Band(line, -half, half, good ? Tar : TarBad);
            if (!oneWay) Stroke(line, AtLeast(0.3, 1.3), good ? Paint : BadEdge);
            Band(line, -half - rim / 2, -half + rim / 2, lit);
            Band(line, half - rim / 2, half + rim / 2, lit);
            if (!good) return;
            if (oneWay)
            {
                flows.Add(new Flow { Line = Copy(line, false), Size = Math.Max(Math.Min(width * 0.3, 3.4), AtLeast(1.5, 7)) });
                return;
            }
            // From far out the road is only a few pixels wide, and arrows going both ways along it would lie on top of each other.
            if (width < Pixel * 14) return;
            // Traffic keeps to the right: one line of arrows each way, each in its own half of the road.
            double size = Math.Max(Math.Min(width * 0.17, 3), AtLeast(1.2, 5.5));
            Vec[] back = Copy(line, true);
            flows.Add(new Flow { Line = Geo.Offset(line, width / 4), Size = size });
            flows.Add(new Flow { Line = Geo.Offset(back, width / 4), Size = size });
        }

        private static Vec[] Copy(IReadOnlyList<Vec> line, bool reversed)
        {
            var copy = new Vec[line.Count];
            for (int i = 0; i < copy.Length; i++) copy[i] = line[reversed ? copy.Length - 1 - i : i];
            return copy;
        }

        /// <summary>
        /// Put the running arrows where they are after so many seconds. Called every frame while
        /// a road is being drawn; the rest of the picture is left alone.
        /// </summary>
        public void Run(double seconds)
        {
            RunPoints.Clear();
            RunColors.Clear();
            RunTris.Clear();
            double gap = AtLeast(15, 46), speed = AtLeast(11, 30), fade = AtLeast(6, 18);
            double shift = seconds * speed % gap;
            foreach (Flow flow in flows)
            {
                double total = Length(flow.Line);
                if (total < flow.Size * 2) continue;
                for (double s = shift; s < total; s += gap)
                {
                    // Each arrow comes in and goes out gently at the ends of the road.
                    double shown = Math.Min(1, Math.Min(s, total - s) / fade);
                    if (shown <= 0.02) continue;
                    double heading;
                    Vec tip = Along(flow.Line, s, out heading);
                    Chevron(tip, heading, flow.Size, ((uint)(shown * 235) << 24) | 0x00FFFFFF);
                }
            }
        }

        /// <summary>An open arrowhead, like the ones painted on a road to say which way to go.</summary>
        private void Chevron(Vec tip, double heading, double size, uint color)
        {
            double cx = Math.Cos(heading), cy = Math.Sin(heading);
            double span = size * 0.62, thick = size * 0.42, back = size * 0.62;
            int At(double along, double across)
            {
                RunPoints.Add(new Vec(tip.X - cx * along - cy * across, tip.Y - cy * along + cx * across));
                RunColors.Add(color);
                return RunPoints.Count - 1;
            }
            int a = At(0, 0), d = At(thick, 0);
            int b = At(back, span), c = At(back + thick, span);
            int e = At(back, -span), f = At(back + thick, -span);
            RunTris.Add(a); RunTris.Add(b); RunTris.Add(c);
            RunTris.Add(a); RunTris.Add(c); RunTris.Add(d);
            RunTris.Add(a); RunTris.Add(d); RunTris.Add(f);
            RunTris.Add(a); RunTris.Add(f); RunTris.Add(e);
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
