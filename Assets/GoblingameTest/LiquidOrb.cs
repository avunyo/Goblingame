using UnityEngine;
using UnityEngine.UI;

namespace Goblingame.TestScene
{
    /// <summary>
    /// Liquid inside a round flask: fills from the bottom, the surface rolls in waves and sloshes
    /// when the level jumps. Drawn as vertical strips clipped to the circle (no mask needed).
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class LiquidOrb : MaskableGraphic
    {
        [Range(0, 1)] public float fill = 1f;
        public Color deep = new Color(.1f, .45f, .12f), surface = new Color(.55f, 1f, .35f), foam = new Color(.85f, 1f, .7f);
        public float waveHeight = 3f, waveSpeed = 2.2f, inset = .9f;
        private float phase, slosh, shown = -1f;

        public void Slosh(float amount) { slosh = Mathf.Min(14f, slosh + amount); }

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;
            if (shown >= 0f && Mathf.Abs(fill - shown) > .002f) slosh = Mathf.Min(14f, slosh + Mathf.Abs(fill - shown) * 60f);
            shown = fill;
            phase += dt * waveSpeed * (1f + slosh * .08f);
            slosh = Mathf.MoveTowards(slosh, 0f, dt * 6f);
            SetVerticesDirty();
        }

        private float Surface(float x, float r)
        {
            float a = waveHeight + slosh;
            return -r + 2f * r * fill + a * Mathf.Sin(x / r * 3.1f + phase) * .7f + a * .45f * Mathf.Sin(x / r * 7.3f - phase * 1.6f) + slosh * .6f * Mathf.Sin(phase * 3f) * (x / r);
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Rect rc = rectTransform.rect; Vector2 c = rc.center; float r = Mathf.Min(rc.width, rc.height) * .5f * inset;
            if (fill <= 0.001f) return;
            const int strips = 56;
            Color Shade(float y) => Color.Lerp(deep, surface, Mathf.Clamp01((y + r) / (2f * r * Mathf.Max(.15f, fill))));
            for (int i = 0; i < strips; i++)
            {
                float x0 = -r + 2f * r * i / strips, x1 = -r + 2f * r * (i + 1) / strips;
                float h0 = Mathf.Sqrt(Mathf.Max(0f, r * r - x0 * x0)), h1 = Mathf.Sqrt(Mathf.Max(0f, r * r - x1 * x1));
                float t0 = Mathf.Clamp(Surface(x0, r), -h0, h0), t1 = Mathf.Clamp(Surface(x1, r), -h1, h1);
                if (t0 <= -h0 + .01f && t1 <= -h1 + .01f) continue;
                Quad(vh, c + new Vector2(x0, -h0), c + new Vector2(x0, t0), c + new Vector2(x1, t1), c + new Vector2(x1, -h1), Shade(-h0), Shade(t0), Shade(t1), Shade(-h1));
                // a bright frothy rim on the surface
                if (t0 < h0 - .5f || t1 < h1 - .5f)
                {
                    float f0 = Mathf.Max(-h0, t0 - 2.5f), f1 = Mathf.Max(-h1, t1 - 2.5f);
                    Quad(vh, c + new Vector2(x0, f0), c + new Vector2(x0, t0), c + new Vector2(x1, t1), c + new Vector2(x1, f1), foam, foam, foam, foam);
                }
            }
        }

        private void Quad(VertexHelper vh, Vector2 a, Vector2 b, Vector2 cc, Vector2 d, Color ca, Color cb, Color ccol, Color cd)
        {
            int i = vh.currentVertCount; Color tint = color;
            vh.AddVert(a, ca * tint, Vector2.zero); vh.AddVert(b, cb * tint, Vector2.zero); vh.AddVert(cc, ccol * tint, Vector2.zero); vh.AddVert(d, cd * tint, Vector2.zero);
            vh.AddTriangle(i, i + 1, i + 2); vh.AddTriangle(i, i + 2, i + 3);
        }
    }
}
