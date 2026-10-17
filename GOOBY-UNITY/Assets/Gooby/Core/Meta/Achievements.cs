using System;
using System.Collections.Generic;
using System.Linq;
using Gooby.Core.Content;
using Gooby.Core.Home;
using Gooby.Core.Json;
using Gooby.Core.Life;
using Gooby.Core.State;
using Newtonsoft.Json.Linq;

namespace Gooby.Core.Meta
{
    /// <summary>Achievement definition (content/achievements/data/achievements.json item).</summary>
    public sealed class AchievementDef
    {
        public string Id, Cat, CondType, CondKey;
        public int Order, Coins, CondCount;
        public JObject Raw;

        public static AchievementDef From(JObject o)
        {
            var cond = J.Dict(o["cond"]);
            return new AchievementDef
            {
                Id = J.Str(o["id"]), Cat = J.Str(o["cat"]), Order = J.Int(o["order"]), Coins = J.Int(o["coins"]),
                CondType = J.Str(cond["type"]), CondKey = J.Str(cond["key"]), CondCount = J.Int(cond["count"]), Raw = o,
            };
        }
    }

    /// <summary>Port of achievements_catalog.gd — 44 web-parity achievements, sorted by order, validated.</summary>
    public static class AchievementsCatalog
    {
        public const string DataPath = "content/achievements/data/achievements";
        public static readonly string[] Categories = { "pflege", "spiel", "garten", "sammeln", "reisen", "fortschritt" };
        public static readonly string[] CondTypes = { "counter", "special" };

        private static List<AchievementDef> _cache;

        public static IReadOnlyList<AchievementDef> All()
        {
            if (_cache != null) return _cache;
            var items = new List<AchievementDef>();
            foreach (var t in ContentSource.LoadItems(DataPath))
                if (t is JObject o) items.Add(AchievementDef.From(o));
            items.Sort((a, b) => a.Order.CompareTo(b.Order));
            _cache = items;
            return items;
        }

        public static void ResetCache() => _cache = null;

        public static AchievementDef ById(string id) => All().FirstOrDefault(d => d.Id == id);

        public static List<AchievementDef> ByCategory(string cat) => All().Where(d => d.Cat == cat).ToList();

        public static int TotalCoins() => All().Sum(d => d.Coins);

        public static List<string> Validate(IEnumerable<AchievementDef> items)
        {
            var errors = new List<string>();
            var seen = new HashSet<string>();
            foreach (var def in items)
            {
                if (string.IsNullOrEmpty(def.Id)) { errors.Add("Eintrag ohne id"); continue; }
                if (!seen.Add(def.Id)) errors.Add($"{def.Id}: doppelte id");
                if (Array.IndexOf(Categories, def.Cat) < 0) errors.Add($"{def.Id}: unbekannte Kategorie '{def.Cat}'");
                if (def.Coins <= 0) errors.Add($"{def.Id}: coins fehlt/<= 0");
                if (Array.IndexOf(CondTypes, def.CondType) < 0) errors.Add($"{def.Id}: cond fehlt/unbekannter type");
                else if (string.IsNullOrEmpty(def.CondKey)) errors.Add($"{def.Id}: cond.key fehlt");
                else if (def.CondCount <= 0) errors.Add($"{def.Id}: cond.count fehlt/<= 0");
            }
            return errors;
        }
    }

    /// <summary>Port of achievements_engine.gd — pure progress evaluation over the v5 state.</summary>
    public static class AchievementsEngine
    {
        public static (int current, int target) ProgressOf(AchievementDef def, JObject state)
        {
            var target = Math.Max(1, def.CondCount);
            var current = 0;
            switch (def.CondType)
            {
                case "counter": current = CounterOf(state, def.CondKey); break;
                case "special": current = SpecialValue(state, def.CondKey, target); break;
            }
            return (J.ClampI(current, 0, target), target);
        }

        public static bool IsSatisfied(AchievementDef def, JObject state)
        {
            var p = ProgressOf(def, state);
            return p.current >= p.target;
        }

