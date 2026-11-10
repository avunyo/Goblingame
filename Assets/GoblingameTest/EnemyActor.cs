using System.Collections.Generic;
using UnityEngine;
namespace Goblingame.TestScene
{
    /// <summary>
    /// Readable melee enemy: patrol → chase → circle → telegraphed wind-up ("!") → strike → recover.
    /// Only one enemy attacks at a time (attack token), the others circle and wait.
    /// The models only have Idle/Walk clips, so attack / hurt / death poses are procedural.
    /// </summary>
    [RequireComponent(typeof(CharacterController),typeof(DamageableTarget))]
    public sealed class EnemyActor : MonoBehaviour
    {
        public string id, displayName, role;
        public int level=1, experienceReward=25;
        public float walkSpeed=1.2f, hitDamage=12, alertRadius=6f;
        public bool elite;
        public Texture2D portrait;
        public Transform visual;

        [Header("Combat feel")]
        public float windup=.7f;
        public float strikeTime=.28f;
        public float recover=.9f;
        public float attackRange=1.4f;
        public float lunge=.8f;
        public float poise=30f;
        public float attackCooldown=1.0f;
        public float leash=16f;
        public float respawnDelay=15f;

        public static readonly List<EnemyActor> All=new List<EnemyActor>();
        private static EnemyActor token;

        private enum State{Patrol,Return,Chase,Circle,Windup,Strike,Recover,Stagger,Dead}
        private State state=State.Patrol;
        private Animator animator;
        private CharacterController controller;
        private DamageableTarget health;
        private GoblinCombat player;
        private Vector3 home,destination,knock;
        private Quaternion restRotation;private Vector3 restPosition,restScale;
        private float timer,cooldown,vertical,poiseDamage,flash,lean,circleSign=1f,alertPop;
        private bool walking,struck;
        private TextMesh alert;
        private Camera view;

        public DamageableTarget Health=>health;
        public bool IsEngaged=>state==State.Chase||state==State.Circle||state==State.Windup||state==State.Strike||state==State.Recover||state==State.Stagger;
        public bool IsTelegraphing=>state==State.Windup;
        public float TelegraphProgress=>state==State.Windup?1f-Mathf.Clamp01(timer/windup):0f;
        public string Status=>state switch{
            State.Dead=>"ПОВЕРЖЕН",State.Windup=>"ГОТОВИТ УДАР — БЛОК!",State.Strike=>"АТАКУЕТ",
            State.Recover=>"ОТКРЫТ — БЕЙ!",State.Stagger=>"ОГЛУШЁН",State.Circle=>"ВЫЖИДАЕТ",
            State.Chase=>"ПРЕСЛЕДУЕТ",State.Return=>"ОТСТУПАЕТ",_=>"НЕ ЗАМЕЧАЕТ"};

        private void OnEnable(){if(!All.Contains(this))All.Add(this);}
        private void OnDisable(){All.Remove(this);if(token==this)token=null;}

        private void Start()
        {
            controller=GetComponent<CharacterController>();health=GetComponent<DamageableTarget>();
            if(visual==null){animator=GetComponentInChildren<Animator>();visual=animator!=null?animator.transform:transform;}
            animator=visual.GetComponentInChildren<Animator>();if(animator==null)animator=GetComponentInChildren<Animator>();
            player=FindFirstObjectByType<GoblinCombat>();
            home=transform.position;destination=home+transform.right*1.5f;timer=Random.Range(.5f,2f);circleSign=Random.value<.5f?-1f:1f;
            restRotation=visual.localRotation;restPosition=visual.localPosition;restScale=visual.localScale;
            health.Died+=OnDeath;health.Hit+=OnHit;
            if(animator!=null&&animator.runtimeAnimatorController!=null){animator.applyRootMotion=false;animator.Play("Idle",0,Random.value);}
            BuildAlert();
        }
        private void OnDestroy(){if(health!=null){health.Died-=OnDeath;health.Hit-=OnHit;}}

        private void BuildAlert()
        {
            var go=new GameObject("Alert");go.transform.SetParent(transform,false);
            go.transform.localPosition=Vector3.up*(controller.height+.75f);
            alert=go.AddComponent<TextMesh>();alert.text="!";alert.fontSize=96;alert.characterSize=.05f;alert.anchor=TextAnchor.MiddleCenter;alert.alignment=TextAlignment.Center;alert.fontStyle=FontStyle.Bold;
            var font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");alert.font=font;go.GetComponent<MeshRenderer>().sharedMaterial=font.material;
            alert.color=new Color(1f,.25f,.1f,0f);
        }

