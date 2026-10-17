using System;
using System.Collections.Generic;
using Gooby.Core.Json;
using Gooby.Core.Life;
using Newtonsoft.Json.Linq;

namespace Gooby.Core.Minigames
{
    /// <summary>Round booking result (minigame_award.gd return dict).</summary>
    public sealed class AwardResult
    {
        public string GameId, Difficulty;
        public int Score, Coins, Best, Xp, LevelsGained, CoinsFromLevels, Level, ModifierBonusCoins, GluecksrolleCoins, SpotlightBonusCoins;
        public bool FirstToday, NewBest, DayCapReached, BeatTarget, ModifierCapped;
        public JObject Modifier = new JObject();

        public JObject ToJson() => new JObject
        {
            ["gameId"] = GameId, ["score"] = Score, ["coins"] = Coins, ["firstToday"] = FirstToday, ["best"] = Best, ["newBest"] = NewBest,
            ["xp"] = Xp, ["levelsGained"] = LevelsGained, ["coinsFromLevels"] = CoinsFromLevels, ["level"] = Level, ["difficulty"] = Difficulty,
            ["dayCapReached"] = DayCapReached, ["beatTarget"] = BeatTarget, ["modifier"] = Modifier, ["modifierBonusCoins"] = ModifierBonusCoins,
            ["gluecksrolleCoins"] = GluecksrolleCoins, ["modifierCapped"] = ModifierCapped, ["spotlightBonusCoins"] = SpotlightBonusCoins,
        };
    }

    /// <summary>
    /// Port of minigame_award.gd — books a finished round: coins (difficulty coin table, daily first-play ×2,
    /// 150-coin timed day cap, endless flat 5), fun +15, plays/lastPlayDay, best per mode, beaten target, XP + level-ups.
    /// Modifier bonuses are honoured when a modifier dict is given (glücksrolle hook via <see cref="GluecksrolleRoller"/>).
    /// </summary>
    public static class MinigameAward
    {
        public const int DailyFirstPlayMult = 2;
        public const double FunReward = 15.0;
        public const int MinigameDayCap = 150;

        /// <summary>ModifierEngine.roll_glueckspilz(state) hook; null → 0.</summary>
        public static Func<JObject, int> GluecksrolleRoller { get; set; }

