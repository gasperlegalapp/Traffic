using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using Traffic.Sim;
using Traffic.Ui;

/// <summary>Draws the toolbar as a web page: boxes for the panels, images for the icons. Below it, the road pen at work.</summary>
internal sealed class HtmlSurface : ISurface
{
    public readonly StringBuilder Html = new StringBuilder();
    public double OffsetY;
    private readonly Dictionary<string, string> icons = new Dictionary<string, string>();

    private static string N(double v)
    {
        return v.ToString("0.##", CultureInfo.InvariantCulture);
    }

    private static string Css(uint c)
    {
        return "rgba(" + ((c >> 16) & 255) + "," + ((c >> 8) & 255) + "," + (c & 255) + "," + N((c >> 24) / 255.0) + ")";
    }

    private string Place(double x, double y, double w, double h)
    {
        return "left:" + N(x) + "px;top:" + N(y + OffsetY) + "px;width:" + N(w) + "px;height:" + N(h) + "px;";
    }

    public void Fill(double x, double y, double w, double h, double radius, uint color)
    {
        Html.Append("<div style='" + Place(x, y, w, h) + "border-radius:" + N(radius) + "px;background:" + Css(color) + "'></div>\n");
    }

    public void Stroke(double x, double y, double w, double h, double radius, double width, uint color)
    {
        Html.Append("<div style='" + Place(x, y, w, h) + "border-radius:" + N(radius) + "px;border:" + N(width) + "px solid " + Css(color) + "'></div>\n");
    }

    public void Icon(string name, double x, double y, double size, uint color)
    {
        string key = name + color;
        string data;
        if (!icons.TryGetValue(key, out data))
        {
            const int px = 96;
            float[] ink = Icons.Draw(name, px);
            var rgba = new byte[px * px * 4];
            for (int i = 0; i < ink.Length; i++)
            {
                rgba[i * 4] = (byte)(color >> 16);
                rgba[i * 4 + 1] = (byte)(color >> 8);
                rgba[i * 4 + 2] = (byte)color;
                rgba[i * 4 + 3] = (byte)Math.Round(ink[i] * (color >> 24));
            }
            data = "data:image/png;base64," + Convert.ToBase64String(Png.Encode(rgba, px, px));
            icons[key] = data;
        }
        Html.Append("<img src='" + data + "' style='" + Place(x, y, size, size) + "'>\n");
    }

    public void Text(string text, double x, double y, double w, double h, int size, bool bold, int align, uint color)
    {
        string flow = align == 3
            ? "white-space:normal;line-height:16px;"
            : "white-space:nowrap;display:flex;align-items:center;justify-content:" + (align == 1 ? "center" : align == 2 ? "flex-end" : "flex-start") + ";";
        Html.Append("<div style='" + Place(x, y, w, h) + flow + "font-size:" + size + "px;font-weight:" + (bold ? "bold" : "normal") + ";color:" + Css(color) + "'>"
            + System.Net.WebUtility.HtmlEncode(text).Replace("\n", "<br>") + "</div>\n");
    }
}

internal static class Png
{
    public static byte[] Encode(byte[] rgba, int w, int h)
    {
        var raw = new MemoryStream();
        for (int y = 0; y < h; y++)
        {
            raw.WriteByte(0);
            raw.Write(rgba, y * w * 4, w * 4);
        }
        var packed = new MemoryStream();
        using (var z = new ZLibStream(packed, CompressionLevel.Optimal, true)) z.Write(raw.GetBuffer(), 0, (int)raw.Length);
        var png = new MemoryStream();
        png.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, 0, 8);
        var head = new byte[13];
        Big(head, 0, (uint)w);
        Big(head, 4, (uint)h);
        head[8] = 8;
        head[9] = 6;
        Chunk(png, "IHDR", head);
        Chunk(png, "IDAT", packed.ToArray());
        Chunk(png, "IEND", new byte[0]);
        return png.ToArray();
    }

    private static void Big(byte[] b, int at, uint v)
    {
        b[at] = (byte)(v >> 24);
        b[at + 1] = (byte)(v >> 16);
        b[at + 2] = (byte)(v >> 8);
        b[at + 3] = (byte)v;
    }

    private static void Chunk(Stream s, string type, byte[] data)
    {
        var len = new byte[4];
        Big(len, 0, (uint)data.Length);
        s.Write(len, 0, 4);
        var body = new byte[4 + data.Length];
        Encoding.ASCII.GetBytes(type, 0, 4, body, 0);
        Array.Copy(data, 0, body, 4, data.Length);
        s.Write(body, 0, body.Length);
        uint crc = 0xffffffff;
        foreach (byte b in body)
        {
            crc ^= b;
            for (int k = 0; k < 8; k++) crc = (crc & 1) != 0 ? 0xedb88320 ^ (crc >> 1) : crc >> 1;
        }
        Big(len, 0, ~crc);
        s.Write(len, 0, 4);
    }
}

