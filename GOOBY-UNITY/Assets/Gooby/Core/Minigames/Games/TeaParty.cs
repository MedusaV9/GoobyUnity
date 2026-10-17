using System;
using Gooby.Core.Random;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Gooby.Core.Minigames.Games
{
    /// <summary>
    /// Teestube — number-identical port of GOOBY/src/minigames/games/teaParty.logic.js (§V5.1/G06):
    /// hold to pour, release inside the band (perfect +6 / good +3), overflow/outside = spill,
    /// every 3rd perfect in a row +2. Coin row: divisor 4, min 4, max 26.
    /// </summary>
    public static class TeaParty
    {
        public sealed class Tune
        {
            [JsonProperty("DURATION_SEC")] public double DurationSec = 60.0;
            [JsonProperty("FILL_RATE")] public double FillRate = 0.5;
            [JsonProperty("BAND_CENTER_MIN")] public double BandCenterMin = 0.55;
            [JsonProperty("BAND_CENTER_MAX")] public double BandCenterMax = 0.85;
            [JsonProperty("BAND_HALF_W")] public double BandHalfW = 0.075;
            [JsonProperty("PERFECT_HALF_W")] public double PerfectHalfW = 0.028;
            [JsonProperty("PERFECT_PTS")] public int PerfectPts = 6;
            [JsonProperty("GOOD_PTS")] public int GoodPts = 3;
            [JsonProperty("STREAK_EVERY")] public int StreakEvery = 3;
            [JsonProperty("STREAK_BONUS")] public int StreakBonus = 2;
            [JsonProperty("SERVE_SEC_START")] public double ServeSecStart = 1.7;
            [JsonProperty("SERVE_SEC_END")] public double ServeSecEnd = 1.1;
            [JsonProperty("OVERFLOW_LEVEL")] public double OverflowLevel = 1.0;
            [JsonProperty("ENDLESS")] public bool Endless = false;
            [JsonProperty("ENDLESS_MAX_SPILLS")] public int EndlessMaxSpills = 3;
            [JsonProperty("AUTOPLAY_AIM_ERR")] public double AutoplayAimErr = 0.06;
            public Tune Clone() => (Tune)MemberwiseClone();
        }

        public static readonly Tune Tea = new Tune();

        public struct Band
        {
            public double Center, Half, PerfectHalf;
        }

        public struct PourResult
        {
            public string Result;
            public int Points;
            public bool Overflow;
        }

        private struct Row { public double FillMult, BandMult, DurationMult, ServeMult, BotErr; }

        private static bool RowFor(string mode, out Row row)
        {
            switch (mode)
            {
                case "easy": row = new Row { FillMult = 0.8, BandMult = 1.25, DurationMult = 1.2, ServeMult = 1.0, BotErr = 0.05 }; return true;
                case "hard":
                case "endless": row = new Row { FillMult = 1.2, BandMult = 0.8, DurationMult = 1.0, ServeMult = 0.85, BotErr = 0.068 }; return true;
                default: row = default; return false;
            }
        }

        public static Tune ApplyDifficulty(Tune tune, string mode = "normal")
        {
            if (mode == "normal" || !RowFor(mode, out var row)) return tune;
            var o = tune.Clone();
            o.DurationSec = tune.DurationSec * row.DurationMult;
            o.FillRate = tune.FillRate * row.FillMult;
            o.BandHalfW = tune.BandHalfW * row.BandMult;
            o.PerfectHalfW = tune.PerfectHalfW * row.BandMult;
            o.ServeSecStart = tune.ServeSecStart * row.ServeMult;
            o.ServeSecEnd = tune.ServeSecEnd * row.ServeMult;
            o.AutoplayAimErr = row.BotErr;
            o.Endless = mode == "endless";
            return o;
        }

        public static Band RollBand(GoobyRng rng, Tune tune)
        {
            var span = tune.BandCenterMax - tune.BandCenterMin;
            return new Band { Center = tune.BandCenterMin + rng.Next() * span, Half = tune.BandHalfW, PerfectHalf = tune.PerfectHalfW };
        }

        public static double FillAfter(double level, double dt, Tune tune) => Math.Max(0.0, level) + tune.FillRate * Math.Max(0.0, dt);

        public static PourResult Pour(double level, Band band, Tune tune)
        {
            var overflow = level >= tune.OverflowLevel;
            var dist = Math.Abs(level - band.Center);
            if (!overflow && dist <= band.PerfectHalf) return new PourResult { Result = "perfect", Points = tune.PerfectPts };
            if (!overflow && dist <= band.Half) return new PourResult { Result = "good", Points = tune.GoodPts };
            return new PourResult { Result = "miss", Points = 0, Overflow = overflow };
        }

        public static int StreakBonusAt(int streak, Tune tune) => streak > 0 && streak % tune.StreakEvery == 0 ? tune.StreakBonus : 0;

        public static double ServeIntervalAt(double elapsed, double duration, Tune tune)
        {
            var t = Math.Min(1.0, Math.Max(0.0, elapsed / duration));
            return tune.ServeSecStart + (tune.ServeSecEnd - tune.ServeSecStart) * t;
        }

        public static int ApplyScore(int score, int delta) => Math.Max(0, score + delta);

        public static bool EndlessShouldEnd(int spills, Tune tune) => tune.Endless && spills >= tune.EndlessMaxSpills;

        public static JObject SimulateAutoplay(string mode = "normal", int seed = 1)
        {
            var tune = ApplyDifficulty(Tea, mode);
            var rng = new GoobyRng(seed);
            var elapsed = 0.0;
            int score = 0, cups = 0, spills = 0, streak = 0;
            var limit = tune.Endless ? 600.0 : tune.DurationSec;
            while (elapsed < limit && !EndlessShouldEnd(spills, tune))
            {
                var band = RollBand(rng, tune);
                var level = band.Center + (rng.Next() * 2.0 - 1.0) * tune.AutoplayAimErr;
                var res = Pour(level, band, tune);
                score = ApplyScore(score, res.Points);
                if (res.Result == "perfect")
                {
                    streak += 1;
                    score = ApplyScore(score, StreakBonusAt(streak, tune));
                }
                else
                {
                    streak = 0;
                    if (res.Result == "miss") spills += 1;
                }
                cups += 1;
                elapsed += level / tune.FillRate + ServeIntervalAt(elapsed, tune.DurationSec, tune);
            }
            return new JObject { ["seed"] = seed, ["mode"] = mode, ["score"] = score, ["cups"] = cups, ["spills"] = spills, ["elapsed"] = elapsed };
        }
    }
}
