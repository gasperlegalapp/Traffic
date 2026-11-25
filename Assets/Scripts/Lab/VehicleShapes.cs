using System.Collections.Generic;
using Traffic.Art;
using UnityEngine;

namespace Traffic.Lab
{
    /// <summary>
    /// The vehicles that have no model in the art file, built from boxes and drums: the
    /// motorcycle with its rider, the big box truck, and the semi's tractor and its two kinds
    /// of trailer. Each is made once and shared by every vehicle of its kind. A shape faces +x
    /// with its middle at the origin and its wheels on the ground, like the models.
    /// </summary>
    public sealed class VehicleShape
    {
        /// <summary>The parts by what they are made of. Paint takes the vehicle's own color. Any may be null.</summary>
        public Mesh Paint, Dark, Metal, Glass, Pale, Rider;
        /// <summary>The panels on its sides that carry a company's name, if it has them: see Livery.</summary>
        public Mesh Panels;
        /// <summary>Where its ends are, how far out its lamps sit from the middle, and how high they are.</summary>
        public float Front, Back, LampOut, LampY;

        private static VehicleShape motorcycle, boxTruck, tractor, boxTrailer, tankTrailer, schoolBus;

        private sealed class Parts
        {
            public readonly SolidBuilder Paint = new SolidBuilder(), Dark = new SolidBuilder(), Metal = new SolidBuilder(), Glass = new SolidBuilder(), Pale = new SolidBuilder(), Rider = new SolidBuilder();

            public VehicleShape Build(float front, float back, float lampOut, float lampY, Mesh panels = null)
            {
                return new VehicleShape
                {
                    Paint = Paint.Empty ? null : Paint.Build(), Dark = Dark.Empty ? null : Dark.Build(), Metal = Metal.Empty ? null : Metal.Build(),
                    Glass = Glass.Empty ? null : Glass.Build(), Pale = Pale.Empty ? null : Pale.Build(), Rider = Rider.Empty ? null : Rider.Build(),
                    Panels = panels, Front = front, Back = back, LampOut = lampOut, LampY = lampY,
                };
            }
        }

        private static readonly Quaternion Level = Quaternion.identity;

        private static void Box(SolidBuilder into, float x, float y, float z, float long_, float high, float wide)
        {
            into.Box(new Vector3(x, y, z), new Vector3(long_, high, wide), Level);
        }

        /// <summary>A wheel on each side of the vehicle at one place along it.</summary>
        private static void Axle(Parts p, float x, float radius, float out_, float width)
        {
            foreach (float z in new[] { out_, -out_ })
            {
                p.Dark.Drum(new Vector3(x, radius, z), Vector3.forward, radius, width);
                p.Metal.Drum(new Vector3(x, radius, z + Mathf.Sign(z) * (width / 2 + 0.01f)), Vector3.forward, radius * 0.5f, 0.03f, 8);
            }
        }

