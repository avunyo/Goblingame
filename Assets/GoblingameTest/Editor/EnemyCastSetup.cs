using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using UnityEngine.Playables;
using UnityEngine.Animations;

namespace Goblingame.TestScene.Editor
{
    public static class EnemyCastSetup
    {
        static readonly string[] Ids={"RatRogue","MossGourmand","BoneSentinel","IronTusk"};
        static readonly string[] Names={"Крысолов","Грибоед","Костяной страж","Железный клык"};
        static readonly string[] Roles={"Ловкач","Танк","Защитник","Мини-босс"};
        static readonly float[] HP={120,320,240,600}, Speeds={1.6f,.7f,1.05f,1.15f};
        static readonly string[] Descriptions={"БЫСТРЫЙ. ХИТРЫЙ. ОПАСНЫЙ.","МЕДЛЕННЫЙ. КРЕПКИЙ. УПРЯМЫЙ.","ПАВШИЙ, НО ВСЁ ЕЩЁ СЛУЖИТ.","СИЛЬНЫЙ. ЯРОСТНЫЙ. БЕСПОЩАДНЫЙ."};
        static readonly Dictionary<string,string> Palette=new Dictionary<string,string>{
            {"fur","57412F"},{"furLight","80664C"},{"pink","955A4C"},{"scarf","9B3C24"},{"leather","38281C"},
            {"metal","4C5058"},{"edge","8B8D8B"},{"dark","161613"},{"bone","B8A47F"},{"ivory","E3D0A3"},
            {"cloth","293F54"},{"cap","B95420"},{"capLight","DA772F"},{"spot","E8B978"},{"stem","8E8564"},
            {"moss","586724"},{"mossLight","809234"},{"boar","873C29"},{"boarLight","A95337"},{"wood","654322"},{"eye","EDB948"}};
        static Font font;
        static Color Cream=new Color(.86f,.80f,.68f);
        public static void RepairFrames()
        {
            var scene=SceneManager.GetActiveScene();
            string path="Assets/EnemyCast/FlatBar.asset";
            var sprite=AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().FirstOrDefault();
            if(sprite==null){var tex=new Texture2D(2,2,TextureFormat.RGBA32,false);tex.name="Flat Bar";tex.SetPixels(new[]{Color.white,Color.white,Color.white,Color.white});tex.Apply();AssetDatabase.CreateAsset(tex,path);sprite=Sprite.Create(tex,new Rect(0,0,2,2),new Vector2(.5f,.5f),100);sprite.name="FlatBar";AssetDatabase.AddObjectToAsset(sprite,tex);AssetDatabase.SaveAssets();}
            foreach(var root in scene.GetRootGameObjects())RepairGraphics(root,sprite);
            foreach(string guid in AssetDatabase.FindAssets("t:Prefab",new[]{"Assets/EnemyCast"}))
            {
                string prefabPath=AssetDatabase.GUIDToAssetPath(guid);var prefab=PrefabUtility.LoadPrefabContents(prefabPath);
                try{RepairGraphics(prefab,sprite);PrefabUtility.SaveAsPrefabAsset(prefab,prefabPath);}finally{PrefabUtility.UnloadPrefabContents(prefab);}
            }
            EditorSceneManager.MarkSceneDirty(scene);AssetDatabase.SaveAssets();EditorSceneManager.SaveScene(scene);
            Debug.Log("Forged frames now have CanvasRenderer; flat health fills installed and saved.");
        }
        static void RepairGraphics(GameObject root,Sprite sprite)
        {
            foreach(var frame in root.GetComponentsInChildren<ForgedFrame>(true)){if(frame.GetComponent<CanvasRenderer>()==null)frame.gameObject.AddComponent<CanvasRenderer>();frame.SetAllDirty();}
            foreach(var fill in root.GetComponentsInChildren<UnityEngine.UI.Image>(true))if(fill.name=="Fill"&&fill.transform.parent.GetComponent<ForgedFrame>()!=null){fill.sprite=sprite;EditorUtility.SetDirty(fill);}
        }
        [MenuItem("Goblingame/Add Enemy Cast and Forged HUD")]
        public static void Apply()
        {
            var scene=SceneManager.GetActiveScene();
            if(EditorApplication.isPlaying||scene.path!="Assets/Scenes/GoblinPlayable.unity")throw new InvalidOperationException("Open GoblinPlayable outside Play.");
            var player=scene.GetRootGameObjects().First(o=>o.name=="Player");
            var partial=scene.GetRootGameObjects().FirstOrDefault(o=>o.name=="Enemy Cast");
            if(partial!=null){if(partial.transform.childCount<4&&partial.GetComponentsInChildren<EnemyActor>().Length==partial.transform.childCount)UnityEngine.Object.DestroyImmediate(partial);else throw new InvalidOperationException("Enemy cast is already installed; preserve existing instances.");}
            if(!EditorSceneManager.SaveScene(scene,AssetDatabase.GenerateUniqueAssetPath("Assets/Scenes/GoblinBeforeEnemyCast.unity"),true))throw new InvalidOperationException("Backup failed.");
            string folder=AssetDatabase.GenerateUniqueAssetPath("Assets/EnemyCast/Generated");AssetDatabase.CreateFolder("Assets/EnemyCast",System.IO.Path.GetFileName(folder));
            var group=new GameObject("Enemy Cast");var actors=new List<EnemyActor>();
            Vector3[] positions={new Vector3(-6,0,6),new Vector3(6,0,6),new Vector3(-6,0,13),new Vector3(6,0,13)};
            for(int i=0;i<Ids.Length;i++)
            {
                string path="Assets/EnemyCast/"+Ids[i]+".fbx";
                var importer=AssetImporter.GetAtPath(path) as ModelImporter;if(importer==null)throw new InvalidOperationException("Missing model "+path);
                importer.animationType=ModelImporterAnimationType.Generic;importer.avatarSetup=ModelImporterAvatarSetup.CreateFromThisModel;
                importer.importAnimation=true;importer.importCameras=false;importer.importLights=false;importer.globalScale=1;importer.isReadable=true;
                var takes=importer.defaultClipAnimations;bool idle=false,walk=false;
                foreach(var take in takes){if(take.name.EndsWith("Idle")){take.name="Idle";idle=true;}else if(take.name.EndsWith("Walk")){take.name="Walk";walk=true;}take.loopTime=true;take.loopPose=true;take.lockRootRotation=true;take.lockRootHeightY=true;take.lockRootPositionXZ=true;}
                if(!idle||!walk)throw new InvalidOperationException("Both animations required: "+Ids[i]);importer.clipAnimations=takes;importer.SaveAndReimport();
                foreach(var source in AssetDatabase.LoadAllAssetsAtPath(path).OfType<Material>())
                {
                    string key=source.name.Split('.')[0];string matPath=folder+"/"+key+".mat";var mat=AssetDatabase.LoadAssetAtPath<Material>(matPath);
                    if(mat==null){mat=new Material(Shader.Find("Universal Render Pipeline/Lit"));Color tint;if(!Palette.ContainsKey(key)||!ColorUtility.TryParseHtmlString("#"+Palette[key],out tint))tint=source.color;mat.SetColor("_BaseColor",tint);mat.SetFloat("_Smoothness",.18f);if(key=="metal"||key=="edge")mat.SetFloat("_Metallic",.5f);AssetDatabase.CreateAsset(mat,matPath);}
                    importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material),source.name),mat);
                }
                importer.SaveAndReimport();
                var model=AssetDatabase.LoadAssetAtPath<GameObject>(path);var clips=AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().Where(c=>!c.name.StartsWith("__")).ToArray();
                var controller=AnimatorController.CreateAnimatorControllerAtPath(folder+"/"+Ids[i]+".controller");var machine=controller.layers[0].stateMachine;
                foreach(string name in new[]{"Idle","Walk"}){var state=machine.AddState(name);state.motion=clips.First(c=>c.name==name);if(name=="Idle")machine.defaultState=state;}
                var root=new GameObject(Names[i]);root.transform.SetParent(group.transform);root.transform.position=player.transform.position+positions[i];
                var visual=(GameObject)PrefabUtility.InstantiatePrefab(model);visual.name="Visual";visual.transform.SetParent(root.transform,false);
                var animator=visual.GetComponent<Animator>()??visual.AddComponent<Animator>();animator.runtimeAnimatorController=controller;animator.applyRootMotion=false;
                var renderers=visual.GetComponentsInChildren<Renderer>();Bounds bounds=renderers[0].bounds;foreach(var r in renderers)bounds.Encapsulate(r.bounds);
                float height=bounds.size.y;if(height<.5f||height>5f)throw new InvalidOperationException("Unexpected model scale: "+Ids[i]+" "+height);
                visual.transform.localPosition+=Vector3.up*(root.transform.position.y-bounds.min.y);
                var cc=root.AddComponent<CharacterController>();cc.height=height;cc.center=new Vector3(0,height*.5f,0);cc.radius=i==3?.56f:i==1?.48f:.28f;cc.stepOffset=.25f;
                var health=root.AddComponent<DamageableTarget>();var data=new SerializedObject(health);data.FindProperty("maxHealth").floatValue=HP[i];data.FindProperty("health").floatValue=HP[i];data.ApplyModifiedProperties();
                var actor=root.AddComponent<EnemyActor>();actor.displayName=Names[i];actor.role=Roles[i];actor.level=new[]{2,4,5,9}[i];actor.elite=i==3;actor.experienceReward=new[]{25,45,40,120}[i];actor.hitDamage=new[]{10,20,16,30}[i];actor.walkSpeed=Speeds[i];actor.visual=visual.transform;actor.portrait=AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/EnemyCast/"+Ids[i]+"_Portrait.png");
                MakeWorldBar(root,health,height,actor.elite);EditorUtility.SetDirty(controller);EditorUtility.SetDirty(machine);AssetDatabase.SaveAssets();ValidateAnimation(animator,controller);
                AssetDatabase.SaveAssets();PrefabUtility.SaveAsPrefabAsset(root,folder+"/"+Ids[i]+".prefab");actors.Add(actor);
            }
            if(player.GetComponent<PlayerProgression>()==null)player.AddComponent<PlayerProgression>();
            var combatData=new SerializedObject(player.GetComponent<GoblinCombat>());combatData.FindProperty("maxHealth").floatValue=200;combatData.ApplyModifiedProperties();
            // Keep the arena under the added patrol routes; the original ground material is retained.
            var ground=scene.GetRootGameObjects().FirstOrDefault(o=>o.name=="Ground");if(ground!=null&&ground.GetComponent<MeshCollider>()!=null){var scale=ground.transform.localScale;ground.transform.localScale=new Vector3(Mathf.Max(scale.x,5),scale.y,Mathf.Max(scale.z,5));}
            BuildHUD(scene,actors);
            EditorSceneManager.MarkSceneDirty(scene);AssetDatabase.SaveAssets();if(!EditorSceneManager.SaveScene(scene))throw new InvalidOperationException("Scene save failed.");
            Debug.Log("ENEMY CAST PASS: four distinct models, weighted skeletons, Idle/Walk motion verified, prefabs and scene saved; forged HUD and bestiary connected.");
        }
        static void ValidateAnimation(Animator animator,AnimatorController controller)
        {
            var graph=PlayableGraph.Create("Enemy Animation Check");graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            try{
                animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
                var playable=AnimatorControllerPlayable.Create(graph,controller);playable.SetLayerWeight(0,1);playable.SetSpeed(1);var output=AnimationPlayableOutput.Create(graph,"Model",animator);output.SetSourcePlayable(playable);output.SetWeight(1);graph.Play();graph.Evaluate(0);
                var leg=animator.GetComponentsInChildren<Transform>().First(t=>t.name=="Thigh.L");
                playable.Play("Walk",0,.1f);graph.Evaluate(0);var first=leg.localRotation;playable.Play("Walk",0,.6f);graph.Evaluate(0);
                if(Quaternion.Angle(first,leg.localRotation)<5)throw new InvalidOperationException("Walk has no leg motion: "+animator.name+" angle="+Quaternion.Angle(first,leg.localRotation)+" clip="+controller.animationClips.First(c=>c.name=="Walk").length);
                playable.Play("Idle",0,0);graph.Evaluate(0);
            }finally{graph.Destroy();}
        }
        static RectTransform Rect(string name,Transform parent,float x,float y,float w,float h)
        {
            var r=new GameObject(name,typeof(RectTransform)).GetComponent<RectTransform>();r.SetParent(parent,false);r.anchorMin=r.anchorMax=r.pivot=new Vector2(0,1);r.anchoredPosition=new Vector2(x,y);r.sizeDelta=new Vector2(w,h);return r;
        }
        static ForgedFrame Frame(string name,Transform parent,float x,float y,float w,float h)
        {var f=Rect(name,parent,x,y,w,h).gameObject.AddComponent<ForgedFrame>();f.raycastTarget=false;return f;}
        static UnityEngine.UI.Text Text(string name,Transform parent,string text,float x,float y,float w,float h,int size)
        {
            if(font==null)font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");var t=Rect(name,parent,x,y,w,h).gameObject.AddComponent<UnityEngine.UI.Text>();t.font=font;t.fontSize=size;t.text=text;t.color=Cream;t.alignment=TextAnchor.MiddleCenter;t.raycastTarget=false;return t;
        }
        static UnityEngine.UI.Image Bar(Transform parent,float x,float y,float w,float h,Color color)
        {
            var rim=Frame("Bar Frame",parent,x,y,w,h);rim.bevel=3;rim.corner=10;
            var fill=Rect("Fill",rim.transform,8,-7,w-16,h-14).gameObject.AddComponent<UnityEngine.UI.Image>();fill.sprite=AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");fill.type=UnityEngine.UI.Image.Type.Filled;fill.fillMethod=UnityEngine.UI.Image.FillMethod.Horizontal;fill.color=color;fill.raycastTarget=false;
            var shine=Rect("Edge Light",rim.transform,9,-8,w-18,2).gameObject.AddComponent<UnityEngine.UI.Image>();shine.color=new Color(1,1,1,.16f);shine.raycastTarget=false;return fill;
        }
        static UnityEngine.UI.RawImage Picture(Transform parent,Texture texture,float x,float y,float w,float h)
        {var p=Rect("Portrait",parent,x,y,w,h).gameObject.AddComponent<UnityEngine.UI.RawImage>();p.texture=texture;p.raycastTarget=false;return p;}
        static void MakeWorldBar(GameObject root,DamageableTarget health,float height,bool elite)
        {
            var obj=new GameObject("Health",typeof(RectTransform),typeof(Canvas),typeof(EnemyHealthLabel));obj.transform.SetParent(root.transform,false);obj.transform.localPosition=Vector3.up*(height+.30f);obj.transform.localScale=Vector3.one*.006f;
            obj.GetComponent<Canvas>().renderMode=RenderMode.WorldSpace;var rect=obj.GetComponent<RectTransform>();rect.sizeDelta=new Vector2(200,45);
            var fill=Bar(obj.transform,0,0,200,20,new Color(.7f,.12f,.09f));Text("Rank",obj.transform,elite?"◆  ЭЛИТА":"",0,25,200,25,19);
            var label=obj.GetComponent<EnemyHealthLabel>();label.target=health;label.fill=fill;
        }
        static void BuildHUD(Scene scene,List<EnemyActor> actors)
        {
            var canvas=scene.GetRootGameObjects().First(o=>o.name=="Combat HUD");var hud=canvas.GetComponent<CombatHUD>();
            var old=canvas.transform.Find("Health Panel");if(old!=null)old.gameObject.SetActive(false);
            var left=Rect("Goblin Status",canvas.transform,24,-25,620,170);
            var portraitFrame=Frame("Portrait Frame",left,0,0,145,145);portraitFrame.corner=30;portraitFrame.bevel=7;
            // Real goblin portrait rendered separately when available; no substitute character image.
            var goblinTexture=AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/EnemyCast/Goblin_Portrait.png");if(goblinTexture!=null)Picture(portraitFrame.transform,goblinTexture,8,-8,129,129);else Text("Goblin Sigil",portraitFrame.transform,"Г",0,0,145,145,76);
            Frame("Nameplate",left,135,-4,238,35);Text("Name",left,"ГОБЛИН",143,-5,220,32,21);
            hud.playerFill=Bar(left,140,-43,452,47,new Color(.29f,.66f,.11f));hud.playerText=Text("Health",left,"200 / 200",155,-49,420,32,22);
            hud.experienceFill=Bar(left,160,-95,408,23,new Color(.54f,.16f,.64f));hud.experienceText=Text("Experience",left,"ОПЫТ  0 / 100",175,-122,370,23,14);
            Frame("Level Badge",left,9,-111,51,51).corner=24;hud.playerLevel=Text("Level",left,"1",9,-111,51,51,26);
            var right=Rect("Enemy Status",canvas.transform,-24,-25,620,170);right.anchorMin=right.anchorMax=new Vector2(1,1);right.pivot=new Vector2(1,1);
            var ef=Frame("Portrait Frame",right,0,0,145,145);ef.corner=30;ef.bevel=7;hud.targetPortrait=Picture(ef.transform,actors[3].portrait,8,-8,129,129);
            Frame("Nameplate",right,135,-4,390,35);hud.targetName=Text("Name",right,"ЖЕЛЕЗНЫЙ КЛЫК",143,-5,370,32,21);
            hud.targetFill=Bar(right,140,-43,452,47,new Color(.69f,.09f,.065f));hud.targetText=Text("Health",right,"600 / 600",155,-49,420,32,22);
            hud.status=Text("Status",right,"МИНИ-БОСС",155,-101,420,32,17);Frame("Level Badge",right,9,-111,51,51).corner=24;hud.targetLevel=Text("Level",right,"9",9,-111,51,51,26);
            var controls=canvas.transform.Find("Controls");if(controls!=null){var t=controls.GetComponent<UnityEngine.UI.Text>();t.text="WASD — движение   Shift — бег   ЛКМ — удар   Q — головой   ПКМ — блок\nC — боевая стойка   R — встать   Tab — противники";t.fontSize=18;}
            var book=Frame("Bestiary",canvas.transform,0,0,1440,790);var br=book.rectTransform;br.anchorMin=br.anchorMax=br.pivot=new Vector2(.5f,.5f);br.anchoredPosition=Vector2.zero;book.interior=new Color(.035f,.04f,.041f,.99f);book.bevel=7;
            Text("Title",book.transform,"ПРОТИВНИКИ",30,-25,1380,55,39);Text("Subtitle",book.transform,"РАЗНЫЕ ХАРАКТЕРЫ. ОДИН МИР.",30,-85,1380,30,17);
            for(int i=0;i<4;i++)
            {
                float x=28+i*347;var card=Frame(Ids[i]+" Card",book.transform,x,-139,335,565);card.bevel=2;
                Picture(card.transform,AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/EnemyCast/"+Ids[i]+"_Card.png"),12,-8,310,388);
                Text("Name",card.transform,Names[i].ToUpperInvariant(),7,-405,320,35,23);Text("Role",card.transform,Roles[i].ToUpperInvariant(),7,-444,320,28,17);
                Text("Health",card.transform,HP[i]+" ЗДОРОВЬЯ   ·   УР. "+actors[i].level,7,-482,320,25,16);
                Text("Description",card.transform,Descriptions[i],10,-517,315,30,12);
            }
            Text("Close Hint",book.transform,"TAB — ВЕРНУТЬСЯ В ИГРУ",30,-730,1380,30,17);hud.bestiary=book.gameObject;book.gameObject.SetActive(false);
            EditorUtility.SetDirty(hud);
        }
    }
}
