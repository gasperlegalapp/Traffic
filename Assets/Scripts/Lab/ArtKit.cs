using System.Collections.Generic;
using Traffic.Art;
using UnityEngine;

namespace Traffic.Lab
{
    /// <summary>
    /// The modeled art: vehicles, trees, bushes and rocks. They come from one file
    /// (Resources/Art/models.bytes, written by Tools/BakeArt/bake.py) and are turned into
    /// meshes here, so nothing depends on how Unity imports a model. Every model shares one
    /// small texture of flat colors. If the file is missing, Ready is false and the lab
    /// falls back to its plain shapes.
    /// </summary>
    public static class ArtKit
    {
        // The shared texture is a grid of flat colors: the file's fixed colors first, then a
        // run of grays that a vehicle's paint tints, then the colors leaves can take.
        private const int Side = 64;
        private const int PaintStart = 32 * Side, PaintSteps = 32, LeafStart = 48 * Side;

        /// <summary>Leaf colors: summer greens, close enough together that the trees read as one color.</summary>
        private static readonly int[] LeafColors = { 0x467d3a, 0x427737, 0x4b843e };
        public const int SummerLeaves = 3;

        private static ArtFile file;
        private static bool tried;
        private static Texture2D texture;
        private static Material shared;
        private static readonly Dictionary<string, Mesh> meshes = new Dictionary<string, Mesh>();

        /// <summary>True when the art file was found and read.</summary>
        public static bool Ready
        {
            get
            {
                if (!tried)
                {
                    tried = true;
                    try
                    {
                        TextAsset asset = Resources.Load<TextAsset>("Art/models");
                        if (asset == null) Debug.LogWarning("Traffic: Assets/Resources/Art/models.bytes was not found, so vehicles and trees are drawn as plain shapes.");
                        else file = ArtFile.Read(asset.bytes);
                    }
                    catch (System.Exception e)
                    {
                        Debug.LogWarning("Traffic: the art file could not be read (" + e.Message + "), so vehicles and trees are drawn as plain shapes.");
                        file = null;
                    }
                }
                return file != null;
            }
        }

        private static readonly Dictionary<string, Texture2D> photos = new Dictionary<string, Texture2D>();

        /// <summary>
        /// One of the ground and road textures (Resources/Art/NAME.bytes, written by
        /// Tools/BakeArt/textures.py). They are stored as picture files that Unity leaves alone
        /// and are decoded here, so nothing depends on Unity's import settings. Null if missing.
        /// </summary>
        public static Texture2D Photo(string name)
        {
            Texture2D texture;
            if (photos.TryGetValue(name, out texture) && texture != null) return texture;
            TextAsset asset = Resources.Load<TextAsset>("Art/" + name);
            if (asset == null)
            {
                Debug.LogWarning("Traffic: Assets/Resources/Art/" + name + ".bytes was not found, so that surface is drawn in a plain color.");
                return null;
            }
            texture = new Texture2D(2, 2, TextureFormat.RGB24, true);
            if (!texture.LoadImage(asset.bytes))
            {
                Debug.LogWarning("Traffic: the texture " + name + " could not be read, so that surface is drawn in a plain color.");
                return null;
            }
            texture.wrapMode = TextureWrapMode.Repeat;
            texture.filterMode = FilterMode.Trilinear;
            texture.anisoLevel = 8;
            photos[name] = texture;
            return texture;
        }

        // ---------------------------------------------------------------- tree tops

        private static Material canopy;
        private static Vector3[][] ballPoints;
        private static int[][] ballTris;

        /// <summary>How many leaf colors there are: the columns of the tree-top texture.</summary>
        public static int LeafCount => LeafColors.Length;

        /// <summary>
        /// The material for tree tops: the foliage picture, repeated side by side once in each
        /// leaf color, so that trees of every color share one material.
        /// </summary>
        public static Material Canopy
        {
            get
            {
                if (canopy != null && canopy.mainTexture != null) return canopy;
                Texture2D leaves = Photo("foliage");
                const int cell = 256;
                var atlas = new Texture2D(cell * LeafColors.Length, cell, TextureFormat.RGB24, true);
                var pixels = new Color32[cell * LeafColors.Length * cell];
                Color32[] source = leaves != null ? leaves.GetPixels32() : null;
                int size = leaves != null ? leaves.width : 0;
                for (int k = 0; k < LeafColors.Length; k++)
                {
                    Color tint = MeshKit.Hex(LeafColors[k]);
                    for (int y = 0; y < cell; y++)
                    {
                        for (int x = 0; x < cell; x++)
                        {
                            // Brighter than the plain color where the leaves catch the light, darker in the gaps between clumps.
                            float gray = source != null ? source[(y * size / cell) * size + x * size / cell].g / 255f : 0.55f;
                            float lit = 0.35f + gray * 1.25f;
                            pixels[y * cell * LeafColors.Length + k * cell + x] = new Color(tint.r * lit, tint.g * lit, tint.b * lit, 1);
                        }
                    }
                }
                atlas.SetPixels32(pixels);
                atlas.wrapMode = TextureWrapMode.Clamp;
                atlas.filterMode = FilterMode.Trilinear;
                atlas.Apply(true);
                canopy = MeshKit.Textured(atlas, Color.white, 0.02f);
                return canopy;
            }
        }

