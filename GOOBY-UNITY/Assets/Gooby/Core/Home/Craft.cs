using System;
using System.Collections.Generic;
using System.Linq;
using Gooby.Core.City;
using Gooby.Core.Content;
using Gooby.Core.Garden;
using Gooby.Core.Json;
using Gooby.Core.State;
using Newtonsoft.Json.Linq;

namespace Gooby.Core.Home
{
    /// <summary>A workshop material (home/materials.json): sticks and leaves from the garden, wood from the tree, the rest from Bodo's Baumarkt.</summary>
    public sealed class CraftMaterial
    {
        public string Id, NameDe, NameEn, Quelle, Icon;
        public int Preis;

        public string Name(string locale) => locale == "en" ? NameEn : NameDe;
    }

    /// <summary>A workbench recipe (home/craft_recipes.json): materials in, furniture into storage.</summary>
    public sealed class CraftRecipe
    {
        public string Id, Item, Station, Bauplan;
        public int Count = 1;
        public double CraftSek = 3.0;
        /// <summary>Needed materials in file order (the card lists them like that).</summary>
        public List<KeyValuePair<string, int>> Materialien = new List<KeyValuePair<string, int>>();
    }

    /// <summary>Recipe and material catalogue — port of craft_recipes.gd / craft_materials.gd (base pack only).</summary>
    public static class CraftCatalog
    {
        public const string RecipesPath = "home/craft_recipes";
        public const string MaterialsPath = "home/materials";
        public const string Werkbank = "werkbank";
        public static readonly string[] Quellen = { "garten-spawn", "baum-ernte", "baumarkt" };

        private static List<CraftMaterial> _materials;
        private static List<CraftRecipe> _recipes;

        public static IReadOnlyList<CraftMaterial> Materials()
        {
            if (_materials != null) return _materials;
            _materials = new List<CraftMaterial>();
            foreach (var t in ContentSource.LoadObject(MaterialsPath)["materials"] as JArray ?? new JArray())
            {
                if (!(t is JObject o) || J.Str(o["id"]).Length == 0) continue;
                var id = J.Str(o["id"]);
                var quelle = J.Str(o["quelle"], "garten-spawn");
                if (Array.IndexOf(Quellen, quelle) < 0) quelle = "garten-spawn";
                _materials.Add(new CraftMaterial
                {
                    Id = id, NameDe = J.Str(o["name_de"], id), NameEn = J.Str(o["name_en"], J.Str(o["name_de"], id)),
                    Quelle = quelle, Preis = Math.Max(0, J.Int(o["preis"])), Icon = J.Str(o["icon"], id),
                });
            }
            return _materials;
        }

        public static CraftMaterial Material(string id) => Materials().FirstOrDefault(m => m.Id == id);

        /// <summary>Every recipe in file order — simple fence first, blueprint pieces later.</summary>
        public static IReadOnlyList<CraftRecipe> Recipes()
        {
            if (_recipes != null) return _recipes;
            _recipes = new List<CraftRecipe>();
            foreach (var t in ContentSource.LoadObject(RecipesPath)["recipes"] as JArray ?? new JArray())
            {
                if (!(t is JObject o)) continue;
                var id = J.Str(o["id"]);
                var output = o["output"] as JObject;
                if (id.Length == 0 || output == null || J.Str(output["item"]).Length == 0) continue;
                var r = new CraftRecipe
                {
                    Id = id, Item = J.Str(output["item"]), Count = Math.Max(1, J.Int(output["count"], 1)),
                    Station = J.Str(o["station"], Werkbank), Bauplan = o["bauplan"] == null || o["bauplan"].Type == JTokenType.Null ? "" : J.Str(o["bauplan"]),
                    CraftSek = Math.Max(0.0, J.NumOr(o["craft_sek"], 3.0)),
                };
                if (o["materialien"] is JObject mats)
                    foreach (var p in mats.Properties())
                    {
                        var n = Math.Max(0, J.Int(p.Value));
                        if (n > 0) r.Materialien.Add(new KeyValuePair<string, int>(p.Name, n));
                    }
                _recipes.Add(r);
            }
            return _recipes;
        }

        public static CraftRecipe Recipe(string id) => Recipes().FirstOrDefault(r => r.Id == id);

        public static List<CraftRecipe> ForStation(string station = Werkbank) => Recipes().Where(r => r.Station == station).ToList();

