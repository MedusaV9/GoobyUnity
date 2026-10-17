using System;
using System.Collections.Generic;
using Gooby.Core.Random;
using Newtonsoft.Json.Linq;

namespace Gooby.Core.Minigames.Games
{
    /// <summary>
    /// Memory — port of GOOBY/src/minigames/games/memoryMatch.logic.js (§C6.1 #5): 4×4 with 8 pairs, from
    /// level 6 4×6 with 12 pairs; score = 20 − misses + time bonus (0–8) + 20 board bonus, cap 48. No fail state.
    /// </summary>
    public static class MemoryMatch
    {
        public const int BigLayoutLevel = 6;

        public sealed class Tune
        {
            public int ScoreBase = 20, TimeBonusMax = 8;
            public double TimeBonusStepSec = 5.0, ParSecSmall = 48.0, ParSecBig = 85.0;
            public double CardW = 0.82, CardH = 1.0, SpacingX = 0.93, SpacingY = 1.12, RevealSec = 0.85, FlipSec = 0.28;
            public int PeekEarnMatches = 3;
            public double PeekSec = 1.0, PreviewSpeedMult = 1.0, WindowMult = 1.0;
            public int RampFloorStep = 0, ClearBonus = 20;
            public bool Endless = false;
            public int EndlessMissFlips = 12;
            public string Mode = "normal";
            public Tune Clone() => (Tune)MemberwiseClone();
        }

        public struct Layout
        {
            public int Cols, Rows, Pairs;
        }

        public static readonly Tune Memory = new Tune();
        public static readonly Layout Small = new Layout { Cols = 4, Rows = 4, Pairs = 8 };
        public static readonly Layout Big = new Layout { Cols = 4, Rows = 6, Pairs = 12 };
        public static readonly string[] FaceKeys =
        {
            "carrot", "apple", "banana", "cheese", "watermelon", "donut-sprinkles", "cupcake", "burger", "ice-cream", "pizza", "cake", "strawberry",
        };

        public static Tune ApplyDifficulty(Tune tune, string mode = "normal")
        {
            double previewSpeed, window;
            int rampFloor, bonus;
            bool endless;
            switch (mode)
            {
                case "easy": previewSpeed = 0.85; window = 1.25; rampFloor = 0; bonus = 20; endless = false; break;
                case "hard": previewSpeed = 1.15; window = 0.8; rampFloor = -1; bonus = 20; endless = false; break;
                case "endless": previewSpeed = 1.15; window = 0.8; rampFloor = -1; bonus = 20; endless = true; break;
                default: return tune;
            }
            var o = tune.Clone();
            o.RevealSec = Math.Max(0.35, tune.RevealSec * window);
            o.PeekSec = Math.Max(0.35, tune.PeekSec * window);
            o.FlipSec = tune.FlipSec / previewSpeed;
            o.PreviewSpeedMult = previewSpeed;
            o.WindowMult = window;
            o.RampFloorStep = rampFloor;
            o.ClearBonus = bonus;
            o.Endless = endless;
            o.Mode = mode;
            return o;
        }

        public static Layout LayoutForLevel(int level) => level >= BigLayoutLevel ? Big : Small;

        public static List<int> BuildDeck(int pairs, GoobyRng rng)
        {
            var deck = new List<int>(pairs * 2);
            for (var i = 0; i < pairs; i++) { deck.Add(i); deck.Add(i); }
            for (var i = deck.Count - 1; i > 0; i--)
            {
                var j = (int)Math.Floor(rng.Next() * (i + 1));
                (deck[i], deck[j]) = (deck[j], deck[i]);
            }
            return deck;
        }

        public static int TimeBonus(double elapsed, Layout layout)
        {
            var par = layout.Pairs > Small.Pairs ? Memory.ParSecBig : Memory.ParSecSmall;
            var over = Math.Max(0.0, elapsed - par);
            var bonus = Memory.TimeBonusMax - (int)Math.Ceiling(over / Memory.TimeBonusStepSec);
            return Math.Max(0, Math.Min(Memory.TimeBonusMax, bonus));
        }

        public static int Score(int misses, double elapsed, Layout layout, Tune tune) =>
            Math.Max(0, tune.ScoreBase - misses + TimeBonus(elapsed, layout) + tune.ClearBonus);

        public static bool IsMatch(int a, int b) => a == b;

        public static bool EndlessShouldEnd(int misses, Tune tune) => tune.Endless && misses >= tune.EndlessMissFlips;

        public static (double width, double height) GridExtents(Layout layout) =>
            ((layout.Cols - 1) * Memory.SpacingX + Memory.CardW, (layout.Rows - 1) * Memory.SpacingY + Memory.CardH);

        public static JObject SimulateAutoplay(int seed = 1, string mode = "normal")
        {
            var tune = ApplyDifficulty(Memory, mode);
            var rng = new GoobyRng(seed);
            var layout = Big;
            var recallFail = mode == "easy" ? 0.04 : (mode == "hard" || mode == "endless" ? 0.12 : 0.08);
            var misses = 0;
            for (var i = 0; i < layout.Pairs; i++) if (rng.Next() < recallFail) misses += 1;
            var pace = mode == "easy" ? 1.2 : (mode == "hard" ? 0.92 : 1.05);
            var elapsed = layout.Pairs * pace;
            var rawTune = tune.Clone();
            rawTune.ClearBonus = 0;
            return new JObject
            {
                ["seed"] = seed, ["mode"] = mode,
                ["score"] = Score(misses, elapsed, layout, tune),
                ["rawScore"] = Score(misses, elapsed, layout, rawTune),
                ["misses"] = misses, ["elapsed"] = elapsed,
            };
        }
    }
}
