using System.Collections.Generic;
using UnityEngine;

namespace Goblingame.TestScene.Editor
{
    /// <summary>
    /// The forest cottage: stone plinth, timber framing, shuttered glowing windows, flower boxes,
    /// a tiled swept roof with moss, a smoking chimney and a porch. Inside: fireplace, bed, table,
    /// shelves of potions, herbs drying on a beam. Outside: woodpile, pumpkin patch, crooked fence.
    /// Walls, roof and everything attached to them fade while the goblin is inside.
    /// </summary>
    public static partial class SwampBuilder
    {
        /// <summary>Routes each primitive to a per-side mesh (1 front, 2 back, 3 left, 4 right) by where it sits.</summary>
        private sealed class ShellRouter
        {
            private readonly System.Func<Vector3, int> sideOf;
            public readonly SortedDictionary<int, MeshB> Parts = new SortedDictionary<int, MeshB>();
            public ShellRouter(System.Func<Vector3, int> sideOf) { this.sideOf = sideOf; }
            private MeshB At(Vector3 p) { int k = sideOf(p); if (!Parts.TryGetValue(k, out var b)) Parts[k] = b = new MeshB(); return b; }
            public void Box(Vector3 c, Vector3 size, Quaternion? rot = null) => At(c).Box(c, size, rot);
            public void Tube(IList<Vector3> path, IList<float> radius, int sides, bool cap = true)
            { Vector3 m = Vector3.zero; foreach (var q in path) m += q; At(m / path.Count).Tube(path, radius, sides, cap); }
            public void Blob(Vector3 c, Vector3 scale, float jitter, System.Random r) => At(c).Blob(c, scale, jitter, r);
            public void Double(Vector3 a, Vector3 b, Vector3 c) => At((a + b + c) / 3f).Double(a, b, c);
            public void DoubleQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d) => At((a + b + c + d) / 4f).DoubleQuad(a, b, c, d);
        }

