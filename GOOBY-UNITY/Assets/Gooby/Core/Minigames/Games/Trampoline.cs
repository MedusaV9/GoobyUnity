using System;
using System.Collections.Generic;
using Gooby.Core.Random;
using Newtonsoft.Json.Linq;

namespace Gooby.Core.Minigames.Games
{
    /// <summary>
    /// Trampolin-Tricks — port of GOOBY/src/minigames/games/trampoline.logic.js (§C6.1 #12): tap in the
    /// shrinking landing window = boost, swipe in the air = flip/spin/twist (points × height factor 1–3),
    /// missed window = butt landing and the height falls back. 60 s, score = trick points only. Coin row /5, 4..26.
    /// </summary>
    public static class Trampoline
    {
        public sealed class Tune
        {
            public double DurationSec = 60.0, Tier2Apex = 2.1, Tier3Apex = 3.3;
            public double WindowBaseSec = 0.3, WindowShrinkPerWu = 0.045, WindowMinSec = 0.1, JudgeZoneSec = 0.5;
            public double Gravity = 9.0, BaseVy = 5.0, BoostMult = 1.16, BoostAdd = 0.35, MaxVy = 8.6, DecayMult = 0.94, MinVy = 4.2;
            public double TrickMinAirSec = 0.35;
            public int ComboTricks = 3, ComboFlipPoints = 12;
            public double ButtStaggerSec = 1.1;
            public bool Endless = false;
            public int EndlessFailureLimit = 3;
            public Tune Clone() => (Tune)MemberwiseClone();
        }

        public static readonly Tune Tramp = new Tune();

        public static int TrickPts(string kind)
        {
            switch (kind)
            {
                case "twist": return 3;
                default: return 2; // flip, spin
            }
        }

        public static Tune ApplyDifficulty(Tune tune, string mode = "normal")
        {
            if (mode != "easy" && mode != "hard" && mode != "endless") return tune;
            var hard = mode == "hard" || mode == "endless";
            var tolerance = hard ? 0.8 : 1.25;
            var o = tune.Clone();
            o.WindowBaseSec = Math.Max(tune.WindowBaseSec * 0.55, tune.WindowBaseSec * tolerance);
            o.WindowMinSec = Math.Max(tune.WindowMinSec * 0.55, tune.WindowMinSec * tolerance);
            o.JudgeZoneSec = Math.Max(0.35, tune.JudgeZoneSec * tolerance);
            o.Endless = mode == "endless";
            return o;
        }

        public static Tune WithHitbox(Tune tune, double hitboxMult = 1.0)
        {
            var mult = !double.IsNaN(hitboxMult) && !double.IsInfinity(hitboxMult) && hitboxMult > 0.0 ? hitboxMult : 1.0;
            if (Math.Abs(mult - 1.0) < 1e-9) return tune;
            var o = tune.Clone();
            o.WindowBaseSec = tune.WindowBaseSec * mult;
            o.WindowMinSec = tune.WindowMinSec * mult;
            o.JudgeZoneSec = tune.JudgeZoneSec * mult;
            return o;
        }

        public static double WindowSecFor(double apexH, Tune tune) => Math.Max(tune.WindowMinSec, tune.WindowBaseSec - tune.WindowShrinkPerWu * apexH);

        public static int HeightMultiplier(double apexH)
        {
            if (apexH >= Tramp.Tier3Apex) return 3;
            if (apexH >= Tramp.Tier2Apex) return 2;
            return 1;
        }

        public static double ApexFor(double vy, double g = 9.0) => (vy * vy) / (2.0 * g);
        public static double AirTimeFor(double vy, double g = 9.0) => (2.0 * vy) / g;
        public static double TimeToImpact(double h, double vy, double g = 9.0) => (vy + Math.Sqrt(vy * vy + 2.0 * g * Math.Max(0.0, h))) / g;

        public static string ClassifyLandingTap(double tti, double apexH, Tune tune)
        {
            if (tti <= WindowSecFor(apexH, tune)) return "boost";
            if (tti <= tune.JudgeZoneSec) return "butt";
            return "ignore";
        }

        public static double NextBounceVy(double vy, string action, Tune tune)
        {
            if (action == "boost") return Math.Min(tune.MaxVy, vy * tune.BoostMult + tune.BoostAdd);
            if (action == "butt") return tune.BaseVy;
            return Math.Max(tune.MinVy, vy * tune.DecayMult);
        }

        public static int TrickPoints(string kind, int mult) => TrickPts(kind) * mult;

        public static bool CanTrick(bool airborne, double tti, bool tricking, Tune tune) => airborne && !tricking && tti > tune.TrickMinAirSec;

        public sealed class TrickChain
        {
            public readonly List<string> Seen = new List<string>();
            public bool Awarded;
        }

        public static (bool triggered, int bonus) RecordTrick(TrickChain chain, string kind)
        {
            if (!chain.Seen.Contains(kind)) chain.Seen.Add(kind);
            var triggered = !chain.Awarded && chain.Seen.Count >= Tramp.ComboTricks;
            if (triggered) chain.Awarded = true;
            return (triggered, triggered ? Tramp.ComboFlipPoints : 0);
        }

        public static bool CrossedMat(double previousH, double nextH, double nextVy) => previousH > 0.0 && nextH <= 0.0 && nextVy < 0.0;

        public static bool EndlessShouldEnd(int failedLandings, Tune tune) => tune.Endless && failedLandings >= tune.EndlessFailureLimit;

        public static JObject SimulateAutoplay(int seed = 1, string mode = "normal")
        {
            var tune = ApplyDifficulty(Tramp, mode);
            var rng = new GoobyRng(seed);
            var duration = tune.Endless ? 90.0 : tune.DurationSec;
            var t = 0.0;
            var vy = tune.BaseVy;
            int score = 0, failures = 0;
            while (t < duration && failures < tune.EndlessFailureLimit)
            {
                var apex = ApexFor(vy, tune.Gravity);
                var win = WindowSecFor(apex, tune);
                var success = rng.Next() < Math.Min(0.96, 0.7 + win * 0.8);
                var mult = HeightMultiplier(apex);
                if (success)
                {
                    score += TrickPoints("twist", mult) + TrickPoints("flip", mult);
                    if (rng.Next() < 0.55) score += tune.ComboFlipPoints;
                    vy = NextBounceVy(vy, "boost", tune);
                }
                else
                {
                    failures += 1;
                    vy = NextBounceVy(vy, "butt", tune);
                }
                t += AirTimeFor(vy, tune.Gravity);
                if (!success) t += tune.ButtStaggerSec;
            }
            return new JObject { ["seed"] = seed, ["mode"] = mode, ["score"] = score, ["failures"] = failures };
        }
    }
}
