using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Goblingame.TestScene.Editor
{
    /// <summary>
    /// Builds an original fairy-tale swamp region around the arena (first step toward an open world):
    /// terrain with ponds and hills, murky water, crooked mossy trees, dead trees, stumps, logs, rocks,
    /// reeds and cattails, lily pads, mushrooms, a mud-dome goblin hut with a boardwalk, fog and fireflies.
    /// Everything is generated low-poly and flat-shaded to match the characters, under one root object
    /// ("Swamp World") so it can be rebuilt or deleted. Deterministic (fixed seed).
    /// </summary>
    public static class SwampBuilder
    {
        private const string Folder = "Assets/SwampWorld";
        private const float Size = 240f, MaxHeight = 18f, FlatHeight = 2f, WaterLevel = 1.55f;
        private static System.Random rng;
        private static readonly Dictionary<string, Material> mats = new Dictionary<string, Material>();

        [MenuItem("Goblingame/Build Swamp Region")]
        public static void Build()
        {
            var scene = SceneManager.GetActiveScene();
            rng = new System.Random(1207);
            mats.Clear();
            foreach (var sub in new[] { "", "/Meshes", "/Materials", "/Textures" })
                if (!AssetDatabase.IsValidFolder(Folder + sub)) AssetDatabase.CreateFolder(sub == "" ? "Assets" : Folder, sub == "" ? "SwampWorld" : sub.Trim('/'));

            var old = scene.GetRootGameObjects().FirstOrDefault(o => o.name == "Swamp World");
            if (old != null) Object.DestroyImmediate(old);
            var root = new GameObject("Swamp World");

            // arena centre and floor height
            var player = scene.GetRootGameObjects().FirstOrDefault(o => o.name == "Player");
            var ground = scene.GetRootGameObjects().FirstOrDefault(o => o.name == "Ground");
            float groundY = ground != null ? ground.transform.position.y : 0f;
            Vector3 centre = player != null ? player.transform.position : Vector3.zero;
            var cast = scene.GetRootGameObjects().FirstOrDefault(o => o.name == "Enemy Cast");
            if (cast != null) { var ps = cast.GetComponentsInChildren<EnemyActor>().Select(e => e.transform.position).Append(centre).ToList(); centre = ps.Aggregate(Vector3.zero, (a, b) => a + b) / ps.Count; }
            centre.y = groundY;

            Vector3 origin = new Vector3(centre.x - Size / 2f, groundY - FlatHeight, centre.z - Size / 2f);
            var terrain = BuildTerrain(root.transform, origin, centre);
            if (ground != null) { var gr = ground.GetComponent<Renderer>(); if (gr != null) gr.enabled = false; }   // the terrain is the floor now (the arena collider stays)

            BuildWater(root.transform, origin);
            Scatter(root.transform, terrain, origin, centre);
            BuildHut(root.transform, terrain, centre + new Vector3(-24f, 0f, -18f), centre);
            BuildBoardwalk(root.transform, terrain, origin, centre);
            BuildAtmosphere(root.transform, centre);

            foreach (Transform t in root.GetComponentsInChildren<Transform>())
                if (t.GetComponent<ParticleSystem>() == null) GameObjectUtility.SetStaticEditorFlags(t.gameObject, StaticEditorFlags.BatchingStatic | StaticEditorFlags.ContributeGI | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic);

            EditorSceneManager.MarkSceneDirty(scene);
            AssetDatabase.SaveAssets();
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[Swamp] built: " + root.GetComponentsInChildren<Renderer>().Length + " renderers, terrain " + Size + " m, water at " + (origin.y + WaterLevel).ToString("0.00"));
        }

        // ───────────────────────── terrain ─────────────────────────
        private static float Noise(float x, float z, float scale, int octaves, float seed)
        {
            float v = 0, amp = 1, freq = 1, norm = 0;
            for (int i = 0; i < octaves; i++) { v += amp * Mathf.PerlinNoise(seed + x * scale * freq, seed * 1.7f + z * scale * freq); norm += amp; amp *= .5f; freq *= 2.1f; }
            return v / norm;
        }

        /// <summary>World height (above terrain origin) at a local position (0..Size).</summary>
        private static float HeightAt(float x, float z, Vector2 c)
        {
            float d = Vector2.Distance(new Vector2(x, z), c);
            float hills = 1.1f + Noise(x, z, .018f, 4, 13.1f) * 5.5f;
            // swampy basins
            float basin = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.42f, .62f, Noise(x, z, .03f, 3, 71.3f)));
            hills = Mathf.Lerp(hills, .55f, basin);
            // rising rim at the world edge
            float edge = Mathf.Max(Mathf.Abs(x - Size / 2f), Mathf.Abs(z - Size / 2f));
            hills += Mathf.Pow(Mathf.InverseLerp(Size * .36f, Size * .5f, edge), 2f) * 13f;
            // the arena stays flat, with a gentle blend into the surroundings
            float flat = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(36f, 22f, d));
            return Mathf.Lerp(hills, FlatHeight, flat);
        }

        private static Terrain BuildTerrain(Transform parent, Vector3 origin, Vector3 centre)
        {
            const int res = 257;
            var data = new TerrainData { heightmapResolution = res };
            data.size = new Vector3(Size, MaxHeight, Size);
            var c = new Vector2(centre.x - origin.x, centre.z - origin.z);
            var h = new float[res, res];
            for (int zi = 0; zi < res; zi++)
                for (int xi = 0; xi < res; xi++)
                    h[zi, xi] = HeightAt(xi * Size / (res - 1), zi * Size / (res - 1), c) / MaxHeight;
            data.SetHeights(0, 0, h);

            var layers = new[]
            {
                Layer("SwampGrass", new Color(.29f, .36f, .16f), new Color(.36f, .42f, .19f), 7f),
                Layer("Moss",       new Color(.22f, .33f, .14f), new Color(.30f, .40f, .17f), 5f),
                Layer("Mud",        new Color(.30f, .24f, .15f), new Color(.24f, .19f, .12f), 6f),
                Layer("WetMud",     new Color(.17f, .15f, .10f), new Color(.21f, .18f, .12f), 5f),
            };
            data.terrainLayers = layers;
            int ar = 256; data.alphamapResolution = ar;
            var alpha = new float[ar, ar, layers.Length];
            for (int zi = 0; zi < ar; zi++)
                for (int xi = 0; xi < ar; xi++)
                {
                    float x = xi * Size / (ar - 1), z = zi * Size / (ar - 1);
                    float height = HeightAt(x, z, c);
                    float steep = data.GetSteepness((float)xi / (ar - 1), (float)zi / (ar - 1));
                    float wet = Mathf.InverseLerp(WaterLevel + .45f, WaterLevel - .1f, height);
                    float moss = Noise(x, z, .06f, 2, 5.5f);
                    float mud = Mathf.Clamp01(Mathf.InverseLerp(18f, 34f, steep) + Mathf.InverseLerp(.62f, .75f, Noise(x, z, .05f, 2, 33f)) * .7f);
                    float[] w = { 1f, moss * 1.4f, mud, wet * 3f };
                    float sum = w.Sum();
                    for (int l = 0; l < w.Length; l++) alpha[zi, xi, l] = w[l] / sum;
                }
            data.SetAlphamaps(0, 0, alpha);
            string path = Folder + "/SwampTerrain.asset";
            AssetDatabase.DeleteAsset(path); AssetDatabase.CreateAsset(data, path);
            var go = Terrain.CreateTerrainGameObject(data);
            go.name = "Swamp Terrain"; go.transform.SetParent(parent, false); go.transform.position = origin;
            var t = go.GetComponent<Terrain>(); t.drawInstanced = true; t.heightmapPixelError = 4f; t.basemapDistance = 200f;
            return t;
        }

        private static TerrainLayer Layer(string name, Color a, Color b, float tile)
        {
            const int n = 64;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, true) { name = name, wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear };
            var px = new Color[n * n];
            for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
            {
                // tileable value noise from Perlin on a torus-ish domain
                float u = x / (float)n * Mathf.PI * 2f, v = y / (float)n * Mathf.PI * 2f;
                float k = Mathf.PerlinNoise(3f + Mathf.Cos(u) * 1.6f, 7f + Mathf.Sin(u) * 1.6f + Mathf.Cos(v) * 1.6f) * .6f + Mathf.PerlinNoise(11f + Mathf.Sin(v) * 3f, 2f + Mathf.Cos(u) * 3f) * .4f;
                var col = Color.Lerp(a, b, k); col.a = name.Contains("Wet") ? .35f : .04f;   // alpha = smoothness for the terrain shader
                px[y * n + x] = col;
            }
            tex.SetPixels(px); tex.Apply(true);
            string texPath = Folder + "/Textures/" + name + ".asset";
            AssetDatabase.DeleteAsset(texPath); AssetDatabase.CreateAsset(tex, texPath);
            var layer = new TerrainLayer { name = name, diffuseTexture = tex, tileSize = new Vector2(tile, tile), smoothness = name.Contains("Wet") ? .5f : .05f };
            string lp = Folder + "/" + name + ".terrainlayer";
            AssetDatabase.DeleteAsset(lp); AssetDatabase.CreateAsset(layer, lp);
            return layer;
        }

        private static void BuildWater(Transform parent, Vector3 origin)
        {
            var water = GameObject.CreatePrimitive(PrimitiveType.Plane);
            water.name = "Swamp Water"; water.transform.SetParent(parent, false);
            water.transform.position = origin + new Vector3(Size / 2f, WaterLevel, Size / 2f);
            water.transform.localScale = new Vector3(Size / 10f, 1f, Size / 10f);
            Object.DestroyImmediate(water.GetComponent<Collider>());
            var m = Mat("Water", new Color(.16f, .23f, .12f, .78f), .92f, transparent: true);
            water.GetComponent<Renderer>().sharedMaterial = m;
        }

        /// <summary>Soft round particle sprite (radial falloff), saved once.</summary>
        public static Texture2D SoftDot()
        {
            const string path = Folder + "/Textures/SoftDot.asset";
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (tex != null) return tex;
            tex = SoftDotTexture();
            AssetDatabase.CreateAsset(tex, path);
            return tex;
        }
        public static Texture2D SoftDotTexture()
        {
            const int n = 64;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, true) { name = "SoftDot", wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
            {
                float d = Vector2.Distance(new Vector2(x + .5f, y + .5f), new Vector2(n / 2f, n / 2f)) / (n / 2f);
                float a = Mathf.Clamp01(1f - d); a = a * a * (3f - 2f * a);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
            tex.Apply(true); return tex;
        }

        // ───────────────────────── materials / meshes ─────────────────────────
        private static Material Mat(string name, Color color, float smooth = .08f, bool transparent = false, Color? emission = null)
        {
            if (mats.TryGetValue(name, out var m)) return m;
            string path = Folder + "/Materials/" + name + ".mat";
            m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(Shader.Find("Standard")); AssetDatabase.CreateAsset(m, path); }
            m.color = color; m.SetFloat("_Glossiness", smooth); m.SetFloat("_Metallic", 0f);
            if (transparent)
            {
                m.SetFloat("_Mode", 3f); m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One); m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                m.SetInt("_ZWrite", 0); m.DisableKeyword("_ALPHATEST_ON"); m.DisableKeyword("_ALPHABLEND_ON"); m.EnableKeyword("_ALPHAPREMULTIPLY_ON"); m.renderQueue = 3000;
            }
            if (emission.HasValue) { m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", emission.Value); m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive; }
            EditorUtility.SetDirty(m);
            mats[name] = m; return m;
        }

        /// <summary>Flat-shaded mesh builder (every triangle gets its own vertices).</summary>
        private sealed class MeshB
        {
            public readonly List<Vector3> v = new List<Vector3>(); public readonly List<int> t = new List<int>();
            public void Tri(Vector3 a, Vector3 b, Vector3 c) { int i = v.Count; v.Add(a); v.Add(b); v.Add(c); t.Add(i); t.Add(i + 1); t.Add(i + 2); }
            public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d) { Tri(a, b, c); Tri(a, c, d); }
            /// <summary>Tapered n-gon tube along a polyline.</summary>
            public void Tube(IList<Vector3> path, IList<float> radius, int sides)
            {
                for (int s = 0; s < path.Count - 1; s++)
                {
                    Vector3 dir = (path[s + 1] - path[s]).normalized;
                    Vector3 side = Vector3.Cross(dir, Mathf.Abs(dir.y) > .9f ? Vector3.right : Vector3.up).normalized, up2 = Vector3.Cross(side, dir);
                    for (int k = 0; k < sides; k++)
                    {
                        float a0 = k * Mathf.PI * 2f / sides, a1 = (k + 1) * Mathf.PI * 2f / sides;
                        Vector3 o0 = side * Mathf.Cos(a0) + up2 * Mathf.Sin(a0), o1 = side * Mathf.Cos(a1) + up2 * Mathf.Sin(a1);
                        Quad(path[s] + o0 * radius[s], path[s + 1] + o0 * radius[s + 1], path[s + 1] + o1 * radius[s + 1], path[s] + o1 * radius[s]);
                    }
                }
                // cap the top
                int last = path.Count - 1; Vector3 dl = (path[last] - path[last - 1]).normalized;
                Vector3 sl = Vector3.Cross(dl, Mathf.Abs(dl.y) > .9f ? Vector3.right : Vector3.up).normalized, ul = Vector3.Cross(sl, dl);
                for (int k = 0; k < sides; k++)
                {
                    float a0 = k * Mathf.PI * 2f / sides, a1 = (k + 1) * Mathf.PI * 2f / sides;
                    Tri(path[last], path[last] + (sl * Mathf.Cos(a1) + ul * Mathf.Sin(a1)) * radius[last], path[last] + (sl * Mathf.Cos(a0) + ul * Mathf.Sin(a0)) * radius[last]);
                }
            }
            /// <summary>Low-poly blob (icosphere-ish, jittered).</summary>
            public void Blob(Vector3 centre, Vector3 scale, float jitter, System.Random r)
            {
                float g = (1f + Mathf.Sqrt(5f)) / 2f;
                var p = new[] { new Vector3(-1, g, 0), new Vector3(1, g, 0), new Vector3(-1, -g, 0), new Vector3(1, -g, 0), new Vector3(0, -1, g), new Vector3(0, 1, g),
                    new Vector3(0, -1, -g), new Vector3(0, 1, -g), new Vector3(g, 0, -1), new Vector3(g, 0, 1), new Vector3(-g, 0, -1), new Vector3(-g, 0, 1) };
                for (int i = 0; i < p.Length; i++) { p[i] = p[i].normalized * (1f + ((float)r.NextDouble() - .5f) * jitter); p[i] = Vector3.Scale(p[i], scale) + centre; }
                int[] f = { 0,11,5, 0,5,1, 0,1,7, 0,7,10, 0,10,11, 1,5,9, 5,11,4, 11,10,2, 10,7,6, 7,1,8, 3,9,4, 3,4,2, 3,2,6, 3,6,8, 3,8,9, 4,9,5, 2,4,11, 6,2,10, 8,6,7, 9,8,1 };
                for (int i = 0; i < f.Length; i += 3)
                {
                    Vector3 a = p[f[i]], b = p[f[i + 1]], c = p[f[i + 2]];
                    if (Vector3.Dot(Vector3.Cross(b - a, c - a), (a + b + c) / 3f - centre) < 0) Tri(a, c, b); else Tri(a, b, c);
                }
            }
            public Mesh Build(string name)
            {
                var m = new Mesh { name = name };
                if (v.Count > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                m.SetVertices(v); m.SetTriangles(t, 0); m.RecalculateNormals(); m.RecalculateBounds();
                return m;
            }
        }

        private static readonly Dictionary<string, Mesh> meshCache = new Dictionary<string, Mesh>();
        private static Mesh SaveMesh(MeshB b, string name)
        {
            var m = b.Build(name);
            string path = Folder + "/Meshes/" + name + ".asset";
            AssetDatabase.DeleteAsset(path); AssetDatabase.CreateAsset(m, path);
            meshCache[name] = m; return m;
        }

        private static GameObject Part(Transform parent, string name, Mesh mesh, Material mat)
        {
            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(parent, false);
            go.GetComponent<MeshFilter>().sharedMesh = mesh; go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            return go;
        }

        private static float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);

        // ───────────────────────── prop prototypes ─────────────────────────
        private static (Mesh trunk, Mesh leaves, Mesh moss) MakeTree(int variant)
        {
            var r = new System.Random(100 + variant);
            float Rr(float a, float b) => a + (float)r.NextDouble() * (b - a);
            var trunk = new MeshB(); var leaves = new MeshB(); var moss = new MeshB();
            // crooked trunk
            var path = new List<Vector3>(); var rad = new List<float>();
            Vector3 p = Vector3.zero; Vector3 lean = new Vector3(Rr(-.35f, .35f), 1f, Rr(-.35f, .35f)).normalized;
            float height = Rr(6f, 9.5f);
            for (int i = 0; i <= 6; i++)
            {
                path.Add(p); rad.Add(Mathf.Lerp(.55f, .18f, i / 6f) * (i == 0 ? 1.35f : 1f));
                lean = (lean + new Vector3(Rr(-.35f, .35f), 0, Rr(-.35f, .35f))).normalized; lean.y = Mathf.Max(lean.y, .55f); lean.Normalize();
                p += lean * height / 6f;
            }
            trunk.Tube(path, rad, 7);
            // roots
            for (int k = 0; k < 4; k++)
            {
                float a = k * Mathf.PI / 2f + Rr(-.3f, .3f); Vector3 o = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                trunk.Tube(new[] { Vector3.up * .6f + o * .3f, o * 1.1f + Vector3.up * .15f, o * 1.6f - Vector3.up * .15f }, new[] { .22f, .14f, .06f }, 5);
            }
            // branches + canopy clumps
            Vector3 top = path[path.Count - 1];
            for (int b = 0; b < 4; b++)
            {
                int at = 3 + r.Next(3); Vector3 start = path[at];
                float a = Rr(0, Mathf.PI * 2f); Vector3 dir = new Vector3(Mathf.Cos(a), Rr(.15f, .6f), Mathf.Sin(a)).normalized;
                Vector3 end = start + dir * Rr(1.6f, 2.8f);
                trunk.Tube(new[] { start, (start + end) / 2f + Vector3.up * .3f, end }, new[] { .16f, .1f, .05f }, 5);
                leaves.Blob(end + Vector3.up * .3f, new Vector3(Rr(1.4f, 2.0f), Rr(.8f, 1.1f), Rr(1.4f, 2.0f)), .35f, r);
                // hanging moss strands
                for (int m = 0; m < 3; m++)
                {
                    Vector3 hang = end + new Vector3(Rr(-1f, 1f), -.2f, Rr(-1f, 1f));
                    float len = Rr(1.2f, 2.6f);
                    moss.Tube(new[] { hang, hang + new Vector3(Rr(-.1f, .1f), -len * .5f, Rr(-.1f, .1f)), hang + new Vector3(Rr(-.15f, .15f), -len, Rr(-.15f, .15f)) }, new[] { .07f, .05f, .015f }, 4);
                }
            }
            leaves.Blob(top + Vector3.up * .6f, new Vector3(Rr(2f, 2.8f), Rr(1f, 1.4f), Rr(2f, 2.8f)), .3f, r);
            return (SaveMesh(trunk, "TreeTrunk" + variant), SaveMesh(leaves, "TreeLeaves" + variant), SaveMesh(moss, "TreeMoss" + variant));
        }

        private static Mesh MakeDeadTree(int variant)
        {
            var r = new System.Random(300 + variant); float Rr(float a, float b) => a + (float)r.NextDouble() * (b - a);
            var b = new MeshB(); var path = new List<Vector3>(); var rad = new List<float>(); Vector3 p = Vector3.zero;
            float h = Rr(3.5f, 6f); Vector3 lean = new Vector3(Rr(-.4f, .4f), 1, Rr(-.4f, .4f)).normalized;
            for (int i = 0; i <= 5; i++) { path.Add(p); rad.Add(Mathf.Lerp(.35f, .06f, i / 5f)); lean = (lean + new Vector3(Rr(-.4f, .4f), 0, Rr(-.4f, .4f))).normalized; lean.y = Mathf.Max(lean.y, .5f); p += lean.normalized * h / 5f; }
            b.Tube(path, rad, 6);
            for (int k = 0; k < 4; k++)
            {
                Vector3 s = path[2 + r.Next(3)]; float a = Rr(0, 6.28f); Vector3 d = new Vector3(Mathf.Cos(a), Rr(.1f, .8f), Mathf.Sin(a)).normalized;
                Vector3 e = s + d * Rr(.8f, 1.8f); b.Tube(new[] { s, e, e + new Vector3(Rr(-.4f, .4f), Rr(.1f, .5f), Rr(-.4f, .4f)) }, new[] { .09f, .05f, .015f }, 5);
            }
            return SaveMesh(b, "DeadTree" + variant);
        }

        private static Mesh MakeRock(int variant, out float radius)
        {
            var r = new System.Random(500 + variant); var b = new MeshB();
            Vector3 s = new Vector3(1f + (float)r.NextDouble() * .6f, .55f + (float)r.NextDouble() * .4f, 1f + (float)r.NextDouble() * .5f);
            b.Blob(Vector3.up * s.y * .55f, s, .45f, r); radius = Mathf.Max(s.x, s.z);
            return SaveMesh(b, "Rock" + variant);
        }

        private static (Mesh stalks, Mesh heads) MakeReeds(int variant)
        {
            var r = new System.Random(700 + variant); float Rr(float a, float b) => a + (float)r.NextDouble() * (b - a);
            var st = new MeshB(); var hd = new MeshB();
            for (int i = 0; i < 9; i++)
            {
                Vector3 bse = new Vector3(Rr(-.6f, .6f), 0, Rr(-.6f, .6f)); float h = Rr(1.2f, 2.3f); Vector3 tip = bse + new Vector3(Rr(-.3f, .3f), h, Rr(-.3f, .3f));
                st.Tube(new[] { bse, (bse + tip) / 2f, tip }, new[] { .03f, .025f, .01f }, 4);
                if (i % 2 == 0) { Vector3 c0 = Vector3.Lerp(bse, tip, .72f); hd.Tube(new[] { c0, c0 + (tip - bse).normalized * .32f }, new[] { .065f, .055f }, 6); }
            }
            // a few leaf blades
            for (int i = 0; i < 6; i++)
            {
                Vector3 bse = new Vector3(Rr(-.5f, .5f), 0, Rr(-.5f, .5f)); Vector3 tip = bse + new Vector3(Rr(-.6f, .6f), Rr(.8f, 1.4f), Rr(-.6f, .6f));
                Vector3 w = Vector3.Cross(tip - bse, Vector3.up).normalized * .06f;
                st.Tri(bse - w, tip, bse + w); st.Tri(bse + w, tip, bse - w);
            }
            return (SaveMesh(st, "Reeds" + variant), SaveMesh(hd, "Cattails" + variant));
        }

        private static (Mesh pad, Mesh flower) MakeLily()
        {
            var pad = new MeshB(); int n = 12;
            for (int k = 1; k < n; k++)   // leave one wedge out: the notch
            {
                float a0 = k * Mathf.PI * 2f / n, a1 = (k + 1) * Mathf.PI * 2f / n;
                pad.Tri(Vector3.zero, new Vector3(Mathf.Cos(a1), 0, Mathf.Sin(a1)) * .55f, new Vector3(Mathf.Cos(a0), 0, Mathf.Sin(a0)) * .55f);
            }
            var fl = new MeshB(); var r = new System.Random(9);
            for (int k = 0; k < 6; k++)
            {
                float a = k * Mathf.PI / 3f; Vector3 d = new Vector3(Mathf.Cos(a), .9f, Mathf.Sin(a)).normalized * .16f; Vector3 s = Vector3.Cross(d, Vector3.up).normalized * .05f;
                fl.Tri(Vector3.up * .02f, d + s, d - s); fl.Tri(Vector3.up * .02f, d - s, d + s);
            }
            return (SaveMesh(pad, "LilyPad"), SaveMesh(fl, "LilyFlower"));
        }

        private static (Mesh stem, Mesh cap) MakeMushroom()
        {
            var st = new MeshB(); var cp = new MeshB(); var r = new System.Random(77);
            st.Tube(new[] { Vector3.zero, new Vector3(.03f, .22f, 0), new Vector3(0, .42f, .02f) }, new[] { .09f, .07f, .06f }, 6);
            cp.Blob(new Vector3(0, .45f, 0), new Vector3(.28f, .16f, .28f), .2f, r);
            return (SaveMesh(st, "MushStem"), SaveMesh(cp, "MushCap"));
        }

        private static Mesh MakeLog(out float length)
        {
            var b = new MeshB(); length = 3.2f;
            b.Tube(new[] { new Vector3(-length / 2f, .32f, 0), new Vector3(0, .34f, .05f), new Vector3(length / 2f, .3f, 0) }, new[] { .32f, .3f, .28f }, 7);
            b.Tube(new[] { new Vector3(.4f, .5f, .1f), new Vector3(.6f, 1.0f, .3f) }, new[] { .08f, .03f }, 5);
            return SaveMesh(b, "Log");
        }

        private static Mesh MakeStump()
        {
            var b = new MeshB(); b.Tube(new[] { Vector3.zero, Vector3.up * .5f, Vector3.up * .75f }, new[] { .55f, .45f, .42f }, 8);
            for (int k = 0; k < 4; k++) { float a = k * 1.57f + .4f; Vector3 o = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)); b.Tube(new[] { Vector3.up * .3f + o * .3f, o * .9f }, new[] { .15f, .04f }, 5); }
            return SaveMesh(b, "Stump");
        }

        // ───────────────────────── scattering ─────────────────────────
        private static void Scatter(Transform parent, Terrain terrain, Vector3 origin, Vector3 centre)
        {
            var props = new GameObject("Props").transform; props.SetParent(parent, false);
            var trees = Enumerable.Range(0, 5).Select(MakeTree).ToArray();
            var dead = Enumerable.Range(0, 3).Select(MakeDeadTree).ToArray();
            var rocks = Enumerable.Range(0, 4).Select(i => (mesh: MakeRock(i, out float rr), radius: rr)).ToArray();
            var reeds = Enumerable.Range(0, 3).Select(MakeReeds).ToArray();
            var lily = MakeLily(); var mush = MakeMushroom(); var log = MakeLog(out _); var stump = MakeStump();

            Material bark = Mat("Bark", new Color(.27f, .21f, .15f)), leaf = Mat("SwampLeaves", new Color(.22f, .32f, .13f)), mossM = Mat("HangingMoss", new Color(.40f, .45f, .30f));
            Material deadBark = Mat("DeadBark", new Color(.36f, .33f, .28f)), stone = Mat("Stone", new Color(.40f, .41f, .37f)), stoneMoss = Mat("MossyStone", new Color(.33f, .40f, .26f));
            Material reed = Mat("Reed", new Color(.42f, .48f, .22f)), cattail = Mat("Cattail", new Color(.33f, .21f, .12f)), pad = Mat("LilyPad", new Color(.24f, .42f, .17f), .35f);
            Material flower = Mat("LilyFlower", new Color(.95f, .72f, .80f)), cap = Mat("ToadCap", new Color(.80f, .34f, .14f)), stem = Mat("ToadStem", new Color(.86f, .80f, .64f));

            var placed = new List<(Vector3 p, float r)>();
            Vector3 Pick(float minArena, float maxArena)
            {
                float a = R(0, Mathf.PI * 2f), d = Mathf.Sqrt(R(minArena * minArena, maxArena * maxArena));
                var p = new Vector3(centre.x + Mathf.Cos(a) * d, 0, centre.z + Mathf.Sin(a) * d);
                p.y = terrain.SampleHeight(p) + terrain.transform.position.y; return p;
            }
            bool Free(Vector3 p, float r) => placed.All(q => (new Vector2(q.p.x - p.x, q.p.z - p.z)).magnitude > q.r + r);
            float water = origin.y + WaterLevel;
            int n;

            // living trees on dry ground
            n = 0; for (int i = 0; i < 900 && n < 110; i++)
            {
                var p = Pick(26f, Size * .47f); if (p.y < water + .25f || !Free(p, 2.4f)) continue;
                var (tr, lv, ms) = trees[rng.Next(trees.Length)];
                var t = new GameObject("Swamp Tree").transform; t.SetParent(props, false); t.position = p - Vector3.up * .2f;
                t.rotation = Quaternion.Euler(0, R(0, 360), 0); t.localScale = Vector3.one * R(.8f, 1.3f);
                Part(t, "Trunk", tr, bark); Part(t, "Leaves", lv, leaf); Part(t, "Moss", ms, mossM);
                var col = t.gameObject.AddComponent<CapsuleCollider>(); col.radius = .45f; col.height = 6f; col.center = Vector3.up * 3f;
                placed.Add((p, 2.4f)); n++;
            }
            // dead trees and stumps, often near water
            n = 0; for (int i = 0; i < 400 && n < 30; i++)
            {
                var p = Pick(24f, Size * .46f); if (p.y > water + 1.5f || !Free(p, 1.4f)) continue;
                var t = new GameObject("Dead Tree").transform; t.SetParent(props, false); t.position = p - Vector3.up * .15f;
                t.rotation = Quaternion.Euler(R(-6, 6), R(0, 360), R(-6, 6)); t.localScale = Vector3.one * R(.8f, 1.2f);
                Part(t, "Trunk", dead[rng.Next(dead.Length)], deadBark);
                var col = t.gameObject.AddComponent<CapsuleCollider>(); col.radius = .3f; col.height = 4f; col.center = Vector3.up * 2f;
                placed.Add((p, 1.4f)); n++;
            }
            n = 0; for (int i = 0; i < 200 && n < 14; i++)
            {
                var p = Pick(20f, Size * .45f); if (p.y < water || !Free(p, 1f)) continue;
                var t = Part(props, "Stump", stump, bark).transform; t.position = p - Vector3.up * .05f; t.rotation = Quaternion.Euler(0, R(0, 360), 0);
                var col = t.gameObject.AddComponent<CapsuleCollider>(); col.radius = .5f; col.height = .8f; col.center = Vector3.up * .4f;
                placed.Add((p, 1f)); n++;
            }
            n = 0; for (int i = 0; i < 200 && n < 12; i++)
            {
                var p = Pick(23f, Size * .45f); if (p.y < water - .2f || !Free(p, 1.8f)) continue;
                var t = Part(props, "Fallen Log", log, bark).transform; t.position = p - Vector3.up * .08f; t.rotation = Quaternion.Euler(0, R(0, 360), R(-4, 4));
                var col = t.gameObject.AddComponent<CapsuleCollider>(); col.direction = 0; col.radius = .32f; col.height = 3.2f; col.center = Vector3.up * .32f;
                placed.Add((p, 1.8f)); n++;
            }
            // rocks (some at the arena edge)
            n = 0; for (int i = 0; i < 600 && n < 55; i++)
            {
                var p = Pick(20f, Size * .48f); if (!Free(p, 1.2f)) continue;
                var (mesh, rr) = rocks[rng.Next(rocks.Length)];
                float s = R(.5f, 1.8f);
                var t = Part(props, "Rock", mesh, rng.NextDouble() < .5 ? stone : stoneMoss).transform; t.position = p - Vector3.up * .15f * s; t.rotation = Quaternion.Euler(R(-10, 10), R(0, 360), R(-10, 10)); t.localScale = Vector3.one * s;
                var mc = t.gameObject.AddComponent<MeshCollider>(); mc.sharedMesh = mesh; mc.convex = true;
                placed.Add((p, rr * s * .8f)); n++;
            }
            // reeds along the water's edge
            n = 0; for (int i = 0; i < 4000 && n < 240; i++)
            {
                var p = Pick(22f, Size * .47f); if (Mathf.Abs(p.y - water) > .45f) continue;
                var (st, hd) = reeds[rng.Next(reeds.Length)];
                var t = new GameObject("Reeds").transform; t.SetParent(props, false); t.position = p - Vector3.up * .1f; t.rotation = Quaternion.Euler(0, R(0, 360), 0); t.localScale = Vector3.one * R(.8f, 1.3f);
                Part(t, "Stalks", st, reed); Part(t, "Cattails", hd, cattail); n++;
            }
            // lily pads on open water
            n = 0; for (int i = 0; i < 6000 && n < 160; i++)
            {
                var p = Pick(24f, Size * .47f); if (p.y > water - .25f) continue;
                var t = Part(props, "Lily Pad", lily.pad, pad).transform; t.position = new Vector3(p.x, water + .02f, p.z); t.rotation = Quaternion.Euler(0, R(0, 360), 0); t.localScale = Vector3.one * R(.7f, 1.4f);
                if (rng.NextDouble() < .25) { var f = Part(t, "Flower", lily.flower, flower).transform; f.localPosition = new Vector3(R(-.15f, .15f), .02f, R(-.15f, .15f)); }
                n++;
            }
            // mushroom clusters
            n = 0; for (int i = 0; i < 800 && n < 45; i++)
            {
                var p = Pick(18f, Size * .45f); if (p.y < water + .1f || !Free(p, .5f)) continue;
                var t = new GameObject("Mushrooms").transform; t.SetParent(props, false); t.position = p;
                int c = 2 + rng.Next(4);
                for (int k = 0; k < c; k++)
                {
                    var m = new GameObject("Toadstool").transform; m.SetParent(t, false);
                    m.localPosition = new Vector3(R(-.4f, .4f), -.02f, R(-.4f, .4f)); m.localRotation = Quaternion.Euler(R(-12, 12), R(0, 360), R(-12, 12)); m.localScale = Vector3.one * R(.6f, 1.6f);
                    Part(m, "Stem", mush.stem, stem); Part(m, "Cap", mush.cap, cap);
                }
                n++;
            }
        }

        // ───────────────────────── hut & boardwalk ─────────────────────────
        private static void BuildHut(Transform parent, Terrain terrain, Vector3 at, Vector3 centre)
        {
            at.y = terrain.SampleHeight(at) + terrain.transform.position.y;
            var hut = new GameObject("Mud-Dome Hut").transform; hut.SetParent(parent, false); hut.position = at;
            Vector3 face = centre - at; face.y = 0; hut.rotation = Quaternion.LookRotation(face.normalized);
            var r = new System.Random(4242);
            var dome = new MeshB(); dome.Blob(Vector3.up * .8f, new Vector3(3.4f, 3.0f, 3.2f), .18f, r);
            var turf = new MeshB(); turf.Blob(Vector3.up * 3.3f, new Vector3(2.6f, .9f, 2.5f), .35f, r);
            var door = new MeshB();
            Vector3 d0 = new Vector3(-.65f, 0, 3.05f), d1 = new Vector3(.65f, 0, 3.15f), d2 = new Vector3(.55f, 1.9f, 3.0f), d3 = new Vector3(-.5f, 2.0f, 2.9f), dt = new Vector3(0, 2.35f, 2.85f);
            door.Quad(d0, d1, d2, d3); door.Tri(d3, d2, dt);
            var frame = new MeshB(); frame.Tube(new[] { d0 + Vector3.left * .1f, d3 + Vector3.left * .1f, dt + Vector3.up * .1f, d2 + Vector3.right * .1f, d1 + Vector3.right * .1f }, new[] { .1f, .1f, .1f, .1f, .1f }, 5);
            var chimney = new MeshB();
            for (int i = 0; i < 5; i++) chimney.Blob(new Vector3(1.25f + (float)r.NextDouble() * .08f, 2.2f + i * .4f, -.9f + (float)r.NextDouble() * .08f), new Vector3(.45f - i * .03f, .26f, .45f - i * .03f), .3f, r);
            Part(hut, "Dome", SaveMesh(dome, "HutDome"), Mat("HutMud", new Color(.38f, .28f, .17f)));
            Part(hut, "Turf Roof", SaveMesh(turf, "HutTurf"), Mat("HutTurf", new Color(.31f, .41f, .16f)));
            Part(hut, "Door", SaveMesh(door, "HutDoor"), Mat("HutDoor", new Color(.42f, .27f, .13f)));
            Part(hut, "Door Frame", SaveMesh(frame, "HutFrame"), Mat("Bark", new Color(.27f, .21f, .15f)));
            Part(hut, "Chimney", SaveMesh(chimney, "HutChimney"), Mat("Stone", new Color(.40f, .41f, .37f)));
            var col = hut.gameObject.AddComponent<SphereCollider>(); col.center = Vector3.up * .6f; col.radius = 3.1f;
            // lantern post with a warm light
            var post = new MeshB(); post.Tube(new[] { Vector3.zero, Vector3.up * 1.6f, new Vector3(.45f, 1.85f, 0) }, new[] { .07f, .06f, .05f }, 5);
            var lamp = new MeshB(); lamp.Blob(new Vector3(.45f, 1.6f, 0), new Vector3(.16f, .22f, .16f), .1f, r);
            var postT = Part(hut, "Lantern Post", SaveMesh(post, "LanternPost"), Mat("Bark", Color.black)).transform; postT.localPosition = new Vector3(1.9f, 0, 3.6f);
            Part(postT, "Lantern", SaveMesh(lamp, "Lantern"), Mat("LanternGlow", new Color(1f, .78f, .35f), .2f, emission: new Color(1.6f, 1.0f, .35f)));
            var light = new GameObject("Lantern Light").AddComponent<Light>(); light.transform.SetParent(postT, false); light.transform.localPosition = new Vector3(.45f, 1.55f, 0);
            light.type = LightType.Point; light.range = 7f; light.intensity = 1.6f; light.color = new Color(1f, .75f, .4f);
            // crooked fence
            var fence = new MeshB();
            for (int i = 0; i < 9; i++)
            {
                float a = Mathf.Lerp(-.9f, .9f, i / 8f) + Mathf.PI * .5f; Vector3 b = new Vector3(Mathf.Cos(a) * 5.2f, 0, Mathf.Sin(a) * 5.2f + .5f);
                fence.Tube(new[] { b - Vector3.up * .2f, b + new Vector3((float)r.NextDouble() * .1f, 1.0f + (float)r.NextDouble() * .3f, 0) }, new[] { .07f, .05f }, 5);
            }
            Part(hut, "Fence", SaveMesh(fence, "HutFence"), Mat("DeadBark", new Color(.36f, .33f, .28f)));
        }

        private static void BuildBoardwalk(Transform parent, Terrain terrain, Vector3 origin, Vector3 centre)
        {
            // find a nearby pond and lay planks from the arena edge across it
            float water = origin.y + WaterLevel; Vector3 best = Vector3.zero; float bestD = float.MaxValue;
            for (int i = 0; i < 400; i++)
            {
                float a = i * .157f, d = 30f + (i % 20) * 2.5f;
                var p = new Vector3(centre.x + Mathf.Cos(a) * d, 0, centre.z + Mathf.Sin(a) * d);
                float h = terrain.SampleHeight(p) + terrain.transform.position.y;
                if (h < water - .2f && d < bestD) { bestD = d; best = p; }
            }
            if (bestD == float.MaxValue) return;
            var walk = new GameObject("Boardwalk").transform; walk.SetParent(parent, false);
            Vector3 dir = (best - new Vector3(centre.x, 0, centre.z)).normalized;
            Vector3 start = new Vector3(centre.x, 0, centre.z) + dir * 24f;
            var plank = new MeshB(); plank.Quad(new Vector3(-.9f, 0, -.22f), new Vector3(-.9f, 0, .22f), new Vector3(.9f, 0, .22f), new Vector3(.9f, 0, -.22f));
            plank.Quad(new Vector3(-.9f, -.08f, -.22f), new Vector3(.9f, -.08f, -.22f), new Vector3(.9f, -.08f, .22f), new Vector3(-.9f, -.08f, .22f));
            var plankMesh = SaveMesh(plank, "Plank"); var wood = Mat("HutDoor", new Color(.42f, .27f, .13f));
            float length = bestD - 24f + 12f; int count = Mathf.RoundToInt(length / .5f);
            for (int i = 0; i < count; i++)
            {
                Vector3 p = start + dir * (i * .5f);
                float h = Mathf.Max(water + .35f, terrain.SampleHeight(p) + terrain.transform.position.y + .12f);
                var t = Part(walk, "Plank", plankMesh, wood).transform; t.position = new Vector3(p.x, h, p.z);
                t.rotation = Quaternion.LookRotation(Vector3.Cross(dir, Vector3.up)) * Quaternion.Euler(R(-3, 3), R(-4, 4), R(-3, 3));
                var bc = t.gameObject.AddComponent<BoxCollider>(); bc.size = new Vector3(1.8f, .12f, .44f); bc.center = new Vector3(0, -.04f, 0);
            }
            Debug.Log("[Swamp] boardwalk: " + count + " planks toward the pond at " + bestD.ToString("0") + " m");
        }

        private static void BuildAtmosphere(Transform parent, Vector3 centre)
        {
            RenderSettings.fog = true; RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = new Color(.46f, .52f, .40f); RenderSettings.fogDensity = .011f;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(.55f, .62f, .52f); RenderSettings.ambientEquatorColor = new Color(.38f, .42f, .30f); RenderSettings.ambientGroundColor = new Color(.18f, .17f, .12f);
            var sun = Object.FindObjectsByType<Light>(FindObjectsSortMode.None).FirstOrDefault(l => l.type == LightType.Directional);
            if (sun != null) { sun.color = new Color(1f, .9f, .72f); sun.intensity = 1.05f; sun.transform.rotation = Quaternion.Euler(38f, -35f, 0f); sun.shadows = LightShadows.Soft; }
            var cam = Camera.main; if (cam != null) { cam.backgroundColor = RenderSettings.fogColor; cam.farClipPlane = Mathf.Max(cam.farClipPlane, 300f); }

            // fireflies drifting over the swamp
            var go = new GameObject("Fireflies"); go.transform.SetParent(parent, false); go.transform.position = centre + Vector3.up * 1.6f;
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main; main.loop = true; main.startLifetime = new ParticleSystem.MinMaxCurve(4f, 8f); main.startSpeed = .15f; main.startSize = new ParticleSystem.MinMaxCurve(.05f, .1f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(.85f, 1f, .4f, 1f), new Color(1f, .9f, .3f, 1f)); main.maxParticles = 400; main.simulationSpace = ParticleSystemSimulationSpace.World;
            var em = ps.emission; em.rateOverTime = 45f;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(80f, 2.5f, 80f);
            var noise = ps.noise; noise.enabled = true; noise.strength = .6f; noise.frequency = .4f;
            var col = ps.colorOverLifetime; col.enabled = true; var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) }, new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, .2f), new GradientAlphaKey(.2f, .5f), new GradientAlphaKey(1f, .75f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var rend = go.GetComponent<ParticleSystemRenderer>();
            var shader = Shader.Find("Legacy Shaders/Particles/Additive");
            if (shader != null)
            {
                var m = new Material(shader) { name = "Firefly" };
                m.mainTexture = SoftDot(); m.SetColor("_TintColor", new Color(.6f, .7f, .3f, .5f));
                string path = Folder + "/Materials/Firefly.mat"; AssetDatabase.DeleteAsset(path); AssetDatabase.CreateAsset(m, path);
                rend.sharedMaterial = m;
            }
        }
    }
}
