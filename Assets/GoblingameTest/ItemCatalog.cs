using System.Collections.Generic;
using UnityEngine;

namespace Goblingame.TestScene
{
    public enum ItemKind { Weapon, Food, Material }

    /// <summary>What an item is: name, look, and what it does in a fight or to your belly.</summary>
    public sealed class ItemDef
    {
        public string id, name, description;
        public ItemKind kind;
        public int maxStack = 1, rarity;          // rarity: 0 common, 1 rare, 2 epic
        public float damage = 1f, speed = 1f, reach, heal;
        public Color main = Color.white, accent = Color.gray, glow = Color.black;

        public string Stats
        {
            get
            {
                switch (kind)
                {
                    case ItemKind.Weapon:
                        return "Урон ×" + damage.ToString("0.##") + "   Скорость ×" + speed.ToString("0.##") + (reach > 0.01f ? "   Досягаемость +" + reach.ToString("0.0") + " м" : "");
                    case ItemKind.Food: return "Лечит " + Mathf.RoundToInt(heal) + " здоровья";
                    default: return "Материал · до " + maxStack + " в стопке";
                }
            }
        }
        public Color RarityColor => rarity == 2 ? new Color(.78f, .5f, 1f) : rarity == 1 ? new Color(.45f, .75f, 1f) : new Color(.93f, .87f, .74f);
    }

    /// <summary>Every item in the game.</summary>
    public static class Items
    {
        public static readonly ItemDef Fists = new ItemDef
        {
            id = "fists", name = "Кулаки", kind = ItemKind.Weapon, description = "Верные зелёные кулаки. Всегда при тебе.",
            main = new Color(.42f, .62f, .25f), accent = new Color(.3f, .45f, .18f)
        };
        public static readonly ItemDef Club = new ItemDef
        {
            id = "club", name = "Сучковатая дубина", kind = ItemKind.Weapon, damage = 1.45f, speed = .88f, reach = .3f, rarity = 0,
            description = "Тяжёлая, кривая и очень убедительная.",
            main = new Color(.45f, .29f, .16f), accent = new Color(.86f, .82f, .7f)
        };
        public static readonly ItemDef Dagger = new ItemDef
        {
            id = "dagger", name = "Костяной кинжал", kind = ItemKind.Weapon, damage = 1.15f, speed = 1.3f, reach = .1f, rarity = 1,
            description = "Вырезан из кости стража. Быстрый, как крысолов.",
            main = new Color(.9f, .86f, .74f), accent = new Color(.33f, .2f, .14f)
        };
        public static readonly ItemDef Cleaver = new ItemDef
        {
            id = "cleaver", name = "Ржавый тесак", kind = ItemKind.Weapon, damage = 1.8f, speed = .95f, reach = .35f, rarity = 2,
            description = "Им рубили кабаньи окорока. Теперь — кабанов.",
            main = new Color(.55f, .42f, .36f), accent = new Color(.2f, .14f, .12f), glow = new Color(.25f, .08f, .02f)
        };
        public static readonly ItemDef Mushroom = new ItemDef
        {
            id = "mushroom", name = "Светогриб", kind = ItemKind.Food, heal = 20f, maxStack = 16,
            description = "Светится, хрустит, лечит. Не спрашивай как.",
            main = new Color(.3f, .95f, .9f), accent = new Color(.85f, .88f, .82f), glow = new Color(.2f, 1f, .9f)
        };
        public static readonly ItemDef Stew = new ItemDef
        {
            id = "stew", name = "Болотная похлёбка", kind = ItemKind.Food, heal = 55f, maxStack = 5, rarity = 1,
            description = "Густая, зелёная, с пузырями. Гоблины в восторге.",
            main = new Color(.4f, .26f, .16f), accent = new Color(.45f, .7f, .2f), glow = new Color(.1f, .2f, .02f)
        };
        public static readonly ItemDef Bone = new ItemDef
        {
            id = "bone", name = "Кость", kind = ItemKind.Material, maxStack = 64,
            description = "Осталась от костяного стража. Пригодится.",
            main = new Color(.9f, .87f, .78f), accent = new Color(.75f, .7f, .6f)
        };
        public static readonly ItemDef Coin = new ItemDef
        {
            id = "coin", name = "Медяк", kind = ItemKind.Material, maxStack = 999,
            description = "Звенит в кармане. Гоблины любят звенеть.",
            main = new Color(1f, .74f, .28f), accent = new Color(.8f, .5f, .15f), glow = new Color(.25f, .15f, .02f)
        };

        private static Dictionary<string, ItemDef> byId;
        public static IEnumerable<ItemDef> All { get { yield return Club; yield return Dagger; yield return Cleaver; yield return Mushroom; yield return Stew; yield return Bone; yield return Coin; } }

        public static ItemDef Get(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            if (byId == null) { byId = new Dictionary<string, ItemDef>(); byId[Fists.id] = Fists; foreach (var d in All) byId[d.id] = d; }
            return byId.TryGetValue(id, out var def) ? def : null;
        }

        /// <summary>"coin:12" → (coin, 12); "club" → (club, 1).</summary>
        public static (ItemDef def, int count) Parse(string entry)
        {
            if (string.IsNullOrEmpty(entry)) return (null, 0);
            int colon = entry.IndexOf(':');
            if (colon < 0) return (Get(entry.Trim()), 1);
            int.TryParse(entry.Substring(colon + 1), out int n);
            return (Get(entry.Substring(0, colon).Trim()), Mathf.Max(1, n));
        }
    }
}
