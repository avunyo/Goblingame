using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Goblingame.TestScene.Editor
{
    public static class GoblinCombatPolish
    {
        [MenuItem("Goblingame/Polish Combat and Health HUD")]
        public static void Apply()
        {
            var scene=SceneManager.GetActiveScene();
            var player=scene.GetRootGameObjects().First(o=>o.name=="Player");
            var animator=player.GetComponentInChildren<Animator>();
            if(EditorApplication.isPlaying)throw new InvalidOperationException("Stop Play first.");
            if(!EditorSceneManager.SaveScene(scene,AssetDatabase.GenerateUniqueAssetPath("Assets/Scenes/GoblinBeforeCombatPolish.unity"),true))throw new InvalidOperationException("Backup failed.");
            string path=AssetDatabase.GenerateUniqueAssetPath("Assets/GoblingameTest/GoblinCombatPolished.controller");
            if(!AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(animator.runtimeAnimatorController),path))throw new InvalidOperationException("Controller copy failed.");
            var controller=AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            var machine=controller.layers[0].stateMachine;
            var source=controller.animationClips.First(c=>c.name=="CombatIdle");
            var pose=UnityEngine.Object.Instantiate(source);pose.name="CombatReady";
            foreach(var binding in AnimationUtility.GetCurveBindings(pose))
            {
                float value=AnimationUtility.GetEditorCurve(source,binding).Evaluate(.05f);
                bool breathe=binding.propertyName=="Chest Front-Back"||binding.propertyName=="Spine Front-Back";
                var curve=new AnimationCurve();
                for(int i=0;i<=24;i++)curve.AddKey(i/8f,value+(breathe?.012f*Mathf.Sin(i/24f*Mathf.PI*2):0f));
                for(int i=0;i<curve.length;i++)curve.SmoothTangents(i,0);
                AnimationUtility.SetEditorCurve(pose,binding,curve);
            }
            AnimationUtility.SetAnimationEvents(pose,new AnimationEvent[0]);
            var settings=AnimationUtility.GetAnimationClipSettings(pose);settings.startTime=0;settings.stopTime=3;settings.loopTime=true;settings.loopBlend=true;
            AnimationUtility.SetAnimationClipSettings(pose,settings);
            AssetDatabase.CreateAsset(pose,AssetDatabase.GenerateUniqueAssetPath("Assets/GoblingameTest/CombatReady.anim"));
            var state=machine.states.Select(s=>s.state).FirstOrDefault(s=>s.name=="CombatReady")??machine.AddState("CombatReady",new Vector3(900,50));
            state.motion=pose;state.iKOnFeet=true;
            EditorUtility.SetDirty(controller);EditorUtility.SetDirty(machine);EditorUtility.SetDirty(state);AssetDatabase.SaveAssets();
            GoblinCombatSetup.Validate(animator,controller,controller.animationClips.First(c=>AssetDatabase.GetAssetPath(c).EndsWith("/Goblin.fbx",StringComparison.Ordinal)));
            if(!pose.humanMotion||!pose.isLooping)throw new InvalidOperationException("Invalid ready pose.");
            animator.runtimeAnimatorController=controller;PrefabUtility.RecordPrefabInstancePropertyModifications(animator);
            var dummyObject=scene.GetRootGameObjects().First(o=>o.name=="Training Dummy");
            var target=dummyObject.GetComponent<DamageableTarget>()??dummyObject.AddComponent<DamageableTarget>();
            var dummy=dummyObject.GetComponent<TrainingDummy>();
            var rootVisual=dummyObject.GetComponent<Renderer>();
            var child=dummyObject.transform.Find("Dummy Visual");
            if(child==null)
            {
                var obj=GameObject.CreatePrimitive(PrimitiveType.Capsule);obj.name="Dummy Visual";obj.transform.SetParent(dummyObject.transform,false);
                UnityEngine.Object.DestroyImmediate(obj.GetComponent<Collider>());child=obj.transform;
                obj.GetComponent<Renderer>().sharedMaterial=rootVisual.sharedMaterial;
            }
            rootVisual.enabled=false;
            var data=new SerializedObject(dummy);data.FindProperty("visual").objectReferenceValue=child.GetComponent<Renderer>();data.FindProperty("player").objectReferenceValue=player.GetComponent<GoblinCombat>();data.ApplyModifiedProperties();
            var old=scene.GetRootGameObjects().FirstOrDefault(o=>o.name=="Combat HUD");if(old!=null)UnityEngine.Object.DestroyImmediate(old);
            var canvasObject=new GameObject("Combat HUD",typeof(RectTransform),typeof(Canvas),typeof(UnityEngine.UI.CanvasScaler),typeof(CombatHUD));
            canvasObject.GetComponent<Canvas>().renderMode=RenderMode.ScreenSpaceOverlay;
            var scaler=canvasObject.GetComponent<UnityEngine.UI.CanvasScaler>();scaler.uiScaleMode=UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1920,1080);scaler.matchWidthOrHeight=.5f;
            var panel=Rect("Health Panel",canvasObject.transform,24,-24,440,225);var bg=panel.gameObject.AddComponent<UnityEngine.UI.Image>();bg.color=new Color(.025f,.035f,.025f,.88f);bg.raycastTarget=false;
            var hud=canvasObject.GetComponent<CombatHUD>();hud.player=player.GetComponent<GoblinCombat>();hud.target=target;hud.dummy=dummy;
            hud.playerText=Label("Player Health",panel,18,-10,400,30,22);hud.playerText.text="ГОБЛИН  100 / 100";
            hud.playerFill=Bar(panel,-47,new Color(.35f,.83f,.28f));
            hud.targetText=Label("Target Health",panel,18,-85,400,30,22);hud.targetText.text="МАНЕКЕН  100 / 100";
            hud.targetFill=Bar(panel,-122,new Color(.95f,.58f,.16f));
            hud.status=Label("Training Status",panel,18,-165,404,45,19);hud.status.text="ГОТОВ К ТРЕНИРОВКЕ";
            var controls=Label("Controls",canvasObject.transform,24,-24,1200,46,20);controls.rectTransform.anchorMin=controls.rectTransform.anchorMax=new Vector2(0,0);controls.rectTransform.pivot=new Vector2(0,0);controls.rectTransform.anchoredPosition=new Vector2(24,20);
            controls.text="WASD — идти   Shift — бег   ЛКМ — удары   Q — удар головой\nПКМ — держать блок   C — боевая стойка   R — встать после поражения";
            VerifyHealth();
            EditorSceneManager.MarkSceneDirty(scene);AssetDatabase.SaveAssets();
            if(!EditorSceneManager.SaveScene(scene))throw new InvalidOperationException("Save failed.");
            Debug.Log("Combat polish saved: ready breathing pose, one-shot fidget, runtime run alignment, dummy phases and health HUD.");
        }
        private static RectTransform Rect(string name,Transform parent,float x,float y,float w,float h)
        {
            var r=new GameObject(name,typeof(RectTransform)).GetComponent<RectTransform>();r.SetParent(parent,false);r.anchorMin=r.anchorMax=r.pivot=new Vector2(0,1);r.anchoredPosition=new Vector2(x,y);r.sizeDelta=new Vector2(w,h);return r;
        }
        private static UnityEngine.UI.Text Label(string name,Transform parent,float x,float y,float w,float h,int size)
        {
            var t=Rect(name,parent,x,y,w,h).gameObject.AddComponent<UnityEngine.UI.Text>();t.font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");t.fontSize=size;t.color=Color.white;t.alignment=TextAnchor.MiddleLeft;t.raycastTarget=false;return t;
        }
        private static UnityEngine.UI.Image Bar(Transform parent,float y,Color color)
        {
            var back=Rect("Bar Background",parent,18,y,404,23);var b=back.gameObject.AddComponent<UnityEngine.UI.Image>();b.color=new Color(.12f,.14f,.12f);b.raycastTarget=false;
            var fill=Rect("Fill",back,2,-2,400,19).gameObject.AddComponent<UnityEngine.UI.Image>();fill.sprite=AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");fill.type=UnityEngine.UI.Image.Type.Filled;fill.fillMethod=UnityEngine.UI.Image.FillMethod.Horizontal;fill.color=color;fill.raycastTarget=false;return fill;
        }
        private static void VerifyHealth()
        {
            var obj=new GameObject("Health Validation");try
            {
                var target=obj.AddComponent<DamageableTarget>();int deaths=0;target.Died+=()=>deaths++;
                target.TakeDamage(16);if(target.Health!=84)throw new InvalidOperationException("Damage failed.");
                target.TakeDamage(200);target.TakeDamage(20);if(!target.IsDead||deaths!=1)throw new InvalidOperationException("Death failed.");
                target.RestoreHealth();if(target.Health!=100)throw new InvalidOperationException("Restore failed.");
            }finally{UnityEngine.Object.DestroyImmediate(obj);}
        }
    }
}
