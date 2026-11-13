using System.Collections.Generic;
using UnityEngine;

namespace Goblingame.TestScene
{
    /// <summary>Low-poly models for every item, built in code: the grip sits at the origin, the business end points up (+Y).</summary>
    public static class ItemMeshes
    {
        private static readonly Dictionary<string, Mesh> meshes = new Dictionary<string, Mesh>();
        private static readonly Dictionary<string, Material[]> materials = new Dictionary<string, Material[]>();

        public static Mesh Mesh(ItemDef def)
        {
            if (def == null) return null;
            if (meshes.TryGetValue(def.id, out var m) && m != null) return m;
            var b = new Builder();
            switch (def.id)
            {
                case "club": Club(b); break;
                case "dagger": Dagger(b); break;
                case "cleaver": Cleaver(b); break;
                case "mushroom": Mushroom(b); break;
                case "stew": Stew(b); break;
                case "bone": Bone(b); break;
                case "coin": Coin(b); break;
                default: Fist(b); break;
            }
            m = b.Build(def.id);
            meshes[def.id] = m;
            return m;
        }

        public static Material[] Materials(ItemDef def)
        {
            if (materials.TryGetValue(def.id, out var mats) && mats[0] != null) return mats;
            mats = new[] { Make(def.name, def.main, def.glow, def.id == "coin" || def.id == "cleaver" ? .55f : .15f), Make(def.name + " accent", def.accent, Color.black, .2f) };
            materials[def.id] = mats;
            return mats;
        }

        private static Material Make(string name, Color c, Color glow, float smooth)
        {
            var m = new Material(Shader.Find("Standard")) { name = name, color = c };
            m.SetFloat("_Glossiness", smooth);
            if (glow.maxColorComponent > 0.01f) { m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", glow); m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive; }
            return m;
        }

        /// <summary>Adds the item model (two materials) to a game object.</summary>
        public static GameObject Spawn(ItemDef def, Transform parent, string name = "Item Model")
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = Mesh(def);
            go.AddComponent<MeshRenderer>().sharedMaterials = Materials(def);
            return go;
        }

        // ───────────── models (0 = main material, 1 = accent) ─────────────
        private static void Club(Builder b)
        {
            b.Tube(1, new[] { new Vector3(0, -.14f, 0), new Vector3(0, .02f, 0) }, new[] { .042f, .04f }, 7);          // wrapped grip
            b.Tube(0, new[] { new Vector3(0, .02f, 0), new Vector3(.01f, .3f, 0), new Vector3(.03f, .52f, .01f), new Vector3(.02f, .72f, 0) }, new[] { .036f, .05f, .085f, .1f }, 7);
            b.Blob(0, new Vector3(.02f, .72f, 0), new Vector3(.11f, .1f, .11f), .25f, 3);
            var r = new System.Random(4);
            for (int i = 0; i < 6; i++)   // bone spikes hammered into the head
            {
                float a = i * 1.05f, y = .5f + (i % 3) * .1f;
                Vector3 o = new Vector3(Mathf.Cos(a), .15f, Mathf.Sin(a)).normalized, at = new Vector3(.025f, y, 0) + new Vector3(o.x, 0, o.z) * .07f;
                b.Cone(1, at, o, .022f, .09f, 4);
            }
            for (int i = 0; i < 3; i++) b.Blob(0, new Vector3(Mathf.Cos(i * 2.1f) * .06f, .35f + i * .1f, Mathf.Sin(i * 2.1f) * .05f), new Vector3(.03f, .03f, .03f), .3f, i);
        }

        private static void Dagger(Builder b)
        {
            b.Tube(1, new[] { new Vector3(0, -.11f, 0), new Vector3(0, .06f, 0) }, new[] { .028f, .03f }, 6);
            b.Blob(0, new Vector3(0, -.13f, 0), new Vector3(.04f, .035f, .04f), .2f, 1);          // knucklebone pommel
            b.Box(0, new Vector3(0, .075f, 0), new Vector3(.17f, .03f, .05f));                      // guard
            b.Blade(0, new Vector3(0, .09f, 0), .045f, .012f, .34f, 0f);
        }

        private static void Cleaver(Builder b)
        {
            b.Tube(1, new[] { new Vector3(0, -.15f, 0), new Vector3(0, .08f, 0) }, new[] { .032f, .034f }, 6);
            b.Box(1, new Vector3(0, .085f, 0), new Vector3(.08f, .03f, .06f));
            // wide chopping slab, set off to one side
            b.Box(0, new Vector3(.07f, .3f, 0), new Vector3(.2f, .4f, .022f));
            b.Box(0, new Vector3(.07f, .51f, 0), new Vector3(.2f, .05f, .018f), Quaternion.Euler(0, 0, 8f));
            b.Box(1, new Vector3(.155f, .3f, 0), new Vector3(.03f, .36f, .03f));                    // dark spine
            b.Blob(1, new Vector3(.03f, .43f, 0), new Vector3(.025f, .025f, .02f), .1f, 2);          // the hole
        }

