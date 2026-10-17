using System;
using Gooby.Core.Random;
using Newtonsoft.Json.Linq;

namespace Gooby.Core.Minigames.Games
{
    /// <summary>
    /// Gießkannen-Wirbel — port of GOOBY/src/minigames/games/gardenRush.logic.js (PLAN2 §C1.2 #2): 8 pots
    /// (#7 from 20 s, #8 from 35 s), wilt window 6 s → 3 s, holding fills the 0.8-s ring; release in the last
    /// quarter = perfect +3, earlier +1, wilted −2, weed −1. 60 s; the one-off sprinkler at 30 s.
    /// </summary>
    public static class GardenRush
    {
        public sealed class Tune
        {
            public double DurationSec = 60.0;
            public int Pots = 8, StartPots = 6;
            public double Pot7AtSec = 20.0, Pot8AtSec = 35.0;
            public double WiltStartSec = 6.0, WiltEndSec = 3.0, FillSec = 0.8, PerfectZone = 0.25;
            public int PerfectPts = 3, EarlyPts = 1, WiltPts = -2, WeedPts = -1;
            public double SpawnStartSec = 3.1, SpawnEndSec = 2.0, RespawnSec = 0.9;
            public double WeedFromSec = 12.0, WeedChance = 0.18, WeedLifeSec = 5.0;
            public double AutoplayHoldSec = 0.75, SprinklerAtSec = 30.0, SprinklerFillFrac = 0.5;
            public bool Endless = false;
            public int EndlessWilts = 3;
            public double AutoplayDistract = 0.32, AutoplayEarly = 0.18;
            public double? EndlessSpawnFloorSec, EndlessWiltFloorSec;
            public double AutoplaySuccess = 0.97;
            public Tune Clone() => (Tune)MemberwiseClone();
        }

        public static readonly Tune Rush = new Tune();

        public static Tune ApplyDifficulty(Tune tune, string mode = "normal")
        {
            double spawnMult, windowMult, durationMult, botSuccess, distract;
            switch (mode)
            {
                case "easy": spawnMult = 1.2; windowMult = 1.25; durationMult = 1.2; botSuccess = 0.995; distract = 0.18; break;
                case "hard":
                case "endless": spawnMult = 0.85; windowMult = 0.8; durationMult = 1.0; botSuccess = 0.77; distract = 0.26; break;
                default: return tune;
            }
            var o = tune.Clone();
            o.DurationSec = tune.DurationSec * durationMult;
            o.SpawnStartSec = tune.SpawnStartSec * spawnMult;
            o.SpawnEndSec = tune.SpawnEndSec * spawnMult;
            o.WiltStartSec = Math.Max(0.35, tune.WiltStartSec * windowMult);
            o.WiltEndSec = Math.Max(0.35, tune.WiltEndSec * windowMult);
            o.FillSec = Math.Max(0.35, tune.FillSec * windowMult);
            o.Endless = mode == "endless";
            o.EndlessSpawnFloorSec = 1.0;
            o.EndlessWiltFloorSec = 1.2;
            o.AutoplaySuccess = botSuccess;
            o.AutoplayDistract = distract;
            return o;
        }

        private static double RampT(double elapsed, double duration, Tune tune) =>
            tune.Endless ? Math.Max(0.0, elapsed / duration) : Math.Min(1.0, Math.Max(0.0, elapsed / duration));

        public static double WiltWindowAt(double elapsed, double duration, Tune tune)
        {
            var t = RampT(elapsed, duration, tune);
            var value = tune.WiltStartSec + (tune.WiltEndSec - tune.WiltStartSec) * t;
            return Math.Max(tune.EndlessWiltFloorSec ?? tune.WiltEndSec, value);
        }

        public static double SpawnIntervalAt(double elapsed, double duration, Tune tune)
        {
            var t = RampT(elapsed, duration, tune);
            var value = tune.SpawnStartSec + (tune.SpawnEndSec - tune.SpawnStartSec) * t;
            return Math.Max(tune.EndlessSpawnFloorSec ?? tune.SpawnEndSec, value);
        }

        public static int ActivePotsAt(double elapsed)
        {
            if (elapsed >= Rush.Pot8AtSec) return 8;
            if (elapsed >= Rush.Pot7AtSec) return 7;
            return Rush.StartPots;
        }

        public static int ReleasePoints(double fillFrac, Tune tune)
        {
            var f = Math.Min(1.0, Math.Max(0.0, fillFrac));
            return f >= 1.0 - tune.PerfectZone ? tune.PerfectPts : tune.EarlyPts;
        }

        public static bool InPerfectZone(double fillFrac, Tune tune) => Math.Min(1.0, Math.Max(0.0, fillFrac)) >= 1.0 - tune.PerfectZone;

        public static double HoldFillFraction(double heldSec, Tune tune) => Math.Min(1.0, Math.Max(0.0, heldSec / tune.FillSec));

        public static double SprinklerRefill(double remainingSec, double windowSec)
        {
            var window = Math.Max(0.0, windowSec);
            return Math.Min(window, Math.Max(0.0, remainingSec) + window * Rush.SprinklerFillFrac);
        }

        public static bool ShouldSpawnSprinkler(double elapsed, bool alreadySpawned) => !alreadySpawned && elapsed >= Rush.SprinklerAtSec;

        public static bool RollWeed(GoobyRng rng, double elapsed) => elapsed >= Rush.WeedFromSec && rng.Next() < Rush.WeedChance;

        public static int ApplyPoints(int score, int points) => Math.Max(0, score + points);

        public static bool EndlessShouldEnd(int withered, Tune tune) => tune.Endless && withered >= tune.EndlessWilts;

        public static JObject SimulateAutoplay(int seed = 1, string mode = "normal")
        {
            var tune = ApplyDifficulty(Rush, mode);
            var rng = new GoobyRng(seed);
            var duration = tune.DurationSec;
            var limit = tune.Endless ? 600.0 : duration;
            var elapsed = 0.0;
            int score = 0, withered = 0;
            var success = tune.AutoplaySuccess;
            while (elapsed < limit && !EndlessShouldEnd(withered, tune))
            {
                elapsed += SpawnIntervalAt(elapsed, duration, tune);
                if (elapsed > limit) break;
                if (rng.Next() < success) score += tune.PerfectPts;
                else withered += 1;
            }
            return new JObject { ["seed"] = seed, ["mode"] = mode, ["score"] = score, ["withered"] = withered, ["elapsed"] = elapsed };
        }
    }
}
