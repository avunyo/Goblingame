using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Goblingame.TestScene
{
    /// <summary>Resting by the fire heals. Flames and light flicker.</summary>
    public sealed class Campfire : MonoBehaviour
    {
        public float radius = 4.5f, healPerSecond = 16f;
        public Transform flames; public Light glow;
        private float baseIntensity, shown; private Vector3[] baseScale;

        private void Start()
        {
            if (glow != null) baseIntensity = glow.intensity;
            if (flames != null) { baseScale = new Vector3[flames.childCount]; for (int i = 0; i < baseScale.Length; i++) baseScale[i] = flames.GetChild(i).localScale; }
        }

        private void Update()
        {
            float t = Time.time;
            if (flames != null)
                for (int i = 0; i < flames.childCount; i++)
                {
                    float k = 0.8f + 0.35f * Mathf.PerlinNoise(t * 5f + i * 3.1f, i * 7.7f);
                    var c = flames.GetChild(i); c.localScale = new Vector3(baseScale[i].x * (1.1f - .2f * k), baseScale[i].y * k, baseScale[i].z * (1.1f - .2f * k));
                    c.localRotation = Quaternion.Euler(6f * Mathf.Sin(t * 7f + i), i * 72f + 20f * Mathf.Sin(t * 2f + i), 6f * Mathf.Cos(t * 6f + i * 2f));
                }
            if (glow != null) glow.intensity = baseIntensity * (0.85f + 0.3f * Mathf.PerlinNoise(t * 6f, 1.3f));

            var player = Interaction.Player;
            if (player == null || player.IsDead || Interaction.Distance(transform.position) > radius) return;
            if (player.Health < player.MaxHealth - 0.01f)
            {
                float healed = player.Heal(healPerSecond * Time.deltaTime);
                shown += healed;
                if (shown >= 10f) { CombatEvents.Raise(player.transform.position + Vector3.up * 1.9f, shown, CombatEvents.Kind.Heal, true); shown = 0f; }
                Interaction.Offer("Костёр — тепло лечит раны");
            }
            else Interaction.Offer("Костёр — здоровье полное");
        }
    }
}
