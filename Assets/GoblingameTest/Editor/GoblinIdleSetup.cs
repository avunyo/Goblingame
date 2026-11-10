using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace Goblingame.TestScene.Editor
{
    public static class GoblinIdleSetup
    {
        [MenuItem("Goblingame/Add Living Idle")]
        [MenuItem("Goblingame/Repair Walk and Add Natural Idle")]
        public static void Apply()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
            {
                Debug.LogWarning("Exit Play Mode and wait for compilation, then choose Goblingame > Add Living Idle.");
                return;
            }
            Scene scene = SceneManager.GetActiveScene();
            var player = scene.GetRootGameObjects().FirstOrDefault(o => o.name == "Player");
            var visual = player != null ? player.transform.Find("GoblinVisual") : null;
            if (visual == null || PrefabStageUtility.GetCurrentPrefabStage() != null)
            {
                Debug.LogWarning("Add Living Idle was skipped. This tool requires Player/GoblinVisual; the current recovered goblin scene uses a different rig.");
                return;
            }
            var animator = visual.GetComponent<Animator>();
            if (animator == null || animator.runtimeAnimatorController == null || animator.avatar == null)
                throw new InvalidOperationException("The goblin needs the existing walk controller and Avatar.");
            var walk = animator.runtimeAnimatorController.animationClips.FirstOrDefault(c =>
                AssetDatabase.GetAssetPath(c).EndsWith("/Goblin.fbx", StringComparison.Ordinal));
            if (walk == null) throw new InvalidOperationException("Could not find the existing generated walk clip.");
            string folder = System.IO.Path.GetDirectoryName(AssetDatabase.GetAssetPath(walk)).Replace('\\', '/');
            string backup = AssetDatabase.GenerateUniqueAssetPath("Assets/Scenes/GoblinBeforeIdle.unity");
            if (!EditorSceneManager.SaveScene(scene, backup, true)) throw new InvalidOperationException("Could not save a scene backup.");
            ConfigureAnimator(animator, folder, walk);
            EditorSceneManager.MarkSceneDirty(scene);
            AssetDatabase.SaveAssets();
            if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Could not save the scene.");
            Debug.Log("Living Idle connected. Press Play: idle looks around; WASD blends to walking, releasing keys blends back to idle.");
        }

        public static void ConfigureAnimator(Animator animator, string folder, AnimationClip walk)
        {
            const string source = "Assets/GoblinArt/Goblin_Idle_Natural.fbx";
            if (AssetDatabase.LoadAssetAtPath<GameObject>(source) == null)
                throw new InvalidOperationException("Wait for Goblin_Idle_Natural.fbx to finish importing.");
            string path = AssetDatabase.GenerateUniqueAssetPath(folder + "/GoblinIdle.fbx");
            if (!AssetDatabase.CopyAsset(source, path)) throw new InvalidOperationException("Could not copy idle animation.");
            var importer = (ModelImporter)AssetImporter.GetAtPath(path);
            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CopyFromOther;
            importer.sourceAvatar = animator.avatar;
            importer.importAnimation = true;
            var clips = importer.defaultClipAnimations;
            if (clips.Length != 1) throw new InvalidOperationException("Expected exactly one idle take.");
            clips[0].name = "Idle_Natural";
            clips[0].loopTime = true;
            clips[0].loopPose = true;
            clips[0].lockRootRotation = true;
            clips[0].lockRootHeightY = true;
            clips[0].lockRootPositionXZ = true;
            importer.clipAnimations = clips;
            importer.SaveAndReimport();
            var idle = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>()
                .FirstOrDefault(c => c.name == "Idle_Natural" && c.length > 0f && c.humanMotion);
            if (idle == null) throw new InvalidOperationException("Idle did not import as humanoid; check Console. Existing controller is unchanged.");
            string controllerPath = AssetDatabase.GenerateUniqueAssetPath(folder + "/GoblinLocomotion.controller");
            var controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            controller.AddParameter("Moving", AnimatorControllerParameterType.Bool);
            var machine = controller.layers[0].stateMachine;
            var idleState = machine.AddState("Idle", new Vector3(230, 50, 0));
            var walkState = machine.AddState("Walk", new Vector3(510, 50, 0));
            idleState.motion = idle;
            walkState.motion = walk;
            idleState.iKOnFeet = true;
            walkState.iKOnFeet = true;
            machine.defaultState = idleState;
            SetTransition(idleState.AddTransition(walkState), true);
            SetTransition(walkState.AddTransition(idleState), false);
            // CreateAnimatorControllerAtPath initially writes an empty controller.
            // Persist every sub-asset and rebuild its runtime data BEFORE playing it.
            var layers = controller.layers;
            layers[0].defaultWeight = 1f;
            controller.layers = layers;
            EditorUtility.SetDirty(controller);
            EditorUtility.SetDirty(machine);
            EditorUtility.SetDirty(idleState);
            EditorUtility.SetDirty(walkState);
            foreach (var transition in idleState.transitions.Concat(walkState.transitions))
                EditorUtility.SetDirty(transition);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(controllerPath, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            if (controller == null || controller.layers.Length != 1 ||
                controller.layers[0].stateMachine.states.Length != 2 ||
                !controller.parameters.Any(p => p.name == "Moving" && p.type == AnimatorControllerParameterType.Bool))
                throw new InvalidOperationException("Saved controller is incomplete. Previous controller preserved.");
            ValidateTransitions(animator, controller, walk);
            Undo.RecordObject(animator, "Connect living idle");
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;
            PrefabUtility.RecordPrefabInstancePropertyModifications(animator);
        }

        internal static void ValidateTransitions(Animator original, AnimatorController controller, AnimationClip walk)
        {
            var preview = EditorSceneManager.NewPreviewScene();
            PlayableGraph graph = default;
            try
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GetAssetPath(walk));
                var copy = (GameObject)PrefabUtility.InstantiatePrefab(prefab, preview);
                var animator = copy.GetComponent<Animator>();
                copy.SetActive(true);
                animator.enabled = true;
                animator.avatar = original.avatar;
                animator.runtimeAnimatorController = null;
                animator.applyRootMotion = false;
                animator.speed = 1f;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                graph = PlayableGraph.Create("Check Goblin Idle Walk");
                graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                var playable = AnimatorControllerPlayable.Create(graph, controller);
                playable.SetSpeed(1d);
                playable.SetLayerWeight(0, 1f);
                var output = AnimationPlayableOutput.Create(graph, "Goblin", animator);
                output.SetSourcePlayable(playable);
                output.SetWeight(1f);
                graph.Play();
                graph.Evaluate(0f);
                if (!playable.HasState(0, Animator.StringToHash("Base Layer.Walk")))
                    throw new InvalidOperationException("Runtime controller has no Walk state after import. Previous controller preserved.");
                playable.SetBool("Moving", true);
                for (int i = 0; i < 20; i++) graph.Evaluate(0.05f);
                if (!playable.GetCurrentAnimatorStateInfo(0).IsName("Base Layer.Walk"))
                    throw new InvalidOperationException("Walk transition validation failed: Moving=" + playable.GetBool("Moving") +
                        ", state=" + playable.GetCurrentAnimatorStateInfo(0).fullPathHash +
                        ", time=" + playable.GetCurrentAnimatorStateInfo(0).normalizedTime +
                        ", transition=" + playable.IsInTransition(0) + ". Previous controller preserved.");
                var leg = animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
                Quaternion first = leg.localRotation;
                graph.Evaluate(0.23f);
                if (Quaternion.Angle(first, leg.localRotation) < 0.05f)
                    throw new InvalidOperationException("Walk state does not move the leg. Previous controller preserved.");
                playable.SetBool("Moving", false);
                for (int i = 0; i < 20; i++) graph.Evaluate(0.05f);
                if (!playable.GetCurrentAnimatorStateInfo(0).IsName("Base Layer.Idle"))
                    throw new InvalidOperationException("Idle transition validation failed. Previous controller preserved.");
                if (controller.parameters.Any(p => p.name == "Running"))
                {
                    playable.SetBool("Running", true);
                    playable.SetBool("Moving", true);
                    for (int i = 0; i < 20; i++) graph.Evaluate(0.05f);
                    if (!playable.GetCurrentAnimatorStateInfo(0).IsName("Base Layer.Run"))
                        throw new InvalidOperationException("Idle to Run validation failed.");
                    first = leg.localRotation;
                    graph.Evaluate(0.17f);
                    if (Quaternion.Angle(first, leg.localRotation) < 0.05f)
                        throw new InvalidOperationException("Run clip does not move the leg.");
                    playable.SetBool("Running", false);
                    for (int i = 0; i < 20; i++) graph.Evaluate(0.05f);
                    if (!playable.GetCurrentAnimatorStateInfo(0).IsName("Base Layer.Walk"))
                        throw new InvalidOperationException("Run to Walk validation failed.");
                    playable.SetBool("Running", true);
                    for (int i = 0; i < 20; i++) graph.Evaluate(0.05f);
                    if (!playable.GetCurrentAnimatorStateInfo(0).IsName("Base Layer.Run"))
                        throw new InvalidOperationException("Walk to Run validation failed.");
                    playable.SetBool("Moving", false);
                    playable.SetBool("Running", false);
                    for (int i = 0; i < 20; i++) graph.Evaluate(0.05f);
                    if (!playable.GetCurrentAnimatorStateInfo(0).IsName("Base Layer.Idle"))
                        throw new InvalidOperationException("Run to Idle validation failed.");
                    Debug.Log("Validated: Idle/Walk/Run transitions and animated running leg.");
                }
                Debug.Log("Validated: Moving=true plays Walk and moves the leg; Moving=false returns to Idle.");
            }
            finally
            {
                if (graph.IsValid()) graph.Destroy();
                EditorSceneManager.ClosePreviewScene(preview);
            }
        }

        private static void SetTransition(AnimatorStateTransition transition, bool moving)
        {
            transition.hasExitTime = false;
            transition.hasFixedDuration = true;
            transition.duration = moving ? 0.2f : 0.3f;
            // A persistent bool must not continually interrupt its own transition.
            // Finish the short blend before considering the reverse direction.
            transition.interruptionSource = TransitionInterruptionSource.None;
            transition.orderedInterruption = true;
            transition.canTransitionToSelf = false;
            transition.AddCondition(moving ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot, 0f, "Moving");
        }
    }
}
