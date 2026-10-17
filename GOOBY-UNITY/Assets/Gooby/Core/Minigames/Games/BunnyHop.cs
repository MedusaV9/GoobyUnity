using System;
using Gooby.Core.Random;
using Newtonsoft.Json.Linq;

namespace Gooby.Core.Minigames.Games
{
    /// <summary>
    /// Hasenhüpfer — port of GOOBY/src/minigames/games/bunnyHop.logic.js (§C6.1 #3): tap = hop, score =
    /// passed gates, speed +2 %/gate, hitbox 70 % of the visuals, the gap narrows every 10 gates; wind from
    /// second 6 (telegraph, then a 0.4-lane shove — gates count double meanwhile). Coin row /2, 3..25.
    /// </summary>
    public static class BunnyHop
    {
        public sealed class Tune
        {
            public double SpeedRampPct = 0.02;
            public int GapNarrowEveryGates = 10;
            public double HitboxScale = 0.7, BaseSpeed = 1.55, PillarSpacingX = 2.7, GapBase = 2.15, GapNarrowStep = 0.16, GapMin = 1.5;
            public double HopVy = 3.1, Gravity = -8.5, FloorY = -3.1, CeilingY = 3.9, BodyHalfW = 0.34, BodyHalfH = 0.42, PillarHalfW = 0.34;
            public double GapMaxClimb = 1.4, GapMaxDive = 1.9;
            public double GustFirstSec = 6.0, GustEverySec = 10.0, GustTelegraphSec = 1.5, GustDurationSec = 2.0, GustShiftLanes = 0.4, LaneHeight = 1.0;
            public double ToleranceMult = 1.0, SpeedMult = 1.0, ScoreMult = 1.0, CoinRate = 1.0, CoinSpawnChance = 0.32, RenderScaleMult = 1.0;
            public bool Endless = false;
            public string Mode = "normal";
            public Tune Clone() => (Tune)MemberwiseClone();
        }

        public static readonly Tune Hop = new Tune();

        public static Tune ApplyDifficulty(Tune tune, string mode = "normal")
        {
            double toleranceRow;
            bool endless;
            switch (mode)
            {
                case "easy": toleranceRow = 1.25; endless = false; break;
                case "hard": toleranceRow = 0.8; endless = false; break;
                case "endless": toleranceRow = 0.8; endless = true; break;
                default: return tune;
            }
            var tolerance = Math.Max(0.55, toleranceRow);
            var o = tune.Clone();
            o.GapBase = tune.GapBase * tolerance;
            o.GapNarrowStep = tune.GapNarrowStep * tolerance;
            o.GapMin = tune.GapMin * tolerance;
            o.ToleranceMult = tolerance;
            o.Endless = endless;
            o.GustFirstSec = endless ? tune.GustTelegraphSec : tune.GustFirstSec;
            o.Mode = mode;
            return o;
        }

        public static double SpeedAtGate(int gates, Tune tune) => tune.BaseSpeed * Math.Pow(1.0 + tune.SpeedRampPct, Math.Max(0, gates));

        public static double GapAtGate(int gates, Tune tune)
        {
            var steps = Math.Floor((double)Math.Max(0, gates) / tune.GapNarrowEveryGates);
            return Math.Max(tune.GapMin, tune.GapBase - tune.GapNarrowStep * steps);
        }

        public static double ForgivingHalf(double visualHalf, Tune tune) => visualHalf * tune.HitboxScale;

        public static (double y, double vy) StepPhysics(double y, double vy, double dt, Tune tune)
        {
            var nvy = vy + tune.Gravity * dt;
            var ny = y + nvy * dt;
            if (ny > tune.CeilingY) return (tune.CeilingY, 0.0);
            return (ny, nvy);
        }

        public static bool Collides(double gx, double gy, double px, double gapCenterY, double gapHeight, Tune tune)
        {
            var halfW = ForgivingHalf(tune.BodyHalfW, tune);
            var halfH = ForgivingHalf(tune.BodyHalfH, tune);
            if (gy - halfH <= tune.FloorY) return true;
            if (Math.Abs(gx - px) > halfW + tune.PillarHalfW) return false;
            var gapTop = gapCenterY + gapHeight / 2.0;
            var gapBottom = gapCenterY - gapHeight / 2.0;
            return gy + halfH > gapTop || gy - halfH < gapBottom;
        }

        public static double RollGapCenter(GoobyRng rng, double gapHeight, double prevCenter, Tune tune)
        {
            const double margin = 0.45;
            var lo = tune.FloorY + margin + gapHeight / 2.0;
            var hi = tune.CeilingY - margin - gapHeight / 2.0;
            if (!double.IsInfinity(prevCenter) && !double.IsNaN(prevCenter))
            {
                lo = Math.Max(lo, prevCenter - tune.GapMaxDive);
                hi = Math.Min(hi, prevCenter + tune.GapMaxClimb);
            }
            return lo + rng.Next() * (hi - lo);
        }

        public static (string phase, int index, int direction) GustPhaseAt(double elapsed, Tune tune)
        {
            var local = elapsed - tune.GustFirstSec;
            var cycle = local + tune.GustTelegraphSec;
            if (cycle < 0.0) return ("none", -1, 1);
            var index = (int)Math.Floor(cycle / tune.GustEverySec);
            var start = tune.GustFirstSec + index * tune.GustEverySec;
            var direction = index % 2 == 0 ? 1 : -1;
            if (elapsed >= start - tune.GustTelegraphSec && elapsed < start) return ("telegraph", index, direction);
            if (elapsed >= start && elapsed < start + tune.GustDurationSec) return ("gust", index, direction);
            return ("none", index, direction);
        }

        public static double ApplyGustShift(double y, int direction, Tune tune)
        {
            var halfH = ForgivingHalf(tune.BodyHalfH, tune);
            var shifted = y + direction * tune.GustShiftLanes * tune.LaneHeight;
            return Math.Max(tune.FloorY + halfH + 0.01, Math.Min(tune.CeilingY - halfH, shifted));
        }

        public static int GatePoints(bool gusting) => gusting ? 2 : 1;

        public static bool CoinSpawns(double roll, Tune tune) => roll < Math.Min(1.0, tune.CoinSpawnChance * tune.CoinRate);

        public static int FinalHopScore(int score, Tune tune) => Math.Max(0, (int)Math.Round(score * tune.ScoreMult, MidpointRounding.AwayFromZero));

        public static bool GapNarrowsAtGate(int gatesPassed, Tune tune)
        {
            if (gatesPassed <= 0 || gatesPassed % tune.GapNarrowEveryGates != 0) return false;
            return GapAtGate(gatesPassed, tune) < GapAtGate(gatesPassed - 1, tune);
        }

        public static JObject SimulateAutoplay(int seed = 1, string mode = "normal")
        {
            var tune = ApplyDifficulty(Hop, mode);
            var rng = new GoobyRng(seed);
            var baseGates = mode == "easy" ? 62 : (mode == "hard" || mode == "endless" ? 48 : 55);
            var gates = baseGates + (int)Math.Floor(rng.Next() * 10.0);
            var score = 0;
            for (var i = 0; i < gates; i++)
            {
                var at = (i * tune.PillarSpacingX) / SpeedAtGate(i, tune);
                var gust = GustPhaseAt(at, tune);
                score += GatePoints(gust.phase == "gust");
                if (CoinSpawns(rng.Next(), tune) && rng.Next() < 0.86) score += 1;
            }
            return new JObject { ["seed"] = seed, ["mode"] = mode, ["score"] = FinalHopScore(score, tune), ["gates"] = gates };
        }
    }
}
