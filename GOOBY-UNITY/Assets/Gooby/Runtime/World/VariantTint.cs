using System.Runtime.CompilerServices;
using Gooby.Core.Home;
using UnityEngine;

namespace Gooby.Unity.World
{
    /// <summary>
    /// Lays a furniture colour (<see cref="FurnitureVariants"/>) over a loaded model: every material keeps its brightness
    /// and takes the variant's pastel hue (the CPU mirror of Godot's furniture_variant.gdshader). Each renderer gets its
    /// own material copies, the originals stay untouched; the original colours are remembered so switching back to
    /// "natur" in the showroom restores them.
    /// </summary>
    public static class VariantTint
    {
        private static readonly ConditionalWeakTable<Renderer, Color[]> Originals = new ConditionalWeakTable<Renderer, Color[]>();

        public static void Apply(GameObject root, string variant)
        {
            if (root == null) return;
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                if (r is ParticleSystemRenderer) continue;
                var known = Originals.TryGetValue(r, out var colors);
                if (!known && FurnitureVariants.IsNeutral(variant)) continue;
                var mats = r.materials;
                if (!known)
                {
                    colors = new Color[mats.Length];
                    for (var i = 0; i < mats.Length; i++) colors[i] = ColorOf(mats[i]);
                    Originals.Add(r, colors);
                }
                for (var i = 0; i < mats.Length && i < colors.Length; i++)
                {
                    var c = colors[i];
                    var (rr, gg, bb) = FurnitureVariants.Blend(c.r, c.g, c.b, variant);
                    SetColor(mats[i], new Color((float)rr, (float)gg, (float)bb, c.a));
                }
                r.materials = mats;
            }
        }

        private static Color ColorOf(Material m)
        {
            if (m == null) return Color.white;
            if (m.HasProperty("_BaseColor")) return m.GetColor("_BaseColor");
            return m.HasProperty("_Color") ? m.color : Color.white;
        }

        private static void SetColor(Material m, Color c)
        {
            if (m == null) return;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.color = c;
        }
    }
}
