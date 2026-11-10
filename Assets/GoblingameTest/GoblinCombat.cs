using UnityEngine;
using UnityEngine.InputSystem;

namespace Goblingame.TestScene
{
    public enum HitResult { Ignored, Hit, Blocked, Parried }

    /// <summary>
    /// Goblin melee. Everything is driven by our own clip clock instead of polling
    /// Animator normalized time, so an interrupted blend can never leave the goblin
    /// stuck in an attack.
    ///
    /// punching_combat contains four punches (L-R-L-R). They are played as a combo:
    /// each click continues the same clip into the next punch; with no click the
    /// goblin blends back to its stance after the current punch.
    /// </summary>
    [DefaultExecutionOrder(100)]
    public sealed class GoblinCombat : MonoBehaviour
    {
        [SerializeField] private Animator animator;
        [SerializeField] private RuntimeAnimatorController fallbackController;
        [SerializeField] private float maxHealth = 100f;

        [Header("Idle gesture (head shake + pointing at the wrist)")]
        [Tooltip("Seconds of calm idle between two gestures (random in this range).")]
        [SerializeField] private float gestureIntervalMin = 9f;
        [SerializeField] private float gestureIntervalMax = 16f;
        [Tooltip("Allow the gesture while enemies are around.")]
        [SerializeField] private bool gestureInCombat = false;

        [Header("Attacks")]
        [SerializeField] private float punchDamage = 12f;
        [SerializeField] private float finisherDamage = 22f;
        [SerializeField] private float headbuttDamage = 28f;
        [SerializeField] private float attackReach = 1.6f;
        [SerializeField] private float headbuttCooldown = 1.1f;

        [Header("Defence")]
        [SerializeField, Range(0f, 1f)] private float blockReduction = 0.85f;
        [SerializeField] private float parryWindow = 0.22f;
        [SerializeField] private float blockArc = 130f;

        [Header("Recovery")]
        [SerializeField] private float regenDelay = 6f;
        [SerializeField] private float regenPerSecond = 8f;

        private struct Move
        {
            public string state; public float start, impact, end, damage, stagger, knockback, arc; public bool heavy;
        }
        // Times are seconds inside the source clip (measured from the bone curves).
        private static readonly Move[] Combo =
        {
            new Move{state="Punch",start=0.30f,impact=0.60f,end=0.82f,stagger=.30f,knockback=.25f,arc=65f},
            new Move{state="Punch",start=0.82f,impact=0.97f,end=1.18f,stagger=.30f,knockback=.25f,arc=65f},
            new Move{state="Punch",start=1.18f,impact=1.32f,end=1.50f,stagger=.35f,knockback=.35f,arc=65f},
            new Move{state="Punch",start=1.50f,impact=1.66f,end=2.05f,stagger=.75f,knockback=1.2f,arc=80f,heavy=true},
        };
        private static readonly Move Headbutt =
            new Move{state="Headbutt",start=0.10f,impact=0.40f,end=0.95f,stagger=1.0f,knockback=1.5f,arc=75f,heavy=true};

        private static readonly int IdleTimeParam = Animator.StringToHash("IdleTime");
        private static readonly int BlockTimeParam = Animator.StringToHash("BlockTime");

        private GoblinMovement movement;
        private ThirdPersonFollow view;
        private string currentState;
        private bool ready, hasIdleTime, hasBlockTime;
        private float health;
        private Vector3 spawnPosition; private Quaternion spawnRotation;
        private Quaternion baseModelRotation; private Vector3 baseModelPosition;

        // attack state
        private bool attacking; private Move move; private int comboIndex;
        private float clipTime; private bool hitDone, queuedPunch, queuedHeadbutt;
        private float nextHeadbutt;
        private DamageableTarget attackTarget;

