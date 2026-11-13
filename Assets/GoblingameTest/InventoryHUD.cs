using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Goblingame.TestScene
{
    /// <summary>
    /// Bottom HUD: a life flask with an experience arc next to the 9-slot belt, a weapon arc in the
    /// bottom-right corner that slides when the wheel flips weapons, a pickup feed, and the bag screen (I).
    /// </summary>
    public sealed class InventoryHUD : MonoBehaviour
    {
        public GoblinCombat player;
        public GoblinInventory inventory;

        private static readonly Color Cream = new Color(.93f, .87f, .74f), Gold = new Color(1f, .78f, .3f), Panel = new Color(.05f, .055f, .07f, .82f),
                                      HeartRed = new Color(.92f, .2f, .22f), XpGreen = new Color(.5f, .95f, .3f);
        private const float SlotSize = 78f, SlotGap = 8f, ArcRadius = 205f, ArcStep = 30f, ArcCentre = 135f;

        private Font font; private Canvas canvas; private PlayerProgression progression; private CombatHUD combatHud;

        private sealed class SlotView
        {
            public RectTransform root; public Image back, frame, glow; public RawImage icon; public Text count, number; public float pop, scale = 1f;
        }

        // ───────────── belt / hearts / xp ─────────────
        private SlotView[] belt;
        private Text levelText, coinText, itemName; private CanvasGroup itemNameGroup; private float itemNameTimer;
        // life flask: green goblin potion that drains, sloshes and bubbles; experience is a violet arc around it
        private RectTransform orbRoot; private LiquidOrb orbLiquid, orbGhost; private Image xpArc, orbFlash, orbRim; private Text hpText, hpMax;
        private RectTransform[] bubbles; private float[] bubbleT; private float ghostHealth, lastHealth, hurtFlash, healGlow, shownXp;

        // ───────────── weapon arc ─────────────
        private RectTransform arcRoot, arcSlotsRoot; private RawImage hubIcon, hubOld; private Text hubName, hubStats, arcHint; private Image hubRing;
        private readonly List<(SlotView view, int index, int copy)> arcSlots = new List<(SlotView, int, int)>();
        private List<ItemDef> arcWeapons = new List<ItemDef>();
        private float arcPos, arcVel, arcTarget, arcActive = 1f, swapT = 1f; private int swapDir;

        // ───────────── pickup feed ─────────────
        private sealed class FeedRow { public RectTransform root; public CanvasGroup group; public RawImage icon; public Text text; public float age = 99f; public string id; public int count; }
        private readonly List<FeedRow> feed = new List<FeedRow>();

        // ───────────── bag screen ─────────────
        private GameObject screen; private SlotView[] grid; private Text tipName, tipStats, tipDesc, tipHint, statsText; private RawImage portrait, dragIcon; private Text dragCount;
        private RectTransform panelRect; private int dragFrom = -1, hover = -1;
        public bool ScreenOpen => screen != null && screen.activeSelf;

        private void Start()
        {
            canvas = GetComponentInParent<Canvas>();
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            combatHud = GetComponent<CombatHUD>();
            if (player != null) progression = player.GetComponent<PlayerProgression>();
            if (player != null) { ghostHealth = lastHealth = player.Health; }
            BuildBelt();
            BuildArc();
            BuildScreen();
            if (inventory != null)
            {
                inventory.Changed += Refresh; inventory.Picked += OnPicked; inventory.WeaponSwapped += OnSwap; inventory.Message += ShowName;
                arcPos = arcTarget = Mathf.Max(0, inventory.Weapons().IndexOf(inventory.Weapon));
            }
            Refresh();
        }

        private void OnDestroy()
        {
            if (inventory != null) { inventory.Changed -= Refresh; inventory.Picked -= OnPicked; inventory.WeaponSwapped -= OnSwap; inventory.Message -= ShowName; }
        }

        // ───────────── building blocks ─────────────
        private RectTransform Rect(string name, Transform parent, Vector2 anchor, Vector2 pos, Vector2 size, Vector2? pivot = null)
        {
            var rt = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>(); rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = anchor; rt.pivot = pivot ?? new Vector2(.5f, .5f); rt.anchoredPosition = pos; rt.sizeDelta = size;
            return rt;
        }
        private Image Img(string name, Transform parent, Vector2 pos, Vector2 size, Sprite sprite, Color color, Vector2? anchor = null)
        {
            var img = Rect(name, parent, anchor ?? new Vector2(.5f, .5f), pos, size).gameObject.AddComponent<Image>();
            img.sprite = sprite; img.color = color; img.raycastTarget = false; if (sprite != null && sprite.border != Vector4.zero) img.type = Image.Type.Sliced;
            return img;
        }
        private Text Txt(string name, Transform parent, Vector2 pos, Vector2 size, int fontSize, Color color, TextAnchor align, bool outline = true, Vector2? anchor = null)
        {
            var t = Rect(name, parent, anchor ?? new Vector2(.5f, .5f), pos, size).gameObject.AddComponent<Text>();
            t.font = font; t.fontSize = fontSize; t.color = color; t.alignment = align; t.raycastTarget = false; t.horizontalOverflow = HorizontalWrapMode.Overflow; t.verticalOverflow = VerticalWrapMode.Overflow;
            if (outline) { var o = t.gameObject.AddComponent<Outline>(); o.effectColor = new Color(0, 0, 0, .85f); o.effectDistance = new Vector2(1.5f, -1.5f); }
            return t;
        }
        private SlotView Slot(string name, Transform parent, Vector2 pos, float size, string number)
        {
            var v = new SlotView();
            v.root = Rect(name, parent, new Vector2(.5f, .5f), pos, new Vector2(size, size));
            v.glow = Img("Glow", v.root, Vector2.zero, new Vector2(size + 18, size + 18), UISprites.Rounded, new Color(Gold.r, Gold.g, Gold.b, 0f));
            v.back = Img("Back", v.root, Vector2.zero, new Vector2(size, size), UISprites.Rounded, Panel);
            v.icon = Rect("Icon", v.root, new Vector2(.5f, .5f), Vector2.zero, new Vector2(size * .82f, size * .82f)).gameObject.AddComponent<RawImage>(); v.icon.raycastTarget = false;
            v.frame = Img("Frame", v.root, Vector2.zero, new Vector2(size, size), UISprites.RoundedFrame, new Color(Cream.r, Cream.g, Cream.b, .25f));
            v.count = Txt("Count", v.root, new Vector2(size * .5f - 8, -size * .5f + 14), new Vector2(60, 24), 20, Color.white, TextAnchor.MiddleRight);
            v.count.rectTransform.pivot = new Vector2(1, .5f);
            if (number != null) v.number = Txt("Key", v.root, new Vector2(-size * .5f + 12, size * .5f - 12), new Vector2(20, 20), 14, new Color(Cream.r, Cream.g, Cream.b, .7f), TextAnchor.MiddleCenter);
            if (v.number != null) v.number.text = number;
            return v;
        }
        private static void Show(SlotView v, GoblinInventory.Slot s)
        {
            var def = s.Empty ? null : Items.Get(s.id);
            v.icon.texture = def != null ? ItemIcons.Get(def) : null; v.icon.enabled = def != null;
            v.count.text = def != null && s.count > 1 ? s.count.ToString() : "";
        }

        // ───────────── belt ─────────────
        private void BuildBelt()
        {
            var root = Rect("Belt HUD", transform, new Vector2(.5f, 0f), new Vector2(0, 0), new Vector2(900, 240), new Vector2(.5f, 0f));
            float width = GoblinInventory.HotbarSize * SlotSize + (GoblinInventory.HotbarSize - 1) * SlotGap;
            var strip = Img("Belt Strip", root, new Vector2(0, 22 + SlotSize / 2f), new Vector2(width + 16, SlotSize + 16), UISprites.Rounded, new Color(0, 0, 0, .35f));
            strip.rectTransform.anchorMin = strip.rectTransform.anchorMax = new Vector2(.5f, 0f);
            belt = new SlotView[GoblinInventory.HotbarSize];
            for (int i = 0; i < belt.Length; i++)
            {
                belt[i] = Slot("Belt " + (i + 1), root, Vector2.zero, SlotSize, (i + 1).ToString());
                belt[i].root.anchorMin = belt[i].root.anchorMax = new Vector2(.5f, 0f);
                belt[i].root.anchoredPosition = new Vector2((i - 4) * (SlotSize + SlotGap), 22 + SlotSize / 2f);
            }
            float xpY = 22 + SlotSize + 14;
            BuildOrb(root, width);
            var coin = Rect("Coins", root, new Vector2(.5f, 0f), new Vector2(width / 2f - 10, xpY + 12), new Vector2(160, 32), new Vector2(1f, .5f));
            var coinIcon = Rect("Coin Icon", coin, new Vector2(1f, .5f), new Vector2(-14, 0), new Vector2(34, 34)).gameObject.AddComponent<RawImage>(); coinIcon.raycastTarget = false; coinIcon.texture = ItemIcons.Get(Items.Coin);
            coinText = Txt("Coin Count", coin, new Vector2(-36, 0), new Vector2(120, 30), 24, Gold, TextAnchor.MiddleRight, true, new Vector2(1f, .5f));
            coinText.rectTransform.pivot = new Vector2(1f, .5f);
            // item name above the belt (shown when switching)
            itemName = Txt("Item Name", root, new Vector2(0, xpY + 16), new Vector2(700, 30), 24, Cream, TextAnchor.MiddleCenter, true, new Vector2(.5f, 0f));
            itemNameGroup = itemName.gameObject.AddComponent<CanvasGroup>(); itemNameGroup.alpha = 0f;
        }

        private static readonly Color Life = new Color(.35f, .95f, .3f), LifeDeep = new Color(.06f, .32f, .1f), Hurt = new Color(1f, .3f, .2f), HurtDeep = new Color(.4f, .03f, .05f), XpViolet = new Color(.78f, .55f, 1f);

        private void BuildOrb(RectTransform root, float beltWidth)
        {
            const float size = 138f;
            orbRoot = Rect("Life Flask", root, new Vector2(.5f, 0f), new Vector2(-beltWidth / 2f - 26f - size / 2f, 18f + size / 2f), new Vector2(size, size));
            // experience arc (behind), 3/4 of a circle opening at the bottom-right
            var track = Img("XP Track", orbRoot, Vector2.zero, new Vector2(size + 34, size + 34), UISprites.Ring, new Color(0, 0, 0, .55f));
            xpArc = Img("XP Arc", orbRoot, Vector2.zero, new Vector2(size + 34, size + 34), UISprites.Ring, XpViolet);
            foreach (var img in new[] { track, xpArc }) { img.type = Image.Type.Filled; img.fillMethod = Image.FillMethod.Radial360; img.fillOrigin = (int)Image.Origin360.Bottom; img.fillClockwise = true; img.rectTransform.localRotation = Quaternion.Euler(0, 0, -45f); }
            track.fillAmount = .75f;
            Img("Flask Shadow", orbRoot, new Vector2(0, -4), new Vector2(size + 6, size + 6), UISprites.Circle, new Color(0, 0, 0, .5f));
            Img("Flask Glass", orbRoot, Vector2.zero, new Vector2(size, size), UISprites.Circle, new Color(.05f, .06f, .08f, .9f));
            orbGhost = Rect("Ghost", orbRoot, new Vector2(.5f, .5f), Vector2.zero, new Vector2(size, size)).gameObject.AddComponent<LiquidOrb>();
            orbGhost.deep = new Color(.55f, .45f, .45f); orbGhost.surface = new Color(.9f, .8f, .75f); orbGhost.foam = new Color(1f, .95f, .9f); orbGhost.raycastTarget = false; orbGhost.color = new Color(1, 1, 1, .55f);
            orbLiquid = Rect("Potion", orbRoot, new Vector2(.5f, .5f), Vector2.zero, new Vector2(size, size)).gameObject.AddComponent<LiquidOrb>(); orbLiquid.raycastTarget = false;
            bubbles = new RectTransform[6]; bubbleT = new float[6];
            for (int i = 0; i < bubbles.Length; i++) { bubbles[i] = Img("Bubble", orbRoot, Vector2.zero, new Vector2(7, 7), UISprites.Ring, new Color(1, 1, 1, .5f)).rectTransform; bubbleT[i] = i / 6f; }
            orbFlash = Img("Flash", orbRoot, Vector2.zero, new Vector2(size * .9f, size * .9f), UISprites.Circle, new Color(1, 1, 1, 0));
            Img("Glass Shine", orbRoot, new Vector2(-30, 34), new Vector2(34, 20), UISprites.Circle, new Color(1, 1, 1, .22f)).rectTransform.localRotation = Quaternion.Euler(0, 0, 35f);
            Img("Glass Glint", orbRoot, new Vector2(-44, 14), new Vector2(8, 8), UISprites.Circle, new Color(1, 1, 1, .35f));
            orbRim = Img("Flask Rim", orbRoot, Vector2.zero, new Vector2(size + 8, size + 8), UISprites.Ring, new Color(.55f, .44f, .3f));
            Img("Flask Rim Inner", orbRoot, Vector2.zero, new Vector2(size - 6, size - 6), UISprites.ThinRing, new Color(0, 0, 0, .6f));
            // a cork-like cap on top
            Img("Cap", orbRoot, new Vector2(0, size / 2f + 4), new Vector2(34, 16), UISprites.Rounded, new Color(.45f, .3f, .18f));
            Img("Cap Band", orbRoot, new Vector2(0, size / 2f - 2), new Vector2(44, 8), UISprites.Rounded, new Color(.55f, .44f, .3f));
            hpText = Txt("HP", orbRoot, new Vector2(0, 6), new Vector2(120, 40), 32, Color.white, TextAnchor.MiddleCenter);
            hpMax = Txt("HP Max", orbRoot, new Vector2(0, -22), new Vector2(120, 22), 16, new Color(1, 1, 1, .75f), TextAnchor.MiddleCenter);
            // level badge where the arc opens
            var badge = Img("Level Badge", orbRoot, Dir(-45f) * (size / 2f + 17f), new Vector2(40, 40), UISprites.Rounded, new Color(.12f, .08f, .18f, .95f));
            badge.rectTransform.localRotation = Quaternion.Euler(0, 0, 45f);
            Img("Level Badge Rim", orbRoot, Dir(-45f) * (size / 2f + 17f), new Vector2(40, 40), UISprites.RoundedFrame, XpViolet).rectTransform.localRotation = Quaternion.Euler(0, 0, 45f);
            levelText = Txt("Level", orbRoot, Dir(-45f) * (size / 2f + 17f), new Vector2(40, 30), 22, Color.white, TextAnchor.MiddleCenter);
        }

        private void ShowName(string text) { if (itemName == null) return; itemName.text = text; itemNameTimer = 2.2f; }

        // ───────────── weapon arc ─────────────
        private void BuildArc()
        {
            arcRoot = Rect("Weapon Arc", transform, new Vector2(1f, 0f), new Vector2(-180, 180), new Vector2(10, 10));
            var track = Img("Arc Track", arcRoot, Vector2.zero, new Vector2(ArcRadius * 2f, ArcRadius * 2f), UISprites.ThinRing, new Color(Cream.r, Cream.g, Cream.b, .3f));
            track.type = Image.Type.Filled; track.fillMethod = Image.FillMethod.Radial360; track.fillOrigin = (int)Image.Origin360.Right; track.fillClockwise = false;
            track.fillAmount = (212f - 58f) / 360f; track.rectTransform.localRotation = Quaternion.Euler(0, 0, 58f);
            // pointer notch at the active spot
            var notch = Img("Notch", arcRoot, Dir(ArcCentre) * (ArcRadius + 52f), new Vector2(14, 14), UISprites.Circle, Gold);
            notch.rectTransform.localRotation = Quaternion.Euler(0, 0, 45);
            arcSlotsRoot = Rect("Arc Slots", arcRoot, new Vector2(.5f, .5f), Vector2.zero, new Vector2(10, 10));
            // hub: the weapon in hand, big
            Img("Hub Shadow", arcRoot, new Vector2(0, -4), new Vector2(188, 188), UISprites.Circle, new Color(0, 0, 0, .45f));
            Img("Hub", arcRoot, Vector2.zero, new Vector2(176, 176), UISprites.Circle, Panel);
            hubRing = Img("Hub Ring", arcRoot, Vector2.zero, new Vector2(176, 176), UISprites.Ring, Gold);
            Img("Hub Inner Ring", arcRoot, Vector2.zero, new Vector2(150, 150), UISprites.ThinRing, new Color(Cream.r, Cream.g, Cream.b, .18f));
            hubOld = Rect("Hub Old Icon", arcRoot, new Vector2(.5f, .5f), Vector2.zero, new Vector2(128, 128)).gameObject.AddComponent<RawImage>(); hubOld.raycastTarget = false;
            hubIcon = Rect("Hub Icon", arcRoot, new Vector2(.5f, .5f), Vector2.zero, new Vector2(128, 128)).gameObject.AddComponent<RawImage>(); hubIcon.raycastTarget = false;
            hubName = Txt("Weapon Name", arcRoot, new Vector2(-20, -112), new Vector2(360, 28), 22, Cream, TextAnchor.MiddleCenter);
            hubStats = Txt("Weapon Stats", arcRoot, new Vector2(-20, -136), new Vector2(360, 22), 16, new Color(Cream.r, Cream.g, Cream.b, .75f), TextAnchor.MiddleCenter);
            arcHint = Txt("Arc Hint", arcRoot, Dir(ArcCentre) * (ArcRadius + 92f), new Vector2(220, 22), 15, new Color(Cream.r, Cream.g, Cream.b, .7f), TextAnchor.MiddleCenter);
            arcHint.text = "колесо мыши";
        }

        private static Vector2 Dir(float degrees) => new Vector2(Mathf.Cos(degrees * Mathf.Deg2Rad), Mathf.Sin(degrees * Mathf.Deg2Rad));

        private void RebuildArcSlots()
        {
            foreach (var s in arcSlots) Destroy(s.view.root.gameObject);
            arcSlots.Clear();
            int n = arcWeapons.Count;
            // three copies of the list make an endless carousel: flipping past the end never jumps
            for (int copy = -1; copy <= 1; copy++)
                for (int i = 0; i < n; i++)
                {
                    var v = Slot("Arc " + arcWeapons[i].id, arcSlotsRoot, Vector2.zero, 74f, null);
                    v.back.sprite = UISprites.Circle; v.back.type = Image.Type.Simple; v.frame.sprite = UISprites.Ring; v.frame.type = Image.Type.Simple;
                    v.glow.sprite = UISprites.Circle; v.glow.type = Image.Type.Simple;
                    v.icon.texture = arcWeapons[i] == Items.Fists ? FistIcon() : ItemIcons.Get(arcWeapons[i]);
                    v.root.gameObject.AddComponent<CanvasGroup>();
                    arcSlots.Add((v, i, copy));
                }
        }

        private void OnSwap(int dir)
        {
            swapDir = dir == 0 ? 1 : dir; swapT = 0f; arcActive = 1f;
            hubOld.texture = hubIcon.texture;
            var list = inventory.Weapons(); int n = list.Count;
            int idx = Mathf.Max(0, list.IndexOf(inventory.Weapon));
            // move the carousel the short way round
            float cur = Mathf.Repeat(arcTarget, n);
            float delta = idx - cur; if (delta > n / 2f) delta -= n; if (delta < -n / 2f) delta += n;
            if (dir != 0 && Mathf.Sign(delta) != Mathf.Sign(dir) && Mathf.Abs(delta) > .01f) delta += dir * n;   // keep the swipe direction
            arcTarget += delta;
            ShowName(inventory.Weapon.name);
        }

        private void OnPicked(ItemDef def, int count)
        {
            // merge into a fresh row of the same item, else take the oldest row
            FeedRow row = null;
            foreach (var r in feed) if (r.id == def.id && r.age < 2.5f) { row = r; row.count += count; break; }
            if (row == null)
            {
                foreach (var r in feed) if (r.age > 3.5f) { row = r; break; }
                if (row == null && feed.Count < 6)
                {
                    row = new FeedRow { root = Rect("Feed Row", transform, new Vector2(1f, 0f), Vector2.zero, new Vector2(320, 44), new Vector2(1f, .5f)) };
                    var back = Img("Back", row.root, Vector2.zero, new Vector2(320, 44), UISprites.Rounded, new Color(0, 0, 0, .55f)); back.rectTransform.anchorMin = Vector2.zero; back.rectTransform.anchorMax = Vector2.one; back.rectTransform.sizeDelta = Vector2.zero;
                    row.icon = Rect("Icon", row.root, new Vector2(1f, .5f), new Vector2(-26, 0), new Vector2(38, 38)).gameObject.AddComponent<RawImage>(); row.icon.raycastTarget = false;
                    row.text = Txt("Text", row.root, new Vector2(-52, 0), new Vector2(260, 30), 20, Cream, TextAnchor.MiddleRight, true, new Vector2(1f, .5f)); row.text.rectTransform.pivot = new Vector2(1f, .5f);
                    row.group = row.root.gameObject.AddComponent<CanvasGroup>();
                    feed.Add(row);
                }
                if (row == null) { row = feed[0]; foreach (var r in feed) if (r.age > row.age) row = r; }
                row.id = def.id; row.count = count;
                // newest row goes to the bottom of the stack
                feed.Remove(row); feed.Insert(0, row);
            }
            row.age = 0f; row.icon.texture = ItemIcons.Get(def);
            row.text.text = "+" + row.count + "  " + def.name; row.text.color = def.RarityColor;
        }

        // ───────────── bag screen ─────────────
        private void BuildScreen()
        {
            var root = Rect("Bag Screen", transform, new Vector2(.5f, .5f), Vector2.zero, Vector2.zero);
            root.anchorMin = Vector2.zero; root.anchorMax = Vector2.one; root.sizeDelta = Vector2.zero;
            screen = root.gameObject;
            var dim = root.gameObject.AddComponent<Image>(); dim.color = new Color(.01f, .01f, .03f, .62f); dim.raycastTarget = false;
            panelRect = Rect("Panel", root, new Vector2(.5f, .5f), new Vector2(0, 20), new Vector2(1200, 700));
            var frame = panelRect.gameObject.AddComponent<ForgedFrame>(); frame.raycastTarget = false; frame.corner = 22; frame.bevel = 6;
            Txt("Title", panelRect, new Vector2(0, 312), new Vector2(600, 44), 36, Gold, TextAnchor.MiddleCenter).text = "СУМКА ГОБЛИНА";
            Txt("Close", panelRect, new Vector2(470, 312), new Vector2(220, 30), 17, new Color(Cream.r, Cream.g, Cream.b, .7f), TextAnchor.MiddleRight).text = "I / Esc — закрыть";
            // left: portrait + stats
            Img("Portrait Back", panelRect, new Vector2(-445, 105), new Vector2(250, 330), UISprites.Rounded, new Color(0, 0, 0, .45f));
            portrait = Rect("Portrait", panelRect, new Vector2(.5f, .5f), new Vector2(-445, 105), new Vector2(240, 320)).gameObject.AddComponent<RawImage>(); portrait.raycastTarget = false; portrait.enabled = false;
            statsText = Txt("Stats", panelRect, new Vector2(-445, -180), new Vector2(250, 200), 19, Cream, TextAnchor.UpperLeft, false);
            statsText.rectTransform.pivot = new Vector2(.5f, .5f); statsText.horizontalOverflow = HorizontalWrapMode.Wrap; statsText.lineSpacing = 1.15f;
            // right: bag (2 × 9) and belt (1 × 9)
            grid = new SlotView[GoblinInventory.Size];
            const float s = 80f, g = 10f; float left = -260f;
            Txt("Bag Label", panelRect, new Vector2(left + 380, 250), new Vector2(760, 26), 18, new Color(Cream.r, Cream.g, Cream.b, .75f), TextAnchor.MiddleLeft, false).text = "СУМКА";
            Txt("Belt Label", panelRect, new Vector2(left + 380, 46), new Vector2(760, 26), 18, new Color(Cream.r, Cream.g, Cream.b, .75f), TextAnchor.MiddleLeft, false).text = "ПОЯС  ·  клавиши 1–9";
            for (int i = 0; i < GoblinInventory.Size; i++)
            {
                bool beltSlot = i < GoblinInventory.HotbarSize;
                int col = beltSlot ? i : (i - GoblinInventory.HotbarSize) % 9, row = beltSlot ? 0 : (i - GoblinInventory.HotbarSize) / 9;
                float y = beltSlot ? -10f : 195f - row * (s + g);
                grid[i] = Slot("Slot " + i, panelRect, new Vector2(left + s / 2f + col * (s + g), y), s, beltSlot ? (i + 1).ToString() : null);
            }
            // tooltip
            Img("Tip Back", panelRect, new Vector2(left + 400, -200), new Vector2(800, 190), UISprites.Rounded, new Color(0, 0, 0, .35f));
            tipName = Txt("Tip Name", panelRect, new Vector2(left + 20, -125), new Vector2(760, 32), 26, Cream, TextAnchor.MiddleLeft, false); tipName.rectTransform.pivot = new Vector2(0, .5f);
            tipStats = Txt("Tip Stats", panelRect, new Vector2(left + 20, -160), new Vector2(760, 26), 19, Gold, TextAnchor.MiddleLeft, false); tipStats.rectTransform.pivot = new Vector2(0, .5f);
            tipDesc = Txt("Tip Desc", panelRect, new Vector2(left + 20, -195), new Vector2(760, 26), 19, new Color(Cream.r, Cream.g, Cream.b, .85f), TextAnchor.MiddleLeft, false); tipDesc.rectTransform.pivot = new Vector2(0, .5f);
            tipHint = Txt("Tip Hint", panelRect, new Vector2(left + 20, -265), new Vector2(760, 24), 16, new Color(Cream.r, Cream.g, Cream.b, .6f), TextAnchor.MiddleLeft, false); tipHint.rectTransform.pivot = new Vector2(0, .5f);
            // the stack held by the mouse
            dragIcon = Rect("Drag Icon", root, new Vector2(.5f, .5f), Vector2.zero, new Vector2(76, 76)).gameObject.AddComponent<RawImage>(); dragIcon.raycastTarget = false; dragIcon.enabled = false;
            dragCount = Txt("Drag Count", dragIcon.transform, new Vector2(26, -24), new Vector2(60, 24), 20, Color.white, TextAnchor.MiddleRight);
            screen.SetActive(false);
        }

        public void ToggleScreen(bool open)
        {
            if (screen == null || inventory == null) return;
            screen.SetActive(open); inventory.Open = open; dragFrom = -1; dragIcon.enabled = false;
            Cursor.lockState = open ? CursorLockMode.None : CursorLockMode.Locked; Cursor.visible = open;
            if (open && portrait.texture == null && player != null) { portrait.texture = PortraitStudio.Render(player.transform, 240, 320, false); portrait.enabled = portrait.texture != null; }
            Refresh();
        }

        private void UpdateScreen()
        {
            var mouse = Mouse.current; if (mouse == null) return;
            Vector2 mp = mouse.position.ReadValue();
            Camera ui = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            hover = -1;
            for (int i = 0; i < grid.Length; i++) if (RectTransformUtility.RectangleContainsScreenPoint(grid[i].root, mp, ui)) { hover = i; break; }
            bool overPanel = RectTransformUtility.RectangleContainsScreenPoint(panelRect, mp, ui);
            if (mouse.leftButton.wasPressedThisFrame)
            {
                if (hover >= 0)
                {
                    if (dragFrom < 0) { if (!inventory[hover].Empty) dragFrom = hover; }
                    else { inventory.Move(dragFrom, hover); dragFrom = -1; }
                }
                else if (dragFrom >= 0 && !overPanel) { inventory.Drop(dragFrom); dragFrom = -1; }   // thrown out of the bag
                else dragFrom = -1;
                Refresh();
            }
            if (mouse.rightButton.wasPressedThisFrame && hover >= 0 && dragFrom < 0) inventory.Use(hover);
            // drag icon follows the cursor
            if (dragFrom >= 0 && !inventory[dragFrom].Empty)
            {
                dragIcon.enabled = true; dragIcon.texture = ItemIcons.Get(Items.Get(inventory[dragFrom].id));
                dragCount.text = inventory[dragFrom].count > 1 ? inventory[dragFrom].count.ToString() : "";
                RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)dragIcon.transform.parent, mp, ui, out var local);
                dragIcon.rectTransform.anchoredPosition = local;
            }
            else { dragIcon.enabled = false; dragFrom = -1; }
            // tooltip
            int tip = hover >= 0 ? hover : dragFrom;
            var def = tip >= 0 && !inventory[tip].Empty ? Items.Get(inventory[tip].id) : null;
            if (def == null) def = inventory.Weapon;
            tipName.text = def.name + (def == inventory.Weapon ? "   · в руке" : ""); tipName.color = def.RarityColor;
            tipStats.text = def.Stats; tipDesc.text = def.description;
            tipHint.text = "ЛКМ — взять / положить     ПКМ — " + (def.kind == ItemKind.Food ? "съесть" : def.kind == ItemKind.Weapon ? "взять в руку" : "—") + "     Вынеси за край сумки — выбросить";
            for (int i = 0; i < grid.Length; i++)
            {
                var v = grid[i]; bool equipped = !inventory[i].Empty && inventory[i].id == inventory.Weapon.id;
                float want = i == hover ? 1.08f : 1f; v.scale = Mathf.Lerp(v.scale, want, 1f - Mathf.Exp(-18f * Time.unscaledDeltaTime)); v.root.localScale = Vector3.one * v.scale;
                v.frame.color = equipped ? Gold : i == hover ? new Color(Cream.r, Cream.g, Cream.b, .8f) : new Color(Cream.r, Cream.g, Cream.b, .25f);
                v.icon.color = i == dragFrom ? new Color(1, 1, 1, .3f) : Color.white;
                var gc = v.glow.color; gc.a = equipped ? .28f : 0f; v.glow.color = gc;
            }
        }

        // ───────────── refresh / per frame ─────────────
        private void Refresh()
        {
            if (inventory == null || belt == null) return;
            for (int i = 0; i < belt.Length; i++) Show(belt[i], inventory[i]);
            if (grid != null) for (int i = 0; i < grid.Length; i++) Show(grid[i], inventory[i]);
            coinText.text = inventory.Count("coin").ToString();
            // the arc lists weapons; rebuild when the set changes
            var list = inventory.Weapons();
            bool same = list.Count == arcWeapons.Count; for (int i = 0; same && i < list.Count; i++) same = list[i] == arcWeapons[i];
            if (!same)
            {
                arcWeapons = list; RebuildArcSlots();
                float idx = Mathf.Max(0, list.IndexOf(inventory.Weapon));
                arcPos = arcTarget = idx;
            }
            var w = inventory.Weapon;
            hubIcon.texture = w == Items.Fists ? FistIcon() : ItemIcons.Get(w);
            hubName.text = w.name; hubName.color = w.RarityColor;
            hubStats.text = w == Items.Fists ? "урон ×1   ·   скорость ×1" : "урон ×" + w.damage.ToString("0.##") + "   ·   скорость ×" + w.speed.ToString("0.##");
            if (statsText != null && player != null)
                statsText.text = "Уровень  " + (progression != null ? progression.Level : 1) + "\nЗдоровье  " + Mathf.CeilToInt(player.Health) + " / " + Mathf.CeilToInt(player.MaxHealth) +
                                 "\nВ руке  " + w.name + "\nУрон  ×" + w.damage.ToString("0.##") + "   Скорость  ×" + w.speed.ToString("0.##") + "\nМедяки  " + inventory.Count("coin");
        }

        private Texture fistIcon;
        private Texture FistIcon()
        {
            if (fistIcon != null) return fistIcon;
            // a green fist drawn as a texture: knuckles + thumb
            const int n = 96; var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp }; var px = new Color[n * n];
            Color skin = Items.Fists.main, dark = Items.Fists.accent;
            for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
            {
                float u = x / (float)n, v = y / (float)n; Color c = new Color(0, 0, 0, 0);
                bool palm = u > .22f && u < .78f && v > .2f && v < .62f;
                for (int k = 0; k < 4; k++) { float cx = .3f + k * .135f; if ((u - cx) * (u - cx) + (v - .64f) * (v - .64f) < .0055f) c = skin; }
                if (palm) c = skin;
                if ((u - .2f) * (u - .2f) / .012f + (v - .42f) * (v - .42f) / .03f < 1f) c = Color.Lerp(skin, dark, .35f);
                if (palm && Mathf.Abs(v - .5f) < .012f && u > .3f) c = dark;
                if (u > .3f && u < .7f && v < .22f && v > .02f) c = Color.Lerp(skin, dark, .5f);
                px[y * n + x] = c;
            }
            t.SetPixels(px); t.Apply(); fistIcon = t; return t;
        }

        private void Update()
        {
            if (player == null || inventory == null || belt == null || orbLiquid == null) return;   // (a hot reload drops the built UI)
            float dt = Time.unscaledDeltaTime;
            var kb = Keyboard.current;
            bool bestiaryOpen = combatHud != null && combatHud.bestiary != null && combatHud.bestiary.activeSelf;
            if (kb != null && Application.isFocused && !bestiaryOpen && !player.IsDead)
            {
                if (kb.iKey.wasPressedThisFrame) ToggleScreen(!ScreenOpen);
                else if (ScreenOpen && kb.escapeKey.wasPressedThisFrame) ToggleScreen(false);
            }
            if (ScreenOpen && player.IsDead) ToggleScreen(false);
            if (ScreenOpen) { UpdateScreen(); if (Time.frameCount % 15 == 0) Refresh(); }

            UpdateHearts(dt);
            UpdateBelt(dt);
            UpdateArc(dt);
            UpdateFeed(dt);
        }

        private void UpdateHearts(float dt)
        {
            float hp = player.Health, max = Mathf.Max(1f, player.MaxHealth), f = Mathf.Clamp01(hp / max);
            if (hp < lastHealth - .01f) { hurtFlash = 1f; orbLiquid.Slosh(6f); }
            if (hp > lastHealth + .5f) healGlow = 1f;
            lastHealth = hp;
            ghostHealth = hp > ghostHealth ? hp : Mathf.MoveTowards(ghostHealth, hp, (hurtFlash > .3f ? 0f : 1f) * max * .45f * dt);
            hurtFlash = Mathf.MoveTowards(hurtFlash, 0f, dt * 2.5f); healGlow = Mathf.MoveTowards(healGlow, 0f, dt * 1.5f);
            orbLiquid.fill = f; orbGhost.fill = Mathf.Clamp01(ghostHealth / max);
            // healthy goblin potion is green; it curdles to red when life runs low
            float danger = Mathf.InverseLerp(.45f, .15f, f);
            orbLiquid.surface = Color.Lerp(Life, Hurt, danger); orbLiquid.deep = Color.Lerp(LifeDeep, HurtDeep, danger);
            orbLiquid.foam = Color.Lerp(new Color(.85f, 1f, .7f), new Color(1f, .75f, .6f), danger);
            orbLiquid.waveSpeed = 2.2f + danger * 3f;
            float pulse = danger > .5f ? Mathf.Sin(Time.unscaledTime * 8f) * .035f * danger : 0f;
            Vector2 shake = hurtFlash > 0f ? new Vector2(Mathf.Sin(Time.unscaledTime * 70f), Mathf.Cos(Time.unscaledTime * 55f)) * 5f * hurtFlash : Vector2.zero;
            orbRoot.localScale = Vector3.one * (1f + pulse + hurtFlash * .06f + healGlow * .04f);
            orbRoot.anchoredPosition = new Vector2(orbRoot.anchoredPosition.x, 18f + 69f) + shake;
            orbFlash.color = new Color(1f, healGlow > hurtFlash ? 1f : .55f, healGlow > hurtFlash ? .7f : .5f, Mathf.Max(hurtFlash * .45f, healGlow * .3f));
            orbRim.color = Color.Lerp(new Color(.55f, .44f, .3f), Hurt, hurtFlash * .8f + danger * .3f * (1f + Mathf.Sin(Time.unscaledTime * 6f)) * .5f);
            hpText.text = Mathf.CeilToInt(hp).ToString(); hpMax.text = "/ " + Mathf.CeilToInt(max);
            // bubbles rise through the potion and pop at the surface
            float r = 69f * .9f, level = -r + 2f * r * f;
            for (int i = 0; i < bubbles.Length; i++)
            {
                bubbleT[i] += dt * (.35f + i * .07f);
                if (bubbleT[i] > 1f) bubbleT[i] -= 1f;
                float x = Mathf.Sin(i * 2.7f + 1f) * r * .55f + Mathf.Sin(Time.unscaledTime * 2f + i) * 3f;
                float bottom = -Mathf.Sqrt(Mathf.Max(0f, r * r - x * x)) + 6f, y = Mathf.Lerp(bottom, level - 4f, bubbleT[i]);
                bool visible = level - 4f > bottom + 4f;
                bubbles[i].anchoredPosition = new Vector2(x, y);
                bubbles[i].localScale = Vector3.one * (.6f + bubbleT[i] * .8f);
                bubbles[i].GetComponent<Image>().color = new Color(1, 1, 1, visible ? .45f * Mathf.Sin(bubbleT[i] * Mathf.PI) : 0f);
            }
            if (progression != null)
            {
                float xp = progression.Experience / (float)Mathf.Max(1, progression.Required);
                if (xp < shownXp - .3f) shownXp = 0f;   // level up: the arc empties and refills
                shownXp = Mathf.MoveTowards(shownXp, xp, dt * .8f);
                xpArc.fillAmount = .75f * shownXp;
                levelText.text = progression.Level.ToString();
            }
        }

        private void UpdateBelt(float dt)
        {
            for (int i = 0; i < belt.Length; i++)
            {
                var v = belt[i];
                bool sel = i == inventory.Selected && !inventory[i].Empty;
                bool equipped = !inventory[i].Empty && inventory[i].id == inventory.Weapon.id;
                float want = sel ? 1.12f : 1f;
                v.scale = Mathf.Lerp(v.scale, want, 1f - Mathf.Exp(-16f * dt));
                v.root.localScale = Vector3.one * v.scale;
                v.frame.color = sel || equipped ? Gold : new Color(Cream.r, Cream.g, Cream.b, .25f);
                var gc = v.glow.color; gc.a = Mathf.Lerp(gc.a, sel ? .3f + .08f * Mathf.Sin(Time.unscaledTime * 4f) : 0f, 1f - Mathf.Exp(-10f * dt)); v.glow.color = gc;
            }
            itemNameTimer -= dt;
            itemNameGroup.alpha = Mathf.MoveTowards(itemNameGroup.alpha, itemNameTimer > 0f ? 1f : 0f, dt * (itemNameTimer > 0f ? 8f : 2f));
        }

        private void UpdateArc(float dt)
        {
            // spring the carousel toward the equipped weapon
            float k = 140f, d = 2f * Mathf.Sqrt(k) * .8f;
            arcVel += ((arcTarget - arcPos) * k - arcVel * d) * dt; arcPos += arcVel * dt;
            arcActive = Mathf.MoveTowards(arcActive, 0f, dt / 3.5f);
            float active = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(arcActive * 2f));
            int n = Mathf.Max(1, arcWeapons.Count);
            int equippedIndex = Mathf.Max(0, arcWeapons.IndexOf(inventory.Weapon));
            foreach (var (view, index, copy) in arcSlots)
            {
                float rel = index + copy * n - arcPos;
                float angle = ArcCentre - rel * ArcStep;             // next weapons sit clockwise (towards the right edge)
                float edge = Mathf.Min(angle - 52f, 218f - angle);   // fade at both ends of the arc
                float alpha = Mathf.Clamp01(edge / 22f) * Mathf.Lerp(.55f, 1f, active);
                bool isEquipped = index == equippedIndex && Mathf.Abs(rel) < .5f;
                view.root.anchoredPosition = Dir(angle) * ArcRadius;
                float sc = Mathf.Lerp(.82f, 1.18f, Mathf.Clamp01(1f - Mathf.Abs(rel)));
                view.root.localScale = Vector3.one * sc;
                view.root.GetComponent<CanvasGroup>().alpha = alpha;
                view.frame.color = isEquipped ? Gold : new Color(Cream.r, Cream.g, Cream.b, .3f);
                var gc = view.glow.color; gc.a = isEquipped ? .35f : 0f; view.glow.color = gc;
                view.root.gameObject.SetActive(alpha > .01f);
            }
            // hub: the new weapon spins in, the old one spins out
            swapT = Mathf.MoveTowards(swapT, 1f, dt * 4.5f);
            float e = 1f - Mathf.Pow(1f - swapT, 3f);
            hubIcon.rectTransform.localRotation = Quaternion.Euler(0, 0, (1f - e) * 70f * swapDir);
            hubIcon.rectTransform.localScale = Vector3.one * (Mathf.Lerp(.4f, 1f, e) + Mathf.Sin(e * Mathf.PI) * .15f);
            hubIcon.color = new Color(1, 1, 1, Mathf.Clamp01(e * 1.6f));
            hubOld.enabled = swapT < 1f && hubOld.texture != null;
            hubOld.rectTransform.localRotation = Quaternion.Euler(0, 0, -e * 70f * swapDir);
            hubOld.rectTransform.localScale = Vector3.one * Mathf.Lerp(1f, .3f, e);
            hubOld.color = new Color(1, 1, 1, 1f - e);
            hubRing.color = Color.Lerp(Gold, Color.white, (1f - e) * .7f);
            hubRing.rectTransform.localScale = Vector3.one * (1f + Mathf.Sin(e * Mathf.PI) * .06f);
            arcHint.color = new Color(Cream.r, Cream.g, Cream.b, .7f * (1f - active * .5f) * (arcWeapons.Count > 1 ? 1f : 0f));
        }

        private void UpdateFeed(float dt)
        {
            for (int i = 0; i < feed.Count; i++)
            {
                var r = feed[i]; r.age += dt;
                float slide = Mathf.Clamp01(r.age / .25f);
                float x = -40f + (1f - (1f - Mathf.Pow(1f - slide, 3f))) * 120f;
                Vector2 target = new Vector2(x, 420f + i * 50f);
                r.root.anchoredPosition = new Vector2(x, Mathf.Lerp(r.root.anchoredPosition.y == 0f ? target.y : r.root.anchoredPosition.y, target.y, 1f - Mathf.Exp(-14f * dt)));
                r.group.alpha = r.age < 3f ? Mathf.Clamp01(r.age / .15f) : Mathf.Clamp01(1f - (r.age - 3f) / .6f);
                r.root.gameObject.SetActive(r.group.alpha > .01f);
            }
        }
    }
}
