using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Goblingame.TestScene.Editor
{
    [InitializeOnLoad]
    public static class GoblinRecoveredGraphicsSetup
    {
        // This recovered goblin shares the original mesh while its Mixamo rig matches the supplied clips.
        private const string ModelPath = "Assets/RecoveredGoblinGraphics/BlenderSource/\u0433\u043e\u0431\u043b\u0438\u043d \u0441 \u0432\u044b\u0441\u043e\u043a\u0438\u043c \u043b\u0438\u0446\u043e\u043c.blend";
        private const string TexturePath = "Assets/RecoveredGoblinGraphics/Character/goblin color.png";
        private const string AnimPath = "Assets/RecoveredGoblinGraphics/Animations/";
        private const string GeneratedPath = "Assets/RecoveredGoblinGraphics/Generated/";
        private const string ControllerPath = GeneratedPath + "GoblinRecovered.controller";
        private const string PrefabPath = GeneratedPath + "RecoveredGoblin.prefab";
        private const string MaterialPath = GeneratedPath + "GoblinAtlas.mat";

        static GoblinRecoveredGraphicsSetup()
        {
            // Let an already-open Editor apply the scene pass through Unity's scene
            // APIs after this script recompiles. The request is one-shot.
            string request = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Temp/GoblinPolish.request");
            if (!File.Exists(request)) return;
            EditorApplication.delayCall += () => ProcessPendingRepair(request);
        }

        private static void ProcessPendingRepair(string request)
        {
            if (!File.Exists(request)) return;
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorApplication.playModeStateChanged -= OnRepairPlayModeChanged;
                EditorApplication.playModeStateChanged += OnRepairPlayModeChanged;
                return;
            }

            string command = File.ReadAllText(request).Trim();
            File.Delete(request);
            try
            {
                const string scenePath = "Assets/Scenes/GoblinPlayable.unity";
                if (SceneManager.GetActiveScene().path != scenePath)
                {
                    Debug.LogError("Goblingame repair stopped: open GoblinPlayable before running the queued repair. No scene was changed.");
                    return;
                }
                if (command == "remove-left-goblin") RemoveExtraTrainingDummyGoblin();
                else RestoreRightGoblinSafely();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        private static void OnRepairPlayModeChanged(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.EnteredEditMode) return;
            EditorApplication.playModeStateChanged -= OnRepairPlayModeChanged;
            string request = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Temp/GoblinPolish.request");
            EditorApplication.delayCall += () => ProcessPendingRepair(request);
        }

        [MenuItem("Goblingame/Recovered Graphics/Polish Goblin Scene")]
        public static void PolishScene() => PolishScene(true);

        [MenuItem("Goblingame/Repair Open Goblin Scene")]
        public static void RepairOpenGoblinScene()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogError("Stop Play Mode, then run Goblingame > Repair Open Goblin Scene again.");
                return;
            }
            RestoreRightGoblinSafely();
        }

        [MenuItem("Goblingame/Remove Extra Goblin From Training Dummy")]
        public static void RemoveExtraGoblinFromTrainingDummy()
        {
            // Keep the target anchor and its oval while deleting stray character renderers.
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogError("Stop Play Mode, then run Goblingame > Remove Extra Goblin From Training Dummy again.");
                return;
            }
            if (SceneManager.GetActiveScene().path != "Assets/Scenes/GoblinPlayable.unity")
            {
                Debug.LogError("Open GoblinPlayable first; no scene objects were changed.");
                return;
            }
            RemoveExtraTrainingDummyGoblin();
        }

        private static void RestoreRightGoblinSafely()
        {
            var player = GameObject.Find("Player");
            var dummy = UnityEngine.Object.FindFirstObjectByType<TrainingDummy>();
            if (player == null || dummy == null)
            {
                Debug.LogError("Could not find Player and Training Dummy in the active scene; no objects were changed.");
                return;
            }

            // The target goblin stood at the Training Dummy position in the user's
            // scene. Earlier passes replaced that visual with an oval, so restore
            // the rigged source prefab there while preserving all unrelated objects.
            Vector3 rightPosition = dummy.transform.position;
            Quaternion rightRotation = dummy.transform.rotation;
            Vector3 priorPlayerPosition = player.transform.position;
            Quaternion priorPlayerRotation = player.transform.rotation;
            Undo.RegisterFullObjectHierarchyUndo(player, "Restore right goblin to Player");
            Undo.RegisterFullObjectHierarchyUndo(dummy.gameObject, "Keep training target in its place");

            Transform goblin = dummy.transform.Cast<Transform>()
                .FirstOrDefault(child => child.name != "Training Oval" && child.GetComponentsInChildren<SkinnedMeshRenderer>(true).Any(r => r.enabled));
            bool recoveredFromRight = goblin != null;
            bool alreadyInPlayer = false;
            if (goblin != null)
            {
                // This is the actual skinned character from the right-hand target.
                // Move its complete hierarchy, including its original bones.
                goblin.SetParent(player.transform, false);
            }
            else
            {
                goblin = player.transform.Cast<Transform>()
                    .FirstOrDefault(child => child.GetComponentsInChildren<SkinnedMeshRenderer>(true).Any(r => r.enabled));
                alreadyInPlayer = goblin != null;
            }
            if (goblin == null)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
                if (prefab == null)
                {
                    Debug.LogError("Recovered goblin prefab is missing; no scene objects were changed.");
                    return;
                }

                // Remove only the specific placeholder oval accidentally attached
                // to Player. Keep every other child, script and scene object intact.
                foreach (Transform child in player.transform.Cast<Transform>().ToArray())
                {
                    var filter = child.GetComponent<MeshFilter>();
                    if (child.name == "Training Oval" && child.childCount == 0 && filter != null)
                        Undo.DestroyObjectImmediate(child.gameObject);
                }

                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, player.scene);
                instance.name = "RecoveredGoblin";
                instance.transform.SetParent(player.transform, false);
                instance.transform.localPosition = Vector3.zero;
                instance.transform.localRotation = Quaternion.identity;
                instance.transform.localScale = Vector3.one;
                goblin = instance.transform;
            }

            // Remove only the known earlier Player visual and the placeholder oval
            // when the right model has just been restored. Never clear other children.
            if (!alreadyInPlayer)
            {
                foreach (Transform child in player.transform.Cast<Transform>().ToArray())
                {
                    if (child == goblin) continue;
                    bool knownOldGoblin = child.name == "RecoveredGoblin" || child.name == "RecoveredGoblinVisual";
                    bool knownPlaceholder = child.name == "Training Oval" && child.GetComponent<MeshFilter>() != null && child.childCount == 0;
                    if (knownOldGoblin || knownPlaceholder) Undo.DestroyObjectImmediate(child.gameObject);
                }
            }

            if (!alreadyInPlayer)
            {
                player.transform.SetPositionAndRotation(rightPosition, rightRotation);
                goblin.localPosition = Vector3.zero;
                goblin.localRotation = Quaternion.identity;
                dummy.transform.SetPositionAndRotation(priorPlayerPosition, priorPlayerRotation);
            }

            var animationRig = recoveredFromRight ? goblin.gameObject : AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            var controller = BuildController(animationRig);
            if (controller == null)
            {
                Debug.LogError("Goblin animation controller could not be built; no further scene changes were made.");
                return;
            }
            // The generated clips are bound relative to the imported Blender
            // model root. Keep the Animator on that same root, otherwise every
            // animated bone path gains an extra prefab-wrapper segment.
            string importedRootName = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath)?.name;
            Transform modelRoot = string.IsNullOrEmpty(importedRootName) ? null : goblin.Find(importedRootName);
            if (modelRoot == null && !string.IsNullOrEmpty(importedRootName))
                modelRoot = goblin.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == importedRootName);
            if (modelRoot == null) modelRoot = goblin;
            var animator = modelRoot.GetComponent<Animator>();
            if (animator == null) animator = modelRoot.gameObject.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;
            PrefabUtility.RecordPrefabInstancePropertyModifications(animator);
            // These hand-retargeted clips contain Generic transform bindings. Do not
            // assign the source model Avatar here: the Animator must evaluate the
            // explicit bone paths written into the generated AnimationClips.
            animator.avatar = null;
            // Cube is the skinned goblin bound to metarig. Cube.001 is a detached
            // static duplicate offset far from the scene origin.
            Transform duplicate = modelRoot.GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(t => t.name == "Cube.001");
            if (duplicate != null)
            {
                Undo.RecordObject(duplicate.gameObject, "Hide detached duplicate goblin mesh");
                duplicate.gameObject.SetActive(false);
                PrefabUtility.RecordPrefabInstancePropertyModifications(duplicate.gameObject);
            }
            Transform skinnedGoblin = modelRoot.GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(t => t.name == "Cube");
            if (skinnedGoblin != null && !skinnedGoblin.gameObject.activeSelf)
            {
                Undo.RecordObject(skinnedGoblin.gameObject, "Enable rigged playable goblin");
                skinnedGoblin.gameObject.SetActive(true);
                PrefabUtility.RecordPrefabInstancePropertyModifications(skinnedGoblin.gameObject);
            }
            AddNoseTriangle(goblin, player.transform);
            AlignPlayableVisualToPlayer(goblin, player.transform);
            var movement = player.GetComponent<GoblinMovement>();
            if (movement != null)
            {
                var serialized = new SerializedObject(movement);
                serialized.FindProperty("animator").objectReferenceValue = animator;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(movement);
            }
            var combat = player.GetComponent<GoblinCombat>();
            if (combat != null)
            {
                var serialized = new SerializedObject(combat);
                serialized.FindProperty("animator").objectReferenceValue = animator;
                serialized.FindProperty("fallbackController").objectReferenceValue = controller;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(combat);
            }

            EditorUtility.SetDirty(player);

            var dummyField = new SerializedObject(dummy).FindProperty("visual");
            var ovalTransform = dummy.transform.Find("Training Oval");
            Renderer oval = ovalTransform != null ? ovalTransform.GetComponent<Renderer>() : null;
            if (oval == null)
            {
                var ovalObject = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                ovalObject.name = "Training Oval";
                ovalObject.transform.SetParent(dummy.transform, false);
                ovalObject.transform.localPosition = new Vector3(0f, 0.82f, 0f);
                ovalObject.transform.localScale = new Vector3(0.76f, 0.86f, 0.68f);
                UnityEngine.Object.DestroyImmediate(ovalObject.GetComponent<Collider>());
                oval = ovalObject.GetComponent<Renderer>();
                var ovalMat = AssetDatabase.LoadAssetAtPath<Material>(GeneratedPath + "TrainingOval.mat");
                if (ovalMat != null) oval.sharedMaterial = ovalMat;
            }
            dummyField.objectReferenceValue = oval;
            dummyField.serializedObject.ApplyModifiedPropertiesWithoutUndo();
            Scene scene = SceneManager.GetActiveScene();
            RemoveMissingScripts(scene);
            EditorSceneManager.MarkSceneDirty(scene);
            AssetDatabase.SaveAssets();
            EditorSceneManager.SaveScene(scene);
            Selection.activeGameObject = player;
            Debug.Log(recoveredFromRight
                ? "Goblingame recovery complete: kept the actual right-hand goblin, removed the detached duplicate mesh, and bound animation to the imported rig root."
                : alreadyInPlayer
                    ? "Goblingame recovery checked: kept the existing rigged goblin and refreshed animation bindings without moving scene objects."
                    : "Goblingame recovery complete: restored the rigged goblin from the saved project model at the former right-hand position and rebuilt its animation bindings.");
        }

        private static void AlignPlayableVisualToPlayer(Transform visual, Transform player)
        {
            var renderers = visual.GetComponentsInChildren<Renderer>(true)
                .Where(renderer => renderer.enabled && renderer.gameObject.activeInHierarchy)
                .ToArray();
            if (renderers.Length == 0)
            {
                Debug.LogWarning("Playable goblin alignment skipped: no active renderers were found.");
                return;
            }

            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);

            // Keep the visible body centered on the controller's ground point.
            // Imported Blender meshes can have a pivot several meters away from
            // their geometry, which makes the player rotate around empty space.
            Vector3 visualGroundCenter = new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
            Vector3 playerGroundPoint = player.position + Vector3.up * 0.02f;
            Vector3 correction = playerGroundPoint - visualGroundCenter;
            if (correction.sqrMagnitude > 0.0001f)
            {
                Undo.RecordObject(visual, "Center playable goblin on player");
                visual.position += correction;
                EditorUtility.SetDirty(visual);
            }
        }

        private static void RemoveExtraTrainingDummyGoblin()
        {
            var player = GameObject.Find("Player");
            var dummy = UnityEngine.Object.FindFirstObjectByType<TrainingDummy>();
            Transform keptGoblin = player != null
                ? player.transform.Cast<Transform>().FirstOrDefault(child =>
                    child.GetComponentsInChildren<SkinnedMeshRenderer>(true).Any(r => r.enabled))
                : null;
            if (player == null || dummy == null || keptGoblin == null)
            {
                Debug.LogError("Extra goblin cleanup stopped: Player/RecoveredGoblin or Training Dummy was not found. No objects were changed.");
                return;
            }

            Undo.RegisterFullObjectHierarchyUndo(dummy.gameObject, "Remove extra goblin and keep training target");
            // Older versions used static MeshRenderers for the left character,
            // so remove rendered model hierarchies regardless of rig type.
            var extras = dummy.transform.Cast<Transform>()
                .Where(child => child.name != "Training Oval" &&
                    child.GetComponentsInChildren<Renderer>(true).Any(r => r.enabled))
                .ToArray();
            foreach (var extra in extras) Undo.DestroyObjectImmediate(extra.gameObject);

            // The former goblin was also sometimes rendered directly on the
            // Training Dummy anchor. Its replacement oval is a separate child.
            foreach (var renderer in dummy.GetComponents<Renderer>()) Undo.DestroyObjectImmediate(renderer);
            var rootMesh = dummy.GetComponent<MeshFilter>();
            if (rootMesh != null) Undo.DestroyObjectImmediate(rootMesh);

            Transform ovalTransform = dummy.transform.Find("Training Oval");
            Renderer oval = ovalTransform != null ? ovalTransform.GetComponent<Renderer>() : null;
            if (oval == null)
            {
                var ovalObject = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                ovalObject.name = "Training Oval";
                ovalObject.transform.SetParent(dummy.transform, false);
                ovalObject.transform.localPosition = new Vector3(0f, 0.82f, 0f);
                ovalObject.transform.localScale = new Vector3(0.76f, 0.86f, 0.68f);
                UnityEngine.Object.DestroyImmediate(ovalObject.GetComponent<Collider>());
                oval = ovalObject.GetComponent<Renderer>();
                var ovalMat = AssetDatabase.LoadAssetAtPath<Material>(GeneratedPath + "TrainingOval.mat");
                if (ovalMat != null) oval.sharedMaterial = ovalMat;
            }

            var visualField = new SerializedObject(dummy).FindProperty("visual");
            visualField.objectReferenceValue = oval;
            visualField.serializedObject.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(dummy);
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            AssetDatabase.SaveAssets();
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
            Selection.activeGameObject = keptGoblin.gameObject;
            Debug.Log("Goblingame cleanup complete: kept Player/RecoveredGoblin and removed only the extra skinned goblin under Training Dummy.");
        }

        private static void AddNoseTriangle(Transform visual, Transform facingRoot)
        {
            var head = visual.GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(t => t.name.IndexOf("head", StringComparison.OrdinalIgnoreCase) >= 0);
            Transform patchParent = head != null ? head : visual;
            var prior = patchParent.Find("GoblinNoseTriangle");
            if (prior != null) UnityEngine.Object.DestroyImmediate(prior.gameObject);

            Vector3 center;
            Vector3 right;
            Vector3 up;
            Vector3 front;
            if (head != null)
            {
                center = head.position + facingRoot.forward * 0.14f - facingRoot.up * 0.045f;
                right = facingRoot.right * 0.045f;
                up = facingRoot.up * 0.035f;
                front = facingRoot.forward * 0.008f;
            }
            else
            {
                var renderers = visual.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(r => r.enabled).ToArray();
                if (renderers.Length == 0) return;
                Bounds bounds = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
                Vector3 face = Camera.main != null ? Camera.main.transform.position - bounds.center : -facingRoot.forward;
                face = Vector3.ProjectOnPlane(face, Vector3.up).normalized;
                if (face.sqrMagnitude < 0.5f) face = -facingRoot.forward;
                Vector3 side = Vector3.Cross(Vector3.up, face).normalized;
                float depth = Mathf.Abs(face.x) * bounds.extents.x + Mathf.Abs(face.z) * bounds.extents.z;
                center = bounds.center + Vector3.up * (bounds.extents.y * 0.72f) + face * (depth + 0.006f);
                right = side * Mathf.Clamp(bounds.extents.x * 0.15f, 0.035f, 0.075f);
                up = Vector3.up * Mathf.Clamp(bounds.extents.y * 0.08f, 0.025f, 0.055f);
                front = face * 0.004f;
                Debug.Log("Goblin has no named head bone; nose triangle was positioned from the head end of its skinned mesh bounds.");
            }
            var mesh = new Mesh { name = "GoblinNoseTriangle" };
            Transform meshRoot = patchParent;
            mesh.vertices = new[]
            {
                meshRoot.InverseTransformPoint(center - right + up + front),
                meshRoot.InverseTransformPoint(center - front),
                meshRoot.InverseTransformPoint(center + right + up + front)
            };
            mesh.triangles = new[] { 0, 1, 2 };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            const string meshPath = GeneratedPath + "GoblinNoseTriangle.asset";
            var oldMesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
            if (oldMesh != null) AssetDatabase.DeleteAsset(meshPath);
            AssetDatabase.CreateAsset(mesh, meshPath);

            const string matPath = GeneratedPath + "GoblinNose.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"))
                { name = "GoblinNose" };
                AssetDatabase.CreateAsset(material, matPath);
            }
            material.color = new Color(0.17f, 0.22f, 0.075f, 1f);
            var patch = new GameObject("GoblinNoseTriangle");
            patch.transform.SetParent(patchParent, false);
            patch.AddComponent<MeshFilter>().sharedMesh = mesh;
            patch.AddComponent<MeshRenderer>().sharedMaterial = material;
        }

        private static void PolishScene(bool showDialog)
        {
            var scene = SceneManager.GetActiveScene();
            var player = GameObject.Find("Player");
            var controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(ControllerPath);
            if (player == null || controller == null)
            {
                if (showDialog) EditorUtility.DisplayDialog("Recovered Goblin", "Не найдены Player или контроллер анимаций. Сначала выбери Build Textured Animated Goblin.", "OK");
                else Debug.LogError("Could not find Player or the recovered animation controller.");
                return;
            }

            // Refine the imported atlas material without tinting the hand-painted texture.
            var goblinMaterial = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (goblinMaterial != null)
            {
                goblinMaterial.color = Color.white;
                if (goblinMaterial.HasProperty("_Smoothness")) goblinMaterial.SetFloat("_Smoothness", 0.2f);
                if (goblinMaterial.HasProperty("_Glossiness")) goblinMaterial.SetFloat("_Glossiness", 0.2f);
                EditorUtility.SetDirty(goblinMaterial);
            }

            var ground = GameObject.Find("Ground");
            if (ground != null && ground.GetComponent<Renderer>() != null)
            {
                var mat = AssetDatabase.LoadAssetAtPath<Material>(GeneratedPath + "RecoveredGround.mat");
                if (mat != null)
                {
                    var earth = new Color(0.105f, 0.145f, 0.09f);
                    if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", earth);
                    else mat.color = earth;
                    EditorUtility.SetDirty(mat);
                    ground.GetComponent<Renderer>().sharedMaterial = mat;
                }
            }

            var sun = GameObject.Find("Directional Light");
            if (sun != null && sun.TryGetComponent<Light>(out var keyLight))
            {
                keyLight.intensity = 1.15f;
                keyLight.color = new Color(1f, 0.91f, 0.78f);
                sun.transform.rotation = Quaternion.Euler(48f, -32f, 0f);
                RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
                RenderSettings.ambientSkyColor = new Color(0.43f, 0.53f, 0.66f);
                RenderSettings.ambientEquatorColor = new Color(0.34f, 0.33f, 0.27f);
                RenderSettings.ambientGroundColor = new Color(0.13f, 0.12f, 0.09f);
                RenderSettings.ambientIntensity = 0.85f;
            }

            var dummy = UnityEngine.Object.FindFirstObjectByType<TrainingDummy>();
            if (dummy != null)
            {
                var visualField = new SerializedObject(dummy).FindProperty("visual");
                ReplaceWithOval(dummy.transform, "Training Oval", new Color(0.72f, 0.34f, 0.13f), null, out var renderer);
                visualField.objectReferenceValue = renderer;
                visualField.serializedObject.ApplyModifiedPropertiesWithoutUndo();
            }

            // Keep each enemy's actor, health and combat behavior, but replace missing
            // character prefabs with simple, readable oval silhouettes.
            foreach (var enemy in UnityEngine.Object.FindObjectsByType<EnemyActor>(FindObjectsSortMode.None))
            {
                Color tint = enemy.elite ? new Color(0.65f, 0.3f, 0.72f) :
                    enemy.displayName.IndexOf("rat", StringComparison.OrdinalIgnoreCase) >= 0 ? new Color(0.58f, 0.48f, 0.35f) :
                    enemy.displayName.IndexOf("bone", StringComparison.OrdinalIgnoreCase) >= 0 ? new Color(0.76f, 0.72f, 0.59f) :
                    new Color(0.37f, 0.54f, 0.28f);
                ReplaceWithOval(enemy.transform, "Enemy Oval", tint, controller, out _);
                enemy.visual = enemy.transform.Find("Enemy Oval");
                EditorUtility.SetDirty(enemy);
            }

            // A restrained brass accent refresh for the existing HUD and bestiary.
            foreach (var frame in UnityEngine.Object.FindObjectsByType<ForgedFrame>(FindObjectsSortMode.None))
            {
                frame.rim = new Color(0.48f, 0.37f, 0.23f, 1f);
                frame.interior = new Color(0.045f, 0.052f, 0.045f, 0.97f);
                frame.SetVerticesDirty();
                EditorUtility.SetDirty(frame);
            }

            // A longer minimum blend softens transitions among the recovered clips.
            var combat = player.GetComponent<GoblinCombat>();
            if (combat != null) EditorUtility.SetDirty(combat);
            EditorSceneManager.MarkSceneDirty(scene);
            AssetDatabase.SaveAssets();
            EditorSceneManager.SaveScene(scene);
            if (showDialog) EditorUtility.DisplayDialog("Recovered Goblin", "Готово: восстановлена модель, текстура, овальные противники и свет.", "OK");
        }

        private static void ReplaceWithOval(Transform root, string name, Color tint, RuntimeAnimatorController controller, out Renderer renderer)
        {
            for (int i = root.childCount - 1; i >= 0; i--)
                UnityEngine.Object.DestroyImmediate(root.GetChild(i).gameObject);
            var oval = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            oval.name = name;
            oval.transform.SetParent(root, false);
            oval.transform.localPosition = new Vector3(0f, 0.82f, 0f);
            oval.transform.localRotation = Quaternion.identity;
            oval.transform.localScale = new Vector3(0.76f, 0.86f, 0.68f);
            UnityEngine.Object.DestroyImmediate(oval.GetComponent<Collider>());
            renderer = oval.GetComponent<Renderer>();
            string matPath = GeneratedPath + name.Replace(" ", "") + ".mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard")) { name = name.Replace(" ", "") };
                AssetDatabase.CreateAsset(mat, matPath);
            }
            mat.color = tint;
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.12f);
            if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", 0.12f);
            renderer.sharedMaterial = mat;
            if (controller != null)
            {
                var animator = oval.AddComponent<Animator>();
                animator.runtimeAnimatorController = controller;
                animator.applyRootMotion = false;
            }
        }

        [MenuItem("Goblingame/Recovered Graphics/Build Textured Animated Goblin")]
        public static void Build()
        {
            Build(true);
        }

        private static void Build(bool showDialog)
        {
            if (!Directory.Exists(GeneratedPath)) Directory.CreateDirectory(GeneratedPath);
            AssetDatabase.Refresh();
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath);
            if (source == null || texture == null)
            {
                if (showDialog) EditorUtility.DisplayDialog("Recovered Goblin", "Модель или UV-текстура не найдена в Assets/RecoveredGoblinGraphics.", "OK");
                else Debug.LogError("Recovered goblin model or UV texture was not found.");
                return;
            }

            Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                material = new Material(shader) { name = "GoblinAtlas" };
                material.mainTexture = texture;
                if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", texture);
                AssetDatabase.CreateAsset(material, MaterialPath);
            }
            else
            {
                material.mainTexture = texture;
                if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", texture);
                EditorUtility.SetDirty(material);
            }

            var controller = BuildController(source);
            var wrapper = new GameObject("RecoveredGoblinVisual");
            var model = (GameObject)PrefabUtility.InstantiatePrefab(source);
            model.transform.SetParent(wrapper.transform, false);
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;
            model.transform.localScale = Vector3.one;
            Transform duplicateMesh = model.GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(t => t.name == "Cube.001");
            if (duplicateMesh != null) duplicateMesh.gameObject.SetActive(false);
            Transform skinnedMesh = model.GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(t => t.name == "Cube");
            if (skinnedMesh != null) skinnedMesh.gameObject.SetActive(true);
            foreach (Transform node in model.GetComponentsInChildren<Transform>(true))
                GameObjectUtility.RemoveMonoBehavioursWithMissingScript(node.gameObject);
            foreach (var renderer in wrapper.GetComponentsInChildren<Renderer>(true))
            {
                var slots = renderer.sharedMaterials;
                for (int i = 0; i < slots.Length; i++) slots[i] = material;
                renderer.sharedMaterials = slots;
            }
            var animator = model.GetComponent<Animator>();
            if (animator == null) animator = model.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;
            animator.avatar = null;
            Bounds bounds = default;
            bool hasBounds = false;
            foreach (var renderer in wrapper.GetComponentsInChildren<Renderer>(true))
            {
                if (!hasBounds) { bounds = renderer.bounds; hasBounds = true; }
                else bounds.Encapsulate(renderer.bounds);
            }
            if (hasBounds && bounds.size.y > 0.001f) model.transform.localScale = Vector3.one * (1.7f / bounds.size.y);
            PrefabUtility.SaveAsPrefabAsset(wrapper, PrefabPath);
            UnityEngine.Object.DestroyImmediate(wrapper);

            Scene current = SceneManager.GetActiveScene();
            var player = GameObject.Find("Player");
            if (player == null)
            {
                if (showDialog) EditorUtility.DisplayDialog("Recovered Goblin", "В открытой сцене не найден объект Player.", "OK");
                else Debug.LogError("Player object was not found in the active Goblin scene.");
                return;
            }
            foreach (Transform child in player.transform.Cast<Transform>().ToArray())
                if (child.name.Equals("Visual", StringComparison.OrdinalIgnoreCase) || child.name.IndexOf("GoblinVisual", StringComparison.OrdinalIgnoreCase) >= 0 || child.name.IndexOf("RecoveredGoblin", StringComparison.OrdinalIgnoreCase) >= 0)
                    UnityEngine.Object.DestroyImmediate(child.gameObject);
            foreach (var root in current.GetRootGameObjects())
                if (root != player && root.name.IndexOf("RecoveredGoblinVisual", StringComparison.OrdinalIgnoreCase) >= 0)
                    UnityEngine.Object.DestroyImmediate(root);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath));
            instance.transform.SetParent(player.transform, false);
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            instance.transform.localScale = Vector3.one;
            AlignPlayableVisualToPlayer(instance.transform, player.transform);
            var ground = GameObject.Find("Ground");
            var groundRenderer = ground != null ? ground.GetComponent<Renderer>() : null;
            if (groundRenderer != null)
            {
                const string groundMaterialPath = GeneratedPath + "RecoveredGround.mat";
                var groundMaterial = AssetDatabase.LoadAssetAtPath<Material>(groundMaterialPath);
                if (groundMaterial == null)
                {
                    var groundShader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                    groundMaterial = new Material(groundShader) { name = "RecoveredGround" };
                    if (groundMaterial.HasProperty("_BaseColor")) groundMaterial.SetColor("_BaseColor", new Color(0.19f, 0.25f, 0.12f));
                    else groundMaterial.color = new Color(0.19f, 0.25f, 0.12f);
                    AssetDatabase.CreateAsset(groundMaterial, groundMaterialPath);
                }
                groundRenderer.sharedMaterial = groundMaterial;
            }

            // Bind the live Animator from the prefab instance in the scene.
            // The temporary object used to write the prefab is destroyed above.
            var playerAnimator = instance.GetComponentsInChildren<Transform>(true)
                .Select(t => t.GetComponent<Animator>()).FirstOrDefault(a => a != null);
            if (playerAnimator == null)
            {
                playerAnimator = instance.AddComponent<Animator>();
                Debug.LogWarning("Recovered prefab had no Animator component after instantiation; added one to the playable root.");
            }
            playerAnimator.runtimeAnimatorController = controller;
            playerAnimator.applyRootMotion = false;
            PrefabUtility.RecordPrefabInstancePropertyModifications(playerAnimator);
            var movement = player.GetComponent<GoblinMovement>();
            if (movement != null)
            {
                var movementSerialized = new SerializedObject(movement);
                movementSerialized.FindProperty("animator").objectReferenceValue = playerAnimator;
                movementSerialized.ApplyModifiedPropertiesWithoutUndo();
            }
            var combat = player.GetComponent<GoblinCombat>();
            if (combat != null)
            {
                var combatSerialized = new SerializedObject(combat);
                combatSerialized.FindProperty("animator").objectReferenceValue = playerAnimator;
                combatSerialized.FindProperty("fallbackController").objectReferenceValue = controller;
                combatSerialized.ApplyModifiedPropertiesWithoutUndo();
            }
            RemoveMissingScripts(current);
            EditorSceneManager.MarkSceneDirty(current);
            EditorSceneManager.SaveScene(current);
            Selection.activeGameObject = instance;
            AssetDatabase.SaveAssets();
            if (showDialog) EditorUtility.DisplayDialog("Recovered Goblin", "Гоблин с UV-текстурой и анимациями добавлен в открытую сцену.", "OK");
        }

        private static void RemoveMissingScripts(Scene scene)
        {
            foreach (var sceneRoot in scene.GetRootGameObjects())
                foreach (Transform node in sceneRoot.GetComponentsInChildren<Transform>(true))
                    GameObjectUtility.RemoveMonoBehavioursWithMissingScript(node.gameObject);
        }

        private static AnimatorController BuildController(GameObject rig)
        {
            // Rebuild clips from the selected scene rig after its binding paths change.
            if (AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath) != null) AssetDatabase.DeleteAsset(ControllerPath);
            var controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            controller.AddParameter("Moving", AnimatorControllerParameterType.Bool);
            controller.AddParameter("Running", AnimatorControllerParameterType.Bool);
            var machine = controller.layers[0].stateMachine;
            var rigRootName = rig.name;
            var rigPaths = new List<string>();
            foreach (Transform bone in rig.GetComponentsInChildren<Transform>(true))
            {
                string relative = AnimationUtility.CalculateTransformPath(bone, rig.transform);
                // Animation paths are relative to the Animator's transform. The
                // Animator root itself is addressed by an empty path; adding the
                // root GameObject name here makes every bone binding miss.
                rigPaths.Add(relative);
            }

            AnimationClip Clip(string filename, bool loop = true)
            {
                string path = AnimPath + filename;
                var candidates = AssetDatabase.LoadAllAssetsAtPath(path)
                    .OfType<AnimationClip>()
                    .Where(c => !c.name.StartsWith("__preview__", StringComparison.Ordinal))
                    .OrderByDescending(c => AnimationUtility.GetCurveBindings(c).Length + AnimationUtility.GetObjectReferenceCurveBindings(c).Length)
                    .ToArray();
                var source = candidates.FirstOrDefault();
                if (source == null) return null;
                int curveCount = AnimationUtility.GetCurveBindings(source).Length;
                if (curveCount == 0)
                {
                    Debug.LogWarning("No animated bone curves were imported from " + path + ".");
                    return null;
                }
                string safe = Path.GetFileNameWithoutExtension(filename);
                return MakeRetargeted(source, safe + "_Retargeted", loop, rigRootName, rigPaths);
            }

            void State(string name, AnimationClip clip, bool isDefault = false)
            {
                if (clip == null) return;
                var state = machine.AddState(name);
                state.motion = clip;
                if (name == "Idle") machine.defaultState = state;
            }

            // The imported standing FBX has no transform curves on this rig. Use
            // the keyed combat-wait clip for the idle pose instead of binding an
            // empty clip (which leaves the goblin frozen in its bind pose).
            var standing = Clip("standing_melle_combat.fbx");
            var combatIdle = Clip("gobli_funny_combat_waiting.fbx") ?? Clip("goblin_walking_bone.fbx");
            var walk = Clip("goblin_walking_bone.fbx");
            var run = Clip("goblin_running_bone.fbx");
            var embeddedIdleSource = AssetDatabase.LoadAllAssetsAtPath(ModelPath)
                .OfType<AnimationClip>().FirstOrDefault(c => c.name == "Scene");
            var embeddedIdle = embeddedIdleSource != null
                ? MakeRetargeted(embeddedIdleSource, "GoblinModel_Idle_Retargeted", true, rigRootName, rigPaths)
                : null;
            // The procedural pose layer lowers arms left in bind pose by the
            // imported idle; locomotion still switches to the walk/run clips.
            var idle = combatIdle ?? embeddedIdle ?? walk ?? standing;
            var punch = Clip("punching_combat.fbx", false);
            State("Idle", idle);
            State("Walk", walk != null ? walk : idle);
            State("Run", run != null ? run : walk != null ? walk : idle);
            State("CombatIdle", combatIdle ?? idle);
            State("CombatReady", idle);
            State("Punch", punch);
            State("Melee", punch);
            State("Headbutt", Clip("funny_head_butt_combat.fbx", false) ?? punch);
            State("Block", Clip("block_combat.fbx") ?? standing);
            State("Death", Clip("dying_combat.fbx", false) ?? punch);
            EditorUtility.SetDirty(controller);
            AssetDatabase.DeleteAsset(GeneratedPath + "BlenderScene_Retargeted.anim");
            AssetDatabase.SaveAssets();
            return controller;
        }

        private static string RetargetPath(string path, string rigRootName)
        {
            if (string.IsNullOrEmpty(path)) return string.Empty;
            if (path.StartsWith(rigRootName + "/", StringComparison.Ordinal))
                return path.Substring(rigRootName.Length + 1);
            return path;
        }

        private static string NormalizeBoneName(string value)
        {
            value = value.Replace("mixamorig:", "").Replace("mixamorig", "").ToLowerInvariant();
            string name = new string(value.Where(char.IsLetterOrDigit).ToArray());
            // The supplied locomotion/combat FBXs use Mixamo names, while the
            // playable Blender rig uses a compact metarig. Match equivalent
            // bones so imported clips bind to the actual goblin skeleton.
            switch (name)
            {
                case "hips": return "metarig";
                case "spine1": return "spine001";
                case "spine2": return "spine002";
                case "neck": return "spine003";
                case "head": return "spine004";
                case "leftshoulder": return "shoulderl";
                case "leftarm": return "upperarml";
                case "leftforearm": return "forearml";
                case "lefthand": return "handl";
                case "leftupleg": return "thighl";
                case "leftleg": return "shinl";
                case "leftfoot": return "footl";
                case "lefttoebase": return "toel";
                case "rightshoulder": return "shoulderr";
                case "rightarm": return "upperarmr";
                case "rightforearm": return "forearmr";
                case "righthand": return "handr";
                case "rightupleg": return "thighr";
                case "rightleg": return "shinr";
                case "rightfoot": return "footr";
                case "righttoebase": return "toer";
                default: return name;
            }
        }

        private static string RetargetToRig(string sourcePath, string rigRootName, List<string> rigPaths)
        {
            if (string.IsNullOrEmpty(sourcePath)) return string.Empty;
            string proposed = RetargetPath(sourcePath, rigRootName);
            if (rigPaths.Contains(proposed)) return proposed;

            var sourceParts = sourcePath.Split('/');
            int bestScore = 0;
            string best = null;
            bool ambiguous = false;
            foreach (string candidate in rigPaths)
            {
                var targetParts = candidate.Split('/');
                int score = 0;
                int sourceIndex = sourceParts.Length - 1;
                int targetIndex = targetParts.Length - 1;
            while (sourceIndex >= 0 && targetIndex >= 0 &&
                       NormalizeBoneName(sourceParts[sourceIndex]) == NormalizeBoneName(targetParts[targetIndex]))
                {
                    score++;
                    sourceIndex--;
                    targetIndex--;
                }
                if (score > bestScore) { bestScore = score; best = candidate; ambiguous = false; }
                else if (score > 0 && score == bestScore && candidate != best) ambiguous = true;
            }
            return bestScore > 0 && !ambiguous ? best : proposed;
        }

        private static AnimationClip MakeRetargeted(AnimationClip source, string assetName, bool loop, string rigRootName, List<string> rigPaths)
        {
            string outputPath = GeneratedPath + assetName + ".anim";
            AssetDatabase.DeleteAsset(outputPath);
            var clip = new AnimationClip { name = assetName, frameRate = source.frameRate, wrapMode = loop ? WrapMode.Loop : WrapMode.Once };
            int writtenCurves = 0;
            foreach (var binding in AnimationUtility.GetCurveBindings(source))
            {
                var target = binding;
                target.path = RetargetToRig(target.path, rigRootName, rigPaths);
                var curve = AnimationUtility.GetEditorCurve(source, binding);
                if (curve == null) continue;
                AnimationUtility.SetEditorCurve(clip, target, curve);
                writtenCurves++;
            }
            foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(source))
            {
                var target = binding;
                target.path = RetargetToRig(target.path, rigRootName, rigPaths);
                AnimationUtility.SetObjectReferenceCurve(clip, target, AnimationUtility.GetObjectReferenceCurve(source, binding));
            }
            // Populate the clip while it is still an in-memory asset. Unity 6 may
            // otherwise serialize an empty clip when editor curves are applied
            // immediately after CreateAsset during a bulk FBX rebuild.
            var clipSettings = AnimationUtility.GetAnimationClipSettings(clip);
            clipSettings.loopTime = loop;
            clipSettings.loopBlend = false;
            AnimationUtility.SetAnimationClipSettings(clip, clipSettings);
            if (writtenCurves == 0)
            {
                Debug.LogError("No transform curves were copied into " + assetName + ".");
                UnityEngine.Object.DestroyImmediate(clip);
                return null;
            }
            AssetDatabase.CreateAsset(clip, outputPath);
            EditorUtility.SetDirty(clip);
            return clip;
        }
    }
}

// Selection-safe cleanup follows the goblin currently chosen in Player.