        // block
        private bool blocking; private float blockStart;
        // idle gesture
        private float idleLength = 3.2167f, blockLength = 3.75f;
        private float breathPhase, nextGesture, gestureClock = -1f;
        // feedback
        private float flinch, hitStopUntil; private bool hitStopActive;
        private float lastCombatTime = -99f, lastHurtTime = -99f;

        public bool InCombat { get; private set; }
        public bool IsDead => ready && health <= 0f;
        public bool IsBlocking => blocking;
        public bool IsAttacking => attacking;
        public bool MovementLocked => ready && (IsDead || blocking || attacking);
        public float Health => health;
        public float MaxHealth => maxHealth;
        public bool HasFacing { get; private set; }
        public Vector3 FacingDirection { get; private set; }

        private void Start()
        {
            movement = GetComponent<GoblinMovement>();
            if (Camera.main != null) view = Camera.main.GetComponent<ThirdPersonFollow>();
            if (animator == null) animator = GetComponentInChildren<Animator>();
            if (animator != null && animator.runtimeAnimatorController == null && fallbackController != null)
                animator.runtimeAnimatorController = fallbackController;
            if (animator != null && animator.runtimeAnimatorController != null) animator.Update(0f);
            ready = animator != null && animator.HasState(0, Animator.StringToHash("Base Layer.Punch"));
            health = maxHealth;
            spawnPosition = transform.position; spawnRotation = transform.rotation;
            ScheduleGesture();
            if (!ready) { Debug.LogWarning("GoblinCombat: animator has no Punch state; combat disabled."); return; }
            foreach (var p in animator.parameters)
            {
                if (p.nameHash == IdleTimeParam) hasIdleTime = true;
                if (p.nameHash == BlockTimeParam) hasBlockTime = true;
            }
            foreach (var clip in animator.runtimeAnimatorController.animationClips)
            {
                if (clip.name.Contains("funny_combat_waiting")) idleLength = clip.length;
                if (clip.name.Contains("block_combat")) blockLength = clip.length;
            }
            baseModelPosition = animator.transform.localPosition;
            baseModelRotation = animator.transform.localRotation;
            animator.Play(Animator.StringToHash("Base Layer.Idle"), 0, 0f); animator.Update(0f);
            animator.transform.localRotation = baseModelRotation;
            currentState = "Idle";
        }

        private void Update()
        {
            if (!ready) return;
            UpdateHitStop();
            animator.speed = 1f;
            float dt = Time.deltaTime;
            var keyboard = Keyboard.current; var mouse = Mouse.current;
            bool focused = Application.isFocused;
            InCombat = Time.time - lastCombatTime < 5f || EnemyNearby(8f);
            HasFacing = false;

            if (IsDead)
            {
                if (focused && keyboard != null && keyboard.rKey.wasPressedThisFrame) Respawn();
                return;
            }

            // out-of-combat regeneration
            if (!InCombat && Time.time - lastHurtTime > regenDelay && health < maxHealth)
                health = Mathf.Min(maxHealth, health + regenPerSecond * dt);

            bool cursorFree = Cursor.lockState != CursorLockMode.Locked && Cursor.visible;
            bool wantsBlock = focused && !cursorFree && mouse != null && mouse.rightButton.isPressed;
            bool punch = focused && !cursorFree && mouse != null && mouse.leftButton.wasPressedThisFrame;
            bool headbutt = focused && keyboard != null && keyboard.qKey.wasPressedThisFrame;
            bool moveInput = focused && keyboard != null &&
                (keyboard.wKey.isPressed || keyboard.aKey.isPressed || keyboard.sKey.isPressed || keyboard.dKey.isPressed);

            if (attacking)
            {
                clipTime += dt;
                if (punch) queuedPunch = true;
                if (headbutt && Time.time >= nextHeadbutt) queuedHeadbutt = true;
                if (clipTime < move.impact) FaceAttackTarget();
                if (!hitDone && clipTime >= move.impact) { hitDone = true; DealHit(move); }
                // block can cancel the wind-up; movement can cancel the follow-through
                if (wantsBlock && clipTime < move.impact - 0.05f) { attacking = false; }
                else if (moveInput && hitDone && clipTime > move.impact + 0.18f && !queuedPunch && !queuedHeadbutt) { EndAttack(0.2f); }
                else if (clipTime >= move.end)
                {
                    if (queuedHeadbutt) { StartHeadbutt(); return; }
                    if (queuedPunch && move.state == "Punch" && comboIndex < Combo.Length - 1) { NextPunch(); return; }
                    EndAttack(0.22f);
                }
                if (attacking) return;
            }

            if (wantsBlock)
            {
                if (!blocking) { blocking = true; blockStart = Time.time; ChangeState("Block", 0.1f, true, hasBlockTime ? 0f : 1.2f); }
                lastCombatTime = Time.time;
                FaceAttackTarget(true);
                DriveBlock(Time.time - blockStart);
                return;
            }
            if (blocking) { blocking = false; ChangeState("Idle", 0.2f); }

            if (headbutt && Time.time >= nextHeadbutt) { StartHeadbutt(); return; }
            if (punch) { comboIndex = -1; NextPunch(); return; }

            if (movement != null && movement.IsMoving)
            {
                gestureClock = -1f; ScheduleGesture();
                ChangeState(movement.IsRunning ? "Run" : "Walk", 0.2f);
            }
            else
            {
                ChangeState("Idle", 0.25f);
                if (view != null && view.IsLocked) { HasFacing = true; FacingDirection = Flat(view.LockTarget.transform.position - transform.position); }
                DriveIdle(dt);
            }
        }

