using System;
using System.Collections.Generic;
using System.Linq;
using Gooby.Core.Content;
using Gooby.Core.Json;
using Gooby.Core.Life;
using Gooby.Core.State;
using Newtonsoft.Json.Linq;

namespace Gooby.Core.Meta
{
    public sealed class StickerDef
    {
        public string Id, NameDe, FlavorDe, HintDe, Image, Page, Set, Rarity, CompletionScope;
        public bool Secret;
        public JObject Cond, Raw;

        public static StickerDef From(JObject o) => new StickerDef
        {
            Id = J.Str(o["id"]), NameDe = J.Str(o["name_de"]), FlavorDe = J.Str(o["flavor_de"]), HintDe = J.Str(o["hint_de"]),
            Image = J.Str(o["image"]), Page = J.Str(o["page"]), Set = J.Str(o["set"]), Rarity = J.Str(o["rarity"], "haeufig"),
            CompletionScope = J.Str(o["completion_scope"], "base"), Secret = J.IsTrue(o["secret"]), Cond = o["cond"] as JObject, Raw = o,
        };
    }

    public sealed class StickerPage
    {
        public string Id, TitleDe, Icon, Tint;
        public int Order;
    }

    /// <summary>Port of sticker_catalog.gd over content/stickers (144 stickers, 23 pages).</summary>
    public static class StickerCatalog
    {
        public const string StickersPath = "content/stickers/data/stickers";
        public const string PagesPath = "content/stickers/data/sticker_pages";
        public static readonly string[] Rarities = { "haeufig", "selten", "episch", "geheim" };
        public static readonly string[] CondTypes = { "counter", "special", "event", "code" };
        public static readonly string[] CompletionScopes = { "base", "online", "dlc" };

        private static List<StickerDef> _all;
        private static List<StickerPage> _pages;

        public static IReadOnlyList<StickerDef> All()
        {
            if (_all != null) return _all;
            _all = new List<StickerDef>();
            foreach (var t in ContentSource.LoadItems(StickersPath)) if (t is JObject o) _all.Add(StickerDef.From(o));
            return _all;
        }

        public static IReadOnlyList<StickerPage> Pages()
        {
            if (_pages != null) return _pages;
            _pages = new List<StickerPage>();
            foreach (var t in ContentSource.LoadItems(PagesPath))
                if (t is JObject o) _pages.Add(new StickerPage { Id = J.Str(o["id"]), TitleDe = J.Str(o["title_de"]), Icon = J.Str(o["icon"]), Tint = J.Str(o["tint"]), Order = J.Int(o["order"]) });
            _pages.Sort((a, b) => a.Order.CompareTo(b.Order));
            return _pages;
        }

        public static void ResetCache()
        {
            _all = null;
            _pages = null;
        }

        public static StickerDef ById(string id) => All().FirstOrDefault(d => d.Id == id);

        public static Dictionary<string, List<StickerDef>> ByPage(IEnumerable<StickerDef> items)
        {
            var grouped = new Dictionary<string, List<StickerDef>>();
            foreach (var d in items)
            {
                if (!grouped.TryGetValue(d.Page, out var list)) grouped[d.Page] = list = new List<StickerDef>();
                list.Add(d);
            }
            return grouped;
        }

        public static int RegularCount(IEnumerable<StickerDef> items) => items.Count(d => !d.Secret);

        public static string CompletionScope(StickerDef def) => Array.IndexOf(CompletionScopes, def.CompletionScope) >= 0 ? def.CompletionScope : "base";

        public static List<StickerDef> RegularForScope(IEnumerable<StickerDef> items, string scope) => items.Where(d => !d.Secret && CompletionScope(d) == scope).ToList();

