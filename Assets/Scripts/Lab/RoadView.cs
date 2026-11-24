using System.Collections.Generic;
using Traffic.Art;
using Traffic.Sim;
using UnityEngine;

namespace Traffic.Lab
{
    /// <summary>Ground, pavement, paint, scenery and the traffic signals.</summary>
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

        /// <summary>One signal head, hung over the lane it controls.</summary>
        private sealed class LaneHead
        {
            public Lane Lane;
            /// <summary>Red, yellow, green; or a single arrow lamp for a lane that only turns left.</summary>
            public Material[] Lamps;
            public Material Arrow;
        }

        private sealed class SignalView
        {
            public Approach Arm;
            public GameObject Heads;
            public GameObject StopSign;
            public List<LaneHead> Lanes = new List<LaneHead>();
        }

        private readonly World world;
        private readonly List<LineView> lines = new List<LineView>();
        private readonly List<SignalView> signals = new List<SignalView>();
        private readonly Transform nodeRing;
        private readonly GameObject rootObject;
        private readonly Material face = MeshKit.Lit(MeshKit.Hex(0xc9ced6), 0.2f);

        public RoadView(World world, Transform parent)
        {
            this.world = world;
            rootObject = new GameObject("Roads");
            Transform root = rootObject.transform;
            root.SetParent(parent, false);

            MeshKit.Spawn("Ground", GroundMesh(3000), GroundMaterial(), root, false);

            // What the roads are made of. Asphalt and gravel take their texture from where they lie
            // on the map; a sidewalk takes it from along its own length, a slab at a time.
            Texture2D asphaltPhoto = ArtKit.Photo("asphalt"), walkPhoto = ArtKit.Photo("sidewalk"), gravelPhoto = ArtKit.Photo("gravel");
            var asphalt = new SurfaceBuilder(64);
            var deck = new SurfaceBuilder(64);
            var walks = new SurfaceBuilder(1);
            var shoulders = new SurfaceBuilder(12);
            var lawn = new SurfaceBuilder(GroundTile);
            var skirt = new FlatBuilder();
            var buried = new FlatBuilder();
            var mouths = new FlatBuilder();
            var grime = new ShadeBuilder();

            // Where the lanes are, so that a sidewalk can be kept off the next road at a sharp corner.
            var lanePoints = new Dictionary<long, List<Vec>>();
            foreach (Lane lane in world.Lanes)
            {
                if (lane.Kind != LaneKind.Road) continue;
                for (double d = 0; d <= lane.Length; d += 2)
                {
                    PathPose at = lane.Path.At(d);
                    long key = Key((int)System.Math.Floor(at.X / 4), (int)System.Math.Floor(at.Y / 4));
                    List<Vec> list;
                    if (!lanePoints.TryGetValue(key, out list)) lanePoints[key] = list = new List<Vec>();
                    list.Add(at.Point);
                }
            }
            bool OnALane(Vec p)
            {
                int cx = (int)System.Math.Floor(p.X / 4), cy = (int)System.Math.Floor(p.Y / 4);
                for (int i = cx - 1; i <= cx + 1; i++)
                {
                    for (int j = cy - 1; j <= cy + 1; j++)
                    {
                        List<Vec> list;
                        if (!lanePoints.TryGetValue(Key(i, j), out list)) continue;
                        foreach (Vec q in list) if (Geo.Hypot(q.X - p.X, q.Y - p.Y) < LaneReach) return true;
                    }
                }
                return false;
            }
            // The roadside along one edge of the pavement: a raised sidewalk with a curb on a street,
            // a gravel shoulder on a highway. Side is 1 for the right of the line, -1 for the left.
            void Roadside(IList<Vec> edge, int side, bool fast)
            {
                if (edge.Count < 2) return;
                if (fast)
                {
                    Vec[] outer = Geo.Offset(new List<Vec>(edge), side * ShoulderWidth);
                    if (side > 0) shoulders.Strip(edge, outer, 0.022f);
                    else shoulders.Strip(outer, edge, 0.022f);
                    return;
                }
                var list = new List<Vec>(edge);
                Vec[] full = Geo.Offset(list, side * WalkWidth), slim = Geo.Offset(list, side * 0.35);
                var far = new Vec[edge.Count];
                for (int i = 0; i < far.Length; i++) far[i] = OnALane(full[i]) ? slim[i] : full[i];
                walks.Ribbon(edge, far, CurbHeight, 1.6f);
                walks.Wall(edge, 0.02f, CurbHeight, 1.6f, -side);
                // The road is darker along the curb, where the dirt gathers.
                grime.Along(list, new[] { 0, -side * 0.25, -side * 0.9 }, new[] { 0.3f, 0.2f, 0f }, 0.04f, false, 2);
            }

            bool anyBridge = false, anyTunnel = false;
            foreach (Strip s in world.Surfaces)
            {
                bool fast = s.Speed >= FastRoad;
                if (s.Heights == null)
                {
                    asphalt.Strip(s.Left, s.Right, 0.03f);
                    Roadside(s.Right, 1, fast);
                    if (s.LeftIsEdge) Roadside(s.Left, -1, fast);
                    continue;
                }
                if (s.Peak > 0)
                {
                    // A bridge: a raised deck that hides and shades the road beneath, with walls down its sides.
                    anyBridge = true;
                    deck.Strip(s.Left, s.Right, 0.03f, s.Heights);
                    skirt.Skirt(s.Right, s.Heights, 0.03f);
                    skirt.Skirt(s.Left, s.Heights, 0.03f);
                    continue;
                }
                // A tunnel. The road is drawn where it is still open to the sky, on its way down;
                // where it is covered over, broken lines on the ground show where it runs, with a
                // dark mouth where it goes under and where it comes back out.
                anyTunnel = true;
                foreach (int[] run in Runs(s.Heights, false))
                {
                    Vec[] l = Part(s.Left, run), r = Part(s.Right, run);
                    asphalt.Strip(l, r, 0.03f);
                    Roadside(r, 1, true);
                    if (s.LeftIsEdge) Roadside(l, -1, true);
                }
                foreach (int[] run in Runs(s.Heights, true))
                {
                    Vec[] l = Part(s.Left, run), r = Part(s.Right, run);
                    buried.Dashed(l, 0.4, 3.5, 3.5, 0.05f);
                    buried.Dashed(r, 0.4, 3.5, 3.5, 0.05f);
                    int deep = System.Math.Min(4, l.Length - 1);
                    mouths.Strip(Part(l, new[] { 0, deep }), Part(r, new[] { 0, deep }), 0.06f);
                    mouths.Strip(Part(l, new[] { l.Length - 1 - deep, l.Length - 1 }), Part(r, new[] { r.Length - 1 - deep, r.Length - 1 }), 0.06f);
                }
            }
            // Intersections: their pavement, and the roadside round each corner from one road to the next.
            foreach (Node node in world.Nodes)
            {
                if (node.Box.Count <= 2) continue;
                asphalt.Fan(node.Pos, node.Box, 0.03f);
                double fastest = 0;
                foreach (Lane lane in world.Lanes) if (lane.Kind == LaneKind.Road && lane.ApproachNode == node.Id) fastest = System.Math.Max(fastest, lane.SpeedLimit);
                foreach (Vec[] corner in node.Corners) Roadside(corner, 1, fastest >= FastRoad);
            }
            Material asphaltMaterial = MeshKit.Textured(asphaltPhoto, asphaltPhoto != null ? Color.white : MeshKit.Hex(0x50545a), 0.12f);
            MeshKit.Spawn("Asphalt", asphalt.Build(), asphaltMaterial, root, false);
            if (!walks.Empty) MeshKit.Spawn("Sidewalks", walks.Build(), MeshKit.Textured(walkPhoto, walkPhoto != null ? Color.white : MeshKit.Hex(0xb0aea8), 0.05f), root, true);
            if (!shoulders.Empty) MeshKit.Spawn("Shoulders", shoulders.Build(), MeshKit.Textured(gravelPhoto, gravelPhoto != null ? Color.white : MeshKit.Hex(0x89816f), 0), root, false);
            if (anyBridge)
            {
                MeshKit.Spawn("Bridge deck", deck.Build(), asphaltMaterial, root, true);
                MeshKit.Spawn("Bridge sides", skirt.Build(), MeshKit.Lit(MeshKit.Hex(0x9a9890), 0.05f), root, true);
            }
            if (anyTunnel)
            {
                MeshKit.Spawn("Tunnel lines", buried.Build(), MeshKit.Lit(MeshKit.Hex(0xd9dfc8), 0), root, false);
                MeshKit.Spawn("Tunnel mouths", mouths.Build(), MeshKit.Lit(MeshKit.Hex(0x15181c), 0), root, false);
            }

            // What traffic leaves on the road: two dark wheel tracks and a line of oil down each lane,
            // heavier through an intersection where everyone brakes and turns.
            foreach (Lane lane in world.Lanes)
            {
                if (lane.Peak != 0) continue;
                bool turning = lane.Kind == LaneKind.Connector;
                float tyre = turning ? 0.2f : 0.13f, oil = turning ? 0.04f : 0.08f;
                grime.Along(lane.Path.Pts, new[] { -1.35, -0.85, -0.4, -0.12, 0.12, 0.4, 0.85, 1.35 }, new[] { 0f, tyre, tyre * 0.15f, oil, oil, tyre * 0.15f, tyre, 0f }, 0.045f, true, 2);
            }
            if (!grime.Empty) MeshKit.Spawn("Wear", grime.Build(), MeshKit.Shade, root, false).GetComponent<MeshRenderer>().receiveShadows = false;

            if (world.Medians.Count > 0)
            {
                // The strip down the middle of a divided road: grass behind a curb, with a row of bushes along it.
                var plants = new CanopyBuilder();
                int planted = 0;
                foreach (Strip m in world.Medians)
                {
                    if (m.Heights != null && m.Peak > 0)
                    {
                        lawn.Strip(m.Left, m.Right, 0.08f, m.Heights);
                        continue;
                    }
                    List<int[]> runs = m.Heights != null ? Runs(m.Heights, false) : new List<int[]> { new[] { 0, m.Left.Length - 1 } };
                    foreach (int[] run in runs)
                    {
                        Vec[] l = Part(m.Left, run), r = Part(m.Right, run);
                        lawn.Strip(l, r, CurbHeight);
                        walks.Wall(l, 0.02f, CurbHeight, 1.6f, -1);
                        walks.Wall(r, 0.02f, CurbHeight, 1.6f, 1);
                    }
                    if (m.Heights != null) continue;
                    for (int i = 9; i < m.Left.Length - 9; i += 11)
                    {
                        var at = MeshKit.ToWorld((m.Left[i].X + m.Right[i].X) / 2, (m.Left[i].Y + m.Right[i].Y) / 2, CurbHeight);
                        plants.Bush(at, 0.75f, planted % ArtKit.SummerLeaves, planted * 7 + 3);
                        planted++;
                    }
                }
                MeshKit.Spawn("Medians", lawn.Build(), GroundMaterial(), root, false);
                if (planted > 0) MeshKit.Spawn("Median plants", plants.Build(), ArtKit.Canopy, root, true);
            }

            var white = new FlatBuilder();
            var yellow = new FlatBuilder();
            var red = new FlatBuilder();
            foreach (Marking m in world.Markings)
            {
                FlatBuilder b = m.Yellow ? yellow : white;
                if (m.Heights != null && m.Peak > 0)
                {
                    // On a bridge the paint follows the deck up and over.
                    if (m.DashOn > 0) b.Dashed(m.Pts, m.Heights, m.Width, m.DashOn, m.DashOff, 0.07f);
                    else b.Line(m.Pts, m.Heights, m.Width, 0.07f);
                    continue;
                }
                // In a tunnel only the open ends are painted.
                List<int[]> runs = m.Heights != null ? Runs(m.Heights, false) : null;
                if (runs == null) runs = new List<int[]> { new[] { 0, m.Pts.Length - 1 } };
                foreach (int[] run in runs)
                {
                    Vec[] pts = Part(m.Pts, run);
                    if (m.DashOn > 0) b.Dashed(pts, m.Width, m.DashOn, m.DashOff, 0.07f);
                    else b.Line(pts, m.Width, 0.07f);
                }
            }
            foreach (ArrowMark arrow in world.Arrows) PaintArrows(arrow, white, red);
            // A crosswalk over each road where it comes into an intersection with a light or stop signs:
            // a row of bars just beyond the stop lines.
            foreach (Node node in world.Nodes)
            {
                if (!node.Controlled) continue;
                foreach (Approach arm in node.Arms)
                {
                    if (arm.Inbound.Count == 0 && arm.Outbound.Count == 0) continue;
                    double dx = System.Math.Cos(arm.Angle), dy = System.Math.Sin(arm.Angle);
                    double near = node.Radius - 3.4, far = node.Radius - 0.9;
                    for (double x = -arm.EdgeCcw + 0.7; x <= arm.EdgeCw - 0.7; x += 1.25)
                    {
                        // Across the road: x to the right of its line, looking away from the intersection.
                        Vec At(double along, double across)
                        {
                            return new Vec(node.Pos.X + dx * along + dy * across, node.Pos.Y + dy * along - dx * across);
                        }
                        white.Strip(new[] { At(near, x - 0.28), At(far, x - 0.28) }, new[] { At(near, x + 0.28), At(far, x + 0.28) }, 0.07f);
                    }
                }
            }
            // Road paint is never quite white, and the yellow is nearer mustard than lemon.
            MeshKit.Spawn("White paint", white.Build(), MeshKit.Lit(MeshKit.Hex(0xdcdcd2), 0), root, false);
            MeshKit.Spawn("Yellow paint", yellow.Build(), MeshKit.Lit(MeshKit.Hex(0xdcb23c), 0), root, false);
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

            AddScenery(world, root);
        }

