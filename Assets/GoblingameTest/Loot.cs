using UnityEngine;

namespace Goblingame.TestScene
{
    /// <summary>What monsters drop and how loot pops out of things.</summary>
    public static class Loot
    {
        public static void DropFor(EnemyActor enemy)
        {
            if (enemy == null) return;
            Vector3 at = enemy.transform.position + Vector3.up * 1f;
            switch (enemy.id)
            {
                case "RatRogue":
                    Throw("coin", Random.Range(2, 6), at);
                    if (Random.value < .25f) Throw("mushroom", 1, at);
                    if (Random.value < .2f) Throw("dagger", 1, at);
                    break;
                case "MossGourmand":
                    Throw("mushroom", Random.Range(1, 3), at);
                    if (Random.value < .2f) Throw("stew", 1, at);
                    Throw("coin", Random.Range(1, 4), at);
                    break;
                case "BoneSentinel":
                    Throw("bone", Random.Range(1, 3), at);
                    Throw("coin", Random.Range(2, 5), at);
                    if (Random.value < .15f) Throw("club", 1, at);
                    break;
                case "IronTusk":
                    Throw("coin", Random.Range(8, 15), at);
                    Throw(Random.value < .35f ? "cleaver" : "stew", 1, at);
                    break;
                default:
                    Throw("coin", Random.Range(1, 4), at);
                    break;
            }
        }

        /// <summary>Pops items out in a little fountain. A weapon the goblin already owns turns into coins.</summary>
        public static void Throw(string id, int count, Vector3 at)
        {
            var def = Items.Get(id); if (def == null || count <= 0) return;
            if (def.kind == ItemKind.Weapon)
            {
                var player = Interaction.Player; var inv = player != null ? player.GetComponent<GoblinInventory>() : null;
                if (inv != null && inv.Has(id)) { id = "coin"; count = 10; }
            }
            float a = Random.Range(0f, Mathf.PI * 2f);
            Vector3 impulse = new Vector3(Mathf.Cos(a) * Random.Range(1.2f, 2.4f), Random.Range(4f, 5.5f), Mathf.Sin(a) * Random.Range(1.2f, 2.4f));
            ItemPickup.Spawn(id, count, at, impulse);
        }

        public static void Throw(string[] entries, Vector3 at)
        {
            if (entries == null) return;
            foreach (var e in entries) { var (def, n) = Items.Parse(e); if (def != null) Throw(def.id, n, at); }
        }
    }
}
