using System;
using System.Collections.Generic;

namespace Traffic.Ui
{
    /// <summary>
    /// What the toolbar draws on. Sizes are in interface units with y down, colors are
    /// 0xAARRGGBB. Unity's GUI is one of these (see LabUi.cs); the preview tool is another.
    /// </summary>
    public interface ISurface
    {
        void Fill(double x, double y, double w, double h, double radius, uint color);
        /// <summary>An outline, drawn inside the box.</summary>
        void Stroke(double x, double y, double w, double h, double radius, double width, uint color);
        void Icon(string name, double x, double y, double size, uint color);
        /// <summary>Text in a box. Align 0 is left, 1 centered, 2 right, all on one line; 3 starts top left and wraps.</summary>
        void Text(string text, double x, double y, double w, double h, int size, bool bold, int align, uint color);
    }

    public enum DockKind { None, Tool, Undo, Redo, Clear, Drive, Build, Shape, Road, Height, Pause, Step, Rate }

    /// <summary>A kind of road on offer in the tray: enough to draw its picture and explain it.</summary>
    public sealed class DockRoad
    {
        /// <summary>The heading it sits under, such as "Street".</summary>
        public string Group = "";
        public string Title = "", Body = "";
        /// <summary>Lanes each way. None backward makes it one-way.</summary>
        public int Forward = 1, Backward;
        public bool Median;
    }

    /// <summary>One thing on the toolbar that can be pointed at and clicked.</summary>
    public struct DockItem
    {
        public DockKind Kind;
        /// <summary>Which one, where there are several of a kind (which tool, which road).</summary>
        public int Index;
        public double X, Y, W, H;

        public bool Contains(double x, double y)
        {
            return x >= X && x < X + W && y >= Y && y < Y + H;
        }
    }

    /// <summary>
    /// The toolbar at the bottom of the screen. While building it holds the tools, with the
    /// kinds of road in a tray above it; while driving it holds pause and the speed of time.
    /// Pointing at anything shows a card that explains it. The owner sets the fields, calls
    /// Layout, then Draw, and asks ItemAt what a click landed on.
    /// </summary>
    public sealed class Dock
    {
        public const uint Accent = 0xFF5EE0C0, Ink = 0xFF0C1A16, Light = 0xFFE7EDF0, Dim = 0xFF8D9AA3;
        private const uint Panel = 0xF2141A1E, Edge = 0x26FFFFFF, Lift = 0x16FFFFFF, Hover = 0x2CFFFFFF;
        private const uint Grass = 0xFF6F9C5B, Asphalt = 0xFF3B4147, Yellow = 0xFFF2C744, Paint = 0xE6FFFFFF;
        private const double Button = 44, Gap = 6, Pad = 8, BarHeight = 60, Margin = 14;

        public static readonly string[] RateNames = { "1/4x", "1x", "2x", "4x" };
        /// <summary>The shapes a road can be drawn in, by the name of their icon, in the order the tray shows them.</summary>
        public static readonly string[] ShapeNames = { "straight", "curve", "roundabout" };
        /// <summary>The kinds of road in the tray, in order. Roads under the same heading sit together, in two rows.</summary>
        public DockRoad[] Roads = new DockRoad[0];

        /// <summary>True on the map the player draws, which is the only one that can be built on.</summary>
        public bool OwnMap;
        public bool Building, Paused, CanUndo, CanRedo;
        /// <summary>0 draws roads, 1 bulldozes.</summary>
        public int Tool;
        public int Shape, Road, Rate;
        /// <summary>Which of HeightLevels is chosen.</summary>
        public int Height;
        /// <summary>The heights a road can be built at, in the order the tray shows them: the ground, three bridges, three tunnels.</summary>
        public static readonly int[] HeightLevels = { 0, 1, 2, 3, -1, -2, -3 };
        /// <summary>What to say about each of them.</summary>
        public string[] HeightNotes = new string[HeightLevels.Length];
        /// <summary>A line of help about what the next click does.</summary>
        public string Hint = "";

        public readonly List<DockItem> Items = new List<DockItem>();
        /// <summary>The top edge of the toolbar and its tray, for placing things above them.</summary>
        public double Top { get; private set; }

        private readonly List<double> dividers = new List<double>();
        /// <summary>Where each heading over the road tiles goes.</summary>
        private readonly List<KeyValuePair<string, double>> headings = new List<KeyValuePair<string, double>>();
        private double barX, barY, barW, trayX, trayY, trayW, trayH, noteX, noteY, noteW, width;
        private bool tray, note;

