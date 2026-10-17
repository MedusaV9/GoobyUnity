using UnityEngine;

namespace Gooby.Unity.World
{
    /// <summary>
    /// Recolours named Blender materials on a streamed GLB (the vet lounge sofa: gb_rose / gb_pfirsich → mint).
    /// Same settle-loop as CarPaint — the prop clone appears a frame or two after Prop() returns.
    /// </summary>
    public sealed class PropTint : MonoBehaviour
    {
        public Color Body = Color.white;
        public string[] Prefixes = { "pbr:fabric", "gb_rose", "gb_pfirsich" };

        private int _painted = -1;
        private int _settle;

        public void Apply()
        {
            var renderers = GetComponentsInChildren<Renderer>(true);
            foreach (var r in renderers)
            {
                var mats = r.materials;
                for (var i = 0; i < mats.Length; i++)
                {
                    var name = mats[i].name ?? "";
                    var hit = false;
                    if (Prefixes != null)
                        foreach (var p in Prefixes)
                            if (!string.IsNullOrEmpty(p) && name.IndexOf(p, System.StringComparison.OrdinalIgnoreCase) >= 0) { hit = true; break; }
                    if (!hit) continue;
                    var c = Body;
                    c.a = 1f;
                    if (mats[i].HasProperty("_BaseColor")) mats[i].SetColor("_BaseColor", c);
                    if (mats[i].HasProperty("baseColorFactor")) mats[i].SetColor("baseColorFactor", c);
                    mats[i].color = c;
                }
            }
            _painted = renderers.Length;
        }

        private void LateUpdate()
        {
            if (_painted > 0 && (++_settle % 30) != 0) return;
            if (GetComponentsInChildren<Renderer>(true).Length != _painted) Apply();
        }
    }
}
