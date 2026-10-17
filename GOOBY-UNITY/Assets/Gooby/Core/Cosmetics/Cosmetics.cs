using System;
using System.Collections.Generic;
using System.Linq;
using Gooby.Core.Content;
using Gooby.Core.Json;
using Gooby.Core.Life;
using Gooby.Core.State;
using Newtonsoft.Json.Linq;

namespace Gooby.Core.Cosmetics
{
    public sealed class CosmeticDef
    {
        public string Id, Kategorie, Slot, NameDe, NameEn, DescDe, DescEn, Rarity, Build;
        public int Preis, MinLevel = 1;
        public List<string> Farben = new List<string>();
        public JObject Params = new JObject();
        public bool Standard;

        public string Name(string locale) => locale == "en" ? NameEn : NameDe;
        public string Desc(string locale) => locale == "en" ? DescEn : DescDe;
    }

    /// <summary>Port of cosmetics_catalog.gd over content/cosmetics (93 items: hut/brille/hals/ruecken/fell).</summary>
    public static class CosmeticsCatalog
    {
        public const string DataPath = "content/cosmetics/data/cosmetics";
        public static readonly string[] Kategorien = { "hut", "brille", "hals", "ruecken", "fell" };
        public static readonly IReadOnlyDictionary<string, string> SlotByKategorie = new Dictionary<string, string> { { "hut", "hat" }, { "brille", "glasses" }, { "hals", "neck" }, { "ruecken", "back" } };
        public const string Fell = "fell";
        public static readonly string[] Rarities = { "haeufig", "selten", "episch", "legendaer" };
        public static readonly string[] NurImShop = { "fell" };
        private static readonly Dictionary<string, string> KategorieAlias = new Dictionary<string, string>
        {
            { "hat", "hut" }, { "hut", "hut" }, { "glasses", "brille" }, { "brille", "brille" }, { "neck", "hals" }, { "hals", "hals" },
            { "back", "ruecken" }, { "ruecken", "ruecken" }, { "fur", "fell" }, { "fell", "fell" },
        };

        private static List<CosmeticDef> _cache;

        public static IReadOnlyList<CosmeticDef> All()
        {
            if (_cache != null) return _cache;
            return _cache = NormalizeAll(ContentSource.LoadItems(DataPath));
        }

        public static void SetItems(JArray rawItems) => _cache = rawItems == null || rawItems.Count == 0 ? null : NormalizeAll(rawItems);
        public static void ResetCache() => _cache = null;

        public static List<CosmeticDef> ByKategorie(string kategorie) => All().Where(d => d.Kategorie == kategorie).ToList();
        public static CosmeticDef ById(string id) => All().FirstOrDefault(d => d.Id == id);
        public static bool Has(string id) => ById(id) != null;
        public static string KategorieOf(string id) => ById(id)?.Kategorie ?? "";
        public static string SlotOf(string id) => SlotByKategorie.TryGetValue(KategorieOf(id), out var s) ? s : "";
        public static List<string> StandardIds(string kategorie = "") => All().Where(d => d.Standard && (kategorie.Length == 0 || d.Kategorie == kategorie)).Select(d => d.Id).ToList();

        public static List<CosmeticDef> NormalizeAll(JArray raw)
        {
            var outp = new List<CosmeticDef>();
            var seen = new HashSet<string>();
            foreach (var t in raw)
            {
                var def = Normalize(t as JObject);
                if (def == null || !seen.Add(def.Id)) continue;
                outp.Add(def);
            }
            return outp;
        }

        private static string KategorieOfRaw(JObject item)
        {
            var raw = J.Str(item["kategorie"], J.Str(item["category"], J.Str(item["slot"])));
            return KategorieAlias.TryGetValue(raw, out var k) ? k : "";
        }

