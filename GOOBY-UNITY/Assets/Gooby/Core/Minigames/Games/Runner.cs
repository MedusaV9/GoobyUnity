using System;
using System.Collections.Generic;
using Gooby.Core.Random;
using Newtonsoft.Json.Linq;

namespace Gooby.Core.Minigames.Games
{
    /// <summary>
    /// Gooby-Runner — port of GOOBY/src/minigames/games/runner.logic.js (§C6.1 #6): 3 lanes, endless run; swipe
    /// left/right = lane, up = jump, down = slide. Obstacles are ALWAYS generated survivable (DP validator).
    /// Coins +1 × combo bonus, speed +5 % every 10 s. First hit = stumble, second = out. Coin row /15, 4..30.
    /// </summary>
    public static class Runner
    {
        public sealed class Obstacle
        {
            public string Pass;
            public double ClearY, GapY, HalfDepth;
        }

        public sealed class Tune
        {
            public int Lanes = 3;
            public double[] LaneX = { -1.1, 0.0, 1.1 };
            public double BaseSpeed = 6.0, SpeedRampPct = 0.05, SpeedRampEverySec = 10.0, MaxSpeed = 13.0, LaneChangeSec = 0.16;
            public double JumpSec = 0.62, JumpHeight = 1.0, SlideSec = 0.65, SlideHeight = 0.5, StandHeight = 1.05, PlayerHalfDepth = 0.28;
            public double RowGapStart = 13.0, RowGapEnd = 8.5, DifficultyFullSec = 90.0, DoubleBlockStart = 0.25, DoubleBlockEnd = 0.62;
            public int CoinScoreBonus = 2;
            public int[] ComboSteps = { 0, 10, 22 };
            public int ComboMaxMult = 3, CoinLine = 3;
            public double CoinLineChance = 0.75, StumbleInvulnSec = 1.6;
            public int MaxHits = 2;
            public double MagnetSec = 4.0, MagnetRadius = 3.0, X2Sec = 6.0, MysteryFirstM = 45.0, MysteryGapM = 70.0, MaxSweepStepM = 0.35;
            public double DensityMult = 1.0, SpeedMult = 1.0, ScoreMult = 1.0, CoinRate = 1.0, RenderScaleMult = 1.0;
            public bool Endless = false;
            public double BotMissChance = 0.025;
            public string Mode = "normal";
            public Tune Clone() => (Tune)MemberwiseClone();
        }

        public static readonly Tune Run = new Tune();

        /// <summary>Order matters: the weighted pick walks this list linearly.</summary>
        public static readonly (string kind, double weight)[] KindWeights = { ("cone", 3.0), ("box", 2.0), ("barrier", 2.0), ("overhead", 2.2), ("car", 1.6) };

        public static readonly Dictionary<string, Obstacle> Obstacles = new Dictionary<string, Obstacle>
        {
            { "cone", new Obstacle { Pass = "jump", ClearY = 0.45, HalfDepth = 0.22 } },
            { "box", new Obstacle { Pass = "jump", ClearY = 0.55, HalfDepth = 0.3 } },
            { "barrier", new Obstacle { Pass = "jump", ClearY = 0.6, HalfDepth = 0.22 } },
            { "overhead", new Obstacle { Pass = "slide", GapY = 0.72, HalfDepth = 0.24 } },
            { "car", new Obstacle { Pass = "none", HalfDepth = 0.95 } },
        };

        public const double MilestoneEveryM = 100.0;

        public static Tune ApplyDifficulty(Tune tune, string mode = "normal")
        {
            double speed, density;
            int extraHits;
            bool endless;
            switch (mode)
            {
                case "easy": speed = 0.85; density = 0.85; extraHits = 1; endless = false; break;
                case "hard": speed = 1.2; density = 1.15; extraHits = 0; endless = false; break;
                case "endless": speed = 1.2; density = 1.15; extraHits = 1; endless = true; break;
                default: return tune;
            }
            var o = tune.Clone();
            o.BaseSpeed = tune.BaseSpeed * speed;
            o.MaxSpeed = endless ? tune.MaxSpeed * 1.4 : tune.MaxSpeed * speed;
            o.RowGapStart = tune.RowGapStart / density;
            o.RowGapEnd = tune.RowGapEnd / density;
            o.MaxHits = tune.MaxHits + extraHits;
            o.DensityMult = density;
            o.SpeedMult = speed;
            o.Endless = endless;
            o.BotMissChance = mode == "easy" ? 0.008 : 0.035;
            o.Mode = mode;
            return o;
        }

        public static double SpeedAt(double elapsed, Tune tune)
        {
            var steps = Math.Floor(Math.Max(0.0, elapsed) / tune.SpeedRampEverySec);
            return Math.Min(tune.MaxSpeed, tune.BaseSpeed * Math.Pow(1.0 + tune.SpeedRampPct, steps));
        }

        public static double DifficultyAt(double elapsed, Tune tune) => Math.Min(1.0, Math.Max(0.0, elapsed / tune.DifficultyFullSec));

        private static double Ramp(double start, double end, double d) => start + (end - start) * d;