internal static class Program
{
    private static void Main(string[] args)
    {
        const double w = 1280, h = 380;
        // The same list the game makes from its road types (see LabApp.RoadTiles).
        var roads = new List<DockRoad>();
        Action<string, int, int, bool, string> road = (group, fwd, back, median, body) => roads.Add(new DockRoad
        {
            Group = group, Forward = fwd, Backward = back, Median = median, Body = body,
            Title = group + ", " + (fwd == 1 ? "1 lane" : fwd + " lanes") + (back == 0 ? " one-way" : " each way"),
        });
        road("Street", 1, 1, false, "25 mph.  $2 a meter.  24 ft wide.\nA double yellow line divides the two directions.\nWhere only streets meet, drivers get stop signs all round.\nCurves no tighter than 70 ft.");
        road("Street", 1, 0, false, "25 mph.  $1 a meter.  12 ft wide.\nTraffic runs the way you draw it, from the first click to the last.");
        road("Avenue", 1, 1, true, "40 mph.  $3 a meter.  34 ft wide.\nA grass median divides the two directions.");
        road("Avenue", 2, 2, true, "40 mph.  $6 a meter.  58 ft wide.\nA grass median divides the two directions.\nWhere it meets another road it gets a traffic light.\nCurves no tighter than 100 ft.");
        road("Avenue", 1, 0, false, "40 mph.  $2 a meter.");
        road("Avenue", 2, 0, false, "40 mph.  $4 a meter.");
        roads.Add(new DockRoad { Group = "Avenue", Forward = 2, Backward = 2, Turn = true, Title = "Avenue, 2 lanes each way, with a turning lane in the middle", Body = "40 mph.  $7.50 a meter.  60 ft wide.\nA turning lane runs down the middle: traffic about to turn left waits there, out of the way of the rest." });
        road("Highway", 1, 0, false, "65 mph.  $3 a meter.");
        road("Highway", 2, 0, false, "65 mph.  $6 a meter.");
        road("Highway", 3, 0, false, "65 mph.  $9 a meter.  36 ft wide.\nTraffic runs the way you draw it, from the first click to the last.\nWhere it meets another road it gets a traffic light.\nCurves no tighter than 200 ft.");
        road("Highway", 4, 0, false, "65 mph.  $12 a meter.");
        var s = new HtmlSurface();
        // Each row is one state of the toolbar, with the pointer resting on something.
        var states = new (string Name, Action<Dock> Set, DockKind Point, int Index)[]
        {
            ("Building, pointing at the wide avenue", d => { d.Building = true; d.Road = 0; d.CanUndo = true; d.Hint = "Click where the road starts."; }, DockKind.Road, 3),
            ("Building a curve on a bridge, pointing at bridge level 2", d => { d.Building = true; d.Shape = 1; d.Road = 5; d.Height = 1; d.CanUndo = true; d.CanRedo = true; d.Hint = "Sweep the road round to where it should end, and click. Right-click to point it again."; }, DockKind.Shape, 1),
            ("Moving, pointing at the move tool", d => { d.Building = true; d.Tool = 3; d.CanUndo = true; d.Hint = "Drag a point to move it. Drag the middle of a road to bend it, or pull it back straight."; }, DockKind.Tool, 3),
            ("Bulldozing", d => { d.Building = true; d.Tool = 1; d.Hint = "Click a road to remove it."; }, DockKind.Tool, 1),
            ("Driving on your own map", d => { d.Rate = 1; }, DockKind.Build, 0),
            ("Driving, paused, on a built-in map", d => { d.OwnMap = false; d.Paused = true; d.Rate = 2; }, DockKind.Pause, 0),
        };
        for (int i = 0; i < states.Length; i++)
        {
            var dock = new Dock { OwnMap = true, Roads = roads.ToArray() };
            dock.HeightNotes[2] = "33 ft up.  Costs 3 times as much.\nIt needs 230 ft at each end to climb, so at least 520 ft in all.\nIt crosses other roads without meeting them, given a level's height between them. Roads can only join it at its ends.";
            states[i].Set(dock);
            dock.Layout(w, h);
            s.OffsetY = i * h;
            s.Html.Append("<div style='left:0;top:" + (i * h) + "px;width:" + w + "px;height:" + h + "px;background:" + (i % 2 == 0 ? "#7da566" : "#6f9c5b") + "'></div>\n");
            s.Text(states[i].Name, 16, 10, 600, 20, 13, false, 0, 0xFF16301C);
            double mx = -1, my = -1;
            foreach (DockItem item in dock.Items)
            {
                if (item.Kind != states[i].Point || item.Index != states[i].Index) continue;
                mx = item.X + item.W / 2;
                my = item.Y + item.H / 2;
            }
            dock.Draw(s, mx, my);
        }
        // The lane tool's card, with the pointer on each button in turn.
        for (int i = 0; i < 4; i++)
        {
            var card = new LaneCard { Added = i == 1, Turns = i >= 2, CanMiddle = i == 3 };
            card.TurnThere[0] = card.TurnThere[1] = card.TurnThere[2] = true;
            card.TurnMay[0] = true;
            s.OffsetY = (states.Length - 1) * h;
            card.Layout(170 + i * 300, i == 2 ? 215 : i == 3 ? 265 : 150, w, h);
            card.Draw(s, 80 + i * 430, i >= 2 ? 130 : 80);
        }
        // Every icon, large, to check the drawing.
        s.OffsetY = states.Length * h;
        s.Html.Append("<div style='left:0;top:" + s.OffsetY + "px;width:" + w + "px;height:130px;background:#141a1e'></div>\n");
        for (int i = 0; i < Icons.Names.Length; i++)
        {
            s.Icon(Icons.Names[i], 14 + i * 70, 24, 58, 0xFFE7EDF0);
            s.Text(Icons.Names[i], 14 + i * 70, 92, 58, 16, 10, false, 1, 0xFF8D9AA3);
        }
        // The road pen at work: what the player sees on the ground at each step of drawing.
        var scenes = new StringBuilder();
        double scenesTop = states.Length * h + 130;
        int scene = 0;
        foreach (PenScene one in PenScenes.All())
        {
            double x = 16 + (scene % 3) * 420, y = scenesTop + 16 + (scene / 3) * 330;
            scenes.Append("<div style='left:" + x + "px;top:" + y + "px;width:404px;height:314px'>" + one.Svg(404, 290) + "</div>\n");
            s.OffsetY = 0;
            s.Text(one.Name, x, y + 292, 404, 20, 12, false, 0, 0xFF16301C);
            scene++;
        }
        double scenesHigh = 16 + ((scene + 2) / 3) * 330;
        string backdrop = "<div style='left:0;top:" + scenesTop + "px;width:" + w + "px;height:" + scenesHigh + "px;background:#dfe6dc'></div>\n";
        string page = "<!doctype html><meta charset='utf-8'><style>body{margin:0;font-family:Arial,'Liberation Sans',sans-serif}div,img{position:absolute;box-sizing:border-box}</style>\n"
            + "<div style='position:relative;width:" + w + "px;height:" + (states.Length * h + 130 + scenesHigh) + "px'>\n" + backdrop + s.Html + scenes + "</div>";
        File.WriteAllText(args.Length > 0 ? args[0] : "toolbar.html", page);
    }
}

