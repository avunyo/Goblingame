using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Goblingame.TestScene.Editor
{
    /// <summary>
    /// Turns a static multi-part enemy model into an animated puppet:
    ///   Visual (Animator) → Rig → Hips → { Torso → { Head, ArmL, ArmR }, LegL, LegR, Tail }
    /// Parts are sorted onto those pivots by name hints + position, then keyframed clips
    /// (Idle, Walk, Attack, Hit, Stagger, Death) are generated as editable .anim assets.
    /// </summary>
    public static class EnemyRigBuilder
    {
        public enum AttackStyle { Stab, Slam, Slash, Smash }

        public struct Spec
        {
            public string id; public AttackStyle style;
            public float windup, strike, recover;   // seconds, matches EnemyActor
            public float walkCycle;                 // seconds per stride cycle
            public float heaviness;                 // 0 light … 1 heavy (amplitudes / sway)
        }

        // word stems found in the part names (e.g. "Huge wooden hammer head", "Bony fingers.003")
        private static readonly string[] HeadHints = { "skull", "jaw", "tooth", "ember", "socket", "cavity", "helmet", "crown", "nasal", "mouth", "head", "ear", "tusk", "snout", "nostril", "muzzle", "nose", "brow", "cheek", "mane", "eye", "cap", "gill", "shadow", "inset" };
        private static readonly string[] ArmHints = { "arm", "forearm", "hand", "palm", "fist", "finger", "knuckle", "elbow", "clavicle", "humerus", "cuff", "bracer", "wrap", "pauldron", "shoulder" };
        private static readonly string[] WeaponHints = { "blade", "grip", "sword", "dagger", "shield", "hammer", "shaft", "winding" };
        private static readonly string[] LegHints = { "leg", "femur", "tibia", "knee", "foot", "boot", "sole", "hoof", "cleft" };
        private static readonly string[] BodyHints = { "belt", "buckle", "pouch", "vest", "collar", "scarf", "tabard", "strap", "stud", "strip", "trousers", "torso", "body", "rib", "spine", "pelvis" };
        private static readonly string[] HandHints = { "hand", "palm", "fist", "finger", "knuckle" };

        private static IEnumerable<string> Words(string name) =>
            name.ToLowerInvariant().Split('.')[0].Split(new[] { ' ', '_', '-' }, StringSplitOptions.RemoveEmptyEntries);
        private static bool Has(string name, string[] hints) => Words(name).Any(w => hints.Any(h => w == h || w.StartsWith(h)));

        private sealed class Part { public Transform t; public Bounds b; public string group; }

        public static AnimatorController Build(Transform visual, Transform root, Spec spec, string folder)
        {
            // ── frame of the creature (upright, facing root.forward)
            Vector3 up = Vector3.up, fwd = Vector3.ProjectOnPlane(root.forward, up).normalized, right = Vector3.Cross(up, fwd);
            var parts = new List<Part>();
            foreach (var r in visual.GetComponentsInChildren<Renderer>())
                parts.Add(new Part { t = r.transform, b = r.bounds });
            Bounds all = parts[0].b; foreach (var p in parts) all.Encapsulate(p.b);
            float H = all.size.y, ground = all.min.y;
            // body centre from the torso itself — whole-model bounds are pulled sideways by shields / hammers
            var torsoCore = parts.Where(p => Has(p.t.name, new[] { "torso", "body", "rib", "spine", "vest", "pelvis" })).ToList();
            Vector3 mid = all.center;
            if (torsoCore.Count > 0) { Bounds tb = torsoCore[0].b; foreach (var p in torsoCore) tb.Encapsulate(p.b); mid = tb.center; }
            Vector3 center = new Vector3(mid.x, ground, mid.z);
            float Lat(Vector3 v) => Vector3.Dot(v - center, right);

            // ── key heights
            var legParts = parts.Where(p => Has(p.t.name, LegHints)).ToList();
            float hipY = legParts.Count > 0 ? legParts.Max(p => p.b.max.y) : ground + H * .4f;
            hipY = Mathf.Clamp(hipY, ground + H * .15f, ground + H * .55f);
            // the main skull / head / mushroom cap (not hammer heads, moss or spots on the cap)
            var skullParts = parts.Where(p => !Has(p.t.name, WeaponHints) &&
                (Has(p.t.name, new[] { "head", "skull" }) || (Has(p.t.name, new[] { "cap" }) && Has(p.t.name, new[] { "mushroom" })))).ToList();
            float neckY = skullParts.Count > 0 ? skullParts.Min(p => p.b.min.y) : ground + H * .75f;
            float torsoHalf = torsoCore.Count > 0 ? torsoCore.Max(p => Mathf.Abs(Lat(p.b.center)) + Vector3.Dot(p.b.extents, new Vector3(Mathf.Abs(right.x), Mathf.Abs(right.y), Mathf.Abs(right.z)))) : H * .2f;
            float headHalf = skullParts.Count > 0 ? skullParts.Max(p => Mathf.Abs(Lat(p.b.center)) + Mathf.Max(p.b.extents.x, p.b.extents.z)) : H * .15f;

            // ── classify
            foreach (var p in parts)
            {
                string n = p.t.name; Vector3 c = p.b.center; float lat = Lat(c); string side = lat >= 0 ? "R" : "L";
                if (Has(n, new[] { "tail" })) p.group = "Tail";
                else if (Has(n, WeaponHints)) p.group = "Arm" + side;                 // re-assigned to the holding hand below
                else if (Has(n, BodyHints)) p.group = "Torso";
                else if (Has(n, LegHints) && c.y < hipY + H * .08f) p.group = "Leg" + side;
                else if (Has(n, ArmHints) && c.y < neckY + H * .05f && Mathf.Abs(lat) > torsoHalf * .55f) p.group = "Arm" + side;
                else if (Has(n, HeadHints) && c.y > neckY - H * .06f) p.group = "Head";
                else if (c.y > neckY && Mathf.Abs(lat) < headHalf * 1.3f) p.group = "Head";
                else if (Mathf.Abs(lat) > torsoHalf * 1.05f && c.y > hipY && c.y < neckY) p.group = "Arm" + side;
                else if (c.y < hipY - H * .05f && Mathf.Abs(lat) > H * .03f) p.group = "Leg" + side;
                else p.group = "Torso";
            }
            // ── weapons move as one piece: grow each weapon from its named parts through every touching
            //    unnamed piece (hammer bands, rivets, shield rims…), then give it to the nearest hand
            float eps = H * .015f;
            bool Touch(Bounds a, Bounds b) { a.Expand(eps * 2f); return a.Intersects(b); }
                        var weapon = new HashSet<Part>(parts.Where(p => Has(p.t.name, WeaponHints)));
            for (int pass = 0; pass < 12; pass++)
            {
                int before = weapon.Count;
                foreach (var p in parts)
                {
                    if (weapon.Contains(p) || Has(p.t.name, new[] { "tail" }) || Has(p.t.name, ArmHints) || Has(p.t.name, LegHints) || Has(p.t.name, HeadHints) || Has(p.t.name, BodyHints) || p.group == "Head") continue;
                    if (weapon.Any(w => Touch(w.b, p.b))) weapon.Add(p);
                }
                if (weapon.Count == before) break;
            }
            var hands = parts.Where(q => q.group.StartsWith("Arm") && Has(q.t.name, HandHints)).ToList();
            var pending = new List<Part>(weapon);
            while (pending.Count > 0)
            {
                // connected component = one weapon
                var comp = new List<Part> { pending[0] }; pending.RemoveAt(0);
                for (int i = 0; i < comp.Count; i++)
                    for (int j = pending.Count - 1; j >= 0; j--)
                        if (Touch(comp[i].b, pending[j].b)) { comp.Add(pending[j]); pending.RemoveAt(j); }
                Bounds cb = comp[0].b; foreach (var p in comp) cb.Encapsulate(p.b);
                if (hands.Count == 0) continue;
                string g = hands.OrderBy(h => (cb.ClosestPoint(h.b.center) - h.b.center).sqrMagnitude).First().group;
                foreach (var p in comp) p.group = g;
                Debug.Log("[EnemyRig] " + spec.id + " weapon (" + comp.Count + " parts: " + string.Join(",", comp.Take(6).Select(p => p.t.name)) + (comp.Count > 6 ? "…" : "") + ") → " + g);
            }

            // ── pivots
            Vector3 GroupCenter(string g, Vector3 fallback)
            { var gp = parts.Where(p => p.group == g).ToList(); if (gp.Count == 0) return fallback; Bounds b = gp[0].b; foreach (var p in gp) b.Encapsulate(p.b); return b.center; }
            Vector3 Shoulder(string g)
            {
                var gp = parts.Where(p => p.group == g && !Has(p.t.name, WeaponHints)).ToList();
                if (gp.Count == 0) return center + up * (neckY - ground) * .9f;
                // the upper-arm bone itself ("A pose arm", "Humerus"); its inner top corner is the shoulder joint
                var upper = gp.Where(p => Has(p.t.name, new[] { "arm", "humerus" })).OrderByDescending(p => p.b.max.y).FirstOrDefault();
                var top = upper ?? gp.OrderByDescending(p => p.b.max.y).First();
                Vector3 s = top.b.ClosestPoint(new Vector3(mid.x, top.b.max.y, mid.z));
                s.y = top.b.max.y - top.b.size.y * .08f; return s;
            }

            var rig = MakePivot("Rig", visual, center, fwd);
            var hips = MakePivot("Hips", rig, new Vector3(center.x, hipY, center.z), fwd);
            var torso = MakePivot("Torso", hips, new Vector3(center.x, hipY, center.z), fwd);
            Vector3 hc = GroupCenter("Head", center + up * (neckY - ground)); hc.y = neckY;
            var head = MakePivot("Head", torso, hc, fwd);
            var armL = MakePivot("ArmL", torso, Shoulder("ArmL"), fwd);
            var armR = MakePivot("ArmR", torso, Shoulder("ArmR"), fwd);
            Vector3 ll = GroupCenter("LegL", center - right * H * .1f); ll.y = hipY;
            Vector3 lr = GroupCenter("LegR", center + right * H * .1f); lr.y = hipY;
            var legL = MakePivot("LegL", hips, ll, fwd);
            var legR = MakePivot("LegR", hips, lr, fwd);
            Transform tail = null;
            var tailParts = parts.Where(p => p.group == "Tail").ToList();
            if (tailParts.Count > 0)
            {
                Bounds tb = tailParts[0].b; foreach (var p in tailParts) tb.Encapsulate(p.b);
                Vector3 anchor = tb.ClosestPoint(new Vector3(center.x, hipY, center.z));
                tail = MakePivot("Tail", hips, anchor, fwd);
            }
            var pivots = new Dictionary<string, Transform> { { "Torso", torso }, { "Head", head }, { "ArmL", armL }, { "ArmR", armR }, { "LegL", legL }, { "LegR", legR } };
            if (tail != null) pivots["Tail"] = tail;
            foreach (var p in parts) p.t.SetParent(pivots[p.group], true);

            foreach (var g in new[] { "ArmL", "ArmR" })
            {
                var piv = pivots[g]; Vector3 rel = piv.position - center;
                Debug.Log("[EnemyRig] " + spec.id + " " + g + " pivot lat=" + Lat(piv.position).ToString("0.00") + " y=" + (piv.position.y - ground).ToString("0.00") +
                          " fwd=" + Vector3.Dot(rel, fwd).ToString("0.00") + " parts: " + string.Join(", ", parts.Where(p => p.group == g).Select(p => p.t.name + "@" + (p.b.max.y - ground).ToString("0.00"))));
            }
            var counts = parts.GroupBy(p => p.group).Select(g => g.Key + ":" + g.Count());
            Debug.Log("[EnemyRig] " + spec.id + " H=" + H.ToString("0.00") + " hip=" + (hipY - ground).ToString("0.00") + " neck=" + (neckY - ground).ToString("0.00") + "  " + string.Join(" ", counts));

            // which arm carries the main weapon
            string[] mainWeapon = { "blade", "shaft", "grip", "hammer", "sword", "dagger" };
            bool rightWeapon = parts.Any(p => p.group == "ArmR" && Has(p.t.name, mainWeapon))
                               || !parts.Any(p => p.group == "ArmL" && Has(p.t.name, mainWeapon));

            // ── clips
            Directory.CreateDirectory(folder);
            float s = rig.lossyScale.y;   // world → rig-local units
            // the main weapon gets its own pivot in the fist, so it can turn in the hand (hammer head leads the swing)
            string mainArm = rightWeapon ? "ArmR" : "ArmL";
            var held = parts.Where(p => p.group == mainArm && weapon.Contains(p)).ToList();
            bool hasWeapon = false;
            if (held.Count > 0)
            {
                Bounds wb = held[0].b; foreach (var p in held) wb.Encapsulate(p.b);
                var fist = parts.Where(p => p.group == mainArm && Has(p.t.name, HandHints)).OrderBy(p => (wb.ClosestPoint(p.b.center) - p.b.center).sqrMagnitude).FirstOrDefault();
                Vector3 grip = fist != null ? wb.ClosestPoint(fist.b.center) : wb.center;
                var wp = MakePivot("Weapon", pivots[mainArm], grip, fwd);
                foreach (var p in held) p.t.SetParent(wp, true);
                hasWeapon = true;
            }
            var ctx = new Ctx { visual = visual, H = H / s, spec = spec, rightWeapon = rightWeapon, hasTail = tail != null, hasWeapon = hasWeapon,
                rigBase = rig.localRotation, rigPos = rig.localPosition, hipsPos = hips.localPosition };
            var clips = new Dictionary<string, AnimationClip>
            {
                { "Idle", Clip(ctx, folder, "Idle", 2.4f, true, IdlePose) },
                { "Walk", Clip(ctx, folder, "Walk", spec.walkCycle, true, WalkPose) },
                { "Attack", Clip(ctx, folder, "Attack", spec.windup + spec.strike + spec.recover, false, AttackPose) },
                { "Hit", Clip(ctx, folder, "Hit", .45f, false, HitPose) },
                { "Stagger", Clip(ctx, folder, "Stagger", 1.2f, true, StaggerPose) },
                { "Death", Clip(ctx, folder, "Death", 1.4f, false, DeathPose) },
            };

            string controllerPath = folder + "/" + spec.id + ".controller";
            AssetDatabase.DeleteAsset(controllerPath);
            var controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            var machine = controller.layers[0].stateMachine;
            foreach (var kv in clips)
            {
                var st = machine.AddState(kv.Key); st.motion = kv.Value; st.writeDefaultValues = true;
                if (kv.Key == "Idle") machine.defaultState = st;
            }
            EditorUtility.SetDirty(controller); AssetDatabase.SaveAssets();
            return controller;
        }

        private static Transform MakePivot(string name, Transform parent, Vector3 world, Vector3 fwd)
        {
            var go = new GameObject(name); Undo.RegisterCreatedObjectUndo(go, "Rig pivot");
            go.transform.SetParent(parent, false);
            go.transform.position = world;
            go.transform.rotation = Quaternion.LookRotation(fwd, Vector3.up);
            return go.transform;
        }

        // ───────────────────────── keyframing ─────────────────────────
        private sealed class Ctx
        {
            public Transform visual; public float H; public Spec spec; public bool rightWeapon, hasTail, hasWeapon;
            public Quaternion rigBase; public Vector3 rigPos, hipsPos;
        }
        /// <summary>Pose at time t (seconds): Euler angles per pivot (pivot frame: x = pitch fwd/back, y = yaw, z = roll) + offsets.</summary>
        private sealed class Pose
        {
            public readonly Dictionary<string, Vector3> rot = new Dictionary<string, Vector3>();
            public Vector3 hipsOffset, rigOffset; public Vector3 rigRot;
            public void R(string bone, float x, float y = 0, float z = 0) { rot[bone] = rot.TryGetValue(bone, out var v) ? v + new Vector3(x, y, z) : new Vector3(x, y, z); }
        }

        private static AnimationClip Clip(Ctx ctx, string folder, string name, float length, bool loop, Action<Ctx, Pose, float, float> pose)
        {
            string path = folder + "/" + ctx.spec.id + "_" + name + ".anim";
            AssetDatabase.DeleteAsset(path);
            var clip = new AnimationClip { name = ctx.spec.id + "_" + name, frameRate = 30 };
            string[] bones = { "Torso", "Head", "ArmL", "ArmR", "LegL", "LegR", "Tail", "Weapon" };
            string P(string b) => b == "Torso" ? "Rig/Hips/Torso" : b == "Head" ? "Rig/Hips/Torso/Head" : b.StartsWith("Arm") ? "Rig/Hips/Torso/" + b
                                : b == "Weapon" ? "Rig/Hips/Torso/" + (ctx.rightWeapon ? "ArmR" : "ArmL") + "/Weapon" : "Rig/Hips/" + b;
            int frames = Mathf.Max(2, Mathf.RoundToInt(length * 30f));
            var rc = new Dictionary<string, AnimationCurve[]>();
            foreach (var b in bones.Concat(new[] { "Rig" })) rc[b] = new[] { new AnimationCurve(), new AnimationCurve(), new AnimationCurve(), new AnimationCurve() };
            var hipPos = new[] { new AnimationCurve(), new AnimationCurve(), new AnimationCurve() };
            var rigPos = new[] { new AnimationCurve(), new AnimationCurve(), new AnimationCurve() };
            var last = new Dictionary<string, Quaternion>();
            for (int i = 0; i <= frames; i++)
            {
                float t = length * i / frames;
                var p = new Pose(); pose(ctx, p, t, length);
                foreach (var b in bones.Concat(new[] { "Rig" }))
                {
                    if ((b == "Tail" && !ctx.hasTail) || (b == "Weapon" && !ctx.hasWeapon)) continue;
                    Vector3 e = b == "Rig" ? p.rigRot : p.rot.TryGetValue(b, out var v) ? v : Vector3.zero;
                    Quaternion q = b == "Rig" ? ctx.rigBase * Quaternion.Euler(e) : Quaternion.Euler(e);
                    if (last.TryGetValue(b, out var prev) && Quaternion.Dot(prev, q) < 0) q = new Quaternion(-q.x, -q.y, -q.z, -q.w);
                    last[b] = q;
                    rc[b][0].AddKey(t, q.x); rc[b][1].AddKey(t, q.y); rc[b][2].AddKey(t, q.z); rc[b][3].AddKey(t, q.w);
                }
                // hips live in the creature frame (rig space); the rig itself lives in the rotated model space
                Vector3 hp = ctx.hipsPos + p.hipsOffset * ctx.H; Vector3 rp = ctx.rigPos + ctx.rigBase * (p.rigOffset * ctx.H);
                hipPos[0].AddKey(t, hp.x); hipPos[1].AddKey(t, hp.y); hipPos[2].AddKey(t, hp.z);
                rigPos[0].AddKey(t, rp.x); rigPos[1].AddKey(t, rp.y); rigPos[2].AddKey(t, rp.z);
            }
            string[] q4 = { "m_LocalRotation.x", "m_LocalRotation.y", "m_LocalRotation.z", "m_LocalRotation.w" };
            string[] p3 = { "m_LocalPosition.x", "m_LocalPosition.y", "m_LocalPosition.z" };
            foreach (var kv in rc)
            {
                if ((kv.Key == "Tail" && !ctx.hasTail) || (kv.Key == "Weapon" && !ctx.hasWeapon)) continue;
                string bp = kv.Key == "Rig" ? "Rig" : P(kv.Key);
                for (int k = 0; k < 4; k++) clip.SetCurve(bp, typeof(Transform), q4[k], kv.Value[k]);
            }
            for (int k = 0; k < 3; k++) { clip.SetCurve("Rig/Hips", typeof(Transform), p3[k], hipPos[k]); clip.SetCurve("Rig", typeof(Transform), p3[k], rigPos[k]); }
            clip.EnsureQuaternionContinuity();
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = loop; AnimationUtility.SetAnimationClipSettings(clip, settings);
            AssetDatabase.CreateAsset(clip, path);
            return clip;
        }

        private static float Ease(float x) { x = Mathf.Clamp01(x); return x * x * (3f - 2f * x); }
        private static float EaseOut(float x) { x = Mathf.Clamp01(x); return 1f - (1f - x) * (1f - x) * (1f - x); }

        // ───────────────────────── poses ─────────────────────────
        private static void IdlePose(Ctx c, Pose p, float t, float len)
        {
            float k = 1f + c.spec.heaviness, ph = t / len * Mathf.PI * 2f;
            p.R("Torso", 2.2f * k * Mathf.Sin(ph), 0, 1.2f * Mathf.Sin(ph * 2f + 1f));
            p.R("Head", -1.6f * Mathf.Sin(ph + .6f), 7f * Mathf.Sin(ph), 0);
            p.R("ArmL", -3f * Mathf.Sin(ph + .4f), 0, -4f - 2f * Mathf.Sin(ph));
            p.R("ArmR", -3f * Mathf.Sin(ph + .9f), 0, 4f + 2f * Mathf.Sin(ph));
            p.R("Tail", 0, 18f * Mathf.Sin(ph), 6f * Mathf.Sin(ph * 2f));
            p.hipsOffset = Vector3.up * (.006f * k * Mathf.Sin(ph * 2f));
            if (c.spec.style == AttackStyle.Slash) p.R(c.rightWeapon ? "ArmL" : "ArmR", -35f, 0, 0); // shield held up
        }

        private static void WalkPose(Ctx c, Pose p, float t, float len)
        {
            float ph = t / len * Mathf.PI * 2f, s = Mathf.Sin(ph), heavy = c.spec.heaviness;
            float leg = Mathf.Lerp(32f, 24f, heavy), arm = Mathf.Lerp(26f, 16f, heavy);
            p.R("LegL", -leg * s); p.R("LegR", leg * s);
            p.R("ArmL", arm * s, 0, -6f); p.R("ArmR", -arm * s, 0, 6f);
            if (c.spec.style == AttackStyle.Slash) p.R(c.rightWeapon ? "ArmL" : "ArmR", -30f - arm * s * .7f); // shield forward, less swing
            p.R("Torso", 6f + 2f * Mathf.Sin(ph * 2f), 5f * s, Mathf.Lerp(4f, 8f, heavy) * s);
            p.R("Head", -4f - 2f * Mathf.Sin(ph * 2f), -4f * s, -Mathf.Lerp(3f, 6f, heavy) * s);
            p.R("Tail", 0, 25f * Mathf.Sin(ph + 1f), 0);
            p.hipsOffset = Vector3.up * (Mathf.Lerp(.025f, .04f, heavy) * (0.5f - 0.5f * Mathf.Cos(ph * 2f)));
        }

        private static void AttackPose(Ctx c, Pose p, float t, float len)
        {
            float w = c.spec.windup, st = c.spec.strike;
            // 0 → 1 wind-up, 1 → 2 strike, 2 → 0 recover
            float a, phase;
            if (t < w) { a = Ease(t / w); phase = 0; }
            else if (t < w + st) { a = EaseOut((t - w) / (st * .45f)); phase = 1; }
            else { a = 1f - Ease((t - w - st) / c.spec.recover); phase = 2; }
            string main = c.rightWeapon ? "ArmR" : "ArmL", off = c.rightWeapon ? "ArmL" : "ArmR";
            float sign = c.rightWeapon ? 1f : -1f;
            Pose windup = new Pose(), strike = new Pose();
            switch (c.spec.style)
            {
                case AttackStyle.Stab:
                    windup.R(main, 45f, 0, 10f * sign); windup.R(off, -25f, 0, -10f * sign); windup.R("Torso", -6f, -25f * sign, 0); windup.R("Head", 4f, 15f * sign, 0);
                    windup.hipsOffset = new Vector3(0, -.03f, -.04f);
                    strike.R(main, -88f, 0, -5f * sign); strike.R(off, 25f, 0, -10f * sign); strike.R("Torso", 16f, 28f * sign, 0); strike.R("Head", -8f, -18f * sign, 0);
                    strike.hipsOffset = new Vector3(0, -.05f, .12f); strike.R("LegL", -22f); strike.R("LegR", 18f); break;
                case AttackStyle.Slam:
                    windup.R("ArmL", -165f, 0, -12f); windup.R("ArmR", -165f, 0, 12f); windup.R("Torso", -14f); windup.R("Head", -10f); windup.hipsOffset = new Vector3(0, .03f, -.03f);
                    strike.R("ArmL", -55f, 0, -6f); strike.R("ArmR", -55f, 0, 6f); strike.R("Torso", 28f); strike.R("Head", 12f); strike.hipsOffset = new Vector3(0, -.06f, .06f);
                    strike.R("LegL", -12f); strike.R("LegR", -12f); break;
                case AttackStyle.Slash:
                    windup.R(main, -150f, 0, 25f * sign); windup.R(off, -40f); windup.R("Torso", -8f, -22f * sign, 0); windup.R("Head", -6f, 10f * sign, 0);
                    strike.R(main, -35f, 0, -35f * sign); strike.R(off, -45f); strike.R("Torso", 14f, 26f * sign, 0); strike.R("Head", 6f, -12f * sign, 0);
                    strike.hipsOffset = new Vector3(0, -.03f, .08f); strike.R("LegL", -18f); strike.R("LegR", 14f); break;
                case AttackStyle.Smash:
                    windup.R("ArmL", -170f, 0, -8f); windup.R("ArmR", -170f, 0, 8f); windup.R("Weapon", 140f); strike.R("Weapon", 145f); windup.R("Torso", -18f, -10f * sign, 0); windup.R("Head", -12f); windup.hipsOffset = new Vector3(0, .02f, -.05f);
                    strike.R("ArmL", -25f, 0, -5f); strike.R("ArmR", -25f, 0, 5f); strike.R("Torso", 34f, 8f * sign, 0); strike.R("Head", 14f); strike.hipsOffset = new Vector3(0, -.08f, .1f);
                    strike.R("LegL", -20f); strike.R("LegR", 10f); break;
            }
            var neutral = new Pose();
            if (c.spec.style == AttackStyle.Slash) neutral.R(off, -35f);   // shield guard, same as idle/walk
            Pose from = phase == 0 ? neutral : windup, to = phase == 0 ? windup : strike;
            if (phase == 2) { from = neutral; to = strike; }
            foreach (var b in from.rot.Keys.Union(to.rot.Keys))
            {
                Vector3 f = from.rot.TryGetValue(b, out var v1) ? v1 : Vector3.zero, g = to.rot.TryGetValue(b, out var v2) ? v2 : Vector3.zero;
                Vector3 e = Vector3.Lerp(f, g, a); p.R(b, e.x, e.y, e.z);
            }
            p.hipsOffset = Vector3.Lerp(from.hipsOffset, to.hipsOffset, a);
            if (phase == 0) { float tremble = Mathf.Sin(t * 60f) * 1.5f * Ease(t / w); p.R("Torso", tremble * .4f, 0, tremble * .4f); p.R(main, tremble); }
        }

        private static void HitPose(Ctx c, Pose p, float t, float len)
        {
            float k = t < .07f ? EaseOut(t / .07f) : 1f - Ease((t - .07f) / (len - .07f));
            p.R("Torso", -20f * k, 0, 6f * k); p.R("Head", -18f * k, 0, -8f * k);
            p.R("ArmL", 28f * k, 0, -18f * k); p.R("ArmR", 28f * k, 0, 18f * k);
            p.R("Tail", 0, 0, 25f * k);
            p.hipsOffset = new Vector3(0, -.02f * k, -.06f * k);
        }

        private static void StaggerPose(Ctx c, Pose p, float t, float len)
        {
            float ph = t / len * Mathf.PI * 2f;
            p.R("Torso", -10f + 3f * Mathf.Sin(ph * 2f), 6f * Mathf.Sin(ph), 7f * Mathf.Sin(ph));
            p.R("Head", 12f, 14f * Mathf.Sin(ph + 1f), 12f * Mathf.Sin(ph));
            p.R("ArmL", 15f, 0, -20f - 6f * Mathf.Sin(ph)); p.R("ArmR", 15f, 0, 20f + 6f * Mathf.Sin(ph));
            p.R("LegL", -8f * Mathf.Sin(ph)); p.R("LegR", 8f * Mathf.Sin(ph));
            p.hipsOffset = new Vector3(0, -.03f, -.02f);
        }

        private static void DeathPose(Ctx c, Pose p, float t, float len)
        {
            // stumble back, then topple backwards around the feet
            float stumble = Ease(t / .3f), fall = t < .3f ? 0f : Mathf.Pow(Mathf.Clamp01((t - .3f) / .6f), 2f);
            float bounce = t > .9f ? Mathf.Sin(Mathf.Clamp01((t - .9f) / .35f) * Mathf.PI) * 4f : 0f;
            p.rigRot = new Vector3(-86f * fall + bounce, 0, 0);
            p.rigOffset = new Vector3(0, 0, -.12f * stumble);
            p.R("Torso", -14f * stumble + 10f * fall, 0, 0);
            p.R("Head", -20f * stumble + 35f * fall, 15f * fall, 0);
            p.R("ArmL", -70f * fall + 20f * stumble, 0, -35f * fall); p.R("ArmR", -60f * fall + 20f * stumble, 0, 40f * fall);
            p.R("LegL", -30f * fall); p.R("LegR", -8f * fall);
            p.R("Tail", 0, 0, 30f * fall);
        }
    }
}
