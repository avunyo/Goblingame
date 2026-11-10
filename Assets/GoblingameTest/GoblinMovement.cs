using UnityEngine;
using UnityEngine.InputSystem;

namespace Goblingame.TestScene
{
    [RequireComponent(typeof(CharacterController))]
    public sealed class GoblinMovement : MonoBehaviour
    {
        [SerializeField] private Transform movementCamera;
        [SerializeField] private Animator animator;
        [SerializeField, Min(0f)] private float moveSpeed = 3f;
        [SerializeField, Min(0f)] private float runSpeed = 5.5f;
        [SerializeField, Min(0f)] private float speedChangeRate = 12f;
        [SerializeField, Min(0f)] private float turnSpeed = 540f;
        [Header("Jump")]
        [SerializeField, Min(0f)] private float jumpHeight = 1.15f;
        [SerializeField, Min(0f)] private float gravity = 22f;
        [SerializeField, Range(0f, 1f)] private float airControl = 0.45f;
        [SerializeField] private float coyoteTime = 0.12f, jumpBuffer = 0.15f;
        private float lastGroundedTime = -9f, jumpPressedTime = -9f, airborneSince;
        private Vector3 airVelocity;
        private bool wasGrounded = true;
        public bool IsAirborne { get; private set; }
        public float VerticalSpeed => verticalSpeed;
        public float AirTime => IsAirborne ? Time.time - airborneSince : 0f;
        /// <summary>Raised on touchdown with the downward speed at impact (m/s).</summary>
        public event System.Action<float> Landed;
        /// <summary>Raised on take-off.</summary>
        public event System.Action Jumped;
        /// <summary>Planar speed while airborne (m/s).</summary>
        public float AirSpeed => airVelocity.magnitude;
        /// <summary>Distance to the ground below (m); large when nothing is below.</summary>
        public float GroundDistance { get; private set; }
        /// <summary>Movement input injected by the automated playtest (editor only use).</summary>
        [HideInInspector] public Vector2 simulatedInput;
        /// <summary>Plunge straight down (air attack).</summary>
        public void Dive(float speed) { verticalSpeed = -Mathf.Abs(speed); airVelocity *= 0.35f; }
        /// <summary>Request a jump (also used by the automated playtest).</summary>
        public void RequestJump() { jumpPressedTime = Time.time; }
        private CharacterController controller;
        private ThirdPersonFollow followCamera;
        private float verticalSpeed;
        private static readonly int MovingParameter = Animator.StringToHash("Moving");
        private bool hasIdleController;
        private bool hasRunController;
        private float horizontalSpeed;
        private static readonly int RunningParameter = Animator.StringToHash("Running");
        private RuntimeAnimatorController configuredController;
        private GoblinCombat combat;
        private Vector3 externalVelocity;
        public bool IsMoving { get; private set; }
        public bool IsRunning { get; private set; }
        /// <summary>Current planar speed (m/s) and its ratio to the nominal walk/run speed.</summary>
        public float CurrentSpeed => horizontalSpeed;
        public float SpeedRatio => horizontalSpeed / Mathf.Max(0.1f, IsRunning ? runSpeed : moveSpeed);
        /// <summary>Signed yaw rate in degrees per second (positive = turning right).</summary>
        public float TurnRate { get; private set; }
        private float lastYaw;

        /// <summary>Short burst of motion (attack lunge, knockback). Decays quickly.</summary>
        public void Push(Vector3 velocity) { velocity.y = 0f; externalVelocity = velocity; }

        private void Awake()
        {
            controller = GetComponent<CharacterController>();
            combat = GetComponent<GoblinCombat>();
            if (movementCamera == null && Camera.main != null)
                movementCamera = Camera.main.transform;
            if (movementCamera != null)
                followCamera = movementCamera.GetComponent<ThirdPersonFollow>();
            if (animator == null) animator = GetComponentInChildren<Animator>();
            if (animator != null)
            {
                animator.applyRootMotion = false;
                animator.speed = 1f;
            }
        }

