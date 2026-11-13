using System.Collections.Generic;
using UnityEngine;

namespace Goblingame.TestScene
{
    /// <summary>
    /// Painted HUD textures in the colours of the twilight forest: indigo-to-plum gradients with a
    /// misty noise, a sunset rim along the top, bronze bevels, starry discs and glowing rune rings.
    /// Baked once at runtime (no image files), cached as sprites.
    /// </summary>
    public static class TwilightUI
    {
        public static readonly Color Indigo = new Color(.06f, .05f, .14f), Plum = new Color(.24f, .11f, .32f), Magenta = new Color(.92f, .38f, .66f),
                                     Sunset = new Color(1f, .6f, .3f), Gold = new Color(1f, .8f, .42f), Bronze = new Color(.55f, .36f, .26f), Teal = new Color(.35f, .95f, .9f);
        private static readonly Dictionary<string, Sprite> cache = new Dictionary<string, Sprite>();

        public static Sprite Slot => Get("slot", () => Panel(128, 128, 24f, 5f, false));
        public static Sprite SlotLit => Get("slotLit", () => Panel(128, 128, 24f, 5f, true));
        public static Sprite Strip => Get("strip", () => Panel(1024, 128, 40f, 6f, false, stars: true));
        public static Sprite Plate => Get("plate", () => Panel(512, 128, 34f, 5f, false, stars: true));
        public static Sprite Orb => Get("orb", () => Disc(128, 6f, false));
        public static Sprite OrbLit => Get("orbLit", () => Disc(128, 6f, true));
        public static Sprite Hub => Get("hub", () => Disc(256, 10f, false, nebula: true));
        public static Sprite MetalRing => Get("metalRing", () => Ring(256, 9f));
        public static Sprite Runes => Get("runes", RuneRing);
        public static Sprite Glow => Get("glow", SoftGlow);
        public static Sprite ArcBand => Get("arcBand", ArcGradient);
        public static Sprite Star => Get("star", Sparkle);

        private static Sprite Get(string key, System.Func<Texture2D> make)
        {
            if (cache.TryGetValue(key, out var s) && s != null) return s;
            var t = make(); t.name = "Twilight " + key; t.wrapMode = TextureWrapMode.Clamp; t.filterMode = FilterMode.Bilinear; t.Apply(true);
            s = Sprite.Create(t, new Rect(0, 0, t.width, t.height), new Vector2(.5f, .5f), 100f, 0, SpriteMeshType.FullRect);
            s.name = key; cache[key] = s; return s;
        }

        private static float Fbm(float x, float y, float seed)
        {
            float v = 0, a = .5f, f = 1f;
            for (int i = 0; i < 4; i++) { v += a * Mathf.PerlinNoise(seed + x * f, seed * 1.3f + y * f); a *= .5f; f *= 2.03f; }
            return v / .9375f;
        }
        private static float Cover(float sd) => Mathf.Clamp01(.5f - sd);
        private static float RoundedSd(float px, float py, float w, float h, float r)
        {
            float qx = Mathf.Abs(px - w / 2f) - (w / 2f - r), qy = Mathf.Abs(py - h / 2f) - (h / 2f - r);
            return new Vector2(Mathf.Max(qx, 0), Mathf.Max(qy, 0)).magnitude + Mathf.Min(Mathf.Max(qx, qy), 0) - r;
        }
        private static float Hash(int x, int y) { uint h = (uint)(x * 374761393 + y * 668265263); h = (h ^ (h >> 13)) * 1274126177; return (h & 0xffff) / 65535f; }

        /// <summary>Rounded panel: misty indigo→plum body, sunset glow under the top edge, bronze-gold bevel.</summary>
        private static Texture2D Panel(int w, int h, float radius, float rim, bool lit, bool stars = false)
        {
            var t = new Texture2D(w, h, TextureFormat.RGBA32, true); var px = new Color[w * h];
            float seed = lit ? 7.7f : 3.3f;
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
            {
                float sd = RoundedSd(x + .5f, y + .5f, w, h, radius), a = Cover(sd);
                if (a <= 0f) { px[y * w + x] = Color.clear; continue; }
                float v = (y + .5f) / h, depth = -sd;
                float mist = Fbm(x / 38f, y / 38f, seed);
                Color c = Color.Lerp(Indigo, Plum, Mathf.SmoothStep(0f, 1f, v * .9f + mist * .25f - .1f));
                c = Color.Lerp(c, Magenta * .35f, Mathf.Clamp01((mist - .55f) * 1.6f) * .5f);
                // sunset haze glowing under the upper edge
                float haze = Mathf.Clamp01(1f - (h - y) / (h * .45f)) * Mathf.Clamp01(1f - depth / (h * .5f));
                c += Sunset * haze * (lit ? .5f : .22f);
                if (lit) c += Magenta * .18f * Mathf.Clamp01(1f - depth / 30f);
                // a few stars in the dark
                if (stars && Hash(x / 2, y / 2) > .996f && v < .8f) c += Color.white * .55f * Hash(x, y + 9);
                // vignette towards the edges
                c *= Mathf.Lerp(.75f, 1f, Mathf.Clamp01(depth / 18f));
                // bevel: gold at the top, bronze/plum at the bottom, a lit upper-left edge
                if (depth < rim)
                {
                    float k = depth / rim;
                    Color metal = Color.Lerp(lit ? Sunset : Gold, Bronze * .8f, 1f - v);
                    metal *= Mathf.Lerp(1.15f, .7f, Mathf.Abs(k - .35f) * 1.6f);
                    c = Color.Lerp(metal, c, Mathf.SmoothStep(0f, 1f, k));
                }
                else if (depth < rim + 1.6f) c *= .55f;   // dark inner line
                c.a = a; px[y * w + x] = c;
            }
            t.SetPixels(px); return t;
        }