        private static void BuildHouse(Transform parent, Vector3 at, Vector3 centre, Dictionary<string, GameObject> templates, TeleportPad dungeonPad, string name)
        {
            const float W = 8.2f, D = 6.4f, H = 3.0f, T = .26f;
            var house = new GameObject(name).transform; house.SetParent(parent, false); house.position = at + Vector3.up * .02f; house.rotation = Quaternion.LookRotation(Flat(centre - at));
            house.gameObject.AddComponent<CameraIgnore>();
            var r = new System.Random(321 + (int)at.x);
            float Rr(float a, float b) => a + (float)r.NextDouble() * (b - a);
            Material plaster = Mat("Plaster", new Color(.60f, .54f, .62f)), timber = Mat("Timber", new Color(.16f, .12f, .17f)), slate = Mat("RoofSlate", new Color(.20f, .17f, .32f)),
                slateB = Mat("RoofSlateLight", new Color(.26f, .22f, .38f)), stone = Mat("HouseStone", new Color(.29f, .28f, .36f)), stoneB = Mat("HouseStoneDark", new Color(.21f, .21f, .28f)),
                windowGlow = Mat("WindowGlow", new Color(1f, .82f, .5f), emission: new Color(2.2f, 1.4f, .6f)), floorWood = Mat("FloorWood", new Color(.34f, .24f, .17f)),
                floorWoodB = Mat("FloorWoodDark", new Color(.27f, .19f, .14f)), shutter = Mat("Shutter", new Color(.16f, .32f, .38f)), furniture = Mat("Furniture", new Color(.38f, .25f, .15f)),
                roofMoss = Mat("RoofMoss", new Color(.16f, .36f, .30f)), blanket = Mat("Blanket", new Color(.18f, .38f, .45f)), linen = Mat("Linen", new Color(.82f, .78f, .7f)),
                rugM = Mat("Rug", new Color(.5f, .16f, .22f)), rugEdge = Mat("RugEdge", new Color(.85f, .62f, .25f)), soot = Mat("Void", new Color(.01f, .01f, .015f), 0f),
                flame = Mat("FlameMid", new Color(1f, .62f, .2f), emission: new Color(2.6f, 1.2f, .25f)), flameCore = Mat("FlameCore", new Color(1f, .96f, .75f), emission: new Color(3f, 2.6f, 1.6f)),
                fireLog = Mat("FireLog", new Color(.30f, .17f, .10f), emission: new Color(.35f, .1f, .02f)), candle = Mat("Candle", new Color(.95f, .9f, .75f), emission: new Color(.6f, .5f, .3f)),
                herb = Mat("Herbs", new Color(.35f, .5f, .25f)), herbDry = Mat("HerbsDry", new Color(.6f, .45f, .25f)), pumpkin = Mat("Pumpkin", new Color(.95f, .48f, .1f), emission: new Color(.18f, .06f, 0f)),
                leafGreen = Mat("PumpkinLeaf", new Color(.2f, .45f, .25f)), soil = Mat("GardenSoil", new Color(.18f, .12f, .12f)), bark = Mat("LogBark", new Color(.19f, .14f, .17f)), wood = Mat("OakWood", new Color(.44f, .31f, .2f)),
                metal = Mat("Iron", new Color(.25f, .25f, .3f), .45f);
            Material[] potions = { Mat("PotionTeal", new Color(.3f, .95f, .85f), .7f, emission: new Color(.2f, .9f, .8f)), Mat("PotionPink", new Color(1f, .4f, .7f), .7f, emission: new Color(.8f, .15f, .45f)),
                                   Mat("PotionAmber", new Color(1f, .7f, .2f), .7f, emission: new Color(.7f, .4f, .05f)), Mat("PotionViolet", new Color(.6f, .45f, 1f), .7f, emission: new Color(.35f, .2f, .9f)) };
            var flowerMats = new[] { Mat("FlowerPink", new Color(1f, .5f, .75f), emission: new Color(.25f, .08f, .15f)), Mat("FlowerYellow", new Color(1f, .85f, .3f), emission: new Color(.25f, .2f, .03f)), Mat("FlowerWhite", new Color(.95f, .95f, .9f), emission: new Color(.15f, .15f, .15f)) };
            var shell = new List<Renderer>(); var shellNormals = new List<Vector3>();
            GameObject Shell(string n, MeshB b, Material m) { var go = Part(house, n, b, m); shell.Add(go.GetComponent<Renderer>()); shellNormals.Add(Vector3.zero); return go; }   // roof: fades whenever inside
            float doorW = 1.7f, seg = (W - doorW) / 2f;
            // wall-side pieces are split per side, so only the walls facing the camera fade
            var routers = new List<(ShellRouter router, string name, Material mat, bool shadow)>();
            ShellRouter Side(string n, Material m, bool shadow = true)
            {
                var rt = new ShellRouter(pt => Mathf.Abs(pt.z) / (D / 2f) >= Mathf.Abs(pt.x) / (W / 2f) ? (pt.z > 0f ? 1 : 2) : (pt.x > 0f ? 4 : 3));
                routers.Add((rt, n, m, shadow)); return rt;
            }

            // ── floor of planks, foundation stones (stay visible)
            var planks = new MeshB(); var planksB = new MeshB();
            int pc = 0; for (float x = -W / 2f + .15f; x < W / 2f; x += .3f, pc++) (pc % 2 == 0 ? planks : planksB).Box(new Vector3(x, .04f + (pc % 3) * .004f, 0), new Vector3(.29f, .1f, D));
            Part(house, "Floor", planks, floorWood); Part(house, "Floor Dark", planksB, floorWoodB);
            var found = new MeshB(); found.Box(new Vector3(0, -.3f, 0), new Vector3(W + .5f, .5f, D + .5f));
            var foundStones = new MeshB();
            for (float x = -W / 2f - .1f; x <= W / 2f + .1f; x += Rr(.55f, .8f)) foreach (float z in new[] { -D / 2f - .28f, D / 2f + .28f }) foundStones.Blob(new Vector3(x, -.12f, z), new Vector3(Rr(.28f, .4f), Rr(.16f, .22f), .14f), .35f, r);
            for (float z = -D / 2f; z <= D / 2f; z += Rr(.55f, .8f)) foreach (float x in new[] { -W / 2f - .28f, W / 2f + .28f }) foundStones.Blob(new Vector3(x, -.12f, z), new Vector3(.14f, Rr(.16f, .22f), Rr(.28f, .4f)), .35f, r);
            Part(house, "Foundation", found, stone); Part(house, "Foundation Stones", foundStones, stoneB);
            // rug with a golden border
            var rug = new MeshB(); rug.Box(new Vector3(.3f, .1f, .3f), new Vector3(3.0f, .02f, 2.0f)); Part(house, "Rug", rug, rugM);
            var rugB = new MeshB(); foreach (float z in new[] { -.68f, 1.28f }) rugB.Box(new Vector3(.3f, .105f, z), new Vector3(3.0f, .022f, .12f)); foreach (float x in new[] { -1.18f, 1.78f }) rugB.Box(new Vector3(x, .105f, .3f), new Vector3(.12f, .022f, 2.0f));
            Part(house, "Rug Border", rugB, rugEdge);

            // ── walls with a stone plinth (the plinth is part of the fading shell)
            var walls = Side("Walls", plaster); var wallCols = new List<(Vector3 c, Vector3 s)>();
            void Wall(Vector3 c, Vector3 size) { walls.Box(c, size); wallCols.Add((c, size)); }
            Wall(new Vector3(0, H / 2f, -D / 2f), new Vector3(W, H, T));
            Wall(new Vector3(-W / 2f, H / 2f, 0), new Vector3(T, H, D));
            Wall(new Vector3(W / 2f, H / 2f, 0), new Vector3(T, H, D));
            Wall(new Vector3(-(doorW / 2f + seg / 2f), H / 2f, D / 2f), new Vector3(seg, H, T));
            Wall(new Vector3(doorW / 2f + seg / 2f, H / 2f, D / 2f), new Vector3(seg, H, T));
            walls.Box(new Vector3(0, 2.3f + (H - 2.3f) / 2f, D / 2f), new Vector3(doorW, H - 2.3f, T));
            var wcol = new GameObject("Wall Colliders"); wcol.transform.SetParent(house, false);
            foreach (var (c, s) in wallCols) { var bc = wcol.AddComponent<BoxCollider>(); bc.center = c; bc.size = s; }
            var plinth = Side("Plinth", stoneB);
            plinth.Box(new Vector3(0, .3f, -D / 2f), new Vector3(W + .1f, .6f, T + .14f));
            foreach (float x in new[] { -W / 2f, W / 2f }) plinth.Box(new Vector3(x, .3f, 0), new Vector3(T + .14f, .6f, D + .1f));
            foreach (float sx in new[] { -1f, 1f }) plinth.Box(new Vector3(sx * (doorW / 2f + seg / 2f), .3f, D / 2f), new Vector3(seg, .6f, T + .14f));
            // individual stones poking out of the plinth
            for (int i = 0; i < 40; i++)
            {
                int side = i % 4; float u = Rr(-.48f, .48f);
                Vector3 p = side == 0 ? new Vector3(u * W, Rr(.12f, .5f), -D / 2f - .12f) : side == 1 ? new Vector3(-W / 2f - .12f, Rr(.12f, .5f), u * D) : side == 2 ? new Vector3(W / 2f + .12f, Rr(.12f, .5f), u * D) : new Vector3(u * W, Rr(.12f, .5f), D / 2f + .12f);
                if (side == 3 && Mathf.Abs(p.x) < doorW / 2f + .1f) continue;
                plinth.Box(p, new Vector3(side == 1 || side == 2 ? .08f : Rr(.25f, .45f), Rr(.12f, .2f), side == 1 || side == 2 ? Rr(.25f, .45f) : .08f));
            }

            // ── timber framing on every wall
            var beams = Side("Timber Frame", timber);
            foreach (float x in new[] { -W / 2f, W / 2f }) foreach (float z in new[] { -D / 2f, D / 2f }) beams.Box(new Vector3(x, H / 2f, z), new Vector3(.34f, H + .1f, .34f));
            foreach (float z in new[] { -D / 2f, D / 2f }) beams.Box(new Vector3(0, H, z), new Vector3(W + .3f, .28f, .34f));
            foreach (float x in new[] { -W / 2f, W / 2f }) beams.Box(new Vector3(x, H, 0), new Vector3(.34f, .28f, D + .3f));
            foreach (float z in new[] { -D / 2f - .02f }) { beams.Box(new Vector3(0, 1.2f, z), new Vector3(W, .16f, .3f)); foreach (float x in new[] { -W / 6f, W / 6f }) beams.Box(new Vector3(x, H / 2f, z), new Vector3(.2f, H, .3f)); }
            foreach (float x in new[] { -W / 2f - .02f, W / 2f + .02f }) { beams.Box(new Vector3(x, 1.2f, 0), new Vector3(.3f, .16f, D)); beams.Box(new Vector3(x, H / 2f, 0), new Vector3(.3f, H, .2f)); }
            // diagonal braces
            float braceA = Mathf.Atan2(H, W / 3f) * Mathf.Rad2Deg;
            beams.Box(new Vector3(-W / 3f, H / 2f, -D / 2f - .03f), new Vector3(.14f, Mathf.Sqrt(H * H + W * W / 9f) * .95f, .28f), Quaternion.Euler(0, 0, 90f - braceA));
            beams.Box(new Vector3(W / 3f, H / 2f, -D / 2f - .03f), new Vector3(.14f, Mathf.Sqrt(H * H + W * W / 9f) * .95f, .28f), Quaternion.Euler(0, 0, braceA - 90f));
            foreach (float x in new[] { -W / 2f - .03f, W / 2f + .03f }) foreach (float sz in new[] { -1f, 1f })
                beams.Box(new Vector3(x, H / 2f, sz * D / 4f), new Vector3(.28f, Mathf.Sqrt(H * H + D * D / 4f) * .92f, .14f), Quaternion.Euler(sz * (90f - Mathf.Atan2(H, D / 2f) * Mathf.Rad2Deg) * (x < 0 ? 1f : 1f), 0, 0));
            foreach (float x in new[] { -doorW / 2f - .1f, doorW / 2f + .1f }) beams.Box(new Vector3(x, 1.2f, D / 2f + .03f), new Vector3(.2f, 2.4f, .32f));
            beams.Box(new Vector3(0, 2.38f, D / 2f + .03f), new Vector3(doorW + .4f, .2f, .32f));
            beams.Box(new Vector3(-W / 4f - .9f, H / 2f, D / 2f + .02f), new Vector3(.14f, H * 1.02f, .3f), Quaternion.Euler(0, 0, 22f));
            beams.Box(new Vector3(W / 4f + .9f, H / 2f, D / 2f + .02f), new Vector3(.14f, H * 1.02f, .3f), Quaternion.Euler(0, 0, -22f));

            // ── windows: glow, frames, shutters, flower boxes under the front ones
            var win = Side("Windows", windowGlow, false); var frames = Side("Window Frames", timber); var shut = Side("Shutters", shutter); var boxes = Side("Flower Boxes", furniture);
            var flowers = new[] { Side("Flowers", flowerMats[0], false), Side("Flowers", flowerMats[1], false), Side("Flowers", flowerMats[2], false) }; var leaves = Side("Flower Leaves", leafGreen, false);
            void Window(Vector3 c, bool alongX, float outward, bool flowerBox)
            {
                Vector3 along = alongX ? Vector3.right : Vector3.forward, outDir = alongX ? Vector3.forward * outward : Vector3.right * outward;
                win.Box(c, alongX ? new Vector3(1.15f, .95f, .1f) : new Vector3(.1f, .95f, 1.15f));
                Vector3 bar = alongX ? new Vector3(1.3f, .09f, .16f) : new Vector3(.16f, .09f, 1.3f), post = alongX ? new Vector3(.09f, 1.1f, .16f) : new Vector3(.16f, 1.1f, .09f);
                frames.Box(c + Vector3.up * .5f, bar); frames.Box(c - Vector3.up * .5f, bar); frames.Box(c, post); frames.Box(c + along * .6f, post); frames.Box(c - along * .6f, post);
                frames.Box(c, alongX ? new Vector3(1.2f, .06f, .14f) : new Vector3(.14f, .06f, 1.2f));
                frames.Box(c - Vector3.up * .58f + outDir * .08f, alongX ? new Vector3(1.45f, .07f, .3f) : new Vector3(.3f, .07f, 1.45f));   // sill
                foreach (float s in new[] { -1f, 1f })
                {
                    // shutters swung half open
                    Vector3 hinge = c + along * s * .66f + outDir * .06f;
                    Quaternion swing = Quaternion.AngleAxis(s * outward * (alongX ? -35f : 35f), Vector3.up);
                    Vector3 sc = hinge + swing * (along * s * .3f) + outDir * .02f;
                    shut.Box(sc, alongX ? new Vector3(.58f, 1.0f, .06f) : new Vector3(.06f, 1.0f, .58f), swing);
                    frames.Box(sc + Vector3.up * .3f + outDir * .03f, alongX ? new Vector3(.5f, .06f, .05f) : new Vector3(.05f, .06f, .5f), swing);
                    frames.Box(sc - Vector3.up * .3f + outDir * .03f, alongX ? new Vector3(.5f, .06f, .05f) : new Vector3(.05f, .06f, .5f), swing);
                }
                if (!flowerBox) return;
                Vector3 bc = c - Vector3.up * .78f + outDir * .25f;
                boxes.Box(bc, alongX ? new Vector3(1.25f, .26f, .3f) : new Vector3(.3f, .26f, 1.25f));
                for (int i = 0; i < 9; i++)
                {
                    Vector3 fp = bc + along * Rr(-.55f, .55f) + Vector3.up * Rr(.18f, .34f) + outDir * Rr(-.06f, .06f);
                    flowers[i % 3].Blob(fp, Vector3.one * Rr(.06f, .09f), .3f, r);
                    leaves.Blob(fp - Vector3.up * .1f + along * Rr(-.08f, .08f), new Vector3(.1f, .05f, .1f), .4f, r);
                }
            }
            Window(new Vector3(-(doorW / 2f + seg / 2f), 1.75f, D / 2f + .1f), true, 1f, true); Window(new Vector3(doorW / 2f + seg / 2f, 1.75f, D / 2f + .1f), true, 1f, true);
            Window(new Vector3(-W / 2f - .1f, 1.75f, .6f), false, -1f, false); Window(new Vector3(W / 2f + .1f, 1.75f, -1.7f), false, 1f, false); Window(new Vector3(1.6f, 1.75f, -D / 2f - .1f), true, -1f, false);

            // ── swept roof (ridge along X): steep at the ridge, kicking out at the eaves, with rows of tiles
            float ridge = H + 2.9f, over = 1.0f, eaveZ = D / 2f + 1.15f;
            float[] zs = { 0f, .35f, .68f, 1f }; float[] ys = { 0f, .46f, .80f, 1f };
            Vector3 Profile(float u, float side)
            {
                // u: 0 at the ridge → 1 at the eave
                int k = Mathf.Min(2, (int)(u * 3f)); float f = u * 3f - k;
                return new Vector3(0, Mathf.Lerp(Mathf.Lerp(ridge, H - .25f, ys[k]), Mathf.Lerp(ridge, H - .25f, ys[k + 1]), f), Mathf.Lerp(zs[k], zs[k + 1], f) * eaveZ * side);
            }
            var roof = new MeshB();
            foreach (float side in new[] { -1f, 1f })
                for (int s = 0; s < 3; s++)
                    for (int c = 0; c < 4; c++)
                    {
                        float x0 = Mathf.Lerp(-W / 2f - over, W / 2f + over, c / 4f), x1 = Mathf.Lerp(-W / 2f - over, W / 2f + over, (c + 1) / 4f);
                        float z0 = zs[s] * eaveZ * side, z1 = zs[s + 1] * eaveZ * side, y0 = Mathf.Lerp(ridge, H - .25f, ys[s]), y1 = Mathf.Lerp(ridge, H - .25f, ys[s + 1]);
                        roof.DoubleQuad(new Vector3(x0, y0, z0), new Vector3(x1, y0, z0), new Vector3(x1, y1, z1), new Vector3(x0, y1, z1));
                    }
            Shell("Roof", roof, slate);
            var tiles = new[] { new MeshB(), new MeshB() }; var mossB = new MeshB();
            foreach (float side in new[] { -1f, 1f })
            {
                int rows = 13;
                for (int row = 0; row < rows; row++)
                {
                    float u = (row + .6f) / rows;
                    Vector3 p = Profile(u, side), p2 = Profile(Mathf.Min(1f, u + .02f), side);
                    Vector3 down = (p2 - p).normalized; Vector3 normal = Vector3.Cross(Vector3.right * side, down).normalized; if (normal.y < 0) normal = -normal;
                    Quaternion q = Quaternion.LookRotation(down, normal);
                    float stagger = (row % 2) * .26f;
                    for (float x = -W / 2f - over + .27f + stagger; x < W / 2f + over - .2f; x += .52f)
                    {
                        Quaternion jitter = Quaternion.Euler(Rr(-3f, 3f), Rr(-4f, 4f), Rr(-3f, 3f));
                        tiles[r.Next(2)].Box(new Vector3(x, p.y, p.z) + normal * .07f, new Vector3(.5f, .045f, .5f), q * Quaternion.Euler(-6f, 0, 0) * jitter);
                    }
                    if (row > 4 && r.NextDouble() < .45) mossB.Blob(new Vector3(Rr(-W / 2f, W / 2f), p.y, p.z) + normal * .12f, new Vector3(Rr(.4f, .9f), .1f, Rr(.3f, .55f)), .5f, r);
                }
            }
            Shell("Roof Tiles", tiles[0], slate); Shell("Roof Tiles Light", tiles[1], slateB); Shell("Roof Moss", mossB, roofMoss);
            var ridgeCap = new MeshB();
            for (float x = -W / 2f - over; x <= W / 2f + over; x += .45f) ridgeCap.Box(new Vector3(x, ridge + .12f, 0), new Vector3(.44f, .2f, .42f), Quaternion.Euler(45f, 0, 0));
            Shell("Ridge", ridgeCap, stoneB);
            // gables with a round attic window and crossed timbers, bargeboards on the edges
            var gable = Side("Gables", plaster); var gableTimber = Side("Gable Timber", timber); var attic = Side("Attic Windows", windowGlow, false);
            foreach (float x in new[] { -W / 2f, W / 2f })
            {
                float sx = Mathf.Sign(x);
                gable.Double(new Vector3(x, H, -D / 2f), new Vector3(x, H, D / 2f), new Vector3(x, ridge - .15f, 0));
                gableTimber.Box(new Vector3(x + sx * .05f, (H + ridge) / 2f, 0), new Vector3(.2f, ridge - H, .2f));
                gableTimber.Box(new Vector3(x + sx * .05f, H + 1.1f, 0), new Vector3(.2f, .14f, D * .6f));
                attic.Tube(new[] { new Vector3(x + sx * .02f, H + 1.9f, 0), new Vector3(x + sx * .14f, H + 1.9f, 0) }, new[] { .32f, .32f }, 10);
                gableTimber.Tube(new[] { new Vector3(x + sx * .1f, H + 1.9f, 0), new Vector3(x + sx * .2f, H + 1.9f, 0) }, new[] { .4f, .4f }, 10);
                foreach (float sz in new[] { -1f, 1f })
                {
                    var pts = new List<Vector3>(); var rad = new List<float>();
                    for (int i = 0; i <= 6; i++) { var pp = Profile(i / 6f, sz); pts.Add(new Vector3(x + sx * (over + .02f), pp.y + .02f, pp.z)); rad.Add(.09f); }
                    gableTimber.Tube(pts, rad, 4);
                }
            }
            // stone chimney above the fireplace, smoke curling out
            Vector3 chim = new Vector3(W / 2f - .45f, 0, .9f);
            var chimney = new MeshB();
            for (float y = H - .2f; y < ridge + .9f; y += .32f) chimney.Box(new Vector3(chim.x + Rr(-.03f, .03f), y, chim.z + Rr(-.03f, .03f)), new Vector3(.9f + Rr(-.04f, .06f), .3f, .9f + Rr(-.04f, .06f)), Quaternion.Euler(0, Rr(-3f, 3f), 0));
            chimney.Box(new Vector3(chim.x, ridge + 1.05f, chim.z), new Vector3(1.1f, .16f, 1.1f));
            Shell("Chimney", chimney, stone);
            var smoke = new GameObject("Chimney Smoke"); smoke.transform.SetParent(house, false); smoke.transform.localPosition = new Vector3(chim.x, ridge + 1.2f, chim.z); smoke.transform.localRotation = Quaternion.Euler(-90, 0, 0);
            var sps = smoke.AddComponent<ParticleSystem>(); var sm = sps.main; sm.startLifetime = new ParticleSystem.MinMaxCurve(4f, 6f); sm.startSpeed = new ParticleSystem.MinMaxCurve(.5f, .9f); sm.startSize = new ParticleSystem.MinMaxCurve(.6f, 1.1f);
            sm.startColor = new Color(.55f, .5f, .62f, .35f); sm.simulationSpace = ParticleSystemSimulationSpace.World; sm.maxParticles = 60;
            var sem = sps.emission; sem.rateOverTime = 5f; var ssh = sps.shape; ssh.shapeType = ParticleSystemShapeType.Cone; ssh.angle = 8f; ssh.radius = .15f;
            var ssz = sps.sizeOverLifetime; ssz.enabled = true; ssz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, .6f, 1, 2.6f));
            var scol = sps.colorOverLifetime; scol.enabled = true; var sg = new Gradient(); sg.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) }, new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(.8f, .15f), new GradientAlphaKey(0f, 1f) }); scol.color = sg;
            var snoise = sps.noise; snoise.enabled = true; snoise.strength = .35f; snoise.frequency = .3f;
            var smokeMat = new Material(Shader.Find("Legacy Shaders/Particles/Alpha Blended")) { name = "ChimneySmoke", mainTexture = SoftDot() };
            string smokePath = Folder + "/Materials/ChimneySmoke.mat"; if (UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(smokePath) == null) UnityEditor.AssetDatabase.CreateAsset(smokeMat, smokePath); else smokeMat = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(smokePath);
            smoke.GetComponent<ParticleSystemRenderer>().sharedMaterial = smokeMat;
            // porch roof over the door
            var porch = Side("Porch Posts", timber);
            foreach (float x in new[] { -1.25f, 1.25f }) porch.Box(new Vector3(x, 1.25f, D / 2f + 1.35f), new Vector3(.16f, 2.5f, .16f));
            porch.Box(new Vector3(0, 2.5f, D / 2f + 1.35f), new Vector3(2.8f, .16f, .18f));
            foreach (float x in new[] { -1.25f, 1.25f }) porch.Box(new Vector3(x, 2.3f, D / 2f + 1.15f), new Vector3(.1f, .1f, .55f), Quaternion.Euler(40f, 0, 0));
            var porchRoof = Side("Porch Roof", slateB); porchRoof.Box(new Vector3(0, 2.75f, D / 2f + .85f), new Vector3(3.2f, .1f, 1.5f), Quaternion.Euler(-16f, 0, 0));
            for (float x = -1.45f; x <= 1.46f; x += .48f) porchRoof.Box(new Vector3(x, 2.82f, D / 2f + .85f), new Vector3(.46f, .05f, 1.5f), Quaternion.Euler(-16f + Rr(-2f, 2f), 0, 0));

            // ── door (open), step stones, lanterns
            var door = new MeshB(); door.Box(new Vector3(-doorW / 2f - .55f, 1.12f, D / 2f + .62f), new Vector3(.1f, 2.2f, 1.3f), Quaternion.Euler(0, -18f, 0));
            Part(house, "Door", door, Mat("HouseDoor", new Color(.33f, .21f, .14f)));
            var hinge = new MeshB(); foreach (float y in new[] { .5f, 1.8f }) hinge.Box(new Vector3(-doorW / 2f - .5f, y, D / 2f + .62f), new Vector3(.13f, .08f, 1.1f), Quaternion.Euler(0, -18f, 0)); Part(house, "Door Bands", hinge, metal);
            var step = new MeshB(); step.Box(new Vector3(0, -.12f, D / 2f + .7f), new Vector3(2.4f, .3f, 1.0f));
            for (int i = 0; i < 4; i++) step.Blob(new Vector3(Rr(-.5f, .5f), -.12f, D / 2f + 1.6f + i * .75f), new Vector3(.42f, .1f, .32f), .3f, r);
            Part(house, "Step", step, stone);
            foreach (float x in new[] { -1.35f, 1.35f })
            {
                var lam = new MeshB(); lam.Blob(new Vector3(x, 2.15f, D / 2f + .42f), new Vector3(.13f, .18f, .13f), .1f, r); lam.Box(new Vector3(x, 2.3f, D / 2f + .25f), new Vector3(.05f, .05f, .4f));
                NoShadow(Part(house, "Lantern", lam, windowGlow));
                PointLight(house, new Vector3(x, 2.1f, D / 2f + .7f), new Color(1f, .72f, .4f), 7f, 1.5f);
            }

            // ── interior (always visible)
            // fireplace on the right wall
            var hearth = new MeshB(); float fx = W / 2f - .4f, fz = chim.z;
            hearth.Box(new Vector3(fx, .7f, fz - .62f), new Vector3(.6f, 1.4f, .36f)); hearth.Box(new Vector3(fx, .7f, fz + .62f), new Vector3(.6f, 1.4f, .36f));
            hearth.Box(new Vector3(fx, 1.5f, fz), new Vector3(.66f, .25f, 1.65f));                      // mantel
            hearth.Box(new Vector3(fx + .08f, 2.25f, fz), new Vector3(.5f, 1.3f, 1.1f));                 // hood up to the ceiling
            hearth.Box(new Vector3(fx - .1f, .08f, fz), new Vector3(.9f, .14f, 1.8f));                   // hearthstone
            Part(house, "Fireplace", hearth, stone);
            var back = new MeshB(); back.Box(new Vector3(fx + .22f, .7f, fz), new Vector3(.08f, 1.3f, .9f)); NoShadow(Part(house, "Fireplace Soot", back, soot));
            var flogs = new MeshB(); for (int i = 0; i < 3; i++) flogs.Tube(new[] { new Vector3(fx - .05f, .2f + i * .05f, fz - .35f + i * .1f), new Vector3(fx + .05f, .22f + i * .06f, fz + .35f - i * .12f) }, new[] { .07f, .07f }, 5);
            Part(house, "Fire Logs", flogs, fireLog);
            var fireRoot = new GameObject("Hearth Flames").transform; fireRoot.SetParent(house, false); fireRoot.localPosition = new Vector3(fx, .22f, fz);
            for (int i = 0; i < 5; i++) { var f = new MeshB(); f.Cone(Vector3.zero, .1f + (i % 2) * .04f, .35f + (i % 3) * .14f, 4); var go = Part(fireRoot, "Flame", f, i < 2 ? flameCore : flame); go.transform.localPosition = new Vector3(Rr(-.08f, .08f), 0, -.25f + i * .12f); NoShadow(go); }
            var hearthLight = PointLight(house, new Vector3(fx - .6f, .8f, fz), new Color(1f, .55f, .22f), 6.5f, 2.2f);
            var flicker = fireRoot.gameObject.AddComponent<Campfire>(); flicker.flames = fireRoot; flicker.glow = hearthLight; flicker.healPerSecond = 6f; flicker.radius = 2.4f;
            var mantel = new MeshB(); for (int i = 0; i < 4; i++) mantel.Tube(new[] { new Vector3(fx - .05f, 1.63f, fz - .55f + i * .35f), new Vector3(fx - .05f, 1.8f + (i % 2) * .08f, fz - .55f + i * .35f) }, new[] { .05f, .045f }, 6);
            NoShadow(Part(house, "Mantel Candles", mantel, candle));
            // bed with blanket and pillow against the back wall, nightstand
            var bed = new MeshB(); bed.Box(new Vector3(-2.85f, .3f, -2.5f), new Vector3(2.1f, .3f, 1.2f));
            foreach (float x in new[] { -3.85f, -1.85f }) foreach (float z in new[] { -3.05f, -1.95f }) bed.Box(new Vector3(x, .2f, z), new Vector3(.12f, .4f, .12f));
            bed.Box(new Vector3(-3.92f, .65f, -2.5f), new Vector3(.12f, .9f, 1.25f));
            Part(house, "Bed", bed, furniture);
            var mattress = new MeshB(); mattress.Box(new Vector3(-2.85f, .52f, -2.5f), new Vector3(2.0f, .16f, 1.12f)); mattress.Blob(new Vector3(-3.5f, .66f, -2.5f), new Vector3(.3f, .1f, .42f), .15f, r);
            Part(house, "Mattress", mattress, linen);
            var cover = new MeshB(); cover.Box(new Vector3(-2.45f, .62f, -2.48f), new Vector3(1.25f, .08f, 1.2f), Quaternion.Euler(0, 0, -2f)); cover.Box(new Vector3(-1.84f, .45f, -2.48f), new Vector3(.06f, .3f, 1.2f));
            Part(house, "Blanket", cover, blanket);
            var night = new MeshB(); night.Box(new Vector3(-1.4f, .32f, -2.85f), new Vector3(.5f, .64f, .45f)); Part(house, "Nightstand", night, furniture);
            var nCandle = new MeshB(); nCandle.Tube(new[] { new Vector3(-1.4f, .64f, -2.85f), new Vector3(-1.4f, .82f, -2.85f) }, new[] { .05f, .045f }, 6); NoShadow(Part(house, "Candle", nCandle, candle));
            var nFlame = new MeshB(); nFlame.Cone(new Vector3(-1.4f, .83f, -2.85f), .03f, .09f, 4); NoShadow(Part(house, "Candle Flame", nFlame, flameCore));
            // table, stools, a bowl and a mug
            var table = new MeshB(); table.Box(new Vector3(-1.1f, .78f, 1.05f), new Vector3(1.6f, .09f, .95f));
            foreach (float x in new[] { -1.75f, -.45f }) foreach (float z in new[] { .7f, 1.4f }) table.Box(new Vector3(x, .38f, z), new Vector3(.1f, .76f, .1f));
            foreach (float z in new[] { .15f, 1.95f }) { table.Tube(new[] { new Vector3(-1.1f, 0, z), new Vector3(-1.1f, .44f, z) }, new[] { .05f, .05f }, 5); table.Tube(new[] { new Vector3(-1.1f, .44f, z), new Vector3(-1.1f, .5f, z) }, new[] { .24f, .24f }, 8); }
            Part(house, "Table", table, furniture);
            var tableware = new MeshB(); tableware.Tube(new[] { new Vector3(-1.4f, .83f, 1.0f), new Vector3(-1.4f, .9f, 1.0f) }, new[] { .12f, .17f }, 8); tableware.Tube(new[] { new Vector3(-.75f, .83f, 1.2f), new Vector3(-.75f, .97f, 1.2f) }, new[] { .06f, .065f }, 6);
            Part(house, "Tableware", tableware, wood);
            var tCandle = new MeshB(); tCandle.Tube(new[] { new Vector3(-1.05f, .83f, .8f), new Vector3(-1.05f, 1.05f, .8f) }, new[] { .05f, .045f }, 6); NoShadow(Part(house, "Table Candle", tCandle, candle));
            var tFlame = new MeshB(); tFlame.Cone(new Vector3(-1.05f, 1.06f, .8f), .03f, .1f, 4); NoShadow(Part(house, "Table Flame", tFlame, flameCore));
            PointLight(house, new Vector3(-1.05f, 1.3f, .8f), new Color(1f, .75f, .45f), 3.5f, .9f);
            // shelves of potions on the back wall
            var shelf = new MeshB(); var jars = new[] { new MeshB(), new MeshB(), new MeshB(), new MeshB() }; var corks = new MeshB();
            foreach (float y in new[] { 1.3f, 1.95f })
            {
                shelf.Box(new Vector3(2.4f, y, -D / 2f + .3f), new Vector3(2.2f, .06f, .36f));
                foreach (float x in new[] { 1.45f, 3.35f }) shelf.Box(new Vector3(x, y - .1f, -D / 2f + .22f), new Vector3(.05f, .2f, .2f), Quaternion.Euler(45f, 0, 0));
                for (float x = 1.55f; x < 3.3f; x += Rr(.22f, .34f))
                {
                    float h = Rr(.14f, .3f), rad = Rr(.05f, .09f);
                    jars[r.Next(4)].Tube(new[] { new Vector3(x, y + .03f, -D / 2f + .3f), new Vector3(x, y + .03f + h * .7f, -D / 2f + .3f), new Vector3(x, y + .03f + h, -D / 2f + .3f) }, new[] { rad, rad, rad * .45f }, 6);
                    corks.Tube(new[] { new Vector3(x, y + .03f + h, -D / 2f + .3f), new Vector3(x, y + .08f + h, -D / 2f + .3f) }, new[] { rad * .4f, rad * .38f }, 5);
                }
            }
            Part(house, "Shelves", shelf, furniture); Part(house, "Corks", corks, wood);
            for (int i = 0; i < 4; i++) NoShadow(Part(house, "Potions", jars[i], potions[i]));
            // a beam across the room with herbs drying on it
            var tie = new MeshB(); tie.Box(new Vector3(0, H - .2f, -.6f), new Vector3(W - .3f, .2f, .22f)); Part(house, "Tie Beam", tie, timber);
            var herbs = new MeshB(); var herbsDry = new MeshB();
            for (float x = -3.2f; x < 3.2f; x += Rr(.35f, .6f))
            {
                float len = Rr(.35f, .6f); var hb = r.NextDouble() < .5 ? herbs : herbsDry;
                hb.Tube(new[] { new Vector3(x, H - .3f, -.6f), new Vector3(x, H - .3f - len * .3f, -.6f) }, new[] { .015f, .015f }, 3, false);
                hb.Cone(new Vector3(x, H - .3f - len, -.6f), Rr(.07f, .11f), len * .75f, 5, r, .3f);
            }
            NoShadow(Part(house, "Herbs", herbs, herb)); NoShadow(Part(house, "Dry Herbs", herbsDry, herbDry));
            // barrel, crates, a broom by the door
            var furn = new MeshB();
            furn.Box(new Vector3(3.35f, .42f, 2.5f), new Vector3(.8f, .8f, .8f), Quaternion.Euler(0, 14f, 0)); furn.Box(new Vector3(3.3f, 1.12f, 2.55f), new Vector3(.6f, .6f, .6f), Quaternion.Euler(0, -20f, 0));
            furn.Tube(new[] { new Vector3(-3.4f, .1f, 2.4f), new Vector3(-3.4f, .55f, 2.4f), new Vector3(-3.4f, 1.0f, 2.4f) }, new[] { .36f, .43f, .36f }, 10);
            Part(house, "Barrel And Crates", furn, furniture);
            var hoops = new MeshB(); foreach (float y in new[] { .3f, .8f }) hoops.Tube(new[] { new Vector3(-3.4f, y, 2.4f), new Vector3(-3.4f, y + .05f, 2.4f) }, new[] { .425f, .425f }, 10); Part(house, "Barrel Hoops", hoops, metal);
            var broom = new MeshB(); broom.Tube(new[] { new Vector3(1.3f, .2f, 2.9f), new Vector3(1.45f, 1.6f, 3.0f) }, new[] { .025f, .025f }, 4); broom.Cone(new Vector3(1.28f, 0f, 2.89f), .17f, .4f, 6);
            Part(house, "Broom", broom, wood);

            // ── yard: crooked fence, woodpile, pumpkin patch, rain barrel
            var fence = new MeshB();
            for (int i = 0; i < 22; i++)
            {
                float a = Mathf.Lerp(.55f, Mathf.PI * 2f - .55f, i / 21f) + Mathf.PI / 2f;
                Vector3 b0 = new Vector3(Mathf.Cos(a) * 8.6f, 0, Mathf.Sin(a) * 7.4f);
                fence.Tube(new[] { b0 - Vector3.up * .2f, b0 + new Vector3(Rr(-.08f, .08f), Rr(.9f, 1.3f), Rr(-.08f, .08f)) }, new[] { .08f, .06f }, 5);
                if (i < 21) { float a2 = Mathf.Lerp(.55f, Mathf.PI * 2f - .55f, (i + 1) / 21f) + Mathf.PI / 2f; Vector3 b1 = new Vector3(Mathf.Cos(a2) * 8.6f, 0, Mathf.Sin(a2) * 7.4f); fence.Tube(new[] { b0 + Vector3.up * .7f, b1 + Vector3.up * Rr(.6f, .8f) }, new[] { .045f, .045f }, 4, false); fence.Tube(new[] { b0 + Vector3.up * .35f, b1 + Vector3.up * Rr(.3f, .4f) }, new[] { .04f, .04f }, 4, false); }
            }
            Part(house, "Fence", fence, timber);
            var pile = new MeshB(); var pileEnds = new MeshB();
            for (int row = 0; row < 4; row++)
                for (int i = 0; i < 6 - row; i++)
                {
                    float z = -1.8f + i * .52f + row * .26f, y = .2f + row * .36f;
                    pile.Tube(new[] { new Vector3(-W / 2f - .9f, y, z), new Vector3(-W / 2f - .2f, y, z) }, new[] { .2f, .2f }, 6, false);
                    foreach (float ex in new[] { -W / 2f - .91f, -W / 2f - .19f }) for (int kk = 0; kk < 6; kk++) { float a0 = kk * Mathf.PI / 3f, a1 = (kk + 1) * Mathf.PI / 3f; pileEnds.Double(new Vector3(ex, y, z), new Vector3(ex, y + Mathf.Cos(a0) * .2f, z + Mathf.Sin(a0) * .2f), new Vector3(ex, y + Mathf.Cos(a1) * .2f, z + Mathf.Sin(a1) * .2f)); }
                }
            Part(house, "Woodpile", pile, bark); Part(house, "Woodpile Ends", pileEnds, wood);
            var block = new MeshB(); block.Tube(new[] { new Vector3(-W / 2f - 1.6f, 0, 1.6f), new Vector3(-W / 2f - 1.6f, .5f, 1.6f) }, new[] { .32f, .3f }, 8);
            Part(house, "Chopping Block", block, wood);
            var axe = new MeshB(); axe.Tube(new[] { new Vector3(-W / 2f - 1.6f, .5f, 1.6f), new Vector3(-W / 2f - 1.25f, 1.05f, 1.75f) }, new[] { .03f, .03f }, 4); Part(house, "Axe Handle", axe, wood);
            var axeHead = new MeshB(); axeHead.Box(new Vector3(-W / 2f - 1.58f, .52f, 1.6f), new Vector3(.08f, .2f, .26f), Quaternion.Euler(0, 20f, 35f)); Part(house, "Axe Head", axeHead, metal);
            var bed2 = new MeshB(); bed2.Box(new Vector3(W / 2f + 1.9f, .06f, .2f), new Vector3(1.8f, .14f, 3.2f)); Part(house, "Garden Bed", bed2, soil);
            var pumps = new MeshB(); var pLeaves = new MeshB(); var stems = new MeshB();
            for (int i = 0; i < 5; i++)
            {
                Vector3 pp = new Vector3(W / 2f + 1.9f + Rr(-.5f, .5f), .3f, -1.1f + i * .65f); float s = Rr(.22f, .38f);
                for (int lobe = 0; lobe < 5; lobe++) { float a = lobe * 1.256f; pumps.Blob(pp + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * s * .45f + Vector3.up * (s * .1f), new Vector3(s * .6f, s * .75f, s * .6f), .1f, r); }
                stems.Tube(new[] { pp + Vector3.up * s * .8f, pp + Vector3.up * (s * .8f + .12f) + Vector3.right * .04f }, new[] { .035f, .025f }, 4);
                for (int l = 0; l < 3; l++) { float a = Rr(0, 6.28f); Vector3 o = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)); pLeaves.DoubleQuad(pp + o * s * .7f + Vector3.up * .02f - Vector3.up * .25f, pp + o * s * 1.4f + Vector3.Cross(o, Vector3.up) * .18f - Vector3.up * .22f, pp + o * s * 2f - Vector3.up * .26f, pp + o * s * 1.4f - Vector3.Cross(o, Vector3.up) * .18f - Vector3.up * .22f); }
            }
            Part(house, "Pumpkins", pumps, pumpkin); Part(house, "Pumpkin Stems", stems, herb); NoShadow(Part(house, "Pumpkin Leaves", pLeaves, leafGreen));
            var rain = new MeshB(); rain.Tube(new[] { new Vector3(W / 2f + .55f, -.05f, D / 2f + .5f), new Vector3(W / 2f + .55f, .45f, D / 2f + .5f), new Vector3(W / 2f + .55f, .95f, D / 2f + .5f) }, new[] { .34f, .4f, .34f }, 10);
            Part(house, "Rain Barrel", rain, furniture);

            PointLight(house, new Vector3(0, 2.4f, 0), new Color(1f, .78f, .5f), 9f, 1.2f);   // warm room light
            Vector3[] sideNormal = { Vector3.zero, Vector3.forward, Vector3.back, Vector3.left, Vector3.right };
            string[] sideName = { "", " (front)", " (back)", " (left)", " (right)" };
            foreach (var (router, n, m, shadow) in routers)
                foreach (var kv in router.Parts)
                {
                    var go = Part(house, n + sideName[kv.Key], kv.Value, m); if (!shadow) NoShadow(go);
                    shell.Add(go.GetComponent<Renderer>()); shellNormals.Add(sideNormal[kv.Key]);
                }
            var hd = house.gameObject.AddComponent<HouseDungeon>(); hd.shell = shell.ToArray(); hd.shellNormals = shellNormals.ToArray(); hd.interiorSize = new Vector3(W - .3f, 4f, D - .3f);

            if (dungeonPad == null)
            {
                BuildChest(house, new Vector3(2.9f, .1f, -2.35f), 0f, 45, 40f, false, "club", "coin:8");
                BuildChest(house, new Vector3(-3.2f, .1f, .2f), 90f, 35, 30f, false, "mushroom:3", "stew");
                // ambush: two mobs burst out of the back corner when the goblin walks in
                var ambush = new GameObject("Ambush").transform; ambush.SetParent(house, false); ambush.localPosition = new Vector3(.8f, .12f, -2.2f);
                var mouth = new GameObject("Mouth").transform; mouth.SetParent(ambush, false);
                var lair = ambush.gameObject.AddComponent<MobLair>(); lair.mouth = mouth; lair.triggerRadius = 4.6f; lair.totalMobs = 2; lair.maxAlive = 2; lair.spawnInterval = 1.2f; lair.emergeDistance = 1.6f; lair.reward = 50; lair.lairName = "Засада";
                lair.templates = Pick(templates, "RatRogue", "BoneSentinel");
            }
            else
            {
                BuildChest(house, new Vector3(-3.2f, .1f, .2f), 90f, 30, 30f, false, "dagger", "coin:6");
                var pad = BuildPad(house, new Vector3(2.1f, .1f, -1.2f), 0f, "спуститься в подземелье");
                pad.target = dungeonPad; dungeonPad.target = pad;
            }
        }
    }
}
