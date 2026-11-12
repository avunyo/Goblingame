using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Goblingame.TestScene.Editor
{
    /// <summary>Gameplay places of the world: campfires, stump lairs, houses, chests, rune circles, the dungeon chamber.</summary>
    public static partial class SwampBuilder
    {
        // ───────────────────────── mob templates ─────────────────────────
        private static Dictionary<string, GameObject> BuildTemplates(Transform root, Scene scene)
        {
            // disabled copies of the cast: lairs clone them at runtime
            var holder = new GameObject("Mob Templates").transform; holder.SetParent(root, false);
            var result = new Dictionary<string, GameObject>();
            var cast = scene.GetRootGameObjects().FirstOrDefault(o => o.name == "Enemy Cast");
            if (cast == null) return result;
            foreach (var actor in cast.GetComponentsInChildren<EnemyActor>())
            {
                if (string.IsNullOrEmpty(actor.id) || result.ContainsKey(actor.id)) continue;
                var copy = Object.Instantiate(actor.gameObject, holder);
                copy.name = actor.id; copy.SetActive(false);
                result[actor.id] = copy;
            }
            return result;
        }

        /// <summary>After the enemies are rebuilt (upgrade), lairs must clone the new versions.</summary>
        public static void RefreshTemplates(Scene scene)
        {
            var root = scene.GetRootGameObjects().FirstOrDefault(o => o.name == RootName);
            if (root == null) return;
            var holder = root.transform.Find("Mob Templates");
            var lairs = root.GetComponentsInChildren<MobLair>(true);
            var wanted = lairs.ToDictionary(l => l, l => (l.templates ?? new GameObject[0]).Select(t => t != null ? t.name : null).ToArray());
            if (holder != null) Object.DestroyImmediate(holder.gameObject);
            var fresh = BuildTemplates(root.transform, scene);
            foreach (var l in lairs) { l.templates = wanted[l].Select(n => n != null && fresh.TryGetValue(n, out var g) ? g : null).ToArray(); EditorUtility.SetDirty(l); }
            Debug.Log("[World] lair templates refreshed (" + fresh.Count + " kinds, " + lairs.Length + " lairs)");
        }

        private static GameObject[] Pick(Dictionary<string, GameObject> templates, params string[] ids) =>
            ids.Select(n => templates.TryGetValue(n, out var g) ? g : null).ToArray();

        // ───────────────────────── campfire ─────────────────────────
        private static void BuildCampfire(Transform parent, Vector3 at)
        {
            at.y = GroundAt(at);
            var fire = new GameObject("Campfire").transform; fire.SetParent(parent, false); fire.position = at;
            var r = new System.Random(55 + (int)at.x);
            var stones = new MeshB();
            for (int i = 0; i < 9; i++) { float a = i * Mathf.PI * 2f / 9f; stones.Blob(new Vector3(Mathf.Cos(a) * .95f, .12f, Mathf.Sin(a) * .95f), new Vector3(.3f, .22f, .26f), .4f, r); }
            Part(fire, "Stones", stones, Mat("FireStone", new Color(.34f, .25f, .27f)));
            var logs = new MeshB();
            for (int i = 0; i < 4; i++) { float a = i * 1.57f + .4f; Vector3 o = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)); logs.Tube(new[] { o * .65f + Vector3.up * .08f, -o * .15f + Vector3.up * .32f }, new[] { .1f, .09f }, 6); }
            Part(fire, "Logs", logs, Mat("FireLog", new Color(.30f, .17f, .10f), emission: new Color(.35f, .1f, .02f)));
            // flames: spiky cones, flickered at runtime
            var flames = new GameObject("Flames").transform; flames.SetParent(fire, false);
            Material[] fm = { Mat("FlameCore", new Color(1f, .96f, .75f), emission: new Color(3f, 2.6f, 1.6f)), Mat("FlameMid", new Color(1f, .62f, .2f), emission: new Color(2.6f, 1.2f, .25f)), Mat("FlameEdge", new Color(1f, .36f, .14f), emission: new Color(2.2f, .55f, .12f)) };
            for (int i = 0; i < 9; i++)
            {
                var b = new MeshB(); b.Cone(Vector3.zero, .16f + (i % 3) * .05f, .7f + (i % 4) * .22f, 4);
                var f = Part(flames, "Flame", b, fm[i < 3 ? 0 : i < 6 ? 1 : 2]);
                float a = i * 2.4f, d = i < 3 ? .1f : i < 6 ? .28f : .42f;
                f.transform.localPosition = new Vector3(Mathf.Cos(a) * d, .22f, Mathf.Sin(a) * d); NoShadow(f);
            }
            // tripod with a cauldron
            var poles = new MeshB(); Vector3 apex = Vector3.up * 2.25f;
            for (int i = 0; i < 3; i++) { float a = i * 2.094f + .5f; poles.Tube(new[] { new Vector3(Mathf.Cos(a) * 1.25f, 0, Mathf.Sin(a) * 1.25f), apex + new Vector3(-Mathf.Cos(a) * .12f, .15f, -Mathf.Sin(a) * .12f) }, new[] { .05f, .04f }, 5); }
            poles.Tube(new[] { apex, apex + Vector3.down * .75f }, new[] { .015f, .015f }, 4, false);
            Part(fire, "Tripod", poles, Mat("FirePole", new Color(.42f, .26f, .2f)));
            var pot = new MeshB(); pot.Blob(new Vector3(0, 1.2f, 0), new Vector3(.42f, .3f, .42f), .08f, r);
            pot.Tube(new[] { new Vector3(0, 1.36f, 0), new Vector3(0, 1.45f, 0) }, new[] { .44f, .47f }, 8);
            Part(fire, "Cauldron", pot, Mat("Cauldron", new Color(.34f, .10f, .09f), .3f));
            var glow = PointLight(fire, new Vector3(0, .9f, 0), new Color(1f, .58f, .25f), 11f, 2.6f);
            var comp = fire.gameObject.AddComponent<Campfire>(); comp.flames = flames; comp.glow = glow;
            // embers
            var go = new GameObject("Embers"); go.transform.SetParent(fire, false); go.transform.localPosition = Vector3.up * .5f; go.transform.localRotation = Quaternion.Euler(-90, 0, 0);
            var ps = go.AddComponent<ParticleSystem>(); var main = ps.main; main.startLifetime = new ParticleSystem.MinMaxCurve(1.2f, 2.6f); main.startSpeed = new ParticleSystem.MinMaxCurve(.5f, 1.3f); main.startSize = new ParticleSystem.MinMaxCurve(.06f, .14f);
            main.startColor = new Color(1f, .7f, .3f); main.simulationSpace = ParticleSystemSimulationSpace.World; main.maxParticles = 80;
            var em = ps.emission; em.rateOverTime = 14f; var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 18f; sh.radius = .25f;
            var noise = ps.noise; noise.enabled = true; noise.strength = .5f;
            go.GetComponent<ParticleSystemRenderer>().sharedMaterial = FireflyMaterial();
            var col = fire.gameObject.AddComponent<CapsuleCollider>(); col.radius = .75f; col.height = 1.2f; col.center = Vector3.up * .5f;
        }

        // ───────────────────────── oak stump lair ─────────────────────────
        private static void BuildStump(Transform parent, Vector3 at, Vector3 centre, Dictionary<string, GameObject> templates, string[] mobs, int total)
        {
            var stump = new GameObject("Oak Stump Lair").transform; stump.SetParent(parent, false); stump.position = at; stump.rotation = Quaternion.LookRotation(Flat(centre - at));
            var r = new System.Random(808 + (int)at.x);
            float Rr(float a, float b) => a + (float)r.NextDouble() * (b - a);
            var bark = new MeshB();
            bark.Tube(new[] { Vector3.zero, Vector3.up * .8f, Vector3.up * 2.2f, Vector3.up * 3.4f }, new[] { 3.4f, 2.75f, 2.55f, 2.65f }, 11, false);
            for (int k = 0; k < 8; k++)
            {
                float a = k * Mathf.PI / 4f + Rr(-.15f, .15f); if (Mathf.Abs(Mathf.DeltaAngle(a * Mathf.Rad2Deg, 90f)) < 28f) continue;   // keep the doorway clear (+Z)
                Vector3 o = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                bark.Tube(new[] { o * 2.4f + Vector3.up * 1.0f, o * 3.9f + Vector3.up * .35f, o * 5.4f - Vector3.up * .25f }, new[] { .7f, .45f, .14f }, 6);
            }
            // jagged broken rim
            for (int k = 0; k < 11; k++)
            {
                float a0 = k * Mathf.PI * 2f / 11f, a1 = (k + 1) * Mathf.PI * 2f / 11f, am = (a0 + a1) / 2f;
                Vector3 p0 = new Vector3(Mathf.Cos(a0), 0, Mathf.Sin(a0)) * 2.65f + Vector3.up * 3.4f, p1 = new Vector3(Mathf.Cos(a1), 0, Mathf.Sin(a1)) * 2.65f + Vector3.up * 3.4f;
                Vector3 tip = new Vector3(Mathf.Cos(am), 0, Mathf.Sin(am)) * 2.5f + Vector3.up * (3.4f + Rr(.3f, 1.5f));
                Vector3 inner = new Vector3(Mathf.Cos(am), 0, Mathf.Sin(am)) * 2.2f + Vector3.up * 3.3f;
                bark.Double(p0, p1, tip); bark.Double(p0, tip, inner); bark.Double(p1, inner, tip);
            }
            // doorway rim (bark arch)
            var arch = new List<Vector3>();
            for (int k = 0; k <= 8; k++) { float a = Mathf.PI * k / 8f; arch.Add(new Vector3(Mathf.Cos(a) * .98f, Mathf.Sin(a) * 2.45f + .05f, 2.72f - Mathf.Abs(Mathf.Cos(a)) * .16f)); }
            bark.Tube(arch, Enumerable.Repeat(.24f, arch.Count).ToArray(), 6);
            Part(stump, "Bark", bark, Mat("OakBark", new Color(.17f, .13f, .16f)));
            var top = new MeshB(); for (int k = 0; k < 11; k++) { float a0 = k * Mathf.PI * 2f / 11f, a1 = (k + 1) * Mathf.PI * 2f / 11f; top.Tri(Vector3.up * 3.25f, new Vector3(Mathf.Cos(a1), 0, Mathf.Sin(a1)) * 2.4f + Vector3.up * 3.3f, new Vector3(Mathf.Cos(a0), 0, Mathf.Sin(a0)) * 2.4f + Vector3.up * 3.3f); }
            Part(stump, "Heartwood", top, Mat("OakWood", new Color(.44f, .31f, .2f)));
            // the dark hollow
            var hollow = new MeshB();
            for (int k = 0; k < 8; k++) { float a0 = Mathf.PI * k / 8f, a1 = Mathf.PI * (k + 1) / 8f; hollow.Double(new Vector3(0, .05f, 2.74f), new Vector3(Mathf.Cos(a0) * .92f, Mathf.Sin(a0) * 2.35f + .05f, 2.74f), new Vector3(Mathf.Cos(a1) * .92f, Mathf.Sin(a1) * 2.35f + .05f, 2.74f)); }
            NoShadow(Part(stump, "Hollow", hollow, Mat("Void", new Color(.01f, .01f, .015f), 0f)));
            // eyes glowing in the dark
            var eyeMat = Mat("LairEye", new Color(1f, .5f, .15f), emission: new Color(3f, 1f, .2f));
            var eyes = new List<Renderer>();
            foreach (float x in new[] { -.3f, .3f })
            { var e = new MeshB(); e.Blob(Vector3.zero, new Vector3(.1f, .07f, .05f), .1f, r); var go = Part(stump, "Eye", e, eyeMat); go.transform.localPosition = new Vector3(x, 1.45f, 2.78f); NoShadow(go); eyes.Add(go.GetComponent<Renderer>()); }
            // moss and glowing mushrooms
            var moss = new MeshB(); for (int k = 0; k < 7; k++) { float a = Rr(0, 6.28f); moss.Blob(new Vector3(Mathf.Cos(a) * Rr(1.2f, 2.4f), 3.35f, Mathf.Sin(a) * Rr(1.2f, 2.4f)), new Vector3(Rr(.5f, 1f), .22f, Rr(.5f, .9f)), .4f, r); }
            Part(stump, "Moss", moss, Mat("OakMoss", new Color(.12f, .34f, .30f)));
            var caps = new MeshB(); var stems = new MeshB();
            for (int k = 0; k < 9; k++)
            {
                float a = Rr(-.6f, 3.8f); Vector3 o = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)); Vector3 bs = o * 2.7f + Vector3.up * Rr(.5f, 2.6f);
                stems.Tube(new[] { bs, bs + o * .25f + Vector3.up * .12f }, new[] { .05f, .04f }, 5);
                caps.Blob(bs + o * .32f + Vector3.up * .16f, new Vector3(.22f, .1f, .22f), .25f, r);
            }
            Part(stump, "Shelf Stems", stems, Mat("ShroomStem", new Color(.8f, .85f, .8f)));
            Part(stump, "Shelf Mushrooms", caps, Mat("GlowCap", new Color(.3f, .95f, .9f), emission: new Color(.25f, 1.3f, 1.2f)));
            PointLight(stump, new Vector3(0, 1.4f, 3.4f), new Color(1f, .45f, .15f), 6f, 1.1f);
            var col = stump.gameObject.AddComponent<CapsuleCollider>(); col.radius = 2.7f; col.height = 4.2f; col.center = Vector3.up * 1.8f;
            var mouth = new GameObject("Mouth").transform; mouth.SetParent(stump, false); mouth.localPosition = new Vector3(0, .1f, 3.25f);
            var lair = stump.gameObject.AddComponent<MobLair>(); lair.mouth = mouth; lair.eyes = eyes.ToArray(); lair.totalMobs = total; lair.maxAlive = 3; lair.lairName = "Дубовый пень";
            lair.templates = Pick(templates, mobs);
        }

        // ───────────────────────── chest / rune circle ─────────────────────────
        private static Chest BuildChest(Transform parent, Vector3 local, float yaw, int xp, float heal, bool big = false)
        {
            float s = big ? 1.45f : 1f;
            var chest = new GameObject(big ? "Great Chest" : "Chest").transform; chest.SetParent(parent, false); chest.localPosition = local; chest.localRotation = Quaternion.Euler(0, yaw, 0); chest.localScale = Vector3.one * s;
            var wood = Mat("ChestWood", new Color(.46f, .28f, .13f)); var metal = Mat("ChestGold", new Color(.9f, .7f, .25f), .5f, emission: new Color(.3f, .2f, .03f));
            var body = new MeshB(); body.Box(new Vector3(0, .28f, 0), new Vector3(1f, .56f, .66f)); Part(chest, "Body", body, wood);
            var bands = new MeshB(); foreach (float x in new[] { -.36f, .36f }) bands.Box(new Vector3(x, .28f, 0), new Vector3(.1f, .58f, .68f)); bands.Box(new Vector3(0, .42f, .34f), new Vector3(.16f, .2f, .05f)); Part(chest, "Bands", bands, metal);
            var lid = new GameObject("Lid").transform; lid.SetParent(chest, false); lid.localPosition = new Vector3(0, .56f, -.33f);
            var lidMesh = new MeshB(); lidMesh.Box(new Vector3(0, .1f, .33f), new Vector3(1f, .2f, .66f)); lidMesh.Box(new Vector3(0, .24f, .33f), new Vector3(.9f, .1f, .5f)); Part(lid, "Lid Wood", lidMesh, wood);
            var lidBands = new MeshB(); foreach (float x in new[] { -.36f, .36f }) lidBands.Box(new Vector3(x, .13f, .33f), new Vector3(.1f, .28f, .68f)); Part(lid, "Lid Bands", lidBands, metal);
            var glow = PointLight(chest, new Vector3(0, .9f, .2f), new Color(1f, .8f, .35f), 3.5f, 1.2f);
            var bc = chest.gameObject.AddComponent<BoxCollider>(); bc.center = new Vector3(0, .38f, 0); bc.size = new Vector3(1f, .76f, .66f);
            var c = chest.gameObject.AddComponent<Chest>(); c.lid = lid; c.experience = xp; c.heal = heal; c.glow = glow; return c;
        }

        private static TeleportPad BuildPad(Transform parent, Vector3 local, float yaw, string label)
        {
            var pad = new GameObject("Rune Circle").transform; pad.SetParent(parent, false); pad.localPosition = local; pad.localRotation = Quaternion.Euler(0, yaw, 0);
            var ringStone = new MeshB();
            for (int k = 0; k < 12; k++)
            {
                float a0 = k * Mathf.PI / 6f, a1 = (k + 1) * Mathf.PI / 6f;
                Vector3 o0 = new Vector3(Mathf.Cos(a0), 0, Mathf.Sin(a0)), o1 = new Vector3(Mathf.Cos(a1), 0, Mathf.Sin(a1));
                ringStone.DoubleQuad(o0 * 1.25f + Vector3.up * .08f, o1 * 1.25f + Vector3.up * .08f, o1 * .95f + Vector3.up * .08f, o0 * .95f + Vector3.up * .08f);
                ringStone.DoubleQuad(o0 * 1.25f, o1 * 1.25f, o1 * 1.25f + Vector3.up * .08f, o0 * 1.25f + Vector3.up * .08f);
            }
            Part(pad, "Stone Ring", ringStone, Mat("RuneStone", new Color(.22f, .22f, .32f)));
            var ring = new GameObject("Ring").transform; ring.SetParent(pad, false); ring.localPosition = Vector3.up * .1f;
            var glyph = new MeshB();
            for (int k = 0; k < 5; k++)
            {
                float a0 = k * Mathf.PI * 2f / 5f, a1 = (k + 2) * Mathf.PI * 2f / 5f;
                Vector3 p0 = new Vector3(Mathf.Cos(a0), 0, Mathf.Sin(a0)) * .85f, p1 = new Vector3(Mathf.Cos(a1), 0, Mathf.Sin(a1)) * .85f, w = Vector3.Cross(p1 - p0, Vector3.up).normalized * .035f;
                glyph.DoubleQuad(p0 - w, p0 + w, p1 + w, p1 - w);
            }
            NoShadow(Part(ring, "Glyph", glyph, Mat("RuneGlow", new Color(.7f, .5f, 1f), emission: new Color(1.6f, .9f, 3f))));
            PointLight(pad, Vector3.up * .8f, new Color(.6f, .4f, 1f), 5f, 1.6f);
            var tp = pad.gameObject.AddComponent<TeleportPad>(); tp.ring = ring; tp.label = label; return tp;
        }

        // ───────────────────────── house ─────────────────────────
        private static void BuildHouse(Transform parent, Vector3 at, Vector3 centre, Dictionary<string, GameObject> templates, TeleportPad dungeonPad, string name)
        {
            const float W = 8.2f, D = 6.4f, H = 3.0f, T = .26f;
            var house = new GameObject(name).transform; house.SetParent(parent, false); house.position = at + Vector3.up * .02f; house.rotation = Quaternion.LookRotation(Flat(centre - at));
            house.gameObject.AddComponent<CameraIgnore>();
            var r = new System.Random(321 + (int)at.x);
            float Rr(float a, float b) => a + (float)r.NextDouble() * (b - a);
            Material plaster = Mat("Plaster", new Color(.60f, .54f, .62f)), timber = Mat("Timber", new Color(.16f, .12f, .17f)), slate = Mat("RoofSlate", new Color(.20f, .17f, .32f)),
                stone = Mat("HouseStone", new Color(.29f, .28f, .36f)), windowGlow = Mat("WindowGlow", new Color(1f, .82f, .5f), emission: new Color(2.2f, 1.4f, .6f)), floorWood = Mat("FloorWood", new Color(.34f, .24f, .17f));
            var shell = new List<Renderer>();
            GameObject Shell(string n, MeshB b, Material m) { var go = Part(house, n, b, m); shell.Add(go.GetComponent<Renderer>()); return go; }

            // floor & foundation (stay visible)
            var fl = new MeshB(); fl.Box(new Vector3(0, .04f, 0), new Vector3(W, .1f, D)); Part(house, "Floor", fl, floorWood);
            var found = new MeshB(); found.Box(new Vector3(0, -.2f, 0), new Vector3(W + .5f, .5f, D + .5f)); Part(house, "Foundation", found, stone);
            var rug = new MeshB(); rug.Box(new Vector3(0, .1f, -.2f), new Vector3(3.2f, .02f, 2.2f)); Part(house, "Rug", rug, Mat("Rug", new Color(.5f, .16f, .22f)));

            // walls with a doorway in the front (+Z)
            void Wall(Vector3 c, Vector3 size)
            {
                var b = new MeshB(); b.Box(c, size); var go = Shell("Wall", b, plaster);
                var bc = go.AddComponent<BoxCollider>(); bc.center = c; bc.size = size;
            }
            float doorW = 1.7f, seg = (W - doorW) / 2f;
            Wall(new Vector3(0, H / 2f, -D / 2f), new Vector3(W, H, T));
            Wall(new Vector3(-W / 2f, H / 2f, 0), new Vector3(T, H, D));
            Wall(new Vector3(W / 2f, H / 2f, 0), new Vector3(T, H, D));
            Wall(new Vector3(-(doorW / 2f + seg / 2f), H / 2f, D / 2f), new Vector3(seg, H, T));
            Wall(new Vector3(doorW / 2f + seg / 2f, H / 2f, D / 2f), new Vector3(seg, H, T));
            var lintel = new MeshB(); lintel.Box(new Vector3(0, 2.3f + (H - 2.3f) / 2f, D / 2f), new Vector3(doorW, H - 2.3f, T)); Shell("Lintel", lintel, plaster);

            // timber frame
            var beams = new MeshB();
            foreach (float x in new[] { -W / 2f, W / 2f }) foreach (float z in new[] { -D / 2f, D / 2f }) beams.Box(new Vector3(x, H / 2f, z), new Vector3(.34f, H + .1f, .34f));
            foreach (float z in new[] { -D / 2f, D / 2f }) { beams.Box(new Vector3(0, H, z), new Vector3(W + .3f, .28f, .34f)); }
            foreach (float x in new[] { -W / 2f, W / 2f }) beams.Box(new Vector3(x, H, 0), new Vector3(.34f, .28f, D + .3f));
            foreach (float x in new[] { -doorW / 2f - .1f, doorW / 2f + .1f }) beams.Box(new Vector3(x, 1.2f, D / 2f + .03f), new Vector3(.2f, 2.4f, .32f));
            beams.Box(new Vector3(0, 2.38f, D / 2f + .03f), new Vector3(doorW + .4f, .2f, .32f));
            beams.Box(new Vector3(-W / 4f - .9f, H / 2f, D / 2f + .02f), new Vector3(.14f, H * 1.02f, .3f), Quaternion.Euler(0, 0, 22f));
            beams.Box(new Vector3(W / 4f + .9f, H / 2f, D / 2f + .02f), new Vector3(.14f, H * 1.02f, .3f), Quaternion.Euler(0, 0, -22f));
            Shell("Timber Frame", beams, timber);
            // glowing windows
            var win = new MeshB(); var frames = new MeshB();
            void Window(Vector3 c, bool alongX)
            {
                win.Box(c, alongX ? new Vector3(1.15f, .95f, .1f) : new Vector3(.1f, .95f, 1.15f));
                Vector3 bar = alongX ? new Vector3(1.3f, .09f, .16f) : new Vector3(.16f, .09f, 1.3f), post = alongX ? new Vector3(.09f, 1.1f, .16f) : new Vector3(.16f, 1.1f, .09f);
                frames.Box(c + Vector3.up * .5f, bar); frames.Box(c - Vector3.up * .5f, bar); frames.Box(c, post);
                Vector3 off = alongX ? Vector3.right * .6f : Vector3.forward * .6f; frames.Box(c + off, post); frames.Box(c - off, post);
            }
            Window(new Vector3(-(doorW / 2f + seg / 2f), 1.75f, D / 2f + .1f), true); Window(new Vector3(doorW / 2f + seg / 2f, 1.75f, D / 2f + .1f), true);
            Window(new Vector3(-W / 2f - .1f, 1.75f, .6f), false); Window(new Vector3(W / 2f + .1f, 1.75f, -.6f), false); Window(new Vector3(1.6f, 1.75f, -D / 2f - .1f), true);
            NoShadow(Shell("Windows", win, windowGlow)); Shell("Window Frames", frames, timber);

            // big swept roof (ridge along X): steep at the ridge, kicking out at the eaves
            float ridge = H + 2.9f, over = 1.0f, eaveZ = D / 2f + 1.15f;
            var roof = new MeshB();
            float[] zs = { 0f, .35f, .68f, 1f }; float[] ys = { 0f, .46f, .80f, 1f };
            foreach (float side in new[] { -1f, 1f })
                for (int s = 0; s < 3; s++)
                    for (int c = 0; c < 4; c++)
                    {
                        float x0 = Mathf.Lerp(-W / 2f - over, W / 2f + over, c / 4f), x1 = Mathf.Lerp(-W / 2f - over, W / 2f + over, (c + 1) / 4f);
                        float z0 = zs[s] * eaveZ * side, z1 = zs[s + 1] * eaveZ * side, y0 = Mathf.Lerp(ridge, H - .25f, ys[s]), y1 = Mathf.Lerp(ridge, H - .25f, ys[s + 1]);
                        float j = s == 2 ? Rr(-.12f, .12f) : 0f;
                        roof.DoubleQuad(new Vector3(x0, y0, z0), new Vector3(x1, y0, z0), new Vector3(x1, y1 + j, z1), new Vector3(x0, y1 - j, z1));
                    }
            roof.Box(new Vector3(0, ridge + .05f, 0), new Vector3(W + over * 2f + .2f, .2f, .3f));
            Shell("Roof", roof, slate);
            // gables
            var gable = new MeshB();
            foreach (float x in new[] { -W / 2f, W / 2f })
            {
                gable.Double(new Vector3(x, H, -D / 2f), new Vector3(x, H, D / 2f), new Vector3(x, ridge - .15f, 0));
                gable.Box(new Vector3(x, (H + ridge) / 2f, 0), new Vector3(.3f, ridge - H, .2f));
            }
            Shell("Gables", gable, plaster);
            // chimney
            var chim = new MeshB(); chim.Box(new Vector3(W / 2f - 1.5f, H + 2.0f, -1.2f), new Vector3(.8f, 3.0f, .8f)); chim.Box(new Vector3(W / 2f - 1.5f, H + 3.55f, -1.2f), new Vector3(1.0f, .22f, 1.0f));
            Shell("Chimney", chim, stone);
            // door (open), step, lanterns
            var door = new MeshB(); door.Box(new Vector3(-doorW / 2f - .55f, 1.12f, D / 2f + .62f), new Vector3(.1f, 2.2f, 1.3f), Quaternion.Euler(0, -18f, 0)); Part(house, "Door", door, Mat("HouseDoor", new Color(.33f, .21f, .14f)));
            var step = new MeshB(); step.Box(new Vector3(0, -.12f, D / 2f + .7f), new Vector3(2.4f, .3f, 1.0f)); Part(house, "Step", step, stone);
            foreach (float x in new[] { -1.35f, 1.35f })
            {
                var lam = new MeshB(); lam.Blob(new Vector3(x, 2.15f, D / 2f + .42f), new Vector3(.13f, .18f, .13f), .1f, r); lam.Box(new Vector3(x, 2.3f, D / 2f + .25f), new Vector3(.05f, .05f, .4f));
                NoShadow(Part(house, "Lantern", lam, windowGlow));
                PointLight(house, new Vector3(x, 2.1f, D / 2f + .7f), new Color(1f, .72f, .4f), 7f, 1.5f);
            }
            PointLight(house, new Vector3(0, 2.4f, 0), new Color(1f, .78f, .5f), 9f, 1.4f);   // warm room light
            // crooked fence around the yard (open at the front)
            var fence = new MeshB();
            for (int i = 0; i < 22; i++)
            {
                float a = Mathf.Lerp(.55f, Mathf.PI * 2f - .55f, i / 21f) + Mathf.PI / 2f;
                Vector3 b0 = new Vector3(Mathf.Cos(a) * 8.6f, 0, Mathf.Sin(a) * 7.4f);
                fence.Tube(new[] { b0 - Vector3.up * .2f, b0 + new Vector3(Rr(-.08f, .08f), Rr(.9f, 1.3f), Rr(-.08f, .08f)) }, new[] { .08f, .06f }, 5);
                if (i < 21) { float a2 = Mathf.Lerp(.55f, Mathf.PI * 2f - .55f, (i + 1) / 21f) + Mathf.PI / 2f; Vector3 b1 = new Vector3(Mathf.Cos(a2) * 8.6f, 0, Mathf.Sin(a2) * 7.4f); fence.Tube(new[] { b0 + Vector3.up * .7f, b1 + Vector3.up * Rr(.6f, .8f) }, new[] { .045f, .045f }, 4, false); }
            }
            Part(house, "Fence", fence, timber);
            // furniture: table, crates, barrel
            var furn = new MeshB(); furn.Box(new Vector3(-2.4f, .78f, -1.9f), new Vector3(1.8f, .1f, 1.0f));
            foreach (float x in new[] { -3.15f, -1.65f }) foreach (float z in new[] { -2.3f, -1.5f }) furn.Box(new Vector3(x, .4f, z), new Vector3(.1f, .76f, .1f));
            furn.Box(new Vector3(3.3f, .5f, 2.3f), new Vector3(.8f, .8f, .8f), Quaternion.Euler(0, 14f, 0)); furn.Box(new Vector3(3.25f, 1.2f, 2.35f), new Vector3(.6f, .6f, .6f), Quaternion.Euler(0, -20f, 0));
            furn.Tube(new[] { new Vector3(-3.4f, .1f, 2.3f), new Vector3(-3.4f, 1.1f, 2.3f) }, new[] { .42f, .42f }, 8);
            Part(house, "Furniture", furn, Mat("Furniture", new Color(.38f, .25f, .15f)));

            var hd = house.gameObject.AddComponent<HouseDungeon>(); hd.shell = shell.ToArray(); hd.interiorSize = new Vector3(W - .3f, 4f, D - .3f);

            if (dungeonPad == null)
            {
                BuildChest(house, new Vector3(2.9f, .1f, -2.35f), 0f, 45, 40f);
                BuildChest(house, new Vector3(-3.2f, .1f, .2f), 90f, 35, 30f);
                // ambush: two mobs burst out of the back corner when the goblin walks in
                var ambush = new GameObject("Ambush").transform; ambush.SetParent(house, false); ambush.localPosition = new Vector3(.8f, .12f, -2.2f);
                var mouth = new GameObject("Mouth").transform; mouth.SetParent(ambush, false);
                var lair = ambush.gameObject.AddComponent<MobLair>(); lair.mouth = mouth; lair.triggerRadius = 4.6f; lair.totalMobs = 2; lair.maxAlive = 2; lair.spawnInterval = 1.2f; lair.emergeDistance = 1.6f; lair.reward = 50; lair.lairName = "Засада";
                lair.templates = Pick(templates, "RatRogue", "BoneSentinel");
            }
            else
            {
                BuildChest(house, new Vector3(-3.2f, .1f, .2f), 90f, 30, 30f);
                var pad = BuildPad(house, new Vector3(2.1f, .1f, -1.2f), 0f, "спуститься в подземелье");
                pad.target = dungeonPad; dungeonPad.target = pad;
            }
        }

        // ───────────────────────── dungeon chamber ─────────────────────────
        /// <summary>Stone chamber far below the map: a lair in the far wall, a great chest, and the way back.</summary>
        private static TeleportPad BuildDungeon(Transform parent, Vector3 at, Dictionary<string, GameObject> templates)
        {
            const float S = 20f, Hh = 6.5f;
            var room = new GameObject("Dungeon Chamber").transform; room.SetParent(parent, false); room.position = at;
            room.gameObject.AddComponent<CameraIgnore>();
            var r = new System.Random(999);
            Material floorM = Mat("DungeonFloor", new Color(.15f, .16f, .24f)), wallM = Mat("DungeonWall", new Color(.19f, .19f, .30f)), trim = Mat("DungeonTrim", new Color(.10f, .10f, .17f));
            var floor = new MeshB();
            for (int x = 0; x < 5; x++) for (int z = 0; z < 5; z++) floor.Box(new Vector3((x - 2) * S / 5f, -.25f + ((x + z) % 2) * .03f, (z - 2) * S / 5f), new Vector3(S / 5f - .08f, .5f, S / 5f - .08f));
            var fgo = Part(room, "Floor", floor, floorM); var fc = fgo.AddComponent<BoxCollider>(); fc.center = new Vector3(0, -.25f, 0); fc.size = new Vector3(S, .5f, S);
            void Wall(Vector3 c, Vector3 size) { var b = new MeshB(); b.Box(c, size); var go = Part(room, "Wall", b, wallM); var bc = go.AddComponent<BoxCollider>(); bc.center = c; bc.size = size; }
            Wall(new Vector3(0, Hh / 2f, -S / 2f), new Vector3(S + 1f, Hh, 1f)); Wall(new Vector3(0, Hh / 2f, S / 2f), new Vector3(S + 1f, Hh, 1f));
            Wall(new Vector3(-S / 2f, Hh / 2f, 0), new Vector3(1f, Hh, S)); Wall(new Vector3(S / 2f, Hh / 2f, 0), new Vector3(1f, Hh, S));
            var pillars = new MeshB();
            foreach (float x in new[] { -5.5f, 5.5f }) foreach (float z in new[] { -5.5f, 5.5f })
                pillars.Tube(new[] { new Vector3(x, 0, z), new Vector3(x, .5f, z), new Vector3(x, Hh - .8f, z), new Vector3(x, Hh - .2f, z) }, new[] { .75f, .5f, .5f, .8f }, 7);
            var pg = Part(room, "Pillars", pillars, trim);
            foreach (float x in new[] { -5.5f, 5.5f }) foreach (float z in new[] { -5.5f, 5.5f }) { var cc = pg.AddComponent<CapsuleCollider>(); cc.center = new Vector3(x, Hh / 2f, z); cc.radius = .55f; cc.height = Hh; }
            // torches
            var flame = Mat("FlameMid", new Color(1f, .62f, .2f), emission: new Color(2.6f, 1.2f, .25f));
            foreach (var p in new[] { new Vector3(-S / 2f + .7f, 2.6f, -4f), new Vector3(-S / 2f + .7f, 2.6f, 4f), new Vector3(S / 2f - .7f, 2.6f, -4f), new Vector3(S / 2f - .7f, 2.6f, 4f) })
            {
                var t = new MeshB(); t.Tube(new[] { p + Vector3.down * .6f, p }, new[] { .06f, .09f }, 5); Part(room, "Torch", t, trim);
                var f = new MeshB(); f.Cone(p, .14f, .5f, 4); NoShadow(Part(room, "Torch Flame", f, flame));
                PointLight(room, p + Vector3.up * .3f, new Color(1f, .6f, .3f), 11f, 2.0f);
            }
            // lair archway in the far wall (-Z)
            var gate = new GameObject("Crypt Gate").transform; gate.SetParent(room, false); gate.localPosition = new Vector3(0, 0, -S / 2f + .55f);
            var arch = new MeshB(); var pts = new List<Vector3>(); for (int k = 0; k <= 8; k++) { float a = Mathf.PI * k / 8f; pts.Add(new Vector3(Mathf.Cos(a) * 1.5f, Mathf.Sin(a) * 3.0f, 0)); }
            arch.Tube(pts, Enumerable.Repeat(.3f, pts.Count).ToArray(), 6); Part(gate, "Arch", arch, trim);
            var hollow = new MeshB(); for (int k = 0; k < 8; k++) { float a0 = Mathf.PI * k / 8f, a1 = Mathf.PI * (k + 1) / 8f; hollow.Double(new Vector3(0, 0, .02f), new Vector3(Mathf.Cos(a0) * 1.4f, Mathf.Sin(a0) * 2.9f, .02f), new Vector3(Mathf.Cos(a1) * 1.4f, Mathf.Sin(a1) * 2.9f, .02f)); }
            NoShadow(Part(gate, "Hollow", hollow, Mat("Void", new Color(.01f, .01f, .015f), 0f)));
            var eyeMat = Mat("LairEye", new Color(1f, .5f, .15f), emission: new Color(3f, 1f, .2f)); var eyes = new List<Renderer>();
            foreach (float x in new[] { -.35f, .35f }) { var e = new MeshB(); e.Blob(Vector3.zero, new Vector3(.12f, .08f, .05f), .1f, r); var go = Part(gate, "Eye", e, eyeMat); go.transform.localPosition = new Vector3(x, 1.7f, .08f); NoShadow(go); eyes.Add(go.GetComponent<Renderer>()); }
            var mouth = new GameObject("Mouth").transform; mouth.SetParent(gate, false); mouth.localPosition = new Vector3(0, .1f, .6f);
            var lair = gate.gameObject.AddComponent<MobLair>(); lair.mouth = mouth; lair.eyes = eyes.ToArray(); lair.triggerRadius = 15f; lair.totalMobs = 5; lair.maxAlive = 2; lair.spawnInterval = 3f; lair.reward = 150; lair.lairName = "Склеп";
            lair.templates = Pick(templates, "BoneSentinel", "RatRogue", "BoneSentinel", "MossGourmand");
            BuildChest(room, new Vector3(6.5f, 0, -7f), -35f, 160, 90f, big: true);
            BuildChest(room, new Vector3(-7f, 0, -6.5f), 40f, 50, 40f);
            var pad = BuildPad(room, new Vector3(0, 0, 7.2f), 180f, "вернуться наверх");
            PointLight(room, new Vector3(0, 5f, 0), new Color(.45f, .5f, .95f), 22f, 1.3f);
            return pad;
        }
    }
}
