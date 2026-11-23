using Traffic.Sim;
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
        private static readonly string[] SliderNames = { "Reaction time", "Speed", "Following gap", "Aggression", "Politeness", "Obeys lights and signs", "Weaving between lanes", "Swerving (drunk)", "Looking away (phone)" };
        private static readonly string[] StateNames = { "CRUISE", "FOLLOW", "BRAKE", "STOPPING", "WAIT", "YIELD", "LETTING IN", "MERGE", "LANE CHANGE", "STARTLED", "CRASH", "SCARED", "WARY", "LOOKING AWAY" };
        private static readonly float[] Speeds = { 0.25f, 1, 2, 4 };
        private static readonly string[] SpeedNames = { "1/4x", "1x", "2x", "4x" };

        private static readonly Color Accent = MeshKit.Hex(0x5ee0c0);
        private static readonly Color Dim = MeshKit.Hex(0x8d9aa3);
        private static readonly Color Warn = MeshKit.Hex(0xffc93c);
        private static readonly Color Info = MeshKit.Hex(0x6cb8ff);
        private static readonly Color Bad = MeshKit.Hex(0xff5d5d);

        private Texture2D white;
        private GUIStyle panel, heading, label, value, big, sub, tagStyle, stateStyle, button, buttonOn, toggle;
        private Vector2 labScroll;
        private bool dragging;
        private float dragMoved;
        private Vector2 lastMouse;

        private void BuildStyles()
        {
            white = new Texture2D(1, 1);
            white.SetPixel(0, 0, Color.white);
            white.Apply();
            Texture2D Solid(Color c)
            {
                var t = new Texture2D(1, 1);
                t.SetPixel(0, 0, c);
                t.Apply();
                return t;
            }
            panel = new GUIStyle { padding = new RectOffset(14, 14, 12, 12) };
            panel.normal.background = Solid(new Color(0.06f, 0.075f, 0.086f, 0.9f));
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
            button = new GUIStyle(GUI.skin.button) { fontSize = 13, fontStyle = FontStyle.Bold };
            toggle = new GUIStyle(GUI.skin.toggle) { fontSize = 13 };
            toggle.normal.textColor = toggle.onNormal.textColor = MeshKit.Hex(0xe7edf0);
            buttonOn = new GUIStyle(button);
            buttonOn.normal.textColor = Accent;
            buttonOn.hover.textColor = Accent;
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

            if (showLabels && e.type == EventType.Repaint) DrawTags(scale);

            var top = new Rect(12, 12, 430, 54);
            var lab = new Rect(12, 76, 300, Mathf.Min(800, h - 150));
            var transport = new Rect(w / 2 - 150, h - 58, 300, 46);
            float inspectorHeight = Mathf.Min(612, h - 24);
            var inspector = new Rect(w - 300, h - 12 - inspectorHeight, 288, inspectorHeight);
            Car car = Sim.CarById(selected);

            DrawTop(top);
            DrawLab(lab);
            DrawTransport(transport);
            if (car != null) DrawInspector(inspector, car);
            if (Time.unscaledTime < toastUntil)
            {
                Vector2 size = label.CalcSize(new GUIContent(toast));
                var r = new Rect(w / 2 - size.x / 2 - 14, h - 108, size.x + 28, 34);
                GUI.Box(r, GUIContent.none, panel);
                GUI.Label(new Rect(r.x + 14, r.y + 8, size.x + 4, 20), toast, label);
            }

            bool overUi = top.Contains(e.mousePosition) || lab.Contains(e.mousePosition) || transport.Contains(e.mousePosition) || (car != null && inspector.Contains(e.mousePosition));
            HandleSceneInput(e, overUi, scale);
        }

        // ------------------------------------------------------------ panels

        private void DrawTop(Rect r)
        {
            GUILayout.BeginArea(r, panel);
            GUILayout.BeginHorizontal();
            GUILayout.BeginVertical(GUILayout.Width(96));
            GUILayout.Label("T R A F F I C", big);
            GUILayout.Space(-6);
            GUILayout.Label("Driver Lab", sub);
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
            Stat("THROUGH", Sim.Passes.ToString());
            if (Sim.CrashesEnabled) Stat("CRASHES", Sim.Crashes.ToString());
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
            GUILayout.BeginArea(r, panel);
            // Vertical scrolling only, with the content held narrower than the panel so
            // the scroll bar never covers the values on the right.
            labScroll = GUILayout.BeginScrollView(labScroll, false, false, GUIStyle.none, GUI.skin.verticalScrollbar);
            GUILayout.BeginVertical(GUILayout.Width(r.width - 50));

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
                    Car special = Sim.SpawnSpecial(kind);
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

        private void DrawTransport(Rect r)
        {
            GUILayout.BeginArea(r, panel);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(paused ? "Play" : "Pause", button, GUILayout.Width(62))) paused = !paused;
            if (GUILayout.Button("Step", button, GUILayout.Width(50)))
            {
                paused = true;
                Sim.Step();
            }
            for (int i = 0; i < Speeds.Length; i++)
            {
                if (GUILayout.Button(SpeedNames[i], speed == Speeds[i] ? buttonOn : button))
                {
                    speed = Speeds[i];
                    paused = false;
                }
            }
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        private void DrawInspector(Rect r, Car car)
        {
            GUILayout.BeginArea(r, panel);
            GUILayout.BeginHorizontal();
            Rect dot = GUILayoutUtility.GetRect(14, 14, GUILayout.Width(14));
            Fill(new Rect(dot.x, dot.y + 4, 12, 12), MeshKit.Hex(car.Color));
            if (car.Special != null) big.normal.textColor = MeshKit.Hex(car.Color);
            GUILayout.Label(car.Special ?? "Car " + car.Id, big);
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
                // From far away only the interesting states get a tag.
                bool dull = car.State == CarState.Cruise || car.State == CarState.Follow;
                if (zoomedOut && car.Id != selected && car.Special == null && dull) continue;
                Vector3 p = cam.WorldToScreenPoint(MeshKit.ToWorld(car.X, car.Y, 2.4f));
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
            switch (e.type)
            {
                case EventType.MouseDown:
                    if (!overUi && e.button == 0)
                    {
                        dragging = true;
                        dragMoved = 0;
                        lastMouse = pixel;
                    }
                    break;
                case EventType.MouseDrag:
                    if (dragging)
                    {
                        Vector2 d = pixel - lastMouse;
                        lastMouse = pixel;
                        dragMoved += Mathf.Abs(d.x) + Mathf.Abs(d.y);
                        Pan(d.x, d.y);
                    }
                    break;
                case EventType.MouseUp:
                    if (dragging && dragMoved < 6) ClickAt(pixel);
                    dragging = false;
                    break;
                case EventType.ScrollWheel:
                    if (!overUi) Zoom(Mathf.Exp(e.delta.y * 0.04f));
                    break;
                case EventType.KeyDown:
                    if (e.keyCode == KeyCode.Space) paused = !paused;
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
