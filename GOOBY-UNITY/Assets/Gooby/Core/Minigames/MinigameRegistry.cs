using System;
using System.Collections.Generic;
using System.Linq;
using Gooby.Core.Content;
using Gooby.Core.Json;
using Newtonsoft.Json.Linq;

namespace Gooby.Core.Minigames
{
    /// <summary>Game manifest (minigames/registry.json — merged from the Godot registry + game.json manifests).</summary>
    public sealed class MinigameMeta
    {
        public string Id, TitleKey, Orientation = "portrait";
        public FrameworkLogic.CoinTable CoinTable;
        public int Target = -1, EnergyCost = MinigameRegistry.DefaultEnergyCost;
        public bool SupportsEndless, ComingSoon;
        public JObject Raw;

        public static MinigameMeta From(JObject o)
        {
            var ct = J.Dict(o["coin_table"]);
            return new MinigameMeta
            {
                Id = J.Str(o["id"]), TitleKey = J.Str(o["title_key"], "mg." + J.Str(o["id"]) + ".title"),
                Orientation = FrameworkLogic.NormalizeOrientation(J.Str(o["orientation"], "portrait")),
                CoinTable = new FrameworkLogic.CoinTable { Divisor = J.Num(ct["divisor"]), Min = J.Int(ct["min"]), Max = J.Int(ct["max"]) },
                Target = J.Int(o["target"], -1), EnergyCost = J.Int(o["energy_cost"], MinigameRegistry.DefaultEnergyCost),
                SupportsEndless = J.IsTrue(o["supports_endless"]), ComingSoon = J.IsTrue(o["coming_soon"]), Raw = o,
            };
        }
    }

    /// <summary>Port of minigame_registry.gd (38 games).</summary>
    public static class MinigameRegistry
    {
        public const string DataPath = "minigames/registry";
        public const int DefaultEnergyCost = 8;
        private static List<MinigameMeta> _all;

        public static IReadOnlyList<MinigameMeta> AllGames()
        {
            if (_all != null) return _all;
            _all = new List<MinigameMeta>();
            foreach (var t in ContentSource.LoadItems(DataPath)) if (t is JObject o && J.Str(o["id"]).Length > 0) _all.Add(MinigameMeta.From(o));
            return _all;
        }

        public static void ResetCache() => _all = null;

        public static MinigameMeta GetGame(string id) => AllGames().FirstOrDefault(g => g.Id == id);

        public static List<MinigameMeta> Playable() => AllGames().Where(g => !g.ComingSoon).ToList();
    }

    /// <summary>Port of arcade_spotlight.gd — the deterministic daily spotlight game (+50 % once per day).</summary>
    /// <summary>
    /// The arcade's labelled rows (arcade_fortschritt.gd W20): every game in exactly one category, derived from its
    /// play principle; unknown future ids fall into "action". Titles: mg.arcade.reihe.&lt;row&gt;.
    /// </summary>
    public static class ArcadeRows
    {
        public static readonly string[] Order = { "geschick", "action", "fahren", "denken", "ranch", "ruhig" };
        public const string Fallback = "action";

        private static readonly Dictionary<string, string[]> Ids = new Dictionary<string, string[]>
        {
            ["geschick"] = new[] { "teaParty", "danceParty", "burgerBuild", "pancakeTower", "veggieChop", "basketBounce", "bubblePop", "trampoline" },
            ["action"] = new[] { "carrotCatch", "carrotGuard", "gvz", "gardenRush", "ghostHunt", "goalieGooby", "harborHopper", "rocketRescue", "runner", "shoppingSurf", "starHopper", "bunnyHop" },
            ["fahren"] = new[] { "cityDrive", "deliveryRush", "toyRacer", "snailMail" },
            ["denken"] = new[] { "memoryMatch", "goobySays", "pipeFlow", "purblePlace", "hideSeek", "gobnom" },
            ["ranch"] = new[] { "ranchHerde", "ranchParcours", "ranchTonnen", "ranchTurnier", "ranchZeit" },
            ["ruhig"] = new[] { "fishingPond", "lanternFloat", "miniGolf" },
        };