        private bool RoadTool => Building && Tool == 0;

        /// <summary>Work out where everything goes on a screen of the given size.</summary>
        public void Layout(double w, double h)
        {
            width = w;
            Items.Clear();
            dividers.Clear();

            // The bar: buttons side by side, with a thin divider between groups.
            var kinds = new List<DockItem>();
            void Add(DockKind kind, int index, double wide)
            {
                kinds.Add(new DockItem { Kind = kind, Index = index, W = wide });
            }
            if (Building)
            {
                Add(DockKind.Tool, 0, Button);
                Add(DockKind.Tool, 1, Button);
                Add(DockKind.None, 0, 0);
                Add(DockKind.Undo, 0, Button);
                Add(DockKind.Redo, 0, Button);
                Add(DockKind.Clear, 0, Button);
                Add(DockKind.None, 0, 0);
                Add(DockKind.Drive, 0, 104);
            }
            else
            {
                Add(DockKind.Pause, 0, Button);
                Add(DockKind.Step, 0, Button);
                Add(DockKind.None, 0, 0);
                for (int i = 0; i < RateNames.Length; i++) Add(DockKind.Rate, i, 50);
                if (OwnMap)
                {
                    Add(DockKind.None, 0, 0);
                    Add(DockKind.Build, 0, 104);
                }
            }
            barW = Pad * 2 - Gap;
            foreach (DockItem k in kinds) barW += k.W + Gap;
            barX = Math.Round((w - barW) / 2);
            barY = h - Margin - BarHeight;
            double x = barX + Pad;
            foreach (DockItem k in kinds)
            {
                if (k.Kind == DockKind.None) dividers.Add(x - Gap / 2);
                else Items.Add(new DockItem { Kind = k.Kind, Index = k.Index, X = x, Y = barY + Pad, W = k.W, H = Button });
                x += k.W + Gap;
            }
            Top = barY;

            // The tray of road choices sits above the bar while the road tool is out.
            tray = RoadTool;
            note = Building && !tray && Hint.Length > 0;
            headings.Clear();
            if (tray)
            {
                // The shapes in a column, then each group of roads in two rows under its heading.
                const double tile = 84, high = 44, between = 18;
                var groups = new List<KeyValuePair<string, List<int>>>();
                for (int i = 0; i < Roads.Length; i++)
                {
                    if (groups.Count == 0 || groups[groups.Count - 1].Key != Roads[i].Group) groups.Add(new KeyValuePair<string, List<int>>(Roads[i].Group, new List<int>()));
                    groups[groups.Count - 1].Value.Add(i);
                }
                int shapeColumns = (ShapeNames.Length + 1) / 2;
                trayW = 14 + shapeColumns * high + (shapeColumns - 1) * Gap + 14;
                foreach (KeyValuePair<string, List<int>> g in groups)
                {
                    int columns = (g.Value.Count + 1) / 2;
                    trayW += between + columns * tile + (columns - 1) * Gap;
                }
                trayW += between + 4 * high + 3 * Gap;
                trayH = 12 + 14 + 6 + high * 2 + Gap + 10 + 16 + 10;
                trayX = Math.Round((w - trayW) / 2);
                trayY = barY - 10 - trayH;
                double tx = trayX + 14, ty = trayY + 32;
                headings.Add(new KeyValuePair<string, double>("SHAPE", tx));
                for (int i = 0; i < ShapeNames.Length; i++) Items.Add(new DockItem { Kind = DockKind.Shape, Index = i, X = tx + (i / 2) * (high + Gap), Y = ty + (i % 2) * (high + Gap), W = high, H = high });
                tx += shapeColumns * high + (shapeColumns - 1) * Gap;
                foreach (KeyValuePair<string, List<int>> g in groups)
                {
                    tx += between;
                    headings.Add(new KeyValuePair<string, double>(g.Key.ToUpperInvariant(), tx));
                    int columns = (g.Value.Count + 1) / 2;
                    for (int k = 0; k < g.Value.Count; k++)
                    {
                        Items.Add(new DockItem { Kind = DockKind.Road, Index = g.Value[k], X = tx + (k % columns) * (tile + Gap), Y = ty + (k / columns) * (high + Gap), W = tile, H = high });
                    }
                    tx += columns * tile + (columns - 1) * Gap;
                }
                // Heights: the ground first, bridges along the top, tunnels underneath them.
                tx += between;
                headings.Add(new KeyValuePair<string, double>("HEIGHT", tx));
                for (int i = 0; i < HeightLevels.Length; i++)
                {
                    int column = i == 0 ? 0 : (i - 1) % 3 + 1, row = i > 3 ? 1 : 0;
                    Items.Add(new DockItem { Kind = DockKind.Height, Index = i, X = tx + column * (high + Gap), Y = ty + row * (high + Gap), W = high, H = high });
                }
                Top = trayY;
            }
            else if (note)
            {
                noteW = Math.Min(w - 24, Hint.Length * 6.6 + 36);
                noteX = Math.Round((w - noteW) / 2);
                noteY = barY - 10 - 34;
                Top = noteY;
            }
        }

