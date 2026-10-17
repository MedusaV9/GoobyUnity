using System.Threading.Tasks;
using UnityEngine;

namespace Gooby.Unity.World
{
    /// <summary>
    /// Places the Blender scenery models (StreamingAssets/gooby/scenery, built by tools/blender/build_scenery.py).
    /// Scene builders stay synchronous: <see cref="Place"/> returns an anchor at the final transform right away
    /// and the GLB clone is parented under it once glTFast has imported the template.
    /// Model convention: origin at the ground centre, facade towards local +Z, 1 unit = 1 m. `position` and
    /// `yaw` are relative to `parent`, so dressing parented to a moving root (horizon) travels with it.
    /// </summary>
    public static class Scenery
    {
        public static GameObject Place(Transform parent, string archetype, Vector3 position, float yaw = 0f, float scale = 1f)
        {
            var anchor = new GameObject("Scenery:" + archetype);
            anchor.transform.SetParent(parent, false);
            anchor.transform.localPosition = position;
            anchor.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            anchor.transform.localScale = Vector3.one * scale;
            _ = FillAsync(anchor.transform, archetype);
            return anchor;
        }

        /// <summary>
        /// Like <see cref="Place(Transform,string,Vector3,float,float)"/> with a per-axis scale (a negative X mirrors the
        /// model, e.g. the left curtain of a window) and primitive <paramref name="fallback"/>s that keep the scene complete
        /// while glTFast imports the GLB: they are switched off — not destroyed — once the clone is in, and simply stay
        /// without glTFast or the file.
        /// </summary>
        public static GameObject Place(Transform parent, string archetype, Vector3 position, float yaw, Vector3 scale, params GameObject[] fallback)
        {
            var anchor = new GameObject("Scenery:" + archetype);
            anchor.transform.SetParent(parent, false);
            anchor.transform.localPosition = position;
            anchor.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            anchor.transform.localScale = scale;
            _ = FillAsync(anchor.transform, archetype, fallback);
            return anchor;
        }

        private static async Task FillAsync(Transform anchor, string archetype, GameObject[] fallback = null)
        {
            var clone = await AssetLoader.SceneryInstanceAsync(archetype, anchor);
            if (clone == null) return;
            clone.transform.localPosition = Vector3.zero;
            clone.transform.localRotation = Quaternion.identity;
            clone.transform.localScale = Vector3.one;
            if (fallback == null) return;
            foreach (var go in fallback)
                if (go != null) go.SetActive(false);
        }
    }
}
