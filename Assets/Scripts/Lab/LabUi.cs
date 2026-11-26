using System.Collections.Generic;
using Traffic.Sim;
using Traffic.Ui;
using UnityEngine;

namespace Traffic.Lab
{
    /// <summary>
    /// On-screen controls for the driver lab, drawn with Unity's immediate-mode GUI.
    /// Mouse and keyboard are read from GUI events, so this works with either of
    /// Unity's input settings.
    /// </summary>
    public sealed partial class LabApp
    {
        private static readonly string[] SliderNames = { "Reaction time", "Speed", "Following gap", "Aggression", "Politeness", "Obeys lights and signs", "Weaving between lanes", "Swerving (drunk)", "Looking away (phone)", "Camping in the left lane" };
        private static readonly string[] StateNames = { "CRUISE", "FOLLOW", "BRAKE", "STOPPING", "WAIT", "YIELD", "LETTING IN", "MERGE", "LANE CHANGE", "STARTLED", "CRASH", "SCARED", "WARY", "LOOKING AWAY", "RUBBERNECKING" };
        private static readonly float[] Speeds = { 0.25f, 1, 2, 4 };

        private static readonly Color Accent = MeshKit.Hex(0x5ee0c0);
        private static readonly Color Dim = MeshKit.Hex(0x8d9aa3);
        private static readonly Color Warn = MeshKit.Hex(0xffc93c);
        private static readonly Color Info = MeshKit.Hex(0x6cb8ff);
        private static readonly Color Bad = MeshKit.Hex(0xff5d5d);

        private readonly Dock dock = new Dock();
        private readonly LaneCard laneCard = new LaneCard();
        private readonly SignCard signCard = new SignCard();
        private GuiSurface surface;
        private Texture2D white;
        private GUIStyle panel, heading, label, value, big, sub, tagStyle, stateStyle, button, buttonOn, toggle, hint, tagPrice, tagDetail, tagJoin;
        private Vector2 labScroll;
        /// <summary>The right button is down, to drag the view; the middle button is down, to turn it.</summary>
        private bool panning, turning;
        /// <summary>The move tool has hold of something, so the drag in progress moves that.</summary>
        private bool grabbing;
        /// <summary>The pointer has gone far enough from where the right button went down to count as a drag and not a click.</summary>
        private bool panMoved;
        private Vector2 lastMouse, pressPixel;
        private const string ViewHelp = "Right-drag moves the view.  Wheel zooms.  Middle-drag, or Q and E, turns it.  N puts north back up.";

        private void BuildStyles()
        {
            white = new Texture2D(1, 1);
            white.SetPixel(0, 0, Color.white);
            white.Apply();
            // A small rounded square that stretches to any size, for buttons.
            Texture2D Pill(Color c)
            {
                const int size = 24;
                var paint = new Painter(size);
                paint.Box(0, 0, 100, 100, 30);
                var t = new Texture2D(size, size, TextureFormat.RGBA32, false);
                var pixels = new Color32[size * size];
                for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color(c.r, c.g, c.b, c.a * paint.Ink[i]);
                t.SetPixels32(pixels);
                t.wrapMode = TextureWrapMode.Clamp;
                t.Apply();
                return t;
            }
            surface = new GuiSurface();
            // The panels draw their own rounded backs (see Card), so the style only sets the padding.
            panel = new GUIStyle { padding = new RectOffset(14, 14, 12, 12) };
            label = new GUIStyle(GUI.skin.label) { fontSize = 13 };
            label.normal.textColor = MeshKit.Hex(0xe7edf0);
            heading = new GUIStyle(label) { fontSize = 10, fontStyle = FontStyle.Bold };
            heading.normal.textColor = Dim;
            value = new GUIStyle(label) { alignment = TextAnchor.MiddleRight };
            value.normal.textColor = Accent;
            big = new GUIStyle(label) { fontSize = 15, fontStyle = FontStyle.Bold };
            sub = new GUIStyle(label) { fontSize = 11 };
            sub.normal.textColor = Accent;
            tagStyle = new GUIStyle(label) { fontSize = 9, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, padding = new RectOffset(4, 4, 1, 1) };
            stateStyle = new GUIStyle(tagStyle) { fontSize = 11 };
            button = new GUIStyle(GUI.skin.button) { fontSize = 13, fontStyle = FontStyle.Bold, border = new RectOffset(8, 8, 8, 8), padding = new RectOffset(8, 8, 5, 6) };
            Texture2D plain = Pill(new Color(1, 1, 1, 0.1f)), lit = Pill(new Color(1, 1, 1, 0.2f)), pressed = Pill(new Color(1, 1, 1, 0.3f));
            button.normal.background = button.focused.background = plain;
            button.hover.background = lit;
            button.active.background = pressed;
            button.normal.textColor = button.hover.textColor = button.active.textColor = button.focused.textColor = MeshKit.Hex(0xe7edf0);
            toggle = new GUIStyle(GUI.skin.toggle) { fontSize = 13 };
            toggle.normal.textColor = toggle.onNormal.textColor = MeshKit.Hex(0xe7edf0);
            buttonOn = new GUIStyle(button);
            Texture2D chosen = Pill(new Color(Accent.r, Accent.g, Accent.b, 0.22f));
            buttonOn.normal.background = buttonOn.hover.background = buttonOn.active.background = buttonOn.focused.background = chosen;
            buttonOn.normal.textColor = buttonOn.hover.textColor = buttonOn.active.textColor = buttonOn.focused.textColor = Accent;
            hint = new GUIStyle(label) { wordWrap = true, fontSize = 12 };
            hint.normal.textColor = Dim;
            // The price tag on the road being drawn is white, so its words are dark.
            tagPrice = new GUIStyle(label) { fontSize = 18, fontStyle = FontStyle.Bold };
            tagPrice.normal.textColor = MeshKit.Hex(0x10161a);
            tagDetail = new GUIStyle(label) { fontSize = 11 };
            tagDetail.normal.textColor = MeshKit.Hex(0x4b5861);
            tagJoin = new GUIStyle(tagDetail) { fontStyle = FontStyle.Bold };
            tagJoin.normal.textColor = Accent;
        }

