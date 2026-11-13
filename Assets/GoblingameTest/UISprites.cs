using System.Collections.Generic;
using UnityEngine;

namespace Goblingame.TestScene
{
    /// <summary>Anti-aliased UI shapes drawn in code: rounded panels (9-sliced), circles, rings and hearts.</summary>
    public static class UISprites
    {
        private static readonly Dictionary<string, Sprite> cache = new Dictionary<string, Sprite>();

        public static Sprite Rounded => Get("rounded", () => RoundedRect(64, 16f, 0f), new Vector4(20, 20, 20, 20));
        public static Sprite RoundedFrame => Get("roundedFrame", () => RoundedRect(64, 16f, 3.2f), new Vector4(20, 20, 20, 20));
        public static Sprite Circle => Get("circle", () => Disc(128, 0f), Vector4.zero);
        public static Sprite Ring => Get("ring", () => Disc(128, 6f), Vector4.zero);
        public static Sprite ThinRing => Get("thinRing", () => Disc(256, 3f), Vector4.zero);
        public static Sprite Heart => Get("heart", () => HeartShape(64, 0f), Vector4.zero);
        public static Sprite HeartOutline => Get("heartOutline", () => HeartShape(64, 5f), Vector4.zero);

        private delegate Texture2D Maker();
        private static Sprite Get(string key, System.Func<Texture2D> make, Vector4 border)
        {
            if (cache.TryGetValue(key, out var s) && s != null) return s;
            var tex = make(); tex.name = "UI " + key; tex.wrapMode = TextureWrapMode.Clamp; tex.filterMode = FilterMode.Bilinear;
            s = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(.5f, .5f), 100f, 0, SpriteMeshType.FullRect, border);
            s.name = key; cache[key] = s; return s;
        }

        private static Texture2D Blank(int n) => new Texture2D(n, n, TextureFormat.RGBA32, false);

        /// <summary>signed distance → coverage (1 inside, 0 outside, smooth over a pixel)</summary>
        private static float Cover(float sd) => Mathf.Clamp01(.5f - sd);

        private static Texture2D RoundedRect(int n, float radius, float outline)
        {
            var t = Blank(n); var px = new Color[n * n]; float h = n / 2f;
            for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
            {
                Vector2 p = new Vector2(x + .5f - h, y + .5f - h);
                Vector2 q = new Vector2(Mathf.Abs(p.x) - (h - radius), Mathf.Abs(p.y) - (h - radius));
                float sd = new Vector2(Mathf.Max(q.x, 0), Mathf.Max(q.y, 0)).magnitude + Mathf.Min(Mathf.Max(q.x, q.y), 0) - radius + .5f;
                float a = Cover(sd);
                if (outline > 0f) a *= 1f - Cover(sd + outline);
                px[y * n + x] = new Color(1, 1, 1, a);
            }
            t.SetPixels(px); t.Apply(); return t;
        }

        private static Texture2D Disc(int n, float ring)
        {
            var t = Blank(n); var px = new Color[n * n]; float h = n / 2f;
            for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
            {
                float sd = new Vector2(x + .5f - h, y + .5f - h).magnitude - (h - 1f);
                float a = Cover(sd);
                if (ring > 0f) a *= 1f - Cover(sd + ring);
                px[y * n + x] = new Color(1, 1, 1, a);
            }
            t.SetPixels(px); t.Apply(); return t;
        }

        private static Texture2D HeartShape(int n, float outline)
        {
            // classic implicit heart: (x²+y²−1)³ − x²y³ ≤ 0, supersampled for smooth edges
            var t = Blank(n); var px = new Color[n * n];
            bool Inside(float fx, float fy, float shrink)
            {
                float x = (fx / n - .5f) * 2.6f / shrink, y = (fy / n - .45f) * 2.6f / shrink;
                float a = x * x + y * y - 1f; return a * a * a - x * x * y * y * y <= 0f;
            }
            float inner = 1f - outline / n * 2.6f;
            for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
            {
                int hit = 0, hitInner = 0;
                for (int sy = 0; sy < 4; sy++) for (int sx = 0; sx < 4; sx++)
                {
                    float fx = x + (sx + .5f) / 4f, fy = y + (sy + .5f) / 4f;
                    if (Inside(fx, fy, 1f)) hit++;
                    if (outline > 0f && Inside(fx, fy, inner)) hitInner++;
                }
                float a = hit / 16f; if (outline > 0f) a = Mathf.Clamp01(a - hitInner / 16f);
                px[y * n + x] = new Color(1, 1, 1, a);
            }
            t.SetPixels(px); t.Apply(); return t;
        }
    }
}