        private void Start()
        {
            RefreshAnimationController();
            if (animator == null || !animator.isHuman || animator.runtimeAnimatorController == null ||
                animator.transform == transform || !animator.transform.IsChildOf(transform)) return;

            // Sample the walk state (even when Idle is the default) once before
            // the first rendered frame so a proud-walk body twist cannot bias the heading.
            Transform model = animator.transform;
            Vector3 originalPosition = model.localPosition;
            Quaternion originalRotation = model.localRotation;
            Vector3 originalScale = model.localScale;
            var previousCulling = animator.cullingMode;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.speed = 1f;
            animator.Update(0f);
            int initialState = animator.GetCurrentAnimatorStateInfo(0).fullPathHash;
            int walkState = Animator.StringToHash("Base Layer.Walk");
            int state = animator.HasState(0, walkState) ? walkState : initialState;
            if (hasIdleController) animator.SetBool(MovingParameter, true);
            Transform leftHip = animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
            Transform rightHip = animator.GetBoneTransform(HumanBodyBones.RightUpperLeg);
            Transform leftArm = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
            Transform rightArm = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
            Vector3 averageRight = Vector3.zero;
            for (int sample = 0; sample < 24; sample++)
            {
                animator.Play(state, 0, sample / 24f);
                animator.Update(0f);
                model.localPosition = originalPosition;
                model.localRotation = originalRotation;
                model.localScale = originalScale;
                if (leftHip != null && rightHip != null)
                    averageRight += transform.InverseTransformDirection(rightHip.position - leftHip.position).normalized;
                if (leftArm != null && rightArm != null)
                    averageRight += transform.InverseTransformDirection(rightArm.position - leftArm.position).normalized;
            }
            if (hasIdleController) animator.SetBool(MovingParameter, false);
            animator.Play(initialState, 0, 0f);
            animator.Update(0f);
            model.localPosition = originalPosition;
            model.localRotation = originalRotation;
            model.localScale = originalScale;
            averageRight.y = 0f;
            if (averageRight.sqrMagnitude > 0.001f)
            {
                Vector3 modelForward = Vector3.Cross(averageRight.normalized, Vector3.up);
                float correction = Vector3.SignedAngle(modelForward, Vector3.forward, Vector3.up);
                model.rotation = Quaternion.AngleAxis(correction, transform.up) * model.rotation;
            }
            animator.speed = hasIdleController ? 1f : 0f;
            animator.cullingMode = previousCulling;
        }

        private void Update()
        {
            if (animator != null && animator.runtimeAnimatorController != configuredController)
                RefreshAnimationController();
            Vector2 input = Vector2.zero;
            Keyboard keyboard = Keyboard.current;
            if (Application.isFocused && keyboard != null)
            {
                input.x = (keyboard.dKey.isPressed ? 1f : 0f) - (keyboard.aKey.isPressed ? 1f : 0f);
                input.y = (keyboard.wKey.isPressed ? 1f : 0f) - (keyboard.sKey.isPressed ? 1f : 0f);
            }
            if (simulatedInput.sqrMagnitude > 0f) input = simulatedInput;
            input = Vector2.ClampMagnitude(input, 1f);
            if (combat != null && combat.MovementLocked) input = Vector2.zero;
            if (combat != null && combat.IsDead) externalVelocity = Vector3.zero;
            bool running = hasRunController && input.sqrMagnitude > 0f &&
                (simulatedInput.sqrMagnitude > 0f || (Application.isFocused && keyboard != null && (keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed)));
            horizontalSpeed = Mathf.MoveTowards(horizontalSpeed, input.sqrMagnitude > 0f ?
                (running ? runSpeed : moveSpeed) : 0f, speedChangeRate * Time.deltaTime);
            Vector3 forward = followCamera != null ? followCamera.PlanarForward :
                movementCamera != null ? movementCamera.forward : Vector3.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.001f) forward = Vector3.forward;
            forward.Normalize();
            Vector3 direction = forward * input.y + Vector3.Cross(Vector3.up, forward) * input.x;
            Vector3 facing = combat != null && combat.HasFacing ? combat.FacingDirection : Vector3.zero;
            facing.y = 0f;
            if (facing.sqrMagnitude > 0.001f)
                transform.rotation = Quaternion.RotateTowards(transform.rotation,
                    Quaternion.LookRotation(facing), turnSpeed * 1.6f * Time.deltaTime);
            else if (direction.sqrMagnitude > 0.001f)
                transform.rotation = Quaternion.RotateTowards(transform.rotation,
                    Quaternion.LookRotation(direction), turnSpeed * Time.deltaTime);

