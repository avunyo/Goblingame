#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Linq;
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
            Mode = File.ReadAllText(Flag).Trim();
            File.Delete(Flag);
            new GameObject("Goblin Playtest").AddComponent<GoblinPlaytest>();
        }

        private static string Mode = "run";
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
        /// <summary>Where the model's face really points: perpendicular to the line between its eyes.</summary>
        private static Vector3 EyeForward(EnemyActor e)
        {
            var eyes = new List<Vector3>();
            foreach (var r in e.visual.GetComponentsInChildren<Renderer>())
            {
                string n = r.name.ToLowerInvariant();
                if ((n.StartsWith("eye") || n.Contains(" eye")) && !n.Contains("socket")) eyes.Add(r.bounds.center);
            }
            if (eyes.Count < 2) return e.transform.forward;
            Vector3 a = eyes[0], b = eyes[1]; float far = 0;
            for (int i = 0; i < eyes.Count; i++) for (int j = i + 1; j < eyes.Count; j++) { var d = eyes[j] - eyes[i]; d.y = 0; if (d.sqrMagnitude > far) { far = d.sqrMagnitude; a = eyes[i]; b = eyes[j]; } }
            Vector3 across = b - a; across.y = 0;
            Vector3 face = Vector3.Cross(across.normalized, Vector3.up);
            Vector3 mid = (a + b) * .5f - e.transform.position; mid.y = 0;
            if (Vector3.Dot(face, mid) < 0) face = -face;
            return face;
        }

        private static Vector3 Flat(Vector3 v) { v.y = 0; return v; }

        /// <summary>An open, flat spot about `dist` from `from` (not inside rocks / trees of the swamp).</summary>
        private Vector3 FreeSpot(Vector3 from, float dist)
        {
            var cc = combat.GetComponent<CharacterController>(); bool was = cc.enabled; cc.enabled = false;
            Vector3 best = from + Vector3.back * dist;
            for (int i = 0; i < 16; i++)
            {
                Vector3 p = from + Quaternion.Euler(0, 180f + i * 22.5f, 0) * Vector3.forward * dist;
                bool blocked = Physics.CheckCapsule(p + Vector3.up * .5f, p + Vector3.up * 1.6f, .6f, ~0, QueryTriggerInteraction.Ignore) &&
                               Physics.OverlapCapsule(p + Vector3.up * .5f, p + Vector3.up * 1.6f, .6f).Any(c => !(c is TerrainCollider) && c.name != "Ground");
                if (!blocked) { best = p; break; }
            }
            cc.enabled = was; return best;
        }

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
            if (Mode == "stance") { yield return StanceGallery(); Finish(); yield break; }
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
                Check(Mathf.Abs(b.min.y - e.transform.position.y) < 0.5f,   // a low-swinging hammer may dip a little
                     e.displayName + " stands on the ground (feet at " + (b.min.y - e.transform.position.y).ToString("0.00") + ")");
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
                    Check(ct != null && Brightness(ct) > 0.08f, e.displayName + " bestiary card rendered (" + (ct != null ? Brightness(ct).ToString("0.00") : "none") + ")");
                }
            }
            Vector3 spawn = combat.transform.position;
            yield return Shot("01_start");

            // ── idle: calm, gesture only when scheduled (away from enemies)
            Teleport(FreeSpot(spawn, 9f), FreeSpot(spawn, 9f) + Vector3.back * 10f);
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

            // ── jump
            var mv = combat.GetComponent<GoblinMovement>();
            float y0 = combat.transform.position.y, maxY = y0; bool wasAir = false, apexShot = false;
            mv.RequestJump();
            for (float t = 0; t < 1.6f; t += Time.unscaledDeltaTime)
            {
                maxY = Mathf.Max(maxY, combat.transform.position.y);
                if (mv.IsAirborne) wasAir = true;
                if (!apexShot && mv.IsAirborne && mv.VerticalSpeed < 0.6f) { apexShot = true; yield return Shot("12_jump_apex"); }
                yield return null;
            }
            Check(wasAir, "Goblin jumps (Space)");
            Check(maxY - y0 > 0.8f && maxY - y0 < 1.6f, "Jump height " + (maxY - y0).ToString("0.00") + " m");
            Check(!mv.IsAirborne && Mathf.Abs(combat.transform.position.y - y0) < 0.1f, "Goblin lands back on the ground");

            // ── dodges: roll from standing, slide from a sprint; both dodge an incoming hit
            {
                var mvd = combat.GetComponent<GoblinMovement>();
                yield return new WaitForSecondsRealtime(0.4f);
                // roll toward open ground (an enemy that already found the goblin may be standing right in front)
                var ccr = combat.GetComponent<CharacterController>(); ccr.enabled = false;
                for (int i = 0; i < 12; i++)
                {
                    Vector3 d = Quaternion.Euler(0, i * 30f, 0) * Vector3.forward;
                    if (!Physics.SphereCast(combat.transform.position + Vector3.up * .8f, .45f, d, out _, 3.5f, ~0, QueryTriggerInteraction.Ignore)) { combat.transform.rotation = Quaternion.LookRotation(d); break; }
                }
                ccr.enabled = true;
                Vector3 p0 = combat.transform.position;
                combat.RequestDodge();
                yield return new WaitForSecondsRealtime(0.25f);
                Check(combat.IsDodging, "R makes the goblin roll");
                var dodged = combat.TakeDamage(10f, combat.transform.position + combat.transform.forward);
                Check(dodged == HitResult.Ignored, "Mid-roll hits are dodged (" + dodged + ")");
                var ccd = combat.GetComponent<CharacterController>();
                for (float t = 0, tick = 0; t < 1.2f && combat.IsDodging; t += Time.unscaledDeltaTime)
                {
                    if (t >= tick) { tick += .1f; Line("info: roll t=" + t.ToString("0.00") + " pos=" + combat.transform.position.ToString("0.00") + " dashing=" + mvd.IsDashing + " prog=" + mvd.DashProgress.ToString("0.00") + " speed=" + mvd.CurrentSpeed.ToString("0.0") + " ccVel=" + ccd.velocity.magnitude.ToString("0.0") + " grounded=" + ccd.isGrounded + " timeScale=" + Time.timeScale); }
                    yield return null;
                }
                Check(!combat.IsDodging, "Roll ends");
                float rollDist = Vector3.Distance(Flat(p0), Flat(combat.transform.position));
                Check(rollDist > 1.5f && rollDist < 5.5f, "Roll travels " + rollDist.ToString("0.0") + " m");
                yield return new WaitForSecondsRealtime(0.5f);
                // sprint toward open ground too ("forward" follows the camera)
                Call(view, "Release");
                for (int i = 0; i < 12; i++)
                {
                    Vector3 d = Quaternion.Euler(0, i * 30f, 0) * Vector3.forward;
                    if (!Physics.SphereCast(combat.transform.position + Vector3.up * .8f, .45f, d, out _, 8f, ~0, QueryTriggerInteraction.Ignore)) { Set(view, "yaw", i * 30f); break; }
                }
                mvd.simulatedInput = Vector2.up;
                for (float t = 0; t < 0.8f; t += Time.unscaledDeltaTime) yield return null;
                Line("info: sprint check running=" + mvd.IsRunning + " speed=" + mvd.CurrentSpeed.ToString("0.0") + " moving=" + mvd.IsMoving + " locked=" + combat.MovementLocked + " pos=" + combat.transform.position.ToString("0.00"));
                p0 = combat.transform.position;
                combat.RequestDodge();
                yield return new WaitForSecondsRealtime(0.3f);
                bool sliding = combat.IsDodging && (string)Get(combat, "dodge").ToString() == "Slide";
                Check(sliding, "R while sprinting slides (" + Get(combat, "dodge") + ")");
                mvd.simulatedInput = Vector2.zero;
                for (float t = 0; t < 1.2f && combat.IsDodging; t += Time.unscaledDeltaTime) yield return null;
                float slideDist = Vector3.Distance(Flat(p0), Flat(combat.transform.position));
                Check(slideDist > 2.5f, "Slide travels " + slideDist.ToString("0.0") + " m");
                yield return new WaitForSecondsRealtime(0.5f);
            }

            // ── smooth heading: switching from "forward" to "right" turns through an arc, no 45° snaps
            {
                var mvh = combat.GetComponent<GoblinMovement>();
                Call(view, "Release");   // a free camera, so "right" stays put while the goblin turns
                Line("info: camera locked during heading test = " + view.IsLocked);
                mvh.simulatedInput = Vector2.up;
                for (float t = 0; t < 0.6f; t += Time.unscaledDeltaTime) yield return null;
                float prev = combat.transform.eulerAngles.y, maxStep = 0f;
                Vector3 camRight = Quaternion.Euler(0, 90, 0) * view.PlanarForward;
                float wantYaw = Mathf.Atan2(camRight.x, camRight.z) * Mathf.Rad2Deg;
                mvh.simulatedInput = Vector2.right;
                float reached = -1f;
                for (float t = 0; t < 0.9f; t += Time.unscaledDeltaTime)
                {
                    yield return null;
                    float y = combat.transform.eulerAngles.y;
                    Vector3 right = Quaternion.Euler(0, 90, 0) * view.PlanarForward;   // the heading the game asks for right now
                    wantYaw = Mathf.Atan2(right.x, right.z) * Mathf.Rad2Deg;
                    maxStep = Mathf.Max(maxStep, Mathf.Abs(Mathf.DeltaAngle(prev, y)) / Mathf.Max(Time.deltaTime, 0.001f) * (1f / 60f));
                    prev = y;
                    if (reached < 0 && Mathf.Abs(Mathf.DeltaAngle(y, wantYaw)) < 8f) reached = t;
                }
                mvh.simulatedInput = Vector2.zero;
                Check(maxStep < 22f, "Goblin turns smoothly (max " + maxStep.ToString("0") + "° per 60 fps frame)");
                Check(reached > 0.08f && reached < 0.6f, "…and still reaches the new heading quickly (" + reached.ToString("0.00") + " s, final error " +
                      Mathf.Abs(Mathf.DeltaAngle(combat.transform.eulerAngles.y, wantYaw)).ToString("0") + "°)");
                Set(view, "manualRelease", false);
                yield return new WaitForSecondsRealtime(0.4f);
            }

            // ── lock-on
            EnemyActor rat = enemies.Find(e => e.id == "RatRogue") ?? enemies[0];
            Vector3 toRat = rat.transform.position - spawn; toRat.y = 0;
            Teleport(rat.transform.position - toRat.normalized * 3.0f, rat.transform.position);
            Set(view, "yaw", Mathf.Atan2(toRat.x, toRat.z) * Mathf.Rad2Deg);   // the player is looking at the rat
            yield return new WaitForSecondsRealtime(1.2f);
            Check(rat.IsEngaged, rat.displayName + " engages the goblin");
            Check(view.IsLocked && view.LockTarget.IsEngaged, "Camera auto-focuses the engaged enemy in view (" + (view.LockTarget != null ? view.LockTarget.displayName : "none") + ")");
            Check(combat.InCombat, "Goblin enters combat automatically");
            yield return Shot("02_lock_on");

            // ── pulling the mouse while focused really moves the view, past the limit it tears free without snapping back
            float yawLocked = (float)Get(view, "yaw");
            Set(view, "lockOffset", new Vector2(25f, 0f));
            yield return new WaitForSecondsRealtime(0.25f);
            float yawPulled = (float)Get(view, "yaw");
            Check(view.IsLocked && Mathf.Abs(Mathf.DeltaAngle(yawLocked, yawPulled)) > 8f, "Mouse pulls the focused camera off the target (" + Mathf.DeltaAngle(yawLocked, yawPulled).ToString("0") + "°)");
            Set(view, "lockOffset", new Vector2(40f, 0f));
            yield return null; yield return null;
            float yawFree = (float)Get(view, "yaw");
            Check(!view.IsLocked, "Pulling further breaks the focus");
            yield return new WaitForSecondsRealtime(0.3f);
            Check(Mathf.Abs(Mathf.DeltaAngle(yawFree, (float)Get(view, "yaw"))) < 3f, "After breaking free the camera stays where it was turned (no snap back)");
            Set(view, "manualRelease", false);
            yield return new WaitForSecondsRealtime(0.4f);

            // ── focus breaks on a quick camera pull and on zooming far out, and stays free during the fight
            Set(view, "lockFlick", new Vector2(30f, 0f));
            yield return new WaitForSecondsRealtime(0.2f);
            Check(!view.IsLocked, "Quick camera pull drops the focus");
            yield return new WaitForSecondsRealtime(1.5f);
            Check(!view.IsLocked, "Camera stays free while the fight goes on");
            Set(view, "manualRelease", false);
            yield return new WaitForSecondsRealtime(0.3f);
            Check(view.IsLocked, "Focus can be re-acquired (F)");
            float zoom0 = (float)Get(view, "targetDistance");
            Set(view, "targetDistance", 8.6f);
            yield return new WaitForSecondsRealtime(0.2f);
            Check(!view.IsLocked, "Zooming far out drops the focus");
            Set(view, "targetDistance", zoom0); Set(view, "manualRelease", false);
            yield return new WaitForSecondsRealtime(0.3f);

            // ── punch combo (start it while the rat is not mid-attack, and keep it from attacking)
            for (float t = 0; t < 4f && (rat.IsTelegraphing || rat.Status == "АТАКУЕТ"); t += Time.unscaledDeltaTime) yield return null;
            // a clean one-on-one: the other enemies step out until the rat is down
            var benched = enemies.Where(e => e != rat).ToList();
            foreach (var e in benched) e.gameObject.SetActive(false);
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

            foreach (var e in benched) e.gameObject.SetActive(true);
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
                for (float t = 0; t < 3f && e.Status != "ВЫЖИДАЕТ" && !e.IsTelegraphing; t += Time.unscaledDeltaTime) yield return null;
                yield return new WaitForSecondsRealtime(0.25f);
                Vector3 toG = combat.transform.position - e.transform.position; toG.y = 0;
                float faceErr = Vector3.Angle(EyeForward(e), toG);
                Check(faceErr < 15f, e.displayName + " looks straight at the goblin (eyes off by " + faceErr.ToString("0") + "°, state " + e.Status + ")");
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

            // ── air slam on the training dummy
            if (hud != null && hud.dummy != null)
            {
                var dummyT = hud.dummy.GetComponent<DamageableTarget>();
                dummyT.RestoreHealth();
                Vector3 dp = hud.dummy.transform.position; Vector3 off = (spawn - dp); off.y = 0; off = off.normalized * 1.3f;
                Teleport(dp + off, dp);
                yield return new WaitForSecondsRealtime(0.3f);
                var mvd = combat.GetComponent<GoblinMovement>();
                float dummyBefore = dummyT.Health;
                mvd.RequestJump();
                yield return new WaitForSecondsRealtime(0.25f);
                Set(combat, "diving", true); mvd.Dive(16f);
                yield return Shot("14_dive");
                for (float t = 0; t < 1.5f && mvd.IsAirborne; t += Time.unscaledDeltaTime) yield return null;
                yield return new WaitForSecondsRealtime(0.05f);
                yield return Shot("14_dive_impact");
                Check(dummyBefore - dummyT.Health >= 20f, "Air slam hits around the landing (" + (dummyBefore - dummyT.Health) + " dmg)");
                Check(!combat.IsDiving, "Slam ends on landing");
                yield return new WaitForSecondsRealtime(0.6f);
                Check(!combat.MovementLocked, "Goblin recovers after the slam");
            }

            // ── bestiary
            if (hud != null && hud.bestiary != null)
            {
                hud.bestiary.SetActive(true);
                yield return new WaitForSecondsRealtime(.3f);
                yield return Shot("08_bestiary");
                hud.bestiary.SetActive(false);
            }
            yield return InventoryChecks(spawn);
            yield return PoseGallery(enemies);
            Finish();
        }

        /// <summary>Quick visual pass: relaxed and armed stances from two sides, plus the HUD.</summary>
        private IEnumerator StanceGallery()
        {
            var inv = combat.GetComponent<GoblinInventory>();
            foreach (var e in EnemyActor.All.ToArray()) if (e != null) e.gameObject.SetActive(false);
            Set(combat, "lastCombatTime", -100f);
            Vector3 home = combat.transform.position;
            Teleport(home, home + Vector3.forward);
            foreach (var d in new[] { Items.Dagger, Items.Club, Items.Cleaver }) if (!inv.Has(d.id)) inv.Add(d, 1, false);
            inv.Add(Items.Mushroom, 3, false);
            yield return new WaitForSecondsRealtime(0.5f);
            view.enabled = false;
            var camT = Camera.main.transform;
            var poses = new (string weapon, bool relaxed)[] { ("fists", true), ("dagger", true), ("club", true), ("dagger", false), ("club", false), ("cleaver", false) };
            foreach (var (weapon, relaxed) in poses)
            {
                inv.Equip(weapon, 0); combat.ForceRelaxed = relaxed;
                yield return new WaitForSecondsRealtime(1.3f);
                Vector3 f = combat.transform.forward, r = combat.transform.right, chest = combat.transform.position + Vector3.up * .75f;
                string tag = (relaxed ? "relaxed_" : "guard_") + weapon;
                Check(relaxed ? combat.RelaxWeight > .9f : (weapon == "fists" || combat.GuardWeight > .9f), "Stance " + tag + " (relax " + combat.RelaxWeight.ToString("0.00") + ", guard " + combat.GuardWeight.ToString("0.00") + ")");
                camT.position = chest + f * 2.1f + r * .9f + Vector3.up * .25f; camT.LookAt(chest);
                yield return Shot("21_" + tag + "_front");
                camT.position = chest + r * 2.3f + f * .3f + Vector3.up * .15f; camT.LookAt(chest);
                yield return Shot("21_" + tag + "_side");
            }
            combat.ForceRelaxed = null;
            view.enabled = true; view.Snap();
            inv.Cycle(1);
            yield return new WaitForSecondsRealtime(0.15f);
            yield return Shot("21_hud_swap");
            yield return new WaitForSecondsRealtime(0.8f);
            yield return Shot("21_hud");
            var ihud = FindFirstObjectByType<InventoryHUD>();
            if (ihud != null) { ihud.ToggleScreen(true); yield return new WaitForSecondsRealtime(0.3f); yield return Shot("21_bag"); ihud.ToggleScreen(false); }
        }

        /// <summary>Belt, pickups, weapons in the hand and their damage, eating, the weapon arc and the bag screen.</summary>
        private IEnumerator InventoryChecks(Vector3 spawn)
        {
            var inv = combat.GetComponent<GoblinInventory>();
            var ihud = FindFirstObjectByType<InventoryHUD>();
            Check(inv != null && ihud != null, "Inventory and its HUD are set up");
            if (inv == null || ihud == null) yield break;
            Check(inv.Count("coin") > 0 || FindObjectsByType<ItemPickup>(FindObjectsSortMode.None).Any(pk => pk.itemId == "coin"), "Fallen monsters drop coins (" + inv.Count("coin") + " in the bag)");
            Call(combat, "Respawn");
            // enemies step aside: these checks are about the goblin and his bag
            var benched = EnemyActor.All.Where(e => e != null && e.gameObject.activeSelf).ToList();
            foreach (var e in benched) e.gameObject.SetActive(false);
            yield return new WaitForSecondsRealtime(0.5f);
            Teleport(spawn, spawn + Vector3.forward);
            view.Snap();
            // eating: a glow mushroom heals and is used up
            if (inv.Count("mushroom") == 0) inv.Add(Items.Mushroom, 2, false);
            int mushSlot = -1; for (int i = 0; i < GoblinInventory.Size; i++) if (inv[i].id == "mushroom") { mushSlot = i; break; }
            Set(combat, "health", 50f);
            int mushBefore = inv.Count("mushroom");
            bool ate = inv.Use(mushSlot);
            Check(ate && combat.Health > 65f && inv.Count("mushroom") == mushBefore - 1, "Eating a glow mushroom heals (50 → " + combat.Health.ToString("0") + ") and uses it up");
            Set(combat, "health", combat.MaxHealth);
            // a dagger lying in front of the goblin flies into the bag
            inv.Equip("fists", 0);
            var drop = ItemPickup.Spawn("dagger", 1, combat.transform.position + combat.transform.forward * 1.8f + Vector3.up, Vector3.up * 2f);
            bool hadDagger = inv.Has("dagger");
            for (float t = 0; t < 4f && !(inv.Count("dagger") > (hadDagger ? 1 : 0)); t += Time.unscaledDeltaTime) yield return null;
            yield return null;   // the picked-up item is removed at the end of the frame
            Check(inv.Has("dagger") && drop == null, "Dagger pickup flies into the bag (had " + hadDagger + ", now " + inv.Count("dagger") + (drop != null ? ", pickup still at " + (drop.transform.position - combat.transform.position).ToString("0.0") : "") + ")");
            inv.Equip("dagger", 1);
            yield return new WaitForSecondsRealtime(0.5f);
            Transform held = inv.Held;
            float handGap = held != null && inv.Hand != null ? Vector3.Distance(held.position, inv.Hand.position) : 99f;
            var heldBounds = held != null ? held.GetComponent<Renderer>().bounds.size.magnitude : 0f;
            Check(held != null && held.name.Contains(Items.Dagger.name) && handGap < .3f && heldBounds > .2f && heldBounds < 1.5f,
                  "The dagger sits in the right hand (" + handGap.ToString("0.00") + " m from the hand, " + heldBounds.ToString("0.00") + " m long)");
            if (held != null)
            {
                view.enabled = false;
                var camT = Camera.main.transform; Vector3 f = combat.transform.forward, r = combat.transform.right;
                camT.position = held.position + f * 1.1f + r * .7f + Vector3.up * .25f; camT.LookAt(held.position);
                yield return Shot("18_hand_dagger");
                inv.Add(Items.Cleaver, 1, false); inv.Equip("cleaver", 1);
                yield return new WaitForSecondsRealtime(0.4f);
                held = inv.Held;
                camT.position = combat.transform.position + f * 2.2f + r * 1.0f + Vector3.up * 1.3f; camT.LookAt(combat.transform.position + Vector3.up * .8f);
                yield return Shot("18_hand_cleaver");
                view.enabled = true;
            }
            // weapons change the damage: one punch on the dummy with fists vs the club
            if (hud != null && hud.dummy != null)
            {
                var dummyT = hud.dummy.GetComponent<DamageableTarget>();
                Vector3 dp = hud.dummy.transform.position; Vector3 off = Flat(spawn - dp).normalized * 1.2f;
                float Punch(string weapon)
                {
                    inv.Equip(weapon, 0); dummyT.RestoreHealth(); Teleport(dp + off, dp);
                    float before = dummyT.Health; Set(combat, "comboIndex", -1); Call(combat, "NextPunch"); return before;
                }
                float b0 = Punch("fists"); yield return new WaitForSecondsRealtime(1.0f); float fistDmg = b0 - dummyT.Health;
                if (!inv.Has("club")) inv.Add(Items.Club, 1, false);
                float b1 = Punch("club"); yield return new WaitForSecondsRealtime(1.2f); float clubDmg = b1 - dummyT.Health;
                Check(fistDmg > 0f && clubDmg > fistDmg * 1.3f, "The club hits harder than fists (" + fistDmg.ToString("0") + " → " + clubDmg.ToString("0") + ")");
                dummyT.RestoreHealth();
                // with a weapon the right arm swings it: wind-up over the shoulder, then the strike
                inv.Equip("club", 0); Teleport(dp + off, dp);
                yield return new WaitForSecondsRealtime(0.3f);
                view.enabled = false;
                var camT = Camera.main.transform; Vector3 side = Vector3.Cross(Vector3.up, Flat(dp - combat.transform.position).normalized);
                camT.position = combat.transform.position + side * 3.2f + Vector3.up * 1.4f - Flat(dp - combat.transform.position).normalized * .6f; camT.LookAt(combat.transform.position + Vector3.up * 1.0f);
                Set(combat, "comboIndex", -1); Call(combat, "NextPunch");
                float maxRaise = 0f; bool s1 = false, s2 = false;
                for (float t = 0; t < 0.9f; t += Time.unscaledDeltaTime)
                {
                    maxRaise = Mathf.Max(maxRaise, -combat.SwingRaise);
                    if (!s1 && t > .13f) { s1 = true; yield return Shot("19_swing_windup"); }
                    if (!s2 && t > .27f) { s2 = true; yield return Shot("19_swing_strike"); }
                    yield return null;
                }
                Check(maxRaise > 80f, "The club is swung over the shoulder, not jabbed (arm raised " + maxRaise.ToString("0") + "°)");
                view.enabled = true;
                dummyT.RestoreHealth();
            }
            // the wheel flips weapons: the arc slides, the hub spins the new icon in
            Teleport(spawn, spawn + Vector3.forward);
            view.Snap();
            yield return new WaitForSecondsRealtime(0.4f);
            string w0 = inv.Weapon.id;
            inv.Cycle(1);
            Check(inv.Weapon.id != w0, "Flipping weapons changes the weapon in hand (" + w0 + " → " + inv.Weapon.id + ")");
            yield return new WaitForSecondsRealtime(0.08f);
            yield return Shot("18_weapon_swap");
            float arcPos0 = (float)Get(ihud, "arcPos");
            yield return new WaitForSecondsRealtime(0.6f);
            Check(Mathf.Abs((float)Get(ihud, "arcPos") - (float)Get(ihud, "arcTarget")) < .05f && Mathf.Abs(arcPos0 - (float)Get(ihud, "arcTarget")) > .02f, "Weapon arc slides to the new weapon");
            yield return Shot("18_hud");
            // belt slots show the bag; the bag screen opens with the cursor free and closes again
            var beltIcon = ihud.transform.Find("Belt HUD/Belt 1/Icon");
            Check(beltIcon != null && beltIcon.GetComponent<UnityEngine.UI.RawImage>().texture != null, "Belt slot 1 shows an item icon");
            ihud.ToggleScreen(true);
            yield return new WaitForSecondsRealtime(0.3f);
            Check(ihud.ScreenOpen && Cursor.lockState != CursorLockMode.Locked && inv.Open, "Bag screen opens with a free cursor");
            yield return Shot("18_bag");
            ihud.ToggleScreen(false);
            yield return null;
            Check(!ihud.ScreenOpen && !inv.Open, "Bag screen closes");
            // throwing a stack out: it lands nearby and isn't grabbed back at once
            int bone = -1; inv.Add(Items.Bone, 3, false); for (int i = 0; i < GoblinInventory.Size; i++) if (inv[i].id == "bone") bone = i;
            inv.Drop(bone);
            yield return new WaitForSecondsRealtime(1.0f);
            var bonePick = FindObjectsByType<ItemPickup>(FindObjectsSortMode.None).FirstOrDefault(pk => pk.itemId == "bone");
            Check(inv.Count("bone") == 0 && bonePick != null, "A dropped stack lies on the ground");
            yield return new WaitForSecondsRealtime(1.8f);
            if (bonePick != null) Teleport(bonePick.transform.position - combat.transform.forward * .8f, bonePick.transform.position);   // walk back to it
            for (float t = 0; t < 4f && inv.Count("bone") == 0; t += Time.unscaledDeltaTime) yield return null;
            Check(inv.Count("bone") == 3, "...and can be picked up again later (" + inv.Count("bone") + ")");
            inv.Equip("fists", 0);
            foreach (var e in benched) if (e != null) e.gameObject.SetActive(true);
        }

        /// <summary>Close-up 3/4 shots of every enemy clip at key phases (for visual review).</summary>
        private IEnumerator PoseGallery(List<EnemyActor> enemies)
        {
            view.enabled = false;
            if (hud != null) hud.gameObject.SetActive(false);
            foreach (var e in enemies) { e.enabled = false; var eb = e.transform.Find("Health"); if (eb != null) eb.gameObject.SetActive(false); var ea = e.transform.Find("Alert"); if (ea != null) ea.gameObject.SetActive(false); }
            // goblin jump, seen from the side, nobody around
            {
                var mvj = combat.GetComponent<GoblinMovement>();
                Vector3 spot = FreeSpot(combat.transform.position, 7f);
                Teleport(spot, spot + Vector3.forward);
                var camJ = Camera.main.transform;
                camJ.position = spot + Vector3.right * 3.2f + Vector3.up * 1.4f; camJ.LookAt(spot + Vector3.up * 1.2f);
                yield return new WaitForSecondsRealtime(0.5f);
                yield return Shot("13_jump_0_stand");
                for (int run = 0; run < 2; run++)
                {
                    string tag = run == 0 ? "stand" : "run";
                    Teleport(spot, spot + Vector3.forward);
                    yield return new WaitForSecondsRealtime(0.3f);
                    if (run == 1)
                    {
                        // run in from behind, jump past the camera's view axis
                        Teleport(spot + Vector3.back * 4f, spot + Vector3.forward);
                        mvj.simulatedInput = Vector2.up;
                        float yawSim = Camera.main.transform.eulerAngles.y;
                        for (float t = 0; t < 0.9f; t += Time.unscaledDeltaTime) yield return null;
                    }
                    mvj.RequestJump();
                    bool r = false, a = false, f = false, reach = false, landed = false;
                    for (float t = 0; t < 1.8f && !landed; t += Time.unscaledDeltaTime)
                    {
                        float vy = mvj.VerticalSpeed;
                        camJ.position = combat.transform.position + combat.transform.right * 3.4f + Vector3.up * 1.2f; camJ.LookAt(combat.transform.position + Vector3.up * 1.1f);
                        if (!r && mvj.IsAirborne && vy > 3f) { r = true; yield return Shot("13_" + tag + "_1_rise"); }
                        else if (!a && mvj.IsAirborne && Mathf.Abs(vy) < 1f) { a = true; yield return Shot("13_" + tag + "_2_apex"); }
                        else if (!f && mvj.IsAirborne && vy < -2f && mvj.GroundDistance > 0.45f) { f = true; yield return Shot("13_" + tag + "_3_fall"); }
                        else if (!reach && mvj.IsAirborne && vy < -2f && mvj.GroundDistance < 0.45f) { reach = true; yield return Shot("13_" + tag + "_4_reach"); }
                        else if (a && !mvj.IsAirborne) { landed = true; yield return new WaitForSecondsRealtime(0.06f); yield return Shot("13_" + tag + "_5_land"); }
                        else yield return null;
                    }
                    mvj.simulatedInput = Vector2.zero;
                    yield return new WaitForSecondsRealtime(0.6f);
                }
                // roll and slide, side view
                for (int k = 0; k < 2; k++)
                {
                    Teleport(spot + Vector3.back * (k == 0 ? 0f : 4f), spot + Vector3.forward);
                    yield return new WaitForSecondsRealtime(0.4f);
                    if (k == 1) { mvj.simulatedInput = Vector2.up; for (float t = 0; t < 0.8f; t += Time.unscaledDeltaTime) yield return null; }
                    combat.RequestDodge();
                    float[] at = k == 0 ? new[] { .12f, .26f, .4f, .62f } : new[] { .12f, .3f, .55f };
                    float t0 = Time.time; int shot = 0;
                    while (shot < at.Length && Time.time - t0 < 1.5f)
                    {
                        camJ.position = combat.transform.position + combat.transform.right * 3.4f + Vector3.up * 1.0f; camJ.LookAt(combat.transform.position + Vector3.up * .7f);
                        if (Time.time - t0 >= at[shot]) { yield return Shot("15_" + (k == 0 ? "roll" : "slide") + "_" + shot); shot++; }
                        else yield return null;
                    }
                    mvj.simulatedInput = Vector2.zero;
                    yield return new WaitForSecondsRealtime(0.8f);
                }
            }
            // goblin face close-ups (mesh check)
            {
                var grs = combat.GetComponentsInChildren<Renderer>(); Bounds gb = grs[0].bounds; foreach (var r in grs) gb.Encapsulate(r.bounds);
                Vector3 head = new Vector3(gb.center.x, gb.max.y - gb.size.y * .2f, gb.center.z);
                var camT = Camera.main.transform; Camera.main.fieldOfView = 40f;
                foreach (var (ang, label) in new[] { (0f, "front"), (40f, "left"), (-40f, "right"), (0f, "below") })
                {
                    Vector3 dir = Quaternion.AngleAxis(ang, Vector3.up) * combat.transform.forward;
                    camT.position = head + dir * gb.size.y * .55f + Vector3.up * (label == "below" ? -gb.size.y * .12f : gb.size.y * .02f);
                    camT.LookAt(head);
                    yield return Shot("11_goblin_face_" + label);
                }
                // flat unlit white: no shading or shadows, so any hole shows the sky through it
                var white = new Material(Shader.Find("Unlit/Color")) { color = Color.white };
                var saved = new Dictionary<Renderer, Material[]>();
                foreach (var r in grs) { saved[r] = r.sharedMaterials; r.sharedMaterials = Enumerable.Repeat(white, r.sharedMaterials.Length).ToArray(); }
                Camera.main.backgroundColor = Color.magenta; var clear = Camera.main.clearFlags; Camera.main.clearFlags = CameraClearFlags.SolidColor;
                foreach (var (ang, label) in new[] { (0f, "front"), (40f, "left"), (-40f, "right"), (0f, "below") })
                {
                    Vector3 dir = Quaternion.AngleAxis(ang, Vector3.up) * combat.transform.forward;
                    camT.position = head + dir * gb.size.y * .55f + Vector3.up * (label == "below" ? -gb.size.y * .12f : gb.size.y * .02f);
                    camT.LookAt(head);
                    yield return Shot("11_goblin_white_" + label);
                }
                foreach (var kv in saved) kv.Key.sharedMaterials = kv.Value;
                Camera.main.clearFlags = clear;
                Camera.main.fieldOfView = 60f;
            }
            combat.gameObject.SetActive(false);
            foreach (var e in enemies) { e.enabled = false; var bar = e.transform.Find("Health"); if (bar != null) bar.gameObject.SetActive(false); var al = e.transform.Find("Alert"); if (al != null) al.gameObject.SetActive(false); }
            Vector3 stage = FreeSpot(combat.transform.position, 10f);
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
                foreach (var (clipName, frac, label) in new[] { ("Idle", 0f, "idle"), ("Walk", .25f, "walk"), ("Attack", -1f, "windup"), ("Attack", -2f, "strike"), ("Attack2", -1f, "windup2"), ("Attack2", -2f, "strike2"), ("Hit", .2f, "hit"), ("Death", 1f, "death") })
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
            // ── the world: mood shots and gameplay places
            var worldRoot = GameObject.Find("Twilight World");
            if (worldRoot != null)
            {
                var cam = Camera.main.transform;
                combat.gameObject.SetActive(true);
                Vector3 home = (Vector3)Get(combat, "spawnPosition");
                Teleport(home, home + Vector3.forward);
                yield return new WaitForSecondsRealtime(0.3f);
                Check(worldRoot.GetComponentsInChildren<Renderer>().Length > 1500, "World built (" + worldRoot.GetComponentsInChildren<Renderer>().Length + " renderers)");

                // mood shots from goblin height and from above
                var looks = new List<(Vector3 pos, Vector3 look, string name)>
                {
                    (home + new Vector3(0, 55, -85), home + new Vector3(0, 0, 15), "16_world_overview"),
                    (home + new Vector3(28, 2.2f, -30), home + new Vector3(60, 6, -75), "16_world_forest_a"),
                    (home + new Vector3(-30, 2.5f, 26), home + new Vector3(-70, 7, 60), "16_world_forest_b"),
                    (home + new Vector3(-6, 3.5f, -14), home + new Vector3(9, 1, -12), "16_world_arena_edge"),
                };
                foreach (var v in looks) { cam.position = v.pos; cam.LookAt(v.look); yield return new WaitForSecondsRealtime(0.2f); yield return Shot(v.name); }
                var ruin = worldRoot.transform.Find("Landmarks/Standing Stones");
                Check(ruin != null, "Standing-stone ruins built");
                if (ruin != null) { cam.position = ruin.position + ruin.forward * 14f + ruin.right * 6f + Vector3.up * 4.5f; cam.LookAt(ruin.position + Vector3.up * 1.5f); yield return new WaitForSecondsRealtime(0.2f); yield return Shot("16_world_ruin"); }
                var lantern = worldRoot.transform.Find("Landmarks/Lantern Post");
                Check(lantern != null, "Paths have lantern posts");
                if (lantern != null) { cam.position = lantern.position + lantern.forward * 3f + lantern.right * 9f + Vector3.up * 2.2f; cam.LookAt(lantern.position + Vector3.up * 1.2f - lantern.right * 6f); yield return new WaitForSecondsRealtime(0.2f); yield return Shot("16_world_path"); }
                var shroom = worldRoot.transform.Find("Landmarks/Giant Mushrooms");
                Check(shroom != null && shroom.childCount > 10, "Giant mushrooms grow in the forest (" + (shroom != null ? shroom.childCount : 0) + ")");
                if (shroom != null && shroom.childCount > 0) { var g = shroom.GetChild(0); cam.position = g.position + new Vector3(7f, 3f, 7f); cam.LookAt(g.position + Vector3.up * 2.5f); yield return new WaitForSecondsRealtime(0.2f); yield return Shot("16_world_mushrooms"); }
                var terrainData = worldRoot.GetComponentInChildren<Terrain>().terrainData;
                Check(terrainData.detailPrototypes.Length >= 2 && terrainData.GetDetailLayer(0, 0, terrainData.detailWidth, terrainData.detailHeight, 0).Cast<int>().Sum() > 20000, "Instanced grass carpet planted");

                // campfire heals
                var fire = worldRoot.GetComponentInChildren<Campfire>();
                if (fire != null)
                {
                    Set(combat, "health", 60f);
                    Teleport(fire.transform.position + new Vector3(1.9f, .2f, 0), fire.transform.position);
                    cam.position = fire.transform.position + new Vector3(4.5f, 2.6f, -4.5f); cam.LookAt(fire.transform.position + Vector3.up * 1.1f);
                    yield return new WaitForSecondsRealtime(1.6f);
                    Check(combat.Health > 75f, "Campfire heals (60 → " + combat.Health.ToString("0") + " in 1.6 s)");
                    yield return Shot("17_campfire");
                }

                // oak stump lair: mobs crawl out when the goblin comes close
                var stump = worldRoot.GetComponentsInChildren<MobLair>().FirstOrDefault(l => l.name.Contains("Stump"));
                if (stump != null)
                {
                    int before = EnemyActor.All.Count;
                    Vector3 mouth = stump.mouth.position, outward = stump.mouth.forward;
                    cam.position = mouth + outward * 13f + Vector3.up * 5f + Vector3.Cross(Vector3.up, outward) * 5f; cam.LookAt(mouth + Vector3.up * 1.5f);
                    Teleport(mouth + outward * 20f, mouth);
                    yield return new WaitForSecondsRealtime(0.3f);
                    yield return Shot("17_stump_asleep");
                    Teleport(mouth + outward * 7f, mouth);
                    for (float t = 0; t < 6f && stump.Spawned == 0; t += Time.unscaledDeltaTime) yield return null;
                    Check(stump.Spawned > 0, "Stump lair wakes and a mob crawls out");
                    yield return new WaitForSecondsRealtime(0.45f);
                    yield return Shot("17_stump_emerging");
                    yield return new WaitForSecondsRealtime(4.5f);
                    Check(EnemyActor.All.Count > before && stump.AliveCount > 0, "Lair mobs join the fight (" + stump.AliveCount + " alive, " + stump.Spawned + " spawned)");
                    yield return Shot("17_stump_mobs");
                    foreach (var e in EnemyActor.All.ToArray()) if (e != null && !e.respawns && !e.Health.IsDead) e.Health.TakeHit(new HitInfo { damage = 9999, source = combat.transform.position });
                    Set(combat, "health", combat.MaxHealth);
                }

                // house: roof and walls fade when inside, ambush, chest
                var houses = worldRoot.GetComponentsInChildren<HouseDungeon>();
                var houseA = houses.FirstOrDefault(h => h.GetComponentInChildren<TeleportPad>() == null);
                if (houseA != null)
                {
                    var ht = houseA.transform;
                    Teleport(ht.position + ht.forward * 12f, ht.position);
                    cam.position = ht.position + ht.forward * 15f + ht.right * 8f + Vector3.up * 6f; cam.LookAt(ht.position + Vector3.up * 2.5f);
                    yield return new WaitForSecondsRealtime(0.4f);
                    Check(!houseA.PlayerInside && houseA.Alpha > .95f, "House is solid from outside");
                    yield return Shot("17_house_outside");
                    cam.position = ht.position + ht.forward * 8f - ht.right * 7f + Vector3.up * 3.2f; cam.LookAt(ht.position + Vector3.up * 2.2f);
                    yield return new WaitForSecondsRealtime(0.2f); yield return Shot("17_house_detail");
                    cam.position = ht.position - ht.forward * 10f + ht.right * 9f + Vector3.up * 5f; cam.LookAt(ht.position + Vector3.up * 2.2f);
                    yield return new WaitForSecondsRealtime(0.2f); yield return Shot("17_house_back");
                    Teleport(ht.position + ht.forward * 1.2f + Vector3.up * .2f, ht.position - ht.forward * 3f);
                    yield return new WaitForSecondsRealtime(1.0f);
                    Check(houseA.PlayerInside && houseA.Alpha < .2f, "Inside the house the roof fades (alpha " + houseA.Alpha.ToString("0.00") + ")");
                    Check(houseA.SolidestWall > .9f, "...but the far walls stay solid (" + houseA.SolidestWall.ToString("0.00") + ")");
                    cam.position = ht.position + ht.forward * 9f + ht.right * 5f + Vector3.up * 9f; cam.LookAt(ht.position + Vector3.up * .8f);
                    var ambush = houseA.GetComponentInChildren<MobLair>();
                    for (float t = 0; t < 5f && ambush != null && ambush.Spawned < 2; t += Time.unscaledDeltaTime) yield return null;
                    Check(ambush != null && ambush.Spawned > 0, "House ambush springs (" + (ambush != null ? ambush.Spawned : 0) + " mobs)");
                    yield return new WaitForSecondsRealtime(0.6f);
                    yield return Shot("17_house_inside");
                    cam.position = ht.position + ht.forward * 3.5f + ht.right * 2.5f + Vector3.up * 6.5f; cam.LookAt(ht.position - ht.forward * .5f);
                    yield return new WaitForSecondsRealtime(0.2f); yield return Shot("17_house_interior");
                    foreach (var e in EnemyActor.All.ToArray()) if (e != null && !e.respawns && !e.Health.IsDead) e.Health.TakeHit(new HitInfo { damage = 9999, source = combat.transform.position });
                    var chest = houseA.GetComponentInChildren<Chest>();
                    var prog = combat.GetComponent<PlayerProgression>(); int xpBefore = prog != null ? prog.Experience + prog.Level * 1000 : 0;
                    int pickupsBefore = FindObjectsByType<ItemPickup>(FindObjectsSortMode.None).Length;
                    if (chest != null) chest.Open();
                    yield return null;
                    Check(FindObjectsByType<ItemPickup>(FindObjectsSortMode.None).Length > pickupsBefore, "Chest loot pops out");
                    yield return new WaitForSecondsRealtime(0.7f);
                    Check(chest != null && chest.IsOpen && (prog == null || prog.Experience + prog.Level * 1000 > xpBefore), "Chest opens and pays experience");
                    yield return Shot("17_house_chest_open");
                    Teleport(ht.position + ht.forward * 12f, ht.position);
                    yield return new WaitForSecondsRealtime(1.0f);
                    Check(houseA.Alpha > .95f, "House turns solid again after leaving");
                }

                // rune circle → dungeon chamber → back
                var pad = worldRoot.GetComponentsInChildren<TeleportPad>().FirstOrDefault(p => p.GetComponentInParent<HouseDungeon>() != null);
                if (pad != null && pad.target != null)
                {
                    Set(combat, "health", combat.MaxHealth);
                    Teleport(pad.transform.position + pad.transform.forward * 1.2f + Vector3.up * .2f, pad.transform.position);
                    yield return new WaitForSecondsRealtime(0.5f);
                    float topY = combat.transform.position.y;
                    pad.Use();
                    yield return new WaitForSecondsRealtime(0.4f);
                    Check(combat.transform.position.y < topY - 40f, "Rune circle takes the goblin down into the dungeon (y " + combat.transform.position.y.ToString("0") + ")");
                    var room = pad.target.transform.parent;
                    cam.position = room.position + new Vector3(7f, 12f, 13f); cam.LookAt(room.position + new Vector3(0, 0, -2f));
                    var crypt = room.GetComponentInChildren<MobLair>();
                    for (float t = 0; t < 6f && crypt != null && crypt.Spawned == 0; t += Time.unscaledDeltaTime) yield return null;
                    Check(crypt != null && crypt.Spawned > 0, "Dungeon lair releases its guards");
                    yield return new WaitForSecondsRealtime(1.5f);
                    Check(combat.transform.position.y > room.position.y - 1f, "Goblin stands on the dungeon floor");
                    yield return Shot("17_dungeon");
                    foreach (var e in EnemyActor.All.ToArray()) if (e != null && !e.respawns && !e.Health.IsDead) e.Health.TakeHit(new HitInfo { damage = 9999, source = combat.transform.position });
                    yield return new WaitForSecondsRealtime(1.1f);
                    pad.target.Use();
                    yield return new WaitForSecondsRealtime(0.4f);
                    Check(Mathf.Abs(combat.transform.position.y - topY) < 3f, "The second circle brings the goblin back up");
                }
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