        private void OnHit(HitInfo hit)
        {
            if(state==State.Dead)return;
            flash=1f;
            Vector3 away=transform.position-hit.source;away.y=0;
            if(state==State.Patrol||state==State.Return)Engage();
            poiseDamage+=hit.damage;
            bool armoured=state==State.Strike;
            if(!armoured&&(hit.heavy||poiseDamage>=poise||state==State.Recover))
            {
                knock=away.normalized*hit.knockback*5f;
                Stagger(hit.stagger+.15f);poiseDamage=0f;
            }
            else knock=away.normalized*hit.knockback*1.5f;
        }
        private void Stagger(float seconds){ReleaseToken();state=State.Stagger;timer=seconds;}
        private void Engage(){if(state==State.Patrol||state==State.Return){state=State.Chase;alertPop=1f;}}
        private void ReleaseToken(){if(token==this)token=null;}

        private void OnDeath()
        {
            ReleaseToken();state=State.Dead;timer=respawnDelay;SetWalking(false,0f);
            var xp=player!=null?player.GetComponent<PlayerProgression>():null;if(xp!=null)xp.AddExperience(experienceReward);
        }

        private void Update()
        {
            if(health==null||player==null)return;
            float dt=Time.deltaTime;
            flash=Mathf.MoveTowards(flash,0f,dt*4f);alertPop=Mathf.MoveTowards(alertPop,0f,dt*1.5f);
            if(state==State.Dead){UpdateDead(dt);Pose(dt);return;}
            cooldown-=dt;
            Vector3 toPlayer=player.transform.position-transform.position;toPlayer.y=0;
            float dist=toPlayer.magnitude;
            float gap=dist-controller.radius-.4f;
            Vector3 dirToPlayer=dist>.001f?toPlayer/dist:transform.forward;
            Vector3 move=Vector3.zero;float speed=walkSpeed;
            float fromHome=Flat(transform.position-home).magnitude;

            if(player.IsDead&&IsEngaged){ReleaseToken();state=State.Return;}

            switch(state)
            {
                case State.Patrol:
                    if(!player.IsDead&&dist<alertRadius){Engage();break;}
                    timer-=dt;
                    if(timer<=0f)
                    {
                        Vector3 delta=Flat(destination-transform.position);
                        if(delta.magnitude<.15f){timer=Random.Range(1.5f,3.5f);destination=home+Quaternion.Euler(0,Random.Range(0,360f),0)*Vector3.forward*Random.Range(.8f,2f);}
                        else{move=delta.normalized;Face(move,240f);}
                    }
                    break;
                case State.Return:
                    {
                        health.Heal(health.MaxHealth*.25f*dt);
                        if(!player.IsDead&&dist<alertRadius*.6f&&fromHome<leash*.7f){Engage();break;}
                        Vector3 delta=Flat(home-transform.position);
                        if(delta.magnitude<.3f){state=State.Patrol;timer=1f;}
                        else{move=delta.normalized;speed=walkSpeed*1.5f;Face(move,300f);}
                    }
                    break;
                case State.Chase:
                    if(fromHome>leash){ReleaseToken();state=State.Return;break;}
                    Face(dirToPlayer,360f);
                    if(gap<=attackRange){if(!TryAttack())state=State.Circle;}
                    else{move=dirToPlayer;speed=walkSpeed*1.6f;}
                    break;
                case State.Circle:
                    {
                        if(fromHome>leash){state=State.Return;break;}
                        Face(dirToPlayer,360f);
                        if(TryAttack())break;
                        float want=attackRange+1.3f;
                        if(gap>want+1.8f){state=State.Chase;break;}
                        Vector3 side=Vector3.Cross(Vector3.up,dirToPlayer)*circleSign;
                        move=dirToPlayer*Mathf.Clamp(gap-want,-1f,1f)+side*.7f;
                        if(Random.value<dt*.25f)circleSign=-circleSign;
                        speed=walkSpeed*.8f;
                    }
                    break;
                case State.Windup:
                    timer-=dt;
                    Face(dirToPlayer,timer>windup*.3f?300f:60f);
                    if(timer<=0f){state=State.Strike;timer=strikeTime;struck=false;}
                    break;
                case State.Strike:
                    {
                        float t=1f-timer/strikeTime;timer-=dt;
                        if(t<.6f&&gap>.1f){move=transform.forward;speed=lunge/(strikeTime*.6f);}
                        if(!struck&&t>=.35f)
                        {
                            struck=true;
                            bool inReach=gap<=attackRange+.5f&&Vector3.Angle(transform.forward,dirToPlayer)<70f;
                            if(inReach)
                            {
                                var result=player.TakeDamage(hitDamage,transform.position);
                                if(result==HitResult.Parried){Stagger(1.5f);knock=-dirToPlayer*3f;break;}
                            }
                        }
                        if(timer<=0f){state=State.Recover;timer=recover;}
                    }
                    break;
                case State.Recover:
                    timer-=dt;
                    if(timer<=0f){ReleaseToken();cooldown=attackCooldown;state=State.Circle;}
                    break;
                case State.Stagger:
                    timer-=dt;
                    if(timer<=0f){cooldown=attackCooldown*.6f;state=State.Circle;}
                    break;
            }

            // movement
            if(controller.isGrounded&&vertical<0)vertical=-2;vertical-=20*dt;
            Vector3 before=transform.position;
            Vector3 velocity=move.normalized*speed*Mathf.Clamp01(move.magnitude)+knock;
            knock=Vector3.MoveTowards(knock,Vector3.zero,dt*12f);
            controller.Move((velocity+Vector3.up*vertical)*dt);
            Vector3 travel=Flat(transform.position-before);
            float moved=dt>0?travel.magnitude/dt:0f;
            SetWalking(moved>.15f&&move.sqrMagnitude>0f,moved/Mathf.Max(.1f,walkSpeed));
            Pose(dt);
        }

