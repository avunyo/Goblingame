using System;
using UnityEngine;
namespace Goblingame.TestScene
{
    /// <summary>One blow, described once so every receiver reacts the same way.</summary>
    public struct HitInfo
    {
        public float damage;
        public Vector3 source;     // world position of the attacker
        public float stagger;      // seconds the victim is interrupted
        public float knockback;    // metres pushed away from the source
        public bool heavy;         // finisher / headbutt
    }

    /// <summary>Global combat feed (damage numbers, camera shake, hit-stop).</summary>
    public static class CombatEvents
    {
        public enum Kind { Damage, Blocked, Parried, Heal, Dodged, Reward }
        public static event Action<Vector3, float, Kind, bool> Number;
        public static void Raise(Vector3 at, float amount, Kind kind, bool onPlayer) => Number?.Invoke(at, amount, kind, onPlayer);
    }

    public sealed class DamageableTarget : MonoBehaviour
    {
        [SerializeField] private float maxHealth=100f;
        [SerializeField] private float health=100f;
        public float Health=>health;
        public float MaxHealth=>maxHealth;
        public float Fraction=>maxHealth>0f?health/maxHealth:0f;
        public bool IsDead=>health<=0f;
        public event Action Damaged;
        public event Action<HitInfo> Hit;
        public event Action Died;
        public static readonly System.Collections.Generic.List<DamageableTarget> All=new System.Collections.Generic.List<DamageableTarget>();
        private void OnEnable(){if(!All.Contains(this))All.Add(this);}
        private void OnDisable(){All.Remove(this);}
        private Collider cachedCollider;
        public Collider MainCollider{get{if(cachedCollider==null)cachedCollider=GetComponentInChildren<Collider>();return cachedCollider;}}
        public Vector3 HitPoint { get { var c=MainCollider;return c!=null?c.bounds.center:transform.position+Vector3.up; } }
        public float Radius { get { var cc=GetComponent<CharacterController>();return cc!=null?cc.radius:.4f; } }

        public void TakeDamage(float amount)=>TakeHit(new HitInfo{damage=amount,source=transform.position-transform.forward});
        public void TakeHit(HitInfo hit)
        {
            if(IsDead||hit.damage<=0f)return;
            health=Mathf.Max(0f,health-hit.damage);
            CombatEvents.Raise(HitPoint+Vector3.up*.4f,hit.damage,CombatEvents.Kind.Damage,false);
            Hit?.Invoke(hit);Damaged?.Invoke();
            if(IsDead)Died?.Invoke();
        }
        public void RestoreHealth(){health=maxHealth;}
        public void Heal(float amount){if(!IsDead)health=Mathf.Min(maxHealth,health+amount);}
    }
}
