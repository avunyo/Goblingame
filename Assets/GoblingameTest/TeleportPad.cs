using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Goblingame.TestScene
{
    /// <summary>Rune circle: E teleports to the linked circle (house cellar ⇄ dungeon).</summary>
    public sealed class TeleportPad : MonoBehaviour
    {
        public TeleportPad target; public string label = "войти в подземелье"; public Transform ring;
        private static float lockedUntil;

        private void Update()
        {
            if (ring != null) ring.Rotate(0f, 40f * Time.deltaTime, 0f, Space.Self);
            var player = Interaction.Player;
            if (target == null || player == null || player.IsDead || Time.time < lockedUntil) return;
            if (Interaction.Distance(transform.position) > 1.8f || Mathf.Abs(player.transform.position.y - transform.position.y) > 3f) return;
            Interaction.Offer("E — " + label);
            if (Interaction.Pressed) Use();
        }

        public void Use()
        {
            var player = Interaction.Player; if (player == null || target == null) return;
            lockedUntil = Time.time + 1f;
            var cc = player.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            player.transform.SetPositionAndRotation(target.transform.position + target.transform.forward * 1.8f + Vector3.up * 0.15f, target.transform.rotation);
            if (cc != null) cc.enabled = true;
            var cam = Camera.main != null ? Camera.main.GetComponent<ThirdPersonFollow>() : null;
            if (cam != null) cam.Snap();
        }
    }
}
