using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Goblingame.TestScene.Editor
{
    /// <summary>
    /// Builds the open-world starting region: a twilight forest around the arena.
    /// Tall dark pines, coloured dusk fog and a gradient sky, teal grass, golden leaf litter, red ferns,
    /// glowing mushrooms, light shafts and fireflies; ponds with reeds. Gameplay places (see the
    /// Places part of this class): campfires (healing), hollow oak stumps (mob lairs), houses whose
    /// roof fades when you walk in (chests, an ambush, a rune circle) and an underground chamber.
    /// Everything is generated low-poly under one root ("Twilight World"); deterministic, rebuildable.
    /// </summary>
    public static partial class SwampBuilder
    {
        private const string Folder = "Assets/SwampWorld";
        public const string RootName = "Twilight World";
        private const float Size = 260f, MaxHeight = 20f, FlatHeight = 2f, WaterLevel = 1.5f;
        private static System.Random rng;
        private static readonly Dictionary<string, Material> mats = new Dictionary<string, Material>();
        private struct Pad { public Vector2 c; public float r, h; }
        private static readonly List<Pad> pads = new List<Pad>();
        private static Vector2 arena;
        private static Terrain world;

        [MenuItem("Goblingame/Build World (Twilight Forest)")]
        public static void Build()
        {
            var scene = SceneManager.GetActiveScene();
            rng = new System.Random(1207); mats.Clear(); pads.Clear(); paths.Clear(); occupied.Clear(); firefly = null;
            foreach (var sub in new[] { "Meshes", "Materials", "Textures", "Shaders" })
                if (!AssetDatabase.IsValidFolder(Folder + "/" + sub)) AssetDatabase.CreateFolder(Folder, sub);
            // fresh meshes every build
            foreach (var guid in AssetDatabase.FindAssets("t:Mesh", new[] { Folder + "/Meshes" })) AssetDatabase.DeleteAsset(AssetDatabase.GUIDToAssetPath(guid));

            foreach (var name in new[] { RootName, "Swamp World" })
            { var old = scene.GetRootGameObjects().FirstOrDefault(o => o.name == name); if (old != null) Object.DestroyImmediate(old); }
            var root = new GameObject(RootName).transform;

            var player = scene.GetRootGameObjects().FirstOrDefault(o => o.name == "Player");
            var ground = scene.GetRootGameObjects().FirstOrDefault(o => o.name == "Ground");
            float groundY = ground != null ? ground.transform.position.y : 0f;
            Vector3 centre = player != null ? player.transform.position : Vector3.zero;
            var cast = scene.GetRootGameObjects().FirstOrDefault(o => o.name == "Enemy Cast");
            if (cast != null) { var ps = cast.GetComponentsInChildren<EnemyActor>().Select(e => e.transform.position).Append(centre).ToList(); centre = ps.Aggregate(Vector3.zero, (a, b) => a + b) / ps.Count; }
            centre.y = groundY;
            Vector3 origin = new Vector3(centre.x - Size / 2f, groundY - FlatHeight, centre.z - Size / 2f);
            arena = new Vector2(Size / 2f, Size / 2f);

            // structure sites (polar around the arena) get flat pads in the terrain
            Vector3 Site(float angle, float dist) => centre + Quaternion.Euler(0, angle, 0) * Vector3.forward * dist;
            Vector3 stumpA = Site(115f, 42f), stumpB = Site(300f, 48f), houseA = Site(205f, 50f), houseB = Site(25f, 56f);
            float PadFor(Vector3 p, float radius)
            {
                var l = new Vector2(p.x - origin.x, p.z - origin.z);
                float h = Mathf.Max(BaseHeight(l.x, l.y), WaterLevel + 0.9f);
                pads.Add(new Pad { c = l, r = radius, h = h }); return h + origin.y;
            }
            stumpA.y = PadFor(stumpA, 10f); stumpB.y = PadFor(stumpB, 10f); houseA.y = PadFor(houseA, 15f); houseB.y = PadFor(houseB, 17f);
            var ruins = new[] { Site(68f, 78f), Site(252f, 84f), Site(158f, 92f) };
            for (int i = 0; i < ruins.Length; i++) ruins[i].y = PadFor(ruins[i], 11f);
            // trodden paths from the arena to every place (houses lead on to the ruins behind them)
            int seed = 1;
            foreach (var site in new[] { stumpA, stumpB, houseA, houseB, ruins[0], ruins[1], ruins[2] })
                AddPath(centre + Flat(site - centre) * 17f, site - Flat(site - centre) * 6f, seed++);

            world = BuildTerrain(root, origin);
            if (ground != null) { var gr = ground.GetComponent<Renderer>(); if (gr != null) gr.enabled = false; }   // the terrain is the floor now (the arena collider stays)
            BuildWater(root, origin);

            var templates = BuildTemplates(root, scene);
            var places = new GameObject("Places").transform; places.SetParent(root, false);
            BuildCampfire(places, centre + new Vector3(9f, 0f, -12f));
            BuildCampfire(places, houseB + Quaternion.LookRotation(Flat(centre - houseB)) * new Vector3(8.5f, 0f, 5f));
            BuildStump(places, stumpA, centre, templates, new[] { "RatRogue", "MossGourmand", "RatRogue", "BoneSentinel" }, 7);
            BuildStump(places, stumpB, centre, templates, new[] { "BoneSentinel", "RatRogue", "MossGourmand" }, 6);
            var dungeon = BuildDungeon(places, centre + new Vector3(0f, -90f, 0f), templates);
            BuildHouse(places, houseA, centre, templates, null, "House by the Clearing");
            BuildHouse(places, houseB, centre, templates, dungeon, "House of the Rune Circle");

            foreach (var pd in pads) occupied.Add((new Vector3(pd.c.x + origin.x, 0, pd.c.y + origin.z), pd.r * .66f));
            occupied.Add((centre + new Vector3(9f, 0, -12f), 2.5f));
            BuildLandmarks(root, centre, ruins);
            Scatter(root, world, origin, centre);
            BuildAtmosphere(root, centre);

            // static batching for scenery; never for things that move, fade or get cloned
            var dynamicRoots = new List<Transform>();
            dynamicRoots.AddRange(root.GetComponentsInChildren<MobLair>(true).Select(c => c.transform));
            dynamicRoots.AddRange(root.GetComponentsInChildren<HouseDungeon>(true).Select(c => c.transform));
            dynamicRoots.AddRange(root.GetComponentsInChildren<Campfire>(true).Select(c => c.transform));
            dynamicRoots.AddRange(root.GetComponentsInChildren<Chest>(true).Select(c => c.transform));
            dynamicRoots.AddRange(root.GetComponentsInChildren<TeleportPad>(true).Select(c => c.transform));
            dynamicRoots.AddRange(root.GetComponentsInChildren<ItemPickup>(true).Select(c => c.transform));
            dynamicRoots.AddRange(root.GetComponentsInChildren<ParticleSystem>(true).Select(c => c.transform));
            var templateRoot = root.Find("Mob Templates"); if (templateRoot != null) dynamicRoots.Add(templateRoot);
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t.GetComponent<ParticleSystem>() != null || dynamicRoots.Any(d => t == d || t.IsChildOf(d))) continue;
                GameObjectUtility.SetStaticEditorFlags(t.gameObject, StaticEditorFlags.BatchingStatic);
            }

            EditorSceneManager.MarkSceneDirty(scene);
            AssetDatabase.SaveAssets();
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[World] built: " + root.GetComponentsInChildren<Renderer>(true).Length + " renderers; " + root.GetComponentsInChildren<MobLair>().Length + " lairs, " +
                      root.GetComponentsInChildren<HouseDungeon>().Length + " houses, " + root.GetComponentsInChildren<Campfire>().Length + " campfires, " + root.GetComponentsInChildren<Chest>().Length + " chests, " +
                      root.GetComponentsInChildren<ItemPickup>().Length + " pickups, " + paths.Count + " paths");
        }

        private static Vector3 Flat(Vector3 v) { v.y = 0; return v.sqrMagnitude > 1e-6f ? v.normalized : Vector3.forward; }
        private static float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
        private static float GroundAt(Vector3 p) => world != null ? world.SampleHeight(p) + world.transform.position.y : p.y;

        // ───────────────────────── terrain ─────────────────────────
        private static float Noise(float x, float z, float scale, int octaves, float seed)
        {
            float v = 0, amp = 1, freq = 1, norm = 0;
            for (int i = 0; i < octaves; i++) { v += amp * Mathf.PerlinNoise(seed + x * scale * freq, seed * 1.7f + z * scale * freq); norm += amp; amp *= .5f; freq *= 2.1f; }
            return v / norm;
        }

        private static float BaseHeight(float x, float z)
        {
            float d = Vector2.Distance(new Vector2(x, z), arena);
            float hills = 1.2f + Noise(x, z, .016f, 4, 13.1f) * 6f;
            float basin = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.44f, .64f, Noise(x, z, .028f, 3, 71.3f)));
            hills = Mathf.Lerp(hills, .5f, basin);
            float edge = Mathf.Max(Mathf.Abs(x - Size / 2f), Mathf.Abs(z - Size / 2f));
            hills += Mathf.Pow(Mathf.InverseLerp(Size * .37f, Size * .5f, edge), 2f) * 14f;
            float flat = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(36f, 22f, d));
            return Mathf.Lerp(hills, FlatHeight, flat);
        }

        private static float HeightAt(float x, float z)
        {
            float h = BaseHeight(x, z);
            foreach (var p in pads)
            {
                float d = Vector2.Distance(new Vector2(x, z), p.c);
                if (d < p.r) h = Mathf.Lerp(h, p.h, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(p.r, p.r * .62f, d)));
            }
            return h;
        }

        private static Terrain BuildTerrain(Transform parent, Vector3 origin)
        {
            const int res = 257;
            var data = new TerrainData { heightmapResolution = res };
            data.size = new Vector3(Size, MaxHeight, Size);
            var h = new float[res, res];
            for (int zi = 0; zi < res; zi++) for (int xi = 0; xi < res; xi++) h[zi, xi] = HeightAt(xi * Size / (res - 1), zi * Size / (res - 1)) / MaxHeight;
            data.SetHeights(0, 0, h);
            var layers = new[]
            {
                Layer("TealGrass",  new Color(.09f, .28f, .27f), new Color(.13f, .37f, .32f), 7f),
                Layer("BlueMoss",   new Color(.09f, .19f, .31f), new Color(.13f, .27f, .40f), 5f),
                Layer("DuskSoil",   new Color(.19f, .14f, .20f), new Color(.26f, .19f, .23f), 6f),
                Layer("WetBank",    new Color(.07f, .09f, .15f), new Color(.10f, .13f, .20f), 5f),
                Layer("LeafLitter", new Color(.70f, .47f, .14f), new Color(.88f, .63f, .20f), 4f),
            };
            data.terrainLayers = layers;
            int ar = 256; data.alphamapResolution = ar;
            var alpha = new float[ar, ar, layers.Length];
            for (int zi = 0; zi < ar; zi++) for (int xi = 0; xi < ar; xi++)
            {
                float x = xi * Size / (ar - 1), z = zi * Size / (ar - 1);
                float height = HeightAt(x, z);
                float steep = data.GetSteepness((float)xi / (ar - 1), (float)zi / (ar - 1));
                float wet = Mathf.InverseLerp(WaterLevel + .4f, WaterLevel - .1f, height);
                float moss = Noise(x, z, .055f, 2, 5.5f);
                float soil = Mathf.Clamp01(Mathf.InverseLerp(18f, 34f, steep) + Mathf.InverseLerp(.66f, .78f, Noise(x, z, .05f, 2, 33f)) * .7f);
                float leaves = Mathf.InverseLerp(.60f, .72f, Noise(x, z, .035f, 3, 91f)) * (1f - Mathf.Clamp01(wet * 2f)) * Mathf.InverseLerp(14f, 26f, Vector2.Distance(new Vector2(x, z), arena));
                float trail = Mathf.InverseLerp(2.3f, 1.1f, PathDistance(origin.x + x, origin.z + z)) * (1f - Mathf.Clamp01(wet * 3f));
                float[] w = { 1f - trail, moss * 1.3f * (1f - trail), soil + trail * 5f, wet * 3f, leaves * 2.6f * (1f - trail * .7f) };
                float sum = w.Sum();
                for (int l = 0; l < w.Length; l++) alpha[zi, xi, l] = w[l] / sum;
            }
            data.SetAlphamaps(0, 0, alpha);
            string path = Folder + "/WorldTerrain.asset";
            AssetDatabase.DeleteAsset(path); AssetDatabase.DeleteAsset(Folder + "/SwampTerrain.asset"); AssetDatabase.CreateAsset(data, path);
            var go = Terrain.CreateTerrainGameObject(data);
            go.name = "Terrain"; go.transform.SetParent(parent, false); go.transform.position = origin;
            var t = go.GetComponent<Terrain>(); t.drawInstanced = true; t.heightmapPixelError = 4f; t.basemapDistance = 220f;
            return t;
        }

        private static TerrainLayer Layer(string name, Color a, Color b, float tile)
        {
            const int n = 64;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, true) { name = name, wrapMode = TextureWrapMode.Repeat };
            var px = new Color[n * n];
            for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
            {
                float u = x / (float)n * Mathf.PI * 2f, v = y / (float)n * Mathf.PI * 2f;
                float k = Mathf.PerlinNoise(3f + Mathf.Cos(u) * 1.6f, 7f + Mathf.Sin(u) * 1.6f + Mathf.Cos(v) * 1.6f) * .6f + Mathf.PerlinNoise(11f + Mathf.Sin(v) * 3f, 2f + Mathf.Cos(u) * 3f) * .4f;
                var col = Color.Lerp(a, b, k); col.a = name.Contains("Wet") ? .35f : .04f;   // alpha = smoothness for the terrain shader
                px[y * n + x] = col;
            }
            tex.SetPixels(px); tex.Apply(true);
            string texPath = Folder + "/Textures/" + name + ".asset";
            AssetDatabase.DeleteAsset(texPath); AssetDatabase.CreateAsset(tex, texPath);
            var layer = new TerrainLayer { name = name, diffuseTexture = tex, tileSize = new Vector2(tile, tile), smoothness = .05f };
            string lp = Folder + "/" + name + ".terrainlayer";
            AssetDatabase.DeleteAsset(lp); AssetDatabase.CreateAsset(layer, lp);
            return layer;
        }

        private static void BuildWater(Transform parent, Vector3 origin)
        {
            var water = GameObject.CreatePrimitive(PrimitiveType.Plane);
            water.name = "Water"; water.transform.SetParent(parent, false);
            water.transform.position = origin + new Vector3(Size / 2f, WaterLevel, Size / 2f);
            water.transform.localScale = new Vector3(Size / 10f, 1f, Size / 10f);
            Object.DestroyImmediate(water.GetComponent<Collider>());
            water.GetComponent<Renderer>().sharedMaterial = Mat("Water", new Color(.07f, .13f, .26f, .82f), .95f, transparent: true);
        }

        // ───────────────────────── materials / textures ─────────────────────────
        private static Material Mat(string name, Color color, float smooth = .06f, bool transparent = false, Color? emission = null)
        {
            if (mats.TryGetValue(name, out var m)) return m;
            string path = Folder + "/Materials/" + name + ".mat";
            m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(Shader.Find("Standard")); AssetDatabase.CreateAsset(m, path); }
            m.shader = Shader.Find("Standard");
            m.color = color; m.SetFloat("_Glossiness", smooth); m.SetFloat("_Metallic", 0f);
            if (transparent)
            {
                m.SetFloat("_Mode", 3f); m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One); m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                m.SetInt("_ZWrite", 0); m.DisableKeyword("_ALPHATEST_ON"); m.DisableKeyword("_ALPHABLEND_ON"); m.EnableKeyword("_ALPHAPREMULTIPLY_ON"); m.renderQueue = 3000;
            }
            else { m.SetFloat("_Mode", 0f); m.SetInt("_SrcBlend", 1); m.SetInt("_DstBlend", 0); m.SetInt("_ZWrite", 1); m.DisableKeyword("_ALPHAPREMULTIPLY_ON"); m.DisableKeyword("_ALPHABLEND_ON"); m.renderQueue = -1; }
            if (emission.HasValue) { m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", emission.Value); m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive; }
            else { m.DisableKeyword("_EMISSION"); m.SetColor("_EmissionColor", Color.black); }
            EditorUtility.SetDirty(m);
            mats[name] = m; return m;
        }

        private static Material Additive(string name, Texture tex, Color tint)
        {
            string path = Folder + "/Materials/" + name + ".mat";
            AssetDatabase.DeleteAsset(path);
            var m = new Material(Shader.Find("Legacy Shaders/Particles/Additive")) { name = name, mainTexture = tex };
            m.SetColor("_TintColor", tint);
            AssetDatabase.CreateAsset(m, path); return m;
        }

        private static Material firefly;
        private static Material FireflyMaterial() { if (firefly == null) firefly = Additive("Firefly", SoftDot(), new Color(.8f, .6f, .25f, .6f)); return firefly; }

        /// <summary>Soft round particle sprite (radial falloff).</summary>
        public static Texture2D SoftDot()
        {
            const string path = Folder + "/Textures/SoftDot.asset";
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (tex != null) return tex;
            const int n = 64;
            tex = new Texture2D(n, n, TextureFormat.RGBA32, true) { name = "SoftDot", wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
            {
                float d = Vector2.Distance(new Vector2(x + .5f, y + .5f), new Vector2(n / 2f, n / 2f)) / (n / 2f);
                float a = Mathf.Clamp01(1f - d); a = a * a * (3f - 2f * a);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
            tex.Apply(true); AssetDatabase.CreateAsset(tex, path); return tex;
        }

        /// <summary>Light-shaft sprite: soft across, fading toward both ends.</summary>
        private static Texture2D ShaftTexture()
        {
            const string path = Folder + "/Textures/Shaft.asset";
            AssetDatabase.DeleteAsset(path);
            const int w = 32, h = 128;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, true) { name = "Shaft", wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
            {
                float across = 1f - Mathf.Abs((x + .5f) / w * 2f - 1f); across = across * across * (3f - 2f * across);
                float v = (y + .5f) / h; float along = Mathf.SmoothStep(0f, 1f, v / .25f) * Mathf.SmoothStep(0f, 1f, (1f - v) / .2f);
                float k = across * along; tex.SetPixel(x, y, new Color(k, k, k, k));
            }
            tex.Apply(true); AssetDatabase.CreateAsset(tex, path); return tex;
        }

        // ───────────────────────── mesh builder ─────────────────────────
        private sealed class MeshB
        {
            public readonly List<Vector3> v = new List<Vector3>(); public readonly List<int> t = new List<int>(); public readonly List<Vector2> uv = new List<Vector2>();
            public void Tri(Vector3 a, Vector3 b, Vector3 c) { int i = v.Count; v.Add(a); v.Add(b); v.Add(c); uv.Add(Vector2.zero); uv.Add(Vector2.zero); uv.Add(Vector2.zero); t.Add(i); t.Add(i + 1); t.Add(i + 2); }
            /// <summary>Triangle wound so it faces away from `inside`.</summary>
            public void TriOut(Vector3 a, Vector3 b, Vector3 c, Vector3 inside) { if (Vector3.Dot(Vector3.Cross(b - a, c - a), (a + b + c) / 3f - inside) < 0) Tri(a, c, b); else Tri(a, b, c); }
            public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d) { Tri(a, b, c); Tri(a, c, d); }
            public void QuadOut(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 inside) { TriOut(a, b, c, inside); TriOut(a, c, d, inside); }
            /// <summary>Thin surface visible from both sides.</summary>
            public void Double(Vector3 a, Vector3 b, Vector3 c) { Tri(a, b, c); Tri(a, c, b); }
            public void DoubleQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d) { Double(a, b, c); Double(a, c, d); }
            public void QuadUV(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
            {
                int i = v.Count; v.AddRange(new[] { a, b, c, d }); uv.AddRange(new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) });
                t.AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3, i, i + 2, i + 1, i, i + 3, i + 2 });
            }
            public void Box(Vector3 c, Vector3 size, Quaternion? rot = null)
            {
                Quaternion q = rot ?? Quaternion.identity; Vector3 e = size * .5f;
                Vector3 P(float x, float y, float z) => c + q * new Vector3(x * e.x, y * e.y, z * e.z);
                Vector3[] p = { P(-1, -1, -1), P(1, -1, -1), P(1, 1, -1), P(-1, 1, -1), P(-1, -1, 1), P(1, -1, 1), P(1, 1, 1), P(-1, 1, 1) };
                int[][] f = { new[] { 0, 1, 2, 3 }, new[] { 5, 4, 7, 6 }, new[] { 4, 0, 3, 7 }, new[] { 1, 5, 6, 2 }, new[] { 3, 2, 6, 7 }, new[] { 4, 5, 1, 0 } };
                foreach (var q4 in f) QuadOut(p[q4[0]], p[q4[1]], p[q4[2]], p[q4[3]], c);
            }
            public void Tube(IList<Vector3> path, IList<float> radius, int sides, bool cap = true)
            {
                Vector3 Side(Vector3 dir) => Vector3.Cross(dir, Mathf.Abs(dir.y) > .9f ? Vector3.right : Vector3.up).normalized;
                for (int s = 0; s < path.Count - 1; s++)
                {
                    Vector3 dir = (path[s + 1] - path[s]).normalized, side = Side(dir), up2 = Vector3.Cross(side, dir);
                    for (int k = 0; k < sides; k++)
                    {
                        float a0 = k * Mathf.PI * 2f / sides, a1 = (k + 1) * Mathf.PI * 2f / sides;
                        Vector3 o0 = side * Mathf.Cos(a0) + up2 * Mathf.Sin(a0), o1 = side * Mathf.Cos(a1) + up2 * Mathf.Sin(a1);
                        QuadOut(path[s] + o0 * radius[s], path[s + 1] + o0 * radius[s + 1], path[s + 1] + o1 * radius[s + 1], path[s] + o1 * radius[s], (path[s] + path[s + 1]) / 2f);
                    }
                }
                if (!cap) return;
                int last = path.Count - 1; Vector3 dl = (path[last] - path[last - 1]).normalized, sl = Side(dl), ul = Vector3.Cross(sl, dl);
                for (int k = 0; k < sides; k++)
                {
                    float a0 = k * Mathf.PI * 2f / sides, a1 = (k + 1) * Mathf.PI * 2f / sides;
                    TriOut(path[last], path[last] + (sl * Mathf.Cos(a1) + ul * Mathf.Sin(a1)) * radius[last], path[last] + (sl * Mathf.Cos(a0) + ul * Mathf.Sin(a0)) * radius[last], path[last - 1]);
                }
            }
            public void Cone(Vector3 baseCentre, float radius, float height, int sides, System.Random r = null, float jitter = 0f)
            {
                Vector3 tip = baseCentre + Vector3.up * height; var ring = new Vector3[sides];
                for (int k = 0; k < sides; k++)
                {
                    float a = k * Mathf.PI * 2f / sides, rr = radius * (1f + (r != null ? ((float)r.NextDouble() - .5f) * jitter : 0f));
                    ring[k] = baseCentre + new Vector3(Mathf.Cos(a) * rr, r != null ? ((float)r.NextDouble() - .5f) * jitter * radius * .6f : 0f, Mathf.Sin(a) * rr);
                }
                Vector3 inside = baseCentre + Vector3.up * height * .3f;
                for (int k = 0; k < sides; k++) { TriOut(ring[k], ring[(k + 1) % sides], tip, inside); TriOut(baseCentre, ring[(k + 1) % sides], ring[k], inside); }
            }
            public void Blob(Vector3 centre, Vector3 scale, float jitter, System.Random r)
            {
                float g = (1f + Mathf.Sqrt(5f)) / 2f;
                var p = new[] { new Vector3(-1, g, 0), new Vector3(1, g, 0), new Vector3(-1, -g, 0), new Vector3(1, -g, 0), new Vector3(0, -1, g), new Vector3(0, 1, g),
                    new Vector3(0, -1, -g), new Vector3(0, 1, -g), new Vector3(g, 0, -1), new Vector3(g, 0, 1), new Vector3(-g, 0, -1), new Vector3(-g, 0, 1) };
                for (int i = 0; i < p.Length; i++) { p[i] = p[i].normalized * (1f + ((float)r.NextDouble() - .5f) * jitter); p[i] = Vector3.Scale(p[i], scale) + centre; }
                int[] f = { 0,11,5, 0,5,1, 0,1,7, 0,7,10, 0,10,11, 1,5,9, 5,11,4, 11,10,2, 10,7,6, 7,1,8, 3,9,4, 3,4,2, 3,2,6, 3,6,8, 3,8,9, 4,9,5, 2,4,11, 6,2,10, 8,6,7, 9,8,1 };
                for (int i = 0; i < f.Length; i += 3) TriOut(p[f[i]], p[f[i + 1]], p[f[i + 2]], centre);
            }
            public Mesh Build(string name)
            {
                var m = new Mesh { name = name };
                if (v.Count > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                m.SetVertices(v); m.SetUVs(0, uv); m.SetTriangles(t, 0); m.RecalculateNormals(); m.RecalculateBounds();
                return m;
            }
        }

        private static Mesh Save(MeshB b, string name)
        {
            var m = b.Build(name);
            AssetDatabase.CreateAsset(m, AssetDatabase.GenerateUniqueAssetPath(Folder + "/Meshes/" + name + ".asset"));
            return m;
        }

        private static GameObject Part(Transform parent, string name, Mesh mesh, Material mat)
        {
            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(parent, false);
            go.GetComponent<MeshFilter>().sharedMesh = mesh; go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            return go;
        }
        private static GameObject Part(Transform parent, string name, MeshB b, Material mat) => Part(parent, name, Save(b, name.Replace(" ", "")), mat);

        private static Light PointLight(Transform parent, Vector3 local, Color color, float range, float intensity)
        {
            var l = new GameObject("Light").AddComponent<Light>(); l.transform.SetParent(parent, false); l.transform.localPosition = local;
            l.type = LightType.Point; l.color = color; l.range = range; l.intensity = intensity; l.shadows = LightShadows.None; return l;
        }
        private static void NoShadow(GameObject go) { go.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; }

        // ───────────────────────── vegetation prototypes ─────────────────────────
        private static (Mesh trunk, Mesh needles) MakePine(int variant)
        {
            var r = new System.Random(100 + variant); float Rr(float a, float b) => a + (float)r.NextDouble() * (b - a);
            var trunk = new MeshB(); var needles = new MeshB();
            float H = Rr(15f, 23f);
            var path = new List<Vector3>(); var rad = new List<float>(); Vector3 p = Vector3.zero; Vector3 lean = new Vector3(Rr(-.06f, .06f), 1f, Rr(-.06f, .06f)).normalized;
            for (int i = 0; i <= 7; i++) { path.Add(p); rad.Add(Mathf.Lerp(.62f, .1f, i / 7f) * (i == 0 ? 1.4f : 1f)); p += lean * H / 7f; lean = (lean + new Vector3(Rr(-.03f, .03f), 0, Rr(-.03f, .03f))).normalized; }
            trunk.Tube(path, rad, 7);
            Vector3 At(float y) { float f = Mathf.Clamp01(y / H) * 7f; int i = Mathf.Min(6, (int)f); return Vector3.Lerp(path[i], path[i + 1], f - i); }
            // bare broken branches low on the trunk
            for (int b = 0; b < 6; b++)
            {
                Vector3 s = At(H * Rr(.14f, .42f)); float a = Rr(0, 6.28f); Vector3 d = new Vector3(Mathf.Cos(a), Rr(-.25f, .1f), Mathf.Sin(a)).normalized;
                Vector3 e = s + d * Rr(1.1f, 2.6f);
                trunk.Tube(new[] { s, (s + e) / 2f + Vector3.down * .15f, e + Vector3.up * Rr(0f, .3f) }, new[] { .09f, .06f, .02f }, 4);
            }
            // drooping tiers of needles
            int tiers = 7 + r.Next(3);
            for (int i = 0; i < tiers; i++)
            {
                float f = i / (float)(tiers - 1);
                float y = H * Mathf.Lerp(.40f, .93f, f), R0 = Mathf.Lerp(4.0f, 1.0f, f) * Rr(.8f, 1.15f), drop = R0 * Rr(.55f, .8f);
                Vector3 c = At(y); const int sides = 7;
                Vector3 topC = c + Vector3.up * drop * .45f, under = c - Vector3.up * drop * .2f;
                var ring = new Vector3[sides];
                for (int k = 0; k < sides; k++)
                {
                    float a = (k + (i % 2) * .5f) * Mathf.PI * 2f / sides, rr = R0 * Rr(.72f, 1.2f);
                    ring[k] = c + new Vector3(Mathf.Cos(a) * rr, -drop * Rr(.5f, 1.05f), Mathf.Sin(a) * rr);
                }
                for (int k = 0; k < sides; k++)
                {
                    needles.TriOut(topC, ring[k], ring[(k + 1) % sides], c);
                    needles.TriOut(under, ring[(k + 1) % sides], ring[k], c + Vector3.up * .1f);
                }
            }
            needles.Cone(At(H * .92f), .8f, H * .12f, 6, r, .3f);
            return (Save(trunk, "PineTrunk"), Save(needles, "PineNeedles"));
        }

        private static Mesh MakeDeadTree(int variant)
        {
            var r = new System.Random(300 + variant); float Rr(float a, float b) => a + (float)r.NextDouble() * (b - a);
            var b = new MeshB(); var path = new List<Vector3>(); var rad = new List<float>(); Vector3 p = Vector3.zero;
            float h = Rr(5f, 9f); Vector3 lean = new Vector3(Rr(-.3f, .3f), 1, Rr(-.3f, .3f)).normalized;
            for (int i = 0; i <= 5; i++) { path.Add(p); rad.Add(Mathf.Lerp(.4f, .05f, i / 5f)); lean = (lean + new Vector3(Rr(-.35f, .35f), 0, Rr(-.35f, .35f))).normalized; lean.y = Mathf.Max(lean.y, .6f); p += lean.normalized * h / 5f; }
            b.Tube(path, rad, 6);
            for (int k = 0; k < 6; k++)
            {
                Vector3 s = path[2 + r.Next(3)]; float a = Rr(0, 6.28f); Vector3 d = new Vector3(Mathf.Cos(a), Rr(.2f, .9f), Mathf.Sin(a)).normalized;
                Vector3 e = s + d * Rr(.9f, 2.2f); b.Tube(new[] { s, e, e + new Vector3(Rr(-.5f, .5f), Rr(.2f, .8f), Rr(-.5f, .5f)) }, new[] { .1f, .05f, .015f }, 4);
            }
            return Save(b, "DeadTree");
        }

        private static Mesh MakeRock(int variant, out float radius)
        {
            var r = new System.Random(500 + variant); var b = new MeshB();
            Vector3 s = new Vector3(1f + (float)r.NextDouble() * .7f, .6f + (float)r.NextDouble() * .5f, 1f + (float)r.NextDouble() * .6f);
            b.Blob(Vector3.up * s.y * .5f, s, .4f, r); radius = Mathf.Max(s.x, s.z);
            return Save(b, "Boulder");
        }

        private static Mesh MakeTuft(int variant)
        {
            var r = new System.Random(900 + variant); float Rr(float a, float b) => a + (float)r.NextDouble() * (b - a);
            var b = new MeshB();
            for (int i = 0; i < 11; i++)
            {
                Vector3 bs = new Vector3(Rr(-.45f, .45f), 0, Rr(-.45f, .45f)); float h = Rr(.45f, 1.1f);
                Vector3 tip = bs + new Vector3(Rr(-.35f, .35f), h, Rr(-.35f, .35f)); float a = Rr(0, 3.14f); Vector3 w = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * Rr(.05f, .09f);
                b.Double(bs - w, bs + w, tip);
            }
            return Save(b, "GrassTuft");
        }

        private static (Mesh stems, Mesh leaves) MakeLeafBush(int variant)
        {
            var r = new System.Random(1100 + variant); float Rr(float a, float b) => a + (float)r.NextDouble() * (b - a);
            var st = new MeshB(); var lv = new MeshB();
            for (int i = 0; i < 5; i++)
            {
                Vector3 bs = new Vector3(Rr(-.2f, .2f), 0, Rr(-.2f, .2f)); float h = Rr(.9f, 1.9f);
                Vector3 mid = bs + new Vector3(Rr(-.3f, .3f), h * .55f, Rr(-.3f, .3f)), tip = mid + new Vector3(Rr(-.4f, .4f), h * .45f, Rr(-.4f, .4f));
                st.Tube(new[] { bs, mid, tip }, new[] { .03f, .022f, .01f }, 4, false);
                for (int k = 0; k < 5; k++)
                {
                    Vector3 c = Vector3.Lerp(mid, tip, Rr(-.5f, 1f)); float a = Rr(0, 6.28f);
                    Vector3 d = new Vector3(Mathf.Cos(a), Rr(-.1f, .35f), Mathf.Sin(a)).normalized * Rr(.22f, .38f), side = Vector3.Cross(d, Vector3.up).normalized * Rr(.1f, .16f);
                    lv.DoubleQuad(c, c + d * .5f + side, c + d, c + d * .5f - side);   // diamond leaf
                }
            }
            return (Save(st, "BushStems"), Save(lv, "BushLeaves"));
        }

        private static Mesh MakeFern(int variant)
        {
            var r = new System.Random(1300 + variant); float Rr(float a, float b) => a + (float)r.NextDouble() * (b - a);
            var b = new MeshB();
            for (int i = 0; i < 6; i++)
            {
                float a = i * 1.05f + Rr(-.2f, .2f); Vector3 o = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)); float len = Rr(.7f, 1.2f);
                Vector3 prev = Vector3.zero;
                for (int s = 1; s <= 4; s++)
                {
                    float f = s / 4f; Vector3 pt = o * len * f + Vector3.up * (Mathf.Sin(f * 2.4f) * len * .55f);
                    Vector3 side = Vector3.Cross(o, Vector3.up) * (.16f * (1.15f - f));
                    b.DoubleQuad(prev - side * .9f, prev + side * .9f, pt + side * .6f, pt - side * .6f); prev = pt;
                }
            }
            return Save(b, "Fern");
        }

        private static (Mesh stalks, Mesh heads) MakeReeds(int variant)
        {
            var r = new System.Random(700 + variant); float Rr(float a, float b) => a + (float)r.NextDouble() * (b - a);
            var st = new MeshB(); var hd = new MeshB();
            for (int i = 0; i < 9; i++)
            {
                Vector3 bse = new Vector3(Rr(-.6f, .6f), 0, Rr(-.6f, .6f)); float h = Rr(1.2f, 2.3f); Vector3 tip = bse + new Vector3(Rr(-.3f, .3f), h, Rr(-.3f, .3f));
                st.Tube(new[] { bse, (bse + tip) / 2f, tip }, new[] { .03f, .025f, .01f }, 4, false);
                if (i % 2 == 0) { Vector3 c0 = Vector3.Lerp(bse, tip, .72f); hd.Tube(new[] { c0, c0 + (tip - bse).normalized * .32f }, new[] { .065f, .055f }, 6); }
            }
            return (Save(st, "Reeds"), Save(hd, "Cattails"));
        }

        private static (Mesh stem, Mesh cap) MakeMushroom()
        {
            var st = new MeshB(); var cp = new MeshB(); var r = new System.Random(77);
            st.Tube(new[] { Vector3.zero, new Vector3(.03f, .22f, 0), new Vector3(0, .42f, .02f) }, new[] { .08f, .06f, .05f }, 6);
            cp.Blob(new Vector3(0, .46f, 0), new Vector3(.27f, .15f, .27f), .2f, r);
            return (Save(st, "ShroomStem"), Save(cp, "ShroomCap"));
        }

        // ───────────────────────── scattering ─────────────────────────
        private static void Scatter(Transform parent, Terrain terrain, Vector3 origin, Vector3 centre)
        {
            var props = new GameObject("Forest").transform; props.SetParent(parent, false);
            var pines = Enumerable.Range(0, 6).Select(MakePine).ToArray();
            var dead = Enumerable.Range(0, 3).Select(MakeDeadTree).ToArray();
            var rocks = Enumerable.Range(0, 4).Select(i => (mesh: MakeRock(i, out float rr), radius: rr)).ToArray();
            var tufts = Enumerable.Range(0, 4).Select(MakeTuft).ToArray();
            var bushes = Enumerable.Range(0, 3).Select(MakeLeafBush).ToArray();
            var ferns = Enumerable.Range(0, 3).Select(MakeFern).ToArray();
            var reeds = Enumerable.Range(0, 3).Select(MakeReeds).ToArray();
            var mush = MakeMushroom();
            var pad = new MeshB(); for (int k = 1; k < 12; k++) { float a0 = k * Mathf.PI / 6f, a1 = (k + 1) * Mathf.PI / 6f; pad.Tri(Vector3.zero, new Vector3(Mathf.Cos(a1), 0, Mathf.Sin(a1)) * .55f, new Vector3(Mathf.Cos(a0), 0, Mathf.Sin(a0)) * .55f); }
            var lily = Save(pad, "LilyPad");

            Material trunk = Mat("PineTrunk", new Color(.10f, .085f, .15f)), needleA = Mat("PineNeedles", new Color(.065f, .18f, .21f)), needleB = Mat("PineNeedlesLight", new Color(.09f, .25f, .26f)),
                deadBark = Mat("DeadBark", new Color(.20f, .18f, .27f)), stone = Mat("Boulder", new Color(.20f, .22f, .34f)), stoneMoss = Mat("MossBoulder", new Color(.15f, .30f, .31f)),
                grassA = Mat("GrassTeal", new Color(.14f, .52f, .45f), emission: new Color(.02f, .10f, .08f)), grassB = Mat("GrassBlue", new Color(.13f, .34f, .58f), emission: new Color(.02f, .05f, .12f)),
                gold = Mat("GoldLeaf", new Color(1f, .72f, .18f), emission: new Color(.30f, .18f, .02f)), stems = Mat("BushStem", new Color(.22f, .16f, .2f)),
                fernRed = Mat("RedFern", new Color(.85f, .2f, .33f), emission: new Color(.22f, .03f, .06f)), fernPink = Mat("PinkFern", new Color(.8f, .35f, .7f), emission: new Color(.15f, .04f, .14f)),
                reed = Mat("Reed", new Color(.16f, .40f, .40f)), cattail = Mat("Cattail", new Color(.30f, .18f, .16f)), padM = Mat("LilyPad", new Color(.12f, .40f, .36f), .35f),
                cap = Mat("GlowCap", new Color(.3f, .95f, .9f), emission: new Color(.25f, 1.3f, 1.2f)), stemM = Mat("ShroomStem", new Color(.8f, .85f, .8f));

            var placed = occupied;
            Vector3 Pick(float minR, float maxR)
            {
                float a = R(0, Mathf.PI * 2f), d = Mathf.Sqrt(R(minR * minR, maxR * maxR));
                var p = new Vector3(centre.x + Mathf.Cos(a) * d, 0, centre.z + Mathf.Sin(a) * d);
                p.x = Mathf.Clamp(p.x, origin.x + 3f, origin.x + Size - 3f); p.z = Mathf.Clamp(p.z, origin.z + 3f, origin.z + Size - 3f);
                p.y = terrain.SampleHeight(p) + terrain.transform.position.y; return p;
            }
            bool Free(Vector3 p, float r) => placed.All(q => new Vector2(q.p.x - p.x, q.p.z - p.z).magnitude > q.r + r) && PathDistance(p.x, p.z) > r + 1.2f;
            float Grove(Vector3 p) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.36f, .6f, Noise(p.x - origin.x, p.z - origin.z, .022f, 2, 44f)));
            float water = origin.y + WaterLevel; int n;

            // tall pines: the forest
            n = 0; for (int i = 0; i < 12000 && n < 560; i++)
            {
                var p = Pick(25f, Size * .49f); if (p.y < water + .3f || rng.NextDouble() > Mathf.Lerp(.18f, 1f, Grove(p)) || !Free(p, 2.4f)) continue;
                var (tr, nd) = pines[rng.Next(pines.Length)];
                var t = new GameObject("Pine").transform; t.SetParent(props, false); t.position = p - Vector3.up * .25f; t.rotation = Quaternion.Euler(R(-2, 2), R(0, 360), R(-2, 2)); t.localScale = Vector3.one * R(.85f, 1.25f);
                Part(t, "Trunk", tr, trunk); Part(t, "Needles", nd, rng.NextDouble() < .6 ? needleA : needleB);
                var col = t.gameObject.AddComponent<CapsuleCollider>(); col.radius = .5f; col.height = 8f; col.center = Vector3.up * 4f;
                placed.Add((p, 2.4f)); n++;
            }
            // young pines fill the gaps at the forest edge
            n = 0; for (int i = 0; i < 6000 && n < 240; i++)
            {
                var p = Pick(20f, Size * .48f); if (p.y < water + .3f || rng.NextDouble() > Mathf.Lerp(.35f, 1f, Grove(p)) || !Free(p, 1.3f)) continue;
                var (tr, nd) = pines[rng.Next(pines.Length)];
                var t = new GameObject("Young Pine").transform; t.SetParent(props, false); t.position = p - Vector3.up * .15f; t.rotation = Quaternion.Euler(R(-3, 3), R(0, 360), R(-3, 3)); t.localScale = Vector3.one * R(.32f, .55f);
                Part(t, "Trunk", tr, trunk); Part(t, "Needles", nd, rng.NextDouble() < .5 ? needleA : needleB);
                var col = t.gameObject.AddComponent<CapsuleCollider>(); col.radius = .6f; col.height = 8f; col.center = Vector3.up * 4f;
                placed.Add((p, 1.3f)); n++;
            }
            n = 0; for (int i = 0; i < 900 && n < 40; i++)
            {
                var p = Pick(24f, Size * .47f); if (p.y > water + 1.6f || !Free(p, 1.5f)) continue;
                var t = Part(props, "Dead Tree", dead[rng.Next(dead.Length)], deadBark).transform; t.position = p - Vector3.up * .15f; t.rotation = Quaternion.Euler(R(-6, 6), R(0, 360), R(-6, 6)); t.localScale = Vector3.one * R(.9f, 1.4f);
                var col = t.gameObject.AddComponent<CapsuleCollider>(); col.radius = .3f; col.height = 4f; col.center = Vector3.up * 2f;
                placed.Add((p, 1.5f)); n++;
            }
            n = 0; for (int i = 0; i < 1600 && n < 120; i++)
            {
                var p = Pick(19f, Size * .48f); if (!Free(p, 1.3f)) continue;
                var (mesh, rr) = rocks[rng.Next(rocks.Length)]; float s = R(.6f, 2.4f);
                var t = Part(props, "Boulder", mesh, rng.NextDouble() < .55 ? stone : stoneMoss).transform; t.position = p - Vector3.up * .2f * s; t.rotation = Quaternion.Euler(R(-12, 12), R(0, 360), R(-12, 12)); t.localScale = Vector3.one * s;
                var mc = t.gameObject.AddComponent<MeshCollider>(); mc.sharedMesh = mesh; mc.convex = true;
                placed.Add((p, rr * s * .85f)); n++;
            }
            // undergrowth (no colliders): grass, golden bushes, ferns, glowing mushrooms
            n = 0; for (int i = 0; i < 6000 && n < 1250; i++)
            {
                var p = Pick(9f, Size * .47f); if (p.y < water + .12f) continue;
                var t = Part(props, "Grass", tufts[rng.Next(tufts.Length)], rng.NextDouble() < .68 ? grassA : grassB); t.transform.position = p - Vector3.up * .03f; t.transform.rotation = Quaternion.Euler(0, R(0, 360), 0); t.transform.localScale = Vector3.one * R(.8f, 1.7f);
                NoShadow(t); n++;
            }
            n = 0; for (int i = 0; i < 6000 && n < 560; i++)
            {
                var p = Pick(20f, Size * .46f); if (p.y < water + .2f) continue;
                if (Noise(p.x - origin.x, p.z - origin.z, .035f, 3, 91f) < .52f && rng.NextDouble() < .8) continue;   // cluster on the leaf-litter patches
                var (st, lv) = bushes[rng.Next(bushes.Length)];
                var t = new GameObject("Golden Bush").transform; t.SetParent(props, false); t.position = p; t.rotation = Quaternion.Euler(0, R(0, 360), 0); t.localScale = Vector3.one * R(.8f, 1.6f);
                NoShadow(Part(t, "Stems", st, stems)); Part(t, "Leaves", lv, gold); n++;
            }
            n = 0; for (int i = 0; i < 4000 && n < 430; i++)
            {
                var p = Pick(18f, Size * .46f); if (p.y < water + .15f) continue;
                var t = Part(props, "Fern", ferns[rng.Next(ferns.Length)], rng.NextDouble() < .7 ? fernRed : fernPink); t.transform.position = p; t.transform.rotation = Quaternion.Euler(0, R(0, 360), 0); t.transform.localScale = Vector3.one * R(.9f, 1.8f);
                NoShadow(t); n++;
            }
            n = 0; for (int i = 0; i < 2500 && n < 120; i++)
            {
                var p = Pick(17f, Size * .46f); if (p.y < water + .1f) continue;
                var t = new GameObject("Glow Mushrooms").transform; t.SetParent(props, false); t.position = p;
                int c = 2 + rng.Next(4);
                for (int k = 0; k < c; k++)
                {
                    var m = new GameObject("Mushroom").transform; m.SetParent(t, false);
                    m.localPosition = new Vector3(R(-.45f, .45f), -.02f, R(-.45f, .45f)); m.localRotation = Quaternion.Euler(R(-12, 12), R(0, 360), R(-12, 12)); m.localScale = Vector3.one * R(.6f, 1.7f);
                    Part(m, "Stem", mush.stem, stemM); NoShadow(Part(m, "Cap", mush.cap, cap));
                }
                n++;
            }
            n = 0; for (int i = 0; i < 5000 && n < 190; i++)
            {
                var p = Pick(22f, Size * .47f); if (Mathf.Abs(p.y - water) > .42f) continue;
                var (st, hd) = reeds[rng.Next(reeds.Length)];
                var t = new GameObject("Reeds").transform; t.SetParent(props, false); t.position = p - Vector3.up * .1f; t.rotation = Quaternion.Euler(0, R(0, 360), 0); t.localScale = Vector3.one * R(.8f, 1.3f);
                Part(t, "Stalks", st, reed); Part(t, "Cattails", hd, cattail); n++;
            }
            n = 0; for (int i = 0; i < 6000 && n < 120; i++)
            {
                var p = Pick(24f, Size * .47f); if (p.y > water - .25f) continue;
                var t = Part(props, "Lily Pad", lily, padM).transform; t.position = new Vector3(p.x, water + .02f, p.z); t.rotation = Quaternion.Euler(0, R(0, 360), 0); t.localScale = Vector3.one * R(.7f, 1.4f); n++;
            }
            BuildGroundCover(parent, terrain, origin, centre, tufts, new[] { grassA, grassB });
        }

        // ───────────────────────── atmosphere ─────────────────────────
        private static void BuildAtmosphere(Transform parent, Vector3 centre)
        {
            Quaternion sunRot = Quaternion.Euler(17f, 205f, 0f);
            Vector3 toSun = -(sunRot * Vector3.forward);
            var sun = Object.FindObjectsByType<Light>(FindObjectsSortMode.None).FirstOrDefault(l => l.type == LightType.Directional);
            if (sun != null) { sun.color = new Color(1f, .60f, .34f); sun.intensity = 1.25f; sun.transform.rotation = sunRot; sun.shadows = LightShadows.Soft; sun.shadowStrength = .85f; }

            var skyShader = Shader.Find("Goblingame/GradientSky");
            if (skyShader != null)
            {
                string path = Folder + "/Materials/DuskSky.mat"; AssetDatabase.DeleteAsset(path);
                var sky = new Material(skyShader) { name = "DuskSky" };
                sky.SetColor("_Top", new Color(.045f, .06f, .21f)); sky.SetColor("_Mid", new Color(.50f, .26f, .52f)); sky.SetColor("_Horizon", new Color(1f, .58f, .36f));
                sky.SetColor("_Ground", new Color(.30f, .25f, .45f)); sky.SetFloat("_MidPoint", .26f); sky.SetVector("_SunDir", toSun); sky.SetColor("_SunGlow", new Color(1f, .62f, .32f));
                AssetDatabase.CreateAsset(sky, path); RenderSettings.skybox = sky;
            }
            else Debug.LogWarning("[World] sky shader not found (Goblingame/GradientSky)");
            RenderSettings.sun = sun;
            RenderSettings.fog = true; RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = new Color(.36f, .30f, .52f); RenderSettings.fogDensity = .0135f;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(.28f, .34f, .66f); RenderSettings.ambientEquatorColor = new Color(.40f, .29f, .50f); RenderSettings.ambientGroundColor = new Color(.09f, .10f, .20f);
            var cam = Camera.main; if (cam != null) { cam.clearFlags = CameraClearFlags.Skybox; cam.farClipPlane = Mathf.Max(cam.farClipPlane, 320f); }

            // slanted light shafts between the trunks
            var shaftMat = Additive("LightShaft", ShaftTexture(), new Color(1f, .62f, .32f, .035f));
            var shafts = new GameObject("Light Shafts").transform; shafts.SetParent(parent, false);
            var qm = new MeshB(); qm.QuadUV(new Vector3(-1.6f, 0, 0), new Vector3(1.6f, 0, 0), new Vector3(1.6f, 15f, 0), new Vector3(-1.6f, 15f, 0)); var quad = Save(qm, "ShaftQuad");
            Vector3 flatSun = Flat(toSun);
            for (int i = 0; i < 22; i++)
            {
                float a = R(0, 6.28f), d = R(18f, 95f);
                Vector3 p = centre + new Vector3(Mathf.Cos(a) * d, -1f, Mathf.Sin(a) * d);
                var t = new GameObject("Shaft").transform; t.SetParent(shafts, false); t.position = p;
                // steeper than the real (very low) sun so the beams read as light falling through the canopy
                Vector3 beam = (flatSun * .55f + Vector3.up).normalized;
                t.rotation = Quaternion.LookRotation(Vector3.Cross(Vector3.up, flatSun), beam) * Quaternion.Euler(0, R(-25f, 25f), 0);
                foreach (float yaw in new[] { 0f, 70f })
                {
                    var q = Part(t, "Beam", quad, shaftMat); q.transform.localRotation = Quaternion.Euler(0, yaw, 0); q.transform.localScale = new Vector3(R(.7f, 1.6f), R(.8f, 1.3f), 1f);
                    NoShadow(q);
                }
            }

            // drifting fireflies
            var go = new GameObject("Fireflies"); go.transform.SetParent(parent, false); go.transform.position = centre + Vector3.up * 2f;
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main; main.loop = true; main.startLifetime = new ParticleSystem.MinMaxCurve(4f, 9f); main.startSpeed = .15f; main.startSize = new ParticleSystem.MinMaxCurve(.08f, .2f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, .85f, .4f, 1f), new Color(.6f, 1f, .8f, 1f)); main.maxParticles = 900; main.simulationSpace = ParticleSystemSimulationSpace.World; main.prewarm = true;
            var em = ps.emission; em.rateOverTime = 95f;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(130f, 5f, 130f);
            var noise = ps.noise; noise.enabled = true; noise.strength = .7f; noise.frequency = .35f;
            var col = ps.colorOverLifetime; col.enabled = true; var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) }, new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, .2f), new GradientAlphaKey(.25f, .5f), new GradientAlphaKey(1f, .75f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            go.GetComponent<ParticleSystemRenderer>().sharedMaterial = FireflyMaterial();
        }
    }
}
