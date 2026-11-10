#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace Goblingame.TestScene.Dev
{
    /// <summary>
    /// Automated play-mode check (editor only). Started by Goblingame > Run Playtest.
    /// Drives the goblin through idle, lock-on, punch combo, enemy attack, block/parry,
    /// headbutt, kills, death/respawn and the bestiary; collects console errors and
    /// screenshots into Temp/Playtest, writes Temp/Playtest/report.txt, then exits Play.
    /// </summary>
    public sealed class GoblinPlaytest : MonoBehaviour
    {
        private static string Root => Directory.GetParent(Application.dataPath).FullName;
        private static string Flag => Path.Combine(Root, "Temp/GoblinPlaytest.run");
        private static string OutDir => Path.Combine(Root, "Temp/Playtest");

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (!File.Exists(Flag)) return;
            File.Delete(Flag);
            new GameObject("Goblin Playtest").AddComponent<GoblinPlaytest>();
        }

        private readonly StringBuilder report = new StringBuilder();
        private readonly List<string> errors = new List<string>();
        private int pass, fail;
        private GoblinCombat combat; private ThirdPersonFollow view; private CombatHUD hud; private Animator anim;

        private void Awake()
        {
            Application.logMessageReceived += OnLog;
            if (Directory.Exists(OutDir)) foreach (var f in Directory.GetFiles(OutDir)) File.Delete(f);
            Directory.CreateDirectory(OutDir);
        }
        private void OnDestroy() { Application.logMessageReceived -= OnLog; }
        private void OnLog(string message, string stack, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                errors.Add(type + ": " + message + "\n" + stack);
        }

        private void Check(bool ok, string what) { if (ok) pass++; else fail++; Line((ok ? "PASS " : "FAIL ") + what); }
        private void Line(string s) { report.AppendLine(s); Debug.Log("[Playtest] " + s); }

        private static object Get(object o, string field) =>
            o.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)?.GetValue(o);
        private static void Set(object o, string field, object value) =>
            o.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)?.SetValue(o, value);
        private static object Call(object o, string method, params object[] args) =>
            o.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)?.Invoke(o, args);

        /// <summary>Share of pixels that differ from the background (corner) colour — i.e. how much of the subject is in frame.</summary>
        private static float Brightness(Texture2D t)
        {
            var px = t.GetPixels(); Color bg = px[0]; int hit = 0;
            foreach (var c in px) if (Mathf.Abs(c.r - bg.r) + Mathf.Abs(c.g - bg.g) + Mathf.Abs(c.b - bg.b) > 0.08f) hit++;
            return hit / (float)px.Length;
        }

        private IEnumerator Shot(string name)
        {
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(Path.Combine(OutDir, name + ".png"));
            yield return null; yield return null;
        }

        private void Teleport(Vector3 position, Vector3 lookAt)
        {
            var cc = combat.GetComponent<CharacterController>();
            cc.enabled = false;
            Vector3 dir = lookAt - position; dir.y = 0;
            combat.transform.SetPositionAndRotation(position, Quaternion.LookRotation(dir));
            cc.enabled = true;
        }

        private IEnumerator Start()
        {
            yield return new WaitForSecondsRealtime(1.5f);
            combat = FindFirstObjectByType<GoblinCombat>();
            view = Camera.main != null ? Camera.main.GetComponent<ThirdPersonFollow>() : null;
            hud = FindFirstObjectByType<CombatHUD>();
            Check(combat != null, "Player GoblinCombat present");
            Check(view != null, "Camera ThirdPersonFollow present");
            Check(hud != null, "CombatHUD present");
            if (combat == null || view == null) { Finish(); yield break; }
            anim = (Animator)Get(combat, "animator");
            Check((bool)Get(combat, "ready"), "Combat ready (Punch state found)");
            Check((bool)Get(combat, "hasIdleTime"), "Animator has IdleTime parameter");
            Check((bool)Get(combat, "hasBlockTime"), "Animator has BlockTime parameter");
            Check((bool)Get(combat, "hasMoveSpeed"), "Walk/Run playback follows movement speed (MoveSpeed parameter)");
            Check(Mathf.Abs(anim.GetFloat("MoveSpeed") - 0.55f) < 0.6f, "MoveSpeed driven (" + anim.GetFloat("MoveSpeed").ToString("0.00") + ")");
            var enemies = new List<EnemyActor>(EnemyActor.All);
            Check(enemies.Count == 4, "Four enemies registered (" + enemies.Count + ")");
            foreach (var e in enemies)
            {
                var rs = e.visual != null ? e.visual.GetComponentsInChildren<Renderer>() : new Renderer[0];
                bool model = e.visual != null && e.visual.name == "Visual" && rs.Length > 3;
                Bounds b = rs.Length > 0 ? rs[0].bounds : new Bounds();
                foreach (var r in rs) b.Encapsulate(r.bounds);
                Check(model, e.displayName + " uses a real model (" + rs.Length + " parts)");
                Check(b.size.y > 1.2f && b.size.y < 4.5f, e.displayName + " is a sensible size (" + b.size.y.ToString("0.00") + " m)");
                // a model lying on its side measures ~0.3; swinging arms / weapons while walking stay above 0.6
                bool upright = b.size.y >= 0.6f * Mathf.Max(b.size.x, b.size.z);
                Check(upright, e.displayName + " stands upright (h " + b.size.y.ToString("0.00") + " vs w " + Mathf.Max(b.size.x, b.size.z).ToString("0.00") + ", state " + e.Status + ")");
                if (!upright)
                {
                    Vector3 c0 = e.transform.position; Renderer far = null; float fd = 0;
                    foreach (var r in rs) { var d = r.bounds.center - c0; d.y = 0; if (d.magnitude > fd) { fd = d.magnitude; far = r; } }
                    Line("info: farthest part " + far.name + " at " + fd.ToString("0.00") + " m, parent " + far.transform.parent.name + " localRot " + far.transform.parent.localEulerAngles.ToString("0"));
                }
                Check(Mathf.Abs(b.min.y - e.transform.position.y) < 0.3f, e.displayName + " stands on the ground (feet at " + (b.min.y - e.transform.position.y).ToString("0.00") + ")");
                var ea = e.visual != null ? e.visual.GetComponent<Animator>() : null;
                Check(ea != null && ea.runtimeAnimatorController != null && ea.runtimeAnimatorController.animationClips.Length >= 6,
                    e.displayName + " has keyframed clips (" + (ea != null && ea.runtimeAnimatorController != null ? ea.runtimeAnimatorController.animationClips.Length : 0) + ")");
                foreach (var bone in new[] { "Rig/Hips/Torso/Head", "Rig/Hips/Torso/ArmL", "Rig/Hips/Torso/ArmR", "Rig/Hips/LegL", "Rig/Hips/LegR" })
                {
                    var t = e.visual != null ? e.visual.Find(bone) : null;
                    Check(t != null && t.childCount > 0, e.displayName + " rig " + bone.Substring(bone.LastIndexOf('/') + 1) + " has " + (t != null ? t.childCount : 0) + " parts");
                }
                var bar = e.transform.Find("Health");
                Check(bar != null && bar.gameObject.activeInHierarchy && bar.GetComponent<Canvas>().enabled && bar.position.y > b.max.y,
                    e.displayName + " health bar above head (" + (bar != null ? bar.position.y.ToString("0.00") : "none") + " vs head " + b.max.y.ToString("0.00") + ")");
            }
            if (hud != null && hud.targetPortrait != null)
            {
                var goblinPic = hud.transform.Find("Goblin Status/Portrait Frame/Portrait");
                var tex = goblinPic != null ? goblinPic.GetComponent<UnityEngine.UI.RawImage>().texture as Texture2D : null;
                Check(tex != null && Brightness(tex) > 0.12f, "Goblin portrait rendered (brightness " + (tex != null ? Brightness(tex).ToString("0.00") : "none") + ")");
                foreach (var e in enemies)
                {
                    var card = hud.bestiary != null ? hud.bestiary.transform.Find(e.id + " Card/Portrait") : null;
                    var ct = card != null ? card.GetComponent<UnityEngine.UI.RawImage>().texture as Texture2D : null;
                    Check(ct != null && Brightness(ct) > 0.12f, e.displayName + " bestiary card rendered (" + (ct != null ? Brightness(ct).ToString("0.00") : "none") + ")");
                }
            }
            Vector3 spawn = combat.transform.position;
            yield return Shot("01_start");

            // ── idle: calm, gesture only when scheduled (away from enemies)
            Teleport(spawn + Vector3.back * 9f, spawn + Vector3.back * 20f);
            yield return new WaitForSecondsRealtime(0.5f);
            Line("info: InCombat at quiet spot = " + combat.InCombat);
            float maxIdle = 0f;
            for (float t = 0; t < 2.5f; t += Time.unscaledDeltaTime) { maxIdle = Mathf.Max(maxIdle, anim.GetFloat("IdleTime")); yield return null; }
            Check(maxIdle < 0.12f, "Calm idle stays in the quiet part of the clip (max " + maxIdle.ToString("0.00") + ")");
            // enemies that already spotted the goblin may follow it here; test the timer itself
            Set(combat, "gestureInCombat", true);
            Set(combat, "nextGesture", Time.time);
            float gesture = 0f;
            for (float t = 0; t < 3.5f; t += Time.unscaledDeltaTime) { gesture = Mathf.Max(gesture, anim.GetFloat("IdleTime")); yield return null; }
            Set(combat, "gestureInCombat", false);
            Check(gesture > 0.5f, "Scheduled gesture plays through (reached " + gesture.ToString("0.00") + ")");
            float next = (float)Get(combat, "nextGesture") - Time.time;
            Check(next > 5f, "Next gesture rescheduled " + next.ToString("0.0") + " s later");

            // ── lock-on
            EnemyActor rat = enemies.Find(e => e.id == "RatRogue") ?? enemies[0];
            Vector3 toRat = rat.transform.position - spawn; toRat.y = 0;
            Teleport(rat.transform.position - toRat.normalized * 3.0f, rat.transform.position);
            yield return new WaitForSecondsRealtime(1.2f);
            Check(rat.IsEngaged, rat.displayName + " engages the goblin");
            Check(view.IsLocked && view.LockTarget == rat, "Camera auto-focuses " + rat.displayName + " (locked=" + (view.LockTarget != null ? view.LockTarget.displayName : "none") + ")");
            Check(combat.InCombat, "Goblin enters combat automatically");
            yield return Shot("02_lock_on");

            // ── punch combo (start it while the rat is not mid-attack, and keep it from attacking)
            for (float t = 0; t < 4f && (rat.IsTelegraphing || rat.Status == "АТАКУЕТ"); t += Time.unscaledDeltaTime) yield return null;
            // duel spot away from the training dummy and other enemies
            Vector3 duel = spawn + Vector3.back * 6f;
            Set(rat, "home", duel);
            var ratCC = rat.GetComponent<CharacterController>(); ratCC.enabled = false; rat.transform.position = duel; ratCC.enabled = true;
            Set(rat, "cooldown", 6f);
            Teleport(duel + Vector3.back * 1.6f, duel);
            yield return new WaitForSecondsRealtime(0.3f);
            float hp0 = rat.Health.Health;
            float playerBefore = combat.Health;
            Set(combat, "comboIndex", -1); Call(combat, "NextPunch");
            float stuck = 0f; bool shot = false;
            for (float t = 0; t < 3.5f; t += Time.unscaledDeltaTime)
            {
                if (t < 2.0f) Set(combat, "queuedPunch", true);
                if (!shot && t > 0.8f) { shot = true; yield return Shot("03_combo"); }
                if (combat.IsAttacking) stuck = t;
                yield return null;
            }
            float dealt = hp0 - rat.Health.Health;
            Check(dealt >= 40f, "4-hit combo damages the rat (" + dealt + " dmg, player took " + (playerBefore - combat.Health) + ")");
            Check(!combat.IsAttacking, "Attack state ends (last attacking at " + stuck.ToString("0.00") + " s)");
            Check(Time.timeScale == 1f, "Hit-stop restored time scale");

            // ── enemy attacks the goblin
            float php = combat.Health; bool telegraphShot = false; float waited = 0f;
            while (combat.Health >= php && waited < 8f)
            {
                if (!telegraphShot && rat.IsTelegraphing && rat.TelegraphProgress > .5f) { telegraphShot = true; yield return Shot("04_telegraph"); }
                waited += Time.unscaledDeltaTime; yield return null;
            }
            Check(telegraphShot, "Enemy telegraphs its attack");
            Check(combat.Health < php, "Enemy hit lands on the goblin (" + (php - combat.Health) + " dmg after " + waited.ToString("0.0") + " s)");

            // ── block / parry logic
            Set(combat, "blocking", true); Set(combat, "blockStart", Time.time);
            var parry = combat.TakeDamage(10f, combat.transform.position + combat.transform.forward);
            Set(combat, "blockStart", Time.time - 1f);
            float before = combat.Health;
            var blocked = combat.TakeDamage(20f, combat.transform.position + combat.transform.forward);
            Set(combat, "blocking", false);
            Check(parry == HitResult.Parried, "Early block parries (" + parry + ")");
            Check(blocked == HitResult.Blocked && before - combat.Health <= 3.01f, "Held block reduces damage (" + blocked + ", -" + (before - combat.Health) + ")");

            // ── headbutt
            yield return new WaitForSecondsRealtime(0.6f);
            Teleport(rat.transform.position - (rat.transform.position - combat.transform.position).normalized * 1.5f, rat.transform.position);
            float hb = rat.Health.Health;
            Call(combat, "StartHeadbutt");
            string statusAfter = "";
            for (float t = 0; t < 1.2f; t += Time.unscaledDeltaTime) { if (hb - rat.Health.Health > 0 && statusAfter == "") statusAfter = rat.Status; yield return null; }
            Check(hb - rat.Health.Health >= 20f || rat.Health.IsDead, "Headbutt damages (" + (hb - rat.Health.Health) + ")");
            Check(statusAfter == "ОГЛУШЁН" || rat.Health.IsDead, "Headbutt staggers the rat (" + statusAfter + ")");

            // ── kill → experience
            var progression = combat.GetComponent<PlayerProgression>();
            int xp0 = progression != null ? progression.Experience + progression.Level * 1000 : 0;
            while (!rat.Health.IsDead) { rat.Health.TakeHit(new HitInfo { damage = 30, source = combat.transform.position }); yield return new WaitForSecondsRealtime(.15f); }
            yield return new WaitForSecondsRealtime(1.2f);
            Check(progression == null || progression.Experience + progression.Level * 1000 > xp0, "Kill grants experience");
            var ratRig = rat.visual.Find("Rig");
            if (ratRig != null)
            {
                float fall = Vector3.Angle(ratRig.up, Vector3.up);
                Check(fall > 50f, "Death animation topples the rat (" + fall.ToString("0") + "°)");
            }
            Check(view.LockTarget != rat, "Camera releases the dead target");
            yield return Shot("05_rat_down");

            // ── every other enemy: notice, walk, wind up and strike (screenshot of the swing)
            foreach (var e in enemies)
            {
                if (e == rat) continue;
                Set(combat, "health", combat.MaxHealth);
                Vector3 front = e.transform.position + e.transform.forward * 4.5f;
                Teleport(front, e.transform.position);
                yield return new WaitForSecondsRealtime(1.3f);
                yield return Shot("06_" + e.id);
                Check(e.IsEngaged, e.displayName + " notices the goblin");
                var torso = e.visual.Find("Rig/Hips/Torso");
                var armL = e.visual.Find("Rig/Hips/Torso/ArmL"); var armR = e.visual.Find("Rig/Hips/Torso/ArmR");
                Quaternion l0 = armL != null ? armL.localRotation : Quaternion.identity, r0 = armR != null ? armR.localRotation : Quaternion.identity;
                float waited2 = 0f; float maxArm = 0f; bool swingShot = false;
                while (waited2 < 7f)
                {
                    if (combat.IsDead) { Call(combat, "Respawn"); Teleport(front, e.transform.position); }
                    if (armL != null) maxArm = Mathf.Max(maxArm, Quaternion.Angle(l0, armL.localRotation), Quaternion.Angle(r0, armR.localRotation));
                    if (!swingShot && e.Status == "АТАКУЕТ") { swingShot = true; yield return Shot("09_strike_" + e.id); }
                    if (swingShot && maxArm > 40f) break;
                    waited2 += Time.unscaledDeltaTime; yield return null;
                }
                Check(swingShot, e.displayName + " attacks");
                Check(maxArm > 40f, e.displayName + " swings its arms in the attack (" + maxArm.ToString("0") + "°)");
            }

            // ── death and respawn
            combat.TakeDamage(9999f, combat.transform.position + combat.transform.forward);
            yield return new WaitForSecondsRealtime(1.5f);
            Check(combat.IsDead, "Goblin can die");
            yield return Shot("07_dead");
            Call(combat, "Respawn");
            yield return new WaitForSecondsRealtime(1f);
            Check(!combat.IsDead && combat.Health >= combat.MaxHealth - .1f, "Respawn restores health");
            Check((combat.transform.position - spawn).magnitude < 0.5f, "Respawn returns to spawn point");

            // ── bestiary
            if (hud != null && hud.bestiary != null)
            {
                hud.bestiary.SetActive(true);
                yield return new WaitForSecondsRealtime(.3f);
                yield return Shot("08_bestiary");
                hud.bestiary.SetActive(false);
            }
            yield return PoseGallery(enemies);
            Finish();
        }

        /// <summary>Close-up 3/4 shots of every enemy clip at key phases (for visual review).</summary>
        private IEnumerator PoseGallery(List<EnemyActor> enemies)
        {
            view.enabled = false;
            if (hud != null) hud.gameObject.SetActive(false);
            combat.gameObject.SetActive(false);
            foreach (var e in enemies) { e.enabled = false; var bar = e.transform.Find("Health"); if (bar != null) bar.gameObject.SetActive(false); var al = e.transform.Find("Alert"); if (al != null) al.gameObject.SetActive(false); }
            Vector3 stage = combat.transform.position + Vector3.back * 12f;
            foreach (var e in enemies)
            {
                var others = new List<EnemyActor>(enemies); others.Remove(e);
                foreach (var o in others) o.gameObject.SetActive(false);
                var cc = e.GetComponent<CharacterController>(); cc.enabled = false;
                e.transform.SetPositionAndRotation(stage, Quaternion.identity);
                var anim = e.visual.GetComponent<Animator>(); if (anim != null) anim.enabled = false;
                e.visual.localScale = (Vector3)Get(e, "restScale"); e.visual.localRotation = (Quaternion)Get(e, "restRotation"); e.visual.localPosition = (Vector3)Get(e, "restPosition");
                var clips = anim != null && anim.runtimeAnimatorController != null ? anim.runtimeAnimatorController.animationClips : new AnimationClip[0];
                float h = e.GetComponent<CharacterController>().height;
                var cam = Camera.main.transform;
                cam.position = stage + new Vector3(h * 1.25f, h * .75f, h * 1.6f); cam.LookAt(stage + Vector3.up * h * .55f);
                foreach (var (clipName, frac, label) in new[] { ("Idle", 0f, "idle"), ("Walk", .25f, "walk"), ("Attack", -1f, "windup"), ("Attack", -2f, "strike"), ("Hit", .2f, "hit"), ("Death", 1f, "death") })
                {
                    var clip = System.Array.Find(clips, c => c.name.EndsWith("_" + clipName));
                    if (clip == null) continue;
                    float t = frac >= 0 ? frac * clip.length : frac == -1f ? e.windup * clip.length / (e.windup + e.strikeTime + e.recover)
                                                                : (e.windup + e.strikeTime * .5f) * clip.length / (e.windup + e.strikeTime + e.recover);
                    clip.SampleAnimation(e.visual.gameObject, Mathf.Min(t, clip.length - .001f));
                    yield return Shot("10_" + e.id + "_" + label);
                }
                foreach (var o in others) o.gameObject.SetActive(true);
                e.gameObject.SetActive(false);
            }
        }

        private void Finish()
        {
            Line("RESULT: " + pass + " passed, " + fail + " failed, " + errors.Count + " console errors");
            foreach (var e in errors) report.AppendLine("ERROR " + e);
            File.WriteAllText(Path.Combine(OutDir, "report.txt"), report.ToString());
            UnityEditor.EditorApplication.ExitPlaymode();
        }
    }
}
#endif
