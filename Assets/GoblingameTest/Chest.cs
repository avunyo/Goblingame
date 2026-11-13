using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Goblingame.TestScene
{
    /// <summary>Treasure chest: E opens it — experience and a swig of healing.</summary>
    public sealed class Chest : MonoBehaviour
    {
        public Transform lid; public int experience = 40; public float heal = 35f; public Light glow;
        [Tooltip("Items that pop out, e.g. \"club\", \"coin:12\", \"mushroom:3\".")] public string[] loot;
        private bool opened; private float openT;
        public bool IsOpen => opened;

        private void Update()
        {
            if (opened)
            {
                openT = Mathf.MoveTowards(openT, 1f, Time.deltaTime * 2.2f);
                if (lid != null) lid.localRotation = Quaternion.Euler(-112f * Mathf.SmoothStep(0f, 1f, openT), 0f, 0f);
                if (glow != null) glow.intensity = Mathf.MoveTowards(glow.intensity, 0f, Time.deltaTime * 2f);
                return;
            }
            if (Interaction.Distance(transform.position) > 2.3f || Interaction.Player == null || Interaction.Player.IsDead ||
                Mathf.Abs(Interaction.Player.transform.position.y - transform.position.y) > 3f) return;
            Interaction.Offer("E — открыть сундук");
            if (Interaction.Pressed) Open();
        }

        public void Open()
        {
            if (opened) return;
            opened = true;
            var player = Interaction.Player; if (player == null) return;
            var xp = player.GetComponent<PlayerProgression>(); if (xp != null) xp.AddExperience(experience);
            CombatEvents.Raise(transform.position + Vector3.up * 1.6f, experience, CombatEvents.Kind.Reward, false);
            Loot.Throw(loot, transform.position + Vector3.up * .9f);
            float healed = player.Heal(heal);
            if (healed > 0.5f) CombatEvents.Raise(player.transform.position + Vector3.up * 1.9f, healed, CombatEvents.Kind.Heal, true);
        }
    }
}
