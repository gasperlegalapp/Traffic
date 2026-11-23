using System.Collections.Generic;
using Traffic.Sim;
using UnityEngine;

namespace Traffic.Lab
{
    /// <summary>
    /// Helpers for building meshes and materials in code.
    /// Sim coordinates (x east, y north) map to Unity as (x, height, z = y).
    /// </summary>
    public static class MeshKit
    {
        public static Vector3 ToWorld(double x, double y, float height = 0)
        {
            return new Vector3((float)x, height, (float)y);
        }

        /// <summary>Unity rotation for a sim heading (radians, counter-clockwise from east). Models face +x.</summary>
        public static Quaternion Heading(double h)
        {
            return Quaternion.Euler(0, (float)(-h * 180 / System.Math.PI), 0);
        }

        public static Color Hex(int rgb, float alpha = 1)
        {
            return new Color(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f, alpha);
        }

        /// <summary>True when the project uses a scriptable render pipeline (URP or HDRP) instead of the built-in one.</summary>
        public static bool Scriptable => UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline != null;

        private static Shader Find(params string[] names)
        {
            foreach (string name in names)
            {
                Shader s = Shader.Find(name);
                if (s != null) return s;
            }
            return null;
        }

        /// <summary>A material that reacts to light and shadow.</summary>
        public static Material Lit(Color color, float smoothness = 0.1f)
        {
            // Each pipeline has its own lit shader, and the other pipeline's draws solid pink.
            Shader shader = Scriptable
                ? Find("Universal Render Pipeline/Lit", "Universal Render Pipeline/Simple Lit", "HDRP/Lit", "Sprites/Default")
                : Find("Standard", "Legacy Shaders/Diffuse", "Sprites/Default");
            var m = new Material(shader);
            m.color = color;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", smoothness);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smoothness);
            return m;
        }

        /// <summary>A flat, glowing color that ignores lighting (lamps, paint highlights).</summary>
        public static Material Unlit(Color color)
        {
            Shader shader = Scriptable
                ? Find("Universal Render Pipeline/Unlit", "HDRP/Unlit", "Unlit/Color", "Sprites/Default")
                : Find("Unlit/Color", "Sprites/Default");
            return new Material(shader) { color = color };
        }

        public static GameObject Spawn(string name, Mesh mesh, Material material, Transform parent, bool shadows = true)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = shadows ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
            return go;
        }