        /// <summary>A road this fast (meters per second) has gravel shoulders; a slower one has curbs and sidewalks.</summary>
        private const double FastRoad = 24;
        private const double WalkWidth = 2.2, ShoulderWidth = 2.6, LaneReach = 2.1;
        private const float CurbHeight = 0.16f;

        /// <summary>How far below the ground a tunnel is before it is covered over and out of sight.</summary>
        public const double Covered = 2.4;

        /// <summary>
        /// The stretches of a tunnel that are covered over, or the stretches still open to the
        /// sky, as first and last positions along its points. Neighboring stretches share an end.
        /// </summary>
        private static List<int[]> Runs(double[] heights, bool covered)
        {
            var runs = new List<int[]>();
            int start = -1;
            for (int i = 0; i + 1 < heights.Length; i++)
            {
                bool under = (heights[i] + heights[i + 1]) / 2 < -Covered;
                if (under == covered && start < 0) start = i;
                if (under != covered && start >= 0)
                {
                    runs.Add(new[] { start, i });
                    start = -1;
                }
            }
            if (start >= 0) runs.Add(new[] { start, heights.Length - 1 });
            return runs;
        }

        private static Vec[] Part(Vec[] pts, int[] run)
        {
            var part = new Vec[run[1] - run[0] + 1];
            System.Array.Copy(pts, run[0], part, 0, part.Length);
            return part;
        }