        public static AwardResult Award(JObject state, MinigameMeta meta, int score, string difficulty, string today, IList<int> chunks = null, JObject modifier = null)
        {
            var id = meta.Id;
            var s = Math.Max(0, score);
            modifier = modifier ?? new JObject();
            var scoreMult = J.NumOr(modifier["score_mult"], 1.0);
            if (scoreMult > 1.0) s = Math.Max(0, (int)Math.Round(s * scoreMult, MidpointRounding.AwayFromZero));
            var mode = FrameworkLogic.NormalizeDifficulty(difficulty);
            var mg = J.EnsureDict(state, "minigames");
            var legacy = J.EnsureDict(mg, "legacy");
            var lastPlayDay = J.EnsureDict(legacy, "lastPlayDay");
            var firstToday = J.Str(lastPlayDay[id]) != today;
            var baseCoins = 0;
            if (mode == "endless") baseCoins = FrameworkLogic.EndlessFlatCoins;
            else if (chunks != null && chunks.Count > 0) foreach (var chunk in chunks) baseCoins += FrameworkLogic.ApplyDifficultyCoinBase(meta.CoinTable, chunk, mode);
            else baseCoins = FrameworkLogic.ApplyDifficultyCoinBase(meta.CoinTable, s, mode);
            var dailyMult = firstToday ? DailyFirstPlayMult : 1;
            var paid = baseCoins * dailyMult;
            var dayCapReached = false;
            var econ = J.EnsureDict(state, "economy");
            if (mode == "endless")
            {
                var granted = Economy.Award(econ, paid, "endless", today);
                if (granted < paid) dayCapReached = true;
                paid = granted;
            }
            else
            {
                var timedGranted = BookMinigameDayCoins(mg, paid, today);
                if (timedGranted < paid) dayCapReached = true;
                paid = timedGranted;
                Economy.Award(econ, paid, "minigame", today);
            }
            var modBonus = 0;
            var modCapped = false;
            var coinMult = J.NumOr(modifier["coin_mult"], 1.0);
            if (coinMult > 1.0 && paid > 0)
            {
                var want = (int)Math.Round(paid * (coinMult - 1.0), MidpointRounding.AwayFromZero);
                modBonus = Economy.Award(econ, want, "modifier", today);
                modCapped = modBonus < want;
            }
            var glueck = 0;
            if (J.IsTrue(modifier["gluecksrolle"]))
            {
                var roll = GluecksrolleRoller?.Invoke(state) ?? 0;
                glueck = Economy.Award(econ, roll, "glueckspilz", today);
                if (glueck < roll) modCapped = true;
            }
            var spotlightBonus = ArcadeSpotlight.BeanspruchBonus(state, id, paid, today);
            if (spotlightBonus > 0) spotlightBonus = Economy.Award(econ, spotlightBonus, "spotlight", today);
            var stats = J.EnsureDict(J.EnsureDict(state, "gooby"), "stats");
            stats["fun"] = Math.Min(100.0, Math.Max(0.0, J.Num(stats["fun"]) + FunReward));
            var plays = J.EnsureDict(mg, "plays");
            plays[id] = J.Int(plays[id]) + 1;
            lastPlayDay[id] = today;
            if (modifier.HasValues && state["achievements"] is JObject ach)
            {
                var counters = J.EnsureDict(ach, "counters");
                counters["modifierPlays"] = J.Int(counters["modifierPlays"]) + 1;
            }
            var prev = PrevBest(legacy, id, mode);
            var newBest = s > prev;
            if (mode == "normal") { if (newBest) J.EnsureDict(legacy, "best")[id] = s; }
            else if (mode == "endless") { if (newBest) J.EnsureDict(legacy, "endlessBest")[id] = s; }
            else if (newBest) J.EnsureDict(J.EnsureDict(legacy, "bestByDiff"), id)[mode] = s;
            var target = meta.Target;
            var beatTarget = mode != "endless" && target >= 0 && s >= target;
            if (beatTarget) J.EnsureDict(J.EnsureDict(legacy, "beaten"), id)[mode] = true;
            var prog = J.EnsureDict(state, "progression");
            var xpGain = Leveling.MinigameXp(paid);
            var xpMult = J.NumOr(modifier["xp_mult"], 1.0);
            if (xpMult > 1.0) xpGain = (int)Math.Round(xpGain * xpMult, MidpointRounding.AwayFromZero);
            var res = Leveling.ApplyXp(J.Num(prog["xp"]), (int)J.NumOr(prog["level"], 1.0), xpGain);
            prog["xp"] = res.Xp;
            prog["level"] = res.Level;
            if (res.CoinsAwarded > 0) Economy.Award(econ, res.CoinsAwarded, "levelUp", today);
            return new AwardResult
            {
                GameId = id, Score = s, Coins = paid, FirstToday = firstToday, Best = Math.Max(prev, s), NewBest = newBest, Xp = xpGain,
                LevelsGained = res.LevelsGained, CoinsFromLevels = res.CoinsAwarded, Level = res.Level, Difficulty = mode, DayCapReached = dayCapReached,
                BeatTarget = beatTarget, Modifier = (JObject)modifier.DeepClone(), ModifierBonusCoins = modBonus, GluecksrolleCoins = glueck,
                ModifierCapped = modCapped, SpotlightBonusCoins = spotlightBonus,
            };
        }

        private static int BookMinigameDayCoins(JObject mg, int amount, string day)
        {
            if (J.Str(mg["dayCoinsDay"]) != day)
            {
                mg["dayCoins"] = 0;
                mg["dayCoinsDay"] = day;
            }
            var used = J.Int(mg["dayCoins"]);
            var headroom = Math.Max(0, MinigameDayCap - used);
            var granted = Math.Min(Math.Max(0, amount), headroom);
            mg["dayCoins"] = used + granted;
            return granted;
        }

        private static int PrevBest(JObject legacy, string id, string mode)
        {
            if (mode == "endless") return J.Int(J.Dict(legacy["endlessBest"])[id]);
            if (mode == "easy" || mode == "hard") return J.Int(J.Dict(J.Dict(legacy["bestByDiff"])[id])[mode]);
            return J.Int(J.Dict(legacy["best"])[id]);
        }
    }
}
