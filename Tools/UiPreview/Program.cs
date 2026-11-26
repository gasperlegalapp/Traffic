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
        // The same list the game makes from its road types (see LabApp.RoadTiles), with shorter words.
        var roads = new List<DockRoad>();
        foreach (RoadType t in RoadType.Toolbar)
        {
            roads.Add(new DockRoad
            {
                Group = RoadType.GroupNames[t.Group], Title = t.Name, Forward = t.Forward, Backward = t.Backward, Median = t.Median > 0, Turn = t.CenterLane, Bays = t.Bays, Class = t.Group, Chooses = !t.OneWay && t.Group != RoadType.Highway,
                Body = t.Mph + " mph.  $" + t.CostPerMeter.ToString(CultureInfo.InvariantCulture) + " a meter.  " + Math.Round(t.Width * 3.28084) + " ft wide.\n"
                    + (t.OneWay ? "Traffic runs the way you draw it." : t.CenterLane ? "A turning lane runs down the middle." : t.Median > 0 ? "A grass median divides the two directions." : "A double yellow line divides the two directions."),
            });
        }
        var s = new HtmlSurface();
        // Each row is one state of the toolbar, with the pointer resting on something.
        var states = new (string Name, Action<Dock> Set, DockKind Point, int Index)[]
        {
            ("Building, pointing at the wide avenue", d => { d.Building = true; d.Road = 0; d.CanUndo = true; d.Hint = "Click where the road starts."; }, DockKind.Road, 3),
            ("Choosing what lies between the two directions of an avenue: the list open, trees chosen, pointing at grass", d => { d.Building = true; d.Road = 7; d.Middle[1] = 3; d.Middle[0] = 1; d.MiddleOpen = 1; d.CanUndo = true; d.Hint = "Click where the road starts."; }, DockKind.MiddleChoice, 2),
            ("Building on the block grid, pointing at the 250 ft grid", d => { d.Building = true; d.Road = 1; d.Grid = 250; d.SnapAngles = true; d.CanUndo = true; d.Hint = "Click where the road ends. Right-click to stop."; }, DockKind.Grid, 4),
            ("Building a curve on a bridge, pointing at bridge level 2", d => { d.Building = true; d.Shape = 1; d.Road = 5; d.Height = 1; d.CanUndo = true; d.CanRedo = true; d.Hint = "Sweep the road round to where it should end, and click. Right-click to point it again."; }, DockKind.Shape, 1),
            ("Moving, pointing at the move tool", d => { d.Building = true; d.Tool = 3; d.CanUndo = true; d.Hint = "Drag a point to move it. Drag the middle of a road to bend it, or pull it back straight."; }, DockKind.Tool, 3),
            ("Bulldozing", d => { d.Building = true; d.Tool = 1; d.Hint = "Click a road to remove it."; }, DockKind.Tool, 1),
            ("The signs tool, and its card: left to the game, with a stop sign on this street, and on a road with no intersection ahead", d => { d.Building = true; d.Tool = 5; d.CanUndo = true; d.Hint = "Choose what runs the intersection ahead, or post a speed limit, with the card. Click another lane to pick it."; }, DockKind.None, 0),
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
            var card = new LaneCard { Added = i == 1, Turns = i >= 2, CanMiddle = i == 3, CanReverse = i != 1, Median = false };
            card.TurnThere[0] = card.TurnThere[1] = card.TurnThere[2] = true;
            card.TurnMay[0] = true;
            s.OffsetY = (states.Length - 1) * h;
            card.Layout(170 + i * 300, i == 2 ? 215 : i == 3 ? 265 : 150, w, h);
            card.Draw(s, 80 + i * 430, i >= 2 ? 130 : 80);
        }
        // The signs tool's card: at an intersection left to the game, at one with a stop sign on this street, and on a road with no intersection ahead.
        for (int i = 0; i < 3; i++)
        {
            var card = new SignCard { Intersection = i < 2, Now = i == 0 ? Control.Light : Control.Stop, Automatic = i == 0, Mph = i == 1 ? 35 : i == 2 ? 55 : 40, Posted = i >= 1 };
            s.OffsetY = (states.Length - 3) * h;
            // The two tall ones either side of the toolbar, the short one above it.
            card.Layout(i == 0 ? 174 : i == 1 ? 1098 : 640, i == 2 ? 270 : 372, w, h);
            card.Draw(s, i == 0 ? 100 : i == 1 ? 1020 : 560, i == 0 ? 82 : i == 1 ? 168 : 150);
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
        // The roads as the game will lay them: pavement, junctions and paint from the built network.
        World built = null;
        try { built = Pen.Map.Segments.Count > 0 ? Pen.Map.ToWorld() : null; } catch (Exception) { }
        if (built != null)
        {
            Func<IEnumerable<Vec>, string> line = pts => string.Join(" ", System.Linq.Enumerable.Select(pts, at));
            foreach (Strip strip in built.Surfaces) svg.Append("<polygon fill='#3a3c40' points='" + line(System.Linq.Enumerable.Concat(strip.Left, System.Linq.Enumerable.Reverse(strip.Right))) + "'/>");
            foreach (Node node in built.Nodes) if (node.Box.Count > 2) svg.Append("<polygon fill='#3a3c40' points='" + line(node.Box) + "'/>");
            foreach (Strip strip in built.Medians) svg.Append("<polygon fill='#7db862' points='" + line(System.Linq.Enumerable.Concat(strip.Left, System.Linq.Enumerable.Reverse(strip.Right))) + "'/>");
            foreach (Marking m in built.Markings)
            {
                svg.Append("<polyline points='" + line(m.Pts) + "' fill='none' stroke='" + (m.Yellow ? "#efbd2e" : "#f1f1ec") + "' stroke-width='" + N(Math.Max(0.9, m.Width * 1.4 * Zoom)) + "'"
                    + (m.DashOn > 0 ? " stroke-dasharray='" + N(m.DashOn * Zoom) + " " + N(m.DashOff * Zoom) + "'" : "") + "/>");
            }
        }
        else foreach (EditSegment road in Pen.Map.Segments)
        {
            var pts = new StringBuilder();
            foreach (Vec p in Pen.Map.Trace(road)) pts.Append(at(p) + " ");
            svg.Append("<polyline points='" + pts + "' fill='none' stroke='#3a3c40' stroke-width='" + N(road.Type.Width * Zoom) + "'/>");
            svg.Append("<polyline points='" + pts + "' fill='none' stroke='" + (road.Type.OneWay ? "#f1f1ec" : "#efbd2e") + "' stroke-width='1' stroke-dasharray='" + (road.Type.OneWay ? "6 8" : "none") + "'/>");
        }
        Ghost g = Pen.Ghost;
        // Each triangle in one flat color: where its corners differ (a glow fading out) it takes their average, which is close enough here.
        Action<List<Vec>, List<uint>, List<int>> triangles = (points, colors, tris) =>
        {
            for (int i = 0; i + 2 < tris.Count; i += 3)
            {
                uint c = colors[tris[i]];
                double alpha = ((colors[tris[i]] >> 24) + (colors[tris[i + 1]] >> 24) + (colors[tris[i + 2]] >> 24)) / 3.0 / 255;
                svg.Append("<polygon points='" + at(points[tris[i]]) + " " + at(points[tris[i + 1]]) + " " + at(points[tris[i + 2]]) + "' fill='rgb(" + ((c >> 16) & 255) + "," + ((c >> 8) & 255) + "," + (c & 255) + ")' fill-opacity='" + alpha.ToString("0.##", CultureInfo.InvariantCulture) + "'/>");
            }
        };
        triangles(g.Points, g.Colors, g.Tris);
        // The arrows that run along the road, caught at one moment.
        g.Run(0.7);
        triangles(g.RunPoints, g.RunColors, g.RunTris);
        // The markers standing at the ends: in the game they are posts with a ball of light on top, seen here from straight above the ground they stand on.
        Func<Vec, double[]> spot = p => new[] { w / 2 + (p.X - Center.X) * Zoom, h / 2 - (p.Y - Center.Y) * Zoom };
        foreach (Ghost.Post post in g.Posts)
        {
            double[] at0 = spot(post.At);
            double tall = (post.Held ? 1.25 : 1) * 34, round = (post.Held ? 1.25 : 1) * 5.5;
            string ink = post.Good ? "#8df7e2" : "#ff8e7e";
            svg.Append("<line x1='" + N(at0[0]) + "' y1='" + N(at0[1]) + "' x2='" + N(at0[0]) + "' y2='" + N(at0[1] - tall) + "' stroke='#f4f8f8' stroke-width='1.4'/>");
            svg.Append("<circle cx='" + N(at0[0]) + "' cy='" + N(at0[1] - tall) + "' r='" + N(round * 2.4) + "' fill='" + ink + "' fill-opacity='.28'/>");
            svg.Append("<circle cx='" + N(at0[0]) + "' cy='" + N(at0[1] - tall) + "' r='" + N(round) + "' fill='" + ink + "'/>");
        }
        svg.Append("</svg>");
        foreach (Ghost.Label say in g.Labels)
        {
            double[] at0 = spot(say.At);
            svg.Append("<div style='left:" + N(at0[0]) + "px;top:" + N(at0[1] - 44) + "px;transform:translateX(-50%);height:22px;padding:0 9px;border-radius:11px;border:1.2px solid rgba(94,224,192,.8);display:flex;align-items:center;white-space:nowrap;font-size:11px;font-weight:bold;color:#5ee0c0;background:rgba(20,26,30,.92)'>" + System.Net.WebUtility.HtmlEncode(say.Text) + "</div>");
        }
        if (Pen.Price.Length > 0)
        {
            // The price tag: on the end in hand when there is one, otherwise in the corner (in the game, beside the pointer).
            string where = "left:10px;top:" + (h - 58) + "px;";
            if (Pen.TagPlaced)
            {
                double[] at0 = spot(Pen.TagAt);
                where = "left:" + N(Math.Max(60, Math.Min(w - 60, at0[0]))) + "px;top:" + N(Math.Max(4, at0[1] - 34 * 1.25 - 5.5 * 1.25 * 1.7 - 54)) + "px;transform:translateX(-50%);";
            }
            svg.Append("<div style='" + where + "height:48px;padding:5px 13px;border-radius:12px;white-space:nowrap;color:#10161a;background:#fff;box-shadow:0 2px 0 rgba(0,0,0,.25)'>"
                + "<div style='position:static;font-size:18px;font-weight:bold;line-height:22px'>" + System.Net.WebUtility.HtmlEncode(Pen.Price) + "</div>"
                + "<div style='position:static;font-size:11px;line-height:16px;color:#4b5861'>" + System.Net.WebUtility.HtmlEncode(Pen.Detail) + "</div></div>");
        }
        else if (Pen.Note.Length > 0)
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

        // A grid of blocks (250 ft): streets laid along its lines, and the next one snapping to it.
        const double g = 250 / 3.28084;
        pen = New("s2", -2 * g, -g, 2 * g, -g, -2 * g, g, 2 * g, g, -g, -g, -g, g);
        pen.Grid = 250;
        Click(pen, g + 6, -g - 5, px);
        add("Grid on: the lines show on the ground, and the road being drawn lands where they cross", pen, g - 9, g + 12, 0, 0, z);

        pen = New("s1");
        pen.Grid = 25;
        Click(pen, -72, -31, px);
        add("The fine grid, for close work: a one-way street between two of its crossings", pen, 58, -27, 0, 0, z);

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

        pen = New("s2", -150, 20, 150, 20);
        pen.Map.TryAddLane(P(-90, 15), P(20, 15), true);
        pen.Tool = RoadPen.Move;
        pen.Hover(P(20, 14.5), px);
        pen.Press(P(20, 14.5));
        pen.Drag(P(95, 12));
        add("Move tool: sliding the end of an added lane along its road", pen, 95, 12, 0, 0, z);

        // Building off lanes. Roads already there are passed as: kind, from x, y, to x, y.
        Func<string, string, double[], RoadPen> lanes = (tool, kind, road) =>
        {
            var p = new RoadPen { Map = new EditableMap(), Type = RoadType.ById(tool) };
            for (int i = 0; i + 3 < road.Length; i += 4) p.Map.TryAdd(P(road[i], road[i + 1]), P(road[i + 2], road[i + 3]), RoadType.ById(kind), true);
            return p;
        };
        pen = lanes("a1", "a4", new double[] { -200, 0, 0, 0 });
        add("A road end: an arrow on each lane that leaves, and a white ring beyond the end for the road as a whole", pen, 60, 60, 0, 0, 2.6);

        // Each picture is drawn later, from the pen as it then stands, so each has a pen of its own.
        Func<int, RoadPen> offLane = steps =>
        {
            RoadPen p = lanes("a1", "a4", new double[] { -200, 0, 0, 0 });
            EditableMap.LaneEnd outer = p.Map.LaneEnds().FindAll(e => e.Leaving && e.Node.X > -100)[1];
            if (steps >= 1) Click(p, outer.At.X, outer.At.Y, 1 / 2.6);
            if (steps >= 2)
            {
                Click(p, 110, -40, 1 / 2.6);
                p.Cancel();
            }
            if (steps >= 3) Click(p, 120, 45, 1 / 2.6);
            return p;
        };
        add("Started on the outer lane's arrow with a one-lane road: it starts at that lane, not at the middle of the road", offLane(1), 110, -40, 0, 0, 2.6);
        add("Built. The lanes nothing has been built on from keep their arrows", offLane(2), 60, 70, 0, 0, 2.6);
        pen = offLane(3);
        EditableMap.LaneEnd inner = pen.Map.LaneEnds().FindAll(e => !e.Leaving && e.Node.X > -100 && e.Node.X < 50)[0];
        add("A road brought in to the ring on a lane that sets out", pen, inner.At.X, inner.At.Y, 0, 0, 2.6);

        pen = lanes("a1", "a1x2", new double[] { -200, 0, 0, 0 });
        EditableMap.LaneEnd right = pen.Map.LaneEnds().FindAll(e => e.Leaving && e.Node.X > -100)[1];
        Click(pen, right.At.X, right.At.Y, 1 / 2.6);
        Click(pen, 120, -6, 1 / 2.6);
        pen.Cancel();
        EditableMap.LaneEnd left = pen.Map.LaneEnds().Find(e => e.Leaving && e.Node.X > -100 && e.Node.X < 50);
        Click(pen, left.At.X, left.At.Y, 1 / 2.6);
        add("Two lanes one way, each carried on as a road of its own: a fork with no light", pen, 110, 45, 0, 0, 2.6);

        pen = lanes("a1", "a1x2", new double[] { -200, 0, 200, 0 });
        add("Partway along a road, the lane under the pointer shows its arrow", pen, -40, -1.6, 0, 0, 2.6);

        pen = lanes("a1", "a1x2", new double[] { -200, 0, 200, 0 });
        Click(pen, -60, -1.6, 1 / 2.6);
        add("Started on that lane and drawn ahead: it peels off along the lane, as an exit", pen, 50, -50, 0, 0, 2.6);

        pen = lanes("a1", "a1x2", new double[] { -200, 0, 200, 0 });
        Click(pen, -70, -60, 1 / 2.6);
        add("Brought in to a lane: a ring on the lane it will join, and it sweeps in alongside", pen, 40, -1.6, 0, 0, 2.6);

        // The same curve from far out, where a meter is a fraction of a pixel: the outlines and pins keep their size on screen.
        pen = New("s2", -500, -200, 500, -200);
        pen.Shape = RoadPen.Curve;
        Click(pen, -300, -200, 2.5);
        Click(pen, -300, -100, 2.5);
        add("Zoomed far out: lines and pins stay visible", pen, 100, 200, 0, 0, 0.4);
        return all;
    }
}
