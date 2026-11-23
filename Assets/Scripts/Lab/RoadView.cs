using System.Collections.Generic;
using Traffic.Sim;
using UnityEngine;

namespace Traffic.Lab
{
    /// <summary>Ground, pavement, paint, trees and the traffic signals.</summary>
    public sealed class RoadView
    {
        private static readonly Color Green = MeshKit.Hex(0x35e07a);
        private static readonly Color Yellow = MeshKit.Hex(0xffc93c);
        private static readonly Color Red = MeshKit.Hex(0xff4040);
        private static readonly Color DimRed = MeshKit.Hex(0x4a1616);
        private static readonly Color DimYellow = MeshKit.Hex(0x4a3a12);
        private static readonly Color DimGreen = MeshKit.Hex(0x12402a);

        private readonly Material[] stopLines = new Material[4];
        private readonly Material[][] lamps = new Material[4][];
        private readonly GameObject[] signalHeads = new GameObject[4];
        private readonly GameObject[] stopSigns = new GameObject[4];

        public RoadView(World world, Transform parent)
        {
            var root = new GameObject("Roads").transform;
            root.SetParent(parent, false);

            var ground = new FlatBuilder();
            const double g = 2000;
            ground.Strip(new[] { new Vec(-g, -g), new Vec(-g, g) }, new[] { new Vec(g, -g), new Vec(g, g) }, 0);
            MeshKit.Spawn("Ground", ground.Build(), MeshKit.Lit(MeshKit.Hex(0x2c3d33), 0), root, false);

            var asphalt = new FlatBuilder();
            foreach (Strip s in world.Surfaces) asphalt.Strip(s.Left, s.Right, 0.03f);
            asphalt.Fan(new Vec(0, 0), world.Box, 0.03f);
            MeshKit.Spawn("Asphalt", asphalt.Build(), MeshKit.Lit(MeshKit.Hex(0x353a42), 0.05f), root, false);

            var white = new FlatBuilder();
            var yellow = new FlatBuilder();
            foreach (Marking m in world.Markings)
            {
                FlatBuilder b = m.Yellow ? yellow : white;
                if (m.DashOn > 0) b.Dashed(m.Pts, m.Width, m.DashOn, m.DashOff, 0.07f);
                else b.Line(m.Pts, m.Width, 0.07f);
            }
            MeshKit.Spawn("White paint", white.Build(), MeshKit.Lit(MeshKit.Hex(0xe8ecf1), 0), root, false);
            MeshKit.Spawn("Yellow paint", yellow.Build(), MeshKit.Lit(MeshKit.Hex(0xf2c230), 0), root, false);

            Material dark = MeshKit.Lit(MeshKit.Hex(0x15181c), 0.3f);
            foreach (StopLine sl in world.StopLines)
            {
                var back = new Vec(-System.Math.Cos(sl.H) * 0.55, -System.Math.Sin(sl.H) * 0.55);
                var b = new FlatBuilder();
                b.Strip(new[] { sl.A, sl.B }, new[] { new Vec(sl.A.X + back.X, sl.A.Y + back.Y), new Vec(sl.B.X + back.X, sl.B.Y + back.Y) }, 0.09f);
                stopLines[sl.Arm] = MeshKit.Unlit(Color.white);
                MeshKit.Spawn("Stop line", b.Build(), stopLines[sl.Arm], root, false);

                // The post stands on the right of the road. Its local +x is the direction of
                // travel and local +z points left, across the road.
                var post = new GameObject("Signal post").transform;
                post.SetParent(root, false);
                post.position = MeshKit.ToWorld(sl.Post.X, sl.Post.Y);
                post.rotation = MeshKit.Heading(sl.H);
                MeshKit.Spawn("Pole", MeshKit.Box(0.28f, 4.4f, 0.28f), dark, post).transform.localPosition = new Vector3(0, 2.2f, 0);

                // Signal head on an arm reaching over the road, lamps facing up so they read from above.
                var head = new GameObject("Signal head");
                head.transform.SetParent(post, false);
                MeshKit.Spawn("Arm", MeshKit.Box(0.16f, 0.16f, 3.4f), dark, head.transform).transform.localPosition = new Vector3(0, 4.3f, 1.7f);
                MeshKit.Spawn("Housing", MeshKit.Box(3.3f, 0.5f, 1.15f), dark, head.transform).transform.localPosition = new Vector3(0, 4.5f, 3.2f);
                lamps[sl.Arm] = new Material[3];
                for (int i = 0; i < 3; i++)
                {
                    lamps[sl.Arm][i] = MeshKit.Unlit(DimRed);
                    MeshKit.Spawn("Lamp", MeshKit.Box(0.8f, 0.06f, 0.8f), lamps[sl.Arm][i], head.transform, false).transform.localPosition = new Vector3(-1.05f + i * 1.05f, 4.78f, 3.2f);
                }
                signalHeads[sl.Arm] = head;

                var sign = new GameObject("Stop sign");
                sign.transform.SetParent(post, false);
                MeshKit.Spawn("Rim", Octagon(1.15f), MeshKit.Unlit(Color.white), sign.transform, false).transform.localPosition = new Vector3(0, 4.42f, 0);
                MeshKit.Spawn("Plate", Octagon(1.0f), MeshKit.Unlit(MeshKit.Hex(0xe5383b)), sign.transform, false).transform.localPosition = new Vector3(0, 4.5f, 0);
                sign.SetActive(false);
                stopSigns[sl.Arm] = sign;
            }

            AddTrees(world, root);
        }

