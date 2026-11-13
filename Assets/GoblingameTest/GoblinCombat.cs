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
            public string state; public float start, impact, end, damage, stagger, knockback, arc, speed; public bool heavy;
        }
        // Times are seconds inside the source clip (measured from the bone curves).
        private static readonly Move[] Combo =
        {
            // quick jabs play a little faster than the authored clip, the finisher keeps its weight
            new Move{state="Punch",start=0.34f,impact=0.60f,end=0.82f,stagger=.30f,knockback=.25f,arc=65f,speed=1.25f},
            new Move{state="Punch",start=0.82f,impact=0.97f,end=1.18f,stagger=.30f,knockback=.25f,arc=65f,speed=1.25f},
            new Move{state="Punch",start=1.18f,impact=1.32f,end=1.50f,stagger=.35f,knockback=.35f,arc=65f,speed=1.2f},
            new Move{state="Punch",start=1.50f,impact=1.66f,end=2.00f,stagger=.75f,knockback=1.2f,arc=80f,heavy=true,speed=1.0f},
        };
        private static readonly Move Headbutt =
            new Move{state="Headbutt",start=0.12f,impact=0.40f,end=0.90f,stagger=1.0f,knockback=1.5f,arc=75f,heavy=true,speed=1.15f};

        private static readonly int IdleTimeParam = Animator.StringToHash("IdleTime");
        private static readonly int BlockTimeParam = Animator.StringToHash("BlockTime");
        private static readonly int MoveSpeedParam = Animator.StringToHash("MoveSpeed");
        private static readonly int AttackSpeedParam = Animator.StringToHash("AttackSpeed");
        private bool hasMoveSpeed, hasAttackSpeed;
        private float bank, pitchLean;
        // procedural jump pose (the goblin has no jump clip)
        private Transform thighL, thighR, shinL, shinR, upperArmL, upperArmR, foreArmL, foreArmR, spine;
        private float airPose, landPose, takeoffPose, reachPose, divePose;
        [Header("Jump")]
        [Tooltip("Run-clip moment (s) used as the in-air base pose: one leg forward, one behind.")]
        [SerializeField] private float airStrideTime = 0.27f;
        [SerializeField] private float diveDamage = 24f, diveRadius = 2.6f;
        private bool diving; private float recoverUntil;
        // combo flow
        private int lastComboIndex = -1; private float lastComboEnd = -9f;
        private bool dashAttack; private Vector3 steerDir;
        // dodge (R): slide when sprinting, roll otherwise
        private enum Dodge { None, Roll, Slide }
        private Dodge dodge; private float dodgeStart, dodgeDuration, nextDodge, dizzyUntil;
        private ParticleSystem dust;
        public bool IsDodging => dodge != Dodge.None;
        private bool dodgeRequested;
        /// <summary>Same as pressing R (used by the automated playtest).</summary>
        public void RequestDodge() { dodgeRequested = true; }
        private Vector3 baseModelScale;

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
        public bool MovementLocked => ready && (IsDead || blocking || attacking || diving || dodge != Dodge.None || Time.time < recoverUntil);
        public bool IsDiving => diving;
        public float Health => health;
        public float MaxHealth => maxHealth;
        /// <summary>Restores health (campfire, chest); returns how much was actually healed.</summary>
        public float Heal(float amount)
        {
            if (!ready || IsDead || amount <= 0f) return 0f;
            float before = health; health = Mathf.Min(maxHealth, health + amount); return health - before;
        }
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
                if (p.nameHash == MoveSpeedParam) hasMoveSpeed = true;
                if (p.nameHash == AttackSpeedParam) hasAttackSpeed = true;
            }
            foreach (var clip in animator.runtimeAnimatorController.animationClips)
            {
                if (clip.name.Contains("funny_combat_waiting")) idleLength = clip.length;
                if (clip.name.Contains("block_combat")) blockLength = clip.length;
            }
            baseModelPosition = animator.transform.localPosition;
            baseModelRotation = animator.transform.localRotation;
            baseModelScale = animator.transform.localScale;
            FindBones();
            BuildDust();
            if (movement != null) { movement.Landed += OnLanded; movement.Jumped += OnJumped; }
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
            // feet follow the body: playback speed tracks the real speed while accelerating / stopping
            if (hasMoveSpeed && movement != null)
                animator.SetFloat(MoveSpeedParam, Mathf.Clamp(movement.SpeedRatio, 0.55f, 1.2f));

            if (dodge != Dodge.None && hasMoveSpeed) animator.SetFloat(MoveSpeedParam, 0f);   // frozen base pose under the dodge
            if (IsDead)
            {
                if (focused && keyboard != null && keyboard.rKey.wasPressedThisFrame) Respawn();
                return;
            }

            bool cursorFree = Cursor.lockState != CursorLockMode.Locked && Cursor.visible;
            bool wantsBlock = focused && !cursorFree && mouse != null && mouse.rightButton.isPressed;
            bool punch = focused && !cursorFree && mouse != null && mouse.leftButton.wasPressedThisFrame;

            // in the air: frozen mid-stride run pose as the base, bones are posed in LateUpdate.
            // LMB in the air = plunging slam.
            if (movement != null && (movement.IsAirborne || diving) && !attacking)
            {
                if (blocking) blocking = false;
                gestureClock = -1f; ScheduleGesture();
                if (punch && !diving && movement.AirTime > 0.08f) { diving = true; movement.Dive(16f); lastCombatTime = Time.time; }
                if (hasMoveSpeed) { if (currentState != "Run") ChangeState("Run", 0.14f, true, airStrideTime); animator.SetFloat(MoveSpeedParam, 0f); }
                else ChangeState("Idle", 0.12f);
                return;
            }
            if (Time.time < recoverUntil) return;   // getting up from the slam

            // ── dodge: R while sprinting = slide, otherwise a (silly) forward roll. Can cancel the end of an attack.
            if (dodge != Dodge.None)
            {
                if (movement == null || !movement.IsDashing) { dodge = Dodge.None; ChangeState("Idle", 0.15f, true); }
                else
                {
                    if (dodge == Dodge.Slide && dust != null && Random.value < 0.6f) dust.Emit(2);
                    return;
                }
            }
            bool dodgePressed = dodgeRequested || (focused && keyboard != null && keyboard.rKey.wasPressedThisFrame);
            dodgeRequested = false;
            if (dodgePressed && Time.time >= nextDodge && movement != null && !movement.IsAirborne && (!attacking || hitDone) && !diving)
            {
                if (attacking) EndAttack(0.05f);
                blocking = false;
                bool sprinting = movement.IsRunning && movement.CurrentSpeed > 4f;
                Vector3 dir = InputDirection(); if (dir.sqrMagnitude < 0.01f) dir = transform.forward;
                dodge = sprinting ? Dodge.Slide : Dodge.Roll;
                dodgeDuration = sprinting ? 0.78f : 0.6f;
                dodgeStart = Time.time; nextDodge = Time.time + dodgeDuration + 0.25f;
                if (sprinting) { movement.Dash(dir, 9.5f, 2.5f, dodgeDuration); if (hasMoveSpeed) ChangeState("Run", 0.08f, true, airStrideTime); }
                else { movement.Dash(dir, 7.5f, 2.0f, dodgeDuration); ChangeState("Idle", 0.06f, true); dizzyUntil = Time.time + dodgeDuration + 0.45f; }
                if (view != null) view.Shake(0.2f);
                return;
            }

            // out-of-combat regeneration
            if (!InCombat && Time.time - lastHurtTime > regenDelay && health < maxHealth)
                health = Mathf.Min(maxHealth, health + regenPerSecond * dt);

            bool headbutt = focused && keyboard != null && keyboard.qKey.wasPressedThisFrame;
            bool moveInput = focused && keyboard != null &&
                (keyboard.wKey.isPressed || keyboard.aKey.isPressed || keyboard.sKey.isPressed || keyboard.dKey.isPressed);

            if (attacking)
            {
                clipTime += dt * (hasAttackSpeed ? MoveSpeedFor(move) : 1f);
                if (punch) queuedPunch = true;
                if (headbutt && Time.time >= nextHeadbutt) queuedHeadbutt = true;
                if (clipTime < move.impact) FaceAttackTarget();
                if (!hitDone && clipTime >= move.impact) { hitDone = true; DealHit(move); }
                // block can cancel the wind-up; movement can cancel the follow-through
                if (wantsBlock && clipTime < move.impact - 0.05f) { attacking = false; }
                else if (moveInput && hitDone && clipTime > move.impact + 0.1f && !queuedPunch && !queuedHeadbutt) { EndAttack(0.15f); }
                else if (clipTime >= move.end)
                {
                    if (queuedHeadbutt) { StartHeadbutt(); return; }
                    if (queuedPunch && move.state == "Punch" && comboIndex < Combo.Length - 1) { NextPunch(); return; }
                    if (queuedPunch && move.state == "Headbutt") { comboIndex = 1; NextPunch(); return; }   // headbutt → right-left punches
                    EndAttack(0.18f);
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
            if (punch)
            {
                // a slightly late click still continues the combo
                comboIndex = Time.time - lastComboEnd < 0.22f && lastComboIndex >= 0 && lastComboIndex < Combo.Length - 1 ? lastComboIndex : -1;
                // attacking out of a sprint: dashing straight punch
                if (comboIndex == -1 && movement != null && movement.IsRunning && movement.CurrentSpeed > 4f) { comboIndex = 0; dashAttack = true; }
                NextPunch(); return;
            }

            if (movement != null && movement.IsMoving)
            {
                gestureClock = -1f; ScheduleGesture();
                // idle → walk is quick (responsive), walk ↔ run blends a little longer
                bool fromLocomotion = currentState == "Walk" || currentState == "Run";
                ChangeState(movement.IsRunning ? "Run" : "Walk", fromLocomotion ? 0.3f : 0.18f);
            }
            else
            {
                ChangeState("Idle", currentState == "Run" ? 0.35f : 0.28f);
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
                ChangeState("Punch", 0.05f, true, move.start);
            }
            BeginMove();
        }

        private void StartHeadbutt()
        {
            nextHeadbutt = Time.time + headbuttCooldown;
            move = Headbutt; comboIndex = -1; clipTime = move.start;
            ChangeState("Headbutt", 0.05f, true, move.start);
            BeginMove();
        }

        private void BeginMove()
        {
            attacking = true; hitDone = false; queuedPunch = false; queuedHeadbutt = false; blocking = false;
            if (hasAttackSpeed) animator.SetFloat(AttackSpeedParam, MoveSpeedFor(move));
            gestureClock = -1f; lastCombatTime = Time.time;
            attackTarget = PickTarget();
            steerDir = attackTarget == null ? InputDirection() : Vector3.zero;   // no target: punch where the player steers
            FaceAttackTarget();
            if (attackTarget != null && movement != null)
            {
                // combo magnetism: every hit slides in to the ideal distance
                Vector3 to = Flat(attackTarget.transform.position - transform.position);
                float gap = to.magnitude - attackTarget.Radius - attackReach * 0.7f;
                if (gap > 0.05f) movement.Push(to.normalized * Mathf.Sqrt(2f * 18f * Mathf.Min(gap, dashAttack ? 2.2f : 1.1f)));
            }
            else if (dashAttack && movement != null) movement.Push(transform.forward * 8f);
        }

        // the weapon in the hand changes how hard and how fast the punches land (the headbutt stays a headbutt)
        private GoblinInventory inventory; private bool inventoryLooked;
        public ItemDef EquippedWeapon
        {
            get
            {
                if (!inventoryLooked || inventory == null) { inventory = GetComponent<GoblinInventory>(); inventoryLooked = true; }
                return inventory != null ? inventory.Weapon : Items.Fists;
            }
        }
        // ── weapon swings: with something in the hand the right arm slashes instead of jabbing.
        // Poses are (raise about the goblin's right axis, sweep about up, elbow bend, shoulder twist);
        // each combo hit has its own line: diagonal, backhand, rising, and an overhead chop to finish.
        private struct SwingPose { public float raise, sweep, elbow, twist; public SwingPose(float r, float s, float e, float t) { raise = r; sweep = s; elbow = e; twist = t; } }
        private static readonly SwingPose[][] Swings =
        {
            new[] { new SwingPose(-150f,  55f, -70f,  25f), new SwingPose(-35f, -50f, -5f, -22f), new SwingPose(-15f, -70f, -15f, -28f) },   // diagonal slash
            new[] { new SwingPose(-85f,  -75f, -60f, -20f), new SwingPose(-80f,  60f, -5f,  25f), new SwingPose(-70f,  75f, -20f,  30f) },   // backhand sweep
            new[] { new SwingPose(-10f,   35f, -40f,  15f), new SwingPose(-160f, -25f, -10f, -15f), new SwingPose(-170f, -35f, -25f, -18f) }, // rising cut
            new[] { new SwingPose(-175f,  10f, -85f,   5f), new SwingPose(-15f,   0f,  0f,   0f), new SwingPose(-5f,    0f, -10f,   0f) },   // overhead chop
        };
        // ── stances layered over the idle clip (which is a raised-fists fighting pose):
        // out of combat the arms hang loose; in combat with a weapon the weapon arm moves out and forward

        private float relaxUpper = 58f, relaxOut = 10f, relaxElbow = 75f, relaxLean = -4f;
        private float guardRaise = 14f, guardOut = 22f, guardElbow = 62f;
        private float relaxWeight, guardWeight;
        /// <summary>0..1, how much the goblin is in the loose, out-of-combat stance.</summary>
        public float RelaxWeight => relaxWeight;
        /// <summary>0..1, how much the armed fighting guard is applied.</summary>
        public float GuardWeight => guardWeight;
        /// <summary>For tests: force the out-of-combat look (null = automatic).</summary>
        public bool? ForceRelaxed { get; set; }

        private void ApplyStance(float dt)
        {
            bool idle = !IsDead && !attacking && !blocking && dodge == Dodge.None && !diving && gestureClock < 0f &&
                        (movement == null || !movement.IsAirborne) && Time.time >= recoverUntil;
            bool calm = ForceRelaxed ?? !InCombat;
            float still = movement != null ? 1f - Mathf.Clamp01(movement.CurrentSpeed / 1.2f) : 1f;
            relaxWeight = Mathf.MoveTowards(relaxWeight, idle && calm ? still : 0f, dt * (idle && calm ? 1.8f : 6f));
            guardWeight = Mathf.MoveTowards(guardWeight, idle && !calm && EquippedWeapon != Items.Fists ? still : 0f, dt * 6f);
            Vector3 right = transform.right, fwd = transform.forward;
            float r = Mathf.SmoothStep(0f, 1f, relaxWeight), g = Mathf.SmoothStep(0f, 1f, guardWeight);
            if (r > 0f)
            {
                Turn(spine, relaxLean * r, right);
                Turn(upperArmL, relaxUpper * r, right); Turn(upperArmR, relaxUpper * r, right);
                Turn(upperArmL, -relaxOut * r, fwd); Turn(upperArmR, relaxOut * r, fwd);
                Turn(foreArmL, relaxElbow * r, right); Turn(foreArmR, relaxElbow * r, right);
            }
            if (g > 0f)
            {
                Turn(upperArmR, guardRaise * g, right); Turn(upperArmR, guardOut * g, fwd);
                Turn(foreArmR, guardElbow * g, right);
            }
        }

        private float swingWeight;
        private SwingPose swingNow;

        private void ApplyWeaponSwing(float dt)
        {
            bool armed = EquippedWeapon != Items.Fists;
            bool swinging = armed && attacking && move.state == "Punch" && comboIndex >= 0 && !IsDead;
            swingWeight = Mathf.MoveTowards(swingWeight, swinging ? 1f : 0f, dt * (swinging ? 14f : 5f));
            if (swingWeight <= 0f || upperArmR == null) return;
            if (swinging)
            {
                var set = Swings[Mathf.Clamp(comboIndex, 0, Swings.Length - 1)];
                float u = Mathf.InverseLerp(move.start, move.impact, clipTime), v = Mathf.InverseLerp(move.impact, move.end, clipTime);
                float wind = Mathf.SmoothStep(0f, 1f, u / .55f), strike = Mathf.Clamp01((u - .55f) / .45f); strike *= strike;   // slow lift, snapping strike
                SwingPose a = Lerp(new SwingPose(-20f, 0f, -30f, 0f), set[0], wind);
                a = Lerp(a, set[1], strike);
                a = Lerp(a, set[2], Mathf.SmoothStep(0f, 1f, v));
                swingNow = a;
            }
            var p = swingNow; float w = swingWeight;
            Vector3 right = transform.right, up = Vector3.up;
            Turn(spine, p.twist * .6f * w, up);
            Turn(upperArmR, p.raise * w, right);
            Turn(upperArmR, p.sweep * w, up);
            Turn(foreArmR, p.elbow * w, right);
        }
        private static SwingPose Lerp(SwingPose a, SwingPose b, float t) =>
            new SwingPose(Mathf.Lerp(a.raise, b.raise, t), Mathf.Lerp(a.sweep, b.sweep, t), Mathf.Lerp(a.elbow, b.elbow, t), Mathf.Lerp(a.twist, b.twist, t));

        /// <summary>For tests: the current arm swing (raise angle) and how strongly it is applied.</summary>
        public float SwingRaise => swingNow.raise * swingWeight;

        private float MoveSpeedFor(Move m) => m.state == "Headbutt" ? m.speed : m.speed * EquippedWeapon.speed;

        private void EndAttack(float blend)
        {
            if (move.state == "Punch") { lastComboIndex = comboIndex; lastComboEnd = Time.time; }
            attacking = false; comboIndex = -1; queuedPunch = queuedHeadbutt = false; dashAttack = false;
            ChangeState("Idle", blend, true, 0f);
        }

        private void FaceAttackTarget(bool preferView = false)
        {
            DamageableTarget t = attackTarget;
            if (t == null || t.IsDead || preferView) t = PickTarget();
            if (t == null) { if (steerDir.sqrMagnitude > 0.01f && !preferView) { HasFacing = true; FacingDirection = steerDir; } return; }
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
            if (dashAttack) { damage *= 1.35f; dashAttack = false; }
            var weapon = EquippedWeapon;
            if (m.state != "Headbutt") damage *= weapon.damage;
            float reach = attackReach + (m.state != "Headbutt" ? weapon.reach : 0f);
            bool any = false;
            DamageableTarget single = null; float singleDist = float.MaxValue;
            foreach (var t in DamageableTarget.All)
            {
                if (t == null || t.IsDead || t.transform.IsChildOf(transform)) continue;
                Vector3 d = Flat(t.transform.position - transform.position);
                float dist = d.magnitude - t.Radius;
                if (dist > reach || Vector3.Angle(transform.forward, d) > m.arc) continue;
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
            if (dodge != Dodge.None)
            {
                // invulnerable through the middle of the roll / slide
                float t = (Time.time - dodgeStart) / Mathf.Max(0.01f, dodgeDuration);
                if (t > 0.05f && t < 0.8f) { CombatEvents.Raise(transform.position + Vector3.up * 1.6f, 0f, CombatEvents.Kind.Dodged, true); return HitResult.Ignored; }
            }
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
        private void OnDestroy() { if (movement != null) { movement.Landed -= OnLanded; movement.Jumped -= OnJumped; } }
        private void OnJumped() { takeoffPose = 1f; }
        private void OnLanded(float fallSpeed)
        {
            landPose = Mathf.Clamp(fallSpeed / 9f, 0.3f, 1f);
            if (diving) { diving = false; landPose = 1.15f; recoverUntil = Time.time + 0.38f; DiveImpact(); }
            else if (view != null && fallSpeed > 9f) view.Shake(0.3f);
        }

        /// <summary>Air attack lands: everything around is knocked back and staggered.</summary>
        private void DiveImpact()
        {
            bool any = false;
            foreach (var t in DamageableTarget.All)
            {
                if (t == null || t.IsDead || t.transform.IsChildOf(transform)) continue;
                if (Flat(t.transform.position - transform.position).magnitude - t.Radius > diveRadius) continue;
                t.TakeHit(new HitInfo { damage = diveDamage, source = transform.position, stagger = 0.9f, knockback = 1.6f, heavy = true });
                any = true;
            }
            lastCombatTime = Time.time;
            if (view != null) view.Shake(any ? 1.2f : 0.7f);
            if (any) HitStop(0.1f);
            ChangeState("Idle", 0.12f, true);
        }

        private Vector3 InputDirection()
        {
            var kb = Keyboard.current; if (kb == null || !Application.isFocused) return Vector3.zero;
            Vector2 i = new Vector2((kb.dKey.isPressed ? 1 : 0) - (kb.aKey.isPressed ? 1 : 0), (kb.wKey.isPressed ? 1 : 0) - (kb.sKey.isPressed ? 1 : 0));
            if (i.sqrMagnitude < 0.01f) return Vector3.zero;
            Vector3 f = view != null ? view.PlanarForward : transform.forward; f.y = 0; f.Normalize();
            return (f * i.y + Vector3.Cross(Vector3.up, f) * i.x).normalized;
        }

        private void FindBones()
        {
            // only bones that actually deform the body count (Rigify also has control / ORG / MCH copies)
            var skinned = new System.Collections.Generic.HashSet<Transform>();
            foreach (var smr in animator.GetComponentsInChildren<SkinnedMeshRenderer>())
                if (smr.enabled && smr.gameObject.activeInHierarchy) foreach (var b in smr.bones) if (b != null) skinned.Add(b);
            foreach (var t in animator.GetComponentsInChildren<Transform>())
            {
                if (skinned.Count > 0 && !skinned.Contains(t)) continue;
                string n = System.Text.RegularExpressions.Regex.Replace(t.name.ToLowerInvariant(), "[^a-z0-9]", "");
                // Blender metarig names (thigh.L …) and Mixamo names (mixamorig:LeftUpLeg …)
                if (n.EndsWith("thighl") || n.EndsWith("leftupleg")) thighL = t; else if (n.EndsWith("thighr") || n.EndsWith("rightupleg")) thighR = t;
                else if (n.EndsWith("shinl") || n.EndsWith("leftleg")) shinL = t; else if (n.EndsWith("shinr") || n.EndsWith("rightleg")) shinR = t;
                else if (n.EndsWith("upperarml") || n.EndsWith("leftarm")) upperArmL = t; else if (n.EndsWith("upperarmr") || n.EndsWith("rightarm")) upperArmR = t;
                else if (n.EndsWith("forearml") || n.EndsWith("leftforearm")) foreArmL = t; else if (n.EndsWith("forearmr") || n.EndsWith("rightforearm")) foreArmR = t;
                else if (n.EndsWith("spine002") || n.EndsWith("spine2") || (spine == null && (n.EndsWith("spine001") || n.EndsWith("spine1")))) spine = t;
            }
            if (thighL == null || shinL == null)
            {
                var names = new System.Text.StringBuilder();
                foreach (var t in skinned) names.Append(t.name).Append(", ");
                Debug.LogWarning("GoblinCombat: leg bones not found — jump pose disabled. Skinned bones: " + names);
            }
            Debug.Log("[GoblinBones] skinned=" + skinned.Count + " thigh " + (thighL ? thighL.name : "-") + "/" + (thighR ? thighR.name : "-") + " shin " + (shinL ? shinL.name : "-") +
                      " arm " + (upperArmL ? upperArmL.name : "-") + " fore " + (foreArmL ? foreArmL.name : "-") + " spine " + (spine ? spine.name : "-"));
        }

        private static void Turn(Transform bone, float degrees, Vector3 axis) { if (bone != null && degrees != 0f) bone.rotation = Quaternion.AngleAxis(degrees, axis) * bone.rotation; }

        private void BuildDust()
        {
            var go = new GameObject("Slide Dust");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0f, 0.08f, -0.2f);
            go.transform.localRotation = Quaternion.Euler(-20f, 180f, 0f);
            dust = go.AddComponent<ParticleSystem>();
            dust.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = dust.main;
            main.playOnAwake = false; main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.45f, 0.8f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.6f, 2.0f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.18f, 0.42f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(.42f, .34f, .22f, .75f), new Color(.30f, .26f, .18f, .55f));
            main.gravityModifier = -0.05f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var emission = dust.emission; emission.rateOverTime = 0f;
            var shape = dust.shape; shape.shapeType = ParticleSystemShapeType.Cone; shape.angle = 35f; shape.radius = 0.15f;
            var col = dust.colorOverLifetime; col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                         new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
            col.color = grad;
            var size = dust.sizeOverLifetime; size.enabled = true; size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, .6f, 1, 1.6f));
            var r = go.GetComponent<ParticleSystemRenderer>();
            var shader = Shader.Find("Legacy Shaders/Particles/Alpha Blended") ?? Shader.Find("Sprites/Default");
            if (shader != null)
            {
                const int n = 32; var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
                for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + .5f, y + .5f), new Vector2(n / 2f, n / 2f)) / (n / 2f);
                    float a = Mathf.Clamp01(1f - d); tex.SetPixel(x, y, new Color(1f, 1f, 1f, a * a * (3f - 2f * a)));
                }
                tex.Apply();
                var m = new Material(shader) { mainTexture = tex };
                if (m.HasProperty("_TintColor")) m.SetColor("_TintColor", new Color(.5f, .5f, .5f, .5f));
                r.sharedMaterial = m;
            }
        }

        /// <summary>
        /// Roll: tuck into a ball and tumble once around the body centre with a little hop, then wobble dizzily.
        /// Slide: lean far back, lead leg straight out, trailing leg folded under, arms out for balance.
        /// </summary>
        private void ApplyDodgePose()
        {
            Vector3 right = transform.right, fwd = transform.forward;
            float now = Time.time;
            if (dodge == Dodge.Roll)
            {
                float t = Mathf.Clamp01((now - dodgeStart) / dodgeDuration);
                float tuck = Mathf.SmoothStep(0f, 1f, t / 0.14f) * (1f - Mathf.SmoothStep(0f, 1f, (t - 0.8f) / 0.2f));
                float spin = 360f * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((t - 0.06f) / 0.82f));
                Turn(spine, 48f * tuck, right);
                Turn(thighL, -100f * tuck, right); Turn(thighR, -95f * tuck, right);
                Turn(shinL, 125f * tuck, right); Turn(shinR, 120f * tuck, right);
                Turn(upperArmL, -65f * tuck, right); Turn(upperArmR, -65f * tuck, right);
                Turn(upperArmL, 15f * tuck, fwd); Turn(upperArmR, -15f * tuck, fwd);
                Turn(foreArmL, -95f * tuck, right); Turn(foreArmR, -95f * tuck, right);
                // tumble around the body centre, with a hop so the head clears the ground
                Vector3 pivot = transform.position + Vector3.up * 0.55f;
                var m = animator.transform;
                m.position = pivot + Quaternion.AngleAxis(spin, right) * (m.position - pivot) + Vector3.up * (0.22f * Mathf.Sin(t * Mathf.PI) - 0.18f * tuck);
                m.rotation = Quaternion.AngleAxis(spin, right) * m.rotation;
            }
            else if (dodge == Dodge.Slide)
            {
                float t = Mathf.Clamp01((now - dodgeStart) / dodgeDuration);
                float w = Mathf.SmoothStep(0f, 1f, t / 0.12f) * (1f - Mathf.SmoothStep(0f, 1f, (t - 0.8f) / 0.2f));
                Turn(thighL, -78f * w, right); Turn(shinL, -5f * w, right);       // lead leg straight out
                Turn(thighR, 18f * w, right); Turn(shinR, 115f * w, right);       // trailing leg folded under
                Turn(upperArmL, -55f * w, right); Turn(upperArmL, -25f * w, fwd); // one arm forward
                Turn(upperArmR, 35f * w, right); Turn(upperArmR, 55f * w, fwd);   // the other out behind
                Turn(spine, -12f * w, right);
                var m = animator.transform;
                m.rotation = Quaternion.AngleAxis(-48f * w, right) * m.rotation;   // lean back on the slide
                m.position += Vector3.down * 0.28f * w;
            }
            // after a roll the goblin is a bit dizzy for a moment
            if (dodge == Dodge.None && now < dizzyUntil)
            {
                float k = Mathf.Clamp01((dizzyUntil - now) / 0.45f);
                Turn(spine, Mathf.Sin(now * 20f) * 9f * k, fwd);
                Turn(spine, Mathf.Sin(now * 13f + 1f) * 5f * k, Vector3.up);
            }
        }


        private static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

        /// <summary>
        /// Layered jump animation on the goblin's bones (on top of a mid-stride run frame):
        /// launch → rise (knees pull up) → apex (tucked, arms out for balance) → fall (legs open toward
        /// the ground) → reach (legs stretch to meet the ground) → landing crouch scaled by the impact.
        /// A running jump keeps a stride (lead knee high, trail leg behind); a standing jump tucks both.
        /// </summary>
        private void ApplyJumpPose(float dt)
        {
            bool air = movement != null && movement.IsAirborne && !IsDead;
            airPose = Mathf.MoveTowards(airPose, air ? 1f : 0f, dt * (air ? 9f : 14f));
            takeoffPose = Mathf.MoveTowards(takeoffPose, 0f, dt * 4f);
            landPose = Mathf.MoveTowards(landPose, 0f, dt * 3f);
            divePose = Mathf.MoveTowards(divePose, diving ? 1f : 0f, dt * 10f);
            float vy = movement != null ? movement.VerticalSpeed : 0f;
            bool nearGround = air && vy < -1f && movement.GroundDistance < 0.75f;
            reachPose = Mathf.MoveTowards(reachPose, nearGround ? 1f : 0f, dt * 10f);
            if (airPose <= 0f && landPose <= 0f && takeoffPose <= 0f) return;

            Vector3 right = transform.right, fwd = transform.forward;
            float rise = Mathf.Clamp01(vy / 5f), fall = Mathf.Clamp01(-vy / 6f), apex = 1f - Mathf.Max(rise, fall);
            float stride = Mathf.Clamp01((movement != null ? movement.AirSpeed : 0f) / 3f);
            float w = airPose * (1f - reachPose * .6f);

            // legs (negative thigh angle = knee forward / up, positive shin = foot back)
            float lead = -45f * rise - 72f * apex - 28f * fall;
            float trail = Mathf.Lerp(lead * .9f, 18f * rise - 12f * apex - 6f * fall, stride);
            float shinLead = 55f * rise + 100f * apex + 35f * fall;
            float shinTrail = Mathf.Lerp(shinLead * .9f, 75f * rise + 70f * apex + 30f * fall, stride);
            float reachThigh = -22f * reachPose * airPose, reachShin = 12f * reachPose * airPose;
            float knee = landPose * Mathf.Sin(Mathf.Clamp01(landPose) * Mathf.PI * .5f);   // eased landing crouch
            float dive = divePose;
            Turn(thighL, lead * w + reachThigh - 48f * knee - 30f * dive, right);
            Turn(thighR, trail * w + reachThigh - 40f * knee - 30f * dive, right);
            Turn(shinL, shinLead * w + reachShin + 88f * knee + 40f * dive, right);
            Turn(shinR, shinTrail * w + reachShin + 80f * knee + 40f * dive, right);

            // arms: swing up on launch, open wide at the apex, rise for balance while falling, raised overhead for the slam
            float armsFwd = -70f * takeoffPose - 35f * rise * airPose - 10f * apex * airPose - 25f * fall * airPose - 120f * dive;
            float armsOut = (20f * rise + 50f * apex + 65f * fall) * airPose * (1f - dive) + 25f * knee;
            Turn(upperArmL, armsFwd, right); Turn(upperArmR, armsFwd, right);
            Turn(upperArmL, -armsOut, fwd); Turn(upperArmR, armsOut, fwd);
            float elbow = (30f * airPose + 20f * knee) * (1f - dive * .7f);
            Turn(foreArmL, -elbow, right); Turn(foreArmR, -elbow, right);

            // spine: arch on launch, curl at the apex, open while falling, fold into the landing / slam
            Turn(spine, -12f * takeoffPose + (8f * apex - 5f * fall) * airPose + 18f * knee + 22f * dive, right);

            // the body sinks into the landing
            animator.transform.position += Vector3.down * 0.16f * knee;
            animator.transform.localScale = new Vector3(baseModelScale.x * (1f + 0.04f * knee), baseModelScale.y * (1f - 0.07f * knee + 0.04f * takeoffPose), baseModelScale.z * (1f + 0.04f * knee));
        }

        private void LateUpdate()
        {
            if (!ready) return;
            // GoblinMovement owns the heading; keep the visual root at its authored pose,
            // plus a short procedural flinch when the goblin is hit.
            animator.transform.localPosition = baseModelPosition;
            animator.transform.localRotation = baseModelRotation;
            // body language: bank into turns, lean forward with speed, settle back when stopping
            if (movement != null && !IsDead)
            {
                float dt = Time.deltaTime, k = 1f - Mathf.Exp(-8f * dt);
                float speed = movement.CurrentSpeed;
                float wantBank = Mathf.Clamp(-movement.TurnRate * 0.025f * Mathf.Clamp01(speed / 3f), -12f, 12f);
                float wantPitch = attacking || blocking ? 0f : Mathf.Clamp(speed * 1.3f, 0f, 8f);
                bank = Mathf.Lerp(bank, wantBank, k); pitchLean = Mathf.Lerp(pitchLean, wantPitch, k);
                animator.transform.rotation = Quaternion.AngleAxis(pitchLean, transform.right) *
                                              Quaternion.AngleAxis(bank, transform.forward) * animator.transform.rotation;
            }
            animator.transform.localScale = baseModelScale;
            ApplyJumpPose(Time.deltaTime);
            ApplyDodgePose();
            ApplyStance(Time.deltaTime);
            ApplyWeaponSwing(Time.deltaTime);
            if (flinch > 0f)
            {
                float angle = Mathf.Sin(flinch * Mathf.PI) * 11f;
                animator.transform.rotation = Quaternion.AngleAxis(-angle, transform.right) * animator.transform.rotation;
                flinch = Mathf.MoveTowards(flinch, 0f, Time.deltaTime * 4f);
            }
        }
    }
}
