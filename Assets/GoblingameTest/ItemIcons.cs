using System.Collections.Generic;
using UnityEngine;

namespace Goblingame.TestScene
{
    /// <summary>Inventory icons rendered once from the real item models (transparent background, three-quarter view).</summary>
    public static class ItemIcons
    {
        private static readonly Dictionary<string, Texture2D> icons = new Dictionary<string, Texture2D>();
        private static readonly Vector3 Studio = new Vector3(0f, -600f, 0f);

        public static Texture2D Get(ItemDef def)
        {
            if (def == null) return null;
            if (icons.TryGetValue(def.id, out var tex) && tex != null) return tex;
            tex = Render(def, 128);
            icons[def.id] = tex;
            return tex;
        }

        private static Texture2D Render(ItemDef def, int size)
        {
            var holder = new GameObject("Icon Studio"); holder.transform.position = Studio;
            var model = ItemMeshes.Spawn(def, holder.transform);
            // weapons lie diagonally like in a classic inventory, small things face the camera
            bool longItem = def.kind == ItemKind.Weapon || def.id == "bone";
            model.transform.localRotation = longItem ? Quaternion.Euler(0, 0, -45f) * Quaternion.Euler(0, 35f, 0) : def.id == "coin" ? Quaternion.Euler(0, 25f, 0) : Quaternion.Euler(-25f, 30f, 0);
            var bounds = model.GetComponent<Renderer>().bounds;

            var camGo = new GameObject("Icon Camera"); var cam = camGo.AddComponent<Camera>();
            var key = new GameObject("Icon Key Light").AddComponent<Light>(); key.type = LightType.Directional; key.intensity = 1.1f; key.color = new Color(1f, .95f, .88f);
            key.transform.rotation = Quaternion.Euler(40f, -30f, 0f);
            var rt = RenderTexture.GetTemporary(size, size, 24, RenderTextureFormat.ARGB32); rt.antiAliasing = 1;
            Texture2D result = null;
            bool fog = RenderSettings.fog;
            try
            {
                RenderSettings.fog = false;
                cam.enabled = false; cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0, 0, 0, 0);
                cam.orthographic = true; cam.orthographicSize = Mathf.Max(bounds.extents.x, bounds.extents.y) * 1.12f;
                cam.nearClipPlane = .01f; cam.farClipPlane = 6f; cam.targetTexture = rt;
                camGo.transform.position = bounds.center - Vector3.forward * 3f; camGo.transform.rotation = Quaternion.identity;
                cam.Render();
                var prev = RenderTexture.active; RenderTexture.active = rt;
                result = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = def.name + " Icon", wrapMode = TextureWrapMode.Clamp };
                result.ReadPixels(new Rect(0, 0, size, size), 0, 0); result.Apply();
                RenderTexture.active = prev;
            }
            catch (System.Exception e) { Debug.LogWarning("Item icon failed for " + def.id + ": " + e.Message); }
            finally
            {
                RenderSettings.fog = fog;
                cam.targetTexture = null; RenderTexture.ReleaseTemporary(rt);
                Object.Destroy(camGo); Object.Destroy(key.gameObject); Object.Destroy(holder);
            }
            return result;
        }
    }
}
