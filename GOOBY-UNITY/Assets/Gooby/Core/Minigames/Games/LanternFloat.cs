using System;
using Gooby.Core.Random;
using Newtonsoft.Json.Linq;

namespace Gooby.Core.Minigames.Games
{
    /// <summary>
    /// Sternenlaternen — port of GOOBY/src/minigames/games/lanternFloat.logic.js (PLAN6 Wave C/C1): a paper
    /// lantern rises automatically; sideways steering guides it through star rings (+2, every 5th golden +5),
    /// past fireflies (+1), through telegraphed gusts and soft cloud bumps (timed −3; endless: 3 bumps end).
    /// </summary>
    public static class LanternFloat
    {
        public sealed class Tune
        {
            public double DurationSec = 60.0, HalfW = 3.1, RingMargin = 0.45, SteerOverreach = 1.25, RiseSpeed = 2.6;
            public double RingSpacingStart = 4.4, RingSpacingEnd = 3.4, RingRadius = 0.56;
            public int GoldEvery = 5, RingPts = 2, GoldPts = 5, FireflyPts = 1;
            public double FireflyChance = 0.45, FireflyRadius = 0.55;
            public double GustFirstSec = 7.0, GustEverySec = 9.0, GustTelegraphSec = 1.1, GustDurationSec = 1.4, GustForce = 1.9;
            public double CloudChance = 0.22;
            public int CloudMinIndex = 3;
            public double CloudHalfW = 0.85;
            public int BumpPenalty = 3;
            public double BumpInvulnSec = 1.2;
            public bool Endless = false;
            public int EndlessMaxBumps = 3;
            public double AutoplayAimErr = 0.9, AutoplayGustDrift = 0.35, AutoplayFireflyRate = 0.8, AutoplayBumpChance = 0.18;
            public string Mode = "normal";
            public Tune Clone() => (Tune)MemberwiseClone();
        }

        public static readonly Tune Lantern = new Tune();

        public static Tune ApplyDifficulty(Tune tune, string mode = "normal")
        {
            double ringMult, riseMult, durationMult;
            switch (mode)
            {
                case "easy": ringMult = 1.25; riseMult = 0.8; durationMult = 1.2; break;
                case "hard":
                case "endless": ringMult = 0.8; riseMult = 1.2; durationMult = 1.0; break;
                default: return tune;
            }
            var o = tune.Clone();
            o.DurationSec = tune.DurationSec * durationMult;
            o.RiseSpeed = tune.RiseSpeed * riseMult;
            o.RingRadius = tune.RingRadius * ringMult;
            o.Endless = mode == "endless";
            o.Mode = mode;
            return o;
        }

        public static double SteerTargetFrom(double nx, Tune tune) => Math.Max(-1.0, Math.Min(1.0, nx)) * tune.HalfW * tune.SteerOverreach;

        public static double ClampLanternX(double x, Tune tune) => Math.Max(-tune.HalfW, Math.Min(tune.HalfW, x));

        public static double RingSpacingAt(double elapsed, Tune tune)
        {
            var t = Math.Min(1.0, Math.Max(0.0, elapsed / tune.DurationSec));
            return tune.RingSpacingStart + (tune.RingSpacingEnd - tune.RingSpacingStart) * t;
        }

        public struct Ring
        {
            public int Index;
            public double X;
            public bool Gold;
            public int Points;
        }

        public static Ring RollRing(GoobyRng rng, int index, Tune tune)
        {
            var x = (rng.Next() * 2.0 - 1.0) * (tune.HalfW - tune.RingMargin);
            var gold = (index + 1) % tune.GoldEvery == 0;
            return new Ring { Index = index, X = x, Gold = gold, Points = gold ? tune.GoldPts : tune.RingPts };
        }

        public static bool RingHit(double lanternX, Ring ring, Tune tune) => Math.Abs(lanternX - ring.X) <= tune.RingRadius;

        public struct Gust
        {
            public int Index;
            public double StartSec, PushSec, EndSec;
            public int Dir;
        }

