using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
namespace Goblingame.TestScene.Editor
{
    [InitializeOnLoad]
    public static class GoblinCombatSmokeCheck
    {
        const string Request="Temp/GoblinExport/combat-smoke.request",Result="Temp/GoblinExport/combat-smoke.txt";
        static int phase;static double next;static GoblinCombat player;static TrainingDummy dummy;static DamageableTarget target;
        static float yaw;
        static float headBeforeDeath;
        static Mesh eyeMesh;static Vector2[] eyeRest;static float eyeMotion;
        static EnemyActor[] cast;static Vector3[] castPositions;
        static GoblinCombatSmokeCheck(){if(File.Exists(Request))EditorApplication.update+=Tick;}
        public static void RunBatch()
        {
            Directory.CreateDirectory("Temp/GoblinExport");File.WriteAllText(Request,"batch");
            SessionState.SetBool("GoblinCombatBatchCheck",true);
            UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/GoblinPlayable.unity");
            EditorApplication.update-=Tick;EditorApplication.update+=Tick;
        }
        static object Get(object obj,string name)=>obj.GetType().GetField(name,BindingFlags.NonPublic|BindingFlags.Instance).GetValue(obj);
        static void Set(object obj,string name,object value)=>obj.GetType().GetField(name,BindingFlags.NonPublic|BindingFlags.Instance).SetValue(obj,value);
        static object Call(object obj,string name,params object[] args)=>obj.GetType().GetMethod(name,BindingFlags.NonPublic|BindingFlags.Instance).Invoke(obj,args);
        static void Check(bool condition,string text){if(!condition)throw new Exception(text);}
        static void Tick()
        {
            if(EditorApplication.isCompiling||EditorApplication.isUpdating)return;
            if(!EditorApplication.isPlaying){EditorApplication.isPlaying=true;return;}
            if(phase==8&&player!=null)Call(player,"LateUpdate");
            if(Time.time<next)return;
            try
            {
                if(phase==0){Application.runInBackground=true;phase=1;next=Time.time+2;return;}
                if(phase==1)
                {
                    player=UnityEngine.Object.FindFirstObjectByType<GoblinCombat>();dummy=UnityEngine.Object.FindFirstObjectByType<TrainingDummy>();target=dummy.GetComponent<DamageableTarget>();
                    if(File.ReadAllText(Request).StartsWith("CAST"))
                    {
                        cast=UnityEngine.Object.FindObjectsByType<EnemyActor>(FindObjectsSortMode.None);Check(cast.Length==4,"Four enemy actors");
                        castPositions=new Vector3[4];for(int i=0;i<4;i++){castPositions[i]=cast[i].transform.position;Check(cast[i].Health!=null&&cast[i].Health.MaxHealth>0,"Enemy health");Check(cast[i].portrait!=null,"Enemy portrait");}
                        var castHud=UnityEngine.Object.FindFirstObjectByType<CombatHUD>();Check(castHud.bestiary!=null&&castHud.experienceFill!=null&&castHud.targetName!=null,"Designed HUD references");
                        Check(player.MaxHealth==200,"Player health matches designed HUD");
                        phase=9;next=Time.time+5f;return;
                    }
                    if(File.ReadAllText(Request).StartsWith("WATCH"))
                    {
                        dummy.enabled=false;player.GetComponent<GoblinMovement>().enabled=false;player.enabled=false;
                        Set(player,"ready",true);Set(player,"health",100f);Set(player,"attack",null);Set(player,"blocking",false);
                        Set(player,"<InCombat>k__BackingField",false);Call(player,"ResetIdleTimer");
                        Set(player,"lastActionTime",Time.time-100f);Call(player,"Update");Check((string)Get(player,"currentState")=="Idle","No watch outside combat");Call(player,"EnterCombat");
                        Set(player,"lastActionTime",Time.time-64f);Call(player,"Update");
                        Check((string)Get(player,"currentState")=="CombatReady","No watch gesture before 65 seconds");
                        Set(player,"lastActionTime",Time.time-66f);Call(player,"Update");
                        Check((string)Get(player,"currentState")=="CombatIdle","Watch gesture after 65 seconds in combat");
                        var a=player.GetComponentInChildren<Animator>();a.Play("Base Layer.CombatIdle",0,.99f);a.Update(0);
                        Call(player,"Update");Check((string)Get(player,"currentState")=="CombatReady","Return to combat stance");
                        Call(player,"Update");Check((string)Get(player,"currentState")=="CombatReady","No repeated watch loop");
                        Call(player,"BeginAttack","Punch");Check(!(bool)Get(player,"playedCombatFidget")&&Mathf.Abs((float)Get(player,"lastActionTime")-Time.time)<.01f,"Action resets waiting timer");
                        string report="PASS: no watch outside combat; no watch at 64s; watch at 66s in combat; returns to CombatReady without looping; action resets timer.";
                        File.WriteAllText(Result,report);Debug.Log(report);File.Delete(Request);EditorApplication.update-=Tick;EditorApplication.isPlaying=false;return;
                    }
                    if(File.ReadAllText(Request).StartsWith("ALIGN"))
                    {
                        dummy.enabled=false;player.GetComponent<GoblinMovement>().enabled=false;player.enabled=false;
                        var a=player.GetComponentInChildren<Animator>();var eyes=player.GetComponent<GoblinIdleEyes>();eyes.enabled=false;
                        a.Play("Base Layer.Idle",0,0);a.Update(0);
                        var head=a.GetBoneTransform(HumanBodyBones.Head);
                        var neutral=(Quaternion)Get(eyes,"neutralHead");var up=(Vector3)Get(eyes,"modelUp");
                        foreach(float angle in new[]{12f,-12f,0f})
                        {
                            head.rotation=a.transform.rotation*Quaternion.AngleAxis(angle,up)*neutral;
                            for(int i=0;i<90;i++)Call(eyes,"LateUpdate");
                            float x=((Vector2)Get(eyes,"gaze")).x;
                            Check(angle==0?Mathf.Abs(x)<.0001f:Mathf.Sign(x)==Mathf.Sign(angle)&&Mathf.Abs(x)>.002f,"Pupils follow head direction "+angle+"; actual="+x);
                        }
                        Set(player,"currentYaw",Get(player,"runYaw"));Set(player,"yawVelocity",0f);
                        Call(player,"ChangeState","Idle",0f,true,0f);a.Update(0);
                        phase=8;next=Time.time+1f;return;
                    }
                    if(File.ReadAllText(Request).StartsWith("EYES"))
                    {
                        dummy.enabled=false;player.GetComponent<GoblinMovement>().enabled=false;player.enabled=false;
                        var eyes=player.GetComponent<GoblinIdleEyes>();Check(eyes!=null&&eyes.enabled,"Eye component initialized");
                        eyeMesh=(Mesh)Get(eyes,"instance");eyeRest=((Mesh)Get(eyes,"original")).uv;
                        Check(eyeMesh!=null&&eyeMesh.isReadable,"Private readable eye mesh");
                        var a=player.GetComponentInChildren<Animator>();a.Play("Base Layer.Idle",0,.08f);a.Update(0);
                        phase=6;next=Time.time+.8f;return;
                    }
                    if(File.ReadAllText(Request).StartsWith("DEATH"))
                    {
                        dummy.enabled=false;player.GetComponent<GoblinMovement>().enabled=false;player.enabled=false;
                        var deathAnimator=player.GetComponentInChildren<Animator>();
                        headBeforeDeath=deathAnimator.GetBoneTransform(HumanBodyBones.Head).position.y;
                        Call(player,"BeginAttack","Melee");player.TakeDamage(1000,player.transform.position-player.transform.forward);
                        Check(player.IsDead&&player.MovementLocked&&Get(player,"attack")==null,"Lethal hit interrupts attack immediately");
                        phase=5;next=Time.time+.35f;return;
                    }
                    Check(player.Health==100,"Player initial health");
                    var hud=UnityEngine.Object.FindFirstObjectByType<CombatHUD>();Check(hud!=null&&hud.playerFill.sprite!=null&&hud.target==target,"HUD references");
                    yaw=(float)Get(player,"runYaw");Check(!float.IsNaN(yaw),"Run alignment finite");
                    dummy.transform.position=player.transform.position+player.transform.forward*1.8f+Vector3.up;Physics.SyncTransforms();
                    Call(player,"BeginAttack","Punch");phase=2;next=Time.time+2;
                }
                else if(phase==2)
                {
                    Check(target.Health==84,"Punch must hit once for 16");
                    Check(Get(player,"attack")==null,"Attack completed before fidget check");
                    float health=player.Health;Set(player,"blocking",true);player.TakeDamage(25,player.transform.position+player.transform.forward);Check(Mathf.Abs(player.Health-(health-2.5f))<.01f,"Front block mitigation");Set(player,"blocking",false);
                    health=player.Health;player.TakeDamage(25,player.transform.position-player.transform.forward);Check(player.Health==health-25,"Unblocked damage");
                    target.TakeDamage(200);Check(dummy.IsDown,"Dummy down");
                    dummy.transform.position+=Vector3.right*10;Call(player,"EnterCombat");Set(player,"lastActionTime",Time.time-66f);
                    phase=3;next=Time.time+1;
                }
                else if(phase==3)
                {
                    Check((string)Get(player,"currentState")=="CombatIdle","One-shot fidget started; actual="+Get(player,"currentState"));phase=4;next=Time.time+6.5;
                }
                else if(phase==4)
                {
                    Check(target.Health==100&&!dummy.IsDown,"Dummy restored");Check((string)Get(player,"currentState")=="CombatReady","Fidget returned to combat stance without looping");
                    var animator=player.GetComponentInChildren<Animator>();headBeforeDeath=animator.GetBoneTransform(HumanBodyBones.Head).position.y;
                    Call(player,"BeginAttack","Melee");player.TakeDamage(1000,player.transform.position-player.transform.forward);
                    Check(player.IsDead&&player.MovementLocked&&Get(player,"attack")==null,"Lethal hit interrupts attack immediately");
                    phase=5;next=Time.time+.35f;
                }
                else if(phase==9)
                {
                    for(int i=0;i<4;i++)Check(Vector3.Distance(castPositions[i],cast[i].transform.position)>.15f,"Enemy patrol movement: "+cast[i].displayName);
                    var xp=player.GetComponent<PlayerProgression>();int before=xp.Experience;int level=xp.Level;
                    cast[0].Health.TakeDamage(9999);Check(cast[0].Health.IsDead,"Enemy can be defeated");Check(xp.Experience!=before||xp.Level!=level,"Enemy awards experience");
                    var hud=UnityEngine.Object.FindFirstObjectByType<CombatHUD>();hud.bestiary.SetActive(true);
                    phase=10;next=Time.time+.5f;
                }
                else if(phase==10)
                {
                    ScreenCapture.CaptureScreenshot("ArtSource/EnemyCast/Unity_Bestiary.png");phase=11;next=Time.time+1f;
                }
                else if(phase==11)
                {
                    UnityEngine.Object.FindFirstObjectByType<CombatHUD>().bestiary.SetActive(false);phase=12;next=Time.time+.5f;
                }
                else if(phase==12)
                {
                    ScreenCapture.CaptureScreenshot("ArtSource/EnemyCast/Unity_HUD.png");phase=13;next=Time.time+1f;
                }
                else if(phase==13)
                {
                    string report="PASS: four enemies initialized and patrolled; health, portraits and HUD connected; enemy defeat awards experience; screenshots captured.";
                    File.WriteAllText(Result,report);System.IO.File.WriteAllText("ArtSource/EnemyCast/Unity_Validation.txt",report);Debug.Log(report);File.Delete(Request);EditorApplication.update-=Tick;EditorApplication.isPlaying=false;
                }
                else if(phase==8)
                {
                    Check(Mathf.Abs(Mathf.DeltaAngle((float)Get(player,"currentYaw"),(float)Get(player,"idleYaw")))<.1f,"Run correction fully replaced by idle correction");
                    var a=player.GetComponentInChildren<Animator>();
                    Vector3 right=(a.GetBoneTransform(HumanBodyBones.RightUpperLeg).position-a.GetBoneTransform(HumanBodyBones.LeftUpperLeg).position).normalized
                        +(a.GetBoneTransform(HumanBodyBones.RightUpperArm).position-a.GetBoneTransform(HumanBodyBones.LeftUpperArm).position).normalized;
                    right.y=0;float error=Vector3.Angle(Vector3.Cross(right,Vector3.up),player.transform.forward);
                    Check(error<2f,"Idle faces player forward; error="+error);
                    string report="PASS: pupils follow head left/right and centre; run to idle faces forward. Error="+error+"; idle correction="+Get(player,"idleYaw");
                    File.WriteAllText(Result,report);Debug.Log(report);File.Delete(Request);EditorApplication.update-=Tick;EditorApplication.isPlaying=false;
                }
                else if(phase==6)
                {
                    var uv=eyeMesh.uv;int changed=0;eyeMotion=0;
                    for(int i=0;i<uv.Length;i++)
                    {
                        float distance=Vector2.Distance(uv[i],eyeRest[i]);eyeMotion=Mathf.Max(eyeMotion,distance);
                        if(distance>.00001f){changed++;Check(GoblinIdleEyes.PupilWeight(eyeRest[i])>0,"Only pupil region changes");}
                    }
                    Check(changed>=10&&eyeMotion>.001f,"Visible idle eye motion");
                    player.GetComponentInChildren<Animator>().Play("Base Layer.Walk",0,0);
                    phase=7;next=Time.time+.9f;
                }
                else if(phase==7)
                {
                    var uv=eyeMesh.uv;float remaining=0;for(int i=0;i<uv.Length;i++)remaining=Mathf.Max(remaining,Vector2.Distance(uv[i],eyeRest[i]));
                    Check(remaining<.0001f,"Eyes return to centre during walk");
                    string report="PASS: idle pupils move; other face UVs unchanged; gaze returns to centre during walking. Peak UV motion="+eyeMotion;
                    File.WriteAllText(Result,report);Debug.Log(report);File.Delete(Request);EditorApplication.update-=Tick;EditorApplication.isPlaying=false;
                }
                else
                {
                    var animator=player.GetComponentInChildren<Animator>();
                    Check(animator.GetCurrentAnimatorStateInfo(0).IsName("Base Layer.Death"),"Death state entered immediately");
                    Check(animator.GetBoneTransform(HumanBodyBones.Head).position.y<headBeforeDeath-.03f,"Collapse visible within 0.35 seconds");
                    string report="PASS: initial health; HUD references; punch hits once; front block; damage; dummy death/restore; fidget returns to ready; lethal hit interrupts attack and visibly collapses within 0.35 seconds. Measured run yaw correction: "+yaw;
                    if(File.ReadAllText(Request).StartsWith("DEATH"))report="PASS: lethal hit interrupts attack, locks movement and visibly collapses within 0.35 seconds.";
                    File.WriteAllText(Result,report);Debug.Log(report);
                    File.Delete(Request);EditorApplication.update-=Tick;EditorApplication.isPlaying=false;
                    if(SessionState.GetBool("GoblinCombatBatchCheck",false))EditorApplication.Exit(0);
                }
            }
            catch(Exception e){File.WriteAllText(Result,"FAIL: "+e);File.Delete(Request);EditorApplication.update-=Tick;EditorApplication.isPlaying=false;Debug.LogException(e);if(SessionState.GetBool("GoblinCombatBatchCheck",false))EditorApplication.Exit(1);}
        }
    }
}




