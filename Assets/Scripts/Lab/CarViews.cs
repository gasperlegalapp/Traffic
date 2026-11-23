using System.Collections.Generic;
using Traffic.Sim;
using UnityEngine;

namespace Traffic.Lab
{
    /// <summary>Draws every car: a simple body with working brake lights and turn signals.</summary>
    public sealed class CarViews
    {
        private sealed class View
        {
            public GameObject Root;
            public Material Paint;
            public MeshRenderer[] Brakes;
            public MeshRenderer[] Left;
            public MeshRenderer[] Right;
        }

        private readonly Transform root;
        private readonly Dictionary<int, View> views = new Dictionary<int, View>();
        private readonly HashSet<int> seen = new HashSet<int>();
        private readonly List<int> gone = new List<int>();
        private readonly Transform ring;

        private readonly Mesh body = MeshKit.Box(4.5f, 0.8f, 1.9f);
        private readonly Mesh cabin = MeshKit.Box(2.3f, 0.6f, 1.62f);
        private readonly Mesh truckCab = MeshKit.Box(2.0f, 2.0f, 2.4f);
        private readonly Mesh truckBox = MeshKit.Box(6.1f, 2.7f, 2.5f);
        private readonly Mesh truckGlass = MeshKit.Box(0.5f, 0.7f, 2.2f);
        private readonly Mesh bikeBody = MeshKit.Box(2.0f, 0.5f, 0.4f);
        private readonly Mesh bikeRider = MeshKit.Box(0.6f, 0.8f, 0.6f);
        private readonly Mesh bikeLamp = MeshKit.Box(0.14f, 0.18f, 0.24f);
        private readonly Material cargo = MeshKit.Lit(MeshKit.Hex(0xdfe3e8), 0.2f);
        private readonly Material leather = MeshKit.Lit(MeshKit.Hex(0x22262b), 0.3f);
        private readonly Mesh lamp = MeshKit.Box(0.14f, 0.2f, 0.46f);
        private readonly Mesh corner = MeshKit.Box(0.3f, 0.2f, 0.3f);
        private readonly Material glass = MeshKit.Lit(MeshKit.Hex(0x161d26), 0.7f);
        private readonly Material brakeOn = MeshKit.Unlit(MeshKit.Hex(0xff2d2d));
        private readonly Material brakeOff = MeshKit.Unlit(MeshKit.Hex(0x5c1414));
        private readonly Material blinkOn = MeshKit.Unlit(MeshKit.Hex(0xffb020));
        private readonly Material blinkOff = MeshKit.Unlit(MeshKit.Hex(0x4a3a18));
        private readonly Material headlight = MeshKit.Unlit(MeshKit.Hex(0xfff6d8));

        public CarViews(Transform parent)
        {
            root = new GameObject("Cars").transform;
            root.SetParent(parent, false);

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

        private View Make(Car car)
        {
            var go = new GameObject("Car " + car.Id);
            go.transform.SetParent(root, false);
            var v = new View { Root = go, Paint = MeshKit.Lit(MeshKit.Hex(car.Color), 0.5f) };
            float halfLen = (float)car.Len / 2;
            float halfWid = (float)car.Wid / 2;
            float lampY = 0.86f;
            Mesh lampMesh = lamp;
            if (car.Vehicle.Kind == VehicleKind.Truck)
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
            float back = -halfLen + 0.05f;
            float front = halfLen - 0.05f;
            float inset = halfWid * 0.45f;
            float edge = halfWid - 0.15f;
            v.Brakes = new[] { Lamp(lampMesh, brakeOff, back, inset), Lamp(lampMesh, brakeOff, back, -inset) };
            v.Left = new[] { Lamp(corner, blinkOff, front - 0.12f, edge), Lamp(corner, blinkOff, back + 0.12f, edge) };
            v.Right = new[] { Lamp(corner, blinkOff, front - 0.12f, -edge), Lamp(corner, blinkOff, back + 0.12f, -edge) };
            Lamp(lampMesh, headlight, front, inset);
            Lamp(lampMesh, headlight, front, -inset);
            return v;
        }

        /// <summary>Remove everything this view created, before loading another map.</summary>
        public void Destroy()
        {
            foreach (View v in views.Values) Object.Destroy(v.Paint);
            views.Clear();
            Object.Destroy(root.gameObject);
        }

        public void Sync(List<Car> cars, double time, int selectedId)
        {
            seen.Clear();
            bool blink = time * 1.6 % 1 < 0.5;
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
                v.Root.transform.SetPositionAndRotation(MeshKit.ToWorld(car.X, car.Y, (float)car.Height), MeshKit.Heading(car.H));
                bool braking = car.A < -0.6 || (car.V < 0.2 && !car.Crashed);
                foreach (MeshRenderer r in v.Brakes) r.sharedMaterial = braking ? brakeOn : brakeOff;
                bool hazard = car.Crashed && blink;
                foreach (MeshRenderer r in v.Left) r.sharedMaterial = hazard || (car.Blinker < 0 && blink) ? blinkOn : blinkOff;
                foreach (MeshRenderer r in v.Right) r.sharedMaterial = hazard || (car.Blinker > 0 && blink) ? blinkOn : blinkOff;
                Color paint = MeshKit.Hex(car.Color);
                if (car.Flash && time * 3 % 1 < 0.5) paint = Color.Lerp(paint, Color.white, 0.8f);
                v.Paint.color = car.Crashed ? paint * 0.35f : paint;
                if (car.Id == selectedId)
                {
                    ring.gameObject.SetActive(true);
                    ring.position = MeshKit.ToWorld(car.X, car.Y, 0.12f + (float)car.Height);
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