        /// <summary>
        /// A panel on each side of a vehicle for a company's name, as one mesh: from one place
        /// along it to another, so high, centered at a height, and so far out from the middle.
        /// The picture reads from left to right on both sides.
        /// </summary>
        private static Mesh SidePanels(float from, float to, float y, float high, float out_)
        {
            float low = y - high / 2, top = y + high / 2;
            var mesh = new Mesh();
            mesh.vertices = new[]
            {
                // The driver's left (+z), seen from that side: the front of the vehicle is on the left of the picture.
                new Vector3(to, low, out_), new Vector3(from, low, out_), new Vector3(from, top, out_), new Vector3(to, top, out_),
                // The right (-z): the back is on the left of the picture.
                new Vector3(from, low, -out_), new Vector3(to, low, -out_), new Vector3(to, top, -out_), new Vector3(from, top, -out_),
            };
            mesh.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1), new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
            mesh.normals = new[] { Vector3.forward, Vector3.forward, Vector3.forward, Vector3.forward, Vector3.back, Vector3.back, Vector3.back, Vector3.back };
            // Each face both ways round, so it shows whichever way the pipeline counts a face.
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3, 0, 2, 1, 0, 3, 2, 4, 5, 6, 4, 6, 7, 4, 6, 5, 4, 7, 6 };
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>A motorcycle and its rider: two wheels on the ground, an engine between them, a tank, a seat, and someone leaning to the bars.</summary>
        public static VehicleShape Motorcycle
        {
            get
            {
                if (motorcycle != null && motorcycle.Dark != null) return motorcycle;
                var p = new Parts();
                const float wheel = 0.33f;
                foreach (float x in new[] { 0.72f, -0.72f })
                {
                    p.Dark.Drum(new Vector3(x, wheel, 0), Vector3.forward, wheel, 0.15f, 12);
                    p.Metal.Drum(new Vector3(x, wheel, 0), Vector3.forward, wheel * 0.45f, 0.17f, 8);
                }
                // Engine and frame, the tank and the tail in the bike's color, the seat, the pipe.
                Box(p.Dark, 0, 0.5f, 0, 0.78f, 0.36f, 0.26f);
                Box(p.Paint, 0.2f, 0.82f, 0, 0.56f, 0.24f, 0.32f);
                Box(p.Dark, -0.32f, 0.8f, 0, 0.52f, 0.1f, 0.28f);
                Box(p.Paint, -0.7f, 0.74f, 0, 0.36f, 0.07f, 0.2f);
                Box(p.Metal, -0.5f, 0.4f, -0.2f, 0.75f, 0.09f, 0.09f);
                // The fork runs from the front wheel up and back to the bars.
                var foot = new Vector3(0.72f, wheel, 0);
                var head = new Vector3(0.46f, 1.0f, 0);
                Quaternion rake = Quaternion.FromToRotation(Vector3.up, (head - foot).normalized);
                foreach (float z in new[] { 0.1f, -0.1f }) p.Metal.Box((foot + head) / 2 + new Vector3(0, 0, z), new Vector3(0.06f, (head - foot).magnitude, 0.06f), rake);
                Box(p.Dark, 0.46f, 1.02f, 0, 0.06f, 0.05f, 0.68f);
                Box(p.Pale, 0.62f, 0.88f, 0, 0.12f, 0.16f, 0.18f);
                Box(p.Paint, 0.74f, 0.7f, 0, 0.3f, 0.05f, 0.18f);
                // The rider: hips on the seat, a leg down each side, the body leaning forward, arms out to the bars, a helmet.
                Box(p.Rider, -0.2f, 0.93f, 0, 0.4f, 0.2f, 0.36f);
                foreach (float z in new[] { 0.2f, -0.2f })
                {
                    p.Rider.Box(new Vector3(0, 0.68f, z), new Vector3(0.16f, 0.46f, 0.13f), Quaternion.Euler(0, 0, 18));
                    p.Rider.Box(new Vector3(0.22f, 1.2f, z * 1.15f), new Vector3(0.5f, 0.09f, 0.09f), Quaternion.Euler(0, 0, -24));
                }
                p.Rider.Box(new Vector3(-0.08f, 1.26f, 0), new Vector3(0.26f, 0.58f, 0.4f), Quaternion.Euler(0, 0, -24));
                Box(p.Pale, 0.08f, 1.66f, 0, 0.27f, 0.27f, 0.26f);
                Box(p.Glass, 0.2f, 1.66f, 0, 0.05f, 0.13f, 0.22f);
                return motorcycle = p.Build(1.05f, -1.05f, 0, 0.78f);
            }
        }

        /// <summary>A big box truck in one piece: a cab in the truck's color and a tall pale box behind it, with a company's name on each side.</summary>
        public static VehicleShape BoxTruck
        {
            get
            {
                if (boxTruck != null && boxTruck.Dark != null) return boxTruck;
                var p = new Parts();
                Box(p.Dark, 0, 0.66f, 0, 7.2f, 0.22f, 1.0f);
                Axle(p, 2.75f, 0.48f, 1.0f, 0.32f);
                Axle(p, -2.3f, 0.48f, 0.92f, 0.56f);
                Box(p.Paint, 3.0f, 1.6f, 0, 1.56f, 1.7f, 2.3f);
                Box(p.Glass, 3.76f, 2.0f, 0, 0.06f, 0.7f, 2.0f);
                foreach (float z in new[] { 1.16f, -1.16f }) Box(p.Glass, 3.2f, 2.0f, z, 0.8f, 0.6f, 0.04f);
                Box(p.Metal, 3.8f, 0.72f, 0, 0.12f, 0.26f, 2.3f);
                Box(p.Pale, -0.85f, 2.2f, 0, 5.8f, 2.7f, 2.4f);
                Box(p.Metal, -3.76f, 0.72f, 0, 0.1f, 0.14f, 2.2f);
                return boxTruck = p.Build(3.8f, -3.8f, 0.85f, 1.0f, SidePanels(-3.55f, 1.85f, 2.3f, 1.35f, 1.215f));
            }
        }