        public static void ResetCache()
        {
            _materials = null;
            _recipes = null;
        }
    }

    /// <summary>Crafting rules over a material inventory {id: count} — port of craft_logic.gd (pure, never costs energy).</summary>
    public static class CraftLogic
    {
        public const string ReasonOk = "";
        public const string ReasonUnknown = "unknown_recipe";
        public const string ReasonStation = "no_station";
        public const string ReasonBlueprint = "missing_blueprint";
        public const string ReasonMaterial = "missing_material";
        public const string ReasonStorage = "storage_full";

        public sealed class Check
        {
            public bool Ok;
            public string Reason = ReasonOk;
            public Dictionary<string, int> Missing = new Dictionary<string, int>();
        }

        public static int CountOf(JObject inventory, string id) => Math.Max(0, J.Int(inventory?[id]));

        public static Dictionary<string, int> MissingMaterials(CraftRecipe recipe, JObject inventory)
        {
            var missing = new Dictionary<string, int>();
            if (recipe == null) return missing;
            foreach (var kv in recipe.Materialien)
            {
                var lack = kv.Value - CountOf(inventory, kv.Key);
                if (lack > 0) missing[kv.Key] = lack;
            }
            return missing;
        }

        /// <summary>Full check: workshop, blueprint, materials, then room in storage for every output piece.</summary>
        public static Check Pruefen(CraftRecipe recipe, JObject inventory, ICollection<string> blueprints, bool werkstattGebaut, int freiLagerpunkte = 999, int lagerwert = 1)
        {
            if (recipe == null) return new Check { Reason = ReasonUnknown };
            if (!werkstattGebaut) return new Check { Reason = ReasonStation };
            if (recipe.Bauplan.Length > 0 && (blueprints == null || !blueprints.Contains(recipe.Bauplan))) return new Check { Reason = ReasonBlueprint };
            var missing = MissingMaterials(recipe, inventory);
            if (missing.Count > 0) return new Check { Reason = ReasonMaterial, Missing = missing };
            if (recipe.Count * Math.Max(1, lagerwert) > freiLagerpunkte) return new Check { Reason = ReasonStorage };
            return new Check { Ok = true };
        }

        /// <summary>Takes the recipe's materials (all or nothing — a refused craft never eats anything).</summary>
        public static bool Consume(JObject inventory, CraftRecipe recipe)
        {
            if (inventory == null || recipe == null || MissingMaterials(recipe, inventory).Count > 0) return false;
            foreach (var kv in recipe.Materialien) Take(inventory, kv.Key, kv.Value);
            return true;
        }

        public static void Add(JObject inventory, string id, int amount = 1)
        {
            if (inventory == null || amount <= 0) return;
            inventory[id] = CountOf(inventory, id) + amount;
        }

        public static bool Take(JObject inventory, string id, int amount = 1)
        {
            var have = CountOf(inventory, id);
            if (inventory == null || amount <= 0 || have < amount) return false;
            if (have == amount) inventory.Remove(id);
            else inventory[id] = have - amount;
            return true;
        }
    }

    /// <summary>
    /// Workshop save binding — port of craft_state.gd: home.materials (material stock), home.blueprints, the workshop
    /// as a garden structure. Bodo's Baumarkt sells into inventory.items (boards, sticks, nails …, blueprint flags);
    /// the workbench takes those packs over into its own stock and reads the bought blueprints as unlocked recipes.
    /// </summary>
    public static class CraftState
    {
        /// <summary>Baumarkt item (inventory.items) → workshop material; other items stay where they are.</summary>
        public static readonly IReadOnlyDictionary<string, string> BaumarktMaterialMap = new Dictionary<string, string>
        {
            { "bretter", "holz" }, { "stoecke", "stock" }, { "naegel", "naegel" }, { "eisen", "eisen" }, { "saatgut", "saatgut" },
        };

        public static JObject Materials(GameStateStore gs) => J.Path(gs?.State, "home", "materials") as JObject ?? new JObject();

        public static int MaterialCount(GameStateStore gs, string id) => CraftLogic.CountOf(Materials(gs), id);

        public static void AddMaterial(GameStateStore gs, string id, int menge = 1)
        {
            if (gs == null || menge <= 0) return;
            gs.Update(state => CraftLogic.Add(J.EnsureDict(J.EnsureDict(state, "home"), "materials"), id, menge));
            gs.NotifySliceChanged("home");
        }

