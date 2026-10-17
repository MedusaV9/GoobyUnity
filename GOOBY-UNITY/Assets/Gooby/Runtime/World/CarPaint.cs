using UnityEngine;

namespace Gooby.Unity.World
{
    /// <summary>
    /// Body paint of a hero-car instance (Autohaus Blechbert): recolours the Blender model's "gb_lack" /
    /// "gb_lackdunkel" materials. The GLB streams in asynchronously, so the paint is re-applied whenever new
    /// renderers appear under the car.
    /// </summary>
    public sealed class CarPaint : MonoBehaviour
    {
        public Color Body = new Color(0.886f, 0.298f, 0.298f);

        private int _painted = -1;

        public void Apply()
        {
            var renderers = GetComponentsInChildren<Renderer>(true);
            foreach (var r in renderers)
            {
                var mats = r.materials; // instances — only this car changes colour
                for (var i = 0; i < mats.Length; i++)
                {
                    var name = mats[i].name;
                    if (!name.StartsWith("gb_lack")) continue;
                    var c = name.StartsWith("gb_lackdunkel") ? Body * 0.82f : Body;
                    c.a = 1f;
                    if (mats[i].HasProperty("_BaseColor")) mats[i].SetColor("_BaseColor", c);
                    if (mats[i].HasProperty("baseColorFactor")) mats[i].SetColor("baseColorFactor", c);
                    mats[i].color = c;
                }
            }
            _painted = renderers.Length;
        }

        private int _settle;

        private void LateUpdate()
        {
            // the GLB streams in within the first seconds; once painted, look for new renderers only now and then
            // (two dozen ambient cars must not walk their hierarchies every frame)
            if (_painted > 0 && (++_settle % 30) != 0) return;
            if (GetComponentsInChildren<Renderer>(true).Length != _painted) Apply();
        }
    }
}
