using System;
using System.Collections.Generic;
using Gooby.Core.Json;
using Newtonsoft.Json.Linq;

namespace Gooby.Core.Life
{
    /// <summary>Pure stat math — 1:1 port of GOOBY/src/systems/stats.js (§C1) / scripts/logic/stats.gd.</summary>
    public static class Stats
    {
        public const double Min = 0.0;
        public const double Max = 100.0;
        public static readonly string[] Keys = { "hunger", "energy", "hygiene", "fun" };

        /// <summary>Decay per real minute while awake (app open or closed).</summary>
        public static readonly Dictionary<string, double> RatesAwake = new Dictionary<string, double>
        {
            { "hunger", -0.35 }, { "energy", -0.25 }, { "hygiene", -0.15 }, { "fun", -0.5 },
        };

        /// <summary>Per real minute while asleep: hunger half rate, energy fills 0→100 in 30 min.</summary>
        public static readonly Dictionary<string, double> RatesAsleep = new Dictionary<string, double>
        {
            { "hunger", -0.175 }, { "energy", 3.334 }, { "hygiene", 0.0 }, { "fun", 0.0 },
        };

        public const double LowStat = 25.0;
        public const double CriticalStat = 10.0;
        public const double ExhaustedAtOrBelow = 15.0;
        public const double ExhaustedMoodCap = 39.0;
        public const double MoodMinWeight = 0.35;
        public const double MoodAvgWeight = 0.65;

        public static readonly (string id, double min)[] MoodBands =
        {
            ("ecstatic", 80.0), ("happy", 60.0), ("neutral", 40.0), ("grumpy", 25.0), ("miserable", 0.0),
        };

        /// <summary>Clamp a stat to [0, 100]; non-finite/non-numeric → 0 (NaN poisoning guard).</summary>
        public static double ClampStat(double n)
        {
            if (double.IsNaN(n) || double.IsInfinity(n)) return Min;
            return Math.Min(Max, Math.Max(Min, n));
        }

        public static double ClampStat(JToken value) => ClampStat(J.NumStats(value));

        public static JObject ClampStats(JObject stats)
        {
            var o = new JObject();
            foreach (var k in Keys)
            {
                var n = J.NumStats(stats?[k]);
                o[k] = ClampStat(double.IsNaN(n) ? 0.0 : n);
            }
            return o;
        }

        /// <summary>One decay/fill tick. Pure — new object. rateMult 0.3 = offline sim (§E4).</summary>
        public static JObject ApplyTick(JObject stats, double dtMin, bool asleep = false, double rateMult = 1.0)
        {
            var rates = asleep ? RatesAsleep : RatesAwake;
            var o = new JObject();
            foreach (var k in Keys)
                o[k] = ClampStat(J.NumStats(stats?[k]) + rates[k] * dtMin * rateMult);
            return o;
        }

        /// <summary>Mood (§C1): 0.35*min + 0.65*avg, minus debuff, exhausted (energy ≤ 15) caps at 39.</summary>
        public static double Mood(JObject stats, double debuff = 0.0)
        {
            var lowest = double.PositiveInfinity;
            var sum = 0.0;
            foreach (var k in Keys)
            {
                var v = J.NumStats(stats?[k]);
                lowest = Math.Min(lowest, v);
                sum += v;
            }
            var avg = sum / Keys.Length;
            var m = MoodMinWeight * lowest + MoodAvgWeight * avg;
            m -= debuff;
            if (J.NumStats(stats?["energy"]) <= ExhaustedAtOrBelow) m = Math.Min(m, ExhaustedMoodCap);
            return Math.Min(Max, Math.Max(Min, m));
        }

        public static string MoodBand(double mood)
        {
            foreach (var (id, min) in MoodBands)
                if (mood >= min) return id;
            return "miserable";
        }

        public static bool IsLow(double value) => value < LowStat;
        public static bool IsCritical(double value) => value < CriticalStat;
        public static bool IsExhausted(JObject stats) => J.NumStats(stats?["energy"]) <= ExhaustedAtOrBelow;

        /// <summary>Apply stat deltas (food, wash, ...) with clamping. Pure — new object.</summary>
        public static JObject ApplyDeltas(JObject stats, IDictionary<string, double> deltas)
        {
            var o = new JObject();
            foreach (var k in Keys)
            {
                deltas.TryGetValue(k, out var d);
                o[k] = ClampStat(J.NumStats(stats?[k]) + d);
            }
            return o;
        }

        public static JObject ApplyDeltas(JObject stats, JObject deltas)
        {
            var d = new Dictionary<string, double>();
            foreach (var k in Keys)
            {
                var n = J.NumStats(deltas?[k]);
                d[k] = double.IsNaN(n) ? 0.0 : n;
            }
            return ApplyDeltas(stats, d);
        }
    }
}
