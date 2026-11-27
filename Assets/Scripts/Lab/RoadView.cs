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
            /// <summary>The light the bar throws on the road round it, in the same color.</summary>
            public Material Glow;
        }

        /// <summary>One signal head, hung over the lane it controls.</summary>
        private sealed class LaneHead
        {
            public Lane Lane;
            /// <summary>Red, yellow, green; or a single arrow lamp for a lane that only turns left.</summary>
            public Material[] Lamps;
            public Material Arrow;
            /// <summary>The glow round whichever lamp is lit, which is what shows the light from far overhead.</summary>
            public Transform Halo;
            public Material HaloGlow;
            public float Across;
        }

        private sealed class SignalView
        {
            public Approach Arm;
            public GameObject Heads;
            public GameObject StopSign;
            public GameObject YieldSign;
            public List<LaneHead> Lanes = new List<LaneHead>();
        }

        private readonly World world;
        private readonly List<LineView> lines = new List<LineView>();
        private readonly List<SignalView> signals = new List<SignalView>();
        private readonly Transform nodeRing;
        private readonly GameObject rootObject;
        // What changes between day and night: the street lamps and the light they throw, and the signs, which give no light of their own.
        private Material lampLens, lampLight, signWhite, signRed;
        private GameObject lampPools;
        private float night;

        public RoadView(World world, Transform parent)
        {
            this.world = world;
            rootObject = new GameObject("Roads");
            Transform root = rootObject.transform;
            root.SetParent(parent, false);

            MeshKit.Spawn("Ground", GroundMesh(3000), GroundMaterial(), root, false);
            AddWater(world, root);

            // What the roads are made of. Asphalt and gravel take their texture from where they lie
            // on the map; a sidewalk takes it from along its own length, a slab at a time.
            Texture2D asphaltPhoto = ArtKit.Photo("asphalt"), walkPhoto = ArtKit.Photo("sidewalk"), gravelPhoto = ArtKit.Photo("gravel");
            var asphalt = new SurfaceBuilder(64);
            var deck = new SurfaceBuilder(64);
            var walks = new SurfaceBuilder(1);
            var shoulders = new SurfaceBuilder(12);
            var lawn = new SurfaceBuilder(GroundTile);
            var verge = new GlowBuilder();
            var lampPosts = new SolidBuilder();
            var lampHeads = new SolidBuilder();
            var pools = new GlowBuilder();
            var skirt = new FlatBuilder();
            var buried = new FlatBuilder();
            var mouths = new FlatBuilder();

            // Where the lanes are, so that a sidewalk can be kept off the next road at a sharp corner.
            var lanePoints = new Dictionary<long, List<Vec>>();
            foreach (Lane lane in world.Lanes)
            {
                if (!Sidewalks || lane.Kind != LaneKind.Road) continue;
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
            // The grass beside a road is mown: a strip a little lighter than the rest, fading out away from the road.
            void Verge(IList<Vec> from, int side)
            {
                verge.Band(from, Outward(from, side * VergeWidth), 0.012f, VergeTint, Color.black);
            }
            // The roadside along one edge of the pavement: a gravel shoulder on a highway, and on slower
            // roads a low concrete curb (or, when sidewalks are on, a raised sidewalk behind a full one).
            // Side is 1 for the right of the line, -1 for the left.
            void Roadside(IList<Vec> edge, int side, bool fast)
            {
                if (edge.Count < 2) return;
                if (fast)
                {
                    Vec[] outer = Geo.Offset(new List<Vec>(edge), side * ShoulderWidth);
                    if (side > 0) shoulders.Strip(edge, outer, 0.022f);
                    else shoulders.Strip(outer, edge, 0.022f);
                    Verge(outer, side);
                    return;
                }
                if (!Sidewalks)
                {
                    // Only a lip of concrete along the edge of the pavement: enough to finish the road, not a curb to step up.
                    Vec[] lip = Geo.Offset(new List<Vec>(edge), side * CurbWidth);
                    walks.Ribbon(edge, lip, CurbLip, 1.6f);
                    walks.Wall(edge, 0.02f, CurbLip, 1.6f, -side);
                    walks.Wall(lip, 0, CurbLip, 1.6f, side);
                    Verge(lip, side);
                    return;
                }
                var list = new List<Vec>(edge);
                Vec[] full = Geo.Offset(list, side * WalkWidth), slim = Geo.Offset(list, side * 0.35);
                var far = new Vec[edge.Count];
                for (int i = 0; i < far.Length; i++) far[i] = OnALane(full[i]) ? slim[i] : full[i];
                walks.Ribbon(edge, far, CurbHeight, 1.6f);
                walks.Wall(edge, 0.02f, CurbHeight, 1.6f, -side);
            }

            bool anyBridge = false, anyTunnel = false;
            int lamps = 0;
            foreach (Strip s in world.Surfaces)
            {
                bool fast = s.Speed >= FastRoad;
                if (s.Heights == null)
                {
                    asphalt.Strip(s.Left, s.Right, 0.03f);
                    Roadside(s.Right, 1, fast);
                    if (s.LeftIsEdge) Roadside(s.Left, -1, fast);
                    if (s.Speed >= LampRoad && !fast) StreetLamps(s.Right, lamps++ % 2 == 0 ? LampSpacing * 0.25 : LampSpacing * 0.75, lampPosts, lampHeads, pools);
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
                asphalt.Fan(node.Hub, node.Box, 0.03f);
                double fastest = 0;
                foreach (Lane lane in world.Lanes) if (lane.Kind == LaneKind.Road && lane.ApproachNode == node.Id) fastest = System.Math.Max(fastest, lane.SpeedLimit);
                foreach (Vec[] corner in node.Corners) Roadside(corner, 1, fastest >= FastRoad);
            }
            // The roads are newly laid: dark, even asphalt with nothing worn into it, and bright paint.
            Material asphaltMaterial = MeshKit.Textured(asphaltPhoto, asphaltPhoto != null ? Color.white : MeshKit.Hex(0x3c3e42), 0.1f);
            MeshKit.Spawn("Asphalt", asphalt.Build(), asphaltMaterial, root, false);
            if (!shoulders.Empty) MeshKit.Spawn("Shoulders", shoulders.Build(), MeshKit.Textured(gravelPhoto, gravelPhoto != null ? Color.white : MeshKit.Hex(0x89816f), 0), root, false);
            if (!verge.Empty) MeshKit.Spawn("Verges", verge.Build(), MeshKit.Brighten, root, false).GetComponent<MeshRenderer>().receiveShadows = false;
            if (!lampPosts.Empty)
            {
                // Street lamps along the avenues. By day the lamp is a pale lens; after dark it is lit, with a pool of light under it.
                lampLens = MeshKit.Unlit(LensByDay);
                lampLight = MeshKit.Glow(MeshKit.Hex(0xffd591, 0));
                MeshKit.Spawn("Lamp posts", lampPosts.Build(), MeshKit.Lit(MeshKit.Hex(0x2a2e33), 0.25f), root, true);
                MeshKit.Spawn("Lamps", lampHeads.Build(), lampLens, root, false);
                lampPools = MeshKit.Spawn("Lamp light", pools.Build(), lampLight, root, false);
                lampPools.GetComponent<MeshRenderer>().receiveShadows = false;
                lampPools.SetActive(false);
            }
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

            if (world.Medians.Count > 0)
            {
                // The strip down the middle of a divided road: grass behind a curb, with a row of bushes along it, or of small trees where the player asked for trees.
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
                        // And across each end, which shows where a median stops short to make way for a left-turn lane.
                        walks.Wall(new[] { l[0], r[0] }, 0.02f, CurbHeight, 1.6f, 1);
                        walks.Wall(new[] { l[l.Length - 1], r[r.Length - 1] }, 0.02f, CurbHeight, 1.6f, -1);
                    }
                    if (m.Heights != null) continue;
                    for (int i = 9; i < m.Left.Length - 9; i += m.Planted ? 13 : 11)
                    {
                        var at = MeshKit.ToWorld((m.Left[i].X + m.Right[i].X) / 2, (m.Left[i].Y + m.Right[i].Y) / 2, CurbHeight);
                        // Street trees: small enough that their tops stay over the median and clear of the lanes.
                        if (m.Planted) plants.Tree(at, 1.35f, 4.6f, planted % ArtKit.SummerLeaves, planted * 7 + 3);
                        else plants.Bush(at, 0.75f, planted % ArtKit.SummerLeaves, planted * 7 + 3);
                        planted++;
                    }
                }
                MeshKit.Spawn("Medians", lawn.Build(), GroundMaterial(), root, false);
                if (planted > 0) MeshKit.Spawn("Median plants", plants.Build(), ArtKit.Canopy, root, true);
            }

            // Built last of the surfaces, so that it holds the curbs round the medians as well as any sidewalks.
            if (!walks.Empty) MeshKit.Spawn("Curbs", walks.Build(), MeshKit.Textured(walkPhoto, walkPhoto != null ? Color.white : MeshKit.Hex(0xc6c4be), 0.05f), root, true);

            var white = new FlatBuilder();
            var yellow = new FlatBuilder();
            var red = new FlatBuilder();
            foreach (Marking m in world.Markings)
            {
                FlatBuilder b = m.Yellow ? yellow : white;
                if (m.Heights != null && m.Peak > 0)
                {
                    // On a bridge the paint follows the deck up and over.
                    if (m.DashOn > 0) b.Dashed(m.Pts, m.Heights, m.Width * PaintWidth, m.DashOn, m.DashOff, 0.07f);
                    else b.Line(m.Pts, m.Heights, m.Width * PaintWidth, 0.07f);
                    continue;
                }
                // In a tunnel only the open ends are painted.
                List<int[]> runs = m.Heights != null ? Runs(m.Heights, false) : null;
                if (runs == null) runs = new List<int[]> { new[] { 0, m.Pts.Length - 1 } };
                foreach (int[] run in runs)
                {
                    Vec[] pts = Part(m.Pts, run);
                    if (m.DashOn > 0) b.Dashed(pts, m.Width * PaintWidth, m.DashOn, m.DashOff, 0.07f);
                    else b.Line(pts, m.Width * PaintWidth, 0.07f);
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
            // Fresh paint: clean white with a little warmth in it, and a warm yellow.
            MeshKit.Spawn("White paint", white.Build(), MeshKit.Lit(MeshKit.Hex(0xf4f1e6), 0), root, false);
            MeshKit.Spawn("Yellow paint", yellow.Build(), MeshKit.Lit(MeshKit.Hex(0xefbd2e), 0), root, false);
            MeshKit.Spawn("Red paint", red.Build(), MeshKit.Lit(MeshKit.Hex(0xe5383b), 0), root, false);

            foreach (StopLine sl in world.StopLines)
            {
                var back = new Vec(-System.Math.Cos(sl.H) * 0.55, -System.Math.Sin(sl.H) * 0.55);
                var b = new FlatBuilder();
                b.Strip(new[] { sl.A, sl.B }, new[] { new Vec(sl.A.X + back.X, sl.A.Y + back.Y), new Vec(sl.B.X + back.X, sl.B.Y + back.Y) }, 0.09f);
                var view = new LineView { Lane = world.Lanes[sl.Lane], Material = MeshKit.Unlit(Color.white), Glow = MeshKit.Glow(Color.white) };
                MeshKit.Spawn("Stop line", b.Build(), view.Material, root, false);
                // The bar is a light, and lights the road a little way to each side of it.
                var glow = new GlowBuilder();
                Vec[] middle = { new Vec(sl.A.X + back.X / 2, sl.A.Y + back.Y / 2), new Vec(sl.B.X + back.X / 2, sl.B.Y + back.Y / 2) };
                foreach (double reach in new[] { 1.5, -1.5 })
                {
                    var far = new Vec(back.X / 0.55 * reach, back.Y / 0.55 * reach);
                    glow.Band(middle, new[] { new Vec(middle[0].X + far.X, middle[0].Y + far.Y), new Vec(middle[1].X + far.X, middle[1].Y + far.Y) }, 0.1f, new Color(1, 1, 1, 0.7f), new Color(1, 1, 1, 0));
                }
                MeshKit.Spawn("Stop line light", glow.Build(), view.Glow, root, false).GetComponent<MeshRenderer>().receiveShadows = false;
                lines.Add(view);
            }

            // A road that stops is not fenced off: it is a road nobody has carried on yet, and it looks like one.
            // (The player had a red and white board across each end and asked for it to go.)

            Material dark = MeshKit.Lit(MeshKit.Hex(0x15181c), 0.3f);
            signWhite = MeshKit.Unlit(Color.white);
            signRed = MeshKit.Unlit(SignRed);
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

        /// <summary>A road this fast (meters per second) has gravel shoulders.</summary>
        private const double FastRoad = 24;
        /// <summary>
        /// Whether slower roads get a raised sidewalk and curb along each side and round each
        /// corner. Off for now: the plan is for the zones a city is built from to lay sidewalks
        /// where they belong, not for every road to have them.
        /// </summary>
        private static readonly bool Sidewalks = false;
        private const double WalkWidth = 2.2, ShoulderWidth = 2.6, LaneReach = 2.1;
        private const float CurbHeight = 0.16f;
        /// <summary>The lip of concrete along the edge of a road: how wide it is, and how far its top stands above the ground.</summary>
        private const double CurbWidth = 0.22;
        private const float CurbLip = 0.075f;
        /// <summary>How far the mown grass beside a road reaches, and how much lighter it is at the road's edge (as red, green, blue).</summary>
        private const double VergeWidth = 2.4;
        private static readonly Color VergeTint = new Color(0.15f, 0.11f, 0.01f, 1);
        /// <summary>
        /// Paint is drawn this much wider than the simulation's lines, which are the true width
        /// of a painted line. At the true width they come out a pixel wide from the usual distance and look like a plan.
        /// </summary>
        private const double PaintWidth = 1.4;
        /// <summary>Roads at least this fast (meters per second) that are not highways have street lamps: avenues, not side streets.</summary>
        private const double LampRoad = 14;
        private const double LampSpacing = 46;
        private const float LampHeight = 8;
        private static readonly Color LensByDay = MeshKit.Hex(0xd9ddd6), LensByNight = MeshKit.Hex(0xffe9b8), SignRed = MeshKit.Hex(0xe5383b);

        /// <summary>
        /// The line so far to one side of another (to its right, or to its left if negative).
        /// Where that would fold back over itself, on the inside of a tight corner, the fold is gathered to a point.
        /// </summary>
        private static Vec[] Outward(IList<Vec> line, double offset)
        {
            Vec[] outer = Geo.Offset(new List<Vec>(line), offset);
            bool Folded(int i)
            {
                return (outer[i + 1].X - outer[i].X) * (line[i + 1].X - line[i].X) + (outer[i + 1].Y - outer[i].Y) * (line[i + 1].Y - line[i].Y) < 0;
            }
            for (int i = 0; i + 1 < outer.Length; i++)
            {
                if (!Folded(i)) continue;
                int last = i + 1;
                while (last + 1 < outer.Length && Folded(last)) last++;
                double x = 0, y = 0;
                for (int k = i; k <= last; k++)
                {
                    x += outer[k].X;
                    y += outer[k].Y;
                }
                var mean = new Vec(x / (last - i + 1), y / (last - i + 1));
                for (int k = i; k <= last; k++) outer[k] = mean;
                i = last;
            }
            return outer;
        }

        /// <summary>
        /// Street lamps along one edge of a road: a post every so often just behind the curb,
        /// with an arm reaching out over the pavement and a lamp at the end of it. The first
        /// stands so far along, so that the two sides of a road can be set out of step.
        /// </summary>
        private static void StreetLamps(Vec[] edge, double first, SolidBuilder posts, SolidBuilder heads, GlowBuilder pools)
        {
            var path = new LanePath(edge);
            // Keep clear of the intersection at each end. A short road gets one lamp, in the middle.
            const double clear = 14;
            if (path.Length < 2 * clear + 6) return;
            if (path.Length < 2 * clear + LampSpacing) first = path.Length / 2;
            for (double s = System.Math.Max(first, clear); s <= path.Length - clear; s += LampSpacing)
            {
                PathPose at = path.At(s);
                // Out from the road is to the right of the way the edge runs.
                double ox = System.Math.Sin(at.H), oy = -System.Math.Cos(at.H);
                Quaternion turn = MeshKit.Heading(System.Math.Atan2(oy, ox));
                Vector3 foot = MeshKit.ToWorld(at.X + ox * 0.9, at.Y + oy * 0.9);
                Vector3 reach = MeshKit.ToWorld(-ox, -oy);
                posts.Box(foot + Vector3.up * (LampHeight / 2), new Vector3(0.2f, LampHeight, 0.2f), turn);
                posts.Box(foot + Vector3.up * (LampHeight - 0.08f) + reach * 1.3f, new Vector3(2.8f, 0.14f, 0.14f), turn);
                Vector3 head = foot + Vector3.up * (LampHeight - 0.2f) + reach * 2.5f;
                posts.Box(head + Vector3.up * 0.07f, new Vector3(1.0f, 0.12f, 0.46f), turn);
                heads.Box(head - Vector3.up * 0.03f, new Vector3(0.8f, 0.08f, 0.34f), turn);
                // After dark: light on the road beneath, and a glow at the lamp itself.
                pools.Pool(new Vector3(head.x, 0.12f, head.z), 16, 0.5f);
                pools.Ball(head - Vector3.up * 0.1f, 2.2f, 0.9f);
            }
        }

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
        /// Open ground: even green grass, from a photograph. Without the picture, a painted
        /// stand-in of much the same green.
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
            Color lush = MeshKit.Hex(0x5f9843), grass = MeshKit.Hex(0x68a04a), dry = MeshKit.Hex(0x71a851);
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

        // ------------------------------------------------------------ water

        /// <summary>
        /// Lakes and rivers: flat water lying just above the ground, with a strip of sand round
        /// the shore. The land has no height, so the water has no depth to show: it is a sheet
        /// of blue that roads go over on bridges.
        /// </summary>
        private static void AddWater(World world, Transform root)
        {
            if (world.Waters.Count == 0) return;
            var verts = new List<Vector3>();
            var normals = new List<Vector3>();
            var tris = new List<int>();
            var sand = new FlatBuilder();
            foreach (Vec[] shape in world.Waters)
            {
                int first = verts.Count;
                foreach (Vec p in shape)
                {
                    verts.Add(MeshKit.ToWorld(p.X, p.Y, 0.018f));
                    normals.Add(Vector3.up);
                }
                List<int> cut = Geo.Triangulate(shape);
                for (int i = 0; i + 2 < cut.Count; i += 3)
                {
                    // Both ways round, so it shows whichever way the pipeline counts a face.
                    tris.Add(first + cut[i]); tris.Add(first + cut[i + 1]); tris.Add(first + cut[i + 2]);
                    tris.Add(first + cut[i]); tris.Add(first + cut[i + 2]); tris.Add(first + cut[i + 1]);
                }
                // The shore: round the outside of the shape, which is to the right of its corners if they run counter-clockwise.
                var round = new List<Vec>(shape) { shape[0] };
                sand.Strip(round, Outward(round, Geo.Area2(shape) > 0 ? ShoreWidth : -ShoreWidth), 0.01f);
            }
            var mesh = new Mesh();
            if (verts.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            MeshKit.Spawn("Shore", sand.Build(), MeshKit.Lit(MeshKit.Hex(0xc9bb8e), 0.05f), root, false);
            MeshKit.Spawn("Water", mesh, MeshKit.Lit(MeshKit.Hex(0x3f8fc9), 0.85f), root, false);
        }

        private const double ShoreWidth = 2.5;

        // ------------------------------------------------------------ scenery

        private sealed class Patch
        {
            public int Signature;
            /// <summary>The tree tops and bushes, the rocks, the shade on the ground under the trees, and the wild flowers. Any may be null.</summary>
            public Mesh Leaves, Rocks, Shade, Flowers;
        }

        /// <summary>One wild flower: where it is, how big, and which of the flower colors.</summary>
        private struct Bloom
        {
            public double X, Y;
            public float Size;
            public int Color;
        }

        private static readonly int[] FlowerColors = { 0xf7f5ea, 0xf6d24a, 0xe98fb4, 0xb9a1e6 };
        private static Material flowerMaterial;

        /// <summary>One material for every flower: a strip of the flower colors, and each flower is mapped to its own.</summary>
        private static Material FlowerMaterial()
        {
            if (flowerMaterial != null && flowerMaterial.mainTexture != null) return flowerMaterial;
            var strip = new Texture2D(FlowerColors.Length, 1, TextureFormat.RGBA32, false);
            for (int i = 0; i < FlowerColors.Length; i++) strip.SetPixel(i, 0, MeshKit.Hex(FlowerColors[i]));
            strip.filterMode = FilterMode.Point;
            strip.wrapMode = TextureWrapMode.Clamp;
            strip.Apply();
            flowerMaterial = MeshKit.Textured(strip, Color.white, 0);
            return flowerMaterial;
        }

        /// <summary>The flowers of one patch as a mesh: each a small flat shape just above the grass, seen from above as a dot of color.</summary>
        private static Mesh FlowerMesh(List<Bloom> blooms)
        {
            var verts = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            foreach (Bloom b in blooms)
            {
                int k = verts.Count;
                float x = (float)b.X, z = (float)b.Y, y = 0.16f + b.Size * 0.4f;
                verts.Add(new Vector3(x - b.Size, y, z));
                verts.Add(new Vector3(x, y, z + b.Size));
                verts.Add(new Vector3(x + b.Size, y, z));
                verts.Add(new Vector3(x, y, z - b.Size));
                for (int i = 0; i < 4; i++)
                {
                    normals.Add(Vector3.up);
                    uvs.Add(new Vector2((b.Color + 0.5f) / FlowerColors.Length, 0.5f));
                }
                // Both ways round, so they show from above whichever way the pipeline counts a face.
                tris.AddRange(new[] { k, k + 1, k + 2, k, k + 2, k + 3, k, k + 2, k + 1, k, k + 3, k + 2 });
            }
            var mesh = new Mesh();
            if (verts.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
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
            // Nothing grows in the water, or on the sand beside it.
            foreach (Vec[] water in world.Waters)
            {
                double x0 = double.PositiveInfinity, x1 = double.NegativeInfinity, y0 = double.PositiveInfinity, y1 = double.NegativeInfinity;
                foreach (Vec p in water)
                {
                    x0 = System.Math.Min(x0, p.X);
                    x1 = System.Math.Max(x1, p.X);
                    y0 = System.Math.Min(y0, p.Y);
                    y1 = System.Math.Max(y1, p.Y);
                    Block(p.X, p.Y, ShoreWidth + 3);
                }
                for (int i = (int)System.Math.Floor(x0 / cell); i <= (int)System.Math.Floor(x1 / cell); i++)
                {
                    for (int j = (int)System.Math.Floor(y0 / cell); j <= (int)System.Math.Floor(y1 / cell); j++)
                    {
                        if (Geo.Inside(new Vec((i + 0.5) * cell, (j + 0.5) * cell), water)) Block((i + 0.5) * cell, (j + 0.5) * cell, cell * 0.6);
                    }
                }
            }

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
                    var blooms = new List<Bloom>();
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
                                // It is summer: every tree is green, in shades close enough to read as one color.
                                int leaf = (int)(Rand(i, j, 5) * ArtKit.SummerLeaves);
                                // Some are narrower and taller than the rest.
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
                            // Wild flowers, out in the open and in drifts: here a scatter of them, there none for a long way.
                            float drift = Hills((float)(i * Plot) / 110, (float)(j * Plot) / 110, 21);
                            if (Rand(i, j, 22) < Mathf.Clamp01((drift - 0.52f) * 2.4f) * 0.55f + 0.015f && wood < 0.6f)
                            {
                                double fx = (i + Rand(i, j, 23)) * Plot, fy = (j + Rand(i, j, 24)) * Plot;
                                int count = 9 + (int)(Rand(i, j, 25) * 14), shown = 0, mostly = (int)(Rand(i, j, 26) * FlowerColors.Length);
                                float spread = 2.5f + 4 * Rand(i, j, 27);
                                for (int k = 0; k < count; k++)
                                {
                                    float turn = Rand(i * 131 + k, j, 28) * 6.2832f, away = Mathf.Sqrt(Rand(i, j * 131 + k, 29)) * spread;
                                    double x1 = fx + Mathf.Cos(turn) * away, y1 = fy + Mathf.Sin(turn) * away;
                                    if (!Open(x1, y1)) continue;
                                    // Most of a scatter is one color, with a few of the others among it.
                                    int color = Rand(i + k, j - k, 30) < 0.75f ? mostly : (int)(Rand(i - k, j + k, 31) * FlowerColors.Length);
                                    blooms.Add(new Bloom { X = x1, Y = y1, Size = 0.2f + 0.14f * Rand(i + k * 17, j, 32), Color = color });
                                    shown++;
                                }
                                signature = signature * 31 + (i * 7919 + j) * 5 + shown;
                            }
                        }
                    }
                    if (plants.Count == 0 && blooms.Count == 0) continue;
                    Patch patch;
                    long key = Key(px, py);
                    if (!patches.TryGetValue(key, out patch) || patch.Signature != signature || (patch.Leaves == null && patch.Rocks == null && patch.Flowers == null))
                    {
                        if (patch != null) foreach (Mesh old in new[] { patch.Leaves, patch.Rocks, patch.Shade, patch.Flowers }) if (old != null) Object.Destroy(old);
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
                        patch = new Patch { Signature = signature, Leaves = leaves.Empty ? null : leaves.Build(), Rocks = stones.Count == 0 ? null : stones.Build(), Shade = shade.Empty ? null : shade.Build(), Flowers = blooms.Count == 0 ? null : FlowerMesh(blooms) };
                        patches[key] = patch;
                    }
                    if (patch.Leaves != null) MeshKit.Spawn("Leaves", patch.Leaves, ArtKit.Canopy, holder, true);
                    if (patch.Rocks != null) MeshKit.Spawn("Rocks", patch.Rocks, ArtKit.Shared, holder, true);
                    if (patch.Shade != null) MeshKit.Spawn("Shade", patch.Shade, MeshKit.Shade, holder, false).GetComponent<MeshRenderer>().receiveShadows = false;
                    if (patch.Flowers != null) MeshKit.Spawn("Flowers", patch.Flowers, FlowerMaterial(), holder, false);
                }
            }
        }

        private struct Plant
        {
            /// <summary>0 a broad tree, 1 a tall, narrow one, 2 a bush, 3 a rock.</summary>
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
        /// in line with each lane, upright and facing the drivers it is for, so a driver
        /// waiting at the stop line looks across at it.
        /// The stop sign that replaces it stands at the roadside by the stop line.
        /// </summary>
        private SignalView BuildSignal(Approach arm, Material dark, Transform root)
        {
            var view = new SignalView { Arm = arm };

            // Both poles stand on the right of the road: local +x is the direction of travel
            // and local +z points left, across the road. Where a road carries straight on across
            // the intersection, the signal hangs over the mouth of that road, facing the drivers
            // it controls, as American signals do. Where none does (the stem of a T, an arm of a
            // Y) there is nothing over there to hang it on, so it stands at the end of its own
            // road, just past the stop line. The stop sign is always beside the stop line.
            Node junction = world.Nodes[arm.Node];
            double far = 2.2;
            foreach (Approach other in junction.Arms)
            {
                if (other == arm) continue;
                double off = other.Angle - arm.H;
                if (System.Math.Abs(System.Math.Atan2(System.Math.Sin(off), System.Math.Cos(off))) > 0.45) continue;
                // As far along as the middle of that road's mouth.
                double mx = junction.Pos.X + System.Math.Cos(other.Angle) * junction.Radius - arm.Post.X, my = junction.Pos.Y + System.Math.Sin(other.Angle) * junction.Radius - arm.Post.Y;
                far = System.Math.Max(far, mx * System.Math.Cos(arm.H) + my * System.Math.Sin(arm.H));
            }
            view.Heads = new GameObject("Signal");
            view.Heads.transform.SetParent(root, false);
            view.Heads.transform.position = MeshKit.ToWorld(arm.Post.X + System.Math.Cos(arm.H) * far, arm.Post.Y + System.Math.Sin(arm.H) * far);
            view.Heads.transform.rotation = MeshKit.Heading(arm.H);
            MeshKit.Spawn("Pole", MeshKit.Box(0.3f, 6.6f, 0.3f), dark, view.Heads.transform).transform.localPosition = new Vector3(0, 3.3f, 0);
            float reach = 0;
            foreach (int id in arm.Inbound)
            {
                Lane lane = world.Lanes[id];
                PathPose end = lane.Path.At(lane.Length);
                float across = (float)Geo.Hypot(end.X - arm.Post.X, end.Y - arm.Post.Y);
                reach = Mathf.Max(reach, across);
                var head = new LaneHead { Lane = lane };
                bool leftOnly = (lane.Allows(Turn.Left) || lane.Allows(Turn.UTurn)) && !lane.Allows(Turn.Straight) && !lane.Allows(Turn.Right);
                // Each head hangs upright over its lane with its lamps toward the traffic it controls (local -x),
                // red at the top. From straight overhead only the housing shows: the bar at the stop line tells the state there.
                if (leftOnly)
                {
                    // A lane that only turns left gets a single arrow.
                    MeshKit.Spawn("Housing", MeshKit.Box(0.42f, 1.5f, 1.5f), dark, view.Heads.transform).transform.localPosition = new Vector3(0, 5.65f, across);
                    head.Arrow = MeshKit.Unlit(DimRed);
                    head.Across = across;
                    var arrow = new FlatBuilder();
                    arrow.Fan(new Vec(0, 0), new[] { new Vec(-0.5, 0), new Vec(0.2, 0.55), new Vec(0.2, 0.2), new Vec(0.5, 0.2), new Vec(0.5, -0.2), new Vec(0.2, -0.2), new Vec(0.2, -0.55) }, 0);
                    // The arrow is drawn flat, pointing along -x. Stand it up on the face of the housing, pointing to the driver's left (+z).
                    Transform lamp = MeshKit.Spawn("Arrow", arrow.Build(), head.Arrow, view.Heads.transform, false).transform;
                    lamp.localPosition = new Vector3(-0.23f, 5.65f, across);
                    lamp.localRotation = Quaternion.LookRotation(Vector3.up, Vector3.left);
                    lamp.localScale = new Vector3(1.2f, 1, 1.2f);
                }
                else
                {
                    MeshKit.Spawn("Housing", MeshKit.Box(0.42f, 2.7f, 1.0f), dark, view.Heads.transform).transform.localPosition = new Vector3(0, 5.05f, across);
                    head.Lamps = new Material[3];
                    head.Across = across;
                    for (int i = 0; i < 3; i++)
                    {
                        head.Lamps[i] = MeshKit.Unlit(DimRed);
                        MeshKit.Spawn("Lamp", MeshKit.Box(0.1f, 0.72f, 0.72f), head.Lamps[i], view.Heads.transform, false).transform.localPosition = new Vector3(-0.24f, 5.91f - i * 0.86f, across);
                    }
                }
                // The glow of the lamp that is lit. It stands a little out from the face of the head, toward the traffic.
                head.HaloGlow = MeshKit.Glow(Color.white);
                GameObject halo = MeshKit.Spawn("Glow", MeshKit.GlowBall, head.HaloGlow, view.Heads.transform, false);
                halo.GetComponent<MeshRenderer>().receiveShadows = false;
                head.Halo = halo.transform;
                head.Halo.localScale = new Vector3(1.7f, 1.7f, 1.7f);
                view.Lanes.Add(head);
            }
            // A no-turn-on-red sign: a white plate with a red ball, on the pole where the driver at the line looks for the light.
            if (arm.NoTurnOnRed)
            {
                MeshKit.Spawn("No turn on red", MeshKit.Box(0.06f, 1.0f, 0.8f), signWhite, view.Heads.transform, false).transform.localPosition = new Vector3(-0.19f, 4.3f, 0);
                Transform ball = MeshKit.Spawn("Red ball", Octagon(0.2f), signRed, view.Heads.transform, false).transform;
                ball.localPosition = new Vector3(-0.23f, 4.5f, 0);
                ball.localRotation = Quaternion.LookRotation(Vector3.up, Vector3.left);
            }
            // The arm reaches out over the farthest lane, and the heads hang from it.
            MeshKit.Spawn("Arm", MeshKit.Box(0.18f, 0.18f, reach + 0.5f), dark, view.Heads.transform).transform.localPosition = new Vector3(0, 6.49f, (reach + 0.5f) / 2);

            view.StopSign = new GameObject("Stop sign");
            view.StopSign.transform.SetParent(root, false);
            view.StopSign.transform.position = MeshKit.ToWorld(arm.Post.X, arm.Post.Y);
            view.StopSign.transform.rotation = MeshKit.Heading(arm.H);
            // The signs stand upright on their posts and face the traffic they are for, as the signal heads do (local -x).
            // They are drawn flat, so they are stood up with their faces that way; the plate sits a little in front of its rim.
            Quaternion upright = Quaternion.LookRotation(Vector3.up, Vector3.left);
            MeshKit.Spawn("Pole", MeshKit.Box(0.22f, 2.6f, 0.22f), dark, view.StopSign.transform).transform.localPosition = new Vector3(0, 1.3f, 0);
            Transform rim = MeshKit.Spawn("Rim", Octagon(1.15f), signWhite, view.StopSign.transform, false).transform;
            rim.localPosition = new Vector3(-0.13f, 3.2f, 0);
            rim.localRotation = upright;
            Transform plate = MeshKit.Spawn("Plate", Octagon(1.0f), signRed, view.StopSign.transform, false).transform;
            plate.localPosition = new Vector3(-0.17f, 3.2f, 0);
            plate.localRotation = upright;
            view.StopSign.SetActive(false);

            // A yield sign: a white triangle with a red border, point down.
            view.YieldSign = new GameObject("Yield sign");
            view.YieldSign.transform.SetParent(root, false);
            view.YieldSign.transform.position = MeshKit.ToWorld(arm.Post.X, arm.Post.Y);
            view.YieldSign.transform.rotation = MeshKit.Heading(arm.H);
            MeshKit.Spawn("Pole", MeshKit.Box(0.22f, 2.6f, 0.22f), dark, view.YieldSign.transform).transform.localPosition = new Vector3(0, 1.3f, 0);
            rim = MeshKit.Spawn("Rim", PointDown(1.45f), signRed, view.YieldSign.transform, false).transform;
            rim.localPosition = new Vector3(-0.13f, 3.3f, 0);
            rim.localRotation = upright;
            plate = MeshKit.Spawn("Plate", PointDown(0.8f), signWhite, view.YieldSign.transform, false).transform;
            plate.localPosition = new Vector3(-0.17f, 3.3f, 0);
            plate.localRotation = upright;
            view.YieldSign.SetActive(false);
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

        /// <summary>A triangle with one corner pointing down once it is stood upright, for a yield sign.</summary>
        private static Mesh PointDown(float radius)
        {
            var ring = new List<Vec>();
            for (int i = 0; i < 3; i++)
            {
                double a = -System.Math.PI / 2 + 2 * System.Math.PI / 3 * i;
                ring.Add(new Vec(System.Math.Cos(a) * radius, System.Math.Sin(a) * radius));
            }
            var b = new FlatBuilder();
            b.Fan(new Vec(0, 0), ring, 0);
            return b.Build();
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
            foreach (Patch patch in patches.Values) foreach (Mesh mesh in new[] { patch.Leaves, patch.Rocks, patch.Shade, patch.Flowers }) if (mesh != null) kept.Add(mesh);
            // So are the shapes every view shares.
            foreach (Mesh mesh in new[] { MeshKit.GlowBall, MeshKit.Ball, MeshKit.Cube }) kept.Add(mesh);
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
            return st == SignalLight.Green ? Green : st == SignalLight.Yellow || st == SignalLight.Yield ? Yellow : Red;
        }

        /// <summary>The light a lane shows: the best of the turns it allows.</summary>
        private static SignalLight LaneLight(Junction jn, Lane lane)
        {
            SignalLight best = SignalLight.Red;
            for (int t = 0; t < Lane.Turns; t++)
            {
                if (lane.Connectors[t] < 0) continue;
                SignalLight st = jn.State(lane.Arm, (Turn)t);
                if (st == SignalLight.Stop || st == SignalLight.Yield) return st;
                if (st == SignalLight.Green) best = SignalLight.Green;
                else if (st == SignalLight.Yellow && best == SignalLight.Red) best = SignalLight.Yellow;
            }
            return best;
        }

        /// <summary>
        /// How dark it is, from 0 in daylight to 1 at night. After dark the street lamps are lit
        /// and throw their light on the road, the lights glow more strongly, and the signs,
        /// which give no light of their own, are dimmer.
        /// </summary>
        public float Night
        {
            get { return night; }
            set
            {
                night = Mathf.Clamp01(value);
                float signs = Mathf.Lerp(1, 0.5f, night);
                signWhite.color = new Color(signs, signs, signs, 1);
                signRed.color = new Color(SignRed.r * signs, SignRed.g * signs, SignRed.b * signs, 1);
                if (lampPools == null) return;
                lampLens.color = Color.Lerp(LensByDay, LensByNight, night);
                Color light = lampLight.color;
                light.a = night;
                lampLight.color = light;
                lampPools.SetActive(night > 0.01f);
            }
        }

        /// <summary>Show the current lights.</summary>
        public void Update(List<Junction> junctions, double time)
        {
            bool flash = time % 1 < 0.5;
            // A light has the sun to compete with by day, so its glow is kept gentler then.
            float glow = Mathf.Lerp(0.5f, 0.95f, night);
            foreach (LineView line in lines)
            {
                SignalLight light = LaneLight(junctions[line.Lane.Node], line.Lane);
                Color c = LampColor(light);
                // A stop sign shows at the stop line as flashing red, and a yield sign as flashing yellow.
                if ((light == SignalLight.Stop || light == SignalLight.Yield) && !flash) c *= 0.45f;
                line.Material.color = c;
                c.a = glow * 0.8f;
                line.Glow.color = c;
            }
            foreach (SignalView s in signals)
            {
                Junction jn = junctions[s.Arm.Node];
                // A light, or a sign for this road, or (on the through street of an intersection run by signs) nothing.
                // A light that has gone out still hangs there, dark; drivers treat it as a stop sign, and the bars at the stop lines say so.
                bool lit = jn.Mode == JunctionMode.Signal || jn.Mode == JunctionMode.ProtectedLeft || jn.Failed;
                s.Heads.SetActive(lit);
                s.StopSign.SetActive((jn.Mode == JunctionMode.AllWay && !jn.Failed) || jn.SignAt(s.Arm.Arm) == Sign.Stop);
                s.YieldSign.SetActive(jn.SignAt(s.Arm.Arm) == Sign.Yield);
                if (!lit) continue;
                foreach (LaneHead head in s.Lanes)
                {
                    SignalLight st = LaneLight(jn, head.Lane);
                    Color lamp = LampColor(st);
                    lamp.a = jn.Failed ? 0 : glow;
                    head.HaloGlow.color = lamp;
                    if (jn.Failed)
                    {
                        if (head.Arrow != null) head.Arrow.color = DimRed;
                        else
                        {
                            head.Lamps[0].color = DimRed;
                            head.Lamps[1].color = DimYellow;
                            head.Lamps[2].color = DimGreen;
                        }
                        continue;
                    }
                    if (head.Arrow != null)
                    {
                        head.Arrow.color = LampColor(st);
                        head.Halo.localPosition = new Vector3(-0.55f, 5.65f, head.Across);
                        continue;
                    }
                    // Red is the top lamp, yellow the middle, green the bottom.
                    int which = st == SignalLight.Green ? 2 : st == SignalLight.Yellow ? 1 : 0;
                    head.Halo.localPosition = new Vector3(-0.55f, 5.91f - which * 0.86f, head.Across);
                    head.Lamps[0].color = st == SignalLight.Red ? Red : DimRed;
                    head.Lamps[1].color = st == SignalLight.Yellow ? Yellow : DimYellow;
                    head.Lamps[2].color = st == SignalLight.Green ? Green : DimGreen;
                }
            }
        }
    }
}
