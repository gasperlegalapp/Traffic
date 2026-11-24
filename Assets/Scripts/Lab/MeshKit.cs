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

        private static Material shade;

        /// <summary>
        /// The material for soft shading laid over the ground (see ShadeBuilder). It only ever
        /// darkens what is beneath it, by the amount each corner of the mesh says, so it looks
        /// right in sun and in shadow alike. It uses the same built-in shader as the guide lines.
        /// </summary>
        public static Material Shade
        {
            get
            {
                if (shade != null) return shade;
                Shader shader = Find("Hidden/Internal-Colored", "Sprites/Default");
                shade = new Material(shader);
                shade.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                shade.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                shade.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
                shade.SetInt("_ZWrite", 0);
                shade.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.LessEqual);
                // After everything solid, before anything else see-through.
                shade.renderQueue = 2900;
                return shade;
            }
        }

        /// <summary>A material that reacts to light and shadow and is covered with a texture.</summary>
        public static Material Textured(Texture texture, Color tint, float smoothness = 0.08f)
        {
            Material m = Lit(tint, smoothness);
            if (texture != null) m.mainTexture = texture;
            return m;
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

    /// <summary>
    /// Collects textured surfaces (asphalt, sidewalks, shoulders) and turns them into one mesh.
    /// Road and ground surfaces take their texture from where they are on the map, so it runs
    /// unbroken from one piece of road into the next; a sidewalk takes it from along its own length.
    /// </summary>
    public sealed class SurfaceBuilder
    {
        private readonly List<Vector3> verts = new List<Vector3>();
        private readonly List<Vector3> normals = new List<Vector3>();
        private readonly List<Vector2> uvs = new List<Vector2>();
        private readonly List<int> tris = new List<int>();
        /// <summary>How many meters of ground one copy of the texture covers.</summary>
        private readonly float tile;

        public SurfaceBuilder(float tile)
        {
            this.tile = tile;
        }

        public bool Empty => verts.Count == 0;

        private int Flat(Vec p, float y)
        {
            verts.Add(MeshKit.ToWorld(p.X, p.Y, y));
            normals.Add(Vector3.up);
            uvs.Add(new Vector2((float)p.X / tile, (float)p.Y / tile));
            return verts.Count - 1;
        }

        private int Mapped(Vec p, float y, float u, float v, Vector3 normal)
        {
            verts.Add(MeshKit.ToWorld(p.X, p.Y, y));
            normals.Add(normal);
            uvs.Add(new Vector2(u, v));
            return verts.Count - 1;
        }

        /// <summary>Both ways round, so it shows whichever way the corners were given.</summary>
        private void Tri(int a, int b, int c)
        {
            tris.Add(a); tris.Add(b); tris.Add(c);
            tris.Add(a); tris.Add(c); tris.Add(b);
        }

        /// <summary>A strip of ground between two lines. Heights, if given, lift each pair of points (a bridge).</summary>
        public void Strip(IList<Vec> left, IList<Vec> right, float y, IList<double> heights = null)
        {
            int l0 = Flat(left[0], y + (heights != null ? (float)heights[0] : 0));
            int r0 = Flat(right[0], y + (heights != null ? (float)heights[0] : 0));
            for (int i = 1; i < left.Count; i++)
            {
                float h = y + (heights != null ? (float)heights[i] : 0);
                int l1 = Flat(left[i], h);
                int r1 = Flat(right[i], h);
                Tri(l0, r0, l1);
                Tri(r0, r1, l1);
                l0 = l1;
                r0 = r1;
            }
        }

        public void Fan(Vec center, IList<Vec> ring, float y)
        {
            int c = Flat(center, y);
            int first = Flat(ring[0], y);
            int prev = first;
            for (int i = 1; i < ring.Count; i++)
            {
                int cur = Flat(ring[i], y);
                Tri(c, prev, cur);
                prev = cur;
            }
            Tri(c, prev, first);
        }

        /// <summary>
        /// A band along a line, such as a sidewalk: the texture runs across it from its inner
        /// edge to its outer one, and repeats every so many meters along it.
        /// </summary>
        public void Ribbon(IList<Vec> inner, IList<Vec> outer, float y, float every)
        {
            float along = 0;
            int a0 = Mapped(inner[0], y, 0, 0, Vector3.up), b0 = Mapped(outer[0], y, 1, 0, Vector3.up);
            for (int i = 1; i < inner.Count; i++)
            {
                along += (float)Geo.Hypot(inner[i].X - inner[i - 1].X, inner[i].Y - inner[i - 1].Y) / every;
                int a1 = Mapped(inner[i], y, 0, along, Vector3.up), b1 = Mapped(outer[i], y, 1, along, Vector3.up);
                Tri(a0, b0, a1);
                Tri(b0, b1, a1);
                a0 = a1;
                b0 = b1;
            }
        }

        /// <summary>An upright face along a line, such as the side of a curb. It faces the given side (1 for the right of the line).</summary>
        public void Wall(IList<Vec> line, float bottom, float top, float every, int side)
        {
            float along = 0;
            int low0 = -1, high0 = -1;
            for (int i = 0; i < line.Count; i++)
            {
                Vec before = line[System.Math.Max(i - 1, 0)], after = line[System.Math.Min(i + 1, line.Count - 1)];
                double dx = after.X - before.X, dy = after.Y - before.Y, len = System.Math.Max(Geo.Hypot(dx, dy), 1e-6);
                var normal = new Vector3((float)(dy / len) * side, 0, (float)(-dx / len) * side);
                if (i > 0) along += (float)Geo.Hypot(line[i].X - line[i - 1].X, line[i].Y - line[i - 1].Y) / every;
                int low1 = Mapped(line[i], bottom, 0.02f, along, normal), high1 = Mapped(line[i], top, 0.1f, along, normal);
                if (i > 0)
                {
                    Tri(low0, high0, low1);
                    Tri(high0, high1, low1);
                }
                low0 = low1;
                high0 = high1;
            }
        }

        public Mesh Build()
        {
            var mesh = new Mesh();
            if (verts.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }
    }

    /// <summary>
    /// Collects soft, see-through shading to lay over the ground: the dark of wheel tracks in a
    /// lane, the grime along a curb, the shade under a tree. Each corner carries how dark it is,
    /// and the darkness fades smoothly between corners, so nothing has a hard edge.
    /// </summary>
    public sealed class ShadeBuilder
    {
        private readonly List<Vector3> verts = new List<Vector3>();
        private readonly List<Color> colors = new List<Color>();
        private readonly List<int> tris = new List<int>();

        public bool Empty => verts.Count == 0;

        private int Point(double x, double y, float height, float dark)
        {
            verts.Add(MeshKit.ToWorld(x, y, height));
            colors.Add(new Color(0, 0, 0, dark));
            return verts.Count - 1;
        }

        /// <summary>
        /// Shading along a line. Across it, at each given distance to the right of the line
        /// (negative for the left), it is as dark as the matching amount; along it, it fades in
        /// and out over a few meters at each end when asked to.
        /// </summary>
        public void Along(IList<Vec> line, double[] across, float[] dark, float height, bool fadeEnds, int step = 1)
        {
            if (line.Count < 2) return;
            var along = new double[line.Count];
            for (int i = 1; i < line.Count; i++) along[i] = along[i - 1] + Geo.Hypot(line[i].X - line[i - 1].X, line[i].Y - line[i - 1].Y);
            double total = along[line.Count - 1];
            int previous = -1;
            for (int i = 0; i < line.Count; i = i == line.Count - 1 ? line.Count : System.Math.Min(i + step, line.Count - 1))
            {
                Vec before = line[System.Math.Max(i - 1, 0)], after = line[System.Math.Min(i + 1, line.Count - 1)];
                double dx = after.X - before.X, dy = after.Y - before.Y, len = System.Math.Max(Geo.Hypot(dx, dy), 1e-6);
                double rx = dy / len, ry = -dx / len;
                float fade = fadeEnds ? Mathf.Clamp01((float)(System.Math.Min(along[i], total - along[i]) / 6)) : 1;
                int first = verts.Count;
                for (int k = 0; k < across.Length; k++) Point(line[i].X + rx * across[k], line[i].Y + ry * across[k], height, dark[k] * fade);
                if (previous >= 0)
                {
                    for (int k = 0; k + 1 < across.Length; k++)
                    {
                        tris.Add(previous + k); tris.Add(first + k); tris.Add(previous + k + 1);
                        tris.Add(previous + k + 1); tris.Add(first + k); tris.Add(first + k + 1);
                    }
                }
                previous = first;
            }
        }

        /// <summary>A round patch of shade, darkest in the middle and gone at its rim.</summary>
        public void Disc(double x, double y, double radius, float height, float dark)
        {
            int center = Point(x, y, height, dark);
            const int sides = 14;
            int first = verts.Count;
            for (int i = 0; i < sides; i++)
            {
                double a = System.Math.PI * 2 * i / sides;
                Point(x + System.Math.Cos(a) * radius * 0.55, y + System.Math.Sin(a) * radius * 0.55, height, dark * 0.75f);
            }
            for (int i = 0; i < sides; i++)
            {
                double a = System.Math.PI * 2 * i / sides;
                Point(x + System.Math.Cos(a) * radius, y + System.Math.Sin(a) * radius, height, 0);
            }
            for (int i = 0; i < sides; i++)
            {
                int j = (i + 1) % sides;
                tris.Add(center); tris.Add(first + j); tris.Add(first + i);
                tris.Add(first + i); tris.Add(first + j); tris.Add(first + sides + i);
                tris.Add(first + j); tris.Add(first + sides + j); tris.Add(first + sides + i);
            }
        }

        public Mesh Build()
        {
            var mesh = new Mesh();
            if (verts.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(verts);
            mesh.SetColors(colors);
            mesh.SetTriangles(tris, 0);
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

        /// <summary>A strip on a bridge: each pair of points has its own height.</summary>
        public void Strip(IList<Vec> left, IList<Vec> right, IList<double> heights, float y)
        {
            int n = left.Count - 1;
            int l0 = Vertex(left[0], y + (float)heights[0]);
            int r0 = Vertex(right[0], y + (float)heights[0]);
            for (int i = 1; i <= n; i++)
            {
                float h = y + (float)heights[i];
                int l1 = Vertex(left[i], h);
                int r1 = Vertex(right[i], h);
                Tri(l0, r0, l1);
                Tri(r0, r1, l1);
                l0 = l1;
                r0 = r1;
            }
        }

        /// <summary>A wall from the ground up to the edge of a bridge.</summary>
        public void Skirt(IList<Vec> edge, IList<double> heights, float y)
        {
            int n = edge.Count - 1;
            int top0 = Vertex(edge[0], y + (float)heights[0]);
            int bottom0 = Vertex(edge[0], 0);
            for (int i = 1; i <= n; i++)
            {
                int top1 = Vertex(edge[i], y + (float)heights[i]);
                int bottom1 = Vertex(edge[i], 0);
                Tri(bottom0, top0, bottom1);
                Tri(top0, top1, bottom1);
                top0 = top1;
                bottom0 = bottom1;
            }
        }

        /// <summary>A painted line on a bridge.</summary>
        public void Line(IList<Vec> pts, IList<double> heights, double width, float y)
        {
            if (pts.Count < 2) return;
            var list = new List<Vec>(pts);
            Strip(Geo.Offset(list, -width / 2), Geo.Offset(list, width / 2), heights, y);
        }

        /// <summary>A broken painted line on a bridge.</summary>
        public void Dashed(IList<Vec> pts, IList<double> heights, double width, double on, double off, float y)
        {
            if (pts.Count < 2) return;
            var along = new double[pts.Count];
            for (int i = 1; i < pts.Count; i++) along[i] = along[i - 1] + Geo.Hypot(pts[i].X - pts[i - 1].X, pts[i].Y - pts[i - 1].Y);
            double total = along[pts.Count - 1];
            int at = 0;
            for (double s = 0; s < total; s += on + off)
            {
                double end = System.Math.Min(s + on, total);
                var seg = new List<Vec>();
                var high = new List<double>();
                for (double t = s; ; t = System.Math.Min(t + 1, end))
                {
                    // The point and height this far along, between the two given points either side of it.
                    while (at < pts.Count - 2 && along[at + 1] < t) at++;
                    double span = along[at + 1] - along[at];
                    double f = span > 0 ? Geo.Clamp((t - along[at]) / span, 0, 1) : 0;
                    seg.Add(new Vec(pts[at].X + (pts[at + 1].X - pts[at].X) * f, pts[at].Y + (pts[at + 1].Y - pts[at].Y) * f));
                    high.Add(heights[at] + (heights[at + 1] - heights[at]) * f);
                    if (t >= end) break;
                }
                Line(seg, high, width, y);
            }
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