        public static List<AchievementDef> NewlyMet(IEnumerable<AchievementDef> catalog, JObject state)
        {
            var unlocked = UnlockedMap(state);
            var result = new List<AchievementDef>();
            foreach (var def in catalog)
            {
                if (string.IsNullOrEmpty(def.Id) || unlocked[def.Id] != null) continue;
                if (IsSatisfied(def, state)) result.Add(def);
            }
            return result;
        }

        public static bool IsUnlocked(JObject state, string id) => UnlockedMap(state)[id] != null;

        public static int UnlockedCount(JObject state, IEnumerable<AchievementDef> catalog) => catalog.Count(d => IsUnlocked(state, d.Id));

        public static int CountNonDefaultDecor(JObject state)
        {
            var defaults = RoomDefs.DefaultItemSet();
            var n = 0;
            if (J.Path(state, "home", "rooms") is JObject rooms)
            {
                foreach (var room in rooms.Properties())
                {
                    if (!(J.Dict(room.Value)["items"] is JArray items)) continue;
                    foreach (var entry in items)
                    {
                        if (!(entry is JObject e)) continue;
                        var itemId = J.Str(e["item"]);
                        if (!string.IsNullOrEmpty(itemId) && !defaults.Contains(itemId)) n++;
                    }
                }
            }
            n += DictSize(J.Path(state, "decor", "wallpaper"));
            n += DictSize(J.Path(state, "decor", "floor"));
            return n;
        }

        private static int SpecialValue(JObject state, string key, int target)
        {
            switch (key)
            {
                case "coins": return (int)J.NumOr(J.Path(state, "economy", "coins"), 0.0);
                case "level": return (int)J.NumOr(J.Path(state, "progression", "level"), 1.0);
                case "fullOutfit": return FilledSlots(J.Path(state, "cosmetics", "outfits", "equipped"));
                case "decor": return CountNonDefaultDecor(state);
                case "streak": return (int)J.NumOr(J.Path(state, "daily", "streak"), 0.0);
                case "play12":
                case "play21": return DistinctPlays(state);
                case "allCrops": return VeggieEntries(state);
                case "stickers": return Math.Max(StickerBookCount(state), CollectionEntries(state));
                case "setsClaimed": return DictSize(J.Path(state, "collections", "claimedSets")) + DictSize(J.Path(state, "stickers", "setRewards"));
                case "neverSick": return NeverSick(state);
                case "weightMax": return (int)J.NumOr(J.Path(state, "gooby", "weight"), 50.0);
                case "weightMin":
                {
                    var w = J.NumOr(J.Path(state, "gooby", "weight"), 50.0);
                    return w > 0.0 && w <= target ? target : 0;
                }
                case "holeInOne": return CounterOf(state, "holeInOnes");
                case "stickerCount": return StickerBookCount(state);
                case "parkVisits": return (int)J.NumOr(J.Path(state, "park", "visits"), 0.0);
                case "coasterRides": return (int)J.NumOr(J.Path(state, "park", "rides", "coaster"), 0.0);
                case "wheelRides": return (int)J.NumOr(J.Path(state, "park", "rides", "wheel"), 0.0);
                case "parkNight": return J.IsTrue(J.Path(state, "park", "nightVisit")) ? 1 : 0;
                case "postcards": return Math.Max(ArraySize(J.Path(state, "vacation", "archive")), (int)J.NumOr(J.Path(state, "vacation", "postcards"), 0.0));
                case "vacationDestinations": return DictSize(J.Path(state, "vacation", "visited"));
            }
            return 0;
        }

        private static int NeverSick(JObject state)
        {
            var level = (int)J.NumOr(J.Path(state, "progression", "level"), 1.0);
            return level >= 10 && CounterOf(state, "sickEver") == 0 ? 1 : 0;
        }

