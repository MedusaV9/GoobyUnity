using System;
using System.Globalization;
using System.Linq;
using Gooby.Core.Json;
using Gooby.Core.Life;
using Newtonsoft.Json.Linq;

namespace Gooby.Core.Meta
{
    /// <summary>Daily login bonus (web-verbatim reward table, one grace day) — port of scripts/logic/daily/daily_bonus.gd.</summary>
    public static class DailyBonus
    {
        public static readonly int[] RewardTable = { 20, 30, 40, 50, 60, 80, 100 };
        public const int FoodFromDay = 7;

        /// <summary>"YYYY-MM-DD" of the previous calendar day; "" for broken input.</summary>
        public static string PrevDay(string day)
        {
            if (string.IsNullOrEmpty(day)) return "";
            var parts = day.Split('-');
            if (parts.Length != 3 || !int.TryParse(parts[0], out var y) || !int.TryParse(parts[1], out var m) || !int.TryParse(parts[2], out var d)) return "";
            try
            {
                var prev = new DateTime(y, m, d, 12, 0, 0, DateTimeKind.Utc).AddDays(-1);
                return prev.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            }
            catch (ArgumentOutOfRangeException) { return ""; }
        }

        public static bool IsClaimable(JObject daily, string day) => J.Str(daily?["lastClaimDay"]) != day;

        public static (int streak, bool grace) NextStreak(JObject daily, string day)
        {
            var streak = Math.Max(0, J.Int(daily?["streak"]));
            var last = J.Str(daily?["lastClaimDay"]);
            if (last.Length == 0 || streak <= 0) return (1, false);
            var yesterday = PrevDay(day);
            if (last == yesterday) return (streak + 1, false);
            if (yesterday.Length > 0 && last == PrevDay(yesterday)) return (streak + 1, true);
            return (1, false);
        }

        public static (int coins, bool includesFood) RewardForStreak(int streakDay)
        {
            var idx = J.ClampI(streakDay, 1, RewardTable.Length) - 1;
            return (RewardTable[idx], streakDay >= FoodFromDay);
        }

        public static string PickBonusFood(double rngValue)
        {
            var ids = FoodCatalog.Foods.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();
            var i = J.ClampI((int)Math.Floor(rngValue * ids.Count), 0, ids.Count - 1);
            return ids[i];
        }

        public sealed class ClaimResult
        {
            public bool Ok;
            public int StreakDay, Coins;
            public string FoodId = "";
            public bool Grace;
        }

        /// <summary>Claims today's bonus (mutates state). rngValue &lt; 0 → random pick.</summary>
        public static ClaimResult Claim(JObject state, string day, double rngValue = -1.0, System.Random random = null)
        {
            if (!(state["daily"] is JObject)) state["daily"] = new JObject { ["lastClaimDay"] = "", ["streak"] = 0 };
            var daily = (JObject)state["daily"];
            if (!IsClaimable(daily, day)) return new ClaimResult { Ok = false };
            var next = NextStreak(daily, day);
            var reward = RewardForStreak(next.streak);
            var foodId = "";
            if (reward.includesFood)
            {
                var roll = rngValue >= 0.0 ? rngValue : (random ?? new System.Random()).NextDouble();
                foodId = PickBonusFood(roll);
                if (!(state["inventory"] is JObject)) state["inventory"] = new JObject { ["food"] = new JObject(), ["items"] = new JObject() };
                var food = J.EnsureDict((JObject)state["inventory"], "food");
                food[foodId] = Math.Max(0, J.Int(food[foodId])) + 1;
            }
            if (state["economy"] is JObject econ) Economy.Award(econ, reward.coins, "dailyBonus");
            daily["lastClaimDay"] = day;
            daily["streak"] = next.streak;
            return new ClaimResult { Ok = true, StreakDay = next.streak, Coins = reward.coins, FoodId = foodId, Grace = next.grace };
        }
    }
}