        /// <summary>What is under a point. Its kind is None when nothing is.</summary>
        public DockItem ItemAt(double x, double y)
        {
            foreach (DockItem item in Items) if (item.Contains(x, y)) return item;
            return default(DockItem);
        }

        /// <summary>True when a point is over the toolbar or its tray, so a click there is not a click on the map.</summary>
        public bool Contains(double x, double y)
        {
            if (x >= barX && x < barX + barW && y >= barY && y < barY + BarHeight) return true;
            return tray && x >= trayX && x < trayX + trayW && y >= trayY && y < barY;
        }

        private bool Enabled(DockItem item)
        {
            return item.Kind == DockKind.Undo ? CanUndo : item.Kind != DockKind.Redo || CanRedo;
        }

        public void Draw(ISurface s, double mouseX, double mouseY)
        {
            DockItem over = ItemAt(mouseX, mouseY);

            Card(s, barX, barY, barW, BarHeight, 20);
            foreach (double d in dividers) s.Fill(d - 0.5, barY + 16, 1, BarHeight - 32, 0, Edge);
            if (tray)
            {
                Card(s, trayX, trayY, trayW, trayH, 20);
                foreach (KeyValuePair<string, double> heading in headings) s.Text(heading.Key, heading.Value, trayY + 11, 160, 14, 10, true, 0, Dim);
                s.Text(Hint, trayX + 14, trayY + trayH - 29, trayW - 28, 18, 12, false, 1, Dim);
            }
            else if (note)
            {
                Card(s, noteX, noteY, noteW, 34, 17);
                s.Text(Hint, noteX, noteY, noteW, 34, 12, false, 1, Light);
            }

            foreach (DockItem item in Items)
            {
                bool hot = item.Kind == over.Kind && item.Index == over.Index && Enabled(item);
                switch (item.Kind)
                {
                    case DockKind.Tool:
                        Glyph(s, item, item.Index == 0 ? "road" : "bulldoze", Tool == item.Index, hot, true);
                        break;
                    case DockKind.Undo:
                        Glyph(s, item, "undo", false, hot, CanUndo);
                        break;
                    case DockKind.Redo:
                        Glyph(s, item, "redo", false, hot, CanRedo);
                        break;
                    case DockKind.Clear:
                        Glyph(s, item, "trash", false, hot, true);
                        break;
                    case DockKind.Pause:
                        Glyph(s, item, Paused ? "play" : "pause", false, hot, true);
                        break;
                    case DockKind.Step:
                        Glyph(s, item, "step", false, hot, true);
                        break;
                    case DockKind.Shape:
                        if (Shape != item.Index) s.Fill(item.X, item.Y, item.W, item.H, 12, Lift);
                        Glyph(s, item, ShapeNames[item.Index], Shape == item.Index, hot, true);
                        break;
                    case DockKind.Rate:
                        {
                            bool on = Rate == item.Index && !Paused;
                            if (on) s.Fill(item.X, item.Y, item.W, item.H, 12, Accent);
                            else if (hot) s.Fill(item.X, item.Y, item.W, item.H, 12, Hover);
                            s.Text(RateNames[item.Index], item.X, item.Y, item.W, item.H, 14, true, 1, on ? Ink : Light);
                        }
                        break;
                    case DockKind.Drive:
                        s.Fill(item.X, item.Y, item.W, item.H, 12, hot ? 0xFF8BF0D6 : Accent);
                        s.Icon("play", item.X + 16, item.Y + 13, 18, Ink);
                        s.Text("Drive", item.X + 40, item.Y, item.W - 48, item.H, 15, true, 0, Ink);
                        break;
                    case DockKind.Build:
                        s.Fill(item.X, item.Y, item.W, item.H, 12, hot ? Hover : Lift);
                        s.Icon("cone", item.X + 14, item.Y + 12, 20, Light);
                        s.Text("Build", item.X + 40, item.Y, item.W - 48, item.H, 15, true, 0, Light);
                        break;
                    case DockKind.Road:
                        RoadTile(s, item, Roads[item.Index], Road == item.Index, hot);
                        break;
                    case DockKind.Height:
                        HeightTile(s, item, HeightLevels[item.Index], Height == item.Index, hot);
                        break;
                }
            }

            if (over.Kind != DockKind.None) Explain(s, over);
        }