        public static List<string> Validate(IEnumerable<StickerDef> items, IEnumerable<StickerPage> pages)
        {
            var errors = new List<string>();
            var pageIds = new HashSet<string>(pages.Select(p => p.Id));
            var seen = new HashSet<string>();
            foreach (var def in items)
            {
                if (string.IsNullOrEmpty(def.Id)) { errors.Add("Eintrag ohne id"); continue; }
                if (!seen.Add(def.Id)) errors.Add($"{def.Id}: doppelte id");
                foreach (var (field, value) in new[] { ("name_de", def.NameDe), ("flavor_de", def.FlavorDe), ("hint_de", def.HintDe), ("image", def.Image), ("page", def.Page) })
                    if (string.IsNullOrEmpty(value)) errors.Add($"{def.Id}: Feld '{field}' fehlt");
                if (Array.IndexOf(Rarities, def.Rarity) < 0) errors.Add($"{def.Id}: unbekannte rarity '{def.Rarity}'");
                if (Array.IndexOf(CompletionScopes, def.CompletionScope) < 0) errors.Add($"{def.Id}: unbekannter completion_scope '{def.CompletionScope}'");
                if (!pageIds.Contains(def.Page)) errors.Add($"{def.Id}: unbekannte Seite '{def.Page}'");
                if (def.Cond == null || Array.IndexOf(CondTypes, J.Str(def.Cond["type"])) < 0) errors.Add($"{def.Id}: cond fehlt/unbekannter type");
                else if (J.Str(def.Cond["key"]).Length == 0) errors.Add($"{def.Id}: cond.key fehlt");
            }
            return errors;
        }
    }

    /// <summary>Port of sticker_unlocks.gd — pure condition evaluation + the signal-driven unlock service.</summary>
    public static class StickerRules
    {
        public static readonly string[] SeasonStickers = { "jz_fruehling", "jz_sommer", "jz_herbst", "jz_winter" };

        public static void FireEventHook(GameStateStore gs, string hookId)
        {
            gs.Update(state =>
            {
                var stickers = J.EnsureDict(state, "stickers");
                J.EnsureDict(stickers, "hooks")[hookId] = true;
            });
            gs.NotifySliceChanged("stickers");
        }

        public static bool CondMet(JObject cond, JObject state)
        {
            if (cond == null) return false;
            var key = J.Str(cond["key"]);
            var count = J.Int(cond["count"], 1);
            switch (J.Str(cond["type"]))
            {
                case "counter": return CounterOf(state, key) >= count;
                case "special": return SpecialValue(state, key, cond["sub"] as JObject) >= count;
                case "event": return J.IsTrue(J.Path(state, "stickers", "hooks", key));
                case "code": return J.Path(state, "codes", "redeemed") is JObject redeemed && redeemed.ContainsKey(key);
            }
            return false;
        }

        public static List<StickerDef> NewlyMet(IEnumerable<StickerDef> catalog, JObject state)
        {
            var unlocked = J.Path(state, "stickers", "unlocked") as JObject ?? new JObject();
            return catalog.Where(d => !string.IsNullOrEmpty(d.Id) && !unlocked.ContainsKey(d.Id) && d.Cond != null && CondMet(d.Cond, state)).ToList();
        }

        public static bool IsUnlocked(JObject state, string id) => J.Path(state, "stickers", "unlocked") is JObject u && u.ContainsKey(id);

        public static int UnlockedCount(JObject state, IEnumerable<StickerDef> catalog) => catalog.Count(d => !d.Secret && IsUnlocked(state, d.Id));

        public static (int unlocked, int total) PageProgress(JObject state, IEnumerable<StickerDef> catalog, string pageId)
        {
            int total = 0, unlocked = 0;
            foreach (var d in catalog)
            {
                if (d.Page != pageId) continue;
                var got = IsUnlocked(state, d.Id);
                if (d.Secret && !got) continue;
                total++;
                if (got) unlocked++;
            }
            return (unlocked, total);
        }

        private static int CounterOf(JObject state, string key)
        {
            var v = J.Path(state, "achievements", "counters", key);
            return J.IsNum(v) ? (int)v.Value<double>() : 0;
        }

