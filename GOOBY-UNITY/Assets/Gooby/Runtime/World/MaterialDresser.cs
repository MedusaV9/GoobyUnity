using System.Collections.Generic;
using UnityEngine;

namespace Gooby.Unity.World
{
    /// <summary>
    /// Gives the Blender models their surfaces: every glTF material of a prop/scenery template is matched by its
    /// palette name (tools/blender/build_props.py PALETTE → "gb_holz", "gb_dachbraun", "gb_erdbeer" …) and swapped for
    /// an <see cref="ArtLibrary.Pbr"/> material with the same colour plus the fitting CC0 grain/normal map: plank
    /// grain on wood, clay relief on roofs, plaster on walls, cloth weave on cushions, leafy detail on crowns, gloss
    /// on glass, paint and metal. The models keep their pastel colours — the textures only add the relief the
    /// concept boards show up close. Runs once per template (AssetLoader), so clones share the materials.
    /// </summary>
    public static class MaterialDresser
    {
        private struct Rule
        {
            public string Surface;      // pbr texture name (null = colour only)
            public float MetresPerTile;
            public float Smoothness;
            public float NormalScale;
            public Rule(string surface, float metres, float smooth, float normal) { Surface = surface; MetresPerTile = metres; Smoothness = smooth; NormalScale = normal; }
        }

        // prefix (after "gb_") → surface; longer prefixes win
        private static readonly (string prefix, Rule rule)[] Rules =
        {
            ("dach", new Rule("roof_tiles", 1.1f, 0.12f, 1.0f)),
            ("haus", new Rule("plaster", 2.6f, 0.08f, 0.6f)),
            ("holz", new Rule("wood_planks", 1.3f, 0.2f, 0.7f)),
            ("tuer", new Rule("wood_planks", 1.0f, 0.22f, 0.7f)),
            ("stamm", new Rule("wood_planks", 0.8f, 0.1f, 0.9f)),
            ("palisade", new Rule("wood_planks", 1.0f, 0.12f, 0.8f)),
            ("zaunweiss", new Rule("wood_planks", 1.0f, 0.18f, 0.5f)),
            ("schild", new Rule("wood_planks", 1.2f, 0.16f, 0.7f)),
            ("tafel", new Rule("plaster", 1.0f, 0.1f, 0.4f)),
            ("stein", new Rule("concrete", 1.2f, 0.1f, 0.7f)),
            ("kies", new Rule("concrete", 1.0f, 0.08f, 0.8f)),
            ("stufe", new Rule("concrete", 1.2f, 0.1f, 0.7f)),
            ("schornstein", new Rule("plaster", 1.0f, 0.08f, 0.6f)),
            ("erde", new Rule("concrete", 0.8f, 0.02f, 1.0f)),
            ("laub", new Rule("leaves", 1.4f, 0.04f, 1.0f)),
            ("bluete", new Rule(null, 0f, 0.15f, 0f)),
            ("markise", new Rule("fabric", 0.6f, 0.06f, 0.6f)),
            ("banner", new Rule("fabric", 0.6f, 0.06f, 0.6f)),
            ("erdbeer", new Rule("fabric", 0.55f, 0.06f, 0.8f)),
            ("rose", new Rule("fabric", 0.55f, 0.06f, 0.8f)),
            ("pfirsich", new Rule("fabric", 0.55f, 0.06f, 0.8f)),
            ("creme", new Rule("fabric", 0.6f, 0.08f, 0.6f)),
            ("blatt", new Rule("fabric", 0.55f, 0.06f, 0.7f)),
            ("himmel", new Rule("fabric", 0.55f, 0.06f, 0.7f)),
            ("mint", new Rule("fabric", 0.55f, 0.08f, 0.6f)),
            ("sand", new Rule("plaster", 1.6f, 0.1f, 0.5f)),
            ("taupe", new Rule("plaster", 1.6f, 0.1f, 0.5f)),
            ("glas", new Rule(null, 0f, 0.92f, 0f)),
            ("lack", new Rule(null, 0f, 0.78f, 0f)),
            ("chrom", new Rule(null, 0f, 0.9f, 0f)),
            ("felge", new Rule(null, 0f, 0.7f, 0f)),
            ("metall", new Rule(null, 0f, 0.65f, 0f)),
            ("laterne", new Rule(null, 0f, 0.5f, 0f)),
            ("kupfer", new Rule(null, 0f, 0.55f, 0f)),
            ("scheinwerfer", new Rule(null, 0f, 0.9f, 0f)),
            ("ruecklicht", new Rule(null, 0f, 0.8f, 0f)),
            ("rueckfahrlicht", new Rule(null, 0f, 0.8f, 0f)),
            ("reifen", new Rule("fabric", 0.4f, 0.12f, 0.5f)),
            ("waesche", new Rule("fabric", 0.45f, 0.06f, 0.7f)),
            // garden animals: plain lit colours (glTFast's own material renders these pastels far too dark)
            ("katze", new Rule(null, 0f, 0.12f, 0f)),
            ("hase", new Rule(null, 0f, 0.12f, 0f)),
            ("vogel", new Rule(null, 0f, 0.15f, 0f)),
            ("falter", new Rule(null, 0f, 0.2f, 0f)),
            // the album's goldfish (build_props album_goldfischglas): a glossy plain orange
            ("fisch", new Rule(null, 0f, 0.45f, 0f)),
        };

        private static readonly Dictionary<string, Material> Cache = new Dictionary<string, Material>();

