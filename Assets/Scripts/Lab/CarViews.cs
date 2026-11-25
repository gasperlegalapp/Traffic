using System.Collections.Generic;
using Traffic.Art;
using Traffic.Sim;
using UnityEngine;

namespace Traffic.Lab
{
    /// <summary>
    /// Draws every vehicle, with its lights working: brake lights that come on as it slows,
    /// turn signals that flash before a turn or a lane change, hazard lights after a crash and,
    /// after dark, headlights that light the road ahead and tail lights. Each light is a lamp
    /// on the vehicle and a glow round it, so it shows by day and from far overhead.
    /// Cars and trucks use the modeled art when it is there (see ArtKit); otherwise, and for
    /// motorcycles, plain shapes.
    /// </summary>
    public sealed class CarViews
    {
        private sealed class View
        {
            public GameObject Root;
            public Material Paint;
            public MeshRenderer[] Brakes, Left, Right, Heads;
            public MeshRenderer BrakeGlow, LeftGlow, RightGlow, Beam;
            /// <summary>True for a modeled vehicle. It has lamps of its own, so the added ones only show while lit.</summary>
            public bool Modeled;
            /// <summary>The brake lights are on. They stay on until the driver has all but stopped slowing, so they do not flicker.</summary>
            public bool Braking;
        }

        /// <summary>The glows for a vehicle of one size: they are the same for every vehicle that size, so they are made once.</summary>
        private sealed class Lights
        {
            public Mesh Brake, Left, Right, Beam;
        }

        private static readonly Dictionary<long, Lights> lightsBySize = new Dictionary<long, Lights>();

        private readonly Transform root;
        private readonly Dictionary<int, View> views = new Dictionary<int, View>();
        private readonly HashSet<int> seen = new HashSet<int>();
        private readonly List<int> gone = new List<int>();
        private readonly Transform ring;
        private readonly List<ArtModel> carModels = ArtKit.Named("car/");
        private readonly List<ArtModel> truckModels = ArtKit.Named("truck/");

        private readonly Mesh body = MeshKit.Box(4.5f, 0.8f, 1.9f);
        private readonly Mesh cabin = MeshKit.Box(2.3f, 0.6f, 1.62f);
        private readonly Mesh truckCab = MeshKit.Box(2.0f, 2.0f, 2.4f);
        private readonly Mesh truckBox = MeshKit.Box(6.1f, 2.7f, 2.5f);
        private readonly Mesh truckGlass = MeshKit.Box(0.5f, 0.7f, 2.2f);
        private readonly Mesh bikeBody = MeshKit.Box(2.0f, 0.5f, 0.4f);
        private readonly Mesh bikeRider = MeshKit.Box(0.6f, 0.8f, 0.6f);
        private readonly Mesh bikeLamp = MeshKit.Box(0.2f, 0.2f, 0.26f);
        private readonly Material cargo = MeshKit.Lit(MeshKit.Hex(0xdfe3e8), 0.2f);
        private readonly Material leather = MeshKit.Lit(MeshKit.Hex(0x22262b), 0.3f);
        private readonly Mesh lamp = MeshKit.Box(0.3f, 0.24f, 0.52f);
        private readonly Mesh corner = MeshKit.Box(0.36f, 0.24f, 0.36f);
        private readonly Material glass = MeshKit.Lit(MeshKit.Hex(0x161d26), 0.7f);
        private readonly Material brakeOn = MeshKit.Unlit(MeshKit.Hex(0xff2d2d));
        private readonly Material brakeOff = MeshKit.Unlit(MeshKit.Hex(0x5c1414));
        private readonly Material tailOn = MeshKit.Unlit(MeshKit.Hex(0xb01c1c));
        private readonly Material blinkOn = MeshKit.Unlit(MeshKit.Hex(0xffb020));
        private readonly Material blinkOff = MeshKit.Unlit(MeshKit.Hex(0x4a3a18));
        private readonly Material headlight = MeshKit.Unlit(MeshKit.Hex(0xfff6d8));
        private readonly Material headlightOff = MeshKit.Unlit(MeshKit.Hex(0xc9cdc6));
        // The light each lamp gives off. Its color is here; how bright it is by day and after dark is set in Night.
        private readonly Material brakeGlow = MeshKit.Glow(MeshKit.Hex(0xff2a1e));
        private readonly Material tailGlow = MeshKit.Glow(MeshKit.Hex(0xff2a1e));
        private readonly Material blinkGlow = MeshKit.Glow(MeshKit.Hex(0xffa514));
        private readonly Material beamGlow = MeshKit.Glow(MeshKit.Hex(0xfff0c8));

