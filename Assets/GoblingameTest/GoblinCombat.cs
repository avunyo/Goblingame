using UnityEngine;
using UnityEngine.InputSystem;

namespace Goblingame.TestScene
{
    [DefaultExecutionOrder(100)]
    public sealed class GoblinCombat : MonoBehaviour
    {
        [SerializeField] private Animator animator;
        [SerializeField] private RuntimeAnimatorController fallbackController;
        [SerializeField] private float punchImpact = 0.4f;
        [SerializeField] private float meleeImpact = 0.4f;
        [SerializeField] private float headbuttImpact = 0.4f;
        [SerializeField] private float maxHealth = 100f;
        private GoblinMovement movement;
        private string currentState;
        private string attack;
        private string queuedAttack;
        private bool dealtHit;
        private bool secondPunch;
        private bool ready;
        private bool blocking;
        private float health;
        private float lastActionTime;
        private bool playedCombatFidget;
        private Quaternion baseModelRotation;
        private Vector3 baseModelPosition;
        public bool InCombat { get; private set; }
        public bool IsDead => ready && health <= 0f;
        public bool MovementLocked => ready && (IsDead || blocking || attack != null);
        public float Health => health;
        public float MaxHealth => maxHealth;

        private void Start()
        {
            movement = GetComponent<GoblinMovement>();
            if (animator == null) animator = GetComponentInChildren<Animator>();
            if (animator != null && animator.runtimeAnimatorController == null && fallbackController != null)
                animator.runtimeAnimatorController = fallbackController;
            if (animator != null && animator.runtimeAnimatorController != null) animator.Update(0f);
            ready = animator != null && animator.HasState(0, Animator.StringToHash("Base Layer.Punch"));
            health = maxHealth;
            ResetIdleTimer();
            if (ready)
            {
                baseModelPosition = animator.transform.localPosition;
                baseModelRotation = animator.transform.localRotation;
                animator.Play(Animator.StringToHash("Base Layer.Idle"),0,0f);animator.Update(0f);
                animator.transform.localRotation = baseModelRotation;
                ChangeState("Idle",0f);
            }
        }

        private void Update()
        {
            if (!ready) return;
            animator.speed = 1f;
            var keyboard = Keyboard.current;
            var mouse = Mouse.current;
            bool focused = Application.isFocused;
            if (IsDead)
            {
                if (focused && keyboard != null && keyboard.rKey.wasPressedThisFrame)
                {
                    health = maxHealth; InCombat = false; attack = null; queuedAttack = null; blocking = false;
                    ResetIdleTimer();
                    ChangeState("Idle", 0.25f);
                }
                return;
            }
            if (focused && keyboard != null && keyboard.cKey.wasPressedThisFrame && attack == null)
            { if(InCombat){InCombat=false;ResetIdleTimer();}else EnterCombat(); }
            bool wantsBlock = focused && mouse != null && mouse.rightButton.isPressed;
            bool punch = focused && mouse != null && mouse.leftButton.wasPressedThisFrame;
            bool headbutt = focused && keyboard != null && keyboard.qKey.wasPressedThisFrame;
            if (attack != null)
            {
                ResetIdleTimer();
                float progress = Progress(attack);
                if ((punch || headbutt) && progress >= 0.35f && queuedAttack == null && !wantsBlock)
                    queuedAttack = headbutt ? "Headbutt" : NextPunch();
                float impact = attack == "Punch" ? punchImpact : attack == "Melee" ? meleeImpact : headbuttImpact;
                if (!dealtHit && progress >= impact)
                {
                    dealtHit = true;
                    HitTarget(attack == "Headbutt" ? 30f : attack == "Melee" ? 22f : 16f);
                }
                if (progress < 0.98f) return;
                attack = null;
                if (queuedAttack != null && !wantsBlock)
                {
                    string next = queuedAttack; queuedAttack = null; BeginAttack(next); return;
                }
                queuedAttack = null;
            }
            blocking = wantsBlock;
            if (blocking) { EnterCombat(); ChangeState("Block", 0.12f); return; }
            if (headbutt || punch) { BeginAttack(headbutt ? "Headbutt" : NextPunch()); return; }
            if (movement != null && movement.IsMoving)
            {
                ResetIdleTimer();
                ChangeState(movement.IsRunning ? "Run" : "Walk", 0.2f);
            }
            else
            {
                if(InCombat&&currentState=="CombatIdle"&&Progress("CombatIdle")<.96f)return;
                if(InCombat&&!playedCombatFidget&&Time.time-lastActionTime>65f)
                {playedCombatFidget=true;ChangeState("CombatIdle",.3f);return;}
                ChangeState(InCombat&&animator.HasState(0,Animator.StringToHash("Base Layer.CombatReady"))?"CombatReady":"Idle",.25f);
            }
        }