            // ── jump (buffered press + coyote time so it never feels like it was eaten)
            bool grounded = controller.isGrounded;
            if (grounded) lastGroundedTime = Time.time;
            if (Application.isFocused && keyboard != null && keyboard.spaceKey.wasPressedThisFrame) jumpPressedTime = Time.time;
            bool canJump = combat == null || (!combat.IsDead && !combat.MovementLocked);
            if (canJump && Time.time - jumpPressedTime <= jumpBuffer && Time.time - lastGroundedTime <= coyoteTime && verticalSpeed <= 0.1f)
            {
                verticalSpeed = Mathf.Sqrt(2f * gravity * jumpHeight);
                jumpPressedTime = lastGroundedTime = -9f;
                airVelocity = direction * horizontalSpeed;
                grounded = false;
                Jumped?.Invoke();
            }
            if (grounded && verticalSpeed < 0f) verticalSpeed = -2f;
            verticalSpeed -= gravity * Time.deltaTime;

            // in the air keep the take-off momentum, steer only a little
            Vector3 planar = direction * horizontalSpeed;
            if (!grounded)
            {
                Vector3 want = direction.sqrMagnitude > 0.001f ? direction * (running ? runSpeed : moveSpeed) : airVelocity;
                airVelocity = Vector3.MoveTowards(airVelocity, want, speedChangeRate * airControl * Time.deltaTime);
                planar = airVelocity;
            }
            else airVelocity = planar;

            Vector3 before = transform.position;
            Vector3 push = externalVelocity;
            externalVelocity = Vector3.MoveTowards(externalVelocity, Vector3.zero, 18f * Time.deltaTime);
            float fallSpeed = -verticalSpeed;
            controller.Move((planar + push + Vector3.up * verticalSpeed) * Time.deltaTime);
            bool nowGrounded = controller.isGrounded;
            if (!nowGrounded && wasGrounded) airborneSince = Time.time;   // jumped or walked off a ledge
            if (nowGrounded && !wasGrounded) { horizontalSpeed = Mathf.Min(airVelocity.magnitude, running ? runSpeed : moveSpeed); Landed?.Invoke(Mathf.Max(0f, fallSpeed)); }
            // tiny steps / slopes do not count as being airborne
            IsAirborne = !nowGrounded && (verticalSpeed > 0f || Time.time - airborneSince > 0.12f);
            wasGrounded = nowGrounded;
            GroundDistance = nowGrounded ? 0f : Physics.Raycast(transform.position + Vector3.up * 0.1f, Vector3.down, out RaycastHit gh, 6f, ~0, QueryTriggerInteraction.Ignore) ? gh.distance - 0.1f : 6f;
            Vector3 displacement = transform.position - before;
            displacement.y = 0f;
            float yaw = transform.eulerAngles.y;
            if (Time.deltaTime > 0f) TurnRate = Mathf.DeltaAngle(lastYaw, yaw) / Time.deltaTime;
            lastYaw = yaw;
            IsMoving = input.sqrMagnitude > 0f && displacement.sqrMagnitude > 0.00000001f;
            IsRunning = running && IsMoving;
            if (animator != null)
            {
                bool moving = input.sqrMagnitude > 0f && displacement.sqrMagnitude > 0.00000001f;
                if (hasIdleController)
                {
                    animator.speed = 1f;
                    animator.SetBool(MovingParameter, moving);
                    if (hasRunController) animator.SetBool(RunningParameter, running && moving);
                }
                else
                    animator.speed = moving ? 1f : 0f;
            }
        }

        private void RefreshAnimationController()
        {
            if (animator == null || animator.runtimeAnimatorController == null) return;
            // Parent Awake may run before the child's Animator has initialized.
            // Query parameters after explicit initialization, also after a controller swap.
            animator.Update(0f);
            hasIdleController = false;
            hasRunController = false;
            foreach (var parameter in animator.parameters)
            {
                if (parameter.name == "Moving" && parameter.type == AnimatorControllerParameterType.Bool)
                    hasIdleController = true;
                if (parameter.name == "Running" && parameter.type == AnimatorControllerParameterType.Bool)
                    hasRunController = true;
            }
            configuredController = animator.runtimeAnimatorController;
            animator.speed = hasIdleController ? 1f : 0f;
        }
    }
}