        /// <summary>Own blueprints (home.blueprints) plus "bp_&lt;werkstatt_id&gt;" for every blueprint bought at the Baumarkt.</summary>
        public static List<string> Blueprints(GameStateStore gs)
        {
            var outp = new List<string>();
            foreach (var t in J.Path(gs?.State, "home", "blueprints") as JArray ?? new JArray())
            {
                var id = J.Str(t);
                if (id.Length > 0 && !outp.Contains(id)) outp.Add(id);
            }
            if (gs == null) return outp;
            foreach (var plan in ShopLogic.Bauplaene())
            {
                var werkstattId = J.Str(plan.Raw?["werkstatt_id"]);
                if (werkstattId.Length == 0 || !ShopLogic.ImRegal(gs, plan)) continue;
                var bp = "bp_" + werkstattId;
                if (!outp.Contains(bp)) outp.Add(bp);
            }
            return outp;
        }

        public static bool HasBlueprint(GameStateStore gs, string id) => Blueprints(gs).Contains(id);

        public static bool AddBlueprint(GameStateStore gs, string id)
        {
            if (gs == null || string.IsNullOrEmpty(id) || HasBlueprint(gs, id)) return false;
            gs.Update(state =>
            {
                var home = J.EnsureDict(state, "home");
                if (!(home["blueprints"] is JArray)) home["blueprints"] = new JArray();
                ((JArray)home["blueprints"]).Add(id);
            });
            gs.NotifySliceChanged("home");
            return true;
        }

        /// <summary>Moves Baumarkt packs (inventory.items) into the workshop stock. Returns the units moved.</summary>
        public static int UebernehmeBaumarktEinkaeufe(GameStateStore gs)
        {
            if (gs == null) return 0;
            var offen = BaumarktMaterialMap.Keys.Any(k => J.Int(J.Path(gs.State, "inventory", "items", k)) > 0);
            if (!offen) return 0;
            var moved = 0;
            gs.Update(state =>
            {
                var items = J.EnsureDict(J.EnsureDict(state, "inventory"), "items");
                var materials = J.EnsureDict(J.EnsureDict(state, "home"), "materials");
                foreach (var kv in BaumarktMaterialMap)
                {
                    var n = Math.Max(0, J.Int(items[kv.Key]));
                    if (n <= 0) continue;
                    items.Remove(kv.Key);
                    CraftLogic.Add(materials, kv.Value, n);
                    moved += n;
                }
            });
            gs.NotifySliceChanged("home");
            gs.NotifySliceChanged("inventory");
            return moved;
        }

        /// <summary>Does the workshop stand in the garden? Without it the workbench stays closed.</summary>
        public static bool WerkstattGebaut(GameStateStore gs) => gs != null && GardenState.Grid(gs).StructuresOfKind("werkstatt").Count > 0;

        public static CraftLogic.Check Pruefen(GameStateStore gs, string recipeId)
        {
            var recipe = CraftCatalog.Recipe(recipeId);
            if (recipe == null) return new CraftLogic.Check { Reason = CraftLogic.ReasonUnknown };
            var def = FurnitureCatalog.Def(recipe.Item);
            var frei = HomeState.StorageCapacity(gs) - HomeState.StoragePointsUsed(gs);
            return CraftLogic.Pruefen(recipe, Materials(gs), Blueprints(gs), WerkstattGebaut(gs), frei, def?.Lagerwert ?? 1);
        }

        /// <summary>Crafts: materials out, the pieces into storage (Doc D §5.3). Reason as in <see cref="CraftLogic"/>.</summary>
        public static (bool ok, string reason, string item, int count) Craft(GameStateStore gs, string recipeId)
        {
            var check = Pruefen(gs, recipeId);
            if (!check.Ok) return (false, check.Reason, "", 0);
            var recipe = CraftCatalog.Recipe(recipeId);
            var done = false;
            gs.Update(state =>
            {
                var home = J.EnsureDict(state, "home");
                if (!CraftLogic.Consume(J.EnsureDict(home, "materials"), recipe)) return;
                if (!(home["storage"] is JArray)) home["storage"] = new JArray();
                for (var i = 0; i < recipe.Count; i++) StorageLogic.Add((JArray)home["storage"], recipe.Item);
                var counters = J.EnsureDict(J.EnsureDict(state, "achievements"), "counters");
                counters["crafts"] = J.Int(counters["crafts"]) + 1;
                done = true;
            });
            if (!done) return (false, CraftLogic.ReasonMaterial, "", 0);
            gs.NotifySliceChanged("home");
            gs.NotifySliceChanged("achievements");
            return (true, CraftLogic.ReasonOk, recipe.Item, recipe.Count);
        }