        private static int DistinctPlays(JObject state)
        {
            if (!(J.Path(state, "minigames", "plays") is JObject plays)) return 0;
            return plays.Properties().Count(p => J.NumOr(p.Value, 0.0) >= 1.0);
        }

        private static int VeggieEntries(JObject state)
        {
            if (!(J.Path(state, "collections", "entries") is JObject entries)) return 0;
            return entries.Properties().Count(p => p.Name.StartsWith("veggies.", StringComparison.Ordinal) && J.NumOr(p.Value, 0.0) >= 1.0);
        }

        private static int CollectionEntries(JObject state)
        {
            if (!(J.Path(state, "collections", "entries") is JObject entries)) return 0;
            return entries.Properties().Count(p => J.NumOr(p.Value, 0.0) >= 1.0);
        }

        private static int StickerBookCount(JObject state) => DictSize(J.Path(state, "stickers", "unlocked"));

        private static JObject UnlockedMap(JObject state) => J.Path(state, "achievements", "unlocked") as JObject ?? new JObject();

        private static int CounterOf(JObject state, string key)
        {
            var v = J.Path(state, "achievements", "counters", key);
            return J.IsNum(v) ? (int)v.Value<double>() : 0;
        }

        private static int FilledSlots(JToken equipped)
        {
            if (!(equipped is JObject eq)) return 0;
            var filled = 0;
            foreach (var slot in new[] { "hat", "glasses", "neck" })
                if (eq[slot] != null && eq[slot].Type != JTokenType.Null) filled++;
            return filled;
        }

        private static int ArraySize(JToken v) => v is JArray a ? a.Count : 0;
        private static int DictSize(JToken v) => v is JObject o ? o.Count : 0;
    }

    /// <summary>
    /// Port of achievements_service.gd — evaluates the catalog on every store change, unlocks once, pays coins once
    /// and re-evaluates because rewards can satisfy follow-up achievements (coins1000).
    /// </summary>
    public sealed class AchievementsService
    {
        private readonly GameStateStore _store;
        private readonly IReadOnlyList<AchievementDef> _catalog;
        private bool _evaluating;

        public event Action<AchievementDef> Unlocked;

        public AchievementsService(GameStateStore store, IReadOnlyList<AchievementDef> catalog = null)
        {
            _store = store;
            _catalog = catalog ?? AchievementsCatalog.All();
        }

        public IReadOnlyList<AchievementDef> Catalog => _catalog;

        public void Attach()
        {
            _store.SliceChanged += (_, __) => EvaluateNow();
            _store.StatsChanged += _ => EvaluateNow();
            _store.LevelChanged += (_, __) => EvaluateNow();
            _store.CoinsChanged += _ => EvaluateNow();
            EvaluateNow();
        }

        public void EvaluateNow()
        {
            if (_store == null || _evaluating) return;
            _evaluating = true;
            LatchSickEver();
            var fresh = AchievementsEngine.NewlyMet(_catalog, _store.State);
            if (fresh.Count > 0)
            {
                var nowMs = _store.Clock.NowMs();
                _store.Update(state =>
                {
                    var slice = J.EnsureDict(state, "achievements");
                    var unlocked = J.EnsureDict(slice, "unlocked");
                    foreach (var def in fresh)
                    {
                        unlocked[def.Id] = nowMs;
                        if (state["economy"] is JObject economy) Economy.Award(economy, def.Coins, "achievement");
                    }
                });
                foreach (var def in fresh) Unlocked?.Invoke(def);
            }
            _evaluating = false;
            if (fresh.Count > 0) EvaluateNow();
        }

        private void LatchSickEver()
        {
            if (J.Str(J.Path(_store.State, "gooby", "health", "state"), "healthy") != "sick") return;
            if (J.Int(J.Path(_store.State, "achievements", "counters", "sickEver")) > 0) return;
            _store.Update(state =>
            {
                if (!(state["achievements"] is JObject slice)) return;
                J.EnsureDict(slice, "counters")["sickEver"] = 1;
            });
        }
    }
}
