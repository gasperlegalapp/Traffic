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
        private bool follow;
        private DriverParams newDriver = DriverParams.Normal;
        private float variation = 0.3f;
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
            world = LabWorld.Build();
            Sim = new Simulation(world, (uint)(System.DateTime.Now.Ticks & 0xffff));
            Sim.OnCrash = (a, b) => Toast("Crash: car " + a.Id + " and car " + b.Id + ".");
            roads = new RoadView(world, transform);
            carViews = new CarViews(transform);
            SetUpScene();
            Fit();
            SpawnCars(10);
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
            cam.backgroundColor = MeshKit.Hex(0x1c2622);
            cam.orthographic = false;
            cam.fieldOfView = 30;
            cam.nearClipPlane = 5;
            cam.farClipPlane = 4000;

            // Turn off whatever lights the open scene came with; the lab brings its own sun.
            foreach (UnityEngine.Light existing in Resources.FindObjectsOfTypeAll<UnityEngine.Light>())
            {
                if (existing.gameObject.scene.IsValid()) existing.enabled = false;
            }
            var sun = new GameObject("Sun").AddComponent<UnityEngine.Light>();
            sun.transform.SetParent(transform, false);
            sun.type = LightType.Directional;
            sun.color = MeshKit.Hex(0xfff1d6);
            sun.intensity = 0.8f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.6f;
            sun.transform.rotation = Quaternion.LookRotation(new Vector3(140, -260, 120));

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = MeshKit.Hex(0x4a5258);
            RenderSettings.fog = false;
            QualitySettings.shadowDistance = 1000;
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
            for (int i = 0; i < n; i++) if (Sim.Spawn(newDriver, variation) != null) made++;
            if (made < n) Toast(made == 0 ? "The road is full." : "Only room for " + made + " more.");
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
        }

        private void Update()
        {
            if (!paused)
            {
                owed += Mathf.Min(Time.unscaledDeltaTime, 0.1f) * speed;
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

            roads.Update(Sim.Junction, Sim.Time);
            carViews.Sync(Sim.Cars, Sim.Time, selected);

            cam.transform.position = target + new Vector3(0, dist * Mathf.Cos(Tilt), -dist * Mathf.Sin(Tilt));
            cam.transform.LookAt(target, Vector3.forward);
        }

        // ------------------------------------------------------------ camera and clicks

        private void Zoom(float factor)
        {
            dist = Mathf.Clamp(dist * factor, 35, 900);
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

        private void OnRenderObject()
        {
            if (!showSensors || Sim == null || Camera.current != cam) return;
            if (lineMaterial == null)
            {
                lineMaterial = new Material(Shader.Find("Hidden/Internal-Colored"));
                lineMaterial.hideFlags = HideFlags.HideAndDontSave;
                lineMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                lineMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                lineMaterial.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
                lineMaterial.SetInt("_ZWrite", 0);
                lineMaterial.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
            }
            lineMaterial.SetPass(0);
            GL.PushMatrix();
            GL.MultMatrix(Matrix4x4.identity);
            GL.Begin(GL.LINES);
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
                    Segment(fx, fy, lead.X - System.Math.Cos(lead.H) * lead.Len * 0.5, lead.Y - System.Math.Sin(lead.H) * lead.Len * 0.5, c);
                }
                RoutePoint pt;
                if (car.StopDist < 70 && Sim.Ahead(car, System.Math.Max(car.StopDist, 0) + car.Len / 2, out pt))
                {
                    PathPose p = pt.Lane.Path.At(pt.S);
                    Color c = car.StopKind == Binding.Yield ? new Color(0.95f, 0.45f, 1, 0.9f) : new Color(0.55f, 0.8f, 1, 0.9f);
                    Segment(fx, fy, p.X, p.Y, c);
                    double nx = System.Math.Sin(p.H) * 1.4;
                    double ny = -System.Math.Cos(p.H) * 1.4;
                    Segment(p.X - nx, p.Y - ny, p.X + nx, p.Y + ny, c);
                }
            }
            GL.End();
            GL.PopMatrix();
        }

        private static void Segment(double x1, double y1, double x2, double y2, Color c)
        {
            GL.Color(c);
            GL.Vertex(MeshKit.ToWorld(x1, y1, 1.7f));
            GL.Vertex(MeshKit.ToWorld(x2, y2, 1.7f));
        }
    }
}