        public static double SpecialValue(JObject state, string key, JObject sub)
        {
            var extra = sub ?? new JObject();
            switch (key)
            {
                case "first_boot": return 1.0;
                case "level": return J.NumOr(J.Path(state, "progression", "level"), 1.0);
                case "weightMax": return J.NumOr(J.Path(state, "gooby", "weight"), 50.0);
                case "streak": return J.NumOr(J.Path(state, "daily", "streak"), 0.0);
                case "playtimeMin": return J.NumOr(J.Path(state, "profile", "playtimeMin"), 0.0);
                case "coinsSpent": return J.NumOr(J.Path(state, "economy", "coinsSpent"), 0.0);
                case "skinsOwned": return ArraySize(J.Path(state, "cosmetics", "fur", "owned"));
                case "outfitsOwned": return ArraySize(J.Path(state, "cosmetics", "outfits", "owned"));
                case "fullOutfit": return FilledSlots(J.Path(state, "cosmetics", "outfits", "equipped"));
                case "setsClaimed": return DictSize(J.Path(state, "collections", "claimedSets"));
                case "collectionEntry": return J.NumOr(J.Path(state, "collections", "entries", $"{J.Str(extra["set"])}.{J.Str(extra["entry"])}"), 0.0);
                case "gameBest": return J.NumOr(J.Path(state, "minigames", "legacy", "best", J.Str(extra["game"])), 0.0);
                case "vacationTrips": return J.NumOr(J.Path(state, "vacation", "trips"), 0.0);
                case "park":
                {
                    var parkKey = J.Str(extra["key"], "visits");
                    if (parkKey == "nightVisit") return J.IsTrue(J.Path(state, "park", "nightVisit")) ? 1.0 : 0.0;
                    if (parkKey == "coasterRides") return J.NumOr(J.Path(state, "park", "rides", "coaster"), 0.0);
                    return J.NumOr(J.Path(state, "park", parkKey), 0.0);
                }
                case "decorPlaced": return DictSize(J.Path(state, "decor", "wallpaper")) + DictSize(J.Path(state, "decor", "floor"));
                case "postcards":
                {
                    // the Godot build left this open ("bis W3a liefert"); the archive exists, so count the cards of the destination
                    var dest = J.Str(extra["dest"]);
                    return Postcards.ArchiveOf(state).Count(e => dest.Length == 0 || J.Str(e["destId"]) == dest);
                }
                case "ranchOwned": return J.IsTrue(J.Path(state, "ranch", "gekauft")) ? 1.0 : 0.0;
                case "horsesOwned": return DictSize(J.Path(state, "ranch", "tiere", "pferde"));
                case "horseBond": return MaxHorseBond(J.Path(state, "ranch", "tiere", "pferde"));
                case "ranchAusbau": return RanchAusbauSumme(J.Path(state, "ranch", "ausbau"));
                case "ranchLager": return J.NumOr(J.Path(state, "ranch", "wirtschaft", "lager", "heu"), 0.0) + J.NumOr(J.Path(state, "ranch", "wirtschaft", "lager", "apfel"), 0.0);
                case "hoftiere": return ArraySize(J.Path(state, "ranch", "hoftiere"));
                case "cityVisits": return DictSize(J.Path(state, "city", "besucht"));
                case "seasonPlay": return SeasonPlayed(state, J.Str(extra["season"]));
                case "seasonsCollected": return SeasonStickers.Count(id => IsUnlocked(state, id));
            }
            return -1.0;
        }

        private static double SeasonPlayed(JObject state, string season)
        {
            if (season.Length == 0) return 0.0;
            return DayStrings(state).Any(day => SeasonOfDay(day) == season) ? 1.0 : 0.0;
        }

        public static string SeasonOfDay(string day)
        {
            var parts = (day ?? "").Split('-');
            if (parts.Length != 3 || !int.TryParse(parts[1], out var month)) return "";
            if (month >= 3 && month <= 5) return "fruehling";
            if (month >= 6 && month <= 8) return "sommer";
            if (month >= 9 && month <= 11) return "herbst";
            return month == 12 || month == 1 || month == 2 ? "winter" : "";
        }

        private static List<string> DayStrings(JObject state)
        {
            var days = new List<string>();
            foreach (var v in new[] { J.Path(state, "daily", "lastClaimDay"), J.Path(state, "achievements", "counters", "petsDay"), J.Path(state, "city", "markt", "tag") })
                if (J.IsString(v) && v.Value<string>().Length > 0) days.Add(v.Value<string>());
            if (J.Path(state, "minigames", "legacy", "lastPlayDay") is JObject lastPlay)
                foreach (var p in lastPlay.Properties())
                    if (J.IsString(p.Value) && p.Value.Value<string>().Length > 0) days.Add(p.Value.Value<string>());
            return days;
        }