        /// <summary>Round version of the slot (for the weapon arc and the flask), optionally a starry nebula.</summary>
        private static Texture2D Disc(int n, float rim, bool lit, bool nebula = false)
        {
            var t = new Texture2D(n, n, TextureFormat.RGBA32, true); var px = new Color[n * n]; float R = n / 2f - 1f;
            for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
            {
                float dx = x + .5f - n / 2f, dy = y + .5f - n / 2f, d = Mathf.Sqrt(dx * dx + dy * dy), sd = d - R, a = Cover(sd);
                if (a <= 0f) { px[y * n + x] = Color.clear; continue; }
                float depth = -sd, rr = d / R, v = (y + .5f) / n;
                float mist = Fbm(x / (n * .22f), y / (n * .22f), nebula ? 11f : 5f);
                Color c = Color.Lerp(Plum * 1.1f, Indigo, Mathf.SmoothStep(0f, 1f, rr * .9f + (mist - .5f) * .4f));
                if (nebula)
                {
                    c = Color.Lerp(c, Magenta * .55f, Mathf.Clamp01((mist - .5f) * 2.2f) * (1f - rr) * .9f);
                    c = Color.Lerp(c, Teal * .3f, Mathf.Clamp01((Fbm(x / 30f, y / 30f, 23f) - .6f) * 3f) * .6f);
                    // a sunset glowing at the bottom of the disc, like the horizon seen through the trees
                    c += Sunset * Mathf.Clamp01(1f - v / .45f) * .45f * Mathf.Clamp01(1f - rr * .6f);
                    if (Hash(x, y) > .993f) c += Color.white * (.4f + .6f * Hash(y, x));
                }
                else c += Sunset * Mathf.Clamp01((v - .55f) / .45f) * (lit ? .35f : .12f);
                if (lit) c += Magenta * .25f * Mathf.Clamp01(1f - depth / (n * .2f));
                if (depth < rim)
                {
                    float k = depth / rim, ang = Mathf.Atan2(dy, dx);
                    Color metal = Color.Lerp(Bronze * .8f, lit ? Sunset : Gold, .5f + .5f * Mathf.Sin(ang + .8f));   // light from the upper left
                    metal *= Mathf.Lerp(1.2f, .7f, Mathf.Abs(k - .4f) * 1.5f);
                    c = Color.Lerp(metal, c, Mathf.SmoothStep(0f, 1f, k));
                }
                else if (depth < rim + 1.8f) c *= .5f;
                c.a = a; px[y * n + x] = c;
            }
            t.SetPixels(px); return t;
        }

        /// <summary>A polished bronze ring with a sunset sheen (for the flask and the hub).</summary>
        private static Texture2D Ring(int n, float width)
        {
            var t = new Texture2D(n, n, TextureFormat.RGBA32, true); var px = new Color[n * n]; float R = n / 2f - 1f;
            for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
            {
                float dx = x + .5f - n / 2f, dy = y + .5f - n / 2f, d = Mathf.Sqrt(dx * dx + dy * dy);
                float a = Cover(d - R) * (1f - Cover(d - (R - width)));
                if (a <= 0f) { px[y * n + x] = Color.clear; continue; }
                float k = (R - d) / width, ang = Mathf.Atan2(dy, dx);
                Color c = Color.Lerp(Bronze * .7f, Gold, .5f + .5f * Mathf.Sin(ang + .8f));
                c = Color.Lerp(c, Magenta, .25f * (.5f + .5f * Mathf.Sin(ang * 3f - 1f)));
                c *= Mathf.Lerp(1.25f, .65f, Mathf.Abs(k - .4f) * 1.6f);
                c.a = a; px[y * n + x] = c;
            }
            t.SetPixels(px); return t;
        }