        /// <summary>The rounded dark back that every panel sits on.</summary>
        public static void Card(ISurface s, double x, double y, double w, double h, double radius)
        {
            s.Fill(x, y, w, h, radius, Panel);
            s.Stroke(x, y, w, h, radius, 1, Edge);
        }

        /// <summary>A square button with an icon in the middle.</summary>
        private static void Glyph(ISurface s, DockItem item, string icon, bool chosen, bool hot, bool enabled)
        {
            if (chosen) s.Fill(item.X, item.Y, item.W, item.H, 12, Accent);
            else if (hot) s.Fill(item.X, item.Y, item.W, item.H, 12, Hover);
            double size = Math.Min(item.W, item.H) * 0.58;
            s.Icon(icon, item.X + (item.W - size) / 2, item.Y + (item.H - size) / 2, size, chosen ? Ink : enabled ? Light : 0x4DE7EDF0);
        }

        /// <summary>A little picture of the road from above: its lanes, lane lines, median and which way it runs.</summary>
        private static void RoadTile(ISurface s, DockItem item, DockRoad road, bool chosen, bool hot)
        {
            int lanes = road.Forward + road.Backward;
            double lane = lanes >= 4 ? 6 : lanes == 3 ? 7 : 8;
            bool twoWay = road.Backward > 0;
            double gap = !twoWay ? 0 : road.Median ? 6 : 0;
            double high = lanes * lane + gap + 4, top = item.Y + (item.H - high) / 2;
            double left = item.X, wide = item.W;
            s.Fill(left, item.Y, wide, item.H, 10, Grass);
            s.Fill(left, top, wide, high, 0, Asphalt);
            s.Fill(left, top + 1.2, wide, 1.1, 0, Paint);
            s.Fill(left, top + high - 2.3, wide, 1.1, 0, Paint);

            // Lanes that run back are drawn above the middle and lanes that run forward below it.
            double y = top + 2;
            for (int k = 1; k < road.Backward; k++) Dashes(s, left, y + k * lane, wide);
            y += road.Backward * lane;
            if (twoWay && road.Median)
            {
                s.Fill(left, y - 0.6, wide, 1.1, 0, Yellow);
                s.Fill(left, y + gap - 0.5, wide, 1.1, 0, Yellow);
                s.Fill(left, y + 1.2, wide, gap - 2.4, 0, 0xFF7DB862);
            }
            else if (twoWay)
            {
                s.Fill(left, y - 1.5, wide, 1.1, 0, Yellow);
                s.Fill(left, y + 0.4, wide, 1.1, 0, Yellow);
            }
            y += gap;
            for (int k = 1; k < road.Forward; k++) Dashes(s, left, y + k * lane, wide);
            if (!twoWay)
            {
                // Arrows show which way a one-way road runs.
                double size = Math.Min(lane, 7);
                for (int k = 0; k < road.Forward; k++) s.Icon("arrow", left + wide / 2 - size / 2, y + k * lane + (lane - size) / 2, size, Paint);
            }
            Ring(s, item, 10, chosen, hot);
        }

        /// <summary>A side view: a road on the ground, a deck on legs above it, or a tube below it.</summary>
        private static void HeightTile(ISurface s, DockItem item, int level, bool chosen, bool hot)
        {
            if (chosen) s.Fill(item.X, item.Y, item.W, item.H, 12, Accent);
            else s.Fill(item.X, item.Y, item.W, item.H, 12, hot ? Hover : Lift);
            uint ink = chosen ? Ink : Light, faint = chosen ? 0x660C1A16u : 0x66E7EDF0u;
            double x = item.X + 8, wide = item.W - 16;
            if (level == 0)
            {
                s.Fill(x, item.Y + item.H / 2 - 1.5, wide, 3, 1.5, ink);
            }
            else if (level > 0)
            {
                double ground = item.Y + item.H - 9, deck = ground - 5 - level * 6;
                s.Fill(x, ground, wide, 1.5, 0, faint);
                s.Fill(x + 5, deck, 2.5, ground - deck, 0, ink);
                s.Fill(x + wide - 7.5, deck, 2.5, ground - deck, 0, ink);
                s.Fill(x, deck - 1.5, wide, 3, 1.5, ink);
            }
            else
            {
                double ground = item.Y + 9, top = ground + 2 + (-level - 1) * 6;
                s.Fill(x, ground, wide, 1.5, 0, faint);
                s.Stroke(x + 4, top, wide - 8, 11, 5.5, 2.5, ink);
            }
        }

