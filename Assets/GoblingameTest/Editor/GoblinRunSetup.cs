using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Goblingame.TestScene.Editor
{
    public static class GoblinRunSetup
    {
        [MenuItem("Goblingame/Add Running")]
        public static void Apply()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode first.");
            var scene=SceneManager.GetActiveScene();
            var player=scene.GetRootGameObjects().FirstOrDefault(o=>o.name=="Player");
            var animator=player != null ? player.GetComponentInChildren<Animator>() : null;
            if (animator == null || PrefabStageUtility.GetCurrentPrefabStage()!=null)
                throw new InvalidOperationException("Open GoblinPlayable first.");
            var oldController=animator.runtimeAnimatorController as AnimatorController;
            if(oldController==null)throw new InvalidOperationException("Missing locomotion controller.");
            var walk=oldController.animationClips.FirstOrDefault(c=>AssetDatabase.GetAssetPath(c).EndsWith("/Goblin.fbx",StringComparison.Ordinal));
            if(walk==null)throw new InvalidOperationException("Missing walk clip.");
            string folder=System.IO.Path.GetDirectoryName(AssetDatabase.GetAssetPath(walk)).Replace('\\','/');
            string backup=AssetDatabase.GenerateUniqueAssetPath("Assets/Scenes/GoblinBeforeRun.unity");
            if(!EditorSceneManager.SaveScene(scene,backup,true))throw new InvalidOperationException("Could not back up scene.");
            string path=AssetDatabase.GenerateUniqueAssetPath(folder+"/GoblinRun.fbx");
            if(!AssetDatabase.CopyAsset("Assets/GoblinArt/Goblin_Run.fbx",path))throw new InvalidOperationException("Run FBX is not imported yet.");
            var importer=(ModelImporter)AssetImporter.GetAtPath(path);
            importer.animationType=ModelImporterAnimationType.Human;
            importer.avatarSetup=ModelImporterAvatarSetup.CopyFromOther;
            importer.sourceAvatar=animator.avatar;
            importer.importAnimation=true;
            var clips=importer.defaultClipAnimations;
            if(clips.Length!=1)throw new InvalidOperationException("Expected one run take.");
            clips[0].name="Run";
            clips[0].loopTime=true;clips[0].loopPose=true;
            clips[0].lockRootRotation=true;clips[0].lockRootHeightY=true;clips[0].lockRootPositionXZ=true;
            importer.clipAnimations=clips;importer.SaveAndReimport();
            var run=AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().FirstOrDefault(c=>c.name=="Run"&&c.humanMotion&&c.length>0);
            if(run==null)throw new InvalidOperationException("Run humanoid import failed.");
            string controllerPath=AssetDatabase.GenerateUniqueAssetPath(folder+"/GoblinWithRun.controller");
            if(!AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(oldController),controllerPath))throw new InvalidOperationException("Could not copy controller.");
            var controller=AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            var machine=controller.layers[0].stateMachine;
            var idle=machine.states.First(s=>s.state.name=="Idle").state;
            var walkState=machine.states.First(s=>s.state.name=="Walk").state;
            var runState=machine.states.FirstOrDefault(s=>s.state.name=="Run").state ?? machine.AddState("Run",new Vector3(510,220,0));
            runState.motion=run;runState.iKOnFeet=true;
            if(!controller.parameters.Any(p=>p.name=="Running"))controller.AddParameter("Running",AnimatorControllerParameterType.Bool);
            // Rebuild this copy's transitions to make all three routes mutually exclusive.
            foreach(var state in new[]{idle,walkState,runState})
                foreach(var transition in state.transitions.ToArray())state.RemoveTransition(transition);
            Link(idle,walkState,true,false);Link(idle,runState,true,true);
            Link(walkState,idle,false,null);Link(walkState,runState,true,true);
            Link(runState,idle,false,null);Link(runState,walkState,true,false);
            foreach(var state in new[]{idle,walkState,runState})
            {EditorUtility.SetDirty(state);foreach(var transition in state.transitions)EditorUtility.SetDirty(transition);}
            EditorUtility.SetDirty(machine);EditorUtility.SetDirty(controller);AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(controllerPath,ImportAssetOptions.ForceUpdate|ImportAssetOptions.ForceSynchronousImport);
            controller=AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            GoblinIdleSetup.ValidateTransitions(animator,controller,walk);
            Undo.RecordObject(animator,"Connect running");animator.runtimeAnimatorController=controller;
            animator.applyRootMotion=false;PrefabUtility.RecordPrefabInstancePropertyModifications(animator);
            EditorSceneManager.MarkSceneDirty(scene);AssetDatabase.SaveAssets();
            if(!EditorSceneManager.SaveScene(scene))throw new InvalidOperationException("Could not save scene.");
            Debug.Log("Running ready: Shift + WASD to run; release Shift to walk; release movement to idle.");
        }
        private static void Link(AnimatorState from,AnimatorState to,bool moving,bool? running)
        {
            var t=from.AddTransition(to);t.hasExitTime=false;t.hasFixedDuration=true;t.duration=.22f;
            t.interruptionSource=TransitionInterruptionSource.None;t.orderedInterruption=true;t.canTransitionToSelf=false;
            t.AddCondition(moving?AnimatorConditionMode.If:AnimatorConditionMode.IfNot,0,"Moving");
            if(running.HasValue)t.AddCondition(running.Value?AnimatorConditionMode.If:AnimatorConditionMode.IfNot,0,"Running");
        }
    }
}
