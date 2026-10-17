using System;
using Gooby.Core.Json;
using Newtonsoft.Json.Linq;

namespace Gooby.Core.Life
{
    /// <summary>
    /// Weight/fitness model — port of GOOBY/src/systems/weight.js (§B5/§C4) / scripts/logic/weight.gd.
    /// COSMETIC ONLY: never changes stats, decay, availability, scores or prices.
    /// </summary>
    public static class Weight
    {
        public const double Min = 5.0;
        public const double Max = 95.0;
        public const double Default = 50.0;
        public const double EatJunk = 2.0;
        public const double EatHealthy = 0.5;
        public const double GameActive = -1.0;
        public const double GameOther = -0.25;
        public const double BallFetch = -0.2;
        public const double DriftTarget = 50.0;
        public const double DriftPerMin = 2.0 / 1440.0;

        public static readonly string[] ActiveGames =
        {
            "runner", "trampoline", "danceParty", "bunnyHop", "gardenRush", "veggieChop", "goalieGooby", "starHopper",
        };

        public const double TierSleekMax = 25.0;
        public const double TierChubbyMax = 60.0;
        public const double TierChonkyMax = 85.0;
        public static readonly string[] Tiers = { "sleek", "chubby", "chonky", "floof" };

        private static readonly (double w, double s)[] ScaleAnchors = { (15.0, 0.93), (42.5, 1.0), (72.5, 1.07), (90.0, 1.14) };

        public static double TierScale(string tier)
        {
            switch (tier)
            {
                case "sleek": return 0.93;
                case "chonky": return 1.07;
                case "floof": return 1.14;
                default: return 1.0;
            }
        }

        public static double ClampWeight(double n)
        {
            if (double.IsNaN(n) || double.IsInfinity(n)) n = Default;
            return J.Clamp(n, Min, Max);
        }

        public static double ClampWeight(JToken value) => ClampWeight(J.NumOr(value, Default));

        public static double OnEat(double value, bool junk) => ClampWeight(ClampWeight(value) + (junk ? EatJunk : EatHealthy));

        public static double OnMinigameEnd(double value, string gameId)
        {
            var delta = Array.IndexOf(ActiveGames, gameId) >= 0 ? GameActive : GameOther;
            return ClampWeight(ClampWeight(value) + delta);
        }

        public static double OnBallFetch(double value) => ClampWeight(ClampWeight(value) + BallFetch);

        /// <summary>Passive drift towards 50 with ±2.0 per 24 h, never past the target.</summary>
        public static double Tick(double value, double dtMin, double mult = 1.0)
        {
            var v = ClampWeight(value);
            var step = DriftPerMin * Math.Max(0.0, dtMin) * mult;
            if (v > DriftTarget) v = Math.Max(DriftTarget, v - step);
            else if (v < DriftTarget) v = Math.Min(DriftTarget, v + step);
            return ClampWeight(v);
        }

        public static string TierOf(double value)
        {
            var v = double.IsNaN(value) ? Default : value;
            if (v <= TierSleekMax) return "sleek";
            if (v <= TierChubbyMax) return "chubby";
            if (v <= TierChonkyMax) return "chonky";
            return "floof";
        }

        /// <summary>Continuous body X/Z scale for the rig (linear through the tier anchors).</summary>
        public static double BodyScale(double value)
        {
            var v = ClampWeight(value);
            if (v <= ScaleAnchors[0].w) return ScaleAnchors[0].s;
            for (var i = 1; i < ScaleAnchors.Length; i++)
            {
                var a = ScaleAnchors[i - 1];
                var b = ScaleAnchors[i];
                if (v <= b.w)
                {
                    var t = (v - a.w) / (b.w - a.w);
                    return a.s + (b.s - a.s) * t;
                }
            }
            return ScaleAnchors[ScaleAnchors.Length - 1].s;
        }
    }
}
