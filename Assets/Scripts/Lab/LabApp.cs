using System.Collections.Generic;
using Traffic.Sim;
using Traffic.Ui;
using UnityEngine;

namespace Traffic.Lab
{
    /// <summary>Creates the driver lab when Play is pressed, in whatever scene is open.</summary>
    public static class LabBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Start()
        {
            if (LabApp.Instance != null) return;
            new GameObject("Driver Lab").AddComponent<LabApp>();
        }
    }

    /// <summary>
    /// The driver lab: owns the simulation, steps it at a fixed rate, and drives
    /// the views, the camera and the on-screen controls (see LabUi.cs).
    /// </summary>
    public sealed partial class LabApp : MonoBehaviour
    {
        private const float Mph = 2.23694f;
        private const float Feet = 3.28084f;
        /// <summary>How far the camera leans away from straight down, radians, and which way it faces (0 is north up).</summary>
        private float tilt = 0.4f, yaw;
        /// <summary>Keys held down that move or turn the view. Kept from GUI key events, so it works under either input setting.</summary>
        private readonly HashSet<KeyCode> held = new HashSet<KeyCode>();

        /// <summary>The running lab, if any.</summary>
        public static LabApp Instance { get; private set; }

        public Simulation Sim { get; private set; }

        private World world;
        private RoadView roads;
        private CarViews carViews;
        private Camera cam;
        private Material lineMaterial;

        private bool paused;
        private float speed = 1;
        private int selected = -1;
        private int selectedNode = -1;
        private int mapIndex;
        private float arrivals = 6;
        private bool follow;
        private DriverParams newDriver = DriverParams.Normal;
        private float variation = 0.3f;
        private float truckShare = 0.12f;
        private float bikeShare = 0.06f;
        private bool showSensors = true;
        private bool showLabels = true;

        private Vector3 target;
        private float dist = 460;
        private double owed;
        private string toast = "";
        private float toastUntil;

        private void Awake()
        {
            Instance = this;
            SetUpScene();
            LoadMap(0);
        }

        /// <summary>Throw away the current map and traffic and start another.</summary>
        private void LoadMap(int index)
        {
            mapIndex = index;
            level = null;
            building = false;
            StartWorld(Maps.All[index].Build());
            Fit();
            SpawnCars(Maps.All[index].StartingCars);
        }

        /// <summary>Replace the roads and traffic with a new world.</summary>
        private void StartWorld(World next)
        {
            bool crashes = Sim == null || Sim.CrashesEnabled;
            bool rightOnRed = Sim == null || Sim.RightOnRed;
            if (roads != null) roads.Destroy();
            if (carViews != null) carViews.Destroy();
            selected = -1;
            selectedNode = -1;
            follow = false;
            world = next;
            Sim = new Simulation(world, (uint)(System.DateTime.Now.Ticks & 0xffff));
            Sim.CrashesEnabled = crashes;
            Sim.RightOnRed = rightOnRed;
            Sim.OnAccident = accident =>
            {
                string kind = accident.Kind == CrashKind.RearEnd ? "Rear-end crash" : accident.Kind == CrashKind.Sideswipe ? "Sideswipe" : accident.Kind == CrashKind.HeadOn ? "Head-on crash" : "Broadside crash";
                int mph = Mathf.RoundToInt((float)accident.ClosingSpeed * Mph);
                Toast(accident.Major ? kind + " at " + mph + " mph. The road is blocked until the wrecks are towed." : kind + " at " + mph + " mph, a fender bender. They will move on shortly.");
            };
            roads = new RoadView(world, transform);
            carViews = new CarViews(transform);
        }

        // ------------------------------------------------------------ drawing roads

        private const string SaveKey = "traffic.drawing";
        /// <summary>How far the player's own map reaches from its middle, each way, meters.</summary>
        private const double OwnMapHalf = 600;
        /// <summary>The puzzle being played, or null in the lab or on the player's own map.</summary>
        private Level level;
        /// <summary>How many of the level's journeys the roads so far make possible, and one that is still missing.</summary>
        private int routesJoined;
        private string routeMissing = "";
        /// <summary>Stars earned once the level is complete; 0 until then. The card that says so can be put away.</summary>
        private int stars;
        private bool resultShown;
        /// <summary>True on any map the player can build on: their own, or a level.</summary>
        private bool Buildable => mapIndex >= OwnMap;
        /// <summary>The map index that means "the one the player is drawing".</summary>
        private static int OwnMap => Maps.All.Length;

        /// <summary>
        /// The hand that draws. It holds the map being built and everything about the road
        /// that is half drawn, and keeps the picture of what the next click would do.
        /// </summary>
        private readonly RoadPen pen = new RoadPen();
        private EditableMap drawing
        {
            get { return pen.Map; }
            set { pen.Map = value; }
        }
        /// <summary>True while laying out roads; traffic only runs once this is off.</summary>
        private bool building;
        /// <summary>Which of RoadType.All is being drawn.</summary>
        private int roadChoice;
        /// <summary>Which of Dock.HeightLevels is being drawn at: the ground, a bridge or a tunnel.</summary>
        private int levelChoice;
        private Vector2 hoverPixel;
        /// <summary>True while the pointer is over a panel or the toolbar, where it draws nothing.</summary>
        private bool pointerOverUi;
        /// <summary>Earlier states of the drawing, for undo and redo.</summary>
        private readonly List<string> undo = new List<string>();
        private readonly List<string> redo = new List<string>();

        /// <summary>Open a puzzle level, as it was last left.</summary>
        private void LoadLevel(int index)
        {
            mapIndex = OwnMap + 1 + index;
            level = Levels.All[index];
            EditableMap fresh = level.Start();
            drawing = EditableMap.Parse(PlayerPrefs.GetString(SaveKey + ".level." + level.Name, ""));
            // What was saved must still hold the level's own roads; if the level has changed since, start it again.
            int own = 0;
            foreach (EditSegment s in drawing.Segments) if (s.Locked) own++;
            if (own < fresh.Segments.Count) drawing = fresh;
            drawing.Ends = fresh.Ends;
            drawing.Bound = fresh.Bound;
            undo.Clear();
            redo.Clear();
            building = true;
            StopDrawing();
            RebuildDrawing(false);
            Fit();
            dist *= 0.8f;
        }

        /// <summary>Open the player's own map, as it was last left.</summary>
        private void LoadOwnMap()
        {
            mapIndex = OwnMap;
            level = null;
            undo.Clear();
            redo.Clear();
            drawing = EditableMap.Parse(PlayerPrefs.GetString(SaveKey, ""));
            // The map has an edge: roads run to it bring traffic in, and roads that stop short are dead ends.
            // It is set out beyond anything drawn before there was one.
            double reach = OwnMapHalf;
            foreach (Vec n in drawing.Nodes) reach = System.Math.Max(reach, System.Math.Ceiling(System.Math.Max(System.Math.Abs(n.X), System.Math.Abs(n.Y)) / 100) * 100);
            drawing.Bound = reach;
            building = true;
            StopDrawing();
            RebuildDrawing(false);
            Fit();
            dist *= 0.55f;
        }

        /// <summary>How many cars wander the roads while they are being built: only a few, to give the place some life.</summary>
        private int AmbientCars => Mathf.Clamp(drawing.Segments.Count + 1, 2, 6);

        /// <summary>
        /// Rebuild the roads from the drawing. In drive mode this starts traffic afresh. In build
        /// mode a few cars drive around, and with keepCars they carry on from where they were.
        /// </summary>
        private void RebuildDrawing(bool keepCars)
        {
            List<Car> before = keepCars && building && Sim != null ? new List<Car>(Sim.Cars) : null;
            StartWorld(drawing.ToWorld());
            stars = 0;
            resultShown = false;
            if (level != null)
            {
                var missing = new List<string>();
                routesJoined = level.Connected(world, missing);
                routeMissing = missing.Count > 0 ? missing[0] : "";
            }
            if (!building)
            {
                // Once the roads are open, everyone who arrives is going somewhere: where the level says, or any other road off the map.
                Sim.UseRoutes = true;
                if (level != null) Sim.Demand = level.DemandFor(world);
                // A few cars to start with, wherever there is room; the rest arrive at the open road ends.
                int starters = level != null ? 0 : Mathf.Min(10, drawing.Segments.Count * 2);
                for (int i = 0; i < starters; i++) Sim.Spawn(newDriver, variation, PickVehicle());
                return;
            }
            if (before != null) foreach (Car old in before) Carry(old);
            // With no way in from outside, put the few cars on the roads directly.
            int want = world.Entries.Count == 0 ? AmbientCars : AmbientCars / 2;
            for (int i = Sim.Cars.Count; i < want; i++) Sim.Spawn(DriverParams.Normal, 0.3, VehicleKind.Car);
        }

        /// <summary>Put a car from before a rebuild back on the road it was on, if that road is still there.</summary>
        private void Carry(Car old)
        {
            if (old.Crashed) return;
            Lane best = null;
            double bestS = 0, bestD = 1.5;
            foreach (Lane lane in Sim.Lanes)
            {
                if (lane.Kind != LaneKind.Road) continue;
                Projection p = lane.Path.Project(old.X, old.Y);
                if (p.D >= bestD || p.S < 8 || p.S > lane.Length - 8) continue;
                if (System.Math.Cos(lane.Path.At(p.S).H - old.H) < 0.7) continue;
                best = lane;
                bestS = p.S;
                bestD = p.D;
            }
            if (best == null) return;
            foreach (Car c in Sim.Cars) if (c.Lane == best.Id && System.Math.Abs(c.S - bestS) < 12) return;
            Car again = Sim.AddCar(best.Id, bestS, old.V, old.Drv, null, old.Vehicle.Kind);
            again.Color = old.Color;
        }

        /// <summary>Forget the road that was half drawn.</summary>
        private void StopDrawing()
        {
            pen.Cancel();
        }

        /// <summary>The drawing has changed: save it and rebuild the roads.</summary>
        private void DrawingChanged(string before)
        {
            undo.Add(before);
            if (undo.Count > 200) undo.RemoveAt(0);
            redo.Clear();
            SaveDrawing();
            RebuildDrawing(true);
        }

        private void Undo(List<string> from, List<string> to)
        {
            if (!building || from.Count == 0) return;
            to.Add(drawing.Serialize());
            // The saved copy holds the roads only: the edge of the map and a level's named places carry over.
            EditableMap earlier = EditableMap.Parse(from[from.Count - 1]);
            earlier.Ends = drawing.Ends;
            earlier.Bound = drawing.Bound;
            drawing = earlier;
            from.RemoveAt(from.Count - 1);
            StopDrawing();
            SaveDrawing();
            RebuildDrawing(true);
        }

        private void SaveDrawing()
        {
            PlayerPrefs.SetString(level != null ? SaveKey + ".level." + level.Name : SaveKey, drawing.Serialize());
            PlayerPrefs.Save();
        }

        private void SetBuilding(bool on)
        {
            building = on;
            paused = false;
            StopDrawing();
            RebuildDrawing(false);
            if (on) return;
            if (level != null && routesJoined < level.RouteCount) Toast("Not every route is joined up yet: nothing can get from " + routeMissing + ".");
            else if (level != null && drawing.Cost() > level.Budget + 0.5) Toast("These roads cost more than the budget, so the level will not count as solved.");
            else if (world.Entries.Count == 0 && drawing.Segments.Count > 0) Toast("No road reaches the edge of the map, the broken line, so no traffic will arrive.");
            else if (drawing.Faults().Count > 0) Toast("Traffic cannot get through everywhere. The trouble spots are ringed in red when you build.");
        }

        /// <summary>The spot on the ground under a screen point (pixels, y down).</summary>
        private bool GroundAt(Vector2 pixel, out Vec point)
        {
            point = default(Vec);
            Ray ray = cam.ScreenPointToRay(new Vector3(pixel.x, Screen.height - pixel.y, 0));
            if (Mathf.Abs(ray.direction.y) < 1e-4f) return false;
            Vector3 hit = ray.origin - ray.direction * (ray.origin.y / ray.direction.y);
            point = new Vec(hit.x, hit.z);
            return true;
        }

        /// <summary>How many meters of ground one pixel of screen covers, at the middle of the view.</summary>
        private double MetersPerPixel => 2 * dist * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) / Mathf.Max(Screen.height, 1);

        /// <summary>A click on the ground while building: whatever the tool in hand does with it.</summary>
        private void EditClick(Vector2 pixel)
        {
            Vec p;
            if (!GroundAt(pixel, out p)) return;
            string before = drawing.Serialize();
            string refused;
            bool changed = pen.Click(p, out refused);
            if (refused != null) Toast(refused);
            if (changed) DrawingChanged(before);
        }

        /// <summary>
        /// The button went down on the ground while building. True if the move tool took hold
        /// of a point or a road there, so that dragging moves that and not the view.
        /// </summary>
        private bool GrabAt(Vector2 pixel)
        {
            Vec p;
            return GroundAt(pixel, out p) && pen.Press(p);
        }

        private void GrabTo(Vector2 pixel)
        {
            Vec p;
            if (GroundAt(pixel, out p)) pen.Drag(p);
        }

        private void GrabDrop()
        {
            string before = drawing.Serialize();
            string refused;
            bool changed = pen.Release(out refused);
            if (refused != null) Toast(refused);
            if (changed) DrawingChanged(before);
        }

        /// <summary>A button on the lane tool's card was pressed.</summary>
        private void LaneAction(LaneAct act)
        {
            string before = drawing.Serialize();
            string refused;
            bool changed = act == LaneAct.Add ? pen.AddLane(out refused) : act == LaneAct.Remove ? pen.RemoveLane(out refused)
                : act == LaneAct.Middle ? pen.AddMiddleLane(out refused) : pen.ToggleTurn(act - LaneAct.Left, out refused);
            if (refused != null) Toast(refused);
            if (changed) DrawingChanged(before);
        }

        /// <summary>A button on the signs tool's card was pressed.</summary>
        private void SignAction(SignAct act, Control which)
        {
            string before = drawing.Serialize();
            string refused;
            bool changed = act == SignAct.Control ? pen.SetControl(which, out refused) : act == SignAct.UsualSpeed ? pen.UsualSpeed(out refused)
                : pen.StepSpeed(act == SignAct.Faster ? 1 : -1, out refused);
            if (refused != null) Toast(refused);
            if (changed) DrawingChanged(before);
        }

        /// <summary>True after dark.</summary>
        private bool night;

        /// <summary>Switch between daylight and night.</summary>
        private void SetNight(bool dark)
        {
            night = dark;
        }

        private Mesh ghostMesh;
        private GameObject ghostObject;
        private int ghostVersion = -1;
        private readonly List<Vector3> ghostPoints = new List<Vector3>();
        private readonly List<Color32> ghostColors = new List<Color32>();

        /// <summary>
        /// While building, lay the pen's picture of what the next click would do on the ground:
        /// the road about to be placed at its real width, the points it joins, the handles
        /// that can be dragged. It is rebuilt only when the pen says the picture has changed.
        /// </summary>
        private void ShowGhost()
        {
            if (ghostObject == null)
            {
                ghostMesh = new Mesh();
                ghostMesh.MarkDynamic();
                ghostMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                ghostObject = MeshKit.Spawn("Build guide", ghostMesh, lineMaterial, transform, false);
                ghostObject.GetComponent<MeshRenderer>().receiveShadows = false;
            }
            ghostObject.SetActive(building);
            if (!building) return;
            Vec hover;
            double pixel = MetersPerPixel;
            if (!pointerOverUi && GroundAt(hoverPixel, out hover)) pen.Hover(hover, pixel);
            else pen.Away(pixel);
            if (pen.Version == ghostVersion) return;
            ghostVersion = pen.Version;
            Ghost ghost = pen.Ghost;
            ghostPoints.Clear();
            ghostColors.Clear();
            for (int i = 0; i < ghost.Points.Count; i++)
            {
                uint c = ghost.Colors[i];
                ghostPoints.Add(MeshKit.ToWorld(ghost.Points[i].X, ghost.Points[i].Y, 0.35f));
                ghostColors.Add(new Color32((byte)(c >> 16), (byte)(c >> 8), (byte)c, (byte)(c >> 24)));
            }
            ghostMesh.Clear();
            ghostMesh.SetVertices(ghostPoints);
            ghostMesh.SetColors(ghostColors);
            ghostMesh.SetTriangles(ghost.Tris, 0);
            // Never cull it: it can be anywhere on the map.
            ghostMesh.bounds = new Bounds(Vector3.zero, new Vector3(1e5f, 1e5f, 1e5f));
        }

        private static string Money(double dollars)
        {
            return "$" + System.Math.Round(dollars).ToString("N0", System.Globalization.CultureInfo.InvariantCulture);
        }

        /// <summary>What the toolbar says about each height a road can be built at.</summary>
        private static string[] HeightNotes()
        {
            var notes = new string[Dock.HeightLevels.Length];
            for (int i = 0; i < notes.Length; i++)
            {
                int level = Dock.HeightLevels[i];
                if (level == 0)
                {
                    notes[i] = "Roads on the ground meet wherever they cross.";
                    continue;
                }
                int tall = Mathf.RoundToInt((float)(System.Math.Abs(level) * EditableMap.LevelHeight) * Feet);
                int ramp = Mathf.RoundToInt((float)EditableMap.RampFor(level) * Feet / 10) * 10;
                int least = Mathf.RoundToInt((float)(2 * EditableMap.RampFor(level) + 20) * Feet / 10) * 10;
                notes[i] = tall + " ft " + (level > 0 ? "up" : "down") + ".  Costs " + EditableMap.CostTimes(level).ToString("0") + " times as much.\n"
                    + "It needs " + ramp + " ft at each end to " + (level > 0 ? "climb" : "go down") + ", so at least " + least + " ft in all.\n"
                    + "It crosses other roads without meeting them, given a level's height between them. Roads can only join it at its ends."
                    + (level < 0 ? "\nTraffic is out of sight while it is under ground." : "");
            }
            return notes;
        }

        /// <summary>What the toolbar needs to know about each kind of road: its picture and its explanation.</summary>
        private static DockRoad[] RoadTiles()
        {
            var tiles = new DockRoad[RoadType.All.Length];
            for (int i = 0; i < tiles.Length; i++)
            {
                RoadType t = RoadType.All[i];
                string body = t.Mph + " mph.  $" + t.CostPerMeter.ToString(t.CostPerMeter % 1 == 0 ? "0" : "0.00", System.Globalization.CultureInfo.InvariantCulture) + " a meter.  " + Mathf.RoundToInt((float)t.Width * Feet) + " ft wide.\n";
                body += t.CenterLane ? "A turning lane runs down the middle: traffic about to turn left waits there, out of the way of the rest.\n" : "";
                body += t.CenterLane ? "" : t.OneWay ? "Traffic runs the way you draw it. Bring it in alongside another road, on the right of that road's traffic, and it merges in; start it on one the same way and it peels off."
                    : t.Median > 0 ? "A grass median divides the two directions."
                    : "A double yellow line divides the two directions.";
                body += t.Group == RoadType.Street ? "\nWhere only streets meet, drivers get stop signs all round." : "\nWhere it meets another road it gets a traffic light.";
                body += "\nCurves no tighter than " + Mathf.RoundToInt((float)t.MinRadius * Feet / 10) * 10 + " ft.";
                tiles[i] = new DockRoad { Group = RoadType.GroupNames[t.Group], Title = t.Name, Body = body, Forward = t.Forward, Backward = t.Backward, Median = t.Median > 0, Turn = t.CenterLane };
            }
            return tiles;
        }

        private void SetUpScene()
        {
            cam = Camera.main;
            if (cam == null)
            {
                var go = new GameObject("Main Camera");
                go.tag = "MainCamera";
                cam = go.AddComponent<Camera>();
            }
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = MeshKit.Hex(0x659d48);
            cam.orthographic = false;
            cam.fieldOfView = 30;
            cam.nearClipPlane = 5;
            cam.farClipPlane = 6000;

            // Turn off whatever lights the open scene came with; the lab brings its own sun.
            foreach (UnityEngine.Light existing in Resources.FindObjectsOfTypeAll<UnityEngine.Light>())
            {
                if (existing.gameObject.scene.IsValid()) existing.enabled = false;
            }
            var sun = new GameObject("Sun").AddComponent<UnityEngine.Light>();
            sun.transform.SetParent(transform, false);
            sun.type = LightType.Directional;
            sun.color = MeshKit.Hex(0xfff1d6);
            // Sun and sky light together are set so that flat ground in the open shows a color at
            // about the brightness it was given: the textures are made to the colors meant for the
            // screen. (Measured from a screenshot: the brighter settings before this showed every
            // color half as bright again, which washed out the cars and burned the sidewalks white.)
            sun.intensity = MeshKit.Scriptable ? 0.85f : 0.9f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.55f;
            // A fairly low sun, so trees and trucks throw long shadows across the ground.
            sun.transform.rotation = Quaternion.LookRotation(new Vector3(150, -190, 110));

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = MeshKit.Scriptable ? MeshKit.Hex(0x737d8c) : MeshKit.Hex(0x4a5258);
            RenderSettings.fog = false;
            // Shadows should reach across the whole map. The setting lives in a different place under each pipeline.
            QualitySettings.shadowDistance = 3000;
            UnityEngine.Rendering.RenderPipelineAsset pipeline = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline;
            if (pipeline != null)
            {
                // The Universal Render Pipeline keeps it on its own asset; set it by name so this
                // script still compiles in a project that does not have that package.
                System.Reflection.PropertyInfo distance = pipeline.GetType().GetProperty("shadowDistance");
                if (distance != null && distance.CanWrite) distance.SetValue(pipeline, 3000f, null);
            }
        }

        /// <summary>Pull back far enough to see the whole track.</summary>
        private void Fit()
        {
            float aspect = (float)Screen.width / Mathf.Max(Screen.height, 1);
            float tan = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            float extent = (float)world.Extent * 1.08f;
            dist = Mathf.Max(extent / tan, extent / (tan * aspect));
            target = Vector3.zero;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void SpawnCars(int n)
        {
            int made = 0;
            for (int i = 0; i < n; i++) if (Sim.Spawn(newDriver, variation, PickVehicle()) != null) made++;
            if (made < n) Toast(made == 0 ? "The road is full." : "Only room for " + made + " more.");
        }

        /// <summary>Choose a vehicle for a new arrival, following the mix set in the control panel.</summary>
        private VehicleKind PickVehicle()
        {
            float r = Random.value;
            if (r < truckShare) return VehicleKind.Truck;
            if (r < truckShare + bikeShare) return VehicleKind.Motorcycle;
            return VehicleKind.Car;
        }

        private void Toast(string text)
        {
            toast = text;
            toastUntil = Time.unscaledTime + 2.4f;
        }

        private void Select(int id)
        {
            selected = id;
            if (id < 0) follow = false;
            else SelectNode(-1);
        }

        private void SelectNode(int id)
        {
            selectedNode = id;
            roads.ShowSelected(id >= 0 ? world.Nodes[id] : null);
        }

        /// <summary>The intersection under a point on the ground, or -1. Bends don't count.</summary>
        private int NodeAt(Vector3 ground)
        {
            foreach (Node node in world.Nodes)
            {
                if (!node.Controlled) continue;
                if (Geo.Hypot(ground.x - node.Pos.X, ground.z - node.Pos.Y) < node.Radius + 2) return node.Id;
            }
            return -1;
        }

        /// <summary>Handle a click on the scene: a car first, otherwise an intersection.</summary>
        private void ClickAt(Vector2 pixel)
        {
            int car = CarAt(pixel);
            if (car >= 0)
            {
                Select(car);
                return;
            }
            Select(-1);
            Ray ray = cam.ScreenPointToRay(new Vector3(pixel.x, Screen.height - pixel.y, 0));
            if (Mathf.Abs(ray.direction.y) < 1e-4f) return;
            SelectNode(NodeAt(ray.origin - ray.direction * (ray.origin.y / ray.direction.y)));
        }

        /// <summary>Something asked for from inside the on-screen controls, to do once they have finished drawing.</summary>
        private System.Action later;

        private void Update()
        {
            if (later != null)
            {
                System.Action todo = later;
                later = null;
                todo();
            }

            // New arrivals at the edge of the map follow the same settings as cars added by hand.
            // While building, only a handful of cars drive around.
            Sim.ArrivalsPerMinute = building ? 3 : level != null ? level.Arrivals : arrivals;
            // A level is complete once enough journeys have been made on roads that join every route within the budget.
            if (level != null && !building && stars == 0 && Sim.Arrived >= level.TripsToFinish && routesJoined == level.RouteCount) stars = level.Stars(Sim, drawing.Cost(), world);
            Sim.MaxCars = building ? AmbientCars : 320;
            Sim.ArrivalDriver = newDriver;
            Sim.ArrivalVariation = variation;
            Sim.ArrivalTrucks = truckShare;
            Sim.ArrivalMotorcycles = bikeShare;

            if (building || !paused)
            {
                owed += Mathf.Min(Time.unscaledDeltaTime, 0.1f) * (building ? 1 : speed);
                int steps = 0;
                while (owed >= Simulation.Dt && steps < 24)
                {
                    Sim.Step();
                    owed -= Simulation.Dt;
                    steps++;
                }
                if (owed > Simulation.Dt) owed = 0;
            }

            Car car = Sim.CarById(selected);
            if (car == null && selected >= 0) Select(-1);
            if (follow && car != null) target = Vector3.Lerp(target, MeshKit.ToWorld(car.X, car.Y), 0.12f);

            roads.Update(Sim.Junctions, Sim.Time);
            carViews.Sync(Sim.Cars, Sim.Time, selected);
            UpdateSensorLines();
            ShowGhost();

            MoveView(Mathf.Min(Time.unscaledDeltaTime, 0.1f));
            PlaceCamera();
        }

        // ------------------------------------------------------------ camera and clicks

        /// <summary>Put the camera where the view says: so far from the point looked at, leaning so much, facing such a way.</summary>
        private void PlaceCamera()
        {
            float sin = Mathf.Sin(yaw), cos = Mathf.Cos(yaw), back = dist * Mathf.Sin(tilt);
            cam.transform.position = target + new Vector3(-sin * back, dist * Mathf.Cos(tilt), -cos * back);
            cam.transform.LookAt(target, new Vector3(sin, 0, cos));
        }

        /// <summary>Zoom in or out, keeping the spot under the pointer (pixels, y down) under it.</summary>
        private void Zoom(float factor, Vector2 pixel)
        {
            Vec before, after;
            bool anchored = GroundAt(pixel, out before) && !follow;
            dist = Mathf.Clamp(dist * factor, 35, 2600);
            PlaceCamera();
            if (!anchored || !GroundAt(pixel, out after)) return;
            target.x += (float)(before.X - after.X);
            target.z += (float)(before.Y - after.Y);
            PlaceCamera();
        }

        /// <summary>Drag the view. dx and dy are in screen pixels, y down.</summary>
        private void Pan(float dx, float dy)
        {
            float perPixel = 2 * dist * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) / Mathf.Max(Screen.height, 1);
            float sin = Mathf.Sin(yaw), cos = Mathf.Cos(yaw);
            // Across the screen and up it, as directions on the ground.
            target -= new Vector3(cos, 0, -sin) * (dx * perPixel);
            target += new Vector3(sin, 0, cos) * (dy * perPixel / Mathf.Cos(tilt));
            follow = false;
        }

        /// <summary>Swing the view round the point it looks at, and lean it nearer to or further from straight down.</summary>
        private void Swing(float round, float lean)
        {
            yaw = Mathf.Repeat(yaw + round, 2 * Mathf.PI);
            tilt = Mathf.Clamp(tilt + lean, 0.1f, 1.05f);
        }

        /// <summary>Back to north up, at the usual lean.</summary>
        private void FaceNorth()
        {
            yaw = 0;
            tilt = 0.4f;
        }

        /// <summary>Move and turn the view for the keys held down: W, A, S, D or the arrows to move, Q and E to turn.</summary>
        private void MoveView(float dt)
        {
            if (held.Count == 0) return;
            float step = 0.9f * Screen.height * dt, dx = 0, dy = 0, round = 0;
            if (held.Contains(KeyCode.A) || held.Contains(KeyCode.LeftArrow)) dx += step;
            if (held.Contains(KeyCode.D) || held.Contains(KeyCode.RightArrow)) dx -= step;
            if (held.Contains(KeyCode.W) || held.Contains(KeyCode.UpArrow)) dy += step;
            if (held.Contains(KeyCode.S) || held.Contains(KeyCode.DownArrow)) dy -= step;
            if (held.Contains(KeyCode.Q)) round -= 1;
            if (held.Contains(KeyCode.E)) round += 1;
            if (dx != 0 || dy != 0) Pan(dx, dy);
            if (round != 0) Swing(round * 1.4f * dt, 0);
        }

        /// <summary>A key let go while the window is not looking would stay held for ever, so let them all go.</summary>
        private void OnApplicationFocus(bool focused)
        {
            if (!focused) held.Clear();
        }

        /// <summary>The car under a screen point (pixels, y down), or -1.</summary>
        private int CarAt(Vector2 pixel)
        {
            Ray ray = cam.ScreenPointToRay(new Vector3(pixel.x, Screen.height - pixel.y, 0));
            if (Mathf.Abs(ray.direction.y) < 1e-4f) return -1;
            float t = (0.7f - ray.origin.y) / ray.direction.y;
            Vector3 hit = ray.origin + ray.direction * t;
            int best = -1;
            double bestD = double.PositiveInfinity;
            foreach (Car c in Sim.Cars)
            {
                if (c.Height < -RoadView.Covered) continue; // out of sight in a tunnel
                double dx = hit.x - c.X;
                double dy = hit.z - c.Y;
                double along = dx * System.Math.Cos(c.H) + dy * System.Math.Sin(c.H);
                double across = -dx * System.Math.Sin(c.H) + dy * System.Math.Cos(c.H);
                if (System.Math.Abs(along) > c.Len / 2 + 0.6 || System.Math.Abs(across) > c.Wid / 2 + 0.6) continue;
                double d = dx * dx + dy * dy;
                if (d < bestD)
                {
                    bestD = d;
                    best = c.Id;
                }
            }
            return best;
        }

        // ------------------------------------------------------------ debug lines

        private Mesh lineMesh;
        private GameObject lineObject;
        private readonly List<Vector3> linePoints = new List<Vector3>();
        private readonly List<Color> lineColors = new List<Color>();
        private readonly List<int> lineIndices = new List<int>();

        /// <summary>
        /// Draw a line from each car to whatever it is reacting to. The lines are an ordinary
        /// mesh, so they draw the same under either render pipeline.
        /// </summary>
        private void UpdateSensorLines()
        {
            if (lineObject == null)
            {
                Shader shader = Shader.Find("Hidden/Internal-Colored");
                if (shader == null) shader = Shader.Find("Sprites/Default");
                lineMaterial = new Material(shader);
                // Blend over the scene and ignore depth, so the lines show on top of everything.
                lineMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                lineMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                lineMaterial.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
                lineMaterial.SetInt("_ZWrite", 0);
                lineMaterial.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
                lineMaterial.renderQueue = 4000;
                lineMesh = new Mesh();
                lineMesh.MarkDynamic();
                lineObject = MeshKit.Spawn("Sensor lines", lineMesh, lineMaterial, transform, false);
                lineObject.GetComponent<MeshRenderer>().receiveShadows = false;
            }
            bool bounded = Buildable && drawing != null && drawing.Bound > 0;
            lineObject.SetActive(showSensors || building || bounded);
            if (!showSensors && !building && !bounded) return;

            linePoints.Clear();
            lineColors.Clear();
            lineIndices.Clear();
            if (bounded)
            {
                // The edge of the ground a level lets you build on, as a broken line.
                double h = drawing.Bound;
                var edge = new Color(1, 1, 1, 0.75f);
                for (double t = -h; t < h; t += 16)
                {
                    double to = System.Math.Min(t + 9, h);
                    Segment(t, -h, to, -h, edge, 0);
                    Segment(t, h, to, h, edge, 0);
                    Segment(-h, t, -h, to, edge, 0);
                    Segment(h, t, h, to, edge, 0);
                }
            }
            if (building)
            {
                UploadLines();
                return;
            }
            if (!showSensors)
            {
                UploadLines();
                return;
            }
            var byId = new Dictionary<int, Car>();
            foreach (Car c in Sim.Cars) byId[c.Id] = c;
            foreach (Car car in Sim.Cars)
            {
                if (car.Crashed || car.Height < 0) continue;
                double fx = car.X + System.Math.Cos(car.H) * car.Len * 0.5;
                double fy = car.Y + System.Math.Sin(car.H) * car.Len * 0.5;
                Car lead;
                if (car.LeaderId >= 0 && car.Gap < 45 && byId.TryGetValue(car.LeaderId, out lead))
                {
                    double timeGap = car.Gap / System.Math.Max(car.V, 1);
                    Color c = timeGap < 0.8 ? new Color(1, 0.3f, 0.3f, 0.9f) : timeGap < 1.5 ? new Color(1, 0.78f, 0.25f, 0.9f) : new Color(0.3f, 0.9f, 0.55f, 0.9f);
                    Segment(fx, fy, lead.X - System.Math.Cos(lead.H) * lead.Len * 0.5, lead.Y - System.Math.Sin(lead.H) * lead.Len * 0.5, c, car.Height);
                }
                RoutePoint pt;
                if (car.StopDist < 70 && Sim.Ahead(car, System.Math.Max(car.StopDist, 0) + car.Len / 2, out pt))
                {
                    PathPose p = pt.Lane.Path.At(pt.S);
                    Color c = car.StopKind == Binding.Yield ? new Color(0.95f, 0.45f, 1, 0.9f) : new Color(0.55f, 0.8f, 1, 0.9f);
                    Segment(fx, fy, p.X, p.Y, c, car.Height);
                    double nx = System.Math.Sin(p.H) * 1.4;
                    double ny = -System.Math.Cos(p.H) * 1.4;
                    Segment(p.X - nx, p.Y - ny, p.X + nx, p.Y + ny, c, car.Height);
                }
            }
            UploadLines();
        }

        private void UploadLines()
        {
            lineMesh.Clear();
            lineMesh.SetVertices(linePoints);
            lineMesh.SetColors(lineColors);
            lineMesh.SetIndices(lineIndices, MeshTopology.Lines, 0);
            // Never cull the lines: they span the whole map.
            lineMesh.bounds = new Bounds(Vector3.zero, new Vector3(1e5f, 1e5f, 1e5f));
        }

        private void Segment(double x1, double y1, double x2, double y2, Color c, double height)
        {
            lineIndices.Add(linePoints.Count);
            lineIndices.Add(linePoints.Count + 1);
            linePoints.Add(MeshKit.ToWorld(x1, y1, 1.7f + (float)height));
            linePoints.Add(MeshKit.ToWorld(x2, y2, 1.7f + (float)height));
            lineColors.Add(c);
            lineColors.Add(c);
        }
    }
}
