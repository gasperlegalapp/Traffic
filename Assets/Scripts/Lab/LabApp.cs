using System.Collections.Generic;
using Traffic.Sim;
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
        /// <summary>Camera tilt away from straight down, radians.</summary>
        private const float Tilt = 0.4f;

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
        /// <summary>The map index that means "the one the player is drawing".</summary>
        private static int OwnMap => Maps.All.Length;

        private EditableMap drawing;
        /// <summary>True while laying out roads; traffic only runs once this is off.</summary>
        private bool building;
        private bool bulldozing;
        private int laneChoice = 1;
        private int speedChoice = 1;
        /// <summary>0 draws straight roads, 1 draws curves.</summary>
        private int shape;
        private bool hasStart;
        private Vec chainStart;
        /// <summary>For a curve, the corner it bends round, once that has been clicked.</summary>
        private bool hasControl;
        private Vec controlPoint;
        private Vector2 hoverPixel;
        /// <summary>Earlier states of the drawing, for undo and redo.</summary>
        private readonly List<string> undo = new List<string>();
        private readonly List<string> redo = new List<string>();
        // The check on the road about to be placed is only redone when something has changed.
        private int editVersion, previewVersion = -1;
        private Vec previewAt;
        private bool previewOk;

        private int DrawLanes => EditableMap.LaneChoices[laneChoice];
        private int DrawMph => EditableMap.SpeedChoices[speedChoice];

        /// <summary>Open the player's own map, as it was last left.</summary>
        private void LoadOwnMap()
        {
            mapIndex = OwnMap;
            drawing = EditableMap.Parse(PlayerPrefs.GetString(SaveKey, ""));
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
            editVersion++;
            if (!building)
            {
                // A few cars to start with, wherever there is room; the rest arrive at the open road ends.
                int starters = Mathf.Min(10, drawing.Segments.Count * 2);
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
            hasStart = false;
            hasControl = false;
            editVersion++;
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
            drawing = EditableMap.Parse(from[from.Count - 1]);
            from.RemoveAt(from.Count - 1);
            StopDrawing();
            SaveDrawing();
            RebuildDrawing(true);
        }

        /// <summary>A line of help about what the next click will do.</summary>
        private string BuildHint()
        {
            if (bulldozing) return "Click a road to remove it.";
            if (!hasStart) return drawing.Segments.Count == 0 ? "Click where the road starts. An open road end is where traffic comes in." : "Click where the road starts. Touch or cross another road to make an intersection.";
            if (shape == 1 && !hasControl) return "Click the corner the road should bend round. Right-click to go back.";
            return "Click where the road ends. Right-click to stop.";
        }

        private void SaveDrawing()
        {
            PlayerPrefs.SetString(SaveKey, drawing.Serialize());
            PlayerPrefs.Save();
        }

        private void SetBuilding(bool on)
        {
            building = on;
            paused = false;
            StopDrawing();
            RebuildDrawing(false);
            if (!on && world.Entries.Count == 0 && drawing.Segments.Count > 0) Toast("No road runs off the map, so no traffic will arrive. Leave a road end open.");
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

        /// <summary>A click on the ground while building: place the next point of a road, or bulldoze one.</summary>
        private void EditClick(Vector2 pixel)
        {
            Vec p;
            if (!GroundAt(pixel, out p)) return;
            string before = drawing.Serialize();
            editVersion++;
            if (bulldozing)
            {
                if (drawing.RemoveNear(p)) DrawingChanged(before);
                return;
            }
            if (!hasStart)
            {
                chainStart = drawing.SnapTo(p).Point;
                hasStart = true;
                hasControl = false;
                return;
            }
            if (shape == 1 && !hasControl)
            {
                controlPoint = p;
                hasControl = true;
                return;
            }
            string problem = drawing.TryAdd(chainStart, p, DrawLanes, DrawMph, true, shape == 1 ? controlPoint : (Vec?)null);
            if (problem != null)
            {
                Toast(problem);
                return;
            }
            // Carry on from where this stretch ended, so a road can be drawn as a chain of clicks.
            chainStart = drawing.SnapTo(p).Point;
            hasControl = false;
            DrawingChanged(before);
        }

        /// <summary>Right-click or Esc while drawing: go back one click.</summary>
        private void StepBack()
        {
            if (hasControl) hasControl = false;
            else hasStart = false;
            editVersion++;
        }

        /// <summary>Guide lines while building: a mark on every point, and the road about to be placed.</summary>
        private void GuideLines()
        {
            var faint = new Color(1, 1, 1, 0.6f);
            foreach (Vec n in drawing.Nodes) Mark(n, 2.5, faint);

            Vec hover;
            if (!GroundAt(hoverPixel, out hover)) return;
            if (bulldozing)
            {
                Vec on;
                int seg = drawing.NearestSegment(hover, EditableMap.SnapRoad * 1.5, out on);
                if (seg < 0) return;
                Polyline(drawing.Trace(drawing.Segments[seg]), new Color(1, 0.3f, 0.3f, 1));
                Mark(on, 4, new Color(1, 0.3f, 0.3f, 1));
                return;
            }
            Vec end = drawing.SnapTo(hover).Point;
            // White shows up on both grass and asphalt.
            var good = new Color(1, 1, 1, 1);
            if (!hasStart)
            {
                Mark(end, 4, good);
                return;
            }
            if (shape == 1 && !hasControl)
            {
                // Choosing the corner of a curve: show the way the road sets off.
                Segment(chainStart.X, chainStart.Y, hover.X, hover.Y, faint, 0);
                Mark(chainStart, 4, good);
                Mark(hover, 3, good);
                return;
            }
            bool curved = shape == 1;
            if (previewVersion != editVersion || Geo.Hypot(hover.X - previewAt.X, hover.Y - previewAt.Y) > 0.4)
            {
                previewOk = drawing.TryAdd(chainStart, hover, DrawLanes, DrawMph, false, curved ? controlPoint : (Vec?)null) == null;
                previewVersion = editVersion;
                previewAt = hover;
            }
            Color c = previewOk ? good : new Color(1, 0.3f, 0.3f, 1);
            Vec[] line = EditableMap.Trace(chainStart, end, curved, controlPoint, 24);
            Polyline(line, c);
            // Show how wide it will be. A single lane lies to the right of the line drawn.
            if (Geo.Hypot(end.X - chainStart.X, end.Y - chainStart.Y) > 1)
            {
                double side = (DrawLanes == 4 ? 2 : 1) * NetworkBuilder.LaneW;
                Polyline(Geo.Offset(line, side), c);
                if (DrawLanes != 1) Polyline(Geo.Offset(line, -side), c);
            }
            if (curved)
            {
                Segment(chainStart.X, chainStart.Y, controlPoint.X, controlPoint.Y, faint, 0);
                Segment(controlPoint.X, controlPoint.Y, end.X, end.Y, faint, 0);
                Mark(controlPoint, 3, faint);
            }
            Mark(chainStart, 4, c);
            Mark(end, 4, c);
        }

        private void Polyline(Vec[] pts, Color c)
        {
            for (int i = 0; i + 1 < pts.Length; i++) Segment(pts[i].X, pts[i].Y, pts[i + 1].X, pts[i + 1].Y, c, 0);
        }

        private void Mark(Vec p, double size, Color c)
        {
            Segment(p.X - size, p.Y, p.X + size, p.Y, c, 0);
            Segment(p.X, p.Y - size, p.X, p.Y + size, c, 0);
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
            cam.backgroundColor = MeshKit.Hex(0x7c9f58);
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
            // The same numbers come out darker under URP than under the built-in pipeline, so each gets its own.
            sun.intensity = MeshKit.Scriptable ? 1.4f : 0.9f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.55f;
            // A fairly low sun, so trees and trucks throw long shadows across the ground.
            sun.transform.rotation = Quaternion.LookRotation(new Vector3(150, -190, 110));

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = MeshKit.Scriptable ? MeshKit.Hex(0x8e99a3) : MeshKit.Hex(0x4a5258);
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
            Sim.ArrivalsPerMinute = building ? 3 : arrivals;
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

            cam.transform.position = target + new Vector3(0, dist * Mathf.Cos(Tilt), -dist * Mathf.Sin(Tilt));
            cam.transform.LookAt(target, Vector3.forward);
        }

        // ------------------------------------------------------------ camera and clicks

        private void Zoom(float factor)
        {
            dist = Mathf.Clamp(dist * factor, 35, 2600);
        }

        /// <summary>Drag the view. dx and dy are in screen pixels, y down.</summary>
        private void Pan(float dx, float dy)
        {
            float perPixel = 2 * dist * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) / Mathf.Max(Screen.height, 1);
            target.x -= dx * perPixel;
            target.z += dy * perPixel / Mathf.Cos(Tilt);
            follow = false;
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
            lineObject.SetActive(showSensors || building);
            if (!showSensors && !building) return;

            linePoints.Clear();
            lineColors.Clear();
            lineIndices.Clear();
            if (building)
            {
                GuideLines();
                UploadLines();
                return;
            }
            var byId = new Dictionary<int, Car>();
            foreach (Car c in Sim.Cars) byId[c.Id] = c;
            foreach (Car car in Sim.Cars)
            {
                if (car.Crashed) continue;
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