        public static double RowGapAt(double difficulty, Tune tune) => Ramp(tune.RowGapStart, tune.RowGapEnd, difficulty);

        public static int CoinLineCount(GoobyRng rng, Tune tune)
        {
            var expected = Math.Max(0.0, tune.CoinLine * tune.CoinRate);
            var whole = (int)Math.Floor(expected);
            var fraction = expected - whole;
            return whole + (fraction > 0.0 && rng.Next() < fraction ? 1 : 0);
        }

        public static int ComboMultiplier(int coinStreak, Tune tune)
        {
            var mult = 0;
            foreach (var step in tune.ComboSteps) if (coinStreak >= step) mult += 1;
            return Math.Min(tune.ComboMaxMult, Math.Max(1, mult));
        }

        public static int Score(double meters, double coinPoints) => Math.Max(0, (int)Math.Floor(meters) + FrameworkLogic.JsRound(coinPoints));

        public static int FinalScore(double meters, double coinPoints, Tune tune) =>
            Math.Max(0, FrameworkLogic.JsRound(Score(meters, coinPoints) * tune.ScoreMult));

        public static bool ActionPasses(string kind, string action)
        {
            if (!Obstacles.TryGetValue(kind, out var def)) return true;
            if (def.Pass == "jump") return action == "jump";
            if (def.Pass == "slide") return action == "slide";
            return false;
        }

        public static bool HitsObstacle(int playerLane, double playerY, bool sliding, int obstacleLane, string kind, double obstacleZ, Tune tune)
        {
            if (playerLane != obstacleLane) return false;
            var def = Obstacles[kind];
            var reach = def.HalfDepth + tune.PlayerHalfDepth;
            if (Math.Abs(obstacleZ) > reach) return false;
            if (def.Pass == "jump") return playerY < def.ClearY;
            if (def.Pass == "slide") return !(sliding && playerY + tune.SlideHeight <= def.GapY);
            return true;
        }

        public static bool SweepHitsObstacle(int playerLane, double playerY, bool sliding, int obstacleLane, string kind, double obstacleZ, double dz, Tune tune)
        {
            var steps = Math.Max(1, (int)Math.Ceiling(Math.Abs(dz) / tune.MaxSweepStepM));
            for (var i = 1; i <= steps; i++)
                if (HitsObstacle(playerLane, playerY, sliding, obstacleLane, kind, obstacleZ + dz * i / steps, tune)) return true;
            return false;
        }

        public static string RollMysteryPower(GoobyRng rng)
        {
            var kinds = new[] { "magnet", "x2", "shield" };
            return kinds[Math.Min(kinds.Length - 1, (int)Math.Floor(rng.Next() * kinds.Length))];
        }

        public static int MysteryCoinPoints(int comboMult, bool x2Active, Tune tune) => tune.CoinScoreBonus * comboMult * (x2Active ? 2 : 1);

        public static bool MagnetCollects(double cx, double cy, double cz, double px, double py, double pz, bool active, Tune tune)
        {
            if (!active) return false;
            var dx = cx - px; var dy = cy - py; var dz = cz - pz;
            return Math.Sqrt(dx * dx + dy * dy + dz * dz) <= tune.MagnetRadius;
        }

        public struct MysteryState
        {
            public double MagnetT, X2T;
            public bool Shield;
        }

        public static MysteryState ActivateMysteryPower(MysteryState state, string kind, Tune tune)
        {
            if (kind == "magnet") { state.MagnetT = tune.MagnetSec; return state; }
            if (kind == "x2") { state.X2T = tune.X2Sec; return state; }
            state.Shield = true;
            return state;
        }

        public struct HitState
        {
            public int Hits;
            public bool Shield;
            public double InvulnT;
            public string Outcome;
        }

        public static HitState ResolveHit(HitState state, Tune tune)
        {
            if (state.InvulnT > 0.0) { state.Outcome = "ignored"; return state; }
            if (state.Shield) return new HitState { Hits = state.Hits, Shield = false, InvulnT = tune.StumbleInvulnSec, Outcome = "shielded" };
            var hits = state.Hits + 1;
            return new HitState { Hits = hits, Shield = false, InvulnT = tune.StumbleInvulnSec, Outcome = hits >= tune.MaxHits ? "wipeout" : "stumble" };
        }

        public static int MaxLaneShift(double gapM, double speed, Tune tune)
        {
            var time = gapM / Math.Max(0.001, speed);
            var perChange = tune.LaneChangeSec + 0.22;
            return Math.Max(0, Math.Min(2, (int)Math.Floor(time / perChange)));
        }

        public sealed class Row
        {
            public string[] Lanes;
            public double Gap;
        }

        public static bool[] PassableLanes(Row row)
        {
            var outp = new bool[row.Lanes.Length];
            for (var i = 0; i < row.Lanes.Length; i++)
            {
                var k = row.Lanes[i];
                outp[i] = k == null || ActionPasses(k, "jump") || ActionPasses(k, "slide");
            }
            return outp;
        }