        /// <summary>Kinds of templates: the character keeps its painted palette texture untouched.</summary>
        public enum Kind { Prop, Scenery, Vehicle, Character }

        public static Kind KindOf(string path)
        {
            var p = path.Replace('\\', '/');
            if (p.Contains("/character/")) return Kind.Character;
            if (p.Contains("/vehicles/")) return Kind.Vehicle;
            if (p.Contains("/scenery/")) return Kind.Scenery;
            return Kind.Prop;
        }

        public static void Dress(GameObject template, Kind kind)
        {
            if (template == null || kind == Kind.Character) return;
            foreach (var r in template.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                var changed = false;
                for (var i = 0; i < mats.Length; i++)
                {
                    var replaced = Replace(mats[i], kind);
                    if (replaced == null || replaced == mats[i]) continue;
                    mats[i] = replaced;
                    changed = true;
                }
                if (changed) r.sharedMaterials = mats;
            }
        }

        private static Material Replace(Material source, Kind kind)
        {
            if (source == null) return null;
            var name = source.name;
            if (!name.StartsWith("gb_")) return null;
            var palette = name.Substring(3);
            var dot = palette.IndexOf(" (Instance)", System.StringComparison.Ordinal);
            if (dot >= 0) palette = palette.Substring(0, dot);
            // board 10 interior models (build_scenery.py cooler/podium/counter): light strips and screens glow, the
            // cooler doors are clear glass — neither is a surface texture (keys stay clear of the gb_glas night glow)
            if (palette.StartsWith("leucht") || palette.StartsWith("vitrine"))
            {
                var c = BaseColor(source);
                var special = name + ":" + (palette.StartsWith("leucht") ? "glow" : "clear") + ":" + ColorUtility.ToHtmlStringRGB(c);
                if (!Cache.TryGetValue(special, out var sm))
                {
                    // strips bloom like the ceiling spots; screens (leuchtmint) only just lift off the bloom threshold
                    var gain = palette.StartsWith("leuchtmint") ? 1.15f : ArtLibrary.GlowPeak;
                    sm = palette.StartsWith("leucht")
                        ? ArtLibrary.Glow(new Color(c.r, c.g, c.b) * gain)
                        : ArtLibrary.Transparent(new Color(c.r, c.g, c.b, 0.2f));
                    Cache[special] = sm;
                }
                return sm;
            }
            Rule? best = null; var bestLen = -1;
            foreach (var (prefix, candidate) in Rules)
                if (palette.StartsWith(prefix) && prefix.Length > bestLen) { best = candidate; bestLen = prefix.Length; }
            if (best == null) return null;
            // furniture fabric rules only make sense on props; on scenery those palette colours are painted wood/plaster
            var rule = best.Value;
            if (kind != Kind.Prop && rule.Surface == "fabric" && (palette == "erdbeer" || palette == "rose" || palette == "pfirsich" || palette == "creme" || palette == "blatt" || palette == "himmel" || palette == "mint"))
                rule = new Rule("plaster", 1.6f, 0.1f, 0.4f);
            var color = BaseColor(source);
            // the crowns of board 03 are a deeper, more saturated green than the palette's pastel leaf colours
            if (kind == Kind.Scenery && palette.StartsWith("laub")) color = new Color(color.r * 0.86f, color.g * 0.93f, color.b * 0.80f, color.a);
            var key = name + ":" + (rule.Surface ?? "-") + ":" + ColorUtility.ToHtmlStringRGBA(color) + ":" + kind;
            if (Cache.TryGetValue(key, out var cached)) return cached;
            Material m;
            if (rule.Surface == null) m = ArtLibrary.Solid(color, rule.Smoothness);
            else m = ArtLibrary.Pbr(rule.Surface, rule.MetresPerTile, color, rule.Smoothness, rule.NormalScale);
            // CarPaint finds the body panels by their palette name
            if (kind == Kind.Vehicle) { m = new Material(m) { name = name }; }
            Cache[key] = m;
            ApplyWindowGlow(key, m);   // a template imported after nightfall joins the lit town
            return m;
        }

        private static bool _windowsLit;

        /// <summary>
        /// Night in Sonnental: every window pane of the scenery models (palette "glas*", shared by all clones of all
        /// houses) glows warm; by day the emission is off again. Vehicles' and props' glass stays as it is.
        /// </summary>
        public static void SetWindowsLit(bool on)
        {
            _windowsLit = on;
            foreach (var kv in Cache) ApplyWindowGlow(kv.Key, kv.Value);
        }

        private static readonly Color WindowGlow = new Color(1f, 0.8f, 0.5f) * 1.5f;

        private static void ApplyWindowGlow(string key, Material m)
        {
            if (m == null || !key.StartsWith("gb_glas") || !key.EndsWith(":" + Kind.Scenery)) return;
            if (_windowsLit) { m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", WindowGlow); }
            else { m.SetColor("_EmissionColor", Color.black); m.DisableKeyword("_EMISSION"); }
        }

        /// <summary>glTFast (URP shader graph) keeps the colour in baseColorFactor; URP Lit in _BaseColor.</summary>
        public static Color BaseColor(Material m)
        {
            if (m.HasProperty("baseColorFactor")) return m.GetColor("baseColorFactor");
            if (m.HasProperty("_BaseColor")) return m.GetColor("_BaseColor");
            return m.HasProperty("_Color") ? m.color : Color.white;
        }
    }
}