        private static void Mushroom(Builder b)
        {
            b.Tube(1, new[] { new Vector3(0, -.05f, 0), new Vector3(.01f, .1f, 0), new Vector3(0, .17f, 0) }, new[] { .04f, .035f, .03f }, 6);
            b.Blob(0, new Vector3(0, .19f, 0), new Vector3(.12f, .06f, .12f), .15f, 5);
        }

        private static void Stew(Builder b)
        {
            b.Tube(0, new[] { new Vector3(0, -.06f, 0), new Vector3(0, -.03f, 0), new Vector3(0, .05f, 0), new Vector3(0, .08f, 0) }, new[] { .07f, .11f, .14f, .145f }, 10);
            b.Disc(1, new Vector3(0, .065f, 0), .13f, 10);
            b.Blob(1, new Vector3(.04f, .075f, .02f), new Vector3(.03f, .02f, .03f), .2f, 7);
            b.Tube(0, new[] { new Vector3(-.03f, .07f, 0), new Vector3(-.09f, .2f, .02f) }, new[] { .012f, .012f }, 5);   // spoon
        }

        private static void Bone(Builder b)
        {
            b.Tube(0, new[] { new Vector3(0, -.12f, 0), new Vector3(0, .12f, 0) }, new[] { .025f, .025f }, 6);
            foreach (float y in new[] { -.13f, .13f }) foreach (float x in new[] { -.03f, .03f }) b.Blob(0, new Vector3(x, y, 0), new Vector3(.035f, .035f, .035f), .1f, 3);
        }

        private static void Coin(Builder b)
        {
            b.Tube(0, new[] { new Vector3(0, 0, -.012f), new Vector3(0, 0, .012f) }, new[] { .09f, .09f }, 12);
            b.Box(1, new Vector3(0, 0, .013f), new Vector3(.06f, .06f, .004f), Quaternion.Euler(0, 0, 45f));
        }

        private static void Fist(Builder b) { b.Blob(0, Vector3.zero, new Vector3(.08f, .07f, .07f), .2f, 1); }

        // ───────────── tiny mesh builder ─────────────
        private sealed class Builder
        {
            private readonly List<Vector3> v = new List<Vector3>();
            private readonly List<int>[] t = { new List<int>(), new List<int>() };

            private void Tri(int s, Vector3 a, Vector3 b, Vector3 c, Vector3 inside)
            {
                int i = v.Count;
                if (Vector3.Dot(Vector3.Cross(b - a, c - a), (a + b + c) / 3f - inside) < 0) { var x = b; b = c; c = x; }
                v.Add(a); v.Add(b); v.Add(c); t[s].Add(i); t[s].Add(i + 1); t[s].Add(i + 2);
            }

            public void Box(int s, Vector3 c, Vector3 size, Quaternion? rot = null)
            {
                var q = rot ?? Quaternion.identity; Vector3 h = size / 2f;
                for (int axis = 0; axis < 3; axis++)
                    foreach (float sg in new[] { -1f, 1f })
                    {
                        int u = (axis + 1) % 3, w = (axis + 2) % 3;
                        var corners = new Vector3[4];
                        float[,] uv = { { -1, -1 }, { 1, -1 }, { 1, 1 }, { -1, 1 } };
                        for (int k = 0; k < 4; k++)
                        {
                            var p = Vector3.zero; p[axis] = sg; p[u] = uv[k, 0]; p[w] = uv[k, 1];
                            corners[k] = c + q * Vector3.Scale(h, p);
                        }
                        Tri(s, corners[0], corners[1], corners[2], c); Tri(s, corners[0], corners[2], corners[3], c);
                    }
            }

            public void Tube(int s, IList<Vector3> path, IList<float> radius, int sides)
            {
                var rings = new List<Vector3[]>();
                for (int i = 0; i < path.Count; i++)
                {
                    Vector3 dir = (i < path.Count - 1 ? path[i + 1] - path[i] : path[i] - path[i - 1]).normalized;
                    Vector3 side = Vector3.Cross(dir, Mathf.Abs(dir.y) > .9f ? Vector3.right : Vector3.up).normalized, up = Vector3.Cross(side, dir);
                    var ring = new Vector3[sides];
                    for (int k = 0; k < sides; k++) { float a = k * Mathf.PI * 2f / sides; ring[k] = path[i] + (side * Mathf.Cos(a) + up * Mathf.Sin(a)) * radius[i]; }
                    rings.Add(ring);
                }
                for (int i = 0; i < rings.Count - 1; i++)
                {
                    Vector3 mid = (path[i] + path[i + 1]) / 2f;
                    for (int k = 0; k < sides; k++) { int n = (k + 1) % sides; Tri(s, rings[i][k], rings[i][n], rings[i + 1][n], mid); Tri(s, rings[i][k], rings[i + 1][n], rings[i + 1][k], mid); }
                }
                Vector3 inA = path[0] + (path[1] - path[0]) * .5f, inB = path[path.Count - 1] + (path[path.Count - 2] - path[path.Count - 1]) * .5f;
                for (int k = 0; k < sides; k++)
                {
                    int n = (k + 1) % sides;
                    Tri(s, path[0], rings[0][n], rings[0][k], inA);
                    Tri(s, path[path.Count - 1], rings[rings.Count - 1][k], rings[rings.Count - 1][n], inB);
                }
            }