        /// <summary>A ball of evenly spread points and the triangles between them: coarse (0) or finer (1).</summary>
        public static void Ball(int detail, out Vector3[] points, out int[] triangles)
        {
            if (ballPoints == null)
            {
                ballPoints = new Vector3[2][];
                ballTris = new int[2][];
                for (int level = 0; level < 2; level++) MakeBall(level + 1, out ballPoints[level], out ballTris[level]);
            }
            points = ballPoints[detail];
            triangles = ballTris[detail];
        }

        /// <summary>A twenty-sided shape with each face split into smaller ones and pushed out to a ball.</summary>
        private static void MakeBall(int splits, out Vector3[] points, out int[] triangles)
        {
            float t = (1 + Mathf.Sqrt(5)) / 2;
            var verts = new List<Vector3>
            {
                new Vector3(-1, t, 0), new Vector3(1, t, 0), new Vector3(-1, -t, 0), new Vector3(1, -t, 0),
                new Vector3(0, -1, t), new Vector3(0, 1, t), new Vector3(0, -1, -t), new Vector3(0, 1, -t),
                new Vector3(t, 0, -1), new Vector3(t, 0, 1), new Vector3(-t, 0, -1), new Vector3(-t, 0, 1),
            };
            for (int i = 0; i < verts.Count; i++) verts[i] = verts[i].normalized;
            var faces = new List<int>
            {
                0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11, 1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
                3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9, 4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1,
            };
            for (int s = 0; s < splits; s++)
            {
                var middle = new Dictionary<long, int>();
                int Mid(int a, int b)
                {
                    long key = a < b ? ((long)a << 32) + b : ((long)b << 32) + a;
                    int index;
                    if (middle.TryGetValue(key, out index)) return index;
                    verts.Add(((verts[a] + verts[b]) / 2).normalized);
                    middle[key] = verts.Count - 1;
                    return verts.Count - 1;
                }
                var next = new List<int>();
                for (int i = 0; i < faces.Count; i += 3)
                {
                    int a = faces[i], b = faces[i + 1], c = faces[i + 2];
                    int ab = Mid(a, b), bc = Mid(b, c), ca = Mid(c, a);
                    next.AddRange(new[] { a, ab, ca, b, bc, ab, c, ca, bc, ab, bc, ca });
                }
                faces = next;
            }
            points = verts.ToArray();
            triangles = faces.ToArray();
        }

        /// <summary>Every model whose name starts a certain way, such as "car/" or "tree/".</summary>
        public static List<ArtModel> Named(string prefix)
        {
            var list = new List<ArtModel>();
            if (!Ready) return list;
            foreach (ArtModel m in file.Models) if (m.Name.StartsWith(prefix)) list.Add(m);
            return list;
        }

        private static Texture2D Texture
        {
            get
            {
                if (texture != null) return texture;
                var pixels = new Color32[Side * Side];
                for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color32(255, 0, 255, 255);
                for (int i = 0; i < file.PaletteCount; i++) pixels[i] = new Color32(file.Palette[i * 3], file.Palette[i * 3 + 1], file.Palette[i * 3 + 2], 255);
                for (int i = 0; i < PaintSteps; i++)
                {
                    byte gray = (byte)Mathf.RoundToInt(Mathf.Lerp(0.5f, 1, i / (PaintSteps - 1f)) * 255);
                    pixels[PaintStart + i] = new Color32(gray, gray, gray, 255);
                }
                for (int i = 0; i < LeafColors.Length; i++) pixels[LeafStart + i] = MeshKit.Hex(LeafColors[i]);
                texture = new Texture2D(Side, Side, TextureFormat.RGBA32, false);
                texture.SetPixels32(pixels);
                // Each color is one pixel, so neighbors must never blend.
                texture.filterMode = FilterMode.Point;
                texture.wrapMode = TextureWrapMode.Clamp;
                texture.Apply();
                return texture;
            }
        }