        private static double MaxHorseBond(JToken pferde)
        {
            if (!(pferde is JObject o)) return 0.0;
            var best = 0.0;
            foreach (var p in o.Properties()) if (p.Value is JObject h) best = Math.Max(best, J.NumOr(h["bindung"], 0.0));
            return best;
        }

        private static double RanchAusbauSumme(JToken ausbau)
        {
            if (!(ausbau is JObject o)) return 0.0;
            return J.NumOr(o["stall"], 1.0) + J.NumOr(o["koppel"], 1.0) + J.NumOr(o["reitplatz"], 1.0);
        }

        private static int FilledSlots(JToken equipped)
        {
            if (!(equipped is JObject eq)) return 0;
            return new[] { "hat", "glasses", "neck" }.Count(slot => eq[slot] != null && eq[slot].Type != JTokenType.Null);
        }

        private static int ArraySize(JToken v) => v is JArray a ? a.Count : 0;
        private static int DictSize(JToken v) => v is JObject o ? o.Count : 0;
    }

    /// <summary>Signal-driven unlock service + page set reward (RewardHub._maybe_claim_set_reward, 120 coins per complete page).</summary>
    public sealed class StickerService
    {
        public const int SetRewardCoins = 120;

        private readonly GameStateStore _gs;
        private readonly IReadOnlyList<StickerDef> _catalog;
        private bool _evaluating;

        public event Action<StickerDef> Unlocked;
        public event Action<StickerPage, int> PageCompleted;

        public StickerService(GameStateStore gs, IReadOnlyList<StickerDef> catalog = null)
        {
            _gs = gs;
            _catalog = catalog ?? StickerCatalog.All();
        }

        public IReadOnlyList<StickerDef> Catalog => _catalog;

        public void Attach()
        {
            _gs.SliceChanged += (_, __) => Evaluate();
            _gs.StatsChanged += _ => Evaluate();
            _gs.LevelChanged += (_, __) => Evaluate();
            Evaluate();
        }

        public void Evaluate()
        {
            if (_evaluating) return;
            _evaluating = true;
            try
            {
                var fresh = StickerRules.NewlyMet(_catalog, _gs.State);
                if (fresh.Count == 0) return;
                var nowMs = _gs.Clock.NowMs();
                _gs.Update(state =>
                {
                    var unlocked = J.EnsureDict(J.EnsureDict(state, "stickers"), "unlocked");
                    foreach (var def in fresh) unlocked[def.Id] = nowMs;
                });
                foreach (var def in fresh)
                {
                    Unlocked?.Invoke(def);
                    MaybeClaimSetReward(def.Page);
                }
            }
            finally { _evaluating = false; }
            // an unlock may complete a page or satisfy seasonsCollected → one follow-up pass
            Evaluate();
        }

        private void MaybeClaimSetReward(string pageId)
        {
            if (string.IsNullOrEmpty(pageId) || SetRewardClaimed(pageId)) return;
            var progress = StickerRules.PageProgress(_gs.State, _catalog, pageId);
            if (progress.total <= 0 || progress.unlocked < progress.total) return;
            var nowMs = _gs.Clock.NowMs();
            _gs.Update(state =>
            {
                J.EnsureDict(J.EnsureDict(state, "stickers"), "setRewards")[pageId] = nowMs;
                if (state["economy"] is JObject econ) Economy.Award(econ, SetRewardCoins, "stickerSet");
            });
            var page = StickerCatalog.Pages().FirstOrDefault(p => p.Id == pageId) ?? new StickerPage { Id = pageId, TitleDe = pageId };
            PageCompleted?.Invoke(page, SetRewardCoins);
        }

        private bool SetRewardClaimed(string pageId) => J.Path(_gs.State, "stickers", "setRewards") is JObject r && r.ContainsKey(pageId);
    }
}