        public static CosmeticDef Normalize(JObject item)
        {
            if (item == null) return null;
            var id = J.Str(item["id"]).Trim();
            var kategorie = KategorieOfRaw(item);
            if (id.Length == 0 || kategorie.Length == 0) return null;
            var rarity = J.Str(item["rarity"], "haeufig");
            var nameDe = J.Str(item["name_de"], J.Str(item["name"], id));
            var descDe = J.Str(item["desc_de"], J.Str(item["desc"]));
            return new CosmeticDef
            {
                Id = id, Kategorie = kategorie, Slot = SlotByKategorie.TryGetValue(kategorie, out var s) ? s : Fell,
                NameDe = nameDe, NameEn = J.Str(item["name_en"], nameDe), DescDe = descDe, DescEn = J.Str(item["desc_en"], descDe),
                Preis = Math.Max(0, J.Int(item["preis"], J.Int(item["price"]))), Rarity = Array.IndexOf(Rarities, rarity) >= 0 ? rarity : "haeufig",
                MinLevel = Math.Max(1, J.Int(item["min_level"], J.Int(item["minLevel"], 1))), Build = J.Str(item["build"]),
                Farben = (item["farben"] as JArray ?? new JArray()).Select(v => v.ToString()).ToList(),
                Params = item["params"] as JObject ?? new JObject(), Standard = J.IsTrue(item["standard"]),
            };
        }

        public static List<string> Validate(JArray raw)
        {
            var errors = new List<string>();
            var seen = new HashSet<string>();
            foreach (var t in raw)
            {
                if (!(t is JObject item)) { errors.Add("Eintrag ist kein Objekt"); continue; }
                var id = J.Str(item["id"]).Trim();
                if (id.Length == 0) { errors.Add("Eintrag ohne id"); continue; }
                if (!seen.Add(id)) errors.Add($"{id}: doppelte id");
                var kategorie = KategorieOfRaw(item);
                if (kategorie.Length == 0) { errors.Add($"{id}: unbekannte Kategorie '{J.Str(item["kategorie"])}'"); continue; }
                foreach (var feld in new[] { "name_de", "name_en", "desc_de", "desc_en" })
                    if (J.Str(item[feld]).Trim().Length == 0) errors.Add($"{id}: Feld '{feld}' fehlt");
                if (Array.IndexOf(Rarities, J.Str(item["rarity"])) < 0) errors.Add($"{id}: unbekannte rarity '{J.Str(item["rarity"])}'");
                if (J.Int(item["preis"], J.Int(item["price"], -1)) < 0) errors.Add($"{id}: preis fehlt/negativ");
                if (J.Str(item["build"]).Trim().Length == 0) errors.Add($"{id}: build fehlt");
                if (kategorie == Fell && (!(item["farben"] is JArray f) || f.Count < 3)) errors.Add($"{id}: Fell braucht 3 Farben (body/bauch/ohr)");
            }
            return errors;
        }
    }

    public struct CosmeticResult
    {
        public bool Ok, Gekauft;
        public string Grund, Id, Kategorie, Vorher;
        public int Preis;
    }

    /// <summary>Port of cosmetics_state.gd — the cosmetics slice (outfits owned/equipped + fur) with buy/equip/toggle/prune.</summary>
    public static class CosmeticsState
    {
        public const string StandardFell = "cream";
        public static readonly string[] Slots = { "hat", "glasses", "neck", "back" };

        public static JObject DefaultSlice() => new JObject
        {
            ["outfits"] = new JObject { ["owned"] = new JArray(), ["equipped"] = new JObject { ["hat"] = JValue.CreateNull(), ["glasses"] = JValue.CreateNull(), ["neck"] = JValue.CreateNull(), ["back"] = JValue.CreateNull() } },
            ["fur"] = new JObject { ["owned"] = new JArray(StandardFell), ["equipped"] = StandardFell },
        };

        public static JObject Normalize(JToken slice)
        {
            var outp = slice as JObject ?? new JObject();
            if (!(outp["outfits"] is JObject)) outp["outfits"] = new JObject();
            var outfits = (JObject)outp["outfits"];
            if (!(outfits["owned"] is JArray)) outfits["owned"] = new JArray();
            if (!(outfits["equipped"] is JObject)) outfits["equipped"] = new JObject();
            var equipped = (JObject)outfits["equipped"];
            foreach (var slot in Slots) if (equipped[slot] == null) equipped[slot] = JValue.CreateNull();
            if (!(outp["fur"] is JObject)) outp["fur"] = new JObject();
            var fur = (JObject)outp["fur"];
            if (!(fur["owned"] is JArray)) fur["owned"] = new JArray();
            if (!((JArray)fur["owned"]).Any(t => t.ToString() == StandardFell)) ((JArray)fur["owned"]).Add(StandardFell);
            if (!J.IsString(fur["equipped"]) || fur["equipped"].Value<string>().Length == 0) fur["equipped"] = StandardFell;
            return outp;
        }

