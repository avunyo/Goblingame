using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Goblingame.TestScene
{
    /// <summary>
    /// A lair (hollow oak stump, ambush inside a house, dungeon room): when the player comes close
    /// its mobs crawl out one by one. Clearing all of them calms the lair and pays a reward.
    /// </summary>
    public sealed class MobLair : MonoBehaviour
    {
        public GameObject[] templates;
        public Transform mouth;
        public float triggerRadius = 11f, spawnInterval = 4f, emergeDistance = 3.5f;
        public int maxAlive = 3, totalMobs = 7, reward = 80;
        public Renderer[] eyes;
        public string lairName = "Логово";

        private readonly List<EnemyActor> alive = new List<EnemyActor>();
        private int spawned; private float timer = 0.8f, shake; private bool awake, cleared;
        private Vector3 restPosition; private MaterialPropertyBlock block;
        public bool IsCleared => cleared;
        public int Spawned => spawned;
        public int AliveCount { get { alive.RemoveAll(e => e == null || e.Health == null || e.Health.IsDead); return alive.Count; } }

        private void Start() { restPosition = transform.localPosition; block = new MaterialPropertyBlock(); SetEyes(0.15f); }

        private void Update()
        {
            if (cleared) return;
            shake = Mathf.MoveTowards(shake, 0f, Time.deltaTime * 2.5f);
            transform.localPosition = restPosition + (shake > 0f ? new Vector3(Mathf.Sin(Time.time * 60f), 0f, Mathf.Cos(Time.time * 53f)) * 0.03f * shake : Vector3.zero);
            if (mouth == null || templates == null || templates.Length == 0) return;
            var player = Interaction.Player;
            bool near = player != null && !player.IsDead && Interaction.Distance(mouth.position) < triggerRadius &&
                        Mathf.Abs(player.transform.position.y - mouth.position.y) < 6f;
            if (near) awake = true;
            SetEyes(awake ? 0.75f + 0.25f * Mathf.Sin(Time.time * 4f) : 0.15f);
            if (!awake) return;
            int living = AliveCount;
            if (spawned >= totalMobs)
            {
                if (living == 0)
                {
                    cleared = true; SetEyes(0f);
                    var xp = player != null ? player.GetComponent<PlayerProgression>() : null;
                    if (xp != null) xp.AddExperience(reward);
                    CombatEvents.Raise(mouth.position + Vector3.up * 2.5f, reward, CombatEvents.Kind.Reward, false);
                }
                return;
            }
            timer -= Time.deltaTime;
            if (timer > 0f || living >= maxAlive || !near) return;
            timer = spawnInterval; shake = 1f;
            var template = templates[spawned % templates.Length];
            if (template == null) { spawned++; return; }
            var go = Instantiate(template, mouth.position, mouth.rotation);
            go.name = template.name; go.SetActive(true);
            var actor = go.GetComponent<EnemyActor>();
            if (actor != null) { actor.respawns = false; actor.Emerge(mouth.position + mouth.forward * emergeDistance); alive.Add(actor); }
            spawned++;
        }

        private void SetEyes(float glow)
        {
            if (eyes == null) return;
            foreach (var r in eyes)
            {
                if (r == null) continue;
                if (block == null) block = new MaterialPropertyBlock();   // survives a hot reload
                r.GetPropertyBlock(block); block.SetColor("_EmissionColor", new Color(1f, .35f, .1f) * (glow * 3f)); block.SetColor("_Color", Color.Lerp(new Color(.1f, .05f, .03f), new Color(1f, .5f, .15f), glow));
                r.SetPropertyBlock(block);
            }
        }
    }
}