        // ------------------------------------------------------------ ground

        private static Texture2D groundTexture;
        private static Material groundMaterial;
        /// <summary>How much ground one copy of the ground texture covers, in meters.</summary>
        private const float GroundTile = 128;

        /// <summary>A whole number scrambled into a fraction from 0 to 1. The same numbers always give the same answer.</summary>
        private static float Rand(int x, int y, int salt)
        {
            unchecked
            {
                uint h = (uint)x * 374761393u + (uint)y * 668265263u + (uint)salt * 2246822519u;
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0xffffff) / 16777216f;
            }
        }

        /// <summary>Smooth random hills, 0 to 1. With a period it repeats, so a texture made from it tiles.</summary>
        private static float Hills(float x, float y, int salt, int period = 0)
        {
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float fx = x - x0, fy = y - y0;
            fx = fx * fx * (3 - 2 * fx);
            fy = fy * fy * (3 - 2 * fy);
            int x1 = x0 + 1, y1 = y0 + 1;
            if (period > 0)
            {
                x0 = (x0 % period + period) % period;
                y0 = (y0 % period + period) % period;
                x1 = (x1 % period + period) % period;
                y1 = (y1 % period + period) % period;
            }
            float low = Mathf.Lerp(Rand(x0, y0, salt), Rand(x1, y0, salt), fx);
            float high = Mathf.Lerp(Rand(x0, y1, salt), Rand(x1, y1, salt), fx);
            return Mathf.Lerp(low, high, fy);
        }

        /// <summary>
        /// Open ground: grass, rougher grass and bare earth in drifts, from photographs. Without
        /// the picture, a painted stand-in of soft green patches.
        /// </summary>
        private static Material GroundMaterial()
        {
            if (groundMaterial != null && groundMaterial.mainTexture != null) return groundMaterial;
            Texture2D photo = ArtKit.Photo("ground");
            if (photo != null)
            {
                groundMaterial = MeshKit.Textured(photo, Color.white, 0);
                return groundMaterial;
            }
            const int n = 256;
            Color lush = MeshKit.Hex(0x6e9753), grass = MeshKit.Hex(0x81a65b), dry = MeshKit.Hex(0xa6ad6b);
            var pixels = new Color32[n * n];
            for (int y = 0; y < n; y++)
            {
                for (int x = 0; x < n; x++)
                {
                    float u = x / (float)n, v = y / (float)n;
                    float t = Hills(u * 4, v * 4, 1, 4) * 0.55f + Hills(u * 12, v * 12, 2, 12) * 0.3f + Hills(u * 48, v * 48, 3, 48) * 0.15f;
                    t = Mathf.Clamp01((t - 0.5f) * 2.2f + 0.5f);
                    pixels[y * n + x] = t < 0.5f ? Color.Lerp(lush, grass, t * 2) : Color.Lerp(grass, dry, t * 2 - 1);
                }
            }
            groundTexture = new Texture2D(n, n, TextureFormat.RGBA32, true);
            groundTexture.SetPixels32(pixels);
            groundTexture.wrapMode = TextureWrapMode.Repeat;
            groundTexture.filterMode = FilterMode.Trilinear;
            groundTexture.Apply(true);
            groundMaterial = MeshKit.Lit(Color.white, 0);
            groundMaterial.mainTexture = groundTexture;
            return groundMaterial;
        }

        /// <summary>One big flat square, with the ground texture repeating across it.</summary>
        private static Mesh GroundMesh(float half)
        {
            var mesh = new Mesh { name = "Ground" };
            mesh.vertices = new[] { new Vector3(-half, 0, -half), new Vector3(-half, 0, half), new Vector3(half, 0, half), new Vector3(half, 0, -half) };
            mesh.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
            float r = half / GroundTile;
            mesh.uv = new[] { new Vector2(-r, -r), new Vector2(-r, r), new Vector2(r, r), new Vector2(r, -r) };
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            mesh.RecalculateBounds();
            return mesh;
        }

        // ------------------------------------------------------------ scenery

        private sealed class Patch
        {
            public int Signature;
            /// <summary>The tree tops and bushes, the rocks, and the shade on the ground under the trees. Any may be null.</summary>
            public Mesh Leaves, Rocks, Shade;
        }

        /// <summary>
        /// Scenery is built in square patches and kept from one set of roads to the next, so
        /// that drawing a road only rebuilds the patches it runs through.
        /// </summary>
        private static readonly Dictionary<long, Patch> patches = new Dictionary<long, Patch>();
        /// <summary>The ground is divided into squares this big, each holding at most one tree.</summary>
        private const double Plot = 15;
        /// <summary>How many squares along each side of a patch.</summary>
        private const int PatchPlots = 8;

        private static long Key(int x, int y)
        {
            return ((long)x << 32) ^ (uint)y;
        }

        /// <summary>
        /// Woods, bushes and rocks on the open ground. Each square of ground decides what
        /// grows on it from its own position alone, so the scenery stays where it is when
        /// the roads change, and only what a new road runs over disappears.
        /// </summary>
        private static void AddScenery(World world, Transform root)
        {
            List<ArtModel> rocks = ArtKit.Named("rock/");

            // Mark the ground the roads take up, in small squares, so any spot can be checked at once.
            const double cell = 5;
            var taken = new HashSet<long>();
            void Block(double x, double y, double reach)
            {
                int x0 = (int)System.Math.Floor((x - reach) / cell), x1 = (int)System.Math.Floor((x + reach) / cell);
                int y0 = (int)System.Math.Floor((y - reach) / cell), y1 = (int)System.Math.Floor((y + reach) / cell);
                for (int i = x0; i <= x1; i++) for (int j = y0; j <= y1; j++) taken.Add(Key(i, j));
            }
            bool Open(double x, double y)
            {
                return !taken.Contains(Key((int)System.Math.Floor(x / cell), (int)System.Math.Floor(y / cell)));
            }
            foreach (Lane lane in world.Lanes)
            {
                for (double s = 0; s <= lane.Length; s += 4)
                {
                    PathPose p = lane.Path.At(s);
                    Block(p.X, p.Y, 8.5);
                }
            }
            foreach (Node node in world.Nodes) Block(node.Pos.X, node.Pos.Y, node.Radius + 7);

            var holder = new GameObject("Scenery").transform;
            holder.SetParent(root, false);
            if (patches.Count > 900) patches.Clear();
            double size = Plot * PatchPlots;
            int reach = (int)System.Math.Ceiling((world.Extent + 130) / size);
            for (int px = -reach; px < reach; px++)
            {
                for (int py = -reach; py < reach; py++)
                {
                    // First work out what stands in this patch; build its mesh only if that has changed.
                    var plants = new List<Plant>();
                    int signature = 17;
                    for (int i = px * PatchPlots; i < (px + 1) * PatchPlots; i++)
                    {
                        for (int j = py * PatchPlots; j < (py + 1) * PatchPlots; j++)
                        {
                            double x = (i + Rand(i, j, 1)) * Plot, y = (j + Rand(i, j, 2)) * Plot;
                            // Woods come in drifts, with clearings between them.
                            float wood = Hills((float)x / 170, (float)y / 170, 11) * 0.65f + Hills((float)x / 60, (float)y / 60, 12) * 0.35f;
                            float chance = Mathf.Clamp01((wood - 0.47f) * 3.4f) * 0.9f + 0.03f;
                            if (Rand(i, j, 3) < chance && Open(x, y))
                            {
                                bool pine = Hills((float)x / 240, (float)y / 240, 13) > 0.62f;
                                bool autumn = Hills((float)x / 130, (float)y / 130, 14) > 0.5f && Rand(i, j, 4) < 0.7f;
                                int leaf = pine ? ArtKit.SummerLeaves + ArtKit.AutumnLeaves + (int)(Rand(i, j, 5) * ArtKit.PineLeaves)
                                    : autumn ? ArtKit.SummerLeaves + (int)(Rand(i, j, 5) * ArtKit.AutumnLeaves)
                                    : (int)(Rand(i, j, 5) * ArtKit.SummerLeaves);
                                // A dark-leaved tree is narrower and taller than a broad-leaved one.
                                plants.Add(new Plant { Kind = pine ? 1 : 0, X = x, Y = y, Turn = Rand(i, j, 7) * 6.2832f, Scale = 0.8f + 0.5f * Rand(i, j, 8), Leaf = leaf, Seed = i * 7919 + j });
                                signature = signature * 31 + (i * 7919 + j) * 2 + 1;
                            }
                            // Undergrowth: a bush in some squares, more of them in the woods, and now and then a rock.
                            double bx = (i + Rand(i, j, 9)) * Plot, by = (j + Rand(i, j, 10)) * Plot;
                            float pick = Rand(i, j, 15);
                            bool bush = pick < 0.06f + wood * 0.3f;
                            if ((bush || (pick > 0.97f && rocks.Count > 0)) && Open(bx, by))
                            {
                                plants.Add(new Plant { Kind = bush ? 2 : 3, Model = bush ? null : rocks[(int)(Rand(i, j, 16) * rocks.Count)], X = bx, Y = by, Turn = Rand(i, j, 17) * 6.2832f, Scale = 0.9f + 0.7f * Rand(i, j, 18), Leaf = (int)(Rand(i, j, 19) * ArtKit.SummerLeaves), Seed = i * 104729 + j });
                                signature = signature * 31 + (i * 7919 + j) * 2;
                            }
                        }
                    }
                    if (plants.Count == 0) continue;
                    Patch patch;
                    long key = Key(px, py);
                    if (!patches.TryGetValue(key, out patch) || patch.Signature != signature || (patch.Leaves == null && patch.Rocks == null))
                    {
                        if (patch != null) foreach (Mesh old in new[] { patch.Leaves, patch.Rocks, patch.Shade }) if (old != null) Object.Destroy(old);
                        var leaves = new CanopyBuilder();
                        var stones = new SceneryBuilder();
                        var shade = new ShadeBuilder();
                        foreach (Plant p in plants)
                        {
                            Vector3 at = MeshKit.ToWorld(p.X, p.Y);
                            if (p.Kind == 3) stones.Add(p.Model, at, p.Turn, p.Scale, 0);
                            else if (p.Kind == 2) leaves.Bush(at, 0.9f * p.Scale, p.Leaf, p.Seed);
                            else
                            {
                                float spread = (p.Kind == 1 ? 2.2f : 3.1f) * p.Scale, height = (p.Kind == 1 ? 10f : 7.5f) * p.Scale;
                                leaves.Tree(at, spread, height, p.Leaf, p.Seed);
                                // It is darker on the ground under a tree, whatever the sun is doing.
                                shade.Disc(p.X, p.Y, spread * 1.3, 0.05f, 0.34f);
                            }
                        }
                        patch = new Patch { Signature = signature, Leaves = leaves.Empty ? null : leaves.Build(), Rocks = stones.Count == 0 ? null : stones.Build(), Shade = shade.Empty ? null : shade.Build() };
                        patches[key] = patch;
                    }
                    if (patch.Leaves != null) MeshKit.Spawn("Leaves", patch.Leaves, ArtKit.Canopy, holder, true);
                    if (patch.Rocks != null) MeshKit.Spawn("Rocks", patch.Rocks, ArtKit.Shared, holder, true);
                    if (patch.Shade != null) MeshKit.Spawn("Shade", patch.Shade, MeshKit.Shade, holder, false).GetComponent<MeshRenderer>().receiveShadows = false;
                }
            }
        }

        private struct Plant
        {
            /// <summary>0 a broad-leaved tree, 1 a dark, narrow one, 2 a bush, 3 a rock.</summary>
            public int Kind;
            public int Seed;
            public ArtModel Model;
            public double X, Y;
            public float Turn, Scale;
            public int Leaf;
        }

        /// <summary>
        /// The signal for one approach, the American way: a pole on the far side of the
        /// intersection with an arm reaching over the road, and one signal head hung from it
        /// in line with each lane, so a driver waiting at the stop line looks across at it.
        /// The stop sign that replaces it stands at the roadside by the stop line.
        /// </summary>
        private SignalView BuildSignal(Approach arm, Material dark, Transform root)
        {
            var view = new SignalView { Arm = arm };

            // Both poles stand on the right of the road: local +x is the direction of travel
            // and local +z points left, across the road. The signal pole is across the
            // intersection from the stop line; the stop sign is beside the stop line.
            double far = 2 * world.Nodes[arm.Node].Radius;
            view.Heads = new GameObject("Signal");
            view.Heads.transform.SetParent(root, false);
            view.Heads.transform.position = MeshKit.ToWorld(arm.Post.X + System.Math.Cos(arm.H) * far, arm.Post.Y + System.Math.Sin(arm.H) * far);
            view.Heads.transform.rotation = MeshKit.Heading(arm.H);
            MeshKit.Spawn("Pole", MeshKit.Box(0.28f, 4.4f, 0.28f), dark, view.Heads.transform).transform.localPosition = new Vector3(0, 2.2f, 0);
            float reach = 0;
            foreach (int id in arm.Inbound)
            {
                Lane lane = world.Lanes[id];
                PathPose end = lane.Path.At(lane.Length);
                float across = (float)Geo.Hypot(end.X - arm.Post.X, end.Y - arm.Post.Y);
                reach = Mathf.Max(reach, across);
                var head = new LaneHead { Lane = lane };
                bool leftOnly = (lane.Allows(Turn.Left) || lane.Allows(Turn.UTurn)) && !lane.Allows(Turn.Straight) && !lane.Allows(Turn.Right);
                // Lamps face up so they read from above.
                if (leftOnly)
                {
                    // A lane that only turns left gets a single arrow.
                    MeshKit.Spawn("Housing", MeshKit.Box(1.5f, 0.5f, 1.5f), dark, view.Heads.transform).transform.localPosition = new Vector3(0, 4.5f, across);
                    MeshKit.Spawn("Face", MeshKit.Box(0.12f, 0.52f, 1.5f), face, view.Heads.transform, false).transform.localPosition = new Vector3(-0.81f, 4.5f, across);
                    head.Arrow = MeshKit.Unlit(DimRed);
                    var arrow = new FlatBuilder();
                    arrow.Fan(new Vec(0, 0), new[] { new Vec(-0.5, 0), new Vec(0.2, 0.55), new Vec(0.2, 0.2), new Vec(0.5, 0.2), new Vec(0.5, -0.2), new Vec(0.2, -0.2), new Vec(0.2, -0.55) }, 0);
                    // The arrow mesh points along local -x; turn it to point left across the road (+z).
                    Transform lamp = MeshKit.Spawn("Arrow", arrow.Build(), head.Arrow, view.Heads.transform, false).transform;
                    lamp.localPosition = new Vector3(0, 4.78f, across);
                    lamp.localRotation = Quaternion.Euler(0, 90, 0);
                    lamp.localScale = new Vector3(1.2f, 1, 1.2f);
                }
                else
                {
                    MeshKit.Spawn("Housing", MeshKit.Box(3.3f, 0.5f, 1.15f), dark, view.Heads.transform).transform.localPosition = new Vector3(0, 4.5f, across);
                    // A pale strip on the edge facing the drivers it controls, to tell it from the signal for the other direction.
                    MeshKit.Spawn("Face", MeshKit.Box(0.12f, 0.52f, 1.15f), face, view.Heads.transform, false).transform.localPosition = new Vector3(-1.71f, 4.5f, across);
                    head.Lamps = new Material[3];
                    for (int i = 0; i < 3; i++)
                    {
                        head.Lamps[i] = MeshKit.Unlit(DimRed);
                        MeshKit.Spawn("Lamp", MeshKit.Box(0.8f, 0.06f, 0.8f), head.Lamps[i], view.Heads.transform, false).transform.localPosition = new Vector3(-1.05f + i * 1.05f, 4.78f, across);
                    }
                }
                view.Lanes.Add(head);
            }
            // The arm reaches out over the farthest lane.
            MeshKit.Spawn("Arm", MeshKit.Box(0.16f, 0.16f, reach), dark, view.Heads.transform).transform.localPosition = new Vector3(0, 4.3f, reach / 2);

            view.StopSign = new GameObject("Stop sign");
            view.StopSign.transform.SetParent(root, false);
            view.StopSign.transform.position = MeshKit.ToWorld(arm.Post.X, arm.Post.Y);
            view.StopSign.transform.rotation = MeshKit.Heading(arm.H);
            MeshKit.Spawn("Pole", MeshKit.Box(0.28f, 4.4f, 0.28f), dark, view.StopSign.transform).transform.localPosition = new Vector3(0, 2.2f, 0);
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
            // Meshes made for these roads would otherwise pile up, one set for every change to the map.
            // The scenery patches are kept, because the next set of roads reuses them.
            var kept = new HashSet<Mesh>();
            foreach (Patch patch in patches.Values) foreach (Mesh mesh in new[] { patch.Leaves, patch.Rocks, patch.Shade }) if (mesh != null) kept.Add(mesh);
            foreach (MeshFilter filter in rootObject.GetComponentsInChildren<MeshFilter>(true))
            {
                Mesh mesh = filter.sharedMesh;
                if (mesh != null && !kept.Contains(mesh)) Object.Destroy(mesh);
            }
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

        /// <summary>The light a lane shows: the best of the turns it allows.</summary>
        private static SignalLight LaneLight(Junction jn, Lane lane)
        {
            SignalLight best = SignalLight.Red;
            for (int t = 0; t < Lane.Turns; t++)
            {
                if (lane.Connectors[t] < 0) continue;
                SignalLight st = jn.State(lane.Arm, (Turn)t);
                if (st == SignalLight.Stop) return SignalLight.Stop;
                if (st == SignalLight.Green) best = SignalLight.Green;
                else if (st == SignalLight.Yellow && best == SignalLight.Red) best = SignalLight.Yellow;
            }
            return best;
        }

        /// <summary>Show the current lights.</summary>
        public void Update(List<Junction> junctions, double time)
        {
            bool flash = time % 1 < 0.5;
            foreach (LineView line in lines)
            {
                SignalLight light = LaneLight(junctions[line.Lane.Node], line.Lane);
                Color c = LampColor(light);
                if (light == SignalLight.Stop && !flash) c *= 0.45f;
                line.Material.color = c;
            }
            foreach (SignalView s in signals)
            {
                Junction jn = junctions[s.Arm.Node];
                bool stop = jn.Mode == JunctionMode.AllWay;
                s.Heads.SetActive(!stop);
                s.StopSign.SetActive(stop);
                if (stop) continue;
                foreach (LaneHead head in s.Lanes)
                {
                    SignalLight st = LaneLight(jn, head.Lane);
                    if (head.Arrow != null)
                    {
                        head.Arrow.color = LampColor(st);
                        continue;
                    }
                    head.Lamps[0].color = st == SignalLight.Red ? Red : DimRed;
                    head.Lamps[1].color = st == SignalLight.Yellow ? Yellow : DimYellow;
                    head.Lamps[2].color = st == SignalLight.Green ? Green : DimGreen;
                }
            }
        }
    }
}