        private void OnGUI()
        {
            if (Sim == null) return;
            if (panel == null) BuildStyles();
            Event e = Event.current;
            float scale = Mathf.Max(1, Screen.height / 950f);
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1));
            float w = Screen.width / scale;
            float h = Screen.height / scale;

            // First of all, under everything else on screen: the corners of the picture a little darker than its middle.
            if (e.type == EventType.Repaint)
            {
                if (vignette == null) vignette = Vignette();
                GUI.color = new Color(1, 1, 1, Mathf.Lerp(0.3f, 0.5f, dusk));
                GUI.DrawTexture(new Rect(0, 0, w, h), vignette);
                GUI.color = Color.white;
            }

            if (showLabels && !building && e.type == EventType.Repaint) DrawTags(scale);

            // A level shows its budget and its routes as well, so its bar is wider.
            var top = new Rect(12, 12, level != null ? 660 : 430, 54);
            var lab = new Rect(12, 76, 300, Mathf.Min(building ? (level != null ? 560 : 400) : 900, h - 100));
            float inspectorHeight = Mathf.Min(612, h - 24);
            var inspector = new Rect(w - 300, h - 12 - inspectorHeight, 288, inspectorHeight);
            Car car = Sim.CarById(selected);

            DrawTop(top);
            DrawLab(lab);
            if (car != null) DrawInspector(inspector, car);

            // The toolbar along the bottom.
            dock.OwnMap = Buildable;
            dock.Building = building;
            dock.Paused = paused;
            dock.Night = night;
            dock.CanUndo = undo.Count > 0;
            dock.CanRedo = redo.Count > 0;
            dock.Tool = pen.Tool;
            dock.SnapAngles = pen.SnapAngles;
            dock.Grid = pen.Grid;
            dock.Shape = pen.Shape;
            if (dock.Roads.Length == 0) dock.Roads = RoadTiles();
            dock.Road = roadChoice;
            for (int i = 0; i < middleChoice.Length; i++) dock.Middle[i] = middleChoice[i];
            dock.Height = levelChoice;
            if (dock.HeightNotes[1] == null) dock.HeightNotes = HeightNotes();
            dock.Rate = System.Array.IndexOf(Speeds, speed);
            dock.Hint = building ? pen.Hint : "";
            dock.Layout(w, h);
            Vector2 mouse = e.mousePosition;
            if (e.type == EventType.Repaint) dock.Draw(surface, mouse.x, mouse.y);
            bool overDock = dock.Contains(mouse.x, mouse.y);
            if (e.type == EventType.MouseDown && e.button == 0 && overDock)
            {
                DockItem hit = dock.ItemAt(mouse.x, mouse.y);
                // The list of middles shuts on any click but the one that opens it.
                if (hit.Kind != DockKind.Middle && hit.Kind != DockKind.MiddleChoice) dock.MiddleOpen = -1;
                if (hit.Kind != DockKind.None) DockClick(hit);
                e.Use();
            }
            else if (e.type == EventType.MouseDown) dock.MiddleOpen = -1;

            // Beside a lane picked with the lane tool: what can be done with it.
            bool cardShown = false;
            if (building && pen.Tool == RoadPen.Lane && pen.Picked)
            {
                Vector3 spot = cam.WorldToScreenPoint(MeshKit.ToWorld(pen.PickedAt.X, pen.PickedAt.Y));
                if (spot.z > 0)
                {
                    cardShown = true;
                    laneCard.Added = pen.PickedIsAdded;
                    laneCard.Middle = pen.PickedIsMiddle;
                    laneCard.CanMiddle = pen.CanAddMiddle;
                    laneCard.Median = pen.PickedHasMedian;
                    laneCard.CanReverse = pen.CanReverse;
                    bool[] there, may;
                    laneCard.Turns = pen.PickedTurns(out there, out may);
                    for (int i = 0; i < 3; i++)
                    {
                        laneCard.TurnThere[i] = there[i];
                        laneCard.TurnMay[i] = may[i];
                    }
                    laneCard.Layout(spot.x / scale, (Screen.height - spot.y) / scale, w, (float)dock.Top - 8);
                    if (e.type == EventType.Repaint) laneCard.Draw(surface, mouse.x, mouse.y);
                    if (e.type == EventType.MouseDown && e.button == 0 && laneCard.Contains(mouse.x, mouse.y))
                    {
                        LaneAct act = laneCard.ItemAt(mouse.x, mouse.y);
                        if (act != LaneAct.None) LaneAction(act);
                        e.Use();
                    }
                }
            }
            bool overCard = cardShown && laneCard.Contains(mouse.x, mouse.y);
            // Beside a lane picked with the signs tool: what runs the intersection ahead, and the speed limit.
            if (building && pen.Tool == RoadPen.Signs && pen.Picked)
            {
                Vector3 spot = cam.WorldToScreenPoint(MeshKit.ToWorld(pen.PickedAt.X, pen.PickedAt.Y));
                if (spot.z > 0)
                {
                    signCard.Intersection = pen.PickedControl(out signCard.Now, out signCard.Automatic);
                    signCard.Mph = pen.PickedSpeed(out signCard.Posted);
                    signCard.NoRed = pen.PickedNoTurnOnRed;
                    signCard.Layout(spot.x / scale, (Screen.height - spot.y) / scale, w, (float)dock.Top - 8);
                    if (e.type == EventType.Repaint) signCard.Draw(surface, mouse.x, mouse.y);
                    if (e.type == EventType.MouseDown && e.button == 0 && signCard.Contains(mouse.x, mouse.y))
                    {
                        Control which;
                        SignAct act = signCard.ItemAt(mouse.x, mouse.y, out which);
                        if (act != SignAct.None) SignAction(act, which);
                        e.Use();
                    }
                    overCard |= signCard.Contains(mouse.x, mouse.y);
                }
            }

            if (e.type == EventType.Repaint) DrawPlaceNames(scale);
            if (level != null && stars > 0 && !resultShown) DrawResult(w, h);

