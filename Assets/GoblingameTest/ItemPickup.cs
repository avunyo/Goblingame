using UnityEngine;

namespace Goblingame.TestScene
{
    /// <summary>
    /// An item lying in the world: spins and bobs, glows a little, and flies into the goblin's bag
    /// when he walks close (no button needed). Drops from chests and monsters pop out in an arc first.
    /// </summary>
    public sealed class ItemPickup : MonoBehaviour
    {
        public string itemId = "mushroom";
        public int count = 1;
        public float magnetRadius = 2.6f, collectRadius = .8f;

        private Transform model; private Vector3 velocity; private bool flying; private float ground, age, seed;
        private bool settled = true;

        /// <summary>Throws an item out of a chest / a fallen monster.</summary>
        public static ItemPickup Spawn(string id, int count, Vector3 at, Vector3 impulse)
        {
            var go = new GameObject("Pickup " + id);
            go.transform.position = at;
            var p = go.AddComponent<ItemPickup>(); p.itemId = id; p.count = count;
            p.velocity = impulse; p.settled = false; p.age = -.35f;   // can't be grabbed while it's still in the air
            return p;
        }

        /// <summary>Can't be picked up for this long (items the goblin just threw away).</summary>
        public void Delay(float seconds) { age = -seconds; }

        private void Start()
        {
            var def = Items.Get(itemId);
            if (def == null) { Destroy(gameObject); return; }
            seed = Random.value * 10f;
            model = ItemMeshes.Spawn(def, transform).transform;
            model.localScale = Vector3.one * (def.kind == ItemKind.Weapon ? 1.15f : 1.6f);
            foreach (var r in model.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var light = new GameObject("Glint").AddComponent<Light>(); light.transform.SetParent(transform, false); light.transform.localPosition = Vector3.up * .5f;
            light.type = LightType.Point; light.range = 2.2f; light.intensity = .9f; light.color = def.rarity == 2 ? new Color(.8f, .5f, 1f) : def.rarity == 1 ? new Color(.5f, .75f, 1f) : new Color(1f, .85f, .5f);
            light.shadows = LightShadows.None;
            ground = GroundBelow(transform.position);
            if (settled) transform.position = new Vector3(transform.position.x, ground, transform.position.z);
        }

        private static float GroundBelow(Vector3 p)
        {
            foreach (var h in Physics.RaycastAll(p + Vector3.up * 2f, Vector3.down, 30f, ~0, QueryTriggerInteraction.Ignore))
                if (h.collider.GetComponentInParent<CharacterController>() == null && h.collider.GetComponentInParent<EnemyActor>() == null)
                    return h.point.y;
            return p.y;
        }

        private void Update()
        {
            float dt = Time.deltaTime; age += dt;
            if (model == null) return;
            if (!settled)
            {
                velocity += Vector3.down * 14f * dt;
                transform.position += velocity * dt;
                if (velocity.y < 0f && transform.position.y <= ground) { transform.position = new Vector3(transform.position.x, ground, transform.position.z); settled = true; ground = GroundBelow(transform.position); }
            }
            model.localPosition = Vector3.up * (.45f + Mathf.Sin(Time.time * 2.4f + seed) * .08f);
            model.localRotation = Quaternion.Euler(0, Time.time * 90f + seed * 30f, 0) * Quaternion.Euler(0, 0, -20f);

            var player = Interaction.Player;
            if (player == null || player.IsDead || age < 0f) return;
            var inv = player.GetComponent<GoblinInventory>(); if (inv == null) return;
            Vector3 chest = player.transform.position + Vector3.up * .7f;
            float d = Vector3.Distance(transform.position + Vector3.up * .45f, chest);
            if (!flying && d < magnetRadius && settled) flying = true;
            if (flying)
            {
                transform.position = Vector3.MoveTowards(transform.position, chest - Vector3.up * .45f, (6f + age * 2f) * dt);
                if (d < collectRadius)
                {
                    int got = inv.Add(Items.Get(itemId), count);
                    count -= got;
                    if (count <= 0) { Destroy(gameObject); return; }
                    flying = false; age = -2f; settled = false; velocity = Vector3.up * 3f; ground = GroundBelow(transform.position);   // bag full: bounce off
                }
            }
        }
    }
}
