using UnityEngine;
namespace Goblingame.TestScene
{
    [RequireComponent(typeof(DamageableTarget))]
    public sealed class TrainingDummy : MonoBehaviour
    {
        [SerializeField] private GoblinCombat player;
        [SerializeField] private Renderer visual;
        private enum State { Waiting, Windup, Recovery, Staggered, Down }
        [SerializeField] private State state;
        private DamageableTarget target;
        private float timer, nextStagger, flash;
        private MaterialPropertyBlock color;
        private Quaternion restRotation;
        public bool IsDown => target != null && target.IsDead;
        public string Status => state == State.Windup ? "ГОТОВИТ УДАР — БЛОК!" : state == State.Recovery ? "ПАУЗА — АТАКУЙ" : state == State.Staggered ? "ОГЛУШЁН" : state == State.Down ? "ВОССТАНАВЛИВАЕТСЯ" : "ГОТОВ К ТРЕНИРОВКЕ";
        private void Awake()
        {
            target=GetComponent<DamageableTarget>();target.Damaged+=OnHit;target.Died+=OnDown;
            if(visual==null)visual=GetComponentInChildren<Renderer>();
            restRotation=visual!=null?visual.transform.localRotation:transform.localRotation;color=new MaterialPropertyBlock();timer=1.3f;
        }
        private void OnDestroy(){if(target!=null){target.Damaged-=OnHit;target.Died-=OnDown;}}
        private void Update()
        {
            timer-=Time.deltaTime;flash=Mathf.MoveTowards(flash,0,Time.deltaTime*3);
            bool engaged=player!=null&&!player.IsDead&&player.InCombat&&InReach();
            if(state==State.Down){if(timer<=0){target.RestoreHealth();state=State.Waiting;timer=1.5f;}}
            else if(!engaged){state=State.Waiting;timer=Mathf.Max(timer,.8f);}
            else if(timer<=0)
            {
                switch(state)
                {
                    case State.Waiting:state=State.Windup;timer=.9f;break;
                    case State.Windup:if(InReach())player.TakeDamage(25,transform.position);state=State.Recovery;timer=1.4f;break;
                    default:state=State.Waiting;timer=.25f;break;
                }
            }
            if(visual==null)return;
            Color shade=Color.Lerp(new Color(.35f,.18f,.08f),new Color(.65f,.4f,.15f),target.Fraction);
            if(state==State.Down)shade=Color.gray;
            if(state==State.Windup)shade=Color.Lerp(Color.yellow,Color.red,1-Mathf.Clamp01(timer/.9f));
            color.SetColor("_BaseColor",Color.Lerp(shade,Color.white,flash));visual.SetPropertyBlock(color);
            float lean=state==State.Down?65:state==State.Windup?-10:state==State.Recovery?18*Mathf.Clamp01(timer/1.4f):flash*10;
            visual.transform.localRotation=Quaternion.Slerp(visual.transform.localRotation,restRotation*Quaternion.Euler(lean,0,0),1-Mathf.Exp(-12*Time.deltaTime));
        }
        private bool InReach()
        {
            Vector3 delta=player.transform.position-transform.position;delta.y=0;
            if(delta.sqrMagnitude>2.2f*2.2f)return false;
            Vector3 origin=transform.position+Vector3.up*.4f, direction=player.transform.position+Vector3.up-origin;
            var hits=Physics.RaycastAll(origin,direction.normalized,direction.magnitude+.1f);
            System.Array.Sort(hits,(a,b)=>a.distance.CompareTo(b.distance));
            foreach(var hit in hits){if(hit.transform.IsChildOf(transform))continue;return hit.transform.IsChildOf(player.transform);}
            return true;
        }
        private void OnHit(){flash=1;if(Time.time>=nextStagger){state=State.Staggered;timer=.4f;nextStagger=Time.time+2;}}
        private void OnDown(){state=State.Down;timer=4;}
        public void TakeDamage(float amount){target.TakeDamage(amount);}
    }
}