        private float night;

        /// <summary>
        /// How dark it is, from 0 in daylight to 1 at night. After dark every vehicle has its
        /// headlights and tail lights on. By day a glow has the sun to compete with, so it is
        /// kept a little weaker than it is at night, when it would otherwise seem dim.
        /// </summary>
        public float Night
        {
            get { return night; }
            set
            {
                night = Mathf.Clamp01(value);
                SetStrength(brakeGlow, Mathf.Lerp(0.78f, 1, night));
                SetStrength(blinkGlow, Mathf.Lerp(0.85f, 1, night));
                SetStrength(tailGlow, 0.34f * night);
                SetStrength(beamGlow, 0.5f * night);
            }
        }

        private static void SetStrength(Material glow, float strength)
        {
            Color c = glow.color;
            c.a = strength;
            glow.color = c;
        }

        public CarViews(Transform parent)
        {
            root = new GameObject("Cars").transform;
            root.SetParent(parent, false);
            Night = 0;

            // Selection ring.
            var inner = new List<Vec>();
            var outer = new List<Vec>();
            for (int i = 0; i <= 40; i++)
            {
                double a = System.Math.PI * 2 * i / 40;
                inner.Add(new Vec(System.Math.Cos(a) * 3.3, System.Math.Sin(a) * 3.3));
                outer.Add(new Vec(System.Math.Cos(a) * 3.8, System.Math.Sin(a) * 3.8));
            }
            var b = new FlatBuilder();
            b.Strip(inner, outer, 0);
            ring = MeshKit.Spawn("Selection", b.Build(), MeshKit.Unlit(MeshKit.Hex(0x5ee0c0)), root, false).transform;
            ring.gameObject.SetActive(false);
        }

        /// <summary>
        /// The glows for a vehicle whose lamps are so far forward, back and out to each side,
        /// and so high. The vehicle faces +x; its left is +z.
        /// </summary>
        private static Lights LightsFor(float front, float back, float inset, float edge, float lampY)
        {
            long key = ((long)Mathf.RoundToInt(front * 20) << 36) ^ ((long)Mathf.RoundToInt(inset * 20) << 24) ^ ((long)Mathf.RoundToInt(edge * 20) << 12) ^ (long)Mathf.RoundToInt(lampY * 20);
            Lights lights;
            // Made in an earlier run and thrown away since: make them again.
            if (lightsBySize.TryGetValue(key, out lights) && lights.Brake != null) return lights;
            var brake = new GlowBuilder();
            foreach (float z in new[] { inset, -inset }) brake.Ball(new Vector3(back - 0.22f, lampY, z), 1.05f, 1);
            var left = new GlowBuilder();
            var right = new GlowBuilder();
            foreach (float x in new[] { front - 0.1f, back + 0.1f })
            {
                left.Ball(new Vector3(x, lampY, edge + 0.12f), 0.95f, 1);
                right.Ball(new Vector3(x, lampY, -edge - 0.12f), 0.95f, 1);
            }
            // The headlights: a glow at each lamp, and the road ahead lit for a few car lengths.
            var beam = new GlowBuilder();
            foreach (float z in new[] { inset, -inset })
            {
                beam.Ball(new Vector3(front + 0.15f, lampY, z), 0.7f, 1);
                beam.Beam(new Vector3(front + 0.3f, 0.16f, z), 19, 0.9f, 3.4f, 0.8f);
            }
            lights = new Lights { Brake = brake.Build(), Left = left.Build(), Right = right.Build(), Beam = beam.Build() };
            lightsBySize[key] = lights;
            return lights;
        }