        /// <summary>A box centered on the origin.</summary>
        public static Mesh Box(float sx, float sy, float sz)
        {
            float x = sx / 2, y = sy / 2, z = sz / 2;
            Vector3[] c =
            {
                new Vector3(-x, -y, -z), new Vector3(x, -y, -z), new Vector3(x, y, -z), new Vector3(-x, y, -z),
                new Vector3(-x, -y, z), new Vector3(x, -y, z), new Vector3(x, y, z), new Vector3(-x, y, z),
            };
            int[][] faces =
            {
                new[] { 0, 3, 2, 1 }, new[] { 5, 6, 7, 4 }, new[] { 4, 7, 3, 0 },
                new[] { 1, 2, 6, 5 }, new[] { 3, 7, 6, 2 }, new[] { 4, 0, 1, 5 },
            };
            var verts = new List<Vector3>();
            var tris = new List<int>();
            foreach (int[] f in faces)
            {
                int b = verts.Count;
                foreach (int i in f) verts.Add(c[i]);
                tris.AddRange(new[] { b, b + 1, b + 2, b, b + 2, b + 3 });
            }
            var mesh = new Mesh();
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>A cone standing on the ground, used for trees.</summary>
        public static Mesh Cone(float radius, float height, int sides, float baseY)
        {
            var verts = new List<Vector3>();
            var tris = new List<int>();
            for (int i = 0; i < sides; i++)
            {
                float a0 = Mathf.PI * 2 * i / sides;
                float a1 = Mathf.PI * 2 * (i + 1) / sides;
                int b = verts.Count;
                verts.Add(new Vector3(Mathf.Cos(a0) * radius, baseY, Mathf.Sin(a0) * radius));
                verts.Add(new Vector3(0, baseY + height, 0));
                verts.Add(new Vector3(Mathf.Cos(a1) * radius, baseY, Mathf.Sin(a1) * radius));
                tris.AddRange(new[] { b, b + 1, b + 2 });
            }
            var mesh = new Mesh();
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }

    /// <summary>Collects flat, upward-facing triangles (pavement and paint) and turns them into one mesh.</summary>
    public sealed class FlatBuilder
    {
        private readonly List<Vector3> verts = new List<Vector3>();
        private readonly List<int> tris = new List<int>();

        private int Vertex(Vec p, float y)
        {
            verts.Add(MeshKit.ToWorld(p.X, p.Y, y));
            return verts.Count - 1;
        }

        /// <summary>Adds a triangle facing both ways, so it shows whichever way round the corners were given.</summary>
        private void Tri(int a, int b, int c)
        {
            tris.Add(a); tris.Add(b); tris.Add(c);
            tris.Add(a); tris.Add(c); tris.Add(b);
        }

        /// <summary>A strip on a bridge: it rises to peak in the middle of its length.</summary>
        public void Strip(IList<Vec> left, IList<Vec> right, float y, double peak)
        {
            int n = left.Count - 1;
            int l0 = Vertex(left[0], y);
            int r0 = Vertex(right[0], y);
            for (int i = 1; i <= n; i++)
            {
                float h = y + (float)(peak * Geo.BridgeProfile((double)i / n));
                int l1 = Vertex(left[i], h);
                int r1 = Vertex(right[i], h);
                Tri(l0, r0, l1);
                Tri(r0, r1, l1);
                l0 = l1;
                r0 = r1;
            }
        }

        /// <summary>A wall from the ground up to the edge of a bridge.</summary>
        public void Skirt(IList<Vec> edge, float y, double peak)
        {
            int n = edge.Count - 1;
            int top0 = Vertex(edge[0], y);
            int bottom0 = Vertex(edge[0], 0);
            for (int i = 1; i <= n; i++)
            {
                int top1 = Vertex(edge[i], y + (float)(peak * Geo.BridgeProfile((double)i / n)));
                int bottom1 = Vertex(edge[i], 0);
                Tri(bottom0, top0, bottom1);
                Tri(top0, top1, bottom1);
                top0 = top1;
                bottom0 = bottom1;
            }
        }

        public void Line(IList<Vec> pts, double width, float y, double peak)
        {
            if (pts.Count < 2) return;
            var list = new List<Vec>(pts);
            Strip(Geo.Offset(list, -width / 2), Geo.Offset(list, width / 2), y, peak);
        }

        public void Strip(IList<Vec> left, IList<Vec> right, float y)
        {
            int l0 = Vertex(left[0], y);
            int r0 = Vertex(right[0], y);
            for (int i = 1; i < left.Count; i++)
            {
                int l1 = Vertex(left[i], y);
                int r1 = Vertex(right[i], y);
                Tri(l0, r0, l1);
                Tri(r0, r1, l1);
                l0 = l1;
                r0 = r1;
            }
        }

        public void Fan(Vec center, IList<Vec> ring, float y)
        {
            int c = Vertex(center, y);
            int first = Vertex(ring[0], y);
            int prev = first;
            for (int i = 1; i < ring.Count; i++)
            {
                int cur = Vertex(ring[i], y);
                Tri(c, prev, cur);
                prev = cur;
            }
            Tri(c, prev, first);
        }

        public void Line(IList<Vec> pts, double width, float y)
        {
            if (pts.Count < 2) return;
            var list = new List<Vec>(pts);
            Strip(Geo.Offset(list, -width / 2), Geo.Offset(list, width / 2), y);
        }

        public void Dashed(IList<Vec> pts, double width, double on, double off, float y)
        {
            if (pts.Count < 2) return;
            var path = new LanePath(pts);
            for (double s = 0; s < path.Length; s += on + off)
            {
                double end = System.Math.Min(s + on, path.Length);
                var seg = new List<Vec>();
                for (double t = s; t < end; t += 1) seg.Add(path.At(t).Point);
                seg.Add(path.At(end).Point);
                Line(seg, width, y);
            }
        }

        public Mesh Build()
        {
            var mesh = new Mesh();
            if (verts.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            var normals = new Vector3[verts.Count];
            for (int i = 0; i < normals.Length; i++) normals[i] = Vector3.up;
            mesh.normals = normals;
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