        /// <summary>The material for everything that keeps its own colors.</summary>
        public static Material Shared
        {
            get
            {
                if (shared == null) shared = NewPaint(Color.white);
                return shared;
            }
        }

        /// <summary>A material for one vehicle's paint. Set its color to repaint the vehicle.</summary>
        public static Material NewPaint(Color color)
        {
            Material m = MeshKit.Lit(color, 0.25f);
            m.mainTexture = Texture;
            return m;
        }

        /// <summary>Where in the shared texture a corner's color is.</summary>
        public static Vector2 Uv(ushort slot, byte shade, int leaf)
        {
            int cell = slot == ArtFile.Paint ? PaintStart + Mathf.RoundToInt(shade / 255f * (PaintSteps - 1))
                : slot == ArtFile.Leaf ? LeafStart + Mathf.Clamp(leaf, 0, LeafColors.Length - 1)
                : slot;
            return new Vector2((cell % Side + 0.5f) / Side, (cell / Side + 0.5f) / Side);
        }

        /// <summary>A mesh for a vehicle. Its first part is the paint, its second everything else.</summary>
        public static Mesh MeshOf(ArtModel model)
        {
            Mesh mesh;
            if (meshes.TryGetValue(model.Name, out mesh) && mesh != null) return mesh;
            var verts = new Vector3[model.Count];
            var normals = new Vector3[model.Count];
            var uvs = new Vector2[model.Count];
            for (int i = 0; i < model.Count; i++)
            {
                verts[i] = new Vector3(model.Positions[i * 3], model.Positions[i * 3 + 1], model.Positions[i * 3 + 2]);
                normals[i] = new Vector3(model.Normals[i * 3], model.Normals[i * 3 + 1], model.Normals[i * 3 + 2]);
                uvs[i] = Uv(model.Slots[i], model.Shades[i], 0);
            }
            mesh = new Mesh { name = model.Name };
            mesh.vertices = verts;
            mesh.normals = normals;
            mesh.uv = uvs;
            mesh.subMeshCount = 2;
            mesh.SetTriangles(model.Paint, 0);
            mesh.SetTriangles(model.Rest, 1);
            mesh.RecalculateBounds();
            meshes[model.Name] = mesh;
            return mesh;
        }
    }

    /// <summary>
    /// Builds tree tops and bushes: each a few lumpy balls of leaves run together, no two the
    /// same, covered with the foliage picture in the tree's own leaf color.
    /// </summary>
    public sealed class CanopyBuilder
    {
        private readonly List<Vector3> verts = new List<Vector3>();
        private readonly List<Vector3> normals = new List<Vector3>();
        private readonly List<Vector2> uvs = new List<Vector2>();
        private readonly List<int> tris = new List<int>();

        public bool Empty => verts.Count == 0;

        private static float Rand(int a, int b, int c)
        {
            unchecked
            {
                uint h = (uint)a * 374761393u + (uint)b * 668265263u + (uint)c * 2246822519u;
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0xffffff) / 16777216f;
            }
        }

        /// <summary>Smooth random lumpiness over the surface of a ball, from 0 to 1.</summary>
        private static float Lumps(Vector3 p, int seed)
        {
            int x0 = Mathf.FloorToInt(p.x), y0 = Mathf.FloorToInt(p.y), z0 = Mathf.FloorToInt(p.z);
            float fx = p.x - x0, fy = p.y - y0, fz = p.z - z0;
            fx = fx * fx * (3 - 2 * fx);
            fy = fy * fy * (3 - 2 * fy);
            fz = fz * fz * (3 - 2 * fz);
            float Corner(int dx, int dy, int dz)
            {
                return Rand(x0 + dx + seed * 31, y0 + dy + seed * 17, z0 + dz + seed * 7);
            }
            float low = Mathf.Lerp(Mathf.Lerp(Corner(0, 0, 0), Corner(1, 0, 0), fx), Mathf.Lerp(Corner(0, 1, 0), Corner(1, 1, 0), fx), fy);
            float high = Mathf.Lerp(Mathf.Lerp(Corner(0, 0, 1), Corner(1, 0, 1), fx), Mathf.Lerp(Corner(0, 1, 1), Corner(1, 1, 1), fx), fy);
            return Mathf.Lerp(low, high, fz);
        }

