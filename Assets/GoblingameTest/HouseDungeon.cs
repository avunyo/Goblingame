using System.Collections.Generic;
using UnityEngine;

namespace Goblingame.TestScene
{
    /// <summary>
    /// A house you can walk into: while the goblin is inside, the roof and the walls between the
    /// camera and the room fade out (the far walls stay solid, so the room still reads as a room).
    /// </summary>
    public sealed class HouseDungeon : MonoBehaviour
    {
        public Renderer[] shell;
        [Tooltip("Outward normal of each shell piece in house space; zero = roof (fades whenever the goblin is inside).")]
        public Vector3[] shellNormals;
        public Vector3 interiorSize = new Vector3(7f, 4f, 5f);
        public float fadedAlpha = 0.08f;

        private sealed class Piece { public Renderer renderer; public Material[] materials; public Vector3 normal; public float alpha = 1f; public bool transparent; }
        private readonly List<Piece> pieces = new List<Piece>();
        public bool PlayerInside { get; private set; }
        /// <summary>Alpha of the roof (the most faded part).</summary>
        public float Alpha { get; private set; } = 1f;
        /// <summary>Alpha of the most opaque wall (for tests: the far side stays solid).</summary>
        public float SolidestWall { get; private set; } = 1f;

        private void Start()
        {
            if (shell == null) return;
            for (int i = 0; i < shell.Length; i++)
            {
                if (shell[i] == null) continue;
                pieces.Add(new Piece { renderer = shell[i], materials = shell[i].materials, normal = shellNormals != null && i < shellNormals.Length ? shellNormals[i] : Vector3.zero });   // per-house instances
            }
        }

        private void Update()
        {
            var player = Interaction.Player;
            if (player != null)
            {
                Vector3 local = transform.InverseTransformPoint(player.transform.position);
                PlayerInside = Mathf.Abs(local.x) < interiorSize.x * .5f && Mathf.Abs(local.z) < interiorSize.z * .5f && local.y > -1f && local.y < interiorSize.y;
            }
            var cam = Camera.main;
            Vector3 toCam = cam != null ? transform.InverseTransformDirection(cam.transform.position - transform.position) : Vector3.forward;
            toCam.y = 0f; toCam.Normalize();
            float roof = 1f, solid = 0f;
            foreach (var p in pieces)
            {
                bool fade = PlayerInside && (p.normal == Vector3.zero || Vector3.Dot(p.normal, toCam) > .2f);
                float want = fade ? fadedAlpha : 1f;
                if (!Mathf.Approximately(p.alpha, want))
                {
                    p.alpha = Mathf.MoveTowards(p.alpha, want, Time.deltaTime * 3.5f);
                    bool needTransparent = p.alpha < 0.995f;
                    if (needTransparent != p.transparent) { p.transparent = needTransparent; foreach (var m in p.materials) SetMode(m, needTransparent); }
                    foreach (var m in p.materials) { var c = m.color; c.a = p.alpha; m.color = c; }
                    p.renderer.shadowCastingMode = p.alpha < .5f ? UnityEngine.Rendering.ShadowCastingMode.Off : UnityEngine.Rendering.ShadowCastingMode.On;
                }
                if (p.normal == Vector3.zero) roof = Mathf.Min(roof, p.alpha); else solid = Mathf.Max(solid, p.alpha);
            }
            Alpha = roof; SolidestWall = solid;
        }

        private static void SetMode(Material m, bool fade)
        {
            if (m == null || !m.HasProperty("_Mode")) return;
            m.SetFloat("_Mode", fade ? 2f : 0f);
            m.SetInt("_SrcBlend", (int)(fade ? UnityEngine.Rendering.BlendMode.SrcAlpha : UnityEngine.Rendering.BlendMode.One));
            m.SetInt("_DstBlend", (int)(fade ? UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha : UnityEngine.Rendering.BlendMode.Zero));
            m.SetInt("_ZWrite", fade ? 0 : 1);
            if (fade) m.EnableKeyword("_ALPHABLEND_ON"); else m.DisableKeyword("_ALPHABLEND_ON");
            m.renderQueue = fade ? 3000 : -1;
        }
    }
}