/// <summary>One moment of drawing a road: a map, the pen in some state, and the pointer somewhere.</summary>
internal sealed class PenScene
{
    public string Name;
    public RoadPen Pen;
    /// <summary>The middle of the picture on the map, and how many pixels a meter takes.</summary>
    public Vec Center;
    public double Zoom = 1.6;

    private static string N(double v)
    {
        return v.ToString("0.#", CultureInfo.InvariantCulture);
    }

    /// <summary>The picture: grass, the roads already built, the pen's ghost over them, and its note beside the pointer.</summary>
    public string Svg(double w, double h)
    {
        Func<Vec, string> at = p => N(w / 2 + (p.X - Center.X) * Zoom) + "," + N(h / 2 - (p.Y - Center.Y) * Zoom);
        var svg = new StringBuilder("<svg xmlns='http://www.w3.org/2000/svg' width='" + w + "' height='" + h + "' style='border-radius:10px;background:#659d48'>");
        foreach (EditSegment road in Pen.Map.Segments)
        {
            var pts = new StringBuilder();
            foreach (Vec p in Pen.Map.Trace(road)) pts.Append(at(p) + " ");
            svg.Append("<polyline points='" + pts + "' fill='none' stroke='#3a3c40' stroke-width='" + N(road.Type.Width * Zoom) + "'/>");
            svg.Append("<polyline points='" + pts + "' fill='none' stroke='" + (road.Type.OneWay ? "#f1f1ec" : "#efbd2e") + "' stroke-width='1' stroke-dasharray='" + (road.Type.OneWay ? "6 8" : "none") + "'/>");
        }
        Ghost g = Pen.Ghost;
        for (int i = 0; i + 2 < g.Tris.Count; i += 3)
        {
            uint c = g.Colors[g.Tris[i]];
            svg.Append("<polygon points='" + at(g.Points[g.Tris[i]]) + " " + at(g.Points[g.Tris[i + 1]]) + " " + at(g.Points[g.Tris[i + 2]]) + "' fill='rgb(" + ((c >> 16) & 255) + "," + ((c >> 8) & 255) + "," + (c & 255) + ")' fill-opacity='" + ((c >> 24) / 255.0).ToString("0.##", CultureInfo.InvariantCulture) + "'/>");
        }
        svg.Append("</svg>");
        if (Pen.Note.Length > 0)
        {
            svg.Append("<div style='left:10px;top:" + (h - 36) + "px;height:26px;padding:0 10px;border-radius:9px;display:flex;align-items:center;white-space:nowrap;font-size:12px;color:#e7edf0;background:" + (Pen.NoteBad ? "rgba(163,41,32,.95)" : "rgba(20,26,30,.95)") + "'>" + System.Net.WebUtility.HtmlEncode(Pen.Note) + "</div>");
        }
        return svg.ToString();
    }
}