        private View Make(Car car)
        {
            var go = new GameObject("Car " + car.Id);
            go.transform.SetParent(root, false);
            List<ArtModel> models = car.Vehicle.Kind == VehicleKind.Truck ? truckModels : car.Vehicle.Kind == VehicleKind.Car ? carModels : null;
            ArtModel art = models != null && models.Count > 0 ? models[car.Id % models.Count] : null;
            var v = new View { Root = go, Modeled = art != null, Paint = art != null ? ArtKit.NewPaint(MeshKit.Hex(car.Color)) : MeshKit.Lit(MeshKit.Hex(car.Color), 0.5f) };
            float halfLen = (float)car.Len / 2;
            float halfWid = (float)car.Wid / 2;
            float lampY = 0.86f;
            Mesh lampMesh = lamp;
            if (art != null)
            {
                // The model faces +x and is centered. Its first part takes the paint.
                GameObject body = MeshKit.Spawn("Body", ArtKit.MeshOf(art), v.Paint, go.transform);
                body.GetComponent<MeshRenderer>().sharedMaterials = new[] { v.Paint, ArtKit.Shared };
                // The lamps go on the model's own ends, which are a little inside the simulation's.
                halfLen = art.SizeX / 2;
                halfWid = art.SizeZ / 2;
                lampY = art.SizeY * 0.42f;
            }
            else if (car.Vehicle.Kind == VehicleKind.Truck)
            {
                // A painted cab in front of a pale cargo box.
                MeshKit.Spawn("Cab", truckCab, v.Paint, go.transform).transform.localPosition = new Vector3(halfLen - 1.0f, 1.25f, 0);
                MeshKit.Spawn("Windshield", truckGlass, glass, go.transform).transform.localPosition = new Vector3(halfLen - 0.3f, 1.75f, 0);
                MeshKit.Spawn("Cargo", truckBox, cargo, go.transform).transform.localPosition = new Vector3(-1.15f, 1.65f, 0);
                lampY = 0.7f;
            }
            else if (car.Vehicle.Kind == VehicleKind.Motorcycle)
            {
                MeshKit.Spawn("Body", bikeBody, v.Paint, go.transform).transform.localPosition = new Vector3(0, 0.55f, 0);
                MeshKit.Spawn("Rider", bikeRider, leather, go.transform).transform.localPosition = new Vector3(-0.15f, 1.15f, 0);
                lampY = 0.75f;
                lampMesh = bikeLamp;
            }
            else
            {
                MeshKit.Spawn("Body", body, v.Paint, go.transform).transform.localPosition = new Vector3(0, 0.6f, 0);
                MeshKit.Spawn("Cabin", cabin, glass, go.transform).transform.localPosition = new Vector3(-0.3f, 1.15f, 0);
            }

            // The model faces +x, so the driver's left is +z.
            MeshRenderer Lamp(Mesh mesh, Material mat, float x, float z)
            {
                GameObject l = MeshKit.Spawn("Lamp", mesh, mat, go.transform, false);
                l.transform.localPosition = new Vector3(x, lampY, z);
                return l.GetComponent<MeshRenderer>();
            }
            MeshRenderer Light(Mesh mesh, Material mat)
            {
                MeshRenderer glow = MeshKit.Spawn("Light", mesh, mat, go.transform, false).GetComponent<MeshRenderer>();
                glow.receiveShadows = false;
                glow.enabled = false;
                return glow;
            }
            // The lamps stand a little proud of the ends of the vehicle, so that they show from above.
            float back = -halfLen - 0.04f;
            float front = halfLen + 0.04f;
            float inset = halfWid * 0.5f;
            float edge = halfWid - 0.12f;
            v.Brakes = new[] { Lamp(lampMesh, brakeOff, back, inset), Lamp(lampMesh, brakeOff, back, -inset) };
            v.Left = new[] { Lamp(corner, blinkOff, front - 0.1f, edge), Lamp(corner, blinkOff, back + 0.1f, edge) };
            v.Right = new[] { Lamp(corner, blinkOff, front - 0.1f, -edge), Lamp(corner, blinkOff, back + 0.1f, -edge) };
            v.Heads = new[] { Lamp(lampMesh, headlightOff, front, inset), Lamp(lampMesh, headlightOff, front, -inset) };
            Lights lights = LightsFor(front, back, inset, edge, lampY);
            v.BrakeGlow = Light(lights.Brake, brakeGlow);
            v.LeftGlow = Light(lights.Left, blinkGlow);
            v.RightGlow = Light(lights.Right, blinkGlow);
            v.Beam = Light(lights.Beam, beamGlow);
            return v;
        }

