using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;

namespace Goblingame.TestScene.Editor
{
    public static class GoblinCombatSetup
    {
        [MenuItem("Goblingame/Connect All Combat Animations")]
        public static void Apply()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode first.");
            var scene=SceneManager.GetActiveScene();
            var player=scene.GetRootGameObjects().FirstOrDefault(o=>o.name=="Player");
            var animator=player!=null?player.GetComponentInChildren<Animator>():null;
            if(animator==null||PrefabStageUtility.GetCurrentPrefabStage()!=null)throw new InvalidOperationException("Open GoblinPlayable first.");
            var existing=animator.runtimeAnimatorController.animationClips;
            var walk=existing.First(c=>AssetDatabase.GetAssetPath(c).EndsWith("/Goblin.fbx",StringComparison.Ordinal));
            var run=existing.FirstOrDefault(c=>c.name=="Run");
            var idle=existing.FirstOrDefault(c=>c.name=="Idle_Natural");
            if(run==null||idle==null)throw new InvalidOperationException("Connect running and natural idle first.");
            string backup=AssetDatabase.GenerateUniqueAssetPath("Assets/Scenes/GoblinBeforeCombat.unity");
            if(!EditorSceneManager.SaveScene(scene,backup,true))throw new InvalidOperationException("Could not save backup.");
            string folder=AssetDatabase.GenerateUniqueAssetPath("Assets/GoblingameTest/CombatGenerated");
            AssetDatabase.CreateFolder("Assets/GoblingameTest",System.IO.Path.GetFileName(folder));
            var clips=new Dictionary<string,AnimationClip>{{"Idle",idle},{"Walk",walk},{"Run",run}};
            foreach(string name in new[]{"CombatIdle","Punch","Melee","Headbutt","Block","Death"})
            {
                string path=folder+"/"+name+".fbx";
                if(!AssetDatabase.CopyAsset("Assets/GoblinArt/Combat/Goblin_"+name+".fbx",path))throw new InvalidOperationException("Missing combat FBX: "+name);
                var importer=(ModelImporter)AssetImporter.GetAtPath(path);
                importer.animationType=ModelImporterAnimationType.Human;importer.avatarSetup=ModelImporterAvatarSetup.CopyFromOther;
                importer.sourceAvatar=animator.avatar;importer.importAnimation=true;
                var takes=importer.defaultClipAnimations;
                if(takes.Length!=1)throw new InvalidOperationException("Expected one take: "+name);
                var clip=takes[0];clip.name=name;
                clip.loopTime=name=="CombatIdle"||name=="Block";clip.loopPose=clip.loopTime;
                clip.lockRootRotation=true;clip.lockRootHeightY=true;clip.lockRootPositionXZ=true;
                // Windows measured from the source gestures: retain wind-up, impact and recovery.
                if(name=="Punch"){clip.firstFrame=38;clip.lastFrame=72;}
                if(name=="Melee"){clip.firstFrame=21;clip.lastFrame=55;}
                if(name=="Headbutt"){clip.firstFrame=24;clip.lastFrame=66;}
                if(name=="Block"){clip.firstFrame=100;clip.lastFrame=104;}
                importer.clipAnimations=takes;importer.SaveAndReimport();
                clips[name]=AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().First(c=>c.name==name&&c.humanMotion&&c.length>0);
            }
            string controllerPath=folder+"/GoblinCombat.controller";
            var controller=AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            controller.AddParameter("Moving",AnimatorControllerParameterType.Bool);
            controller.AddParameter("Running",AnimatorControllerParameterType.Bool);
            var machine=controller.layers[0].stateMachine;
            int index=0;
            foreach(var pair in clips)
            {
                var state=machine.AddState(pair.Key,new Vector3(200+(index%3)*230,50+(index/3)*90,0));index++;
                state.motion=pair.Value;state.iKOnFeet=pair.Key!="Death";state.writeDefaultValues=true;
                if(pair.Key=="Idle")machine.defaultState=state;
                EditorUtility.SetDirty(state);
            }
            // GoblinCombat requests crossfades only when the desired state changes.
            // No automatic transitions can compete with attacks or restart a held block.
            var layers=controller.layers;layers[0].defaultWeight=1;controller.layers=layers;
            EditorUtility.SetDirty(machine);EditorUtility.SetDirty(controller);AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(controllerPath,ImportAssetOptions.ForceSynchronousImport|ImportAssetOptions.ForceUpdate);
            controller=AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            Validate(animator,controller,walk);
            Undo.RecordObject(animator,"Connect combat");animator.runtimeAnimatorController=controller;animator.applyRootMotion=false;
            PrefabUtility.RecordPrefabInstancePropertyModifications(animator);
            var combat=player.GetComponent<GoblinCombat>()??Undo.AddComponent<GoblinCombat>(player);
            var data=new SerializedObject(combat);data.FindProperty("animator").objectReferenceValue=animator;
            data.FindProperty("punchImpact").floatValue=14f/34f;data.FindProperty("meleeImpact").floatValue=14f/34f;data.FindProperty("headbuttImpact").floatValue=18f/42f;
            data.ApplyModifiedProperties();
            if(!scene.GetRootGameObjects().Any(o=>o.name=="Training Dummy"))
            {
                var dummy=GameObject.CreatePrimitive(PrimitiveType.Capsule);Undo.RegisterCreatedObjectUndo(dummy,"Create training dummy");
                dummy.name="Training Dummy";dummy.transform.position=player.transform.position+Vector3.forward*4f+Vector3.up;
                var material=new Material(Shader.Find("Universal Render Pipeline/Lit"));material.SetColor("_BaseColor",new Color(.65f,.4f,.15f));
                AssetDatabase.CreateAsset(material,folder+"/Dummy.mat");dummy.GetComponent<Renderer>().sharedMaterial=material;
                var target=dummy.AddComponent<TrainingDummy>();var targetData=new SerializedObject(target);
                targetData.FindProperty("player").objectReferenceValue=combat;targetData.ApplyModifiedProperties();
            }
            EditorSceneManager.MarkSceneDirty(scene);AssetDatabase.SaveAssets();
            if(!EditorSceneManager.SaveScene(scene))throw new InvalidOperationException("Could not save combat scene.");
            Debug.Log("Combat ready: WASD walk, Shift run, LMB combo, Q headbutt, RMB block, C combat stance, R revive when down. Dummy flashes red before its counterattack.");
        }
        internal static void Validate(Animator original,AnimatorController controller,AnimationClip walk)
        {
            var preview=EditorSceneManager.NewPreviewScene();PlayableGraph graph=default;
            try
            {
                var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GetAssetPath(walk));
                var copy=(GameObject)PrefabUtility.InstantiatePrefab(prefab,preview);var animator=copy.GetComponent<Animator>();
                animator.avatar=original.avatar;animator.runtimeAnimatorController=null;animator.applyRootMotion=false;animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
                graph=PlayableGraph.Create("Validate All Goblin States");graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                var playable=AnimatorControllerPlayable.Create(graph,controller);playable.SetLayerWeight(0,1);playable.SetSpeed(1);
                var output=AnimationPlayableOutput.Create(graph,"Goblin",animator);output.SetSourcePlayable(playable);output.SetWeight(1);graph.Play();graph.Evaluate(0);
                foreach(string name in new[]{"Idle","Walk","Run","CombatIdle","Punch","Melee","Headbutt","Block","Death"})
                {
                    int hash=Animator.StringToHash("Base Layer."+name);
                    if(!playable.HasState(0,hash))throw new InvalidOperationException("Missing state: "+name);
                    playable.CrossFadeInFixedTime(hash,.12f,0,0f);
                    for(int i=0;i<8;i++)graph.Evaluate(.05f);
                    if(playable.GetCurrentAnimatorStateInfo(0).fullPathHash!=hash)throw new InvalidOperationException("Crossfade failed: "+name);
                    var bones=new[]{HumanBodyBones.LeftUpperLeg,HumanBodyBones.RightUpperArm,HumanBodyBones.Head,HumanBodyBones.Spine,HumanBodyBones.Chest}
                        .Select(animator.GetBoneTransform).Where(b=>b!=null).ToArray();
                    var rotations=bones.Select(b=>b.localRotation).ToArray();
                    bool changed=false;
                    // Idle contains deliberate still intervals; sample across the whole clip.
                    foreach(float phase in new[]{.13f,.37f,.63f,.87f})
                    {
                        playable.Play(hash,0,phase);graph.Evaluate(0f);
                        changed |= bones.Where((b,i)=>Quaternion.Angle(rotations[i],b.localRotation)>.02f).Any();
                    }
                    if(name!="Block"&&!changed)
                        throw new InvalidOperationException("No bone motion in "+name);
                }
                Debug.Log("Validated all 9 states and crossfades, including walk/run and three attacks.");
            }
            finally{if(graph.IsValid())graph.Destroy();EditorSceneManager.ClosePreviewScene(preview);}
        }
    }
}
