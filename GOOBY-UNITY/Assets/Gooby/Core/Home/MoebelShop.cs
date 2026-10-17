using System;
using System.Collections.Generic;
using System.Linq;
using Gooby.Core.Content;
using Gooby.Core.Json;
using Gooby.Core.Life;
using Gooby.Core.State;
using Newtonsoft.Json.Linq;

namespace Gooby.Core.Home
{
    /// <summary>
    /// Colour variants of furniture (port of furniture_variants.gd): not extra models but a recolour over the loaded
    /// model — the brightness of the original stays, the hue comes from the pastel palette. "natur" is the untouched
    /// original; catalog entries without their own list get natur + rosé + mint + sky blue.
    /// </summary>
    public static class FurnitureVariants
    {
        public const string DefaultId = "natur";
        /// <summary>The storage's own word for "no variant" (StorageLogic rows before the showroom).</summary>
        public const string StorageDefault = "default";
        public const double ReferenceLuminance = 0.62, Strength = 0.9;

        public static readonly IReadOnlyDictionary<string, (double r, double g, double b)> Palette = new Dictionary<string, (double, double, double)>
        {
            ["natur"] = (1.0, 1.0, 1.0), ["rose"] = (1.0, 0.835, 0.898), ["mint"] = (0.788, 0.941, 0.878),
            ["himmel"] = (0.812, 0.914, 0.961), ["butter"] = (1.0, 0.937, 0.761), ["lavendel"] = (0.886, 0.839, 0.961),
            ["salbei"] = (0.839, 0.898, 0.776), ["sand"] = (0.937, 0.878, 0.776), ["koralle"] = (1.0, 0.824, 0.761),
            ["schiefer"] = (0.835, 0.863, 0.886),
        };

        public static readonly string[] FallbackIds = { "natur", "rose", "mint", "himmel" };

        public static bool IsNeutral(string variant) => string.IsNullOrEmpty(variant) || variant == DefaultId || variant == StorageDefault || !Palette.ContainsKey(variant);

        /// <summary>The variants an item offers ("natur" first; unknown ids dropped).</summary>
        public static List<string> IdsFor(FurnitureDef def)
        {
            var outp = new List<string> { DefaultId };
            IEnumerable<string> raw = (def?.Raw?["variants"] as JArray)?.Select(v => J.Str(v)).ToList();
            if (raw == null || !raw.Any()) raw = FallbackIds;
            foreach (var id in raw)
                if (Palette.ContainsKey(id) && !outp.Contains(id)) outp.Add(id);
            return outp;
        }

        public static string Normalize(FurnitureDef def, string variant) => IdsFor(def).Contains(variant ?? "") ? variant : DefaultId;

        /// <summary>The recolour of one base colour (CPU mirror of furniture_variant.gdshader): keep the brightness, take the hue.</summary>
        public static (double r, double g, double b) Blend(double r, double g, double b, string variant)
        {
            if (IsNeutral(variant)) return (r, g, b);
            var t = Palette[variant];
            var lum = 0.2126 * r + 0.7152 * g + 0.0722 * b;
            var level = J.Clamp(lum / ReferenceLuminance, 0.35, 1.15);
            return (r + (t.r * level - r) * Strength, g + (t.g * level - g) * Strength, b + (t.b * level - b) * Strength);
        }
    }

    /// <summary>
    /// The GOOBEA furniture store (port of shop_catalog.gd + shop_purchase.gd): every catalog piece with a price is for
    /// sale (price 0 = workshop-only), filtered by category and by name (German, English or id), cheapest first. A
    /// purchase pays and puts the piece — in its colour — into the storage in one step, or does nothing.
    /// </summary>
    public static class MoebelShop
    {
        public const string Ok = "ok", Unbekannt = "unknown_item", Pleite = "not_enough_coins", Voll = "storage_full";

        public static bool Sellable(FurnitureDef def) => def != null && def.Preis > 0;

        public static List<string> Categories() => FurnitureCatalog.Defs().Values.Where(Sellable).Select(d => d.Kategorie).Distinct()
            .OrderBy(k => k, StringComparer.Ordinal).ToList();

        public static List<FurnitureDef> Filter(string query, string kategorie = "")
        {
            var needle = (query ?? "").Trim().ToLowerInvariant();
            return FurnitureCatalog.Defs().Values
                .Where(d => Sellable(d) && (string.IsNullOrEmpty(kategorie) || d.Kategorie == kategorie)
                    && (needle.Length == 0 || (d.NameDe ?? "").ToLowerInvariant().Contains(needle) || (d.NameEn ?? "").ToLowerInvariant().Contains(needle) || d.Id.ToLowerInvariant().Contains(needle)))
                .OrderBy(d => d.Preis).ThenBy(d => d.Id, StringComparer.Ordinal).ToList();
        }

        private static int Capacity(JObject state) => J.Int(J.Path(state, HomeState.SliceId, "storageCapacity"), 100);

        /// <summary>Free storage points, minus what the delivery van is still bringing.</summary>
        public static int StorageFree(JObject state)
        {
            var defs = HomeState.DefsAsJson();
            var used = StorageLogic.PointsUsed(J.Path(state, HomeState.SliceId, "storage") as JArray ?? new JArray(), defs);
            return Math.Max(0, Capacity(state) - used - Lieferung.PunkteUnterwegs(state, defs));
        }

        public static string Check(JObject state, string itemId)
        {
            var def = FurnitureCatalog.Def(itemId);
            if (state == null || !Sellable(def)) return Unbekannt;
            if (J.Int(J.Path(state, "economy", "coins")) < def.Preis) return Pleite;
            if (StorageLogic.ItemWeight(itemId, HomeState.DefsAsJson()) > StorageFree(state)) return Voll;
            return Ok;
        }

        /// <summary>
        /// Buys one piece in a colour (unknown colours fall back to natur) — it comes with the delivery van
        /// (<see cref="Lieferung"/>), then lands in the storage; only Ok has changed the state.
        /// </summary>
        public static string Kaufe(JObject state, string itemId, string variant = FurnitureVariants.DefaultId)
        {
            var reason = Check(state, itemId);
            if (reason != Ok) return reason;
            var def = FurnitureCatalog.Def(itemId);
            if (!Economy.Spend(J.EnsureDict(state, "economy"), def.Preis, "moebelhaus")) return Pleite;
            var v = FurnitureVariants.Normalize(def, variant);
            Lieferung.Bestellen(state, itemId, v == FurnitureVariants.DefaultId ? FurnitureVariants.StorageDefault : v);
            return Ok;
        }
    }
}
