using System;
using System.Collections.Generic;
using Gooby.Core.Json;
using Newtonsoft.Json.Linq;

namespace Gooby.Core.Minigames
{
    /// <summary>
    /// Pure difficulty / coin / orientation policy — number-identical port of
    /// GOOBY/src/minigames/framework.logic.js (§G5) / scripts/minigames/framework_logic.gd.
    /// Golden values: Tests/Fixtures/expected/framework.json.
    /// </summary>
    public static class FrameworkLogic
    {
        public static readonly string[] DifficultyModes = { "easy", "normal", "hard", "endless" };

        public static readonly IReadOnlyDictionary<string, double> DifficultyCoinMult = new Dictionary<string, double>
        {
            { "easy", 0.7 }, { "normal", 1.0 }, { "hard", 1.3 },
        };

        public const int EndlessFlatCoins = 5;
        public const int EndlessMinLevel = 10;
        public static readonly string[] DifficultyExcludedGames = { "cityDrive", "goobyWelt" };
        public const int StrikesForTeleport = 3;
        public static readonly string[] CarGames = { "cityDrive", "deliveryRush" };

        /// <summary>JS Math.round: .5 rounds towards +infinity; for x ≥ 0 == floor(x + 0.5).</summary>
        public static int JsRound(double x) => (int)Math.Floor(x + 0.5);

        public static string NormalizeDifficulty(string mode) =>
            mode != null && Array.IndexOf(DifficultyModes, mode) >= 0 ? mode : "normal";

        public static bool DifficultyEnabled(string gameId, JObject meta = null)
        {
            if (meta != null && J.IsTrue(meta["dev"])) return false;
            if (meta != null && J.IsTrue(meta["difficulty_opt_in"])) return true;
            return Array.IndexOf(DifficultyExcludedGames, gameId) < 0;
        }

        public static string EffectiveDifficulty(string gameId, JObject prms = null, JObject meta = null)
        {
            if (prms != null && prms["mode"] != null && prms["mode"].Type != JTokenType.Null) return "normal";
            if (!DifficultyEnabled(gameId, meta)) return "normal";
            return NormalizeDifficulty(J.Str(prms?["difficulty"], null));
        }

        public struct CoinTable
        {
            public double Divisor;
            public int Min;
            public int Max;
            public CoinTable(double divisor, int min, int max) { Divisor = divisor; Min = min; Max = max; }
        }

        /// <summary>§G5.2 base coin math (BEFORE daily ×2): min(max, max(min, round(rowClamp(score) × mult))).</summary>
        public static int ApplyDifficultyCoinBase(CoinTable table, double score, string mode)
        {
            var s = Math.Max(0, (int)Math.Floor(score));
            var divisor = table.Divisor == 0 ? 1.0 : table.Divisor;
            var rowClamp = Math.Min(table.Max, Math.Max(table.Min, (int)Math.Floor(s / divisor)));
            DifficultyCoinMult.TryGetValue(mode, out var mult);
            if (!DifficultyCoinMult.ContainsKey(mode)) mult = 1.0;
            return Math.Min(table.Max, Math.Max(table.Min, JsRound(rowClamp * mult)));
        }

        public static int ApplyDifficultyCoinBase(JObject coinTable, double score, string mode) =>
            ApplyDifficultyCoinBase(new CoinTable(J.NumOr(coinTable?["divisor"], 1.0), J.Int(coinTable?["min"]), J.Int(coinTable?["max"])), score, mode);

        public sealed class DifficultySlice
        {
            public string Selected = "normal";
            public JObject Beaten = new JObject();
            public JObject BestByDiff = new JObject();
            public int Best;
            public int EndlessBest;
        }

        /// <summary>Defensive v5 slice reader (§G5.5) — hostile/missing containers never throw.</summary>
        public static DifficultySlice DifficultySliceOf(JObject state, string gameId)
        {
            var mg = J.Dict(state?["minigames"]);
            var legacy = J.Dict(mg["legacy"]);
            var selRaw = J.Str(J.Dict(mg["difficulty"])[gameId], null);
            return new DifficultySlice
            {
                Selected = selRaw == "easy" || selRaw == "hard" ? selRaw : "normal",
                Beaten = J.Dict(J.Dict(legacy["beaten"])[gameId]),
                BestByDiff = J.Dict(J.Dict(legacy["bestByDiff"])[gameId]),
                Best = Math.Max(0, (int)Math.Floor(J.Num(J.Dict(legacy["best"])[gameId]))),
                EndlessBest = Math.Max(0, (int)Math.Floor(J.Num(J.Dict(legacy["endlessBest"])[gameId]))),
            };
        }

        public static bool EndlessUnlocked(JObject state, string gameId)
        {
            var level = Math.Max(1, (int)Math.Floor(J.NumOr(J.Dict(state?["progression"])["level"], 1.0)));
            return J.IsTrue(DifficultySliceOf(state, gameId).Beaten["hard"]) && level >= EndlessMinLevel;
        }

        public static int BestForMode(JObject state, string gameId, string mode)
        {
            var slice = DifficultySliceOf(state, gameId);
            if (mode == "endless") return slice.EndlessBest;
            if (mode == "easy" || mode == "hard") return Math.Max(0, (int)Math.Floor(J.Num(slice.BestByDiff[mode])));
            return slice.Best;
        }

        public static (int strikes, bool teleport) ApplyStrike(double strikes)
        {
            var n = Math.Max(0, (int)Math.Floor(strikes)) + 1;
            return (n, n >= StrikesForTeleport);
        }

        public static string NormalizeOrientation(string value) => value == "landscape" ? "landscape" : "portrait";

        public static bool ShouldShowRotateGate(string orientationFlag, bool viewportIsLandscape)
        {
            if (NormalizeOrientation(orientationFlag) != "landscape") return false;
            return !viewportIsLandscape;
        }

        public static string OrientationLockFor(string gameOrientation) =>
            NormalizeOrientation(gameOrientation) == "landscape" ? "unlock" : "portrait";

        /// <summary>
        /// The orientation a round runs in (minigame_host.gd effective_orientation): an explicit choice
        /// ("portrait"/"landscape" from the settings' orientation_mode) wins, "auto" or anything else falls back to the
        /// game's registry default.
        /// </summary>
        public static string EffectiveOrientation(string choice, string gameOrientation) =>
            choice == "portrait" || choice == "landscape" ? choice : NormalizeOrientation(gameOrientation);
    }
}
