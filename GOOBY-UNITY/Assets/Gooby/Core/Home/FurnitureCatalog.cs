using System;
using System.Collections.Generic;
using System.Linq;
using Gooby.Core.Content;
using Gooby.Core.Json;
using Newtonsoft.Json.Linq;

namespace Gooby.Core.Home
{
    /// <summary>Normalised furniture definition (home/furniture_catalog.json item) — port of furniture_catalog.gd _normalize_def.</summary>
    public sealed class FurnitureDef
    {
        public string Id, NameDe, NameEn, Glb, Pflicht, Kategorie, Vista, Proc;
        public int FootprintW = 1, FootprintH = 1, Preis, Lagerwert = 1, WallSize = 1, Verkaufswert;
        public GridLayer Layer;
        public bool Surface, BlocksMovement, CanToggleLight, Exterior;
        public double Fill = FurnitureCatalog.DefaultFill;
        public JObject Raw;

        public string DisplayName(string locale) => locale == "en" ? NameEn : NameDe;
    }

    public static class FurnitureCatalog
    {
        public const string CatalogPath = "home/furniture_catalog";
        public static readonly string[] MandatorySlots = { "bett", "couch", "kuehlschrank" };
        public const double DefaultFill = 0.94;

        private static Dictionary<string, FurnitureDef> _defs;

        public static IReadOnlyDictionary<string, FurnitureDef> Defs()
        {
            if (_defs != null) return _defs;
            _defs = new Dictionary<string, FurnitureDef>();
            foreach (var t in ContentSource.LoadItems(CatalogPath))
            {
                if (!(t is JObject o)) continue;
                var def = Normalize(o);
                if (def != null) _defs[def.Id] = def;
            }
            return _defs;
        }

        public static FurnitureDef Def(string itemId) => itemId != null && Defs().TryGetValue(itemId, out var d) ? d : null;

        public static List<string> Ids() => Defs().Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();

        public static List<FurnitureDef> ByCategory(string kategorie) => Ids().Select(id => Defs()[id]).Where(d => d.Kategorie == kategorie).ToList();

        public static Dictionary<string, int> MandatoryCounts(JArray items)
        {
            var counts = MandatorySlots.ToDictionary(s => s, _ => 0);
            foreach (var entry in items ?? new JArray())
            {
                if (!(entry is JObject e)) continue;
                var slot = Def(J.Str(e["item"]))?.Pflicht ?? "";
                if (slot.Length > 0) counts[slot] = (counts.TryGetValue(slot, out var n) ? n : 0) + 1;
            }
            return counts;
        }

        public static bool IsLastOfMandatorySlot(JArray items, string uid)
        {
            FurnitureDef target = null;
            foreach (var entry in items ?? new JArray())
                if (entry is JObject e && J.Str(e["uid"]) == uid) { target = Def(J.Str(e["item"])); break; }
            var slot = target?.Pflicht ?? "";
            if (slot.Length == 0) return false;
            return MandatoryCounts(items).TryGetValue(slot, out var n) && n <= 1;
        }

        public static void ResetCache() => _defs = null;

        private static GridLayer? LayerOf(string name)
        {
            switch (name)
            {
                case "RUG": return GridLayer.Rug;
                case "FLOOR": return GridLayer.Floor;
                case "SURFACE": return GridLayer.Surface;
                case "WALL": return GridLayer.Wall;
                case "CEILING": return GridLayer.Ceiling;
            }
            return null;
        }

        private static FurnitureDef Normalize(JObject raw)
        {
            var id = J.Str(raw["id"]);
            var layer = LayerOf(J.Str(raw["layer"]));
            if (id.Length == 0 || layer == null) return null;
            var fp = raw["footprint"] as JArray;
            var fw = Math.Max(1, fp != null && fp.Count > 0 ? J.Int(fp[0], 1) : 1);
            var fh = Math.Max(1, fp != null && fp.Count > 1 ? J.Int(fp[1], 1) : 1);
            var blocksDefault = layer == GridLayer.Floor;
            return new FurnitureDef
            {
                Id = id,
                NameDe = J.Str(raw["name_de"], id),
                NameEn = J.Str(raw["name_en"], J.Str(raw["name_de"], id)),
                Glb = J.Str(raw["glb"]),
                FootprintW = fw, FootprintH = fh,
                Layer = layer.Value,
                Preis = Math.Max(0, J.Int(raw["preis"])),
                Lagerwert = J.ClampI(J.Int(raw["lagerwert"], 1), 1, 4),
                Pflicht = J.Str(raw["pflicht"]),
                Kategorie = J.Str(raw["kategorie"], "deko"),
                Surface = J.IsTrue(raw["surface"]),
                BlocksMovement = raw["blocks_movement"]?.Type == JTokenType.Boolean ? raw["blocks_movement"].Value<bool>() : blocksDefault,
                CanToggleLight = J.IsTrue(raw["can_toggle_light"]),
                WallSize = Math.Max(1, J.Int(raw["wall_size"], fw)),
                Fill = Math.Min(1.0, Math.Max(0.5, J.NumOr(raw["fill"], DefaultFill))),
                Verkaufswert = Math.Max(0, J.Int(raw["verkaufswert"])),
                Exterior = J.IsTrue(raw["exterior"]),
                Vista = J.Str(raw["vista"]),
                Proc = J.Str(raw["proc"]),
                Raw = raw,
            };
        }
    }

    /// <summary>Shed (storage) upgrade tiers — port of shed_logic.gd.</summary>
    public static class ShedLogic
    {
        public const int BasisKapazitaet = 100;
        public const int MaxStufe = 3;
        public static readonly int[] Bonus = { 0, 50, 100, 200 };
        public static readonly int[] Preise = { 0, 500, 1500, 4000 };

        public static int ClampStufe(int stufe) => J.ClampI(stufe, 0, MaxStufe);
        public static int Kapazitaet(int stufe) => BasisKapazitaet + Bonus[ClampStufe(stufe)];

        public static int UpgradePreis(int stufe)
        {
            var ziel = ClampStufe(stufe) + 1;
            return ziel > MaxStufe ? 0 : Preise[ziel];
        }

        public static bool KannUpgraden(int stufe, int muenzen)
        {
            var preis = UpgradePreis(stufe);
            return preis > 0 && muenzen >= preis;
        }
    }
}