        /// <summary>
        /// A school bus, in the yellow that is its paint: a short hood, a long body with a row of
        /// windows down each side and a black stripe under them, a pale roof, and dark bumpers.
        /// </summary>
        public static VehicleShape SchoolBus
        {
            get
            {
                if (schoolBus != null && schoolBus.Dark != null) return schoolBus;
                var p = new Parts();
                Box(p.Dark, 0, 0.62f, 0, 8.4f, 0.2f, 1.0f);
                Axle(p, 2.9f, 0.48f, 1.0f, 0.32f);
                Axle(p, -2.6f, 0.48f, 0.94f, 0.54f);
                Box(p.Paint, 3.85f, 1.25f, 0, 1.3f, 1.0f, 2.1f);
                Box(p.Paint, -0.65f, 1.85f, 0, 7.7f, 2.2f, 2.36f);
                Box(p.Pale, -0.65f, 2.98f, 0, 7.5f, 0.08f, 2.1f);
                Box(p.Glass, 3.22f, 2.3f, 0, 0.06f, 0.75f, 2.0f);
                Box(p.Glass, -4.52f, 2.35f, 0, 0.06f, 0.6f, 1.5f);
                foreach (float z in new[] { 1.19f, -1.19f })
                {
                    Box(p.Glass, -0.7f, 2.35f, z, 6.9f, 0.6f, 0.04f);
                    Box(p.Dark, -0.65f, 1.55f, z, 7.7f, 0.12f, 0.04f);
                    Box(p.Dark, -0.65f, 1.15f, z, 7.7f, 0.06f, 0.04f);
                }
                Box(p.Dark, 4.5f, 0.7f, 0, 0.14f, 0.3f, 2.3f);
                Box(p.Dark, -4.5f, 0.7f, 0, 0.14f, 0.3f, 2.36f);
                return schoolBus = p.Build(4.5f, -4.5f, 0.85f, 1.0f);
            }
        }

        /// <summary>The front of a semi: a long hood, a tall cab with a wind deflector, stacks, tanks, and a plate at the back for the trailer to sit on.</summary>
        public static VehicleShape Tractor
        {
            get
            {
                if (tractor != null && tractor.Dark != null) return tractor;
                var p = new Parts();
                Box(p.Dark, -0.2f, 0.72f, 0, 4.0f, 0.22f, 1.0f);
                Axle(p, 1.45f, 0.5f, 1.05f, 0.32f);
                Axle(p, -0.75f, 0.5f, 0.95f, 0.56f);
                Axle(p, -1.75f, 0.5f, 0.95f, 0.56f);
                Box(p.Paint, 1.5f, 1.38f, 0, 1.4f, 1.0f, 1.9f);
                Box(p.Paint, 0.25f, 1.9f, 0, 1.3f, 2.0f, 2.3f);
                Box(p.Paint, 0.15f, 3.08f, 0, 1.0f, 0.4f, 2.0f);
                Box(p.Glass, 0.92f, 2.25f, 0, 0.06f, 0.72f, 2.0f);
                foreach (float z in new[] { 1.16f, -1.16f })
                {
                    Box(p.Glass, 0.4f, 2.25f, z, 0.7f, 0.6f, 0.04f);
                    Box(p.Metal, -0.5f, 2.1f, z * 0.98f, 0.14f, 2.5f, 0.14f);
                    p.Metal.Drum(new Vector3(0.1f, 0.78f, z * 0.9f), Vector3.right, 0.3f, 1.0f, 8);
                }
                Box(p.Metal, 2.2f, 1.32f, 0, 0.08f, 0.8f, 1.5f);
                Box(p.Metal, 2.2f, 0.68f, 0, 0.14f, 0.3f, 2.4f);
                Box(p.Dark, -1.3f, 0.98f, 0, 1.3f, 0.12f, 1.2f);
                return tractor = p.Build(2.2f, -2.2f, 0.9f, 1.0f);
            }
        }

