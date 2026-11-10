using UnityEngine;
using UnityEngine.InputSystem;

namespace Goblingame.TestScene
{
    /// <summary>
    /// Free third-person orbit camera. The mouse orbits freely around the goblin;
    /// when an enemy engages (or the player presses F / middle mouse) the camera
    /// smoothly focuses on that enemy while still following the goblin.
    /// </summary>
    [DefaultExecutionOrder(300)]
    public sealed class ThirdPersonFollow : MonoBehaviour
    {
        [SerializeField] private Transform target;
        [Tooltip("Initial offset; its length is the starting zoom distance.")]
        [SerializeField] private Vector3 offset = new Vector3(0f, 3f, -5f);
        [SerializeField] private Vector3 lookOffset = new Vector3(0f, 1f, 0f);
        [SerializeField, Min(0.01f)] private float smoothTime = 0.15f;

        [Header("Free look")]
        [SerializeField] private float mouseSensitivity = 0.12f;
        [SerializeField] private float minPitch = -25f, maxPitch = 65f;
        [SerializeField] private float minDistance = 2.5f, maxDistance = 9f;
        [SerializeField] private float zoomStep = 0.6f;

        [Header("Focus on enemies")]
        [SerializeField] private bool autoFocus = true;
        [SerializeField] private float autoFocusRadius = 7f;
        [SerializeField] private float loseFocusDistance = 15f;
        [SerializeField] private float focusTurnSharpness = 6f;
        [SerializeField] private float focusPitch = 16f;
        [SerializeField] private float shoulderOffset = 0.55f;

        private float yaw, pitch, distance, targetDistance;
        private Vector3 pivot, pivotVelocity;
        private float shake;
        private EnemyActor locked;
        private bool manualRelease;          // player cancelled focus; don't auto-grab the same fight
        private float manualReleaseUntil;
        private Vector2 lockFlick;
        private Camera cam;

        public EnemyActor LockTarget => locked;
        public bool IsLocked => locked != null;
        public Transform Target => target;

        /// <summary>Planar forward used for WASD — the real view direction.</summary>
        public Vector3 PlanarForward
        {
            get
            {
                Vector3 f = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
                return f;
            }
        }

        public void Shake(float amount) { shake = Mathf.Max(shake, amount); }

        private void Start()
        {
            cam = GetComponent<Camera>();
            distance = targetDistance = Mathf.Clamp(offset.magnitude, minDistance, maxDistance);
            yaw = Mathf.Atan2(-offset.x, -offset.z) * Mathf.Rad2Deg;
            pitch = Mathf.Clamp(Mathf.Asin(Mathf.Clamp(offset.normalized.y, -1f, 1f)) * Mathf.Rad2Deg, minPitch, maxPitch);
            if (target == null) return;
            pivot = target.position + lookOffset;
            Place(1f);
            LockCursor(true);
        }

        private void OnDisable() { LockCursor(false); }

        private static void LockCursor(bool value)
        {
            Cursor.lockState = value ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !value;
        }

        private void Update()
        {
            var keyboard = Keyboard.current; var mouse = Mouse.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame) LockCursor(false);
            if (mouse != null && Application.isFocused && Cursor.lockState != CursorLockMode.Locked &&
                (mouse.leftButton.wasPressedThisFrame || mouse.rightButton.wasPressedThisFrame)) LockCursor(true);

            bool toggle = Application.isFocused && ((keyboard != null && keyboard.fKey.wasPressedThisFrame) ||
                (mouse != null && mouse.middleButton.wasPressedThisFrame));
            if (toggle)
            {
                if (locked != null) { locked = null; manualRelease = true; manualReleaseUntil = Time.time + 4f; }
                else { manualRelease = false; locked = FindBest(14f, true); }
            }
        }

