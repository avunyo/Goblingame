using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Goblingame.TestScene.Editor
{
    /// <summary>
    /// One-click upgrade of GoblinPlayable:
    ///  • goblin animator gets IdleTime / BlockTime motion-time parameters (rare idle gesture, held guard);
    ///  • the oval placeholders are replaced by the real enemy models (rat, mushroom, skeleton, boar);
    ///  • each enemy gets its own fighting character (speed, wind-up, poise…).
    /// Runs automatically once after compile when Temp/GoblinUpgrade.request exists, or from the menu.
    /// </summary>
    [InitializeOnLoad]
    public static class GoblinGameUpgrade
    {
        private const string ScenePath = "Assets/Scenes/GoblinPlayable.unity";
        private const string GoblinController = "Assets/RecoveredGoblinGraphics/Generated/GoblinRecovered.controller";
        private const string EnemyModels = "Assets/RecoveredGoblinGraphics/Enemies/";
        private const string EnemyControllers = "Assets/EnemyCast/Controllers";
        private const string PaletteFolder = "Assets/EnemyCast/Generated 1/";

        private struct Archetype
        {
            public string id, name; public float scale, windup, strike, recover, range, lunge, poise, cooldown, alert, leash, speed, damage;
            public EnemyRigBuilder.AttackStyle style; public float cycle, heavy;
        }
        private static readonly Archetype[] Cast =
        {
            // scale = height relative to the goblin
            new Archetype{id="RatRogue",    name="Крысолов",       scale=.95f,windup=.45f,strike=.22f,recover=.70f,range=1.20f,lunge=1.3f,poise=18f,cooldown=.7f,alert=7f,leash=16f,speed=1.6f,damage=10f,style=EnemyRigBuilder.AttackStyle.Stab,cycle=.62f,heavy=0f},
            new Archetype{id="MossGourmand",name="Грибоед",        scale=1.15f,windup=1.0f,strike=.35f,recover=1.3f,range=1.50f,lunge=.5f, poise=60f,cooldown=1.6f,alert=5f,leash=14f,speed=.75f,damage=20f,style=EnemyRigBuilder.AttackStyle.Slam,cycle=1.1f,heavy=.8f},
            new Archetype{id="BoneSentinel",name="Костяной страж", scale=1.05f,windup=.70f,strike=.28f,recover=1.0f,range=1.45f,lunge=.8f, poise=40f,cooldown=1.1f,alert=6f,leash=16f,speed=1.05f,damage=16f,style=EnemyRigBuilder.AttackStyle.Slash,cycle=.85f,heavy=.3f},
            new Archetype{id="IronTusk",    name="Железный клык",  scale=1.5f,windup=1.15f,strike=.38f,recover=1.4f,range=1.90f,lunge=1.0f,poise=90f,cooldown=1.4f,alert=8f,leash=18f,speed=1.15f,damage=30f,style=EnemyRigBuilder.AttackStyle.Smash,cycle=1.0f,heavy=1f},
        };

        static GoblinGameUpgrade()
        {
            string request = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Temp/GoblinUpgrade.request");
            if (!File.Exists(request)) return;
            EditorApplication.delayCall += () => RunQueued(request);
        }

        internal static void RunQueued(string request)
        {
            if (!File.Exists(request)) return;
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
            { EditorApplication.delayCall += () => RunQueued(request); return; }
            var scene = SceneManager.GetActiveScene();
            if (scene.path != ScenePath) { Debug.LogWarning("[GoblinUpgrade] Open " + ScenePath + " and choose Goblingame > Upgrade Camera, Combat and Enemies."); File.Delete(request); return; }
            string command = File.ReadAllText(request).Trim();
            File.Delete(request);
            if (scene.isDirty)
            {
                if (command != "retry") { Debug.LogWarning("[GoblinUpgrade] Scene has unsaved changes — save it, then choose Goblingame > Upgrade Camera, Combat and Enemies."); return; }
                // A previous upgrade attempt failed half-way. Keep a copy of the in-memory scene, then reload the saved one.
                string copy = AssetDatabase.GenerateUniqueAssetPath("Assets/Scenes/GoblinUnsavedBeforeRetry.unity");
                EditorSceneManager.SaveScene(scene, copy, true);
                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
                Debug.Log("[GoblinUpgrade] Unsaved scene state copied to " + copy + "; reloaded " + ScenePath + " from disk.");
            }
            try { Apply(); } catch (Exception e) { Debug.LogException(e); return; }
            GoblinPlaytestLauncher.StartIfRequested();
        }

        [MenuItem("Goblingame/Upgrade Camera, Combat and Enemies")]
        public static void Apply()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play Mode first.");
            var scene = SceneManager.GetActiveScene();
            if (scene.path != ScenePath) throw new InvalidOperationException("Open " + ScenePath + " first.");

            string backup = AssetDatabase.GenerateUniqueAssetPath("Assets/Scenes/GoblinBeforeUpgrade.unity");
            if (!EditorSceneManager.SaveScene(scene, backup, true)) throw new InvalidOperationException("Backup failed; nothing changed.");

            UpgradeGoblinAnimator();
            // the in-air base pose refers to the run clip, which now starts 0.14 s later
            var player = scene.GetRootGameObjects().FirstOrDefault(o => o.name == "Player");
            var gc = player != null ? player.GetComponent<GoblinCombat>() : null;
            if (gc != null) { var so = new SerializedObject(gc); var f = so.FindProperty("airStrideTime"); if (f != null) { f.floatValue = 0.13f; so.ApplyModifiedProperties(); } }
            int replaced = UpgradeEnemies(scene);

            EditorSceneManager.MarkSceneDirty(scene);
            AssetDatabase.SaveAssets();
            if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Scene save failed.");
            Debug.Log("[GoblinUpgrade] DONE: goblin animator time-params set; " + replaced + " enemy models installed; backup " + backup);
        }

        // ───────────────────────── goblin animator ─────────────────────────
        private static void UpgradeGoblinAnimator()
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(GoblinController);
            if (controller == null) { Debug.LogWarning("[GoblinUpgrade] Goblin controller not found: " + GoblinController); return; }
            foreach (string p in new[] { "IdleTime", "BlockTime", "MoveSpeed", "AttackSpeed" })
                if (!controller.parameters.Any(x => x.name == p)) controller.AddParameter(p, AnimatorControllerParameterType.Float);
            var ps = controller.parameters;
            foreach (var p in ps) if (p.name == "MoveSpeed" || p.name == "AttackSpeed") p.defaultFloat = 1f;
            controller.parameters = ps;
            foreach (var child in controller.layers[0].stateMachine.states)
            {
                var s = child.state;
                string param = s.name == "Idle" || s.name == "CombatReady" || s.name == "CombatIdle" ? "IdleTime" : s.name == "Block" ? "BlockTime" : null;
                if (s.name == "Walk" || s.name == "Run") { s.speedParameter = "MoveSpeed"; s.speedParameterActive = true; EditorUtility.SetDirty(s); }
                if (s.name == "Punch" || s.name == "Headbutt") { s.speedParameter = "AttackSpeed"; s.speedParameterActive = true; EditorUtility.SetDirty(s); }
                if (param == null) continue;
                s.timeParameter = param; s.timeParameterActive = true;
                EditorUtility.SetDirty(s);
            }
            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            GoblinAnimTools.BuildLoops(controller);
            Debug.Log("[GoblinUpgrade] Goblin animator: Idle/CombatReady/CombatIdle use IdleTime, Block uses BlockTime.");
        }

        // ───────────────────────── enemies ─────────────────────────
        private static int UpgradeEnemies(Scene scene)
        {
            int count = 0;
            var actors = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<EnemyActor>(true)).ToArray();
            foreach (var actor in actors)
            {
                var a = Cast.FirstOrDefault(c => c.name == actor.displayName || c.id == actor.id || actor.gameObject.name == c.name);
                if (string.IsNullOrEmpty(a.id)) { Debug.LogWarning("[GoblinUpgrade] Unknown enemy " + actor.name); continue; }
                Undo.RecordObject(actor, "Upgrade enemy");
                actor.id = a.id; actor.windup = a.windup; actor.strikeTime = a.strike; actor.recover = a.recover;
                actor.attackRange = a.range; actor.lunge = a.lunge; actor.poise = a.poise; actor.attackCooldown = a.cooldown;
                actor.alertRadius = a.alert; actor.leash = a.leash; actor.walkSpeed = a.speed; actor.hitDamage = a.damage;

                // always (re)install: the step is idempotent and re-fits size/orientation
                if (InstallModel(actor, a, GoblinHeight(scene))) count++;
                EditorUtility.SetDirty(actor);
            }
            return count;
        }

        private static float GoblinHeight(Scene scene)
        {
            var player = scene.GetRootGameObjects().FirstOrDefault(o => o.name == "Player");
            var cc = player != null ? player.GetComponent<CharacterController>() : null;
            if (player != null)
            {
                var rs = player.GetComponentsInChildren<Renderer>();
                if (rs.Length > 0) { Bounds b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds); if (b.size.y > .5f) return b.size.y; }
            }
            return cc != null ? cc.height : 2f;
        }

        private static bool InstallModel(EnemyActor actor, Archetype a, float goblinHeight)
        {
            string path = EnemyModels + a.id + ".fbx";
            var model = PrepareModel(path);
            if (model == null) { Debug.LogWarning("[GoblinUpgrade] Model missing: " + path); return false; }

            var root = actor.transform;
            var old = actor.visual != null && actor.visual != root ? actor.visual.gameObject : null;
            // remove placeholders and leftovers of an interrupted run
            foreach (Transform child in root.Cast<Transform>().ToArray())
                if ((child.name.Contains("Oval") || child.name == "Visual") && child.gameObject != old) Undo.DestroyObjectImmediate(child.gameObject);
            if (old != null) Undo.DestroyObjectImmediate(old);

            var visual = (GameObject)PrefabUtility.InstantiatePrefab(model, root);
            Undo.RegisterCreatedObjectUndo(visual, "Enemy model");
            visual.name = "Visual";
            visual.transform.localPosition = Vector3.zero;
            // keep the importer's axis conversion stored on the model root (Blender Z-up → Unity Y-up)
            visual.transform.localRotation = model.transform.localRotation;
            visual.transform.localScale = Vector3.one;
            Debug.Log("[GoblinUpgrade] " + a.id + " prefab root rotation " + model.transform.localEulerAngles.ToString("0") +
                      ", first part rotation " + (model.transform.childCount > 0 ? model.transform.GetChild(0).localEulerAngles.ToString("0") : "-"));
            if (visual.GetComponentsInChildren<Renderer>().Length == 0) { Debug.LogWarning("[GoblinUpgrade] " + a.id + " has no renderers"); return false; }
            OrientModel(visual.transform, root, a.id);
            {
                // FBX units vary (cm vs m): fit the model to a height relative to the goblin
                var rs = visual.GetComponentsInChildren<Renderer>();
                Bounds raw = rs[0].bounds; foreach (var r in rs) raw.Encapsulate(r.bounds);
                float want = goblinHeight * a.scale;
                visual.transform.localScale = Vector3.one * (want / Mathf.Max(raw.size.y, 1e-4f));
                Debug.Log("[GoblinUpgrade] " + a.id + ": raw height " + raw.size.y.ToString("0.###") + " → " + want.ToString("0.00") + " m");
            }

            var renderers = visual.GetComponentsInChildren<Renderer>();
            Bounds b = renderers[0].bounds; foreach (var r in renderers) b.Encapsulate(r.bounds);
            // stand on the ground and centre the body over the controller
            visual.transform.position += new Vector3(root.position.x - b.center.x, root.position.y - b.min.y, root.position.z - b.center.z);
            float height = b.size.y;

            // puppet rig + keyframed clips (the FBX parts are rigid pieces)
            if (PrefabUtility.IsPartOfPrefabInstance(visual)) PrefabUtility.UnpackPrefabInstance(visual, PrefabUnpackMode.OutermostRoot, InteractionMode.AutomatedAction);
            var controller = EnemyRigBuilder.Build(visual.transform, root, new EnemyRigBuilder.Spec
            {
                id = a.id, style = a.style, windup = a.windup, strike = a.strike, recover = a.recover, walkCycle = a.cycle, heaviness = a.heavy
            }, "Assets/EnemyCast/Animations/" + a.id);
            var animator = visual.GetComponent<Animator>();
            if (animator == null) animator = visual.AddComponent<Animator>();
            animator.avatar = null; animator.runtimeAnimatorController = controller; animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            var cc = root.GetComponent<CharacterController>();
            Undo.RecordObject(cc, "Enemy collider");
            cc.height = height; cc.center = new Vector3(0, height * .5f, 0);
            cc.radius = Mathf.Clamp(Mathf.Min(b.extents.x, b.extents.z) * .8f, .25f, .7f);
            cc.stepOffset = Mathf.Min(.3f, height * .3f);

            var bar = root.Find("Health");
            if (bar != null) { Undo.RecordObject(bar, "Bar height"); bar.localPosition = Vector3.up * (height + .35f); }
            actor.visual = visual.transform;
            return true;
        }

        /// <summary>
        /// Stands the model upright and turns it to face the actor's forward.
        /// Up: of the 6 axis-aligned orientations, pick the one where the eyes sit near the top
        /// and the model is taller than a flat slab. Facing: eyes point along root.forward.
        /// </summary>
        private static void OrientModel(Transform visual, Transform root, string id)
        {
            var eyes = new List<(Transform t, Vector3 p)>();
            foreach (var r in visual.GetComponentsInChildren<Renderer>())
            {
                Mesh mesh = null;
                if (r is SkinnedMeshRenderer s) mesh = s.sharedMesh;
                else { var mf = r.GetComponent<MeshFilter>(); if (mf != null) mesh = mf.sharedMesh; }
                if (mesh == null) continue;
                var mats = r.sharedMaterials;
                for (int sub = 0; sub < Mathf.Min(mats.Length, mesh.subMeshCount); sub++)
                {
                    if (mats[sub] == null || !mats[sub].name.StartsWith("eye", StringComparison.OrdinalIgnoreCase)) continue;
                    Vector3[] verts; int[] tris;
                    try { verts = mesh.vertices; tris = mesh.GetTriangles(sub); } catch { continue; }
                    foreach (int i in tris) eyes.Add((r.transform, verts[i]));
                }
            }
            if (eyes.Count == 0) { Debug.LogWarning("[GoblinUpgrade] " + id + ": no eye material; orientation unchanged."); return; }

            Vector3 Eye() { Vector3 e = Vector3.zero; foreach (var (t, p) in eyes) e += t.TransformPoint(p); return e / eyes.Count; }
            Bounds All() { var rs = visual.GetComponentsInChildren<Renderer>(); Bounds b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds); return b; }

            Quaternion start = visual.rotation;
            Quaternion[] options = { Quaternion.identity, Quaternion.Euler(90, 0, 0), Quaternion.Euler(-90, 0, 0), Quaternion.Euler(0, 0, 90), Quaternion.Euler(0, 0, -90), Quaternion.Euler(180, 0, 0) };
            float bestScore = float.MinValue, original = 0f; Quaternion best = start; var log = new System.Text.StringBuilder();
            foreach (var o in options)
            {
                visual.rotation = o * start;
                Bounds b = All(); Vector3 e = Eye();
                float eyeHeight = (e.y - b.min.y) / Mathf.Max(b.size.y, 1e-6f);            // 1 = top of the model
                float tall = Mathf.Min(1.5f, b.size.y / Mathf.Max(b.size.x, b.size.z, 1e-6f)); // flat slab → ~0
                float score = eyeHeight * tall;
                if (o == Quaternion.identity) original = score;
                log.Append(o.eulerAngles.ToString("0") + "=" + score.ToString("0.00") + "(eye " + eyeHeight.ToString("0.00") + ", tall " + tall.ToString("0.00") + ") ");
                if (score > bestScore) { bestScore = score; best = o * start; }
            }
            // the imported orientation is trusted unless it is clearly lying down
            if (original >= bestScore * 0.6f) best = start;
            visual.rotation = best;
            Debug.Log("[GoblinUpgrade] " + id + " upright candidates: " + log + "→ " + best.eulerAngles.ToString("0"));

            Bounds all = All(); Vector3 dir = Eye() - all.center; dir.y = 0;
            if (dir.sqrMagnitude < 1e-8f) return;
            // Precise facing: the face points perpendicular to the line between the two eyes.
            // (The model centre is pulled sideways by shields / tails / weapons, so it only decides the sign.)
            var clusters = eyes.GroupBy(e => e.t).Select(g => g.Aggregate(Vector3.zero, (acc, e) => acc + e.t.TransformPoint(e.p)) / g.Count()).ToList();
            if (clusters.Count >= 2)
            {
                Vector3 a = clusters[0], b = clusters[1]; float far = 0f;
                for (int i = 0; i < clusters.Count; i++) for (int j = i + 1; j < clusters.Count; j++)
                {
                    Vector3 d = clusters[j] - clusters[i]; d.y = 0;
                    if (d.sqrMagnitude > far) { far = d.sqrMagnitude; a = clusters[i]; b = clusters[j]; }
                }
                Vector3 across = b - a; across.y = 0;
                if (across.sqrMagnitude > 1e-8f)
                {
                    Vector3 face = Vector3.Cross(across.normalized, Vector3.up);
                    if (Vector3.Dot(face, dir) < 0f) face = -face;
                    Debug.Log("[GoblinUpgrade] " + id + " facing from eye pair (centre estimate was off by " + Vector3.Angle(face, dir).ToString("0") + "°)");
                    dir = face;
                }
            }
            Vector3 want = root.forward; want.y = 0;
            // pure yaw (FromToRotation could flip around another axis for opposite vectors)
            visual.rotation = Quaternion.AngleAxis(Vector3.SignedAngle(dir, want, Vector3.up), Vector3.up) * visual.rotation;
        }

        private static GameObject PrepareModel(string path)
        {
            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer == null) return null;
            bool changed = false;
            if (importer.animationType != ModelImporterAnimationType.Generic) { importer.animationType = ModelImporterAnimationType.Generic; changed = true; }
            if (!importer.importAnimation) { importer.importAnimation = true; changed = true; }
            if (!importer.isReadable) { importer.isReadable = true; changed = true; }
            if (importer.importCameras || importer.importLights) { importer.importCameras = false; importer.importLights = false; changed = true; }
            if (importer.defaultClipAnimations.Length > 0 &&
                (importer.clipAnimations == null || importer.clipAnimations.Length == 0 || importer.clipAnimations.Any(c => c.name != "Idle" && c.name != "Walk")))
            {
                var takes = importer.defaultClipAnimations;
                foreach (var t in takes)
                {
                    if (t.name.EndsWith("Idle")) t.name = "Idle"; else if (t.name.EndsWith("Walk")) t.name = "Walk";
                    t.loopTime = true; t.loopPose = true; t.lockRootRotation = true; t.lockRootHeightY = true; t.lockRootPositionXZ = true;
                }
                importer.clipAnimations = takes; changed = true;
            }
            // keep the model's own Blender colours (undo palette remaps of an earlier pass)
            foreach (var pair in importer.GetExternalObjectMap().ToArray())
                if (pair.Key.type == typeof(Material) && AssetDatabase.GetAssetPath(pair.Value).StartsWith(PaletteFolder))
                { importer.RemoveRemap(pair.Key); changed = true; }
            if (changed) importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<GameObject>(path);
        }

        private static RuntimeAnimatorController PrepareController(string id, string modelPath)
        {
            if (!AssetDatabase.IsValidFolder(EnemyControllers)) AssetDatabase.CreateFolder("Assets/EnemyCast", "Controllers");
            string path = EnemyControllers + "/" + id + ".controller";
            var existing = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            if (existing != null) return existing;
            var clips = AssetDatabase.LoadAllAssetsAtPath(modelPath).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__")).ToArray();
            var controller = AnimatorController.CreateAnimatorControllerAtPath(path);
            var machine = controller.layers[0].stateMachine;
            foreach (string name in new[] { "Idle", "Walk" })
            {
                var clip = clips.FirstOrDefault(c => c.name == name) ?? clips.FirstOrDefault(c => c.name.EndsWith(name));
                if (clip == null) { Debug.LogWarning("[GoblinUpgrade] " + id + " has no " + name + " clip"); continue; }
                var state = machine.AddState(name); state.motion = clip;
                if (name == "Idle") machine.defaultState = state;
            }
            EditorUtility.SetDirty(controller); EditorUtility.SetDirty(machine);
            AssetDatabase.SaveAssets();
            return controller;
        }
    }
}