        private static bool Has(JArray arr, string id) => arr.Any(t => t.Type == JTokenType.String && t.Value<string>() == id);

        public static bool IsOwned(JObject slice, string id)
        {
            var def = CosmeticsCatalog.ById(id);
            if (def != null && def.Standard) return true;
            var data = Normalize(slice);
            if (CosmeticsCatalog.KategorieOf(id) == CosmeticsCatalog.Fell) return Has((JArray)data["fur"]["owned"], id);
            return Has((JArray)data["outfits"]["owned"], id);
        }

        public static List<string> Owned(JObject slice, string kategorie = "")
        {
            var data = Normalize(slice);
            var outp = new List<string>();
            foreach (var t in (JArray)data["outfits"]["owned"])
            {
                var id = t.ToString();
                if (kategorie.Length == 0 || CosmeticsCatalog.KategorieOf(id) == kategorie) outp.Add(id);
            }
            if (kategorie.Length == 0 || kategorie == CosmeticsCatalog.Fell)
                foreach (var t in (JArray)data["fur"]["owned"]) outp.Add(t.ToString());
            return outp;
        }

        public static string Equipped(JObject slice, string kategorie)
        {
            var data = Normalize(slice);
            if (kategorie == CosmeticsCatalog.Fell) return J.Str(data["fur"]["equipped"]);
            if (!CosmeticsCatalog.SlotByKategorie.TryGetValue(kategorie, out var slot)) return "";
            return J.Str(data["outfits"]["equipped"][slot]);
        }

        public static Dictionary<string, string> EquippedMap(JObject slice) => CosmeticsCatalog.Kategorien.ToDictionary(k => k, k => Equipped(slice, k));

        public static CosmeticResult Grant(JObject slice, string id, string quelle = "shop")
        {
            var def = CosmeticsCatalog.ById(id);
            if (def == null) return Nein(id, "unbekannt");
            if (Array.IndexOf(CosmeticsCatalog.NurImShop, def.Kategorie) >= 0 && quelle != "shop" && !def.Standard) return Nein(id, "nur_im_shop");
            var data = Normalize(slice);
            var liste = (JArray)(def.Kategorie == CosmeticsCatalog.Fell ? data["fur"]["owned"] : data["outfits"]["owned"]);
            if (Has(liste, id)) return Nein(id, "schon_besessen");
            liste.Add(id);
            return new CosmeticResult { Ok = true, Grund = "", Id = id, Kategorie = def.Kategorie };
        }

        public static CosmeticResult CanBuy(JObject slice, JObject econ, string id, int level = 1)
        {
            var def = CosmeticsCatalog.ById(id);
            if (def == null) return new CosmeticResult { Ok = false, Grund = "unbekannt", Preis = 0, Id = id };
            var preis = def.Preis;
            if (IsOwned(slice, id)) return new CosmeticResult { Ok = false, Grund = "schon_besessen", Preis = preis, Id = id };
            if (level < def.MinLevel) return new CosmeticResult { Ok = false, Grund = "level_zu_niedrig", Preis = preis, Id = id };
            if (!Economy.CanAfford(econ, preis)) return new CosmeticResult { Ok = false, Grund = "zu_teuer", Preis = preis, Id = id };
            return new CosmeticResult { Ok = true, Grund = "", Preis = preis, Id = id, Kategorie = def.Kategorie };
        }

        public static CosmeticResult Buy(JObject slice, JObject econ, string id, int level = 1)
        {
            var pruefung = CanBuy(slice, econ, id, level);
            pruefung.Gekauft = false;
            if (!pruefung.Ok) return pruefung;
            if (!Economy.Spend(econ, pruefung.Preis, "cosmetics"))
            {
                pruefung.Ok = false;
                pruefung.Grund = "zu_teuer";
                return pruefung;
            }
            Grant(slice, id, "shop");
            pruefung.Gekauft = true;
            return pruefung;
        }

