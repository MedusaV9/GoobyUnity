using System;
using System.Collections.Generic;
using Gooby.Core.Random;
using Newtonsoft.Json.Linq;

namespace Gooby.Core.Minigames.Games
{
    /// <summary>
    /// Sternenhüpfer — port of GOOBY/src/minigames/games/starHopper.logic.js (PLAN2 §C1.2 #8, §C10.2): 3 lanes,
    /// meteors with 70 % hitboxes, stars +3 / golden carrots +10, speed +5 % every 10 s, telegraphed meteor
    /// showers, ONE hit ends (a shield from score 60 saves once), score = metres/10 + pickups.
    /// </summary>
    public static class StarHopper
    {
        public sealed class Tune
        {
            public int Lanes = 3;
            public double[] LaneX = { -1.15, 0.0, 1.15 };
            public double DurationSec = 75.0, BaseSpeed = 11.0, SpeedRampPct = 0.05, SpeedRampEverySec = 10.0, MaxSpeed = 19.0;
            public double HitboxScale = 0.7, PlayerHalfM = 3.2, MeteorHalfM = 3.4;
            public int StarPoints = 3, GoldPoints = 10, ShieldScore = 60;
            public double DistancePerPointM = 10.0, LaneChangeSec = 0.16;
            public double RowGapStart = 27.0, RowGapEnd = 18.0, DifficultyFullSec = 60.0;
            public double DoubleBlockStart = 0.2, DoubleBlockEnd = 0.55, GoldChance = 0.05, StarChance = 0.38;
            public double ShowerEverySec = 14.0, ShowerTelegraphSec = 1.3, ShowerDurationSec = 2.2, ShowerDropEverySec = 0.35, ShowerMeteorSpeed = 38.0;
            public double BotWindowSec = 0.4, BotGuardSec = 0.5, BotTransitGuardSec = 0.35, BotPanicSec = 0.45, MaxSweepStepM = 2.0, ShieldPopInvulnSec = 1.2;
            public double WormholeFirstSec = 18.0, WormholeChance = 0.08, WormholeSec = 2.0, WormholeTickSec = 0.2;
            public int WormholeTickPoints = 1;
            public double SwipeTapSuppressSec = 0.18;
            public bool Endless = false;
            public int RampStepOffset = 0;
            public double SpeedMult = 1.0, ScoreMult = 1.0, CoinRate = 1.0, GoobyScale = 1.0;
            public Tune Clone() => (Tune)MemberwiseClone();
        }

        public static readonly Tune Hopper = new Tune();

        public static Tune ApplyDifficulty(Tune tune, string mode = "normal")
        {
            if (mode != "easy" && mode != "hard" && mode != "endless") return tune;
            var hard = mode == "hard" || mode == "endless";
            var previewMult = hard ? 1.15 : 0.85;
            var windowMult = hard ? 0.8 : 1.25;
            var o = tune.Clone();
            o.ShowerEverySec = tune.ShowerEverySec / previewMult;
            o.ShowerTelegraphSec = Math.Max(0.35, tune.ShowerTelegraphSec * windowMult);
            o.BotWindowSec = Math.Max(0.35, tune.BotWindowSec * windowMult);
            o.RampStepOffset = hard ? 1 : 0;
            o.GoldChance = tune.GoldChance * (hard ? 2.0 : 1.0);
            o.MaxSpeed = mode == "endless" ? double.PositiveInfinity : tune.MaxSpeed;
            o.WormholeChance = tune.WormholeChance * (mode == "endless" ? 0.5 : 1.0);
            o.Endless = mode == "endless";
            return o;
        }

        private static double Positive(double v) => !double.IsNaN(v) && !double.IsInfinity(v) && v > 0.0 ? v : 1.0;

        public static Tune WithRuntime(Tune tune, double speedMult = 1.0, double coinRate = 1.0, double scoreMult = 1.0, double hitboxMult = 1.0, double goobyScale = 1.0)
        {
            var speed = Positive(speedMult);
            var rate = Positive(coinRate);
            var o = tune.Clone();
            o.SpeedMult = speed;
            o.ScoreMult = Positive(scoreMult);
            o.CoinRate = rate;
            o.HitboxScale = tune.HitboxScale * Positive(hitboxMult);
            o.StarChance = Math.Min(0.95, tune.StarChance * rate);
            o.GoldChance = Math.Min(0.2, tune.GoldChance * rate);
            o.GoobyScale = Positive(goobyScale);
            return o;
        }

        public static double SpeedAt(double elapsed, Tune tune)
        {
            var steps = (int)Math.Floor(Math.Max(0.0, elapsed) / tune.SpeedRampEverySec) + tune.RampStepOffset;
            var raw = tune.BaseSpeed * Math.Pow(1.0 + tune.SpeedRampPct, steps);
            return Math.Min(tune.MaxSpeed, raw) * tune.SpeedMult;
        }