        /// <summary>§C6.1 #6 survivability validator: DP reachability across the rows.</summary>
        public static bool IsPatternSurvivable(IList<Row> rows, double speed, Tune tune)
        {
            var lanes = tune.Lanes;
            var reachable = new bool[lanes];
            for (var i = 0; i < lanes; i++) reachable[i] = true;
            foreach (var row in rows)
            {
                var shift = MaxLaneShift(row.Gap, speed, tune);
                var pass = PassableLanes(row);
                var next = new bool[lanes];
                for (var to = 0; to < lanes; to++)
                {
                    if (!pass[to]) continue;
                    for (var from = 0; from < lanes; from++)
                        if (reachable[from] && Math.Abs(to - from) <= shift) { next[to] = true; break; }
                }
                reachable = next;
                if (Array.IndexOf(reachable, true) < 0) return false;
            }
            return true;
        }

        private static string PickKind(GoobyRng rng)
        {
            var total = 0.0;
            foreach (var kw in KindWeights) total += kw.weight;
            var roll = rng.Next() * total;
            var last = "";
            foreach (var kw in KindWeights)
            {
                last = kw.kind;
                roll -= kw.weight;
                if (roll <= 0.0) return last;
            }
            return last;
        }

        /// <summary>V8-faithful replica of <c>[0,1,2].sort(() => rng() - 0.5)</c> (TimSort run detection + binary insertion).</summary>
        public static int[] ShuffleLaneOrder(GoobyRng rng)
        {
            if (rng.Next() - 0.5 < 0.0)
            {
                if (rng.Next() - 0.5 < 0.0) return new[] { 2, 1, 0 };
                if (rng.Next() - 0.5 >= 0.0) return new[] { 1, 0, 2 };
                return rng.Next() - 0.5 < 0.0 ? new[] { 2, 1, 0 } : new[] { 1, 2, 0 };
            }
            if (rng.Next() - 0.5 >= 0.0) return new[] { 0, 1, 2 };
            if (rng.Next() - 0.5 >= 0.0) return new[] { 0, 1, 2 };
            return rng.Next() - 0.5 < 0.0 ? new[] { 2, 0, 1 } : new[] { 0, 2, 1 };
        }

        public static Row GenerateRow(GoobyRng rng, double elapsed, IList<Row> recentRows, Tune tune)
        {
            var lanesN = tune.Lanes;
            var d = DifficultyAt(elapsed, tune);
            var speed = SpeedAt(elapsed, tune);
            var gap = RowGapAt(d, tune) * (0.9 + rng.Next() * 0.25);
            var window = new List<Row>();
            for (var i = Math.Max(0, recentRows.Count - 4); i < recentRows.Count; i++) window.Add(recentRows[i]);
            for (var attempt = 0; attempt < 12; attempt++)
            {
                var lanes = new string[lanesN];
                var blockCount = rng.Next() < Ramp(tune.DoubleBlockStart, tune.DoubleBlockEnd, d) ? 2 : 1;
                var order = ShuffleLaneOrder(rng);
                for (var b = 0; b < blockCount; b++) lanes[order[b]] = PickKind(rng);
                var row = new Row { Lanes = lanes, Gap = gap };
                var probe = new List<Row>(window) { row };
                if (IsPatternSurvivable(probe, speed, tune)) return row;
            }
            var fallback = new string[lanesN];
            fallback[0] = "cone";
            return new Row { Lanes = fallback, Gap = gap };
        }

        public static int CrossedMilestone(double prevMeters, double meters, double everyM = -1.0)
        {
            var step = everyM > 0.0 ? everyM : MilestoneEveryM;
            var prev = Math.Floor(Math.Max(0.0, prevMeters) / step);
            var now = Math.Floor(Math.Max(0.0, meters) / step);
            return now > prev ? (int)(now * step) : 0;
        }

        public static JObject SimulateAutoplay(string mode = "normal", int seed = 1, double maxSec = 180.0)
        {
            var tune = ApplyDifficulty(Run, mode);
            var rng = new GoobyRng(seed);
            var elapsed = 0.0;
            var meters = 0.0;
            var hits = 0;
            var coinPoints = 0.0;
            var streak = 0;
            while (elapsed < maxSec && hits < tune.MaxHits)
            {
                var d = DifficultyAt(elapsed, tune);
                var gap = RowGapAt(d, tune) * (0.9 + rng.Next() * 0.25);
                var speed = SpeedAt(elapsed, tune);
                elapsed += gap / speed;
                meters += gap;
                if (rng.Next() < tune.BotMissChance)
                {
                    hits += 1;
                    streak = 0;
                }
                else if (rng.Next() < tune.CoinLineChance)
                {
                    var coins = CoinLineCount(rng, tune);
                    for (var i = 0; i < coins; i++)
                    {
                        streak += 1;
                        coinPoints += MysteryCoinPoints(ComboMultiplier(streak, tune), false, tune);
                    }
                }
            }
            return new JObject { ["score"] = FinalScore(meters, coinPoints, tune), ["elapsed"] = elapsed, ["meters"] = meters, ["hits"] = hits };
        }
    }
}