        private bool TryAttack()
        {
            if(cooldown>0f||player.IsDead)return false;
            if(token!=null&&token!=this&&token.isActiveAndEnabled&&token.state!=State.Dead)return false;
            token=this;state=State.Windup;timer=windup;return true;
        }

        private void UpdateDead(float dt)
        {
            controller.enabled=false;timer-=dt;
            if(timer<=0f)
            {
                transform.position=home;visual.localRotation=restRotation;visual.localPosition=restPosition;visual.localScale=restScale;
                health.RestoreHealth();controller.enabled=true;state=State.Patrol;timer=2f;lean=0f;poiseDamage=0f;
            }
        }

        private void Pose(float dt)
        {
            float targetLean=0f,sink=0f;
            switch(state)
            {
                case State.Windup:targetLean=-14f*Mathf.SmoothStep(0,1,1f-timer/windup);break;
                case State.Strike:targetLean=22f;break;
                case State.Recover:targetLean=8f*Mathf.Clamp01(timer/recover);break;
                case State.Stagger:targetLean=-18f;break;
                case State.Dead:targetLean=-80f;sink=Mathf.Clamp01((respawnDelay-timer-3f)/2f)*.8f;break;
            }
            float k=state==State.Strike?1f-Mathf.Exp(-30f*dt):1f-Mathf.Exp(-10f*dt);
            lean=Mathf.Lerp(lean,targetLean,k);
            // shake during wind-up and after a hit
            Vector3 jitter=Vector3.zero;
            if(state==State.Windup)jitter=Random.insideUnitSphere*.025f*TelegraphProgress;
            jitter+=Random.insideUnitSphere*.05f*flash;
            visual.localRotation=restRotation;visual.localPosition=restPosition;
            visual.rotation=Quaternion.AngleAxis(lean,transform.right)*visual.rotation;
            visual.position+=jitter+Vector3.down*sink;
            float squash=1f+.1f*flash;
            visual.localScale=new Vector3(restScale.x*squash,restScale.y/squash,restScale.z*squash);
        }

        private void LateUpdate()
        {
            if(alert==null)return;
            if(view==null)view=Camera.main;
            float a=state==State.Windup?.6f+.4f*Mathf.PingPong(Time.time*8f,1f):alertPop;
            alert.text=state==State.Windup?"!":"?";
            alert.color=state==State.Windup?new Color(1f,.22f,.08f,a):new Color(1f,.85f,.3f,a);
            float s=state==State.Windup?1f+.4f*TelegraphProgress:1f+(1f-alertPop)*.3f;
            alert.transform.localScale=Vector3.one*s;
            if(view!=null)alert.transform.rotation=view.transform.rotation;
        }

        private void Face(Vector3 direction,float degreesPerSecond){direction.y=0;if(direction.sqrMagnitude>.001f)transform.rotation=Quaternion.RotateTowards(transform.rotation,Quaternion.LookRotation(direction),degreesPerSecond*Time.deltaTime);}
        private void SetWalking(bool value,float rate)
        {
            if(animator==null||animator.runtimeAnimatorController==null)return;
            animator.speed=value?Mathf.Clamp(rate,.7f,2.2f):1f;
            if(walking==value)return;walking=value;animator.CrossFadeInFixedTime(value?"Walk":"Idle",.2f);
        }
        private static Vector3 Flat(Vector3 v){v.y=0;return v;}
    }
}
