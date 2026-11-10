using UnityEngine;
namespace Goblingame.TestScene
{
    [RequireComponent(typeof(CharacterController),typeof(DamageableTarget))]
    public sealed class EnemyActor : MonoBehaviour
    {
        public string displayName, role;
        public int level=1, experienceReward=25;
        public float walkSpeed=1.2f, hitDamage=12, alertRadius=5f;
        public bool elite;
        public Texture2D portrait;
        public Transform visual;
        private Animator animator;
        private CharacterController controller;
        private DamageableTarget health;
        private GoblinCombat player;
        private Vector3 home, destination;
        private Quaternion restRotation;
        private float pause, attackTimer, respawn, vertical;
        private bool walking, attacking, hit;
        public DamageableTarget Health=>health;
        public string Status=>health!=null&&health.IsDead?"ПОВЕРЖЕН":attacking?"ГОТОВИТ УДАР":"";
        private void Start()
        {
            controller=GetComponent<CharacterController>();health=GetComponent<DamageableTarget>();animator=GetComponentInChildren<Animator>();
            player=FindFirstObjectByType<GoblinCombat>();home=transform.position;destination=home+Vector3.right;pause=Random.Range(.5f,2f);
            if(visual==null)visual=animator!=null?animator.transform:transform;
            restRotation=visual.localRotation;
            health.Died+=OnDeath;
            if(animator!=null&&animator.runtimeAnimatorController!=null){animator.applyRootMotion=false;animator.Play("Idle");}
        }
        private void OnDestroy(){if(health!=null)health.Died-=OnDeath;}
        private void OnDeath()
        {
            attacking=false;SetWalking(false);respawn=12f;
            var xp=player!=null?player.GetComponent<PlayerProgression>():null;if(xp!=null)xp.AddExperience(experienceReward);
        }
        private void Update()
        {
            if(health==null||player==null)return;
            if(health.IsDead)
            {
                visual.localRotation=Quaternion.Slerp(visual.localRotation,restRotation*Quaternion.Euler(0,0,78),1-Mathf.Exp(-5*Time.deltaTime));
                controller.enabled=false;respawn-=Time.deltaTime;
                if(respawn<=0){transform.position=home;visual.localRotation=restRotation;health.RestoreHealth();controller.enabled=true;pause=2;}
                return;
            }
            Vector3 toPlayer=player.transform.position-transform.position;toPlayer.y=0;
            bool engaged=player.InCombat&&!player.IsDead&&toPlayer.magnitude<alertRadius;
            Vector3 direction=Vector3.zero;
            if(attacking)
            {
                attackTimer+=Time.deltaTime;
                if(!hit&&attackTimer>=.7f){hit=true;if(engaged&&toPlayer.magnitude<2.1f&&Vector3.Dot(transform.forward,toPlayer.normalized)>.45f)player.TakeDamage(hitDamage,transform.position);}
                visual.localRotation=restRotation*Quaternion.Euler(-10*Mathf.Sin(Mathf.Clamp01(attackTimer/1.3f)*Mathf.PI),0,0);
                if(attackTimer>1.6f){attacking=false;pause=.65f;visual.localRotation=restRotation;}
            }
            else if(engaged)
            {
                Face(toPlayer);pause-=Time.deltaTime;
                if(toPlayer.magnitude>1.75f)direction=toPlayer.normalized;
                else if(pause<=0){attacking=true;hit=false;attackTimer=0;}
            }
            else if(pause>0)pause-=Time.deltaTime;
            else
            {
                Vector3 delta=destination-transform.position;delta.y=0;
                if(delta.magnitude<.12f){pause=Random.Range(1.5f,3.5f);destination=home+(destination.x>home.x?Vector3.left:Vector3.right)*1.25f;}
                else{direction=delta.normalized;Face(direction);}
            }
            if(controller.isGrounded&&vertical<0)vertical=-2;vertical-=20*Time.deltaTime;
            Vector3 before=transform.position;controller.Move((direction*walkSpeed+Vector3.up*vertical)*Time.deltaTime);
            Vector3 travel=transform.position-before;travel.y=0;SetWalking(travel.sqrMagnitude>.0000001f&&direction.sqrMagnitude>0);
        }
        private void Face(Vector3 direction){if(direction.sqrMagnitude>.001f)transform.rotation=Quaternion.RotateTowards(transform.rotation,Quaternion.LookRotation(direction),180*Time.deltaTime);}
        private void SetWalking(bool value){if(walking==value)return;walking=value;if(animator!=null&&animator.runtimeAnimatorController!=null)animator.CrossFadeInFixedTime(value?"Walk":"Idle",.2f);}
    }
}
