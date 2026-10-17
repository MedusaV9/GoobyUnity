using System;
using System.Collections.Generic;
using System.Linq;
using Gooby.Core.Content;
using Gooby.Core.Json;
using Newtonsoft.Json.Linq;

namespace Gooby.Core.Garden
{
    public sealed class CropDef
    {
        public string Id, NameDe, NameEn, Licht, Wind, Food, Samen, Farbe;
        public int Stufen, MinutenProStufe, Preis, Ernte;
        public bool Exot;

        public string DisplayName(string locale) => locale == "en" ? NameEn : NameDe;
        public int TotalMinutes => Stufen * MinutenProStufe;
        public bool WindEmpfindlich => Wind == "empfindlich";
    }

    /// <summary>Crop catalog (home/garden_crops.json) — port of garden_crops.gd.</summary>
    public static class GardenCrops
    {
        public const string DataPath = "home/garden_crops";
        public static readonly string[] LichtArten = { "sonne", "schatten", "neutral" };
        public static readonly string[] WindArten = { "normal", "empfindlich" };

        private static Dictionary<string, CropDef> _crops;

        public static IReadOnlyDictionary<string, CropDef> All()
        {
            if (_crops != null) return _crops;
            _crops = new Dictionary<string, CropDef>();
            foreach (var t in ContentSource.LoadObject(DataPath)["crops"] as JArray ?? new JArray())
            {
                if (!(t is JObject o)) continue;
                var c = Normalize(o);
                if (c != null) _crops[c.Id] = c;
            }
            return _crops;
        }

        public static CropDef Crop(string id) => id != null && All().TryGetValue(id, out var c) ? c : null;

        public static List<string> Ids() => All().Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();

        public static List<CropDef> Plantable(bool imGewaechshaus) => Ids().Select(id => All()[id]).Where(c => !c.Exot || imGewaechshaus).ToList();

        public static int BasePrice(string id) => Crop(id)?.Preis ?? 0;
        public static int TotalMinutes(string id) => Crop(id)?.TotalMinutes ?? 0;
        public static string SamenItem(string id) => Crop(id)?.Samen ?? "";

        public static void ResetCache() => _crops = null;

        private static CropDef Normalize(JObject raw)
        {
            var id = J.Str(raw["id"]);
            if (id.Length == 0) return null;
            var licht = J.Str(raw["licht"], "neutral");
            if (Array.IndexOf(LichtArten, licht) < 0) licht = "neutral";
            var wind = J.Str(raw["wind"], "normal");
            if (Array.IndexOf(WindArten, wind) < 0) wind = "normal";
            return new CropDef
            {
                Id = id,
                NameDe = J.Str(raw["name_de"], id),
                NameEn = J.Str(raw["name_en"], J.Str(raw["name_de"], id)),
                Stufen = Math.Max(1, J.Int(raw["stufen"], 3)),
                MinutenProStufe = Math.Max(1, J.Int(raw["minuten_pro_stufe"], 60)),
                Licht = licht, Wind = wind,
                Preis = Math.Max(0, J.Int(raw["preis"])),
                Ernte = Math.Max(1, J.Int(raw["ernte"], 1)),
                Exot = J.IsTrue(raw["exot"]),
                Food = J.Str(raw["food"]),
                Samen = J.Str(raw["samen"]),
                Farbe = J.Str(raw["farbe"], "#8FD06C"),
            };
        }
    }
}
