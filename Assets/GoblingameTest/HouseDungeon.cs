using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Goblingame.TestScene
{
    /// <summary>
    /// A house you can walk into: while the goblin is inside, the roof and walls fade out
    /// so the room stays visible from the third-person camera.
    /// </summary>
    public sealed class HouseDungeon : MonoBehaviour
    {
        public Renderer[] shell;
        public Vector3 interiorSize = new Vector3(7f, 4f, 5f);
        public float fadedAlpha = 0.1f;
        private readonly List<Material> materials = new List<Material>();
        private float alpha = 1f; private bool transparentMode;
        public bool PlayerInside { get; private set; }
        public float Alpha => alpha;

        private void Start()
        {
            if (shell == null) return;
            foreach (var r in shell) if (r != null) materials.AddRange(r.materials);   // per-house instances
        }

        private void Update()
        {
            var player = Interaction.Player;
            if (player != null)
            {
                Vector3 local = transform.InverseTransformPoint(player.transform.position);
                PlayerInside = Mathf.Abs(local.x) < interiorSize.x * .5f && Mathf.Abs(local.z) < interiorSize.z * .5f && local.y > -1f && local.y < interiorSize.y;
            }
            float want = PlayerInside ? fadedAlpha : 1f;
            if (Mathf.Approximately(alpha, want)) return;
            alpha = Mathf.MoveTowards(alpha, want, Time.deltaTime * 3f);
            bool needTransparent = alpha < 0.995f;
            if (needTransparent != transparentMode) { transparentMode = needTransparent; foreach (var m in materials) SetMode(m, needTransparent); }
            foreach (var m in materials) { var c = m.color; c.a = alpha; m.color = c; }
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
