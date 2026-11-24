namespace Traffic.Ui
{
    /// <summary>The icons on the toolbar, each drawn in code as a white shape that can be tinted.</summary>
    public static class Icons
    {
        public static readonly string[] Names = { "road", "bulldoze", "trash", "undo", "redo", "play", "pause", "step", "cone", "straight", "curve", "roundabout", "arrow", "star", "replace", "angle", "grid", "move" };

        /// <summary>Draw an icon into a square of the given size. Returns coverage per pixel, rows from the top.</summary>
        public static float[] Draw(string name, int size)
        {
            var p = new Painter(size);
            switch (name)
            {
                case "road": // a stretch of road seen from above
                    p.Line(28, 12, 28, 88, 9);
                    p.Line(72, 12, 72, 88, 9);
                    p.Line(50, 14, 50, 28, 7);
                    p.Line(50, 43, 50, 57, 7);
                    p.Line(50, 72, 50, 86, 7);
                    break;
                case "bulldoze": // a bulldozer from the side, blade to the right
                    p.Box(10, 64, 58, 22, 11);
                    p.Box(16, 44, 46, 24, 5);
                    p.Box(24, 20, 26, 28, 5);
                    p.Line(62, 62, 82, 66, 7);
                    p.Curve(84, 40, 76, 62, 90, 86, 9);
                    p.Erase = true;
                    p.Box(30, 26, 14, 13, 2);
                    p.Dot(23, 75, 9);
                    p.Dot(39, 75, 9);
                    p.Dot(55, 75, 9);
                    break;
                case "trash":
                    p.Line(22, 27, 78, 27, 9);
                    p.Line(42, 15, 58, 15, 8);
                    p.Solid(29, 36, 71, 36, 66, 88, 34, 88);
                    p.Erase = true;
                    p.Line(43, 46, 44, 78, 6);
                    p.Line(57, 46, 56, 78, 6);
                    break;
                case "undo":
                case "redo":
                    {
                        // A line running back from an arrowhead, round a bend and underneath.
                        double m = name == "undo" ? 1 : -1, c = 50;
                        p.Line(c - 20 * m, 36, c + 12 * m, 36, 9);
                        if (m > 0) p.Arc(c + 12, 57, 21, -90, 90, 9);
                        else p.Arc(c - 12, 57, 21, 270, 90, 9);
                        p.Line(c + 12 * m, 78, c - 12 * m, 78, 9);
                        p.Solid(c - 40 * m, 36, c - 16 * m, 18, c - 16 * m, 54);
                    }
                    break;
                case "play":
                    p.Solid(30, 18, 82, 50, 30, 82);
                    p.Path(8, 30, 18, 82, 50, 30, 82, 30, 18);
                    break;
                case "pause":
                    p.Box(24, 18, 18, 64, 5);
                    p.Box(58, 18, 18, 64, 5);
                    break;
                case "step": // play one moment at a time
                    p.Solid(20, 20, 62, 50, 20, 80);
                    p.Path(8, 20, 20, 62, 50, 20, 80, 20, 20);
                    p.Box(68, 16, 14, 68, 5);
                    break;
                case "cone": // a traffic cone, for building
                    p.Solid(43, 10, 57, 10, 76, 76, 24, 76);
                    p.Box(10, 76, 80, 13, 5);
                    p.Erase = true;
                    p.Line(20, 34, 80, 34, 8);
                    p.Line(20, 56, 80, 56, 8);
                    break;
                case "straight":
                    p.Line(24, 76, 76, 24, 9);
                    p.Dot(24, 76, 22);
                    p.Dot(76, 24, 22);
                    break;
                case "curve":
                    p.Curve(22, 78, 26, 26, 78, 22, 9);
                    p.Dot(22, 78, 22);
                    p.Dot(78, 22, 22);
                    break;
                case "roundabout": // a ring, with a gap and an arrowhead to show the way round
                    p.Arc(50, 50, 30, 20, 320, 10);
                    p.Solid(90, 30, 62, 36, 84, 58);
                    p.Dot(50, 50, 12);
                    break;
                case "star":
                    {
                        // Five points round a five-sided middle.
                        var tip = new double[10];
                        var dent = new double[10];
                        for (int k = 0; k < 5; k++)
                        {
                            double a = (-90 + 72 * k) * System.Math.PI / 180, b = (-54 + 72 * k) * System.Math.PI / 180;
                            tip[k * 2] = 50 + 46 * System.Math.Cos(a);
                            tip[k * 2 + 1] = 53 + 46 * System.Math.Sin(a);
                            dent[k * 2] = 50 + 19 * System.Math.Cos(b);
                            dent[k * 2 + 1] = 53 + 19 * System.Math.Sin(b);
                        }
                        p.Solid(dent);
                        for (int k = 0; k < 5; k++)
                        {
                            int before = (k + 4) % 5;
                            p.Solid(tip[k * 2], tip[k * 2 + 1], dent[k * 2], dent[k * 2 + 1], dent[before * 2], dent[before * 2 + 1]);
                        }
                    }
                    break;
                case "replace": // two arrows, one each way: swap one thing for another
                    p.Line(16, 34, 66, 34, 9);
                    p.Solid(62, 16, 88, 34, 62, 52);
                    p.Line(34, 68, 84, 68, 9);
                    p.Solid(38, 50, 12, 68, 38, 86);
                    break;
                case "angle": // two lines from a corner, with an arc between them
                    p.Line(18, 80, 84, 80, 9);
                    p.Line(18, 80, 64, 24, 9);
                    p.Arc(18, 80, 34, -50, 0, 6);
                    break;
                case "grid":
                    p.Line(36, 14, 36, 86, 8);
                    p.Line(64, 14, 64, 86, 8);
                    p.Line(14, 36, 86, 36, 8);
                    p.Line(14, 64, 86, 64, 8);
                    break;
                case "move": // arrows pointing four ways from the middle
                    p.Line(50, 24, 50, 76, 8);
                    p.Line(24, 50, 76, 50, 8);
                    p.Solid(50, 6, 34, 26, 66, 26);
                    p.Solid(50, 94, 34, 74, 66, 74);
                    p.Solid(6, 50, 26, 34, 26, 66);
                    p.Solid(94, 50, 74, 34, 74, 66);
                    break;
                case "arrow": // pointing right
                    p.Line(12, 50, 64, 50, 16);
                    p.Solid(56, 22, 92, 50, 56, 78);
                    break;
            }
            return p.Ink;
        }
    }
}
