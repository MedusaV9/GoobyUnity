using System;
using System.Collections.Generic;
using Gooby.Core.Random;
using Newtonsoft.Json.Linq;

namespace Gooby.Core.Minigames.Games
{
    /// <summary>
    /// Blasen-Platzer — port of GOOBY/src/minigames/games/bubblePop.logic.js (§C6.1 #11): 60 s, the target
    /// food rotates every 12 s, match +2, wrong bubble −2 + 0.5 s stun, spiky bubbles never pop (tap −1).
    /// Rise and spawn cadence ramp linearly. Coin row /4, 4..24.
    /// </summary>
    public static class BubblePop
    {
        public sealed class Tune
        {
            public double DurationSec = 60.0, TargetRotateSec = 12.0;
            public int MatchPts = 2, WrongPts = -2;
            public double StunSec = 0.5;
            public int SpikyPts = -1;
            public double RiseStart = 0.62, RiseEnd = 1.1, SpawnSecStart = 0.9, SpawnSecEnd = 0.5, TargetChance = 0.52, SpikyChance = 0.15;
            public double ChainWindowSec = 2.0;
            public int ChainCount = 3;
            public double ChainRadius = 1.25, FoodTouchRadius = 0.42, SpikyTouchRadius = 0.6;
            public bool Endless = false;
            public int EndlessSpikyLimit = 3;
            public double EndlessSpawnFloorSec = 0.35;
            public Tune Clone() => (Tune)MemberwiseClone();
        }

        public static readonly Tune Bubble = new Tune();
        public static readonly string[] Foods = { "carrot", "apple", "banana", "cheese", "donut-sprinkles", "cupcake" };
        public const int MatchStreakEvery = 5;

        public static Tune ApplyDifficulty(Tune tune, string mode = "normal")
        {
            if (mode != "easy" && mode != "hard" && mode != "endless") return tune;
            var hard = mode == "hard" || mode == "endless";
            var spawnMult = hard ? 0.85 : 1.2;
            var windowMult = hard ? 0.8 : 1.25;
            var o = tune.Clone();
            o.DurationSec = hard ? tune.DurationSec : tune.DurationSec * 1.2;
            o.SpawnSecStart = tune.SpawnSecStart * spawnMult;
            o.SpawnSecEnd = tune.SpawnSecEnd * spawnMult;
            o.TargetRotateSec = Math.Max(0.35, tune.TargetRotateSec * windowMult);
            o.Endless = mode == "endless";
            return o;
        }

        private static double RampT(double elapsed, double duration, Tune tune) =>
            tune.Endless ? Math.Max(0.0, elapsed / duration) : Math.Min(1.0, Math.Max(0.0, elapsed / duration));

        public static double RiseSpeedAt(double elapsed, double duration, Tune tune)
        {
            var t = RampT(elapsed, duration, tune);
            return tune.RiseStart + (tune.RiseEnd - tune.RiseStart) * t;
        }

        public static double SpawnIntervalAt(double elapsed, double duration, Tune tune)
        {
            var t = RampT(elapsed, duration, tune);
            var interval = tune.SpawnSecStart + (tune.SpawnSecEnd - tune.SpawnSecStart) * t;
            return tune.Endless ? Math.Max(tune.EndlessSpawnFloorSec, interval) : interval;
        }

        public static int TargetIndexAt(double elapsed, Tune tune) => Math.Max(0, (int)Math.Floor(elapsed / tune.TargetRotateSec));

        /// <summary>Seeded target order: every food once before any repeats; no two equal neighbours.</summary>
        public static List<string> TargetOrder(GoobyRng rng, int count)
        {
            var order = new List<string>();
            while (order.Count < count)
            {
                var shuffled = new List<string>(Foods);
                for (var i = shuffled.Count - 1; i > 0; i--)
                {
                    var j = (int)Math.Floor(rng.Next() * (i + 1));
                    (shuffled[i], shuffled[j]) = (shuffled[j], shuffled[i]);
                }
                if (order.Count > 0 && shuffled[0] == order[order.Count - 1])
                {
                    var first = shuffled[0];
                    shuffled.RemoveAt(0);
                    shuffled.Add(first);
                }
                order.AddRange(shuffled);
            }
            return order.GetRange(0, count);
        }

        public struct BubbleKind
        {
            public string Kind;
            public string Food;
        }

        public static BubbleKind RollBubble(GoobyRng rng, string targetFood, Tune tune)
        {
            if (rng.Next() < tune.SpikyChance) return new BubbleKind { Kind = "spiky", Food = "" };
            if (rng.Next() < tune.TargetChance) return new BubbleKind { Kind = "food", Food = targetFood };
            var others = new List<string>();
            foreach (var f in Foods) if (f != targetFood) others.Add(f);
            var idx = Math.Min(others.Count - 1, (int)Math.Floor(rng.Next() * others.Count));
            return new BubbleKind { Kind = "food", Food = others[idx] };
        }

        public static (string result, int delta, double stunSec, bool pops) PopResult(BubbleKind bubble, string targetFood)
        {
            if (bubble.Kind == "spiky") return ("spiky", Bubble.SpikyPts, 0.0, false);
            if (bubble.Food == targetFood) return ("match", Bubble.MatchPts, 0.0, true);
            return ("wrong", Bubble.WrongPts, Bubble.StunSec, true);
        }

        public static int ApplyScore(int score, int delta) => Math.Max(0, score + delta);

        public static double TouchRadiusFor(string kind) => kind == "spiky" ? Bubble.SpikyTouchRadius : Bubble.FoodTouchRadius;

        public static bool MatchStreakMilestone(int streak) => streak > 0 && streak % MatchStreakEvery == 0;

        public static bool EndlessShouldEnd(int spikyPops, Tune tune) => tune.Endless && spikyPops >= tune.EndlessSpikyLimit;

        public static JObject SimulateAutoplay(int seed = 1, string mode = "normal")
        {
            var tune = ApplyDifficulty(Bubble, mode);
            var rng = new GoobyRng(seed);
            var duration = tune.Endless ? 90.0 : tune.DurationSec;
            var elapsed = 0.0;
            int score = 0, matches = 0, spikyPops = 0;
            var reactionAccuracy = Math.Min(0.93, 0.93 * (tune.TargetRotateSec / Bubble.TargetRotateSec));
            while (elapsed < duration && spikyPops < tune.EndlessSpikyLimit)
            {
                elapsed += SpawnIntervalAt(elapsed, tune.DurationSec, tune);
                var bubble = RollBubble(rng, "carrot", tune);
                if (bubble.Kind == "spiky")
                {
                    if (rng.Next() < 0.025) spikyPops += 1;
                    continue;
                }
                if (bubble.Food == "carrot" && rng.Next() < reactionAccuracy)
                {
                    score += tune.MatchPts;
                    matches += 1;
                    if (matches % tune.ChainCount == 0) score += tune.MatchPts;
                }
            }
            return new JObject { ["seed"] = seed, ["mode"] = mode, ["score"] = score, ["spikyPops"] = spikyPops };
        }
    }
}
