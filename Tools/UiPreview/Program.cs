using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using Traffic.Ui;

/// <summary>Draws the toolbar as a web page: boxes for the panels, images for the icons.</summary>
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
        const double w = 1280, h = 330;
        var s = new HtmlSurface();
        // Each row is one state of the toolbar, with the pointer resting on something.
        var states = new (string Name, Action<Dock> Set, DockKind Point, int Index)[]
        {
            ("Building, pointing at the two-lane road", d => { d.Building = true; d.Road = 1; d.Limit = 1; d.CanUndo = true; d.Hint = "Click where the road starts."; }, DockKind.Road, 1),
            ("Building a curve, pointing at 65 mph", d => { d.Building = true; d.Shape = 1; d.Road = 2; d.Limit = 3; d.CanUndo = true; d.CanRedo = true; d.Hint = "Click the corner the road should bend round."; }, DockKind.Limit, 3),
            ("Bulldozing", d => { d.Building = true; d.Tool = 1; d.Hint = "Click a road to remove it."; }, DockKind.Tool, 1),
            ("Driving on your own map", d => { d.Rate = 1; }, DockKind.Build, 0),
            ("Driving, paused, on a built-in map", d => { d.OwnMap = false; d.Paused = true; d.Rate = 2; }, DockKind.Pause, 0),
        };
        for (int i = 0; i < states.Length; i++)
        {
            var dock = new Dock { OwnMap = true };
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
        // Every icon, large, to check the drawing.
        s.OffsetY = states.Length * h;
        s.Html.Append("<div style='left:0;top:" + s.OffsetY + "px;width:" + w + "px;height:130px;background:#141a1e'></div>\n");
        for (int i = 0; i < Icons.Names.Length; i++)
        {
            s.Icon(Icons.Names[i], 20 + i * 104, 16, 84, 0xFFE7EDF0);
            s.Text(Icons.Names[i], 20 + i * 104, 104, 84, 16, 11, false, 1, 0xFF8D9AA3);
        }
        string page = "<!doctype html><meta charset='utf-8'><style>body{margin:0;font-family:Arial,'Liberation Sans',sans-serif}div,img{position:absolute;box-sizing:border-box}</style>\n"
            + "<div style='position:relative;width:" + w + "px;height:" + (states.Length * h + 130) + "px'>\n" + s.Html + "</div>";
        File.WriteAllText(args.Length > 0 ? args[0] : "toolbar.html", page);
    }
}