            // On the ground while a road is being drawn: a few words at each place it will make an intersection.
            if (building && e.type == EventType.Repaint)
            {
                foreach (Ghost.Label say in pen.Ghost.Labels)
                {
                    Vector3 p = cam.WorldToScreenPoint(MeshKit.ToWorld(say.At.X, say.At.Y, 1));
                    if (p.z <= 0) continue;
                    Vector2 size = tagDetail.CalcSize(new GUIContent(say.Text));
                    float reach = (float)(pen.Ghost.AtLeast(8, 14) / MetersPerPixel) / scale;
                    var r = new Rect(p.x / scale - size.x / 2 - 9, (Screen.height - p.y) / scale - reach - 30, size.x + 18, 22);
                    surface.Fill(r.x, r.y, r.width, r.height, 11, 0xEB141A1E);
                    surface.Stroke(r.x, r.y, r.width, r.height, 11, 1.2, 0xCC5EE0C0);
                    GUI.Label(new Rect(r.x + 9, r.y + 3, size.x + 4, 16), say.Text, tagJoin);
                }
            }

            // While a road is being drawn: its price on a tag, riding on the end in hand, with what the
            // money buys under it. Where there is no price, a note beside the pointer: in red, why it cannot be built.
            if (building && !overDock && !overCard && pen.Price.Length > 0)
            {
                Vector2 price = tagPrice.CalcSize(new GUIContent(pen.Price)), detail = tagDetail.CalcSize(new GUIContent(pen.Detail));
                float wide = Mathf.Max(price.x, detail.x) + 26, tall = pen.Detail.Length > 0 ? 48 : 32;
                var r = new Rect(mouse.x + 18, mouse.y + 16, wide, tall);
                if (pen.TagPlaced)
                {
                    // Just above the ball on top of the marker at that end.
                    float above = MarkerHeight(true) + (float)pen.Ghost.AtLeast(1.0, 5.5) * 1.7f;
                    Vector3 p = cam.WorldToScreenPoint(MeshKit.ToWorld(pen.TagAt.X, pen.TagAt.Y, above));
                    if (p.z > 0) r = new Rect(p.x / scale - wide / 2, (Screen.height - p.y) / scale - tall - 6, wide, tall);
                }
                r.x = Mathf.Clamp(r.x, 8, w - wide - 8);
                r.y = Mathf.Clamp(r.y, 8, (float)dock.Top - tall - 8);
                if (e.type == EventType.Repaint)
                {
                    surface.Fill(r.x, r.y + 2, r.width, r.height, 12, 0x40000000);
                    surface.Fill(r.x, r.y, r.width, r.height, 12, 0xFFFFFFFF);
                }
                GUI.Label(new Rect(r.x + 13, r.y + 5, wide - 20, 24), pen.Price, tagPrice);
                if (pen.Detail.Length > 0) GUI.Label(new Rect(r.x + 13, r.y + 27, wide - 20, 16), pen.Detail, tagDetail);
            }
            else if (building && !overDock && !overCard && pen.Note.Length > 0)
            {
                Vector2 size = label.CalcSize(new GUIContent(pen.Note));
                var r = new Rect(Mathf.Max(8, Mathf.Min(mouse.x + 18, w - size.x - 28)), mouse.y + 16, size.x + 20, 26);
                if (!pen.NoteBad) Card(r, 9);
                else if (e.type == EventType.Repaint) surface.Fill(r.x, r.y, r.width, r.height, 9, 0xF2A32920);
                GUI.Label(new Rect(r.x + 10, r.y + 3, size.x + 4, 20), pen.Note, label);
            }

            // How to move the view, in the top right corner, out of the way.
            {
                Vector2 size = hint.CalcSize(new GUIContent(ViewHelp));
                var r = new Rect(w - size.x - 36, 12, size.x + 24, 28);
                Card(r, 14);
                GUI.Label(new Rect(r.x + 12, r.y + 5, size.x + 4, 18), ViewHelp, hint);
            }

            if (Time.unscaledTime < toastUntil)
            {
                Vector2 size = label.CalcSize(new GUIContent(toast));
                var r = new Rect(w / 2 - size.x / 2 - 16, (float)dock.Top - 46, size.x + 32, 36);
                Card(r, 18);
                GUI.Label(new Rect(r.x + 16, r.y + 8, size.x + 4, 20), toast, label);
            }

