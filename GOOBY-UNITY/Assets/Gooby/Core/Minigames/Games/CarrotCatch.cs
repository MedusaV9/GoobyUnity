using System;
using Gooby.Core.Random;
using Newtonsoft.Json.Linq;

namespace Gooby.Core.Minigames.Games
{
    /// <summary>
    /// Möhrenfang — port of GOOBY/src/minigames/games/carrotCatch.logic.js (§C6.1/G8): 60-s round, good food
    /// +1–3 by rarity, junk −2 + 0.5 s dizzy, fall speed +8 %/10 s (stepped), junk ratio 10 %→30 %, one golden
    /// carrot +10. Coin row /3, 4..25.
    /// </summary>
    public static class CarrotCatch
    {
        public sealed class Tune
        {
            public double DurationSec = 60.0, FallRampPct = 0.08, FallRampEverySec = 10.0;
            public double JunkRatioStart = 0.1, JunkRatioEnd = 0.3;
            public int JunkPenalty = -2;
            public double DizzySec = 0.5;
            public int GoldenPoints = 10;
            public double GoldenSpeedMult = 1.5, GoldenWindowStartSec = 15.0, GoldenWindowEndSec = 35.0, GoldenFallLeadSec = 5.0;
            public double RottenRatioOfJunk = 0.35, BasketHalfWidth = 0.62, SpawnEdgePad = 0.5, FallBaseSpeed = 2.3;
            public double SpawnBaseSec = 1.05, SpawnEndFraction = 0.72;
            public double SpawnIntervalMult = 1.0, WindowMult = 1.0, DurationMult = 1.0, SpeedMult = 1.0, ScoreMult = 1.0;
            public bool Endless = false;
            public int EndlessMissedCarrots = 3;
            public string Mode = "normal";
            public Tune Clone() => (Tune)MemberwiseClone();
        }

        public static readonly Tune Catch = new Tune();

        public struct Food
        {
            public string Key;
            public int Value, Weight;
        }

        public static readonly Food[] GoodFoods =
        {
            new Food { Key = "carrot", Value = 1, Weight = 26 }, new Food { Key = "apple", Value = 1, Weight = 15 },
            new Food { Key = "banana", Value = 1, Weight = 14 }, new Food { Key = "cheese", Value = 2, Weight = 8 },
            new Food { Key = "watermelon", Value = 2, Weight = 8 }, new Food { Key = "donut-sprinkles", Value = 2, Weight = 7 },
            new Food { Key = "cupcake", Value = 2, Weight = 7 }, new Food { Key = "burger", Value = 3, Weight = 6 },
            new Food { Key = "ice-cream", Value = 3, Weight = 5 }, new Food { Key = "cake", Value = 3, Weight = 4 },
        };

        public static readonly string[] JunkFoods = { "soda-can-crushed", "fish-bones" };
        public const int ComboMilestoneEvery = 5;

        public static Tune ApplyDifficulty(Tune tune, string mode = "normal")
        {
            double spawn, window, duration;
            bool endless;
            switch (mode)
            {
                case "easy": spawn = 1.2; window = 1.25; duration = 1.2; endless = false; break;
                case "hard": spawn = 0.85; window = 0.8; duration = 1.0; endless = false; break;
                case "endless": spawn = 0.85; window = 0.8; duration = 1.0; endless = true; break;
                default: return tune;
            }
            var o = tune.Clone();
            o.DurationSec = tune.DurationSec * duration;
            o.SpawnBaseSec = tune.SpawnBaseSec * spawn;
            o.BasketHalfWidth = Math.Max(tune.BasketHalfWidth * 0.55, tune.BasketHalfWidth * window);
            o.SpawnIntervalMult = spawn;
            o.WindowMult = window;
            o.DurationMult = duration;
            o.Endless = endless;
            o.Mode = mode;
            return o;
        }

        /// <summary>The only allowed gameplay modifier: turbo (§C-SYS4.3).</summary>
        public static Tune ApplyModifier(Tune tune, string type, double speedMult, double scoreMult)
        {
            if (type != "turbo") return tune;
            var sm = Math.Max(0.1, speedMult);
            var o = tune.Clone();
            o.FallBaseSpeed = tune.FallBaseSpeed * sm;
            o.SpawnBaseSec = tune.SpawnBaseSec / sm;
            o.SpeedMult = sm;
            o.ScoreMult = Math.Max(0.0, scoreMult);
            return o;
        }

        public static double FallSpeedMultAt(double elapsed, Tune tune)
        {
            var steps = Math.Max(0, (int)Math.Floor(elapsed / tune.FallRampEverySec));
            return Math.Pow(1.0 + tune.FallRampPct, steps);
        }

