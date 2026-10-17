using System;
using Gooby.Core.Json;
using Newtonsoft.Json.Linq;

namespace Gooby.Core.Life
{
    public struct XpResult
    {
        public double Xp;
        public int Level;
        public int LevelsGained;
        public int CoinsAwarded;
    }

    /// <summary>Pure leveling — 1:1 port of GOOBY/src/systems/leveling.js (§C1.5) / scripts/logic/leveling.gd.</summary>
    public static class Leveling
    {
        public const int MaxLevel = 40;
        public const int XpBase = 100;
        public const int XpStep = 50;
        /// <summary>Level-up reward = 25 * newLevel coins.</summary>
        public const int LevelUpCoinsPerLevel = 25;
        public const int MinigameBase = 10;
        public const int MinigameBonusCap = 15;
        public const int MinigameCoinDivisor = 2;
        public const int XpCompletedSleep = 10;

        /// <summary>XP required to advance from level L to L+1: 100 + 50*(L-1).</summary>
        public static int XpToNext(int level) => XpBase + XpStep * (level - 1);

        /// <summary>Cumulative XP needed to reach a level from level 1 (to L10 = 2700).</summary>
        public static int CumulativeXpToLevel(int level)
        {
            var sum = 0;
            for (var l = 1; l < level; l++) sum += XpToNext(l);
            return sum;
        }

        /// <summary>Apply an XP grant with multi-level-ups and the max-level cap. Pure.</summary>
        public static XpResult ApplyXp(double xpIn, int levelIn, double amount)
        {
            var level = J.ClampI(levelIn, 1, MaxLevel);
            var xp = Math.Max(0.0, xpIn);
            var gained = 0;
            var coins = 0;
            if (level >= MaxLevel)
                return new XpResult { Xp = 0, Level = level, LevelsGained = 0, CoinsAwarded = 0 };
            xp += Math.Max(0.0, amount);
            while (level < MaxLevel && xp >= XpToNext(level))
            {
                xp -= XpToNext(level);
                level += 1;
                gained += 1;
                coins += LevelUpCoinsPerLevel * level;
            }
            if (level >= MaxLevel) xp = 0.0;
            return new XpResult { Xp = xp, Level = level, LevelsGained = gained, CoinsAwarded = coins };
        }

        /// <summary>ApplyXp for a {"xp","level"} progress object (a saved 0 is a VALID value).</summary>
        public static XpResult ApplyXp(JObject progress, double amount)
        {
            var levelTok = progress?["level"];
            var level = J.IsNum(levelTok) ? (int)Math.Floor(levelTok.Value<double>()) : 1;
            var xp = J.NumOr(progress?["xp"], 0.0);
            return ApplyXp(xp, level, amount);
        }

        /// <summary>XP for finishing a minigame: 10 + min(15, floor(coinsEarned / 2)).</summary>
        public static int MinigameXp(double coinsEarned)
        {
            var bonus = (int)Math.Floor(Math.Max(0.0, coinsEarned) / MinigameCoinDivisor);
            return MinigameBase + Math.Min(MinigameBonusCap, bonus);
        }
    }
}