        private static Mesh Octagon(float radius)
        {
            var ring = new List<Vec>();
            for (int i = 0; i < 8; i++)
            {
                double a = System.Math.PI / 8 + System.Math.PI / 4 * i;
                ring.Add(new Vec(System.Math.Cos(a) * radius, System.Math.Sin(a) * radius));
            }
            var b = new FlatBuilder();
            b.Fan(new Vec(0, 0), ring, 0);
            return b.Build();
        }

        private static void AddTrees(World world, Transform root)
        {
            var rng = new Rng(42);
            var holder = new GameObject("Trees").transform;
            holder.SetParent(root, false);
            Mesh cone = MeshKit.Cone(2.4f, 6, 7, 0.4f);
            var shades = new Material[4];
            for (int i = 0; i < shades.Length; i++) shades[i] = MeshKit.Lit(Color.HSVToRGB(0.36f + 0.012f * i, 0.42f, 0.30f + 0.035f * i), 0);
            double range = world.Extent + 60;
            int placed = 0;
            for (int i = 0; i < 900 && placed < 150; i++)
            {
                double x = rng.Range(-range, range);
                double y = rng.Range(-range, range);
                if (Geo.Hypot(x, y) < 24) continue;
                bool clear = true;
                foreach (Lane lane in world.Lanes)
                {
                    if (lane.Kind == LaneKind.Road && lane.Path.Project(x, y).D < 10)
                    {
                        clear = false;
                        break;
                    }
                }
                if (!clear) continue;
                float scale = (float)rng.Range(0.7, 1.4);
                Transform t = MeshKit.Spawn("Tree", cone, shades[rng.Int(shades.Length)], holder).transform;
                t.position = MeshKit.ToWorld(x, y);
                t.localScale = new Vector3(scale, scale, scale);
                placed++;
            }
        }

        /// <summary>Show the current light for each arm.</summary>
        public void Update(Junction junction, double time)
        {
            for (int arm = 0; arm < 4; arm++)
            {
                SignalLight st = junction.ArmState(arm);
                bool stop = st == SignalLight.Stop;
                signalHeads[arm].SetActive(!stop);
                stopSigns[arm].SetActive(stop);
                Color line = st == SignalLight.Green ? Green : st == SignalLight.Yellow ? Yellow : Red;
                if (stop && time % 1 >= 0.5) line *= 0.45f;
                stopLines[arm].color = line;
                lamps[arm][0].color = st == SignalLight.Red ? Red : DimRed;
                lamps[arm][1].color = st == SignalLight.Yellow ? Yellow : DimYellow;
                lamps[arm][2].color = st == SignalLight.Green ? Green : DimGreen;
            }
        }
    }
}