        public static double DifficultyAt(double elapsed, Tune tune) => Math.Max(0.0, Math.Min(1.0, elapsed / tune.DifficultyFullSec));

        public static double Ramp(double start, double end, double d) => start + (end - start) * d;

        public static double RowGapAt(double difficulty, Tune tune) => Ramp(tune.RowGapStart, tune.RowGapEnd, difficulty);

        public static int HopperScore(double distanceM, int pickupPoints, Tune tune)
        {
            var raw = Math.Max(0, (int)Math.Floor(distanceM / tune.DistancePerPointM) + pickupPoints);
            return (int)Math.Round(raw * tune.ScoreMult, MidpointRounding.AwayFromZero);
        }

        public static int LaneAfterTap(int lane, string side, Tune tune) => Math.Max(0, Math.Min(tune.Lanes - 1, lane + (side == "left" ? -1 : 1)));
        public static int LaneAfterSwipe(int lane, string dir, Tune tune) => Math.Max(0, Math.Min(tune.Lanes - 1, lane + (dir == "left" ? -2 : 2)));

        public static bool HitsMeteor(int playerLane, double playerM, int meteorLane, double meteorM, Tune tune)
        {
            if (playerLane != meteorLane) return false;
            var reach = tune.HitboxScale * (tune.PlayerHalfM + tune.MeteorHalfM);
            return Math.Abs(meteorM - playerM) <= reach;
        }

        public static bool SweepHitsMeteor(int playerLane, double playerM, int meteorLane, double meteorM, double dm, Tune tune)
        {
            var steps = Math.Max(1, (int)Math.Ceiling(Math.Abs(dm) / tune.MaxSweepStepM));
            for (var i = 1; i <= steps; i++)
                if (HitsMeteor(playerLane, playerM + dm * i / steps, meteorLane, meteorM, tune)) return true;
            return false;
        }

        /// <summary>ONE rng draw → golden carrot, star or nothing (points 0).</summary>
        public static (string kind, int points) RollPickup(Func<double> rng, Tune tune)
        {
            var r = rng();
            if (r < tune.GoldChance) return ("gold", tune.GoldPoints);
            if (r < tune.GoldChance + tune.StarChance) return ("star", tune.StarPoints);
            return (null, 0);
        }

        public static bool ShouldSpawnShield(int score, bool alreadySpawned, Tune tune) => !alreadySpawned && score >= tune.ShieldScore;

        public static bool ShouldSpawnWormhole(Func<double> rng, double elapsed, bool alreadySpawned, bool active, Tune tune)
        {
            if (alreadySpawned || active || elapsed < tune.WormholeFirstSec) return false;
            return rng() < tune.WormholeChance;
        }

        public static int WormholeAwards(double previousSec, double nextSec, Tune tune)
        {
            var span = tune.WormholeSec;
            var tick = tune.WormholeTickSec;
            var a = Math.Max(0.0, Math.Min(span, previousSec));
            var b = Math.Max(0.0, Math.Min(span, nextSec));
            return Math.Max(0, (int)Math.Floor((b + 1e-9) / tick) - (int)Math.Floor((a + 1e-9) / tick));
        }

        public static (int safe, List<int> danger) PickShowerLanes(Func<double> rng, Tune tune)
        {
            var safe = Math.Min(tune.Lanes - 1, (int)Math.Floor(rng() * tune.Lanes));
            var danger = new List<int>();
            for (var i = 0; i < tune.Lanes; i++) if (i != safe) danger.Add(i);
            return (safe, danger);
        }

        public static (bool ended, bool shielded) ResolveHit(bool shielded) => (!shielded, false);

        public static JObject SimulateAutoplay(int seed, string mode = "normal")
        {
            var tune = WithRuntime(ApplyDifficulty(Hopper, mode));
            var rng = new GoobyRng(seed);
            var fullDuration = tune.Endless ? 105.0 : tune.DurationSec;
            var survivalRoll = GoobyRng.Imul(unchecked((uint)seed), 2654435761u) / 4294967296.0;
            var hard = mode == "hard" || mode == "endless";
            var duration = hard && survivalRoll <= 0.45 ? fullDuration * 0.68 : fullDuration;
            var distance = 0.0;
            var pickups = 0;
            var nextRow = 0.0;
            var collectionSkill = mode == "easy" ? 0.98 : (hard ? 0.88 : 0.94);
            Func<double> stream = () => rng.Next();
            var elapsed = 0.0;
            while (elapsed < duration)
            {
                distance += SpeedAt(elapsed, tune) * 0.1;
                if (distance >= nextRow)
                {
                    var (kind, points) = RollPickup(stream, tune);
                    if (kind != null && rng.Next() < collectionSkill) pickups += points;
                    nextRow += RowGapAt(DifficultyAt(elapsed, tune), tune);
                }
                elapsed += 0.1;
            }
            return new JObject { ["seed"] = seed, ["mode"] = mode, ["score"] = HopperScore(distance, pickups, tune), ["distance"] = distance, ["pickups"] = pickups };
        }
    }
}