        /// <summary>The n-th gust — fully deterministic schedule (direction from a Knuth hash of the index).</summary>
        public static Gust GustAt(int index, Tune tune)
        {
            var startSec = tune.GustFirstSec + index * tune.GustEverySec;
            var pushSec = startSec + tune.GustTelegraphSec;
            var h = GoobyRng.Imul(unchecked((uint)(index + 1)), 2654435761u);
            return new Gust { Index = index, StartSec = startSec, PushSec = pushSec, EndSec = pushSec + tune.GustDurationSec, Dir = (h & 2) == 0 ? 1 : -1 };
        }

        public static (Gust gust, string phase) GustPhaseAt(double elapsed, Tune tune)
        {
            var idx = Math.Max(0, (int)Math.Floor((elapsed - tune.GustFirstSec) / tune.GustEverySec));
            var gust = GustAt(idx, tune);
            if (elapsed >= gust.EndSec) gust = GustAt(idx + 1, tune);
            var phase = "idle";
            if (elapsed < gust.StartSec) phase = "idle";
            else if (elapsed < gust.PushSec) phase = "telegraph";
            else if (elapsed < gust.EndSec) phase = "push";
            return (gust, phase);
        }

        /// <summary>Cloud of the n-th ring gap; ALWAYS consumes exactly TWO rng draws.</summary>
        public static (bool present, double x) RollCloud(GoobyRng rng, int index, Tune tune)
        {
            var present = rng.Next() < tune.CloudChance && index >= tune.CloudMinIndex;
            var x = (rng.Next() * 2.0 - 1.0) * (tune.HalfW - tune.RingMargin);
            return (present, x);
        }

        public static bool CloudHit(double lanternX, double cloudX, Tune tune) => Math.Abs(lanternX - cloudX) <= tune.CloudHalfW;

        public static int ApplyScore(int score, int delta) => Math.Max(0, score + delta);

        public static bool EndlessShouldEnd(int bumps, Tune tune) => tune.Endless && bumps >= tune.EndlessMaxBumps;

        public static JObject SimulateAutoplay(string mode = "normal", int seed = 1)
        {
            var tune = ApplyDifficulty(Lantern, mode);
            var rng = new GoobyRng(seed);
            var elapsed = 0.0;
            int score = 0, hits = 0, golds = 0, fireflies = 0, bumps = 0, index = 0;
            var limit = tune.Endless ? 600.0 : tune.DurationSec;
            while (!EndlessShouldEnd(bumps, tune))
            {
                elapsed += RingSpacingAt(elapsed, tune) / tune.RiseSpeed;
                if (elapsed >= limit) break;
                var ring = RollRing(rng, index, tune);
                var (gust, phase) = GustPhaseAt(elapsed, tune);
                var drift = phase == "push" ? gust.Dir * tune.AutoplayGustDrift : 0.0;
                var aim = ring.X + (rng.Next() * 2.0 - 1.0) * tune.AutoplayAimErr + drift;
                if (RingHit(ClampLanternX(aim, tune), ring, tune))
                {
                    hits += 1;
                    if (ring.Gold) golds += 1;
                    score = ApplyScore(score, ring.Points);
                }
                if (rng.Next() < tune.FireflyChance && rng.Next() < tune.AutoplayFireflyRate)
                {
                    fireflies += 1;
                    score = ApplyScore(score, tune.FireflyPts);
                }
                var (present, _) = RollCloud(rng, index, tune);
                if (present && rng.Next() < tune.AutoplayBumpChance)
                {
                    bumps += 1;
                    if (!tune.Endless) score = ApplyScore(score, -tune.BumpPenalty);
                }
                index += 1;
            }
            return new JObject
            {
                ["seed"] = seed, ["mode"] = mode, ["score"] = score, ["rings"] = index, ["hits"] = hits,
                ["golds"] = golds, ["fireflies"] = fireflies, ["bumps"] = bumps, ["elapsed"] = elapsed,
            };
        }
    }
}
