using System.Collections.Generic;
using Traffic.Art;
using Traffic.Sim;
using UnityEngine;

namespace Traffic.Lab
{
    /// <summary>
    /// Draws every vehicle, with its lights working: brake lights that come on as it slows,
    /// turn signals that flash before a turn or a lane change, hazard lights after a crash and,
    /// after dark, headlights that light the road ahead and tail lights. The lights are a
    /// small detail, not a show: each is a lamp set into the end of the vehicle that changes
    /// color, hardly noticed by day, and only after dark does it have a faint glow round it.
    /// (The first version had big bright glows by day, standing off the car and all flashing
    /// in step. The player found that very annoying.)
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
            public MeshRenderer BrakeGlow, Beam;
            /// <summary>The trailer, for a vehicle that pulls one: it is placed by itself, behind the tractor.</summary>
            public Transform Tow;
            /// <summary>For a school bus: the stop sign that swings out from its side, and the red lamps high on its ends that flash in turn, while it is taking children on.</summary>
            public GameObject StopArm;
            public MeshRenderer[] Flashers;
            /// <summary>True for a modeled vehicle. It has lamps of its own, so the added ones only show while lit.</summary>
            public bool Modeled;
            /// <summary>The brake lights are on. They stay on until the driver has all but stopped slowing, so they do not flicker.</summary>
            public bool Braking;
        }

        /// <summary>The glows of lamps so far along a vehicle, so far out and so high: the same for every vehicle that size, so they are made once.</summary>
        private static readonly Dictionary<long, Mesh> glows = new Dictionary<long, Mesh>();

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
        private readonly Mesh bikeLamp = MeshKit.Box(0.08f, 0.12f, 0.16f);
        private readonly Mesh stopArm = StopArmMesh();
        private readonly Material tire = MeshKit.Lit(MeshKit.Hex(0x1c1e22), 0.15f);
        private readonly Material metal = MeshKit.Lit(MeshKit.Hex(0xa3aab2), 0.6f);
        private readonly Material pale = MeshKit.Lit(MeshKit.Hex(0xe9ecef), 0.25f);
        private readonly Material cargo = MeshKit.Lit(MeshKit.Hex(0xdfe3e8), 0.2f);
        private readonly Material leather = MeshKit.Lit(MeshKit.Hex(0x22262b), 0.3f);
        // Thin plates that lie against the end of the vehicle, where its own lamps are.
        private readonly Mesh lamp = MeshKit.Box(0.08f, 0.16f, 0.4f);
        private readonly Mesh corner = MeshKit.Box(0.1f, 0.14f, 0.2f);
        private readonly Material glass = MeshKit.Lit(MeshKit.Hex(0x161d26), 0.7f);
        private readonly Material brakeOn = MeshKit.Unlit(MeshKit.Hex(0xe23a32));
        private readonly Material brakeOff = MeshKit.Unlit(MeshKit.Hex(0x5c1414));
        private readonly Material tailOn = MeshKit.Unlit(MeshKit.Hex(0x9a2020));
        private readonly Material blinkOn = MeshKit.Unlit(MeshKit.Hex(0xf2a524));
        private readonly Material blinkOff = MeshKit.Unlit(MeshKit.Hex(0x4a3a18));
        private readonly Material headlight = MeshKit.Unlit(MeshKit.Hex(0xfff6d8));
        private readonly Material headlightOff = MeshKit.Unlit(MeshKit.Hex(0xc9cdc6));
        // The light each lamp gives off. Its color is here; how bright it is by day and after dark is set in Night.
        private readonly Material brakeGlow = MeshKit.Glow(MeshKit.Hex(0xff2a1e));
        private readonly Material tailGlow = MeshKit.Glow(MeshKit.Hex(0xff2a1e));
        private readonly Material beamGlow = MeshKit.Glow(MeshKit.Hex(0xfff0c8));

        private float night;

        /// <summary>
        /// How dark it is, from 0 in daylight to 1 at night. After dark every vehicle has its
        /// headlights and tail lights on, and a lit lamp has a faint glow. By day there is no
        /// glow at all: a lamp only changes color.
        /// </summary>
        public float Night
        {
            get { return night; }
            set
            {
                night = Mathf.Clamp01(value);
                SetStrength(brakeGlow, 0.45f * night);
                SetStrength(tailGlow, 0.18f * night);
                SetStrength(beamGlow, 0.42f * night);
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
        /// The glow of a pair of lamps at one end of a vehicle, so far along it, so far out to
        /// each side and so high. At the front it comes with the light the lamps throw on the road.
        /// </summary>
        private static Mesh GlowAt(float x, float out_, float y, bool front)
        {
            long key = ((long)Mathf.RoundToInt(x * 20 + 2000) << 36) ^ ((long)Mathf.RoundToInt(out_ * 20) << 24) ^ ((long)Mathf.RoundToInt(y * 20) << 12) ^ (front ? 1 : 0);
            Mesh made;
            // Made in an earlier run and thrown away since: make it again.
            if (glows.TryGetValue(key, out made) && made != null) return made;
            var b = new GlowBuilder();
            foreach (float z in out_ > 0.05f ? new[] { out_, -out_ } : new[] { 0f })
            {
                // Small, and right at the lamp: seen after dark only.
                if (!front) b.Ball(new Vector3(x - 0.05f, y, z), 0.5f, 1);
                else
                {
                    b.Ball(new Vector3(x + 0.05f, y, z), 0.45f, 1);
                    b.Beam(new Vector3(x + 0.3f, 0.16f, z), 17, 0.8f, 3.0f, 0.8f);
                }
            }
            glows[key] = made = b.Build();
            return made;
        }

        /// <summary>The eight-sided stop sign a school bus swings out, lying flat until it is stood up.</summary>
        private static Mesh StopArmMesh()
        {
            var ring = new List<Vec>();
            for (int i = 0; i < 8; i++)
            {
                double a = System.Math.PI / 8 + System.Math.PI / 4 * i;
                ring.Add(new Vec(System.Math.Cos(a) * 0.45, System.Math.Sin(a) * 0.45));
            }
            var b = new FlatBuilder();
            b.Fan(new Vec(0, 0), ring, 0);
            return b.Build();
        }

        /// <summary>Put a built shape under a parent, each of its parts in what it is made of.</summary>
        private void Dress(Transform parent, VehicleShape shape, Material paint, Material rider, Material livery)
        {
            if (shape.Paint != null) MeshKit.Spawn("Paint", shape.Paint, paint, parent);
            if (shape.Dark != null) MeshKit.Spawn("Dark", shape.Dark, tire, parent);
            if (shape.Metal != null) MeshKit.Spawn("Metal", shape.Metal, metal, parent);
            if (shape.Glass != null) MeshKit.Spawn("Glass", shape.Glass, glass, parent);
            if (shape.Pale != null) MeshKit.Spawn("Pale", shape.Pale, pale, parent);
            if (shape.Rider != null) MeshKit.Spawn("Rider", shape.Rider, rider, parent);
            if (shape.Panels != null && livery != null) MeshKit.Spawn("Name", shape.Panels, livery, parent, false);
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
            // Where the lamps go: on the front of the vehicle, and on its back, which for a tractor and trailer is another body.
            Transform frontBody = go.transform, backBody = go.transform;
            float front = halfLen - 0.01f, back = -halfLen + 0.01f, inset = halfWid * 0.5f, edge = halfWid - 0.14f, backLampY = -1;
            VehicleKind kind = car.Vehicle.Kind;
            if (art != null)
            {
                // The model faces +x and is centered. Its first part takes the paint.
                GameObject body = MeshKit.Spawn("Body", ArtKit.MeshOf(art), v.Paint, go.transform);
                body.GetComponent<MeshRenderer>().sharedMaterials = new[] { v.Paint, ArtKit.Shared };
                // The lamps go on the model's own ends, which are a little inside the simulation's.
                halfLen = art.SizeX / 2;
                halfWid = art.SizeZ / 2;
                lampY = art.SizeY * 0.42f;
                front = halfLen - 0.01f;
                back = -halfLen + 0.01f;
                inset = halfWid * 0.5f;
                edge = halfWid - 0.14f;
            }
            else if (kind == VehicleKind.SchoolBus)
            {
                v.Modeled = true;
                VehicleShape bus = VehicleShape.SchoolBus;
                Dress(go.transform, bus, v.Paint, leather, null);
                front = bus.Front - 0.01f;
                back = bus.Back + 0.01f;
                lampY = backLampY = bus.LampY;
                inset = bus.LampOut;
                edge = halfWid - 0.16f;
                // The stop sign on the driver's side, facing the traffic ahead and behind; and two red lamps high on each end.
                v.StopArm = MeshKit.Spawn("Stop arm", stopArm, brakeOn, go.transform, false);
                v.StopArm.transform.localPosition = new Vector3(1.6f, 1.9f, halfWid + 0.5f);
                v.StopArm.transform.localRotation = Quaternion.Euler(0, 0, 90);
                v.StopArm.SetActive(false);
                v.Flashers = new MeshRenderer[4];
                for (int i = 0; i < 4; i++)
                {
                    GameObject flasher = MeshKit.Spawn("Flasher", lamp, brakeOn, go.transform, false);
                    flasher.transform.localPosition = new Vector3(i < 2 ? front - 1.25f : back, 2.82f, i % 2 == 0 ? 0.8f : -0.8f);
                    v.Flashers[i] = flasher.GetComponent<MeshRenderer>();
                    v.Flashers[i].enabled = false;
                }
            }
            else if (kind == VehicleKind.Motorcycle || kind == VehicleKind.BoxTruck || kind == VehicleKind.Semi)
            {
                // Built from boxes and drums (see VehicleShape). A truck carries the name of one of the made-up companies.
                v.Modeled = true;
                bool tanker = kind == VehicleKind.Semi && car.Id % 3 == 0;
                List<Livery> names = Livery.Of(tanker);
                Material name = kind == VehicleKind.Motorcycle ? null : VehicleShape.LiveryMaterial(names[(car.Id / 3) % names.Count]);
                VehicleShape shape = kind == VehicleKind.Motorcycle ? VehicleShape.Motorcycle : kind == VehicleKind.BoxTruck ? VehicleShape.BoxTruck : VehicleShape.Tractor;
                Dress(go.transform, shape, v.Paint, leather, name);
                VehicleShape rear = shape;
                if (kind == VehicleKind.Semi)
                {
                    rear = tanker ? VehicleShape.TankTrailer : VehicleShape.BoxTrailer;
                    v.Tow = new GameObject("Trailer " + car.Id).transform;
                    v.Tow.SetParent(root, false);
                    Dress(v.Tow, rear, v.Paint, leather, name);
                    backBody = v.Tow;
                }
                front = shape.Front - 0.01f;
                back = rear.Back + 0.01f;
                lampY = shape.LampY;
                backLampY = rear.LampY;
                inset = shape.LampOut;
                edge = kind == VehicleKind.Motorcycle ? 0.16f : halfWid - 0.16f;
                if (kind == VehicleKind.Motorcycle) lampMesh = bikeLamp;
            }
            else if (kind == VehicleKind.Truck)
            {
                // A painted cab in front of a pale cargo box.
                MeshKit.Spawn("Cab", truckCab, v.Paint, go.transform).transform.localPosition = new Vector3(halfLen - 1.0f, 1.25f, 0);
                MeshKit.Spawn("Windshield", truckGlass, glass, go.transform).transform.localPosition = new Vector3(halfLen - 0.3f, 1.75f, 0);
                MeshKit.Spawn("Cargo", truckBox, cargo, go.transform).transform.localPosition = new Vector3(-1.15f, 1.65f, 0);
                lampY = 0.7f;
            }
            else
            {
                MeshKit.Spawn("Body", body, v.Paint, go.transform).transform.localPosition = new Vector3(0, 0.6f, 0);
                MeshKit.Spawn("Cabin", cabin, glass, go.transform).transform.localPosition = new Vector3(-0.3f, 1.15f, 0);
            }
            if (backLampY < 0) backLampY = lampY;

            // The model faces +x, so the driver's left is +z.
            MeshRenderer Lamp(Transform on, Mesh mesh, Material mat, float x, float y, float z)
            {
                GameObject l = MeshKit.Spawn("Lamp", mesh, mat, on, false);
                l.transform.localPosition = new Vector3(x, y, z);
                return l.GetComponent<MeshRenderer>();
            }
            MeshRenderer Light(Transform on, Mesh mesh, Material mat)
            {
                MeshRenderer glow = MeshKit.Spawn("Light", mesh, mat, on, false).GetComponent<MeshRenderer>();
                glow.receiveShadows = false;
                glow.enabled = false;
                return glow;
            }
            // The lamps lie against the ends of the vehicle, where its own are: a plate a few centimeters proud of the bodywork.
            // A motorcycle has one of each, in the middle.
            if (inset > 0.05f)
            {
                v.Brakes = new[] { Lamp(backBody, lampMesh, brakeOff, back, backLampY, inset), Lamp(backBody, lampMesh, brakeOff, back, backLampY, -inset) };
                v.Heads = new[] { Lamp(frontBody, lampMesh, headlightOff, front, lampY, inset), Lamp(frontBody, lampMesh, headlightOff, front, lampY, -inset) };
            }
            else
            {
                v.Brakes = new[] { Lamp(backBody, lampMesh, brakeOff, back, backLampY, 0) };
                v.Heads = new[] { Lamp(frontBody, lampMesh, headlightOff, front, lampY + 0.1f, 0) };
            }
            v.Left = new[] { Lamp(frontBody, corner, blinkOff, front, lampY, edge), Lamp(backBody, corner, blinkOff, back, backLampY, edge) };
            v.Right = new[] { Lamp(frontBody, corner, blinkOff, front, lampY, -edge), Lamp(backBody, corner, blinkOff, back, backLampY, -edge) };
            v.BrakeGlow = Light(backBody, GlowAt(back, inset, backLampY, false), brakeGlow);
            v.Beam = Light(frontBody, GlowAt(front, inset, lampY, true), beamGlow);
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
            // The lamps and their light belong to this view alone; the glow shapes and the built vehicles are shared and kept.
            foreach (Material m in new[] { cargo, leather, glass, tire, metal, pale, brakeOn, brakeOff, tailOn, blinkOn, blinkOff, headlight, headlightOff, brakeGlow, tailGlow, beamGlow }) Object.Destroy(m);
            foreach (Mesh m in new[] { body, cabin, truckCab, truckBox, truckGlass, bikeLamp, lamp, corner, stopArm }) Object.Destroy(m);
        }

        public void Sync(List<Car> cars, double time, int selectedId)
        {
            seen.Clear();
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
                float high = Mathf.Max(0, (float)car.Height);
                if (v.Tow == null) v.Root.transform.SetPositionAndRotation(MeshKit.ToWorld(car.X, car.Y, high), MeshKit.Heading(car.H));
                else
                {
                    // A tractor and its trailer each lie along their own stretch of the road, joined where they meet.
                    if (v.Tow.gameObject.activeSelf == hidden) v.Tow.gameObject.SetActive(!hidden);
                    v.Root.transform.SetPositionAndRotation(MeshKit.ToWorld(car.CabX, car.CabY, high), MeshKit.Heading(car.CabH));
                    v.Tow.SetPositionAndRotation(MeshKit.ToWorld(car.TowX, car.TowY, high), MeshKit.Heading(car.TowH));
                }

                // No two cars' signals flash in step: each has its own beat, up to a tenth faster or slower than the next car's.
                uint mix = unchecked((uint)car.Id * 2246822519u);
                double beat = 1.45 * (0.9 + 0.2 * ((mix >> 8) % 1000) / 1000.0), offset = ((mix >> 18) % 1000) / 1000.0;
                bool blink = (time * beat + offset) % 1 < 0.5;
                // Brake lights: on while the driver is slowing down or standing still, and they stay on
                // through the moments when the foot only eases, as a real driver's do.
                bool stopped = car.V < 0.2 && !car.Crashed;
                v.Braking = stopped || car.A < -0.5 || (v.Braking && car.A < -0.12);
                SetLamps(v, v.Brakes, v.Braking || dark, v.Braking ? brakeOn : tailOn, brakeOff);
                Show(v.BrakeGlow, dark, v.Braking ? brakeGlow : tailGlow);

                // Turn signals, by day and by night; after a crash, both sides flash.
                bool hazard = car.Crashed && blink;
                bool left = hazard || (car.Blinker < 0 && blink), right = hazard || (car.Blinker > 0 && blink);
                SetLamps(v, v.Left, left, blinkOn, blinkOff);
                SetLamps(v, v.Right, right, blinkOn, blinkOff);

                // A school bus taking children on: its stop sign is out, and its red lamps flash, one side and then the other.
                if (v.StopArm != null)
                {
                    if (v.StopArm.activeSelf != car.Loading) v.StopArm.SetActive(car.Loading);
                    bool one = time * 1.1 % 1 < 0.5;
                    for (int i = 0; i < v.Flashers.Length; i++) Show(v.Flashers[i], car.Loading && (i % 2 == 0) == one, brakeOn);
                }

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
                if (views[id].Tow != null) Object.Destroy(views[id].Tow.gameObject);
                Object.Destroy(views[id].Root);
                views.Remove(id);
            }
        }
    }
}
