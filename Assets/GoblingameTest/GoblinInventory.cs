using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Goblingame.TestScene
{
    /// <summary>
    /// The goblin's bag: 9 hotbar slots (keys 1–9) + 18 bag slots. Weapons are equipped from it,
    /// the mouse wheel flips through the weapons (fists always count), food is eaten from the hotbar.
    /// </summary>
    [DefaultExecutionOrder(100)]   // the held weapon follows the hand after the combat script has posed the bones
    public sealed class GoblinInventory : MonoBehaviour
    {
        public const int HotbarSize = 9, BagSize = 18, Size = HotbarSize + BagSize;

        [Serializable] public struct Slot { public string id; public int count; public bool Empty => string.IsNullOrEmpty(id) || count <= 0; }
        [SerializeField] private Slot[] slots = new Slot[Size];
        [SerializeField] private string equippedId = "fists";
        [SerializeField] private int selected;
        [Tooltip("Pause between weapon swaps from the mouse wheel / touchpad swipe.")]
        [SerializeField] private float swipeCooldown = .16f;

        public event Action Changed;                       // contents changed
        public event Action<ItemDef, int> Picked;          // something went into the bag
        public event Action<int> WeaponSwapped;            // -1 / +1 / 0 (direct pick)
        public event Action<string> Message;               // short notices ("Сумка полна")

        public int Selected => selected;
        public ItemDef Weapon => Items.Get(equippedId) ?? Items.Fists;
        public Slot this[int i] => slots[i];
        public bool Open { get; set; }                     // inventory screen is up (no hotbar keys / wheel)

        private GoblinCombat combat;
        private Transform hand, held;
        private Quaternion gripRotation = Quaternion.identity; private Vector3 gripOffset; private float handScale = 1f;
        private float drawT = 1f, nextSwipe;

        private void Awake()
        {
            if (slots == null || slots.Length != Size) slots = new Slot[Size];
            combat = GetComponent<GoblinCombat>();
        }

        private void Start()
        {
            FindHand();
            RefreshHeld();
        }

        // ───────────── contents ─────────────
        public int Count(string id) { int n = 0; foreach (var s in slots) if (s.id == id) n += s.count; return n; }
        public bool Has(string id) => id == "fists" || Count(id) > 0;

        /// <summary>Adds items (fills stacks, then empty hotbar slots, then the bag). Returns how many fit.</summary>
        public int Add(ItemDef def, int count, bool announce = true)
        {
            if (def == null || count <= 0 || def == Items.Fists) return 0;
            int left = count;
            for (int i = 0; i < Size && left > 0; i++)
                if (slots[i].id == def.id && slots[i].count < def.maxStack) { int put = Mathf.Min(left, def.maxStack - slots[i].count); slots[i].count += put; left -= put; }
            for (int i = 0; i < Size && left > 0; i++)
                if (slots[i].Empty) { int put = Mathf.Min(left, def.maxStack); slots[i] = new Slot { id = def.id, count = put }; left -= put; }
            int added = count - left;
            if (added > 0)
            {
                Changed?.Invoke();
                if (announce) Picked?.Invoke(def, added);
                // first real weapon picked up gets equipped straight away
                if (def.kind == ItemKind.Weapon && Weapon == Items.Fists) Equip(def.id, 0);
            }
            if (left > 0) Message?.Invoke("Сумка полна");
            return added;
        }

        public void RemoveAt(int i, int count)
        {
            if (slots[i].Empty) return;
            string id = slots[i].id;
            slots[i].count -= count; if (slots[i].count <= 0) slots[i] = default;
            if (id == equippedId && !Has(id)) Equip("fists", 0);
            Changed?.Invoke();
        }

        /// <summary>Throws a whole stack out in front of the goblin.</summary>
        public void Drop(int i)
        {
            if (i < 0 || i >= Size || slots[i].Empty) return;
            var s = slots[i]; slots[i] = default;
            if (s.id == equippedId && !Has(s.id)) Equip("fists", 0);
            Changed?.Invoke();
            var p = ItemPickup.Spawn(s.id, s.count, transform.position + transform.forward * .6f + Vector3.up * 1f, transform.forward * 3f + Vector3.up * 3.5f);
            p.Delay(2.5f);
        }

        /// <summary>Swap / merge two slots (drag &amp; drop in the inventory screen).</summary>
        public void Move(int from, int to)
        {
            if (from == to || from < 0 || to < 0) return;
            var a = slots[from]; var b = slots[to];
            var def = Items.Get(a.id);
            if (!a.Empty && a.id == b.id && def != null && b.count < def.maxStack)
            {
                int put = Mathf.Min(a.count, def.maxStack - b.count);
                slots[to].count += put; slots[from].count -= put; if (slots[from].count <= 0) slots[from] = default;
            }
            else { slots[from] = b; slots[to] = a; }
            Changed?.Invoke();
        }

        /// <summary>Use a slot: weapons are equipped, food is eaten.</summary>
        public bool Use(int i)
        {
            var def = Items.Get(slots[i].id); if (def == null) return false;
            if (def.kind == ItemKind.Weapon) { Equip(def.id, 0); return true; }
            if (def.kind == ItemKind.Food)
            {
                if (combat == null || combat.IsDead) return false;
                if (combat.Health >= combat.MaxHealth - .5f) { Message?.Invoke("Здоровье и так полное"); return false; }
                float healed = combat.Heal(def.heal);
                CombatEvents.Raise(transform.position + Vector3.up * 1.9f, healed, CombatEvents.Kind.Heal, true);
                RemoveAt(i, 1);
                Message?.Invoke("Съеден: " + def.name);
                return true;
            }
            Message?.Invoke(def.name + " — материал");
            return false;
        }

        // ───────────── weapons ─────────────
        /// <summary>Fists first, then every weapon in slot order (one entry per kind).</summary>
        public List<ItemDef> Weapons()
        {
            var list = new List<ItemDef> { Items.Fists };
            foreach (var s in slots) { var d = Items.Get(s.id); if (d != null && d.kind == ItemKind.Weapon && !list.Contains(d)) list.Add(d); }
            return list;
        }

        public void Equip(string id, int direction)
        {
            if (!Has(id)) id = "fists";
            if (id == equippedId && direction == 0) return;
            equippedId = id; drawT = 0f;
            for (int i = 0; i < HotbarSize; i++) if (slots[i].id == id) { selected = i; break; }
            RefreshHeld();
            WeaponSwapped?.Invoke(direction);
            Changed?.Invoke();
        }

        public void Cycle(int direction)
        {
            var list = Weapons();
            int at = Mathf.Max(0, list.IndexOf(Weapon));
            Equip(list[(at + direction + list.Count) % list.Count].id, direction == 0 ? 1 : direction);
        }

        public void Select(int i)
        {
            selected = Mathf.Clamp(i, 0, HotbarSize - 1);
            var def = Items.Get(slots[selected].id);
            if (def == null) { Changed?.Invoke(); return; }
            if (def.kind == ItemKind.Weapon) Equip(def.id, 0);
            else if (def.kind == ItemKind.Food) Use(selected);
            Changed?.Invoke();
        }

        private void Update()
        {
            var kb = Keyboard.current; var mouse = Mouse.current;
            if (!Application.isFocused || Open || Cursor.lockState != CursorLockMode.Locked) { AnimateHeld(); return; }
            if (kb != null)
            {
                for (int i = 0; i < HotbarSize; i++)
                    if (kb[(Key)((int)Key.Digit1 + i)].wasPressedThisFrame) Select(i);
            }
            // wheel / touchpad swipe flips weapons; Ctrl + wheel is the camera zoom
            bool ctrl = kb != null && (kb.leftCtrlKey.isPressed || kb.rightCtrlKey.isPressed);
            if (mouse != null && !ctrl)
            {
                float scroll = mouse.scroll.ReadValue().y;
                if (Mathf.Abs(scroll) > .01f && Time.unscaledTime >= nextSwipe && combat != null && !combat.IsDead)
                {
                    nextSwipe = Time.unscaledTime + swipeCooldown;
                    Cycle(scroll < 0 ? 1 : -1);
                }
            }
            AnimateHeld();
        }

        // ───────────── the weapon in the hand ─────────────
        private void FindHand()
        {
            Transform index = null, pinky = null, middle = null;
            foreach (var t in GetComponentsInChildren<Transform>(true))
            {
                string n = t.name.ToLowerInvariant().Replace(" ", "").Replace("_", "").Replace(".", "");
                if (hand == null && (n.EndsWith("righthand") || n.EndsWith("handr"))) hand = t;
                else if (index == null && n.Contains("righthandindex1")) index = t;
                else if (pinky == null && n.Contains("righthandpinky1")) pinky = t;
                else if (middle == null && n.Contains("righthandmiddle1")) middle = t;
            }
            if (hand == null) { Debug.LogWarning("[Inventory] right hand bone not found — weapons are not shown in the hand"); return; }
            handScale = hand.lossyScale.x;
            // the handle runs across the knuckles (pinky → index), the blade side points at the thumb
            Vector3 fingers = middle != null ? middle.position - hand.position : hand.rotation * Vector3.up;
            Vector3 across = index != null && pinky != null ? index.position - pinky.position : hand.rotation * Vector3.right;
            fingers.Normalize();
            across = Vector3.ProjectOnPlane(across, fingers).normalized;
            if (across.sqrMagnitude < .1f) across = Vector3.Cross(fingers, transform.forward).normalized;
            Vector3 palm = Vector3.Cross(fingers, across);
            Quaternion world = Quaternion.LookRotation(-palm, -across);   // item +Y along the handle, blade up out of the fist (thumb side)
            gripRotation = Quaternion.Inverse(hand.rotation) * world;
            float reach = middle != null ? (middle.position - hand.position).magnitude : .08f;
            gripOffset = Quaternion.Inverse(hand.rotation) * (fingers * reach * 1.15f - palm * reach * .25f);   // metres, in the hand's frame
            Debug.Log("[Inventory] hand " + hand.name + " index=" + (index != null) + " pinky=" + (pinky != null) + " middle=" + (middle != null) + " scale=" + handScale.ToString("0.###"));
        }

        private void RefreshHeld()
        {
            if (held != null) Destroy(held.gameObject);
            held = null;
            if (hand == null || Weapon == Items.Fists) return;
            // not parented to the bone: the rig's bones are scaled down a hundredfold, so the weapon follows the hand in world space
            held = ItemMeshes.Spawn(Weapon, null, "Held " + Weapon.name).transform;
            held.gameObject.SetActive(isActiveAndEnabled);
            FollowHand();
            foreach (var r in held.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            AnimateHeld();
        }

        private float WeaponScale
        {
            get { var cc = GetComponent<CharacterController>(); return .62f * (cc != null ? Mathf.Clamp(cc.height / 1.5f, .6f, 1.6f) : 1f); }
        }

        private void AnimateHeld()
        {
            drawT = Mathf.MoveTowards(drawT, 1f, Time.deltaTime * 5f);
            if (held == null) return;
            // pops into the fist with a little overshoot
            float s = drawT >= 1f ? 1f : Mathf.Sin(drawT * Mathf.PI * .5f) * (1f + .25f * Mathf.Sin(drawT * Mathf.PI));
            held.localScale = Vector3.one * WeaponScale * Mathf.Max(.01f, s);
        }

        private void FollowHand()
        {
            if (held == null || hand == null) return;
            Quaternion inHand = hand.rotation * gripRotation;
            if (combat != null)
            {
                // relaxed: the blade hangs down and a little back along the leg; on guard it points at the enemy
                Vector3 up = Vector3.up, fwd = transform.forward, side = transform.right;
                Quaternion lowered = Quaternion.LookRotation(side, (-up * .85f - fwd * .35f + side * .12f).normalized);
                Quaternion guard = Quaternion.LookRotation(side, (fwd * .9f + up * .3f + side * .18f).normalized);
                float r = Mathf.SmoothStep(0f, 1f, combat.RelaxWeight), g = Mathf.SmoothStep(0f, 1f, combat.GuardWeight) * .85f;
                if (g > 0f) inHand = Quaternion.Slerp(inHand, guard, g);
                if (r > 0f) inHand = Quaternion.Slerp(inHand, lowered, r);
            }
            held.SetPositionAndRotation(hand.position + hand.rotation * gripOffset, inHand);
        }

        private void LateUpdate() { FollowHand(); }
        private void OnEnable() { if (held != null) held.gameObject.SetActive(true); }
        private void OnDisable() { if (held != null) held.gameObject.SetActive(false); }
        private void OnDestroy() { if (held != null) Destroy(held.gameObject); }

        /// <summary>The weapon model currently in the hand (null with bare fists).</summary>
        public Transform Held => held;
        public Transform Hand => hand;
    }
}