        /// <summary>
        /// A box trailer with a company's name down each side. Its front reaches out past its
        /// own middle's share of the vehicle, over the back of the tractor, as a real one sits.
        /// </summary>
        public static VehicleShape BoxTrailer
        {
            get
            {
                if (boxTrailer != null && boxTrailer.Dark != null) return boxTrailer;
                var p = new Parts();
                Box(p.Pale, 0.55f, 2.52f, 0, 10.6f, 2.8f, 2.5f);
                TrailerGear(p);
                return boxTrailer = p.Build(5.85f, -4.78f, 0.9f, 1.3f, SidePanels(-4.4f, 5.5f, 2.52f, 2.475f, 1.265f));
            }
        }

        /// <summary>A tank trailer: a long drum on a frame, with a walkway and hatches on top and the company's name on a plate each side.</summary>
        public static VehicleShape TankTrailer
        {
            get
            {
                if (tankTrailer != null && tankTrailer.Dark != null) return tankTrailer;
                var p = new Parts();
                p.Metal.Drum(new Vector3(0.55f, 2.42f, 0), Vector3.right, 1.2f, 10.4f, 14);
                Box(p.Dark, 0.55f, 3.64f, 0, 6.5f, 0.08f, 0.5f);
                foreach (float x in new[] { -2f, 0.55f, 3.1f }) p.Dark.Drum(new Vector3(x, 3.72f, 0), Vector3.up, 0.3f, 0.16f, 8);
                foreach (float x in new[] { -3.3f, 0.3f, 3.9f }) Box(p.Dark, x, 1.25f, 0, 0.5f, 0.5f, 1.6f);
                TrailerGear(p);
                return tankTrailer = p.Build(5.75f, -4.78f, 0.9f, 1.3f, SidePanels(-2.45f, 3.55f, 2.42f, 1.5f, 1.225f));
            }
        }

        /// <summary>What every trailer has underneath: a frame, two axles at the back, legs to stand on at the front, and a bar across the back.</summary>
        private static void TrailerGear(Parts p)
        {
            Box(p.Dark, -0.6f, 1.02f, 0, 8.3f, 0.2f, 1.1f);
            Axle(p, -3.2f, 0.5f, 0.95f, 0.56f);
            Axle(p, -4.15f, 0.5f, 0.95f, 0.56f);
            foreach (float z in new[] { 0.8f, -0.8f }) Box(p.Metal, 2.4f, 0.6f, z, 0.12f, 0.9f, 0.12f);
            Box(p.Metal, -4.76f, 0.72f, 0, 0.1f, 0.14f, 2.3f);
        }

        // ---------------------------------------------------------------- company names

        private static readonly Dictionary<Livery, Material> liveries = new Dictionary<Livery, Material>();

        /// <summary>The material that shows a company's name: its picture, made once.</summary>
        public static Material LiveryMaterial(Livery livery)
        {
            Material made;
            if (liveries.TryGetValue(livery, out made) && made != null && made.mainTexture != null) return made;
            uint[] rows = livery.Paint();
            var pixels = new Color32[rows.Length];
            // The picture's rows run from the top down; a texture's from the bottom up.
            for (int y = 0; y < Livery.Height; y++)
            {
                for (int x = 0; x < Livery.Width; x++)
                {
                    uint c = rows[(Livery.Height - 1 - y) * Livery.Width + x];
                    pixels[y * Livery.Width + x] = new Color32((byte)(c >> 16), (byte)(c >> 8), (byte)c, 255);
                }
            }
            var picture = new Texture2D(Livery.Width, Livery.Height, TextureFormat.RGBA32, true);
            picture.SetPixels32(pixels);
            picture.wrapMode = TextureWrapMode.Clamp;
            picture.filterMode = FilterMode.Trilinear;
            picture.anisoLevel = 4;
            picture.Apply(true);
            made = MeshKit.Textured(picture, Color.white, 0.15f);
            liveries[livery] = made;
            return made;
        }
    }
}