        public static double FallSpeedAt(double elapsed, Tune tune) => tune.FallBaseSpeed * FallSpeedMultAt(elapsed, tune);

        public static double JunkRatioAt(double elapsed, double duration = 60.0)
        {
            var t = Math.Min(1.0, Math.Max(0.0, elapsed / duration));
            return Catch.JunkRatioStart + (Catch.JunkRatioEnd - Catch.JunkRatioStart) * t;
        }

        public static double SpawnIntervalAt(double elapsed, double duration, Tune tune)
        {
            var t = Math.Min(1.0, Math.Max(0.0, elapsed / duration));
            return tune.SpawnBaseSec * (1.0 - (1.0 - tune.SpawnEndFraction) * t);
        }

        public struct Item
        {
            public string Kind, Key;
            public int Value;
        }

        public static Item RollItem(GoobyRng rng, double elapsed, Tune tune)
        {
            if (rng.Next() < JunkRatioAt(elapsed, tune.DurationSec))
            {
                if (rng.Next() < tune.RottenRatioOfJunk) return new Item { Kind = "rotten", Key = "carrot", Value = tune.JunkPenalty };
                var idx = Math.Min(JunkFoods.Length - 1, (int)Math.Floor(rng.Next() * JunkFoods.Length));
                return new Item { Kind = "junk", Key = JunkFoods[idx], Value = tune.JunkPenalty };
            }
            var total = 0.0;
            foreach (var f in GoodFoods) total += f.Weight;
            var roll = rng.Next() * total;
            foreach (var f in GoodFoods)
            {
                roll -= f.Weight;
                if (roll < 0.0) return new Item { Kind = "good", Key = f.Key, Value = f.Value };
            }
            var last = GoodFoods[GoodFoods.Length - 1];
            return new Item { Kind = "good", Key = last.Key, Value = last.Value };
        }

        public static int ApplyCatch(int score, int value) => Math.Max(0, score + value);

        public static double GoldenSpawnAt(GoobyRng rng, double duration, Tune tune)
        {
            var latest = Math.Max(0.0, Math.Min(tune.GoldenWindowEndSec, duration - tune.GoldenFallLeadSec));
            var earliest = Math.Min(tune.GoldenWindowStartSec, latest);
            return earliest + rng.Next() * (latest - earliest);
        }

        public static double ItemFallSpeed(double elapsed, string kind, Tune tune) =>
            FallSpeedAt(elapsed, tune) * (kind == "golden" ? tune.GoldenSpeedMult : 1.0);

        public static double SpawnXForRoll(double roll, double halfW)
        {
            var bound = Math.Max(0.0, halfW - Catch.SpawnEdgePad);
            return (Math.Min(1.0, Math.Max(0.0, roll)) * 2.0 - 1.0) * bound;
        }

        public static bool BasketCatchesX(double itemX, double basketX, Tune tune) =>
            Math.Abs(itemX - basketX) <= tune.BasketHalfWidth + 1.7763568394002505e-15;

        public static bool IsRoundOver(double elapsed, int missedCarrots, Tune tune, double durationSec = -1.0)
        {
            var dur = durationSec < 0.0 ? tune.DurationSec : durationSec;
            if (tune.Endless) return missedCarrots >= tune.EndlessMissedCarrots;
            return elapsed >= dur;
        }

        public static int FinalScore(int score, Tune tune) => Math.Max(0, FrameworkLogic.JsRound(score * tune.ScoreMult));

        public static bool ComboMilestone(int combo) => combo > 0 && combo % ComboMilestoneEvery == 0;

        public static JObject SimulateAutoplay(string mode = "normal", int seed = 1)
        {
            var tune = ApplyDifficulty(Catch, mode);
            var rng = new GoobyRng(seed);
            var skill = mode == "easy" ? 0.98 : (mode == "hard" || mode == "endless" ? 0.76 : 0.94);
            int score = 0, missedCarrots = 0;
            var elapsed = 0.0;
            var limit = tune.Endless ? 240.0 : tune.DurationSec;
            while (elapsed < limit)
            {
                var item = RollItem(rng, elapsed, tune);
                var good = item.Kind == "good";
                var caught = rng.Next() < (good ? skill : 0.04);
                if (caught) score = ApplyCatch(score, item.Value);
                else if (tune.Endless && good && item.Key == "carrot") missedCarrots += 1;
                elapsed += SpawnIntervalAt(elapsed, tune.DurationSec, tune);
                if (IsRoundOver(elapsed, missedCarrots, tune)) break;
            }
            if (!tune.Endless && rng.Next() < skill) score += tune.GoldenPoints;
            return new JObject { ["score"] = FinalScore(score, tune), ["elapsed"] = elapsed, ["missedCarrots"] = missedCarrots };
        }
    }
}
