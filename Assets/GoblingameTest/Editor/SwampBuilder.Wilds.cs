using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Goblingame.TestScene.Editor
{
    /// <summary>
    /// Fills the forest between the places: trodden paths with lantern posts, standing-stone ruins,
    /// giant glowing mushrooms, fallen logs, stumps, pebbles, a carpet of instanced grass, and items to find.
    /// </summary>
    public static partial class SwampBuilder
    {
        private static readonly List<Vector3[]> paths = new List<Vector3[]>();
        /// <summary>Footprints (world xz + radius) that trees and rocks keep away from.</summary>
        private static readonly List<(Vector3 p, float r)> occupied = new List<(Vector3, float)>();

        // ───────────────────────── paths ─────────────────────────
        private static void AddPath(Vector3 from, Vector3 to, int seed)
        {
            var pts = new List<Vector3>();
            float len = Vector3.Distance(from, to); int n = Mathf.Max(2, Mathf.CeilToInt(len / 2f));
            Vector3 side = Vector3.Cross(Vector3.up, Flat(to - from));
            for (int i = 0; i <= n; i++)
            {
                float t = i / (float)n;
                float wiggle = (Mathf.PerlinNoise(seed * 3.17f, t * 2.3f) - .5f) * 2f * 7f * Mathf.Sin(t * Mathf.PI);
                pts.Add(Vector3.Lerp(from, to, t) + side * wiggle);
            }
            paths.Add(pts.ToArray());
        }

        private static float PathDistance(float wx, float wz)
        {
            float best = float.MaxValue; var p = new Vector2(wx, wz);
            foreach (var path in paths)
                for (int i = 0; i < path.Length - 1; i++)
                {
                    Vector2 a = new Vector2(path[i].x, path[i].z), b = new Vector2(path[i + 1].x, path[i + 1].z), ab = b - a;
                    float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(1e-4f, ab.sqrMagnitude));
                    best = Mathf.Min(best, (a + ab * t - p).sqrMagnitude);
                }
            return Mathf.Sqrt(best);
        }

        private static bool Clear(Vector3 p, float r) => occupied.All(q => new Vector2(q.p.x - p.x, q.p.z - p.z).magnitude > q.r + r);

        // ───────────────────────── landmarks (before the forest is planted) ─────────────────────────
        private static void BuildLandmarks(Transform parent, Vector3 centre, Vector3[] ruins)
        {
            var root = new GameObject("Landmarks").transform; root.SetParent(parent, false);
            // lantern posts along every path
            var post = Mat("LanternPost", new Color(.16f, .12f, .15f)); var glass = Mat("WindowGlow", new Color(1f, .82f, .5f), emission: new Color(2.2f, 1.4f, .6f));
            int k = 0;
            foreach (var path in paths)
            {
                float walked = 0f, next = 9f;
                for (int i = 1; i < path.Length; i++)
                {
                    walked += Vector3.Distance(path[i - 1], path[i]);
                    if (walked < next) continue;
                    next += 15f; k++;
                    Vector3 dir = Flat(path[i] - path[i - 1]), side = Vector3.Cross(Vector3.up, dir) * (k % 2 == 0 ? 1.9f : -1.9f);
                    Vector3 at = path[i] + side; at.y = GroundAt(at);
                    var t = new GameObject("Lantern Post").transform; t.SetParent(root, false); t.position = at; t.rotation = Quaternion.LookRotation(-side);
                    var b = new MeshB();
                    b.Tube(new[] { Vector3.zero, Vector3.up * 2.3f }, new[] { .09f, .07f }, 6);
                    b.Tube(new[] { Vector3.up * 2.15f, new Vector3(0, 2.3f, .55f), new Vector3(0, 2.2f, .75f) }, new[] { .045f, .04f, .035f }, 4, false);
                    b.Box(new Vector3(0, 2.62f, 0), new Vector3(.25f, .06f, .25f));
                    Part(t, "Post", b, post);
                    var l = new MeshB(); l.Box(new Vector3(0, 1.95f, .75f), new Vector3(.2f, .28f, .2f)); l.Cone(new Vector3(0, 2.09f, .75f), .17f, .14f, 4);
                    NoShadow(Part(t, "Lantern", l, glass));
                    PointLight(t, new Vector3(0, 1.9f, .85f), new Color(1f, .7f, .38f), 7.5f, 1.25f);
                    var col = t.gameObject.AddComponent<CapsuleCollider>(); col.radius = .12f; col.height = 2.4f; col.center = Vector3.up * 1.2f;
                    occupied.Add((at, 1.2f));
                }
            }
            for (int i = 0; i < ruins.Length; i++) BuildRuin(root, ruins[i], centre, i);
            BuildGiantMushrooms(root, centre);
        }

        private static void BuildRuin(Transform parent, Vector3 at, Vector3 centre, int variant)
        {
            var ruin = new GameObject("Standing Stones").transform; ruin.SetParent(parent, false); ruin.position = at; ruin.rotation = Quaternion.LookRotation(Flat(centre - at));
            var r = new System.Random(4040 + variant); float Rr(float a, float b) => a + (float)r.NextDouble() * (b - a);
            Material stone = Mat("RuinStone", new Color(.27f, .27f, .37f)), moss = Mat("MossBoulder", new Color(.15f, .30f, .31f)),
                     rune = Mat("RuneGlow", new Color(.7f, .5f, 1f), emission: new Color(1.6f, .9f, 3f));
            var stones = new MeshB(); var mossB = new MeshB(); var runes = new MeshB();
            int count = 8;
            for (int i = 0; i < count; i++)
            {
                if (i == 2 && variant == 1) continue;   // a gap in one circle
                float a = i * Mathf.PI * 2f / count + Rr(-.1f, .1f); Vector3 o = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                float h = Rr(2.2f, 3.6f); bool broken = r.NextDouble() < .3; if (broken) h *= .45f;
                var tilt = Quaternion.Euler(Rr(-8f, 8f), -a * Mathf.Rad2Deg + 90f, Rr(-8f, 8f));
                Vector3 c = o * 5.6f + Vector3.up * (h / 2f - .25f);
                stones.Box(c, new Vector3(1.0f, h, .55f), tilt);
                stones.Box(c + tilt * Vector3.up * (h / 2f), new Vector3(1.1f, .2f, .65f), tilt);          // weathered cap
                mossB.Blob(c + tilt * new Vector3(0, h / 2f + .1f, 0), new Vector3(.55f, .14f, .35f), .5f, r);
                // carved rune facing the middle
                Vector3 face = c - o * .29f + Vector3.up * Rr(-.2f, .3f), side = Vector3.Cross(Vector3.up, o) * .14f;
                runes.DoubleQuad(face - side + Vector3.up * .25f, face + side + Vector3.up * .25f, face + side * .3f - Vector3.up * .25f, face - side * .3f - Vector3.up * .25f);
            }
            // a lintel across two stones (the gate) facing the arena
            stones.Box(new Vector3(0, 3.3f, 5.6f), new Vector3(3.4f, .5f, .7f), Quaternion.Euler(0, 0, Rr(-4f, 4f)));
            foreach (float x in new[] { -1.3f, 1.3f }) stones.Box(new Vector3(x, 1.5f, 5.6f), new Vector3(.75f, 3.1f, .6f));
            // fallen blocks, an altar in the middle
            for (int i = 0; i < 4; i++) stones.Box(new Vector3(Rr(-4f, 4f), .25f, Rr(-4f, 2f)), new Vector3(Rr(.7f, 1.4f), .5f, Rr(.5f, .9f)), Quaternion.Euler(Rr(-10f, 10f), Rr(0, 180f), Rr(-10f, 10f)));
            stones.Box(new Vector3(0, .45f, 0), new Vector3(1.6f, .9f, 1.0f)); stones.Box(new Vector3(0, .95f, 0), new Vector3(1.9f, .14f, 1.2f));
            var sgo = Part(ruin, "Stones", stones, stone); Part(ruin, "Moss", mossB, moss); NoShadow(Part(ruin, "Runes", runes, rune));
            var mc = sgo.AddComponent<MeshCollider>(); mc.sharedMesh = sgo.GetComponent<MeshFilter>().sharedMesh;
            PointLight(ruin, new Vector3(0, 2.2f, 0), new Color(.6f, .45f, 1f), 9f, 1.4f);
            // something on the altar
            string[] gifts = { "coin:8", "bone:3", "stew" };
            Pickup(ruin, new Vector3(0, 1.05f, 0), gifts[variant % gifts.Length]);
            occupied.Add((at, 7.5f));
        }

        private static void BuildGiantMushrooms(Transform parent, Vector3 centre)
        {
            var root = new GameObject("Giant Mushrooms").transform; root.SetParent(parent, false);
            Material stem = Mat("GiantStem", new Color(.78f, .78f, .74f)), gill = Mat("GiantGill", new Color(.42f, .30f, .45f));
            Material[] caps =
            {
                Mat("GiantCapTeal", new Color(.22f, .75f, .72f), emission: new Color(.08f, .55f, .5f)),
                Mat("GiantCapPink", new Color(.85f, .35f, .62f), emission: new Color(.45f, .08f, .3f)),
                Mat("GiantCapViolet", new Color(.5f, .35f, .9f), emission: new Color(.22f, .12f, .6f)),
            };
            var spots = Mat("CapSpots", new Color(.95f, .92f, .8f), emission: new Color(.35f, .32f, .25f));
            var protos = new List<(Mesh stem, Mesh cap, Mesh gill, Mesh spots, float h)>();
            for (int v = 0; v < 4; v++)
            {
                var r = new System.Random(6100 + v); float Rr(float a, float b) => a + (float)r.NextDouble() * (b - a);
                float h = Rr(2.6f, 4.2f); var s = new MeshB(); var c = new MeshB(); var g = new MeshB(); var sp = new MeshB();
                Vector3 top = new Vector3(Rr(-.5f, .5f), h, Rr(-.5f, .5f));
                s.Tube(new[] { Vector3.zero, new Vector3(top.x * .3f, h * .35f, top.z * .3f), new Vector3(top.x * .7f, h * .7f, top.z * .7f), top }, new[] { .55f, .34f, .3f, .32f }, 8);
                float R = Rr(1.6f, 2.4f);
                c.Blob(top + Vector3.up * .35f, new Vector3(R, R * .42f, R), .12f, r);
                g.Tube(new[] { top + Vector3.up * .05f, top + Vector3.up * .06f }, new[] { R * .92f, R * .92f }, 10);
                for (int i = 0; i < 9; i++) { float a = Rr(0, 6.28f), d = Rr(.2f, .85f) * R; sp.Blob(top + new Vector3(Mathf.Cos(a) * d, .35f + R * .42f * Mathf.Sqrt(Mathf.Max(0f, 1f - d * d / (R * R))) * .95f, Mathf.Sin(a) * d), new Vector3(.18f, .06f, .18f), .3f, r); }
                protos.Add((Save(s, "GiantStem"), Save(c, "GiantCap"), Save(g, "GiantGill"), Save(sp, "CapSpots"), h));
            }
            int n = 0;
            for (int i = 0; i < 3000 && n < 34; i++)
            {
                float a = R(0, Mathf.PI * 2f), d = R(28f, Size * .44f);
                Vector3 p = centre + new Vector3(Mathf.Cos(a) * d, 0, Mathf.Sin(a) * d); p.y = GroundAt(p);
                if (p.y < centre.y - FlatHeight + WaterLevel + .4f || !Clear(p, 3f) || PathDistance(p.x, p.z) < 3.5f) continue;
                // they grow in little families
                int family = 1 + rng.Next(3);
                for (int f = 0; f < family && n < 34; f++)
                {
                    Vector3 q = p + (f == 0 ? Vector3.zero : new Vector3(R(-3.5f, 3.5f), 0, R(-3.5f, 3.5f))); q.y = GroundAt(q);
                    if (f > 0 && !Clear(q, 1.6f)) continue;
                    var pr = protos[rng.Next(protos.Count)]; float s = f == 0 ? R(1.1f, 1.6f) : R(.5f, .85f);
                    var t = new GameObject("Giant Mushroom").transform; t.SetParent(root, false); t.position = q - Vector3.up * .1f; t.rotation = Quaternion.Euler(R(-5, 5), R(0, 360), R(-5, 5)); t.localScale = Vector3.one * s;
                    Part(t, "Stem", pr.stem, stem); Part(t, "Cap", pr.cap, caps[rng.Next(caps.Length)]); NoShadow(Part(t, "Gills", pr.gill, gill)); NoShadow(Part(t, "Spots", pr.spots, spots));
                    var col = t.gameObject.AddComponent<CapsuleCollider>(); col.radius = .45f; col.height = pr.h; col.center = Vector3.up * pr.h / 2f;
                    if (f == 0 && n % 3 == 0) PointLight(t, Vector3.up * (pr.h * .8f), new Color(.45f, .9f, .9f), 8f, 1.1f);
                    occupied.Add((q, 1.8f * s)); n++;
                }
            }
        }

        // ───────────────────────── ground cover (after the trees) ─────────────────────────
        private static void BuildGroundCover(Transform parent, Terrain terrain, Vector3 origin, Vector3 centre, Mesh[] tufts, Material[] grassMats)
        {
            var root = new GameObject("Ground Cover").transform; root.SetParent(parent, false);
            float water = origin.y + WaterLevel;
            Vector3 Spot(float minR, float maxR)
            {
                float a = R(0, Mathf.PI * 2f), d = Mathf.Sqrt(R(minR * minR, maxR * maxR));
                var p = new Vector3(centre.x + Mathf.Cos(a) * d, 0, centre.z + Mathf.Sin(a) * d);
                p.x = Mathf.Clamp(p.x, origin.x + 3f, origin.x + Size - 3f); p.z = Mathf.Clamp(p.z, origin.z + 3f, origin.z + Size - 3f);
                p.y = GroundAt(p); return p;
            }
            Material bark = Mat("LogBark", new Color(.19f, .14f, .17f)), wood = Mat("OakWood", new Color(.44f, .31f, .2f)), moss = Mat("OakMoss", new Color(.12f, .34f, .30f)),
                     pebble = Mat("Pebble", new Color(.24f, .25f, .36f)), shelf = Mat("ShelfFungus", new Color(.95f, .62f, .25f), emission: new Color(.25f, .12f, .02f));

            // fallen logs: long, mossy, with shelf fungus
            var logs = new List<(Mesh bark, Mesh ends, Mesh moss, Mesh fungus, float len, float rad)>();
            for (int v = 0; v < 4; v++)
            {
                var r = new System.Random(7100 + v); float Rr(float a, float b) => a + (float)r.NextDouble() * (b - a);
                float len = Rr(4.5f, 8f), rad = Rr(.32f, .55f);
                var b = new MeshB(); var e = new MeshB(); var m = new MeshB(); var f = new MeshB();
                var path = new List<Vector3>(); var radii = new List<float>();
                for (int i = 0; i <= 4; i++) { path.Add(new Vector3(-len / 2f + len * i / 4f, rad * .8f + Rr(-.05f, .05f), Rr(-.12f, .12f))); radii.Add(rad * Rr(.85f, 1.1f)); }
                b.Tube(path, radii, 8, false);
                foreach (int end in new[] { 0, 4 }) { var c = path[end]; for (int kk = 0; kk < 8; kk++) { float a0 = kk * Mathf.PI / 4f, a1 = (kk + 1) * Mathf.PI / 4f; e.Double(c, c + new Vector3(0, Mathf.Cos(a0), Mathf.Sin(a0)) * radii[end], c + new Vector3(0, Mathf.Cos(a1), Mathf.Sin(a1)) * radii[end]); } }
                for (int i = 0; i < 4; i++) m.Blob(path[1 + r.Next(3)] + new Vector3(Rr(-.5f, .5f), rad * .75f, 0), new Vector3(Rr(.4f, .8f), .12f, rad * .8f), .4f, r);
                for (int i = 0; i < 5; i++) { var p = path[1 + r.Next(3)]; float side = r.NextDouble() < .5 ? -1f : 1f; f.Blob(p + new Vector3(Rr(-.4f, .4f), Rr(-.1f, .25f), side * rad * 1.02f), new Vector3(.2f, .05f, .14f), .2f, r); }
                // a broken-off branch stub
                b.Tube(new[] { path[2] + Vector3.up * rad * .6f, path[2] + new Vector3(.3f, rad + .7f, .2f) }, new[] { .12f, .05f }, 5);
                logs.Add((Save(b, "LogBark"), Save(e, "LogEnds"), Save(m, "LogMoss"), Save(f, "LogFungus"), len, rad));
            }
            int n = 0;
            for (int i = 0; i < 4000 && n < 55; i++)
            {
                var p = Spot(16f, Size * .46f); if (p.y < water + .2f || !Clear(p, 2.8f) || PathDistance(p.x, p.z) < 2.6f) continue;
                var lg = logs[rng.Next(logs.Count)];
                var t = new GameObject("Fallen Log").transform; t.SetParent(root, false); t.position = p - Vector3.up * .12f; t.rotation = Quaternion.Euler(R(-3, 3), R(0, 360), R(-3, 3)); t.localScale = Vector3.one * R(.85f, 1.15f);
                Part(t, "Bark", lg.bark, bark); Part(t, "Ends", lg.ends, wood); Part(t, "Moss", lg.moss, moss); NoShadow(Part(t, "Fungus", lg.fungus, shelf));
                var col = t.gameObject.AddComponent<CapsuleCollider>(); col.direction = 0; col.radius = lg.rad; col.height = lg.len; col.center = Vector3.up * lg.rad * .8f;
                occupied.Add((p, lg.len * .45f)); n++;
            }
            // stumps
            var stumpB = new MeshB(); var stumpTop = new MeshB(); var rs = new System.Random(7300);
            stumpB.Tube(new[] { Vector3.zero, Vector3.up * .3f, Vector3.up * .75f }, new[] { .62f, .45f, .42f }, 8, false);
            for (int kk = 0; kk < 5; kk++) { float a = kk * 1.25f; Vector3 o = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)); stumpB.Tube(new[] { o * .35f + Vector3.up * .3f, o * .85f, o * 1.1f - Vector3.up * .15f }, new[] { .16f, .1f, .04f }, 5); }
            for (int kk = 0; kk < 8; kk++) { float a0 = kk * Mathf.PI / 4f, a1 = (kk + 1) * Mathf.PI / 4f; stumpTop.Tri(Vector3.up * .76f, new Vector3(Mathf.Cos(a1) * .42f, .75f, Mathf.Sin(a1) * .42f), new Vector3(Mathf.Cos(a0) * .42f, .75f, Mathf.Sin(a0) * .42f)); }
            Mesh stumpMesh = Save(stumpB, "Stump"), stumpTopMesh = Save(stumpTop, "StumpTop");
            n = 0;
            for (int i = 0; i < 3000 && n < 60; i++)
            {
                var p = Spot(15f, Size * .46f); if (p.y < water + .2f || !Clear(p, 1.1f) || PathDistance(p.x, p.z) < 2f) continue;
                var t = new GameObject("Stump").transform; t.SetParent(root, false); t.position = p - Vector3.up * .05f; t.rotation = Quaternion.Euler(0, R(0, 360), 0); t.localScale = Vector3.one * R(.7f, 1.5f);
                Part(t, "Bark", stumpMesh, bark); Part(t, "Top", stumpTopMesh, wood);
                var col = t.gameObject.AddComponent<CapsuleCollider>(); col.radius = .45f; col.height = .8f; col.center = Vector3.up * .4f;
                occupied.Add((p, .9f)); n++;
            }
            // pebbles and small stones (no colliders)
            var pebbles = Enumerable.Range(0, 3).Select(v => { var b = new MeshB(); var r = new System.Random(7500 + v); for (int kk = 0; kk < 4; kk++) b.Blob(new Vector3((float)r.NextDouble() - .5f, .05f, (float)r.NextDouble() - .5f) * .9f, new Vector3(.18f, .1f, .14f) * (.6f + (float)r.NextDouble()), .5f, r); return Save(b, "Pebbles"); }).ToArray();
            n = 0;
            for (int i = 0; i < 3000 && n < 260; i++)
            {
                var p = Spot(12f, Size * .47f); if (!Clear(p, .3f)) continue;
                var go = Part(root, "Pebbles", pebbles[rng.Next(pebbles.Length)], pebble); go.transform.position = p; go.transform.rotation = Quaternion.Euler(0, R(0, 360), 0); go.transform.localScale = Vector3.one * R(.7f, 1.8f);
                NoShadow(go); n++;
            }
            // items to find: glow mushrooms to eat, a club by the arena fire
            n = 0;
            for (int i = 0; i < 3000 && n < 30; i++)
            {
                var p = Spot(18f, Size * .43f); if (p.y < water + .2f || !Clear(p, .8f)) continue;
                Pickup(root, root.InverseTransformPoint(p), n % 7 == 6 ? "coin:4" : "mushroom"); n++;
            }
            Pickup(root, root.InverseTransformPoint(centre + new Vector3(6.6f, 0, -14.2f)), "club");

            PlantDetailGrass(terrain, origin, centre, tufts, grassMats);
        }

        private static void Pickup(Transform parent, Vector3 local, string entry)
        {
            var (def, count) = Items.Parse(entry); if (def == null) return;
            var go = new GameObject("Pickup " + def.id); go.transform.SetParent(parent, false); go.transform.localPosition = local;
            var p = go.AddComponent<ItemPickup>(); p.itemId = def.id; p.count = count;
        }

        // ───────────────────────── instanced grass carpet ─────────────────────────
        private static void PlantDetailGrass(Terrain terrain, Vector3 origin, Vector3 centre, Mesh[] tufts, Material[] mats)
        {
            string dir = Folder + "/Prefabs"; if (!AssetDatabase.IsValidFolder(dir)) AssetDatabase.CreateFolder(Folder, "Prefabs");
            var protos = new List<DetailPrototype>();
            for (int i = 0; i < 2; i++)
            {
                var go = new GameObject("DetailGrass" + i, typeof(MeshFilter), typeof(MeshRenderer));
                go.GetComponent<MeshFilter>().sharedMesh = tufts[i % tufts.Length];
                var mat = mats[i % mats.Length]; mat.enableInstancing = true; EditorUtility.SetDirty(mat);
                go.GetComponent<MeshRenderer>().sharedMaterial = mat;
                string p = dir + "/DetailGrass" + i + ".prefab"; AssetDatabase.DeleteAsset(p);
                var prefab = PrefabUtility.SaveAsPrefabAsset(go, p); Object.DestroyImmediate(go);
                protos.Add(new DetailPrototype
                {
                    usePrototypeMesh = true, prototype = prefab, renderMode = DetailRenderMode.VertexLit, useInstancing = true,
                    minWidth = .45f, maxWidth = .9f, minHeight = .45f, maxHeight = 1.0f, noiseSpread = .35f,
                    healthyColor = Color.white, dryColor = new Color(.85f, .9f, 1f)
                });
            }
            var data = terrain.terrainData;
            const int res = 256;
            data.SetDetailScatterMode(DetailScatterMode.InstanceCountMode);
            data.SetDetailResolution(res, 32);
            data.detailPrototypes = protos.ToArray();
            var layers = new[] { new int[res, res], new int[res, res] };
            float water = origin.y + WaterLevel;
            for (int z = 0; z < res; z++) for (int x = 0; x < res; x++)
            {
                float wx = origin.x + (x + .5f) * Size / res, wz = origin.z + (z + .5f) * Size / res;
                Vector3 p = new Vector3(wx, 0, wz); float h = terrain.SampleHeight(p) + origin.y;
                if (h < water + .15f) continue;
                float fromArena = new Vector2(wx - centre.x, wz - centre.z).magnitude; if (fromArena < 10f) continue;
                bool onPad = pads.Any(pd => new Vector2(pd.c.x + origin.x - wx, pd.c.y + origin.z - wz).magnitude < pd.r * .45f); if (onPad) continue;
                float lush = Noise(wx - origin.x, wz - origin.z, .04f, 3, 17f);
                float onPath = Mathf.InverseLerp(2.4f, 1.2f, PathDistance(wx, wz));
                float amount = Mathf.Clamp01(Mathf.InverseLerp(.32f, .62f, lush)) * (1f - onPath) * Mathf.InverseLerp(10f, 16f, fromArena);
                int count = Mathf.RoundToInt(amount * 3.4f + (float)rng.NextDouble() * .6f);
                if (count <= 0) continue;
                int a = Mathf.CeilToInt(count * .65f); layers[0][z, x] = a; layers[1][z, x] = count - a;
            }
            data.SetDetailLayer(0, 0, 0, layers[0]); data.SetDetailLayer(0, 0, 1, layers[1]);
            terrain.detailObjectDistance = 85f; terrain.detailObjectDensity = 1f;
            EditorUtility.SetDirty(data);
        }
    }
}