        // ───────────────────────── idle ─────────────────────────
        private void ScheduleGesture() { nextGesture = Time.time + Random.Range(gestureIntervalMin, Mathf.Max(gestureIntervalMin, gestureIntervalMax)); }

        private void DriveIdle(float dt)
        {
            if (!hasIdleTime) return;   // legacy controller: the clip simply loops
            float seconds;
            if (gestureClock >= 0f)
            {
                gestureClock += dt;
                seconds = gestureClock;
                if (gestureClock >= idleLength) { gestureClock = -1f; breathPhase = 0f; ScheduleGesture(); seconds = 0f; }
            }
            else
            {
                // calm stance: gently ping-pong through the quiet first part of the clip (breathing)
                breathPhase += dt * Mathf.PI * 2f / 3.4f;
                seconds = 0.02f + 0.24f * (0.5f - 0.5f * Mathf.Cos(breathPhase));
                bool allowed = gestureInCombat || !InCombat;
                if (allowed && Time.time >= nextGesture) gestureClock = seconds;
                else if (!allowed) ScheduleGesture();
            }
            animator.SetFloat(IdleTimeParam, Mathf.Repeat(seconds / idleLength, 0.9999f));
        }

        private void DriveBlock(float held)
        {
            if (hasBlockTime)
            {
                // raise the guard (1.0s → 1.4s of the clip), then hold it with a small sway
                float seconds = held < 0.16f ? Mathf.Lerp(1.0f, 1.4f, held / 0.16f)
                    : 1.44f + 0.05f * Mathf.Sin((held - 0.16f) * 4f);
                animator.SetFloat(BlockTimeParam, seconds / blockLength);
            }
            else if (held > 0.6f && Mathf.Repeat(held, 0.6f) < Time.deltaTime)
                ChangeState("Block", 0.15f, true, 1.25f);
        }

        // ───────────────────────── attacks ─────────────────────────
        private void NextPunch()
        {
            comboIndex++;
            bool fresh = !attacking || move.state != "Punch" || comboIndex == 0;
            move = Combo[comboIndex];
            if (fresh)
            {
                clipTime = move.start;
                ChangeState("Punch", 0.08f, true, move.start);
            }
            BeginMove();
        }