        private void LateUpdate()
        {
            if (target == null) return;
            float dt = Time.unscaledDeltaTime;
            var mouse = Mouse.current;
            Vector2 look = Vector2.zero;
            if (mouse != null && Application.isFocused && Cursor.lockState == CursorLockMode.Locked)
                look = mouse.delta.ReadValue() * mouseSensitivity;
            if (mouse != null && Application.isFocused)
            {
                float scroll = mouse.scroll.ReadValue().y;
                if (Mathf.Abs(scroll) > 0.01f) targetDistance = Mathf.Clamp(targetDistance - Mathf.Sign(scroll) * zoomStep, minDistance, maxDistance);
            }
            distance = Mathf.Lerp(distance, targetDistance, 1f - Mathf.Exp(-10f * dt));

            UpdateLock(look);

            if (locked != null)
            {
                // Face the enemy from behind the goblin; a hard mouse flick switches target.
                Vector3 to = locked.Health.HitPoint - target.position; to.y = 0f;
                if (to.sqrMagnitude > 0.01f)
                {
                    float wantYaw = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
                    float k = 1f - Mathf.Exp(-focusTurnSharpness * dt);
                    yaw = Mathf.LerpAngle(yaw, wantYaw, k);
                    pitch = Mathf.Lerp(pitch, focusPitch, k * 0.5f);
                }
                lockFlick = Vector2.Lerp(lockFlick, Vector2.zero, 1f - Mathf.Exp(-4f * dt)) + look;
                if (Mathf.Abs(lockFlick.x) > 18f) { SwitchTarget(Mathf.Sign(lockFlick.x)); lockFlick = Vector2.zero; }
                pitch = Mathf.Clamp(pitch - look.y * 0.5f, minPitch, maxPitch);
            }
            else
            {
                yaw += look.x;
                pitch = Mathf.Clamp(pitch - look.y, minPitch, maxPitch);
            }

            Vector3 desiredPivot = target.position + lookOffset;
            if (locked != null)
            {
                // Keep both in frame: bias the pivot toward the enemy.
                Vector3 enemy = locked.Health.HitPoint;
                desiredPivot = Vector3.Lerp(desiredPivot, enemy, 0.3f);
                desiredPivot.y = Mathf.Max(target.position.y + lookOffset.y, desiredPivot.y - 0.2f);
            }
            pivot = Vector3.SmoothDamp(pivot, desiredPivot, ref pivotVelocity, smoothTime, Mathf.Infinity, dt);
            Place(dt);
        }

        private void Place(float dt)
        {
            Quaternion rot = Quaternion.Euler(pitch, yaw, 0f);
            Vector3 side = rot * Vector3.right * (locked != null ? shoulderOffset : 0f);
            Vector3 origin = pivot + side;
            Vector3 back = rot * Vector3.back;
            float dist = distance + (locked != null ? 0.8f : 0f);
            // Pull the camera in front of walls / terrain, ignoring characters.
            float free = dist;
            foreach (var hit in Physics.SphereCastAll(origin, 0.25f, back, dist, ~0, QueryTriggerInteraction.Ignore))
            {
                if (hit.collider is CharacterController || hit.collider.GetComponentInParent<CharacterController>() != null) continue;
                if (hit.distance > 0f && hit.distance < free) free = hit.distance;
            }
            Vector3 pos = origin + back * Mathf.Max(0.6f, free);
            if (shake > 0f)
            {
                pos += Random.insideUnitSphere * shake * 0.12f;
                shake = Mathf.MoveTowards(shake, 0f, dt * 4f);
            }
            transform.SetPositionAndRotation(pos, rot);
        }

        private void UpdateLock(Vector2 look)
        {
            if (locked != null)
            {
                bool lost = locked.Health == null || locked.Health.IsDead ||
                    Flat(locked.transform.position - target.position).magnitude > loseFocusDistance;
                if (lost) { locked = null; manualRelease = false; }
            }
            if (manualRelease && Time.time > manualReleaseUntil && FindBest(autoFocusRadius, false) == null) manualRelease = false;
            if (locked == null && autoFocus && !manualRelease)
            {
                var candidate = FindBest(autoFocusRadius, false);
                if (candidate != null && candidate.IsEngaged) locked = candidate;
            }
        }

        private EnemyActor FindBest(float radius, bool preferView)
        {
            EnemyActor best = null; float bestScore = float.MaxValue;
            Vector3 view = PlanarForward;
            foreach (var enemy in EnemyActor.All)
            {
                if (enemy == null || enemy.Health == null || enemy.Health.IsDead) continue;
                Vector3 d = Flat(enemy.transform.position - target.position);
                float dist = d.magnitude;
                if (dist > radius) continue;
                float angle = Vector3.Angle(view, d);
                if (preferView && angle > 100f) continue;
                float score = dist + angle * (preferView ? 0.05f : 0.01f) - (enemy.IsEngaged ? 2f : 0f);
                if (score < bestScore) { bestScore = score; best = enemy; }
            }
            return best;
        }

        private void SwitchTarget(float direction)
        {
            if (locked == null) return;
            Vector3 from = Flat(locked.transform.position - target.position);
            EnemyActor best = null; float bestAngle = 180f;
            foreach (var enemy in EnemyActor.All)
            {
                if (enemy == null || enemy == locked || enemy.Health == null || enemy.Health.IsDead) continue;
                Vector3 d = Flat(enemy.transform.position - target.position);
                if (d.magnitude > loseFocusDistance) continue;
                float angle = Vector3.SignedAngle(from, d, Vector3.up);
                if (Mathf.Sign(angle) != direction) continue;
                if (Mathf.Abs(angle) < bestAngle) { bestAngle = Mathf.Abs(angle); best = enemy; }
            }
            if (best != null) locked = best;
        }

        private static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }
    }
}
