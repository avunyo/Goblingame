using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Goblingame.TestScene.Editor
{
    public static class GoblinTestSetup
    {
        private const string ModelPath = "Assets/GoblinArt/Goblin_Walk_Fixed.fbx";

        [MenuItem("Goblingame/Finish Test Scene")]
        public static void Finish()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
            {
                Debug.LogWarning("Exit Play Mode and wait for compilation, then try again.");
                return;
            }
            Scene scene = SceneManager.GetActiveScene();
            if (PrefabStageUtility.GetCurrentPrefabStage() != null ||
                scene.name != "GoblinTest" || SceneManager.sceneCount != 1)
            {
                Debug.LogWarning("Open Assets/GoblinTest.unity as the only scene, then use Goblingame > Finish Test Scene.");
                return;
            }
            var roots = scene.GetRootGameObjects();
            var player = roots.SingleOrDefault(o => o.name == "Player");
            var ground = roots.SingleOrDefault(o => o.name == "Ground");
            var camera = roots.SelectMany(o => o.GetComponentsInChildren<Camera>()).FirstOrDefault(c => c.CompareTag("MainCamera"));
            if (player == null || ground == null || ground.GetComponent<Collider>() == null || camera == null)
                throw new InvalidOperationException("Expected Player, Ground with a collider, and Main Camera in GoblinTest.");
            if (player.transform.Find("GoblinVisual") != null)
                throw new InvalidOperationException("GoblinVisual is already present. No duplicate was created.");
            if (AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath) == null)
                throw new InvalidOperationException("Goblin_Walk_Fixed.fbx has not finished importing.");

            // Work on a separate imported copy: original model and import settings are preserved.
            string folder = AssetDatabase.GenerateUniqueAssetPath("Assets/GoblingameTest/Generated");
            string folderName = System.IO.Path.GetFileName(folder);
            AssetDatabase.CreateFolder("Assets/GoblingameTest", folderName);
            string modelCopy = folder + "/Goblin.fbx";
            if (!AssetDatabase.CopyAsset(ModelPath, modelCopy)) throw new InvalidOperationException("Could not copy the model.");
            var importer = (ModelImporter)AssetImporter.GetAtPath(modelCopy);
            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.importAnimation = true;
            importer.importCameras = false;
            importer.importLights = false;
            var clips = importer.clipAnimations;
            if (clips.Length == 0) clips = importer.defaultClipAnimations;
            if (clips.Length == 0) throw new InvalidOperationException("No animation takes found.");
            foreach (var clip in clips)
            {
                clip.loopTime = true;
                clip.loopPose = true;
                clip.firstFrame = 9f;
                clip.lastFrame = 78f;
                clip.lockRootRotation = true;
                clip.lockRootHeightY = true;
                clip.lockRootPositionXZ = true;
            }
            importer.clipAnimations = clips;
            importer.SaveAndReimport();
            var assets = AssetDatabase.LoadAllAssetsAtPath(modelCopy);
            var walk = assets.OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview__") && c.length > 0f);
            var avatar = assets.OfType<Avatar>().FirstOrDefault(a => a.isValid && a.isHuman);
            if (walk == null || avatar == null) throw new InvalidOperationException("The copied model needs a valid humanoid Avatar and a walk clip.");
            var animatorController = AnimatorController.CreateAnimatorControllerAtPath(folder + "/GoblinWalk.controller");
            var machine = animatorController.layers[0].stateMachine;
            var state = machine.AddState("Walk");
            state.motion = walk;
            machine.defaultState = state;

            // Save under a new path before editing, preserving the user's original scene.
            string outputScene = AssetDatabase.GenerateUniqueAssetPath("Assets/Scenes/GoblinPlayable.unity");
            if (!EditorSceneManager.SaveScene(scene, outputScene)) throw new InvalidOperationException("Could not save the separate scene.");
            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Set up playable goblin");
            try
            {
                var visual = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(modelCopy), scene);
                Undo.RegisterCreatedObjectUndo(visual, "Add goblin");
                visual.name = "GoblinVisual";
                visual.transform.SetParent(player.transform, false);
                visual.transform.localPosition = Vector3.zero;
                visual.transform.localRotation = Quaternion.identity;
                visual.transform.localScale = Vector3.one;
                var animator = visual.GetComponent<Animator>();
                if (animator == null) animator = visual.AddComponent<Animator>();
                animator.avatar = avatar;
                animator.runtimeAnimatorController = animatorController;
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                // Show a real walk pose in the editor and size the visual to the existing capsule.
                walk.SampleAnimation(visual, 0f);
                var renderers = visual.GetComponentsInChildren<Renderer>();
                GoblinVisualPolish.ApplyColor(renderers, folder);
                Bounds bounds = BoundsOf(renderers);
                if (bounds.size.y < 0.001f) throw new InvalidOperationException("The model has no visible height.");
                visual.transform.localScale *= 2f / bounds.size.y;
                bounds = BoundsOf(renderers);
                visual.transform.position += new Vector3(player.transform.position.x - bounds.center.x,
                    player.transform.position.y - bounds.min.y, player.transform.position.z - bounds.center.z);
                var capsule = player.transform.Find("Visual");
                if (capsule != null) { Undo.RecordObject(capsule.gameObject, "Hide capsule"); capsule.gameObject.SetActive(false); }
                foreach (var collider in visual.GetComponentsInChildren<Collider>()) collider.enabled = false;
                var movement = player.GetComponent<GoblinMovement>() ?? Undo.AddComponent<GoblinMovement>(player);
                var character = player.GetComponent<CharacterController>();
                Undo.RecordObject(character, "Fit controller");
                character.height = 2f; character.radius = 0.4f; character.center = Vector3.up;
                var movementData = new SerializedObject(movement);
                movementData.FindProperty("movementCamera").objectReferenceValue = camera.transform;
                movementData.FindProperty("animator").objectReferenceValue = animator;
                movementData.ApplyModifiedProperties();
                var follow = camera.GetComponent<ThirdPersonFollow>() ?? Undo.AddComponent<ThirdPersonFollow>(camera.gameObject);
                var followData = new SerializedObject(follow);
                followData.FindProperty("target").objectReferenceValue = player.transform;
                followData.ApplyModifiedProperties();
                Undo.RecordObject(camera.transform, "Position camera");
                camera.transform.position = player.transform.position + new Vector3(0f, 3f, -5f);
                camera.transform.LookAt(player.transform.position + Vector3.up);
                EditorSceneManager.MarkSceneDirty(scene);
                AssetDatabase.SaveAssets();
                if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Could not save the configured scene.");
                Undo.CollapseUndoOperations(undoGroup);
                Selection.activeGameObject = player;
                Debug.Log("Goblin ready: " + outputScene + ". Press Play, click Game, then use WASD. Release keys to freeze the walk pose.");
            }
            catch
            {
                Undo.RevertAllDownToGroup(undoGroup);
                throw;
            }
        }

        private static Bounds BoundsOf(Renderer[] renderers)
        {
            if (renderers.Length == 0) throw new InvalidOperationException("No renderers found on the goblin.");
            Bounds bounds = renderers[0].bounds;
            foreach (var renderer in renderers.Skip(1)) bounds.Encapsulate(renderer.bounds);
            return bounds;
        }
    }
}