        private static void Dashes(ISurface s, double left, double y, double wide)
        {
            for (double x = left + 5; x < left + wide - 7; x += 16) s.Fill(x, y - 0.5, 8, 1, 0, Paint);
        }

        private static void Ring(ISurface s, DockItem item, double radius, bool chosen, bool hot)
        {
            if (chosen) s.Stroke(item.X, item.Y, item.W, item.H, radius, 2.5, Accent);
            else if (hot) s.Stroke(item.X, item.Y, item.W, item.H, radius, 1.5, 0x99FFFFFF);
        }

        /// <summary>The card that says what the thing under the pointer is.</summary>
        private void Explain(ISurface s, DockItem item)
        {
            string title, body;
            Describe(item, out title, out body);
            const double w = 290;
            // Roughly how many lines the text will wrap to.
            int lines = 0;
            foreach (string paragraph in body.Split('\n')) lines += Math.Max(1, (int)Math.Ceiling(paragraph.Length / 43.0));
            double h = 14 + 20 + 4 + lines * 16 + 14;
            double x = Math.Max(12, Math.Min(width - w - 12, item.X + item.W / 2 - w / 2));
            double y = Top - 10 - h;
            Card(s, x, y, w, h, 16);
            s.Text(title, x + 16, y + 13, w - 32, 20, 15, true, 0, Light);
            s.Text(body, x + 16, y + 37, w - 32, h - 46, 12, false, 3, 0xFFB9C4CB);
        }

        public void Describe(DockItem item, out string title, out string body)
        {
            title = "";
            body = "";
            switch (item.Kind)
            {
                case DockKind.Tool:
                    title = item.Index == 0 ? "Road" : "Bulldoze";
                    body = item.Index == 0 ? "Draw roads. Pick a shape and a kind of road, then click on the ground." : "Click a road to remove it.";
                    break;
                case DockKind.Undo:
                    title = "Undo";
                    body = CanUndo ? "Take back the last change. Ctrl+Z." : "Nothing to take back yet.";
                    break;
                case DockKind.Redo:
                    title = "Redo";
                    body = CanRedo ? "Put back what was undone. Ctrl+Y." : "Nothing has been undone.";
                    break;
                case DockKind.Clear:
                    title = "Clear the map";
                    body = "Remove every road. Undo brings them back.";
                    break;
                case DockKind.Drive:
                    title = "Drive";
                    body = "Open the roads and let the traffic in.";
                    break;
                case DockKind.Build:
                    title = "Build";
                    body = "Send the traffic home and change the roads.";
                    break;
                case DockKind.Pause:
                    title = Paused ? "Play" : "Pause";
                    body = "Space bar.";
                    break;
                case DockKind.Step:
                    title = "Step";
                    body = "Move the traffic on by one moment. Period key.";
                    break;
                case DockKind.Rate:
                    title = RateNames[item.Index] + " speed";
                    body = item.Index == 1 ? "Time runs as it does on the street." : item.Index == 0 ? "Slow motion, to watch one driver closely." : "Time runs faster.";
                    break;
                case DockKind.Shape:
                    title = item.Index == 0 ? "Straight" : item.Index == 1 ? "Curve" : "Roundabout";
                    body = item.Index == 0 ? "Click where the road starts, then where it ends. Keep clicking to carry on from there."
                        : item.Index == 1 ? "Click where the road starts and where it ends, then pull it into a round curve with the pointer and click again."
                        : "Click where the middle should be, pull out to the size you want, and click again. Roads that cross the ring join it, and whatever is inside is removed.\nNobody stops for a light: traffic already going round has the right of way.";
                    break;
                case DockKind.Road:
                    title = Roads[item.Index].Title;
                    body = Roads[item.Index].Body;
                    break;
                case DockKind.Height:
                    {
                        int level = HeightLevels[item.Index];
                        title = level == 0 ? "On the ground" : level > 0 ? "Bridge, level " + level : "Tunnel, level " + -level;
                        body = HeightNotes[item.Index] ?? "";
                    }
                    break;
            }
        }
    }
}
