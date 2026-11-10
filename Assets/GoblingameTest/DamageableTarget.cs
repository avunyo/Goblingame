using System;
using UnityEngine;
namespace Goblingame.TestScene
{
    public sealed class DamageableTarget : MonoBehaviour
    {
        [SerializeField] private float maxHealth=100f;
        [SerializeField] private float health=100f;
        public float Health=>health;
        public float MaxHealth=>maxHealth;
        public float Fraction=>health/maxHealth;
        public bool IsDead=>health<=0f;
        public event Action Damaged;
        public event Action Died;
        public Vector3 HitPoint { get { var c=GetComponentInChildren<Collider>();return c!=null?c.bounds.center:transform.position; } }
        public void TakeDamage(float amount)
        {if(IsDead||amount<=0f)return;health=Mathf.Max(0f,health-amount);Damaged?.Invoke();if(IsDead)Died?.Invoke();}
        public void RestoreHealth(){health=maxHealth;}
    }
}