        public static List<(CraftRecipe recipe, CraftLogic.Check check)> RecipeStates(GameStateStore gs, string station = CraftCatalog.Werkbank) =>
            CraftCatalog.ForStation(station).Select(r => (r, Pruefen(gs, r.Id))).ToList();

        /// <summary>
        /// What the missing materials cost in Bodo's packs (5 boards, 10 sticks, 20 nails …) — the "is it worth it?"
        /// hint under a recipe. 0 when nothing missing can be bought there.
        /// </summary>
        public static int BaumarktKosten(CraftRecipe recipe, JObject inventory)
        {
            var total = 0;
            var packs = ShopLogic.Materialien();
            foreach (var kv in CraftLogic.MissingMaterials(recipe, inventory))
            {
                var itemId = BaumarktMaterialMap.FirstOrDefault(m => m.Value == kv.Key).Key;
                var pack = itemId == null ? null : packs.FirstOrDefault(p => p.Inventar == itemId);
                if (pack == null) continue;
                total += (int)Math.Ceiling(kv.Value / (double)Math.Max(1, pack.Menge)) * pack.Preis;
            }
            return total;
        }
    }

    /// <summary>
    /// Weekly yield of the herb box (recipe r_kraeuterkasten) — port of kraeuter_kasten.gd: every box owned (storage or
    /// placed in a room) grows one herb bundle a week into inventory.items, the craft ware of the own market stall.
    /// Save: home.kraeuterkasten = {letzteErnteUnix}. The cycle anchors on the first call after owning one and then
    /// only moves in whole weeks; more than two weeks behind are not hoarded.
    /// </summary>
    public static class KraeuterKasten
    {
        public const string ItemId = "kraeuterkasten";
        public const string WareId = "kraeuter";
        public const long WocheS = 7 * 86400;
        public const int MaxWochen = 2;

        public static int Anzahl(GameStateStore gs)
        {
            if (gs == null) return 0;
            var summe = StorageLogic.CountOf(HomeState.Storage(gs), ItemId);
            if (J.Path(gs.State, "home", "rooms") is JObject rooms)
                foreach (var room in rooms.Properties())
                    foreach (var e in J.Path(room.Value, "items") as JArray ?? new JArray())
                        if (J.Str(e?["item"]) == ItemId) summe++;
            return summe;
        }

        public static long LetzteErnteUnix(GameStateStore gs) => Math.Max(0L, J.Long(J.Path(gs?.State, "home", "kraeuterkasten", "letzteErnteUnix")));

        public static int WochenSeit(long letzteUnix, long unixS)
        {
            if (letzteUnix <= 0 || unixS <= letzteUnix) return 0;
            return (int)Math.Max(0, Math.Min(MaxWochen, (unixS - letzteUnix) / WocheS));
        }

        public static int Faellig(GameStateStore gs, long unixS)
        {
            var kaesten = Anzahl(gs);
            return kaesten <= 0 ? 0 : WochenSeit(LetzteErnteUnix(gs), unixS) * kaesten;
        }

        /// <summary>Books the due bundles and moves the anchor by whole weeks. Returns the bundles credited.</summary>
        public static int Schoepfe(GameStateStore gs, long unixS)
        {
            if (gs == null || Anzahl(gs) <= 0) return 0;
            var letzte = LetzteErnteUnix(gs);
            var wochen = WochenSeit(letzte, unixS);
            var ertrag = wochen * Anzahl(gs);
            if (letzte > 0 && wochen <= 0) return 0;
            gs.Update(state =>
            {
                var home = J.EnsureDict(state, "home");
                home["kraeuterkasten"] = new JObject { ["letzteErnteUnix"] = letzte <= 0 ? unixS : letzte + wochen * WocheS };
                if (ertrag <= 0) return;
                var items = J.EnsureDict(J.EnsureDict(state, "inventory"), "items");
                items[WareId] = Math.Max(0, J.Int(items[WareId])) + ertrag;
            });
            gs.NotifySliceChanged("home");
            if (ertrag > 0) gs.NotifySliceChanged("inventory");
            return ertrag;
        }
    }
}