            public void Cone(int s, Vector3 at, Vector3 dir, float radius, float length, int sides)
            {
                dir.Normalize();
                Vector3 side = Vector3.Cross(dir, Mathf.Abs(dir.y) > .9f ? Vector3.right : Vector3.up).normalized, up = Vector3.Cross(side, dir), tip = at + dir * length, inside = at + dir * length * .3f;
                for (int k = 0; k < sides; k++)
                {
                    float a0 = k * Mathf.PI * 2f / sides, a1 = (k + 1) * Mathf.PI * 2f / sides;
                    Vector3 p0 = at + (side * Mathf.Cos(a0) + up * Mathf.Sin(a0)) * radius, p1 = at + (side * Mathf.Cos(a1) + up * Mathf.Sin(a1)) * radius;
                    Tri(s, p0, p1, tip, inside); Tri(s, at, p1, p0, inside);
                }
            }

            /// <summary>Diamond-section blade from base (width, thickness) tapering to a point.</summary>
            public void Blade(int s, Vector3 at, float width, float thick, float length, float curve)
            {
                Vector3 tip = at + new Vector3(curve, length, 0);
                Vector3[] b = { at + new Vector3(-width, 0, 0), at + new Vector3(0, 0, thick), at + new Vector3(width, 0, 0), at + new Vector3(0, 0, -thick) };
                Vector3[] m = { at + new Vector3(-width * .85f, length * .55f, 0), at + new Vector3(curve * .3f, length * .55f, thick * .8f), at + new Vector3(width * .95f, length * .55f, 0), at + new Vector3(curve * .3f, length * .55f, -thick * .8f) };
                Vector3 inside = at + Vector3.up * length * .4f;
                for (int k = 0; k < 4; k++)
                {
                    int n = (k + 1) % 4;
                    Tri(s, b[k], b[n], m[n], inside); Tri(s, b[k], m[n], m[k], inside);
                    Tri(s, m[k], m[n], tip, inside);
                }
                Tri(s, b[0], b[2], b[1], inside); Tri(s, b[0], b[3], b[2], inside);
            }

            public void Disc(int s, Vector3 c, float radius, int sides)
            {
                for (int k = 0; k < sides; k++)
                {
                    float a0 = k * Mathf.PI * 2f / sides, a1 = (k + 1) * Mathf.PI * 2f / sides;
                    Tri(s, c, c + new Vector3(Mathf.Cos(a0), 0, Mathf.Sin(a0)) * radius, c + new Vector3(Mathf.Cos(a1), 0, Mathf.Sin(a1)) * radius, c - Vector3.up);
                }
            }

            public void Blob(int s, Vector3 c, Vector3 scale, float jitter, int seed)
            {
                var r = new System.Random(seed);
                float g = (1f + Mathf.Sqrt(5f)) / 2f;
                Vector3[] p = { new Vector3(-1, g, 0), new Vector3(1, g, 0), new Vector3(-1, -g, 0), new Vector3(1, -g, 0), new Vector3(0, -1, g), new Vector3(0, 1, g), new Vector3(0, -1, -g), new Vector3(0, 1, -g), new Vector3(g, 0, -1), new Vector3(g, 0, 1), new Vector3(-g, 0, -1), new Vector3(-g, 0, 1) };
                for (int i = 0; i < p.Length; i++) p[i] = c + Vector3.Scale(p[i].normalized * (1f + ((float)r.NextDouble() - .5f) * jitter), scale);
                int[] f = { 0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11, 1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8, 3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9, 4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1 };
                for (int i = 0; i < f.Length; i += 3) Tri(s, p[f[i]], p[f[i + 1]], p[f[i + 2]], c);
            }

            public Mesh Build(string name)
            {
                var m = new Mesh { name = "Item " + name };
                m.SetVertices(v); m.subMeshCount = 2; m.SetTriangles(t[0], 0); m.SetTriangles(t[1], 1);
                m.RecalculateNormals(); m.RecalculateBounds();
                return m;
            }
        }
    }
}