            bool overUi = overDock || overCard || top.Contains(mouse) || lab.Contains(mouse) || (car != null && inspector.Contains(mouse));
            HandleSceneInput(e, overUi, scale);
        }

        private Texture2D vignette;

        /// <summary>
        /// A picture that is clear in the middle and darkens toward its corners. Laid over the
        /// view it draws the eye to the middle of the screen, as a camera lens does.
        /// </summary>
        private static Texture2D Vignette()
        {
            const int n = 128;
            var pixels = new Color32[n * n];
            for (int y = 0; y < n; y++)
            {
                for (int x = 0; x < n; x++)
                {
                    float dx = (x + 0.5f) / n * 2 - 1, dy = (y + 0.5f) / n * 2 - 1;
                    // 0 in the middle, 1 in the corners; nothing at all until over halfway out.
                    float r = Mathf.Sqrt(dx * dx + dy * dy) / 1.4142f;
                    float dark = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(0.52f, 1.02f, r));
                    pixels[y * n + x] = new Color32(6, 12, 18, (byte)(dark * dark * 255));
                }
            }
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false);
            t.SetPixels32(pixels);
            t.wrapMode = TextureWrapMode.Clamp;
            t.filterMode = FilterMode.Bilinear;
            t.Apply();
            return t;
        }

        /// <summary>Something on the toolbar was clicked.</summary>
        private void DockClick(DockItem item)
        {
            switch (item.Kind)
            {
                case DockKind.Tool:
                    pen.Tool = item.Index;
                    StopDrawing();
                    break;
                case DockKind.Undo:
                    Undo(undo, redo);
                    break;
                case DockKind.Redo:
                    Undo(redo, undo);
                    break;
                case DockKind.Clear:
                    if (drawing.Segments.Count == 0) break;
                    string before = drawing.Serialize();
                    drawing.Clear();
                    StopDrawing();
                    DrawingChanged(before);
                    break;
                case DockKind.Drive:
                    later = () => SetBuilding(false);
                    break;
                case DockKind.Build:
                    later = () => SetBuilding(true);
                    break;
                case DockKind.Shape:
                    pen.Shape = item.Index;
                    break;
                case DockKind.Road:
                    roadChoice = item.Index;
                    pen.Type = RoadType.Offered(roadChoice, middleChoice[RoadType.Toolbar[roadChoice].Group]);
                    break;
                case DockKind.Middle:
                    dock.MiddleOpen = dock.MiddleOpen == item.Index ? -1 : item.Index;
                    break;
                case DockKind.MiddleChoice:
                    if (dock.MiddleOpen >= 0 && dock.MiddleOpen < middleChoice.Length) middleChoice[dock.MiddleOpen] = item.Index;
                    dock.MiddleOpen = -1;
                    pen.Type = RoadType.Offered(roadChoice, middleChoice[RoadType.Toolbar[roadChoice].Group]);
                    break;
                case DockKind.Height:
                    levelChoice = item.Index;
                    pen.Level = Dock.HeightLevels[levelChoice];
                    break;
                case DockKind.Snap:
                    pen.SnapAngles = !pen.SnapAngles;
                    break;
                case DockKind.Grid:
                    pen.Grid = RoadPen.GridFeet[item.Index];
                    break;
                case DockKind.Night:
                    SetNight(!night);
                    break;
                case DockKind.Pause:
                    paused = !paused;
                    break;
                case DockKind.Step:
                    paused = true;
                    Sim.Step();
                    break;
                case DockKind.Rate:
                    speed = Speeds[item.Index];
                    paused = false;
                    break;
            }
        }

        /// <summary>A label at each named place on the edge of the map, in its own color.</summary>
        private void DrawPlaceNames(float scale)
        {
            if (drawing == null || !Buildable) return;
            foreach (MapEnd end in drawing.Ends)
            {
                Vector3 p = cam.WorldToScreenPoint(MeshKit.ToWorld(end.At.X, end.At.Y, 1));
                if (p.z <= 0) continue;
                Vector2 size = label.CalcSize(new GUIContent(end.Name));
                var r = new Rect(p.x / scale - size.x / 2 - 10, (Screen.height - p.y) / scale - 13, size.x + 20, 26);
                surface.Fill(r.x, r.y, r.width, r.height, 8, 0xFF000000u | (uint)end.Color);
                surface.Stroke(r.x, r.y, r.width, r.height, 8, 1.5, 0xCCFFFFFFu);
                GUI.Label(new Rect(r.x + 10, r.y + 3, size.x + 4, 20), end.Name, label);
            }
        }

        /// <summary>The card that says a level is complete, and how well.</summary>
        private void DrawResult(float w, float h)
        {
            var r = new Rect(w / 2 - 190, h / 2 - 150, 380, 262);
            Card(r, 20);
            if (Event.current.type == EventType.Repaint)
            {
                for (int i = 0; i < 3; i++) surface.Icon("star", r.x + 190 - 78 + i * 54, r.y + 62, 46, i < stars ? 0xFFFFC93Cu : 0x33FFFFFFu);
            }
            GUILayout.BeginArea(new Rect(r.x + 24, r.y + 18, r.width - 48, r.height - 30));
            GUILayout.Label(level.Name + " solved", big);
            GUILayout.Space(64);
            double slowdown = Sim.TripIdeal > 0 ? Sim.TripSeconds / Sim.TripIdeal : 1;
            GUILayout.Label("Built for " + Money(drawing.Cost()) + " of " + Money(level.Budget) + ".", label);
            GUILayout.Label("Journeys took " + slowdown.ToString("0.00") + " times as long as on empty roads.", label);
            GUILayout.Label(stars >= 3 ? "Traffic hardly noticed. Three stars." : "Three stars for " + level.ThreeStars.ToString("0.0") + " times or less" + (stars < 2 ? ", two for " + level.TwoStars.ToString("0.0") + "." : "."), hint);
            GUILayout.FlexibleSpace();
            GUILayout.BeginHorizontal();
            int next = System.Array.IndexOf(Levels.All, level) + 1;
            if (next < Levels.All.Length && GUILayout.Button("Next puzzle", buttonOn)) later = () => LoadLevel(next);
            if (GUILayout.Button("Keep watching", button)) later = () => resultShown = true;
            if (GUILayout.Button("Improve it", button)) later = () => SetBuilding(true);
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        /// <summary>The rounded dark back of a panel.</summary>
        private void Card(Rect r, float radius = 16)
        {
            if (Event.current.type == EventType.Repaint) Dock.Card(surface, r.x, r.y, r.width, r.height, radius);
        }

        /// <summary>Draws the toolbar with Unity's immediate-mode GUI.</summary>
        private sealed class GuiSurface : ISurface
        {
            private readonly Dictionary<string, Texture2D> icons = new Dictionary<string, Texture2D>();
            private readonly Dictionary<int, GUIStyle> styles = new Dictionary<int, GUIStyle>();

            private static Color Tint(uint c)
            {
                return new Color(((c >> 16) & 255) / 255f, ((c >> 8) & 255) / 255f, (c & 255) / 255f, (c >> 24) / 255f);
            }

            public void Fill(double x, double y, double w, double h, double radius, uint color)
            {
                GUI.DrawTexture(new Rect((float)x, (float)y, (float)w, (float)h), Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, Tint(color), 0, (float)radius);
            }

            public void Stroke(double x, double y, double w, double h, double radius, double width, uint color)
            {
                GUI.DrawTexture(new Rect((float)x, (float)y, (float)w, (float)h), Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, Tint(color), (float)width, (float)radius);
            }

            public void Icon(string name, double x, double y, double size, uint color)
            {
                Texture2D t;
                if (!icons.TryGetValue(name, out t))
                {
                    // Drawn large with smaller copies made from it, so it stays smooth at any size.
                    const int px = 64;
                    float[] ink = Icons.Draw(name, px);
                    var pixels = new Color32[px * px];
                    for (int row = 0; row < px; row++)
                    {
                        // The painter's rows run from the top; a texture's run from the bottom.
                        for (int col = 0; col < px; col++) pixels[(px - 1 - row) * px + col] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(ink[row * px + col] * 255));
                    }
                    t = new Texture2D(px, px, TextureFormat.RGBA32, true);
                    t.SetPixels32(pixels);
                    t.wrapMode = TextureWrapMode.Clamp;
                    t.filterMode = FilterMode.Trilinear;
                    t.Apply(true);
                    icons[name] = t;
                }
                GUI.DrawTexture(new Rect((float)x, (float)y, (float)size, (float)size), t, ScaleMode.StretchToFill, true, 0, Tint(color), 0, 0);
            }

            public void Text(string text, double x, double y, double w, double h, int size, bool bold, int align, uint color)
            {
                int key = size * 8 + align * 2 + (bold ? 1 : 0);
                GUIStyle style;
                if (!styles.TryGetValue(key, out style))
                {
                    style = new GUIStyle(GUI.skin.label)
                    {
                        fontSize = size,
                        fontStyle = bold ? FontStyle.Bold : FontStyle.Normal,
                        alignment = align == 1 ? TextAnchor.MiddleCenter : align == 2 ? TextAnchor.MiddleRight : align == 3 ? TextAnchor.UpperLeft : TextAnchor.MiddleLeft,
                        wordWrap = align == 3,
                        clipping = TextClipping.Overflow,
                        padding = new RectOffset(0, 0, 0, 0),
                        margin = new RectOffset(0, 0, 0, 0),
                    };
                    styles[key] = style;
                }
                style.normal.textColor = Tint(color);
                GUI.Label(new Rect((float)x, (float)y, (float)w, (float)h), text, style);
            }
        }

        // ------------------------------------------------------------ panels

        private void DrawTop(Rect r)
        {
            Card(r);
            GUILayout.BeginArea(r, panel);
            GUILayout.BeginHorizontal();
            GUILayout.BeginVertical(GUILayout.Width(96));
            GUILayout.Label("T R A F F I C", big);
            GUILayout.Space(-6);
            GUILayout.Label(building ? "Road builder" : "Driver Lab", sub);
            GUILayout.EndVertical();
            double v = 0;
            int moving = 0;
            foreach (Car c in Sim.Cars)
            {
                if (c.Crashed) continue;
                v += c.V;
                moving++;
            }
            int t = (int)Sim.Time;
            Stat("CARS", Sim.Cars.Count.ToString());
            Stat("AVG SPEED", (moving > 0 ? Mathf.RoundToInt((float)(v / moving) * Mph) : 0) + " mph");
            if (world.Entries.Count > 0) Stat("LEFT MAP", Sim.Left.ToString());
            else Stat("THROUGH", Sim.Passes.ToString());
            if (level != null)
            {
                big.normal.textColor = drawing.Cost() > level.Budget + 0.5 ? Bad : MeshKit.Hex(0xe7edf0);
                Stat("COST", Money(drawing.Cost()) + " / " + Money(level.Budget));
                big.normal.textColor = routesJoined < level.RouteCount ? Warn : Accent;
                Stat("ROUTES", routesJoined + " of " + level.RouteCount);
                big.normal.textColor = MeshKit.Hex(0xe7edf0);
            }
            else if (mapIndex == OwnMap) Stat("COST", Money(drawing.Cost()));
            else if (Sim.CrashesEnabled) Stat("CRASHES", Sim.Crashes.ToString());
            else Stat("BUMPS", Sim.Bumps.ToString());
            Stat("CLOCK", t / 60 + ":" + (t % 60).ToString("00"));
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        private void Stat(string name, string text)
        {
            GUILayout.BeginVertical();
            GUILayout.Label(name, heading);
            GUILayout.Space(-4);
            GUILayout.Label(text, big);
            GUILayout.EndVertical();
        }

        private void DrawLab(Rect r)
        {
            Card(r);
            GUILayout.BeginArea(r, panel);
            // Vertical scrolling only, with the content held narrower than the panel so
            // the scroll bar never covers the values on the right.
            labScroll = GUILayout.BeginScrollView(labScroll, false, false, GUIStyle.none, GUI.skin.verticalScrollbar);
            GUILayout.BeginVertical(GUILayout.Width(r.width - 50));

            GUILayout.Label("MAP", heading);
            for (int i = 0; i < Maps.All.Length; i++)
            {
                if (GUILayout.Button(Maps.All[i].Name, i == mapIndex ? buttonOn : button) && i != mapIndex)
                {
                    // Changing map changes what this panel holds, so it waits until the panel has finished drawing.
                    int pick = i;
                    later = () =>
                    {
                        LoadMap(pick);
                        Toast(Maps.All[pick].Name + ": " + Maps.All[pick].Blurb + ".");
                    };
                }
            }
            if (GUILayout.Button("Draw your own", mapIndex == OwnMap ? buttonOn : button) && mapIndex != OwnMap) later = LoadOwnMap;
            GUILayout.Space(8);
            GUILayout.Label("PUZZLES", heading);
            for (int i = 0; i < Levels.All.Length; i++)
            {
                bool current = level == Levels.All[i];
                if (GUILayout.Button((i + 1) + ". " + Levels.All[i].Name, current ? buttonOn : button) && !current)
                {
                    int pick = i;
                    later = () => LoadLevel(pick);
                }
            }
            if (level != null)
            {
                // What the puzzle asks for, and how far along it is.
                GUILayout.Space(8);
                GUILayout.Label(level.Blurb, hint);
                GUILayout.Space(4);
                GUILayout.Label(routesJoined < level.RouteCount
                    ? routesJoined + " of " + level.RouteCount + " routes joined. Nothing can get from " + routeMissing + " yet."
                    : "Every route is joined." + (building ? " Press Drive to see how it copes." : ""), label);
                if (!building) GUILayout.Label(Sim.Arrived + " of " + level.TripsToFinish + " journeys finished.", label);
            }
            if (building)
            {
                // While building, the tools are on the toolbar at the bottom and the rest of this panel waits.
                GUILayout.Space(8);
                if (level == null) GUILayout.Label("Draw the roads with the toolbar at the bottom of the screen, then press Drive to let the traffic in.", hint);
                GUILayout.EndVertical();
                GUILayout.EndScrollView();
                GUILayout.EndArea();
                return;
            }
            if (world.Entries.Count > 0)
            {
                arrivals = Slider("New arrivals per entry lane", arrivals.ToString("0") + " a minute", arrivals, 0, 20);
            }

            GUILayout.Space(10);
            GUILayout.Label("CARS", heading);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("+1", button)) SpawnCars(1);
            if (GUILayout.Button("+10", button)) SpawnCars(10);
            if (GUILayout.Button("Clear all", button))
            {
                Sim.Clear();
                Select(-1);
            }
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Force a cut-in", button))
            {
                Car cutter = Sim.ForceCutIn();
                if (cutter != null)
                {
                    Select(cutter.Id);
                    Toast("Car " + cutter.Id + " cut in. Watch the car behind it.");
                }
                else Toast("No two cars are lined up side by side right now. Try again in a moment.");
            }
            if (GUILayout.Button("Clear crashes", button)) Sim.ClearCrashes();
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("+ Truck", button) && Sim.Spawn(newDriver, variation, VehicleKind.Truck) == null) Toast("The road is full.");
            if (GUILayout.Button("+ Motorcycle", button) && Sim.Spawn(newDriver, variation, VehicleKind.Motorcycle) == null) Toast("The road is full.");
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("+ Box truck", button) && Sim.Spawn(newDriver, variation, VehicleKind.BoxTruck) == null) Toast("The road is full.");
            if (GUILayout.Button("+ Semi", button) && Sim.Spawn(newDriver, variation, VehicleKind.Semi) == null) Toast("The road is full.");
            GUILayout.EndHorizontal();
            if (GUILayout.Button("+ School bus", button))
            {
                if (Sim.Spawn(newDriver, 0, VehicleKind.SchoolBus) == null) Toast("The road is full.");
                else Toast("A school bus stops along quieter streets. Traffic behind it waits, and traffic the other way too, unless the road has four lanes or more.");
            }
            truckShare = Slider("Trucks among new vehicles", Mathf.RoundToInt(truckShare * 100) + "%", truckShare, 0, 0.6f);
            bigTruckShare = Slider("Box trucks and semis among trucks", Mathf.RoundToInt(bigTruckShare * 100) + "%", bigTruckShare, 0, 1);
            bikeShare = Slider("Motorcycles among new vehicles", Mathf.RoundToInt(bikeShare * 100) + "%", bikeShare, 0, 0.6f);
            busShare = Slider("School buses among new vehicles", Mathf.RoundToInt(busShare * 100) + "%", busShare, 0, 0.2f);
            camperShare = Slider("Left-lane campers among new drivers", Mathf.RoundToInt(camperShare * 100) + "%", camperShare, 0, 0.3f);

            GUILayout.Space(10);
            GUILayout.Label("SPECIAL DRIVERS", heading);
            for (int i = 0; i < SpecialDriver.All.Length; i += 2)
            {
                GUILayout.BeginHorizontal();
                for (int k = i; k < i + 2 && k < SpecialDriver.All.Length; k++)
                {
                    SpecialDriver kind = SpecialDriver.All[k];
                    GUI.contentColor = MeshKit.Hex(kind.Color);
                    bool pressed = GUILayout.Button("+ " + kind.Name, button);
                    GUI.contentColor = Color.white;
                    if (!pressed) continue;
                    Car special = Sim.SpawnSpecial(kind, PickVehicle());
                    if (special == null) Toast("The road is full.");
                    else
                    {
                        Select(special.Id);
                        Toast(kind.Name + ": " + kind.Blurb + ".");
                    }
                }
                GUILayout.EndHorizontal();
            }

            GUILayout.Space(10);
            GUILayout.Label("RULES", heading);
            Sim.RightOnRed = GUILayout.Toggle(Sim.RightOnRed, " Right turn on red", toggle);
            Sim.KeepRight = GUILayout.Toggle(Sim.KeepRight, " Slower traffic keeps right", toggle);
            Sim.CrashesEnabled = GUILayout.Toggle(Sim.CrashesEnabled, " Crashes wreck cars", toggle);

            GUILayout.Space(10);
            GUILayout.Label("INTERSECTION", heading);
            if (selectedNode < 0)
            {
                GUILayout.Label("Click an intersection to change it.", label);
            }
            else
            {
                JunctionMode mode = Sim.Junctions[selectedNode].Mode;
                if (GUILayout.Button("Traffic light", mode == JunctionMode.Signal ? buttonOn : button)) Sim.SetMode(selectedNode, JunctionMode.Signal);
                if (GUILayout.Button("Traffic light with left arrows", mode == JunctionMode.ProtectedLeft ? buttonOn : button)) Sim.SetMode(selectedNode, JunctionMode.ProtectedLeft);
                if (GUILayout.Button("All-way stop", mode == JunctionMode.AllWay ? buttonOn : button)) Sim.SetMode(selectedNode, JunctionMode.AllWay);
                Junction chosen = Sim.Junctions[selectedNode];
                // A light can go out: drivers then take it as stop signs all round until it is mended.
                // (The button is always there, and only greyed where there is no light: the panel must not gain or lose a control between one pass of the GUI and the next.)
                GUI.enabled = chosen.Failed || mode == JunctionMode.Signal || mode == JunctionMode.ProtectedLeft;
                if (GUILayout.Button(chosen.Failed ? "The light is out. Mend it" : "Put the light out", chosen.Failed ? buttonOn : button)) Sim.FailSignal(selectedNode, !chosen.Failed);
                GUI.enabled = true;
                if (GUILayout.Button("Stop signs on the side street", chosen.IsSideStreet(Sign.Stop) ? buttonOn : button)) Sim.SetSigns(selectedNode, Sign.Stop);
                if (GUILayout.Button("Yield signs on the side street", chosen.IsSideStreet(Sign.Yield) ? buttonOn : button)) Sim.SetSigns(selectedNode, Sign.Yield);
                if (GUILayout.Button("No signs", chosen.IsSideStreet(Sign.None) ? buttonOn : button)) Sim.SetSigns(selectedNode, Sign.None);
            }

            GUILayout.Space(10);
            GUILayout.Label("SHOW", heading);
            showSensors = GUILayout.Toggle(showSensors, " What each car is watching", toggle);
            showLabels = GUILayout.Toggle(showLabels, " What each driver is doing", toggle);

            GUILayout.Space(10);
            GUILayout.Label("NEW DRIVERS", heading);
            GUILayout.BeginHorizontal();
            foreach (var preset in DriverParams.Presets)
            {
                if (GUILayout.Button(preset.Key, button)) newDriver = preset.Value();
            }
            GUILayout.EndHorizontal();
            DriverSliders(newDriver, DriverParams.Count);
            variation = Slider("Variety", Mathf.RoundToInt(variation * 100) + "%", variation, 0, 1);

            GUILayout.EndVertical();
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void DriverSliders(DriverParams drv, int count)
        {
            for (int i = 0; i < count; i++)
            {
                float lo = (float)DriverParams.Ranges[i, 0];
                float hi = (float)DriverParams.Ranges[i, 1];
                float v = (float)drv.Get(i);
                string text = i == 0 ? v.ToString("0.00") + " s" : i == 1 ? Mathf.RoundToInt(v * 100) + "% of limit" : i == 2 ? v.ToString("0.0") + " s" : Mathf.RoundToInt(v * 100) + "%";
                float next = Slider(SliderNames[i], text, v, lo, hi);
                if (next != v) drv.Set(i, next);
            }
        }

        private float Slider(string name, string text, float v, float lo, float hi)
        {
            GUILayout.Space(2);
            GUILayout.BeginHorizontal();
            GUILayout.Label(name, label);
            GUILayout.FlexibleSpace();
            GUILayout.Label(text, value);
            GUILayout.EndHorizontal();
            return GUILayout.HorizontalSlider(v, lo, hi);
        }

        private void DrawInspector(Rect r, Car car)
        {
            Card(r);
            GUILayout.BeginArea(r, panel);
            GUILayout.BeginHorizontal();
            Rect dot = GUILayoutUtility.GetRect(14, 14, GUILayout.Width(14));
            Fill(new Rect(dot.x, dot.y + 4, 12, 12), MeshKit.Hex(car.Color));
            if (car.Special != null) big.normal.textColor = MeshKit.Hex(car.Color);
            GUILayout.Label((car.Special != null ? car.Special + " " : "") + car.Vehicle.Name.ToLower() + " " + car.Id, big);
            big.normal.textColor = MeshKit.Hex(0xe7edf0);
            GUILayout.FlexibleSpace();
            stateStyle.normal.textColor = StateColor(car.State);
            GUILayout.Label(StateNames[(int)car.State], stateStyle);
            if (GUILayout.Button("X", button, GUILayout.Width(26))) Select(-1);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            Stat("SPEED", Mathf.RoundToInt((float)car.V * Mph) + " mph");
            Stat("GAP AHEAD", car.Gap < 90 ? Mathf.RoundToInt((float)car.Gap * Feet) + " ft" : "Clear");
            Stat("NEXT TURN", car.Turn == Turn.Left ? "Left" : car.Turn == Turn.Right ? "Right" : "Straight");
            GUILayout.EndHorizontal();

            Graph("Speed (mph)", car, car.HistV, Mph, 0, 45, Accent, false);
            Graph("Gap ahead (ft)", car, car.HistGap, Feet, 0, 200, Info, false);
            Graph("Gas and brake", car, car.HistA, 1, -8, 4, Warn, true);

            DriverSliders(car.Drv, DriverParams.Total);

            GUILayout.Space(8);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Follow", follow ? buttonOn : button))
            {
                follow = !follow;
                if (follow && dist > 150) dist = 110;
            }
            if (GUILayout.Button("Cut in now", button))
            {
                if (Sim.ForceCutIn(car.Id) == null) Toast("No lane to cut into from here.");
            }
            if (GUILayout.Button("Remove", button))
            {
                Sim.RemoveCar(car.Id);
                Select(-1);
            }
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        private void Graph(string name, Car car, float[] data, float unit, float lo, float hi, Color color, bool zeroLine)
        {
            Rect r = GUILayoutUtility.GetRect(100, 50, GUILayout.ExpandWidth(true));
            if (Event.current.type != EventType.Repaint) return;
            Fill(r, new Color(1, 1, 1, 0.05f));
            float YOf(float x)
            {
                return r.yMax - 3 - (Mathf.Clamp(x, lo, hi) - lo) / (hi - lo) * (r.height - 20);
            }
            if (zeroLine) Fill(new Rect(r.x, YOf(0), r.width, 1), new Color(1, 1, 1, 0.2f));
            float prevY = 0;
            for (int k = 0; k < car.HistCount; k++)
            {
                int idx = (car.HistIndex - car.HistCount + k + Simulation.HistN) % Simulation.HistN;
                float x = r.xMax - (car.HistCount - 1 - k) / (float)(Simulation.HistN - 1) * r.width;
                float y = YOf(data[idx] * unit);
                // A short vertical bar from the previous sample keeps steps connected.
                float top = k == 0 ? y : Mathf.Min(y, prevY);
                float height = k == 0 ? 1.6f : Mathf.Abs(y - prevY) + 1.6f;
                Fill(new Rect(x - 0.8f, top, 1.6f, height), color);
                prevY = y;
            }
            GUI.Label(new Rect(r.x + 6, r.y + 2, r.width, 16), name, heading);
        }

        private void Fill(Rect r, Color c)
        {
            Color old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, white);
            GUI.color = old;
        }

        private static Color StateColor(CarState s)
        {
            switch (s)
            {
                case CarState.Brake:
                case CarState.Startled:
                case CarState.Crash:
                case CarState.Scared:
                case CarState.Distracted:
                    return Bad;
                case CarState.Wary:
                case CarState.Gawking:
                case CarState.Stopping:
                case CarState.Wait:
                case CarState.Yield:
                    return Warn;
                case CarState.LaneChange:
                case CarState.Merge:
                case CarState.LettingIn:
                    return Info;
                default:
                    return Accent;
            }
        }

        // ------------------------------------------------------------ tags over cars

        private void DrawTags(float scale)
        {
            bool zoomedOut = dist > 260;
            foreach (Car car in Sim.Cars)
            {
                if (car.Height < -RoadView.Covered) continue; // out of sight in a tunnel
                // From far away only the interesting states get a tag.
                bool dull = car.State == CarState.Cruise || car.State == CarState.Follow;
                if (zoomedOut && car.Id != selected && car.Special == null && dull) continue;
                Vector3 p = cam.WorldToScreenPoint(MeshKit.ToWorld(car.X, car.Y, 2.4f + (float)car.Height));
                if (p.z <= 0 || p.x < -40 || p.x > Screen.width + 40 || p.y < -40 || p.y > Screen.height + 40) continue;
                string text = StateNames[(int)car.State];
                if (car.Special != null) text = dull ? car.Special : car.Special + " - " + text;
                Vector2 size = tagStyle.CalcSize(new GUIContent(text));
                var r = new Rect(p.x / scale - size.x / 2, (Screen.height - p.y) / scale - size.y - 12, size.x, size.y);
                bool bad = car.State == CarState.Brake || car.State == CarState.Startled || car.State == CarState.Crash || car.State == CarState.Scared;
                Fill(r, bad ? new Color(0.84f, 0.19f, 0.19f, 0.92f) : new Color(0.04f, 0.055f, 0.063f, 0.78f));
                tagStyle.normal.textColor = bad ? Color.white : car.Special != null ? MeshKit.Hex(car.Color) : StateColor(car.State);
                GUI.Label(r, text, tagStyle);
            }
        }

        // ------------------------------------------------------------ mouse and keyboard

        private void HandleSceneInput(Event e, bool overUi, float scale)
        {
            Vector2 pixel = e.mousePosition * scale;
            hoverPixel = pixel;
            pointerOverUi = overUi;
            switch (e.type)
            {
                case EventType.MouseDown:
                    if (overUi) break;
                    if (e.button == 0)
                    {
                        // The left button builds and picks things; it never moves the view. With the move tool a press
                        // on a point or a road takes hold of it; any other press is a click, there and then.
                        grabbing = building && GrabAt(pixel);
                        if (grabbing) break;
                        if (building) EditClick(pixel);
                        else ClickAt(pixel);
                    }
                    else if (e.button == 1)
                    {
                        panning = true;
                        panMoved = false;
                        lastMouse = pressPixel = pixel;
                    }
                    else if (e.button == 2)
                    {
                        turning = true;
                        lastMouse = pixel;
                    }
                    break;
                case EventType.MouseDrag:
                    if (grabbing && e.button == 0) GrabTo(pixel);
                    else if (panning && e.button == 1)
                    {
                        // A hand is never quite still: the view only starts to move once the pointer has clearly left
                        // where it went down. Anything less is a right-click.
                        if (!panMoved && (pixel - pressPixel).magnitude > Mathf.Max(8f, Screen.height / 120f)) panMoved = true;
                        if (!panMoved) break;
                        Pan(pixel.x - lastMouse.x, pixel.y - lastMouse.y);
                        lastMouse = pixel;
                    }
                    else if (turning && e.button == 2)
                    {
                        Swing((pixel.x - lastMouse.x) * 0.0036f, (lastMouse.y - pixel.y) * 0.0024f);
                        lastMouse = pixel;
                    }
                    break;
                case EventType.MouseUp:
                    if (e.button == 0)
                    {
                        if (grabbing) GrabDrop();
                        grabbing = false;
                    }
                    else if (e.button == 1)
                    {
                        // A right-click that did not drag the view takes back the last click of the road being drawn.
                        if (panning && !panMoved && building) pen.Back();
                        panning = false;
                    }
                    else if (e.button == 2) turning = false;
                    break;
                case EventType.ScrollWheel:
                    if (!overUi) Zoom(Mathf.Exp(e.delta.y * 0.04f), pixel);
                    break;
                case EventType.KeyUp:
                    held.Remove(e.keyCode);
                    break;
                case EventType.KeyDown:
                    if (!(e.control || e.command || e.alt))
                    {
                        // Keys that move or turn the view work while they are held: see LabApp.MoveView.
                        if (e.keyCode == KeyCode.W || e.keyCode == KeyCode.A || e.keyCode == KeyCode.S || e.keyCode == KeyCode.D || e.keyCode == KeyCode.Q || e.keyCode == KeyCode.E
                            || e.keyCode == KeyCode.LeftArrow || e.keyCode == KeyCode.RightArrow || e.keyCode == KeyCode.UpArrow || e.keyCode == KeyCode.DownArrow)
                        {
                            held.Add(e.keyCode);
                            break;
                        }
                        if (e.keyCode == KeyCode.N || e.keyCode == KeyCode.Home)
                        {
                            FaceNorth();
                            break;
                        }
                    }
                    if (building)
                    {
                        if (e.keyCode == KeyCode.Escape) StopDrawing();
                        else if (e.keyCode == KeyCode.Z && (e.control || e.command)) Undo(undo, redo);
                        else if (e.keyCode == KeyCode.Y && (e.control || e.command)) Undo(redo, undo);
                        else if (e.control || e.command || e.alt) break;
                        // The number keys pick up the road tool in each shape; letters pick up the other tools.
                        else if (e.keyCode >= KeyCode.Alpha1 && e.keyCode <= KeyCode.Alpha3)
                        {
                            pen.Tool = RoadPen.Road;
                            pen.Shape = e.keyCode - KeyCode.Alpha1;
                        }
                        else if (e.keyCode == KeyCode.M) pen.Tool = RoadPen.Move;
                        else if (e.keyCode == KeyCode.L) pen.Tool = RoadPen.Lane;
                        else if (e.keyCode == KeyCode.T) pen.Tool = RoadPen.Signs;
                        else if (e.keyCode == KeyCode.K) SetNight(!night);
                        else if (e.keyCode == KeyCode.R) pen.Tool = RoadPen.Replace;
                        else if (e.keyCode == KeyCode.B) pen.Tool = RoadPen.Bulldoze;
                        else if (e.keyCode == KeyCode.G) pen.Grid = RoadPen.GridFeet[(System.Array.IndexOf(RoadPen.GridFeet, pen.Grid) + 1) % RoadPen.GridFeet.Length];
                    }
                    else if (e.keyCode == KeyCode.Space) paused = !paused;
                    else if (e.keyCode == KeyCode.K) SetNight(!night);
                    else if (e.keyCode == KeyCode.Period)
                    {
                        paused = true;
                        Sim.Step();
                    }
                    else if (e.keyCode == KeyCode.Escape)
                    {
                        Select(-1);
                        SelectNode(-1);
                    }
                    break;
            }
        }
    }
}