        /// <summary>One lumpy ball of leaves.</summary>
        private void Lobe(Vector3 center, float radius, float squash, int detail, int seed, Vector3 tree, float span, float turn, int leaf)
        {
            Vector3[] points;
            int[] triangles;
            ArtKit.Ball(detail, out points, out triangles);
            int first = verts.Count;
            float cos = Mathf.Cos(turn), sin = Mathf.Sin(turn);
            foreach (Vector3 p in points)
            {
                float r = radius * (0.78f + 0.44f * Lumps(p * 2.1f + new Vector3(seed, seed * 0.37f, seed * 0.11f), seed));
                var at = new Vector3(center.x + p.x * r, center.y + p.y * r * squash, center.z + p.z * r);
                verts.Add(at);
                normals.Add(new Vector3(p.x, p.y / squash, p.z).normalized);
                // The picture is laid over the tree from above, turned differently for each tree.
                float dx = (at.x - tree.x) / span, dz = (at.z - tree.z) / span;
                float u = Mathf.Clamp(0.5f + dx * cos - dz * sin, 0.03f, 0.97f), v = Mathf.Clamp(0.5f + dx * sin + dz * cos, 0.03f, 0.97f);
                uvs.Add(new Vector2((leaf + u) / ArtKit.LeafCount, v));
            }
            foreach (int i in triangles) tris.Add(first + i);
        }

        /// <summary>A tree: a crown of leaves of a given spread, its top a given height above the ground.</summary>
        public void Tree(Vector3 at, float spread, float height, int leaf, int seed)
        {
            float span = spread * 2.7f, turn = Rand(seed, 1, 0) * 6.2832f;
            var middle = new Vector3(at.x, at.y + height - spread * 0.62f, at.z);
            Lobe(middle, spread * 0.82f, 0.8f, 1, seed, at, span, turn, leaf);
            int lobes = 3 + (int)(Rand(seed, 2, 0) * 3);
            for (int k = 0; k < lobes; k++)
            {
                float a = (k + Rand(seed, 3, k)) * 6.2832f / lobes, out_ = spread * (0.42f + 0.22f * Rand(seed, 4, k));
                var side = new Vector3(at.x + Mathf.Cos(a) * out_, middle.y - spread * (0.1f + 0.3f * Rand(seed, 5, k)), at.z + Mathf.Sin(a) * out_);
                Lobe(side, spread * (0.5f + 0.2f * Rand(seed, 6, k)), 0.78f, 0, seed + k * 13 + 5, at, span, turn, leaf);
            }
        }

        /// <summary>A bush: one or two low balls of leaves sitting on the ground.</summary>
        public void Bush(Vector3 at, float spread, int leaf, int seed)
        {
            float span = spread * 2.8f, turn = Rand(seed, 1, 0) * 6.2832f;
            Lobe(new Vector3(at.x, at.y + spread * 0.45f, at.z), spread, 0.7f, 0, seed, at, span, turn, leaf);
            if (Rand(seed, 2, 0) > 0.5f) Lobe(new Vector3(at.x + spread * 0.7f, at.y + spread * 0.35f, at.z + spread * 0.3f), spread * 0.7f, 0.7f, 0, seed + 9, at, span, turn, leaf);
        }

        public Mesh Build()
        {
            var mesh = new Mesh { name = "Tree tops" };
            if (verts.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }
    }

    /// <summary>Gathers many copies of models (a forest, say) into one mesh, so they draw in one go.</summary>
    public sealed class SceneryBuilder
    {
        private readonly List<Vector3> verts = new List<Vector3>();
        private readonly List<Vector3> normals = new List<Vector3>();
        private readonly List<Vector2> uvs = new List<Vector2>();
        private readonly List<int> tris = new List<int>();

        public int Count => verts.Count;

        /// <summary>Stand a model on the ground, turned and sized, with its leaves a chosen color.</summary>
        public void Add(ArtModel model, Vector3 at, float turn, float scale, int leaf)
        {
            int first = verts.Count;
            float cos = Mathf.Cos(turn), sin = Mathf.Sin(turn);
            for (int i = 0; i < model.Count; i++)
            {
                float x = model.Positions[i * 3], y = model.Positions[i * 3 + 1], z = model.Positions[i * 3 + 2];
                float nx = model.Normals[i * 3], nz = model.Normals[i * 3 + 2];
                verts.Add(new Vector3(at.x + (x * cos - z * sin) * scale, at.y + y * scale, at.z + (x * sin + z * cos) * scale));
                normals.Add(new Vector3(nx * cos - nz * sin, model.Normals[i * 3 + 1], nx * sin + nz * cos));
                uvs.Add(ArtKit.Uv(model.Slots[i], model.Shades[i], leaf));
            }
            foreach (int i in model.Paint) tris.Add(first + i);
            foreach (int i in model.Rest) tris.Add(first + i);
        }

        public Mesh Build()
        {
            var mesh = new Mesh { name = "Scenery" };
            if (verts.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