        /// <summary>Show a lamp or a glow, or hide it, touching it only when that changes.</summary>
        private static void Show(MeshRenderer r, bool shown, Material material)
        {
            if (r.enabled != shown) r.enabled = shown;
            if (shown && r.sharedMaterial != material) r.sharedMaterial = material;
        }

        private static void SetLamps(View v, MeshRenderer[] lamps, bool lit, Material on, Material off)
        {
            foreach (MeshRenderer r in lamps) Show(r, lit || !v.Modeled, lit ? on : off);
        }

        /// <summary>Remove everything this view created, before loading another map.</summary>
        public void Destroy()
        {
            foreach (View v in views.Values) Object.Destroy(v.Paint);
            views.Clear();
            Object.Destroy(root.gameObject);
            // The lamps and their light belong to this view alone; the glow shapes are shared and kept.
            foreach (Material m in new[] { cargo, leather, glass, brakeOn, brakeOff, tailOn, blinkOn, blinkOff, headlight, headlightOff, brakeGlow, tailGlow, blinkGlow, beamGlow }) Object.Destroy(m);
            foreach (Mesh m in new[] { body, cabin, truckCab, truckBox, truckGlass, bikeBody, bikeRider, bikeLamp, lamp, corner }) Object.Destroy(m);
        }

        public void Sync(List<Car> cars, double time, int selectedId)
        {
            seen.Clear();
            bool blink = time * 1.6 % 1 < 0.5;
            bool dark = night > 0.5f;
            ring.gameObject.SetActive(false);
            foreach (Car car in cars)
            {
                seen.Add(car.Id);
                View v;
                if (!views.TryGetValue(car.Id, out v))
                {
                    v = Make(car);
                    views[car.Id] = v;
                }
                // In a tunnel a car stays in view on the way down, then is out of sight until it comes back up.
                bool hidden = car.Height < -RoadView.Covered;
                if (v.Root.activeSelf == hidden) v.Root.SetActive(!hidden);
                v.Root.transform.SetPositionAndRotation(MeshKit.ToWorld(car.X, car.Y, Mathf.Max(0, (float)car.Height)), MeshKit.Heading(car.H));

                // Brake lights: on while the driver is slowing down or standing still, and they stay on
                // through the moments when the foot only eases, as a real driver's do.
                bool stopped = car.V < 0.2 && !car.Crashed;
                v.Braking = stopped || car.A < -0.5 || (v.Braking && car.A < -0.12);
                SetLamps(v, v.Brakes, v.Braking || dark, v.Braking ? brakeOn : tailOn, brakeOff);
                Show(v.BrakeGlow, v.Braking || dark, v.Braking ? brakeGlow : tailGlow);

                // Turn signals, by day and by night; after a crash, both sides flash.
                bool hazard = car.Crashed && blink;
                bool left = hazard || (car.Blinker < 0 && blink), right = hazard || (car.Blinker > 0 && blink);
                SetLamps(v, v.Left, left, blinkOn, blinkOff);
                SetLamps(v, v.Right, right, blinkOn, blinkOff);
                Show(v.LeftGlow, left, blinkGlow);
                Show(v.RightGlow, right, blinkGlow);

                // Headlights after dark. A wreck's are out.
                bool lit = dark && !(car.Crashed && car.CrashMajor);
                SetLamps(v, v.Heads, lit, headlight, headlightOff);
                Show(v.Beam, lit, beamGlow);

                Color paint = MeshKit.Hex(car.Color);
                if (car.Flash && time * 3 % 1 < 0.5) paint = Color.Lerp(paint, Color.white, 0.8f);
                v.Paint.color = !car.Crashed ? paint : car.CrashMajor ? paint * 0.35f : paint * 0.75f;
                if (car.Id == selectedId)
                {
                    ring.gameObject.SetActive(true);
                    ring.position = MeshKit.ToWorld(car.X, car.Y, 0.12f + Mathf.Max(0, (float)car.Height));
                    float size = Mathf.Max(1, (float)car.Len / 4.5f);
                    ring.localScale = new Vector3(size, 1, size);
                }
            }
            gone.Clear();
            foreach (int id in views.Keys) if (!seen.Contains(id)) gone.Add(id);
            foreach (int id in gone)
            {
                Object.Destroy(views[id].Paint);
                Object.Destroy(views[id].Root);
                views.Remove(id);
            }
        }
    }
}