        public static string RowOf(string gameId)
        {
            foreach (var kv in Ids)
                if (System.Array.IndexOf(kv.Value, gameId) >= 0) return kv.Key;
            return Fallback;
        }

        /// <summary>True when the id is placed on purpose (the completeness guard of the tests).</summary>
        public static bool Assigned(string gameId)
        {
            foreach (var kv in Ids)
                if (System.Array.IndexOf(kv.Value, gameId) >= 0) return true;
            return false;
        }
    }

    public static class ArcadeSpotlight
    {
        public const string AnkerTag = "2024-01-01";
        public const int BonusProzent = 50;
        public const string MarkerKey = "spotlightBonusDay";

        public static List<string> PoolIds(IEnumerable<MinigameMeta> games)
        {
            var ids = new List<string>();
            foreach (var g in games)
            {
                if (g.ComingSoon || string.IsNullOrEmpty(g.Id) || ids.Contains(g.Id)) continue;
                ids.Add(g.Id);
            }
            ids.Sort(StringComparer.Ordinal);
            return ids;
        }

        public static string SpotlightId(IEnumerable<MinigameMeta> games, string day)
        {
            var pool = PoolIds(games);
            var n = pool.Count;
            var ziel = TagNummer(day);
            if (n == 0 || ziel < 0) return "";
            if (n == 1) return pool[0];
            var anker = TagNummer(AnkerTag);
            if (ziel <= anker) return pool[(int)(Mix(TagesSeed(ziel)) % (uint)n)];
            var idx = (int)(Mix(TagesSeed(anker)) % (uint)n);
            for (var k = anker + 1; k <= ziel; k++) idx = (int)((idx + 1 + Mix(TagesSeed(k)) % (uint)(n - 1)) % n);
            return pool[idx];
        }

        public static bool IstSpotlight(string gameId, string day, IEnumerable<MinigameMeta> games = null) =>
            !string.IsNullOrEmpty(gameId) && SpotlightId(games ?? MinigameRegistry.AllGames(), day) == gameId;

        public static bool BonusVerfuegbar(JObject state, string day) => !(state["minigames"] is JObject mg) || J.Str(mg[MarkerKey]) != day;

        public static int BeanspruchBonus(JObject state, string gameId, int basisCoins, string day, IEnumerable<MinigameMeta> games = null)
        {
            if (basisCoins <= 0 || !BonusVerfuegbar(state, day) || !IstSpotlight(gameId, day, games)) return 0;
            if (!(state["minigames"] is JObject mg)) return 0;
            mg[MarkerKey] = day;
            return (int)Math.Round(basisCoins * BonusProzent / 100.0, MidpointRounding.AwayFromZero);
        }

        /// <summary>"YYYY-MM-DD" → Unix day number (noon trick against DST edges); -1 for broken input.</summary>
        public static long TagNummer(string day)
        {
            var parts = (day ?? "").Split('-');
            if (parts.Length != 3 || !int.TryParse(parts[0], out var y) || !int.TryParse(parts[1], out var m) || !int.TryParse(parts[2], out var d)) return -1;
            try
            {
                var unix = new DateTimeOffset(y, m, d, 12, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds();
                return (long)Math.Floor(unix / 86400.0);
            }
            catch (ArgumentOutOfRangeException) { return -1; }
        }

        private static uint TagesSeed(long tagNummer)
        {
            var dt = DateTimeOffset.FromUnixTimeSeconds(tagNummer * 86400 + 43200).UtcDateTime;
            return (uint)(dt.Year * 10000 + dt.Month * 100 + dt.Day);
        }

        private static uint Mix(uint seed)
        {
            var z = unchecked(seed + 0x9E3779B9u);
            z = unchecked((z ^ (z >> 16)) * 0x21F0AAADu);
            z = unchecked((z ^ (z >> 15)) * 0x735A2D97u);
            return (z ^ (z >> 15)) & 0x7FFFFFFFu;
        }
    }
}
