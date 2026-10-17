using System;
using System.Collections.Generic;
using Gooby.Core.Random;
using Newtonsoft.Json.Linq;

namespace Gooby.Core.Minigames.Games
{
    /// <summary>
    /// Guck-guck-Garten — port of GOOBY/src/minigames/games/hideSeek.logic.js (PLAN5 §V5.2): critters hide
    /// behind a 3×4 grid and peek periodically; tapping the right spot finds them (+2), a cleared wave +3.
    /// </summary>
    public static class HideSeek
    {
        public sealed class Tune
        {
            public double DurationSec = 60.0;
            public int Cols = 3, Rows = 4;
            public int WaveHidersStart = 3, WaveHidersMax = 5, WaveRampWaves = 4;
            public int FindPts = 2, WaveBonus = 3;
            public double WaveSecStart = 13.0, WaveSecEnd = 9.0, ServeSec = 1.0;
            public double PeekEverySec = 2.4, PeekDurationSec = 0.75;
            public bool Endless = false;
            public int EndlessMaxExpired = 3;
            public double AutoplayTapSec = 1.2, AutoplayFindRate = 0.9;
            public string Mode = "normal";
            public Tune Clone() => (Tune)MemberwiseClone();
        }

        public static readonly Tune Seek = new Tune();

        public static Tune ApplyDifficulty(Tune tune, string mode = "normal")
        {
            double waveSecMult, peekDurMult, peekEveryMult, durationMult, botRate;
            switch (mode)
            {
                case "easy": waveSecMult = 1.25; peekDurMult = 1.3; peekEveryMult = 1.0; durationMult = 1.2; botRate = 0.97; break;
                case "hard":
                case "endless": waveSecMult = 0.8; peekDurMult = 0.8; peekEveryMult = 1.25; durationMult = 1.0; botRate = 0.82; break;
                default: return tune;
            }
            var o = tune.Clone();
            o.DurationSec = tune.DurationSec * durationMult;
            o.WaveSecStart = tune.WaveSecStart * waveSecMult;
            o.WaveSecEnd = tune.WaveSecEnd * waveSecMult;
            o.PeekDurationSec = tune.PeekDurationSec * peekDurMult;
            o.PeekEverySec = tune.PeekEverySec * peekEveryMult;
            o.AutoplayFindRate = botRate;
            o.Endless = mode == "endless";
            o.Mode = mode;
            return o;
        }

        public static int SpotCount(Tune tune) => tune.Cols * tune.Rows;

        public static int HidersForWave(int wave, Tune tune)
        {
            var t = Math.Min(1.0, Math.Max(0.0, (double)wave / tune.WaveRampWaves));
            double start = tune.WaveHidersStart;
            return FrameworkLogic.JsRound(start + (tune.WaveHidersMax - start) * t);
        }

        public static double WaveSecFor(int wave, Tune tune)
        {
            var t = Math.Min(1.0, Math.Max(0.0, (double)wave / tune.WaveRampWaves));
            return tune.WaveSecStart + (tune.WaveSecEnd - tune.WaveSecStart) * t;
        }

        /// <summary>Unique, uniform, ascending hider indices — partial Fisher-Yates consuming EXACTLY n rng values.</summary>
        public static List<int> RollHiders(GoobyRng rng, int wave, Tune tune)
        {
            var total = SpotCount(tune);
            var n = Math.Min(HidersForWave(wave, tune), total);
            var pool = new List<int>(total);
            for (var i = 0; i < total; i++) pool.Add(i);
            for (var i = 0; i < n; i++)
            {
                var j = i + (int)Math.Floor(rng.Next() * (total - i));
                (pool[i], pool[j]) = (pool[j], pool[i]);
            }
            var outp = pool.GetRange(0, n);
            outp.Sort();
            return outp;
        }

        public static int ApplyScore(int score, int delta) => Math.Max(0, score + delta);

        public static bool EndlessShouldEnd(int expired, Tune tune) => tune.Endless && expired >= tune.EndlessMaxExpired;

        public static JObject SimulateAutoplay(string mode = "normal", int seed = 1)
        {
            var tune = ApplyDifficulty(Seek, mode);
            var rng = new GoobyRng(seed);
            var elapsed = 0.0;
            int score = 0, wave = 0, expired = 0, found = 0;
            var limit = tune.Endless ? 600.0 : tune.DurationSec;
            while (elapsed < limit && !EndlessShouldEnd(expired, tune))
            {
                var left = RollHiders(rng, wave, tune).Count;
                var waveSec = WaveSecFor(wave, tune);
                var waveT = 0.0;
                while (left > 0 && waveT < waveSec && elapsed + waveT < limit)
                {
                    waveT += tune.AutoplayTapSec;
                    if (rng.Next() < tune.AutoplayFindRate)
                    {
                        left -= 1;
                        found += 1;
                        score = ApplyScore(score, tune.FindPts);
                    }
                }
                if (left == 0) score = ApplyScore(score, tune.WaveBonus);
                else if (waveT >= waveSec) expired += 1;
                elapsed += waveT + tune.ServeSec;
                wave += 1;
            }
            return new JObject { ["seed"] = seed, ["mode"] = mode, ["score"] = score, ["waves"] = wave, ["found"] = found, ["expired"] = expired, ["elapsed"] = elapsed };
        }
    }
}