        private void StartHeadbutt()
        {
            nextHeadbutt = Time.time + headbuttCooldown;
            move = Headbutt; comboIndex = -1; clipTime = move.start;
            ChangeState("Headbutt", 0.08f, true, move.start);
            BeginMove();
        }

        private void BeginMove()
        {
            attacking = true; hitDone = false; queuedPunch = false; queuedHeadbutt = false; blocking = false;
            gestureClock = -1f; lastCombatTime = Time.time;
            attackTarget = PickTarget();
            FaceAttackTarget();
            if (attackTarget != null && movement != null)
            {
                Vector3 to = Flat(attackTarget.transform.position - transform.position);
                float gap = to.magnitude - attackTarget.Radius - attackReach * 0.7f;
                if (gap > 0.05f) movement.Push(to.normalized * Mathf.Sqrt(2f * 18f * Mathf.Min(gap, 1.1f)));
            }
        }

        private void EndAttack(float blend)
        {
            attacking = false; comboIndex = -1; queuedPunch = queuedHeadbutt = false;
            ChangeState("Idle", blend, true, 0f);
        }

        private void FaceAttackTarget(bool preferView = false)
        {
            DamageableTarget t = attackTarget;
            if (t == null || t.IsDead || preferView) t = PickTarget();
            if (t == null) return;
            HasFacing = true; FacingDirection = Flat(t.transform.position - transform.position);
        }

        private DamageableTarget PickTarget()
        {
            if (view != null && view.IsLocked && view.LockTarget.Health != null && !view.LockTarget.Health.IsDead &&
                Flat(view.LockTarget.transform.position - transform.position).magnitude < 4.5f)
                return view.LockTarget.Health;
            DamageableTarget best = null; float bestScore = float.MaxValue;
            Vector3 fwd = view != null ? view.PlanarForward : transform.forward;
            foreach (var t in DamageableTarget.All)
            {
                if (t == null || t.IsDead || t.transform.IsChildOf(transform)) continue;
                Vector3 d = Flat(t.transform.position - transform.position);
                float dist = d.magnitude - t.Radius;
                if (dist > 3.2f) continue;
                float angle = Vector3.Angle(fwd, d);
                if (angle > 110f && dist > 1.2f) continue;
                float score = dist + angle * 0.02f;
                if (score < bestScore) { bestScore = score; best = t; }
            }
            return best;
        }

        private void DealHit(Move m)
        {
            float damage = m.state == "Headbutt" ? headbuttDamage : m.heavy ? finisherDamage : punchDamage;
            bool any = false;
            DamageableTarget single = null; float singleDist = float.MaxValue;
            foreach (var t in DamageableTarget.All)
            {
                if (t == null || t.IsDead || t.transform.IsChildOf(transform)) continue;
                Vector3 d = Flat(t.transform.position - transform.position);
                float dist = d.magnitude - t.Radius;
                if (dist > attackReach || Vector3.Angle(transform.forward, d) > m.arc) continue;
                if (m.heavy) { Apply(t, m, damage); any = true; }
                else
                {
                    float score = t == attackTarget ? -1f : dist;
                    if (score < singleDist) { singleDist = score; single = t; }
                }
            }
            if (single != null) { Apply(single, m, damage); any = true; }
            if (any)
            {
                lastCombatTime = Time.time;
                if (view != null) view.Shake(m.heavy ? 1f : 0.45f);
                HitStop(m.heavy ? 0.09f : 0.045f);
            }
        }

        private void Apply(DamageableTarget t, Move m, float damage)
        {
            t.TakeHit(new HitInfo { damage = damage, source = transform.position, stagger = m.stagger, knockback = m.knockback, heavy = m.heavy });
        }

