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
        }
        private static readonly Archetype[] Cast =
        {
            new Archetype{id="RatRogue",    name="Крысолов",       scale=.80f,windup=.45f,strike=.22f,recover=.70f,range=1.20f,lunge=1.3f,poise=18f,cooldown=.7f,alert=7f,leash=16f,speed=1.6f,damage=10f},
            new Archetype{id="MossGourmand",name="Грибоед",        scale=.85f,windup=1.0f,strike=.35f,recover=1.3f,range=1.50f,lunge=.5f, poise=60f,cooldown=1.6f,alert=5f,leash=14f,speed=.75f,damage=20f},
            new Archetype{id="BoneSentinel",name="Костяной страж", scale=.85f,windup=.70f,strike=.28f,recover=1.0f,range=1.45f,lunge=.8f, poise=40f,cooldown=1.1f,alert=6f,leash=16f,speed=1.05f,damage=16f},
            new Archetype{id="IronTusk",    name="Железный клык",  scale=1.0f,windup=1.15f,strike=.38f,recover=1.4f,range=1.90f,lunge=1.0f,poise=90f,cooldown=1.4f,alert=8f,leash=18f,speed=1.15f,damage=30f},
        };

        static GoblinGameUpgrade()
        {
            string request = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Temp/GoblinUpgrade.request");
            if (!File.Exists(request)) return;
            EditorApplication.delayCall += () => RunQueued(request);
        }

        private static void RunQueued(string request)
        {
            if (!File.Exists(request)) return;
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
            { EditorApplication.delayCall += () => RunQueued(request); return; }
            var scene = SceneManager.GetActiveScene();
            if (scene.path != ScenePath) { Debug.LogWarning("[GoblinUpgrade] Open " + ScenePath + " and choose Goblingame > Upgrade Camera, Combat and Enemies."); File.Delete(request); return; }
            if (scene.isDirty) { Debug.LogWarning("[GoblinUpgrade] Scene has unsaved changes — save it, then choose Goblingame > Upgrade Camera, Combat and Enemies."); File.Delete(request); return; }
            File.Delete(request);
            try { Apply(); } catch (Exception e) { Debug.LogException(e); }
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
            foreach (string p in new[] { "IdleTime", "BlockTime" })
                if (!controller.parameters.Any(x => x.name == p)) controller.AddParameter(p, AnimatorControllerParameterType.Float);
            foreach (var child in controller.layers[0].stateMachine.states)
            {
                var s = child.state;
                string param = s.name == "Idle" || s.name == "CombatReady" || s.name == "CombatIdle" ? "IdleTime" : s.name == "Block" ? "BlockTime" : null;
                if (param == null) continue;
                s.timeParameter = param; s.timeParameterActive = true;
                EditorUtility.SetDirty(s);
            }
            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
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

                bool hasModel = actor.visual != null && actor.visual.GetComponentInChildren<SkinnedMeshRenderer>() != null;
                if (!hasModel && InstallModel(actor, a)) count++;
                EditorUtility.SetDirty(actor);
            }
            return count;
        }

        private static bool InstallModel(EnemyActor actor, Archetype a)
        {
            string path = EnemyModels + a.id + ".fbx";
            var model = PrepareModel(path);
            if (model == null) { Debug.LogWarning("[GoblinUpgrade] Model missing: " + path); return false; }
            var controller = PrepareController(a.id, path);

            var root = actor.transform;
            var old = actor.visual != null && actor.visual != root ? actor.visual.gameObject : null;
            foreach (Transform child in root.Cast<Transform>().ToArray())
                if (child.name.Contains("Oval") && child.gameObject != old) Undo.DestroyObjectImmediate(child.gameObject);
            if (old != null) Undo.DestroyObjectImmediate(old);

            var visual = (GameObject)PrefabUtility.InstantiatePrefab(model, root);
            Undo.RegisterCreatedObjectUndo(visual, "Enemy model");
            visual.name = "Visual";
            visual.transform.localPosition = Vector3.zero;
            visual.transform.localRotation = Quaternion.identity;
            visual.transform.localScale = Vector3.one * a.scale;
            var animator = visual.GetComponent<Animator>() ?? visual.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller; animator.applyRootMotion = false;

            FaceForward(visual.transform, root);
            var renderers = visual.GetComponentsInChildren<Renderer>();
            Bounds b = renderers[0].bounds; foreach (var r in renderers) b.Encapsulate(r.bounds);
            // stand on the ground and centre the body over the controller
            visual.transform.position += new Vector3(root.position.x - b.center.x, root.position.y - b.min.y, root.position.z - b.center.z);
            float height = b.size.y;

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

        /// <summary>Turns the model so the centre of its eyes points along the actor's forward.</summary>
        private static void FaceForward(Transform visual, Transform root)
        {
            var smr = visual.GetComponentInChildren<SkinnedMeshRenderer>();
            if (smr == null || smr.sharedMesh == null) return;
            var mesh = smr.sharedMesh;
            int eyeIndex = Array.FindIndex(smr.sharedMaterials, m => m != null && m.name.StartsWith("eye", StringComparison.OrdinalIgnoreCase));
            if (eyeIndex < 0 || eyeIndex >= mesh.subMeshCount) return;
            Vector3[] verts; int[] tris;
            try { verts = mesh.vertices; tris = mesh.GetTriangles(eyeIndex); } catch { return; }
            if (tris.Length == 0) return;
            Vector3 eye = Vector3.zero; foreach (int i in tris) eye += verts[i]; eye /= tris.Length;
            Vector3 all = Vector3.zero; foreach (var v in verts) all += v; all /= verts.Length;
            Vector3 dir = smr.transform.TransformDirection(eye - all); dir.y = 0;
            if (dir.sqrMagnitude < 1e-6f) return;
            Vector3 want = root.forward; want.y = 0;
            visual.rotation = Quaternion.FromToRotation(dir.normalized, want.normalized) * visual.rotation;
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
            if (importer.clipAnimations == null || importer.clipAnimations.Length == 0 || importer.clipAnimations.Any(c => c.name != "Idle" && c.name != "Walk"))
            {
                var takes = importer.defaultClipAnimations;
                foreach (var t in takes)
                {
                    if (t.name.EndsWith("Idle")) t.name = "Idle"; else if (t.name.EndsWith("Walk")) t.name = "Walk";
                    t.loopTime = true; t.loopPose = true; t.lockRootRotation = true; t.lockRootHeightY = true; t.lockRootPositionXZ = true;
                }
                importer.clipAnimations = takes; changed = true;
            }
            // reuse the palette materials made for the original enemy pass, if present
            foreach (var source in AssetDatabase.LoadAllAssetsAtPath(path).OfType<Material>())
            {
                string key = source.name.Split('.')[0];
                var mat = AssetDatabase.LoadAssetAtPath<Material>(PaletteFolder + key + ".mat");
                if (mat == null) continue;
                var id = new AssetImporter.SourceAssetIdentifier(typeof(Material), source.name);
                if (importer.GetExternalObjectMap().TryGetValue(id, out var existing) && existing == mat) continue;
                importer.AddRemap(id, mat); changed = true;
            }
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