        /// <summary>Thin ring of rune marks (rotates slowly behind the hub).</summary>
        private static Texture2D RuneRing()
        {
            const int n = 256; var t = new Texture2D(n, n, TextureFormat.RGBA32, true); var px = new Color[n * n]; float R = n / 2f - 4f;
            for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
            {
                float dx = x + .5f - n / 2f, dy = y + .5f - n / 2f, d = Mathf.Sqrt(dx * dx + dy * dy), ang = (Mathf.Atan2(dy, dx) / (Mathf.PI * 2f) + 1f) % 1f;
                float a = 0f;
                a = Mathf.Max(a, Cover(Mathf.Abs(d - R) - .8f) * .7f);                          // outer hairline
                a = Mathf.Max(a, Cover(Mathf.Abs(d - (R - 16f)) - .6f) * .45f);                 // inner hairline
                int glyph = Mathf.FloorToInt(ang * 24f); float local = ang * 24f - glyph;     // 24 rune cells
                float band = (R - 3f - d) / 10f;                                                 // 0..1 across the rune band
                if (band > 0f && band < 1f)
                {
                    int g = (int)(Hash(glyph, 3) * 5f);
                    float sx = (local - .5f) * 10f, sy = (band - .5f) * 10f;
                    float stroke = g == 0 ? Mathf.Abs(sx) : g == 1 ? Mathf.Min(Mathf.Abs(sx - sy * .6f), Mathf.Abs(sy + 2f)) : g == 2 ? Mathf.Min(Mathf.Abs(sx + 1.5f), Mathf.Abs(sx - 1.5f)) :
                                   g == 3 ? Mathf.Abs(Mathf.Abs(sx) - Mathf.Abs(sy) * .7f) : Mathf.Min(Mathf.Abs(sy), Mathf.Abs(sx - 2f));
                    a = Mathf.Max(a, Cover(stroke * 1.6f - .9f) * (local > .18f && local < .82f ? 1f : 0f));
                }
                px[y * n + x] = new Color(1f, 1f, 1f, a);
            }
            t.SetPixels(px); return t;
        }

        private static Texture2D SoftGlow()
        {
            const int n = 128; var t = new Texture2D(n, n, TextureFormat.RGBA32, true); var px = new Color[n * n];
            for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
            {
                float d = new Vector2(x + .5f - n / 2f, y + .5f - n / 2f).magnitude / (n / 2f);
                float a = Mathf.Clamp01(1f - d); a = a * a * a;
                px[y * n + x] = new Color(1f, 1f, 1f, a);
            }
            t.SetPixels(px); return t;
        }

        /// <summary>Ring whose colour runs violet → magenta → sunset around the circle (used as a filled arc).</summary>
        private static Texture2D ArcGradient()
        {
            const int n = 512; var t = new Texture2D(n, n, TextureFormat.RGBA32, true); var px = new Color[n * n]; float R = n / 2f - 2f, w = 7f;
            for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
            {
                float dx = x + .5f - n / 2f, dy = y + .5f - n / 2f, d = Mathf.Sqrt(dx * dx + dy * dy);
                float a = Cover(Mathf.Abs(d - (R - w / 2f)) - w / 2f);
                float halo = Mathf.Clamp01(1f - Mathf.Abs(d - (R - w / 2f)) / 14f); halo *= halo * .35f;
                if (a <= 0f && halo <= 0f) { px[y * n + x] = Color.clear; continue; }
                float ang = (Mathf.Atan2(dy, dx) / (Mathf.PI * 2f) + 1f) % 1f;
                Color c = ang < .5f ? Color.Lerp(new Color(.55f, .4f, 1f), Magenta, ang * 2f) : Color.Lerp(Magenta, Sunset, (ang - .5f) * 2f);
                c = Color.Lerp(c, Color.white, Mathf.Clamp01(1f - Mathf.Abs(d - (R - w / 2f)) / 2f) * .35f);
                c.a = Mathf.Max(a, halo); px[y * n + x] = c;
            }
            t.SetPixels(px); return t;
        }

        private static Texture2D Sparkle()
        {
            const int n = 32; var t = new Texture2D(n, n, TextureFormat.RGBA32, true); var px = new Color[n * n];
            for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
            {
                float dx = Mathf.Abs(x + .5f - n / 2f) / (n / 2f), dy = Mathf.Abs(y + .5f - n / 2f) / (n / 2f);
                float core = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy) * 2.2f);
                float cross = Mathf.Clamp01(1f - dx * 8f) * Mathf.Clamp01(1f - dy) + Mathf.Clamp01(1f - dy * 8f) * Mathf.Clamp01(1f - dx);
                px[y * n + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(core + cross * .8f));
            }
            t.SetPixels(px); return t;
        }
    }
}
