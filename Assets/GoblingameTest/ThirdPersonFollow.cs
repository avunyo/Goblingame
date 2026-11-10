using UnityEngine;

namespace Goblingame.TestScene
{
    public sealed class ThirdPersonFollow : MonoBehaviour
    {
        [SerializeField] private Transform target;
        [SerializeField] private Vector3 offset = new Vector3(0f, 3f, -5f);
        [SerializeField] private Vector3 lookOffset = new Vector3(0f, 1f, 0f);
        [SerializeField, Min(0.01f)] private float smoothTime = 0.15f;
        private Vector3 velocity;

        // Movement uses the intended view direction, never the camera's follow lag.
        public Vector3 PlanarForward
        {
            get
            {
                Vector3 forward = lookOffset - offset;
                forward.y = 0f;
                return forward.sqrMagnitude > 0.001f ? forward.normalized : Vector3.forward;
            }
        }

        private void Start()
        {
            if (target == null) return;
            transform.position = target.position + offset;
            Aim();
        }

        private void LateUpdate()
        {
            if (target == null) return;
            transform.position = Vector3.SmoothDamp(transform.position,
                target.position + offset, ref velocity, smoothTime);
            Aim();
        }

        private void Aim()
        {
            // Fixed viewing angle: lateral movement must not steer the camera and WASD.
            Vector3 direction = lookOffset - offset;
            if (direction.sqrMagnitude > 0.001f)
                transform.rotation = Quaternion.LookRotation(direction);
        }
    }
}