internal static class PenScenes
{
    private static Vec P(double x, double y)
    {
        return new Vec(x, y);
    }

    private static RoadPen New(string road, params double[] roads)
    {
        var pen = new RoadPen { Map = new EditableMap(), Type = RoadType.ById(road) };
        // Roads already on the map, as fours: from x, y to x, y.
        for (int i = 0; i + 3 < roads.Length; i += 4)
        {
            string no = pen.Map.TryAdd(P(roads[i], roads[i + 1]), P(roads[i + 2], roads[i + 3]), RoadType.ById("s2"), true);
            if (no != null) throw new InvalidOperationException(no);
        }
        return pen;
    }

    private static void Click(RoadPen pen, double x, double y, double pixel)
    {
        string refused;
        pen.Hover(P(x, y), pixel);
        pen.Click(P(x, y), out refused);
    }

    public static List<PenScene> All()
    {
        var all = new List<PenScene>();
        Action<string, RoadPen, double, double, double, double, double> add = (name, pen, hx, hy, cx, cy, zoom) =>
        {
            if (!double.IsNaN(hx)) pen.Hover(P(hx, hy), 1 / zoom);
            else pen.Away(1 / zoom);
            all.Add(new PenScene { Name = name, Pen = pen, Center = P(cx, cy), Zoom = zoom });
        };
        const double z = 1.6, px = 1 / z;

        RoadPen pen = New("s2", -150, -60, 150, -60);
        Click(pen, -60, -60, px);
        add("Straight, from a road: pulled square to it, with the line it follows", pen, -55, 62, 0, 0, z);

        pen = New("s2");
        pen.Shape = RoadPen.Curve;
        Click(pen, -90, -50, px);
        add("Curve, step 2: pointing the road", pen, -30, -40, 0, 0, z);

        pen = New("s2");
        pen.Shape = RoadPen.Curve;
        Click(pen, -90, -50, px);
        Click(pen, -30, -40, px);
        add("Curve, step 3: sweeping it round", pen, 70, 50, 0, 0, z);

        pen = New("a2", -120, -70, -20, -70, 110, -80, 110, 80);
        pen.Shape = RoadPen.Curve;
        Click(pen, -20, -70, px);
        add("Curve carrying on from a road end and catching on another road", pen, 108, -25, 0, 0, z);

        pen = New("h2");
        pen.Shape = RoadPen.Curve;
        Click(pen, -100, -40, px);
        Click(pen, -60, -40, px);
        Click(pen, 0, -10, px);
        add("One-way highway, second piece flowing from the first", pen, 90, 60, 0, 10, z);

        pen = New("h2");
        pen.Shape = RoadPen.Curve;
        Click(pen, -80, -40, px);
        Click(pen, -40, -40, px);
        add("Too tight for a highway: red, and it says why", pen, -30, 10, -20, 0, z);

        pen = New("s2", -260, 0, 260, 0, 0, -220, 0, 220);
        pen.Shape = RoadPen.Roundabout;
        Click(pen, 0, 0, px);
        add("Roundabout being sized", pen, 28, 14, 0, 0, z);

        pen = New("s2", -100, -40, 0, -40, 0, -40, 90, 40, 0, -40, -10, 70);
        pen.Tool = RoadPen.Move;
        add("Move tool: every point that can be dragged, pointer over one", pen, 2, -38, 0, 0, z);

        pen = New("s2", -140, -40, 0, -40, 0, -40, 130, 30);
        pen.Tool = RoadPen.Move;
        pen.Hover(P(0, -40), px);
        pen.Press(P(0, -40));
        pen.Drag(P(10, 30));
        add("Move tool: dragging a point, its roads follow", pen, 10, 30, 0, 0, z);

        pen = New("s2", -140, -20, 140, -20);
        pen.Tool = RoadPen.Move;
        pen.Hover(P(0, -20), px);
        pen.Press(P(0, -20));
        pen.Drag(P(10, 45));
        add("Move tool: bending a road by its middle", pen, 10, 45, 0, 0, z);

        pen = New("a4", -150, 0, 150, 0, 0, 0, 0, 90);
        pen.Tool = RoadPen.Replace;
        add("Replace: the road as it would become", pen, -70, 4, 0, 0, z);

        pen = New("s2", -150, 0, 150, 0, 0, 0, 0, 90);
        pen.Tool = RoadPen.Bulldoze;
        add("Bulldoze: the road that would go", pen, 3, 50, 0, 0, z);

        pen = New("a2", -150, 30, 150, 30, -150, -40, 150, -40);
        Click(pen, -60, -110, px);
        add("Crossing two roads: a ring where each intersection will be", pen, 20, 100, 0, 0, z);

        pen = New("s2", -130, -70, 110, -70, 110, -70, 110, 70, 110, 70, -130, 70, -130, 70, -130, -70);
        Click(pen, -40, -70, px);
        add("A block laid with straight streets: tight corners, and a street drawn across it", pen, -40, 70, 0, 0, z);

        pen = New("a4", -130, -70, 110, -70, 110, -70, 110, 70, 110, 70, -130, 70, -130, 70, -130, -70);
        Click(pen, 20, -120, px);
        add("An avenue across the corner of a block, at a slant: an intersection on each side", pen, 160, 20, 0, 0, z);

        pen = New("s2", -130, -70, 110, -70, 110, -70, 110, 70, 110, 70, -130, 70, -130, 70, -130, -70);
        Click(pen, 0, 10, px);
        add("Pointed too close beside a corner for a junction of its own: it joins at the corner", pen, 93.5, -70, 0, 0, z);

        pen = New("a4", -150, 20, 150, 20);
        pen.Tool = RoadPen.Lane;
        add("Lane tool: the lane under the pointer, lit for its whole length", pen, 30, 14, 0, 0, 2.2);

        pen = New("a4", -150, 20, 150, 20);
        pen.Tool = RoadPen.Lane;
        pen.Press(P(30, 12));
        string no;
        pen.Release(out no);
        add("Lane tool: a lane picked with a click (the card goes beside it)", pen, 60, -40, 0, 0, 2.2);

        pen = New("s2", -150, 20, 150, 20);
        pen.Tool = RoadPen.Lane;
        pen.Hover(P(-90, 15), px);
        pen.Press(P(-90, 15));
        pen.Drag(P(70, 14));
        add("Lane tool: dragging along a road adds a lane for that part", pen, 70, 14, 0, 0, z);

        // The same curve from far out, where a meter is a fraction of a pixel: the outlines and pins keep their size on screen.
        pen = New("s2", -500, -200, 500, -200);
        pen.Shape = RoadPen.Curve;
        Click(pen, -300, -200, 2.5);
        Click(pen, -300, -100, 2.5);
        add("Zoomed far out: lines and pins stay visible", pen, 100, 200, 0, 0, 0.4);
        return all;
    }
}
