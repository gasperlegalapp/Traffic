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

        private sealed class LineView
        {
            public Lane Lane;
            public Material Material;
        }

        private sealed class SignalView
        {
            public Approach Arm;
            /// <summary>The movement the main lamps show.</summary>
            public Turn Main;
            public bool HasLeft;
            public GameObject Head;
            public GameObject ArrowHead;
            public GameObject StopSign;
            public Material[] Lamps;
            public Material ArrowLamp;
        }

        private readonly World world;
        private readonly List<LineView> lines = new List<LineView>();
        private readonly List<SignalView> signals = new List<SignalView>();
        private readonly Transform nodeRing;
        private readonly GameObject rootObject;

        public RoadView(World world, Transform parent)
        {
            this.world = world;
            rootObject = new GameObject("Roads");
            Transform root = rootObject.transform;
            root.SetParent(parent, false);

            var ground = new FlatBuilder();
            const double g = 3000;
            ground.Strip(new[] { new Vec(-g, -g), new Vec(-g, g) }, new[] { new Vec(g, -g), new Vec(g, g) }, 0);
            MeshKit.Spawn("Ground", ground.Build(), MeshKit.Lit(MeshKit.Hex(0x2c3d33), 0), root, false);

            var asphalt = new FlatBuilder();
            foreach (Strip s in world.Surfaces) asphalt.Strip(s.Left, s.Right, 0.03f);
            foreach (Node node in world.Nodes) if (node.Box.Count > 2) asphalt.Fan(node.Pos, node.Box, 0.03f);
            MeshKit.Spawn("Asphalt", asphalt.Build(), MeshKit.Lit(MeshKit.Hex(0x353a42), 0.05f), root, false);

            var white = new FlatBuilder();
            var yellow = new FlatBuilder();
            var red = new FlatBuilder();
            foreach (Marking m in world.Markings)
            {
                FlatBuilder b = m.Yellow ? yellow : white;
                if (m.DashOn > 0) b.Dashed(m.Pts, m.Width, m.DashOn, m.DashOff, 0.07f);
                else b.Line(m.Pts, m.Width, 0.07f);
            }
            foreach (ArrowMark arrow in world.Arrows) PaintArrows(arrow, white, red);
            MeshKit.Spawn("White paint", white.Build(), MeshKit.Lit(MeshKit.Hex(0xe8ecf1), 0), root, false);
            MeshKit.Spawn("Yellow paint", yellow.Build(), MeshKit.Lit(MeshKit.Hex(0xf2c230), 0), root, false);
            MeshKit.Spawn("Red paint", red.Build(), MeshKit.Lit(MeshKit.Hex(0xe5383b), 0), root, false);

            foreach (StopLine sl in world.StopLines)
            {
                var back = new Vec(-System.Math.Cos(sl.H) * 0.55, -System.Math.Sin(sl.H) * 0.55);
                var b = new FlatBuilder();
                b.Strip(new[] { sl.A, sl.B }, new[] { new Vec(sl.A.X + back.X, sl.A.Y + back.Y), new Vec(sl.B.X + back.X, sl.B.Y + back.Y) }, 0.09f);
                var view = new LineView { Lane = world.Lanes[sl.Lane], Material = MeshKit.Unlit(Color.white) };
                MeshKit.Spawn("Stop line", b.Build(), view.Material, root, false);
                lines.Add(view);
            }

            Material dark = MeshKit.Lit(MeshKit.Hex(0x15181c), 0.3f);
            foreach (Node node in world.Nodes)
            {
                if (!node.Controlled) continue;
                foreach (Approach arm in node.Arms) if (arm.Inbound.Count > 0) signals.Add(BuildSignal(arm, dark, root));
            }

            // Ring that marks the selected intersection.
            var inner = new List<Vec>();
            var outer = new List<Vec>();
            for (int i = 0; i <= 64; i++)
            {
                double a = System.Math.PI * 2 * i / 64;
                inner.Add(new Vec(System.Math.Cos(a) * 0.96, System.Math.Sin(a) * 0.96));
                outer.Add(new Vec(System.Math.Cos(a), System.Math.Sin(a)));
            }
            var ringMesh = new FlatBuilder();
            ringMesh.Strip(inner, outer, 0);
            nodeRing = MeshKit.Spawn("Selected intersection", ringMesh.Build(), MeshKit.Unlit(MeshKit.Hex(0x5ee0c0)), root, false).transform;
            nodeRing.gameObject.SetActive(false);

            AddTrees(world, root);
        }

        private SignalView BuildSignal(Approach arm, Material dark, Transform root)
        {
            var view = new SignalView { Arm = arm };
            bool[] offered = world.Lanes[arm.Inbound[0]].ApproachTurns; // callers skip arms nothing arrives on
            view.Main = offered[(int)Turn.Straight] ? Turn.Straight : offered[(int)Turn.Right] ? Turn.Right : Turn.Left;
            view.HasLeft = (offered[(int)Turn.Left] || offered[(int)Turn.UTurn]) && view.Main != Turn.Left;

            // The post stands on the right of the road. Its local +x is the direction of
            // travel and local +z points left, across the road.
            var post = new GameObject("Signal post").transform;
            post.SetParent(root, false);
            post.position = MeshKit.ToWorld(arm.Post.X, arm.Post.Y);
            post.rotation = MeshKit.Heading(arm.H);
            MeshKit.Spawn("Pole", MeshKit.Box(0.28f, 4.4f, 0.28f), dark, post).transform.localPosition = new Vector3(0, 2.2f, 0);

            // How far across the road the leftmost and rightmost arriving lanes are.
            Lane leftLane = world.Lanes[arm.Inbound[0]];
            Lane rightLane = world.Lanes[arm.Inbound[arm.Inbound.Count - 1]];
            PathPose leftEnd = leftLane.Path.At(leftLane.Length);
            PathPose rightEnd = rightLane.Path.At(rightLane.Length);
            float toLeft = (float)Geo.Hypot(leftEnd.X - arm.Post.X, leftEnd.Y - arm.Post.Y);
            float toRight = (float)Geo.Hypot(rightEnd.X - arm.Post.X, rightEnd.Y - arm.Post.Y);

            // Signal heads on an arm reaching over the road, lamps facing up so they read from above.
            view.Head = new GameObject("Signal head");
            view.Head.transform.SetParent(post, false);
            float reach = view.HasLeft ? toLeft : toRight;
            MeshKit.Spawn("Arm", MeshKit.Box(0.16f, 0.16f, reach), dark, view.Head.transform).transform.localPosition = new Vector3(0, 4.3f, reach / 2);
            MeshKit.Spawn("Housing", MeshKit.Box(3.3f, 0.5f, 1.15f), dark, view.Head.transform).transform.localPosition = new Vector3(0, 4.5f, toRight);
            view.Lamps = new Material[3];
            for (int i = 0; i < 3; i++)
            {
                view.Lamps[i] = MeshKit.Unlit(DimRed);
                MeshKit.Spawn("Lamp", MeshKit.Box(0.8f, 0.06f, 0.8f), view.Lamps[i], view.Head.transform, false).transform.localPosition = new Vector3(-1.05f + i * 1.05f, 4.78f, toRight);
            }

            // A separate lamp over the left-turn lane, used when left turns have their own arrow.
            view.ArrowHead = new GameObject("Left arrow");
            view.ArrowHead.transform.SetParent(view.Head.transform, false);
            view.ArrowLamp = MeshKit.Unlit(DimRed);
            MeshKit.Spawn("Housing", MeshKit.Box(1.5f, 0.5f, 1.5f), dark, view.ArrowHead.transform).transform.localPosition = new Vector3(0, 4.5f, toLeft);
            var arrow = new FlatBuilder();
            arrow.Fan(new Vec(0, 0), new[] { new Vec(-0.5, 0), new Vec(0.2, 0.55), new Vec(0.2, 0.2), new Vec(0.5, 0.2), new Vec(0.5, -0.2), new Vec(0.2, -0.2), new Vec(0.2, -0.55) }, 0);
            // The arrow mesh points along local -x; turn it to point left across the road (+z).
            Transform lamp = MeshKit.Spawn("Lamp", arrow.Build(), view.ArrowLamp, view.ArrowHead.transform, false).transform;
            lamp.localPosition = new Vector3(0, 4.78f, toLeft);
            lamp.localRotation = Quaternion.Euler(0, 90, 0);
            lamp.localScale = new Vector3(1.2f, 1, 1.2f);

            view.StopSign = new GameObject("Stop sign");
            view.StopSign.transform.SetParent(post, false);
            MeshKit.Spawn("Rim", Octagon(1.15f), MeshKit.Unlit(Color.white), view.StopSign.transform, false).transform.localPosition = new Vector3(0, 4.42f, 0);
            MeshKit.Spawn("Plate", Octagon(1.0f), MeshKit.Unlit(MeshKit.Hex(0xe5383b)), view.StopSign.transform, false).transform.localPosition = new Vector3(0, 4.5f, 0);
            view.StopSign.SetActive(false);
            return view;
        }

        /// <summary>Paint the turn arrows for one lane, just before its stop line.</summary>
        private static void PaintArrows(ArrowMark mark, FlatBuilder white, FlatBuilder red)
        {
            double fx = System.Math.Cos(mark.At.H), fy = System.Math.Sin(mark.At.H);
            // Local coordinates: x forward along the lane, y to the driver's left, origin back from the line.
            System.Func<double, double, double, Vec> at = (back, x, y) => new Vec(mark.At.X - fx * back + fx * x - fy * y, mark.At.Y - fy * back + fy * x + fx * y);
            const float h = 0.07f;
            bool left = mark.Turns[(int)Turn.Left];
            bool straight = mark.Turns[(int)Turn.Straight];
            bool right = mark.Turns[(int)Turn.Right];

            const double b0 = 9.5;
            white.Line(new[] { at(b0, 0, 0), at(b0, straight ? 2.3 : 1.7, 0) }, 0.24, h);
            if (straight) white.Fan(at(b0, 2.7, 0), new[] { at(b0, 2.3, 0.45), at(b0, 3.3, 0), at(b0, 2.3, -0.45) }, h);
            if (left)
            {
                white.Line(new[] { at(b0, 1.6, 0), at(b0, 1.6, 0.6) }, 0.24, h);
                white.Fan(at(b0, 1.6, 0.9), new[] { at(b0, 1.15, 0.6), at(b0, 1.6, 1.4), at(b0, 2.05, 0.6) }, h);
            }
            if (right)
            {
                white.Line(new[] { at(b0, 1.6, 0), at(b0, 1.6, -0.6) }, 0.24, h);
                white.Fan(at(b0, 1.6, -0.9), new[] { at(b0, 1.15, -0.6), at(b0, 1.6, -1.4), at(b0, 2.05, -0.6) }, h);
            }

            // A U-turn symbol further back: plain where allowed, crossed out in red where not.
            if (!mark.Turns[(int)Turn.UTurn] && !mark.NoUTurn) return;
            const double b1 = 16.5;
            var hook = new List<Vec> { at(b1, 0, -0.45), at(b1, 1.5, -0.45) };
            for (int i = 0; i <= 8; i++)
            {
                double a = -System.Math.PI / 2 + System.Math.PI * i / 8;
                hook.Add(at(b1, 1.5 + 0.45 * System.Math.Cos(a), 0.45 * System.Math.Sin(a)));
            }
            hook.Add(at(b1, 0.9, 0.45));
            white.Line(hook, 0.22, h);
            white.Fan(at(b1, 0.6, 0.45), new[] { at(b1, 0.9, 0.85), at(b1, 0.1, 0.45), at(b1, 0.9, 0.05) }, h);
            if (!mark.NoUTurn) return;
            var ring = new List<Vec>();
            for (int i = 0; i <= 32; i++)
            {
                double a = System.Math.PI * 2 * i / 32;
                ring.Add(at(b1, 1.0 + 1.45 * System.Math.Cos(a), 1.45 * System.Math.Sin(a)));
            }
            red.Line(ring, 0.26, h + 0.01f);
            red.Line(new[] { at(b1, 2.0, 1.0), at(b1, 0.0, -1.0) }, 0.26, h + 0.01f);
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
            double range = world.Extent + 70;
            int placed = 0;
            for (int i = 0; i < 2000 && placed < 260; i++)
            {
                double x = rng.Range(-range, range);
                double y = rng.Range(-range, range);
                bool clear = true;
                foreach (Node node in world.Nodes)
                {
                    if (Geo.Hypot(x - node.Pos.X, y - node.Pos.Y) < node.Radius + 9) clear = false;
                }
                foreach (Lane lane in world.Lanes)
                {
                    if (!clear) break;
                    if (lane.Kind == LaneKind.Road && lane.Path.Project(x, y).D < 10) clear = false;
                }
                if (!clear) continue;
                float scale = (float)rng.Range(0.7, 1.4);
                Transform t = MeshKit.Spawn("Tree", cone, shades[rng.Int(shades.Length)], holder).transform;
                t.position = MeshKit.ToWorld(x, y);
                t.localScale = new Vector3(scale, scale, scale);
                placed++;
            }
        }

        /// <summary>Remove everything this view created, before loading another map.</summary>
        public void Destroy()
        {
            Object.Destroy(rootObject);
        }

        /// <summary>Show or hide the ring around the selected intersection.</summary>
        public void ShowSelected(Node node)
        {
            nodeRing.gameObject.SetActive(node != null);
            if (node == null) return;
            float r = (float)node.Radius + 3;
            nodeRing.position = MeshKit.ToWorld(node.Pos.X, node.Pos.Y, 0.11f);
            nodeRing.localScale = new Vector3(r, 1, r);
        }

        private static Color LampColor(SignalLight st)
        {
            return st == SignalLight.Green ? Green : st == SignalLight.Yellow ? Yellow : Red;
        }

        /// <summary>Show the current lights.</summary>
        public void Update(List<Junction> junctions, double time)
        {
            bool flash = time % 1 < 0.5;
            foreach (LineView line in lines)
            {
                Junction jn = junctions[line.Lane.Node];
                // A lane shows the best light of the turns it allows.
                SignalLight best = SignalLight.Red;
                for (int t = 0; t < Lane.Turns; t++)
                {
                    if (line.Lane.Connectors[t] < 0) continue;
                    SignalLight st = jn.State(line.Lane.Arm, (Turn)t);
                    if (st == SignalLight.Stop) best = SignalLight.Stop;
                    else if (st == SignalLight.Green) best = SignalLight.Green;
                    else if (st == SignalLight.Yellow && best == SignalLight.Red) best = SignalLight.Yellow;
                }
                Color c = LampColor(best);
                if (best == SignalLight.Stop && !flash) c *= 0.45f;
                line.Material.color = c;
            }
            foreach (SignalView s in signals)
            {
                Junction jn = junctions[s.Arm.Node];
                bool stop = jn.Mode == JunctionMode.AllWay;
                s.Head.SetActive(!stop);
                s.StopSign.SetActive(stop);
                if (stop) continue;
                SignalLight st = jn.State(s.Arm.Arm, s.Main);
                s.Lamps[0].color = st == SignalLight.Red ? Red : DimRed;
                s.Lamps[1].color = st == SignalLight.Yellow ? Yellow : DimYellow;
                s.Lamps[2].color = st == SignalLight.Green ? Green : DimGreen;
                bool arrow = s.HasLeft && jn.HasArrow(s.Arm.Arm);
                s.ArrowHead.SetActive(arrow);
                if (arrow) s.ArrowLamp.color = LampColor(jn.State(s.Arm.Arm, Turn.Left));
            }
        }
    }
}