        public static CosmeticResult Equip(JObject slice, string id)
        {
            var def = CosmeticsCatalog.ById(id);
            if (def == null) return Nein(id, "unbekannt");
            if (!IsOwned(slice, id)) return Nein(id, "nicht_besessen");
            var vorher = Equipped(slice, def.Kategorie);
            if (vorher == id) return new CosmeticResult { Ok = true, Grund = "unveraendert", Kategorie = def.Kategorie, Vorher = vorher, Id = id };
            var data = Normalize(slice);
            if (def.Kategorie == CosmeticsCatalog.Fell) data["fur"]["equipped"] = id;
            else data["outfits"]["equipped"][def.Slot] = id;
            return new CosmeticResult { Ok = true, Grund = "", Kategorie = def.Kategorie, Vorher = vorher, Id = id };
        }

        public static CosmeticResult Unequip(JObject slice, string kategorie)
        {
            var vorher = Equipped(slice, kategorie);
            var data = Normalize(slice);
            if (kategorie == CosmeticsCatalog.Fell)
            {
                if (vorher == StandardFellId()) return new CosmeticResult { Ok = false, Grund = "unveraendert", Kategorie = kategorie, Vorher = vorher };
                data["fur"]["equipped"] = StandardFellId();
                return new CosmeticResult { Ok = true, Grund = "", Kategorie = kategorie, Vorher = vorher };
            }
            if (!CosmeticsCatalog.SlotByKategorie.TryGetValue(kategorie, out var slot)) return new CosmeticResult { Ok = false, Grund = "unbekannt", Kategorie = kategorie, Vorher = "" };
            if (vorher.Length == 0) return new CosmeticResult { Ok = false, Grund = "unveraendert", Kategorie = kategorie, Vorher = "" };
            data["outfits"]["equipped"][slot] = JValue.CreateNull();
            return new CosmeticResult { Ok = true, Grund = "", Kategorie = kategorie, Vorher = vorher };
        }

        public static CosmeticResult Toggle(JObject slice, string id)
        {
            var kategorie = CosmeticsCatalog.KategorieOf(id);
            if (kategorie.Length > 0 && Equipped(slice, kategorie) == id) return Unequip(slice, kategorie);
            return Equip(slice, id);
        }

        /// <summary>Removes equipped ids that no longer exist in the catalog (pack update); returns the cleared categories.</summary>
        public static List<string> PruneEquipped(JObject slice)
        {
            var geraeumt = new List<string>();
            foreach (var kategorie in CosmeticsCatalog.Kategorien)
            {
                var id = Equipped(slice, kategorie);
                if (id.Length == 0 || CosmeticsCatalog.Has(id)) continue;
                var data = Normalize(slice);
                if (kategorie == CosmeticsCatalog.Fell) data["fur"]["equipped"] = StandardFellId();
                else data["outfits"]["equipped"][CosmeticsCatalog.SlotByKategorie[kategorie]] = JValue.CreateNull();
                geraeumt.Add(kategorie);
            }
            return geraeumt;
        }

        public static int SlotsFilled(JObject slice)
        {
            var equipped = (JObject)Normalize(slice)["outfits"]["equipped"];
            return Slots.Count(slot => J.IsString(equipped[slot]) && equipped[slot].Value<string>().Length > 0);
        }

        /// <summary>Runs a mutator over (cosmetics, economy) inside a store update and notifies the slice.</summary>
        public static T ApplyToState<T>(GameStateStore gs, Func<JObject, JObject, T> mutator)
        {
            var result = default(T);
            gs.Update(state =>
            {
                if (!(state["cosmetics"] is JObject)) state["cosmetics"] = DefaultSlice();
                var econ = J.EnsureDict(state, "economy");
                result = mutator(Normalize(state["cosmetics"]), econ);
            });
            gs.NotifySliceChanged("cosmetics");
            return result;
        }

        private static string StandardFellId()
        {
            var standards = CosmeticsCatalog.StandardIds(CosmeticsCatalog.Fell);
            return standards.Count > 0 ? standards[0] : StandardFell;
        }

        private static CosmeticResult Nein(string id, string grund) => new CosmeticResult { Ok = false, Grund = grund, Id = id, Kategorie = CosmeticsCatalog.KategorieOf(id), Vorher = "" };
    }
}