        private string NextPunch()
        {
            string result = secondPunch ? "Melee" : "Punch";
            secondPunch = !secondPunch;
            return result;
        }
        private void BeginAttack(string name)
        {
            EnterCombat(); blocking = false; attack = name; dealtHit = false;
            ChangeState(name, 0.12f, true);
        }
        private void ChangeState(string name, float duration, bool restart = false, float startTime = 0f)
        {
            if (!restart && currentState == name) return;
            currentState = name;
            animator.CrossFadeInFixedTime(Animator.StringToHash("Base Layer." + name), Mathf.Max(duration, 0.2f), 0, startTime);
        }
        private float Progress(string name)
        {
            int hash = Animator.StringToHash("Base Layer." + name);
            if (animator.IsInTransition(0))
            {
                var next = animator.GetNextAnimatorStateInfo(0);
                if (next.fullPathHash == hash) return next.normalizedTime;
            }
            var state = animator.GetCurrentAnimatorStateInfo(0);
            return state.fullPathHash == hash ? state.normalizedTime : 0f;
        }
        private void HitTarget(float damage)
        {
            DamageableTarget closest = null;
            float nearest = 2.4f;
            foreach (var collider in Physics.OverlapSphere(transform.position + Vector3.up, 2.4f))
            {
                var dummy = collider.GetComponentInParent<DamageableTarget>();
                if (dummy == null || dummy.IsDead || dummy.transform.IsChildOf(transform)) continue;
                Vector3 delta = dummy.transform.position - transform.position; delta.y = 0f;
                if (delta.magnitude < nearest && Vector3.Dot(transform.forward, delta.normalized) > 0.45f)
                { closest = dummy; nearest = delta.magnitude; }
            }
            if (closest == null) return;
            Vector3 origin = transform.position + Vector3.up;
            Vector3 toTarget = closest.HitPoint - origin;
            var hits = Physics.RaycastAll(origin, toTarget.normalized, toTarget.magnitude + 0.2f);
            System.Array.Sort(hits, (a,b) => a.distance.CompareTo(b.distance));
            foreach (var hit in hits)
            {
                if (hit.transform.IsChildOf(transform)) continue;
                if (hit.transform.GetComponentInParent<DamageableTarget>() != closest) return;
                break;
            }
            closest.TakeDamage(damage);
        }
        public void TakeDamage(float amount, Vector3 attackerPosition)
        {
            if (!ready || IsDead || amount <= 0f) return;
            Vector3 direction = attackerPosition - transform.position; direction.y = 0f;
            bool guarded = blocking && Vector3.Dot(transform.forward, direction.normalized) > 0.25f;
            health = Mathf.Max(0f, health - (guarded ? amount * 0.1f : amount));
            EnterCombat();
            Debug.Log(guarded ? "Blocked! Health: " + health : "Hit! Health: " + health);
            if (health <= 0f)
            {
                attack = null; queuedAttack = null; blocking = false;
                // Skip the long standing lead-in, retaining the first loss of balance.
                // Blend out of the current pose over 0.3 seconds instead of snapping
                // into the fall in only a couple of frames.
                ChangeState("Death", 0.3f, true, 1.95f);
                Debug.Log("Goblin down. Press R to restart the combat test.");
            }
        }
        private void ResetIdleTimer(){lastActionTime=Time.time;playedCombatFidget=false;}
        private void EnterCombat(){InCombat=true;ResetIdleTimer();}
        private void LateUpdate()
        {
            if (!ready) return;

            // GoblinMovement owns the player's heading. Some imported clips animate
            // the model root, which can make the mesh yaw independently of the
            // CharacterController and look like it is orbiting around a stray pivot.
            // Keep the visual root at its authored local orientation; the player
            // transform supplies the world-facing direction.
            animator.transform.localPosition = baseModelPosition;
            animator.transform.localRotation = baseModelRotation;
        }
    }
}
