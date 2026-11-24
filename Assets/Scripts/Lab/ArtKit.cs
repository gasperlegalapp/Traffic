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

        /// <summary>Leaf colors. The first few are summer greens, then autumn, then the dark greens of pines.</summary>
        private static readonly int[] LeafColors =
        {
            0x5f9e52, 0x4f8c4a, 0x74ad5c, 0x67a04f, 0x86b85e,
            0xd98b3e, 0xe2b04c, 0xc8643a, 0xcf9a45,
            0x3f7350, 0x356647, 0x487a52,
        };
        public const int SummerLeaves = 5, AutumnLeaves = 4, PineLeaves = 3;

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