        // ───────────────────────── receiving ─────────────────────────
        public HitResult TakeDamage(float amount, Vector3 attackerPosition)
        {
            if (!ready || IsDead || amount <= 0f) return HitResult.Ignored;
            lastCombatTime = lastHurtTime = Time.time;
            Vector3 dir = Flat(attackerPosition - transform.position);
            bool frontal = dir.sqrMagnitude < 0.001f || Vector3.Angle(transform.forward, dir) < blockArc * 0.5f;
            Vector3 chest = transform.position + Vector3.up * 1.6f;
            if (blocking && frontal)
            {
                if (Time.time - blockStart <= parryWindow)
                {
                    CombatEvents.Raise(chest, 0f, CombatEvents.Kind.Parried, true);
                    if (view != null) view.Shake(0.6f);
                    HitStop(0.08f);
                    return HitResult.Parried;
                }
                float chip = amount * (1f - blockReduction);
                health = Mathf.Max(0f, health - chip);
                CombatEvents.Raise(chest, chip, CombatEvents.Kind.Blocked, true);
                if (movement != null) movement.Push(-dir.normalized * 3f);
                if (view != null) view.Shake(0.35f);
                if (health <= 0f) Die();
                return HitResult.Blocked;
            }
            health = Mathf.Max(0f, health - amount);
            CombatEvents.Raise(chest, amount, CombatEvents.Kind.Damage, true);
            flinch = 1f;
            if (view != null) view.Shake(0.8f);
            if (movement != null) movement.Push(-dir.normalized * 4f);
            // light attacks are interrupted; finisher / headbutt have armour after start-up
            if (attacking && !(move.heavy && clipTime > move.start + 0.12f)) EndAttack(0.12f);
            if (health <= 0f) Die();
            return HitResult.Hit;
        }

        private void Die()
        {
            attacking = false; blocking = false; queuedPunch = queuedHeadbutt = false;
            ChangeState("Death", 0.15f, true, 0.95f);
            Debug.Log("Goblin down. Press R to get up.");
        }

        private void Respawn()
        {
            var cc = GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            transform.SetPositionAndRotation(spawnPosition, spawnRotation);
            if (cc != null) cc.enabled = true;
            health = maxHealth; lastCombatTime = -99f;
            ScheduleGesture();
            ChangeState("Idle", 0.25f, true);
        }

        // ───────────────────────── helpers ─────────────────────────
        private void ChangeState(string name, float duration, bool restart = false, float startTime = 0f)
        {
            if (!restart && currentState == name) return;
            int hash = Animator.StringToHash("Base Layer." + name);
            if (!animator.HasState(0, hash)) return;
            currentState = name;
            animator.CrossFadeInFixedTime(hash, duration, 0, startTime);
        }

        private bool EnemyNearby(float radius)
        {
            foreach (var e in EnemyActor.All)
                if (e != null && e.IsEngaged && Flat(e.transform.position - transform.position).magnitude < radius) return true;
            return false;
        }

        private void HitStop(float seconds) { hitStopUntil = Mathf.Max(hitStopUntil, Time.unscaledTime + seconds); }
        private void UpdateHitStop()
        {
            if (Time.unscaledTime < hitStopUntil) { Time.timeScale = 0.05f; hitStopActive = true; }
            else if (hitStopActive) { Time.timeScale = 1f; hitStopActive = false; }
        }
        private void OnDisable() { if (hitStopActive) { Time.timeScale = 1f; hitStopActive = false; } }

        private static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

        private void LateUpdate()
        {
            if (!ready) return;
            // GoblinMovement owns the heading; keep the visual root at its authored pose,
            // plus a short procedural flinch when the goblin is hit.
            animator.transform.localPosition = baseModelPosition;
            animator.transform.localRotation = baseModelRotation;
            if (flinch > 0f)
            {
                float angle = Mathf.Sin(flinch * Mathf.PI) * 11f;
                animator.transform.rotation = Quaternion.AngleAxis(-angle, transform.right) * animator.transform.rotation;
                flinch = Mathf.MoveTowards(flinch, 0f, Time.deltaTime * 4f);
            }
        }
    }
}
