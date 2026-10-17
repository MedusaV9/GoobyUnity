using System;
using Gooby.Core.Json;
using Newtonsoft.Json.Linq;

namespace Gooby.Core.Life
{
    /// <summary>
    /// Pure coin math — port of GOOBY/src/systems/economy.js award/spend core (§C1.5/§C-SYS11)
    /// / scripts/logic/economy.gd. Operates on ONE economy slice object (mutates in place).
    /// </summary>
    public static class Economy
    {
        /// <summary>Modifier surplus (doppelGold + glueckspilz) per local day.</summary>
        public const int DayCoinCap = 150;
        /// <summary>Coins from reason 'endless' per local day.</summary>
        public const int EndlessDayCap = 100;
        public const double QuickDeliveryMarkup = 0.2;
        public const int QuickDeliveryPrice = 400;
        public const int QuickDeliveryLevel = 8;
        public const int StartingCoins = 100;

        public static JObject DefaultSlice() => new JObject
        {
            ["coins"] = StartingCoins,
            ["coinsEarned"] = 0,
            ["coinsSpent"] = 0,
            ["dayCoins"] = 0,
            ["dayCoinsDay"] = "",
            ["endlessCoins"] = 0,
            ["endlessCoinsDay"] = "",
        };

        /// <summary>Integer ≥ 0, fractions round AGAINST the player.</summary>
        public static int NormAward(double amount) => Math.Max(0, (int)Math.Floor(amount));

        /// <summary>Integer ≥ 0, rounded UP.</summary>
        public static int NormCost(double amount) => Math.Max(0, (int)Math.Ceiling(amount));

        public static int Coins(JObject econ) => J.Int(econ?["coins"]);

        public static bool CanAfford(JObject econ, double amount) => Coins(econ) >= NormCost(amount);

        /// <summary>
        /// Grant coins (floored, never negative). Returns the coins ACTUALLY granted after the
        /// day caps (0 = capped — UI renders the "Tagesbonus erreicht" note).
        /// </summary>
        public static int Award(JObject econ, double amount, string reason = "", string day = "")
        {
            var n = NormAward(amount);
            if (n == 0) return 0;
            var granted = n;
            if (reason == "glueckspilz" || reason == "modifier")
                granted = BookDayLedger(econ, n, day, "dayCoins", "dayCoinsDay", DayCoinCap);
            else if (reason == "endless")
                granted = BookDayLedger(econ, n, day, "endlessCoins", "endlessCoinsDay", EndlessDayCap);
            if (granted > 0)
            {
                econ["coins"] = Coins(econ) + granted;
                econ["coinsEarned"] = J.Int(econ["coinsEarned"]) + granted;
            }
            return granted;
        }

        /// <summary>Spend atomically: full amount or nothing (never partial, never negative).</summary>
        public static bool Spend(JObject econ, double amount, string reason = "")
        {
            var n = NormCost(amount);
            if (!CanAfford(econ, n)) return false;
            econ["coins"] = Coins(econ) - n;
            econ["coinsSpent"] = J.Int(econ["coinsSpent"]) + n;
            return true;
        }

        /// <summary>Quick-delivery price: +20% rounded UP, integer-cent math (5 → 6, not 7).</summary>
        public static int QuickPrice(double basePrice)
        {
            var cents = Math.Round(Math.Max(0.0, basePrice) * 100.0 * (1.0 + QuickDeliveryMarkup), MidpointRounding.AwayFromZero);
            return (int)Math.Ceiling(cents / 100.0);
        }

        private static int BookDayLedger(JObject econ, int amount, string day, string coinsKey, string dayKey, int cap)
        {
            if (J.Str(econ[dayKey]) != day)
            {
                econ[coinsKey] = 0;
                econ[dayKey] = day;
            }
            var headroom = Math.Max(0, cap - J.Int(econ[coinsKey]));
            var granted = Math.Min(Math.Max(0, amount), headroom);
            econ[coinsKey] = J.Int(econ[coinsKey]) + granted;
            return granted;
        }
    }
}
