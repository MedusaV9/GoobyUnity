using System;
using System.Collections.Generic;
using System.Linq;
using Gooby.Core.Random;

namespace Gooby.Core.Minigames.Games
{
    /// <summary>
    /// Einkaufs-Surf — numerically identical port of shopping_surf_logic.gd (web shoppingSurf.logic.js, §C8): Gooby runs
    /// down the shopping street on 3 lanes à 1.6 m; swipes change lanes, jump over carts and curb gaps, slide under
    /// awnings. Coins +1 (×2 with the power-up), near misses +2, speed 8 → 16 m/s. Holds tuning, the 12 street chunks,
    /// the never-impossible validator and the scoring; the run simulation is <see cref="ShoppingSurfRun"/>.
    /// </summary>
    public static class ShoppingSurf
    {
        public sealed class ObDef
        {
            public string Pass;
            public double ClearY, HalfW, HalfDepth, OwnSpeed, TelegraphSec, CrossSpeed, GapY, SlowMult = 1.0, SlowSec;
        }

        public sealed class Tune
        {
            public int Lanes = 3;
            public double LaneW = 1.6;
            public double[] LaneX = { -1.6, 0.0, 1.6 };
            public double BaseSpeed = 8.0, SpeedStep = 0.25, SpeedEverySec = 5.0, MaxSpeed = 16.0;
            public double LaneChangeSec = 0.12, JumpSec = 0.55, JumpHeight = 1.35, SlideSec = 0.5, SlideHeight = 0.5, StandHeight = 1.05;
            public double FastDropSpeed = 10.0, BufferSec = 0.25;
            public double PlayerHalfW = 0.42, PlayerHalfDepth = 0.3;
            public double StumbleSec = 0.8, InvulnSec = 1.5;
            public int ArcadeMaxCrashes = 3;
            public double StumbleSpeedMult = 0.5, NearMissM = 0.35;
            public double ChunkLenM = 30.0, SpawnAheadM = 70.0, DespawnZ = 8.0, GapMinDistanceM = 800.0;
            public Dictionary<string, ObDef> Obstacles = DefaultObstacles();
            public double MagnetSec = 6.0, MagnetRadius = 3.0, X2Sec = 8.0, TurboSec = 2.5, TurboSpeedMult = 1.4, TurboMinGapM = 400.0;
            public double PowerupGapMinM = 180.0, PowerupGapMaxM = 260.0;
            public double CoinY = 0.55, CoinStepM = 1.1, MagnetPullSpeed = 14.0;
            public double TravelDistanceM = 700.0, TravelJogSpeed = 7.0;
            public int TravelCoinCap = 30, TravelCleanBonus = 5;
            public double MaxSweepStepM = 0.32;
            public double ValidatorReactSec = 0.18, ValidatorLaneCostSec = 0.22, ValidatorActionLeadSec = 0.35, ValidatorRowEpsSec = 0.4;
            public double DensityMult = 1.0, DensityCap = 1.0, DensityRampFullM = 1500.0;
            public double SpeedMult = 1.0, ScoreMult = 1.0, CoinRate = 1.0, RenderScaleMult = 1.0;
            public bool GatedSpawns, Endless;
            public double BotMissChance = 0.012;
            public string Mode = "normal";
            public Tune Clone() => (Tune)MemberwiseClone();
        }

        private static Dictionary<string, ObDef> DefaultObstacles() => new Dictionary<string, ObDef>
        {
            ["cart"] = new ObDef { Pass = "jump", ClearY = 0.55, HalfW = 0.55, HalfDepth = 0.5, OwnSpeed = 2.0, TelegraphSec = 0.9 },
            ["crate"] = new ObDef { Pass = "none", HalfW = 0.6, HalfDepth = 0.45, OwnSpeed = 0.0 },
            ["npc"] = new ObDef { Pass = "jump", ClearY = 0.75, HalfW = 0.38, HalfDepth = 0.32, OwnSpeed = 0.0, CrossSpeed = 1.2 },
            ["awning"] = new ObDef { Pass = "slide", GapY = 0.88, HalfDepth = 0.18, OwnSpeed = 0.0 },
            ["puddle"] = new ObDef { Pass = "soft", HalfW = 0.65, HalfDepth = 0.5, OwnSpeed = 0.0, SlowMult = 0.9, SlowSec = 2.0 },
            ["gap"] = new ObDef { Pass = "jump", HalfDepth = 1.1, OwnSpeed = 0.0 },
        };

        public static readonly Tune Surf = new Tune();

        private struct DiffRow
        {
            public double Speed, CapMult, Density, DensityCap, BotMiss;
            public bool HasDensityCap, Endless;
            public int ExtraCrashes;
        }

        private static readonly Dictionary<string, DiffRow> Difficulty = new Dictionary<string, DiffRow>
        {
            ["easy"] = new DiffRow { Speed = 0.85, CapMult = 0.85, Density = 0.85, ExtraCrashes = 1, BotMiss = 0.005 },
            ["normal"] = new DiffRow { Speed = 1.0, CapMult = 1.0, Density = 1.0, BotMiss = 0.012 },
            ["hard"] = new DiffRow { Speed = 1.2, CapMult = 1.125, Density = 1.15, BotMiss = 0.07 },
            ["endless"] = new DiffRow { Speed = 1.2, CapMult = 1.25, Density = 1.15, DensityCap = 1.5, HasDensityCap = true, Endless = true, BotMiss = 0.07 },
        };

        public sealed class Hazard
        {
            public double AtM;
            public string Kind;
            public int? Lane;
            public int[] Lanes;
            public Hazard At(double startM) => new Hazard { AtM = startM + AtM, Kind = Kind, Lane = Lane, Lanes = Lanes };
        }

        public sealed class CoinRow
        {
            public double AtM;
            public int Lane, N;
            public bool Arc;
            public CoinRow At(double startM) => new CoinRow { AtM = startM + AtM, Lane = Lane, N = N, Arc = Arc };
        }

        public sealed class Chunk
        {
            public string Name;
            public double MinM;
            public Hazard[] Hazards;
            public CoinRow[] Coins;
        }

        private static Hazard H(double at, string kind, int lane) => new Hazard { AtM = at, Kind = kind, Lane = lane };
        private static Hazard HL(double at, string kind, params int[] lanes) => new Hazard { AtM = at, Kind = kind, Lanes = lanes };
        private static Hazard HA(double at, string kind) => new Hazard { AtM = at, Kind = kind };
        private static CoinRow C(double at, int lane, int n, bool arc = false) => new CoinRow { AtM = at, Lane = lane, N = n, Arc = arc };

        /// <summary>§C8.1 chunk pool — 12 hand-built 30-m sections (validator-checked).</summary>
        public static readonly Chunk[] Chunks =
        {
            new Chunk { Name = "warmup", MinM = 0.0, Hazards = new[] { H(15, "cart", 1) }, Coins = new[] { C(15, 1, 5, true), C(22, 2, 4) } },
            new Chunk { Name = "cratePair", MinM = 0.0, Hazards = new[] { H(10, "crate", 0), H(10, "crate", 1), HL(23, "awning", 0, 1) }, Coins = new[] { C(10, 2, 5), C(23, 2, 4) } },
            new Chunk { Name = "cartsStagger", MinM = 60.0, Hazards = new[] { H(9, "cart", 2), H(22, "cart", 0) }, Coins = new[] { C(9, 2, 5, true), C(16, 1, 4) } },
            new Chunk { Name = "shopperCross", MinM = 60.0, Hazards = new[] { H(8, "crate", 1), HA(21, "npc") }, Coins = new[] { C(14, 0, 4), C(26, 1, 4) } },
            new Chunk { Name = "slideRow", MinM = 120.0, Hazards = new[] { HL(10, "awning", 1, 2), H(10, "crate", 0), H(24, "cart", 1) }, Coins = new[] { C(15, 2, 5), C(24, 1, 5, true) } },
            new Chunk { Name = "puddleAlley", MinM = 120.0, Hazards = new[] { H(12, "crate", 0), H(12, "puddle", 1), H(12, "crate", 2) }, Coins = new[] { C(18, 1, 5), C(24, 0, 3) } },
            new Chunk { Name = "actionWall", MinM = 200.0, Hazards = new[] { H(13, "cart", 0), H(13, "cart", 1), HL(13, "awning", 2) }, Coins = new[] { C(13, 1, 5, true), C(20, 2, 4) } },
            new Chunk { Name = "shopperCrates", MinM = 200.0, Hazards = new[] { HA(9, "npc"), H(22, "crate", 1), H(22, "crate", 2) }, Coins = new[] { C(15, 1, 4), C(22, 0, 5) } },
            new Chunk { Name = "zigzag", MinM = 300.0, Hazards = new[] { H(8, "crate", 0), H(16, "crate", 2), H(24, "crate", 1) }, Coins = new[] { C(12, 1, 3), C(20, 0, 3) } },
            new Chunk { Name = "doubleSlide", MinM = 300.0, Hazards = new[] { HL(9, "awning", 0, 1), HL(23, "awning", 1, 2) }, Coins = new[] { C(16, 1, 5) } },
            new Chunk { Name = "curbBreak", MinM = 800.0, Hazards = new[] { HA(10, "gap"), H(24, "cart", 1) }, Coins = new[] { C(10, 1, 5, true), C(18, 0, 4) } },
            new Chunk { Name = "gauntlet", MinM = 400.0, Hazards = new[] { H(8, "cart", 1), H(21, "crate", 0), H(21, "puddle", 1) }, Coins = new[] { C(14, 2, 4), C(26, 2, 4) } },
        };

        public static readonly string[] PowerupKinds = { "magnet", "x2", "shield", "turbo" };

        /// <summary>Derived tune (§G5.3); normal returns the base table itself.</summary>
        public static Tune ApplyDifficulty(Tune tune, string mode = "normal")
        {
            var id = mode != null && Difficulty.ContainsKey(mode) ? mode : "normal";
            if (id == "normal") return tune;
            var row = Difficulty[id];
            var maxSpeed = tune.MaxSpeed * row.CapMult;
            var t = tune.Clone();
            t.BaseSpeed = tune.BaseSpeed * row.Speed;
            t.MaxSpeed = maxSpeed;
            t.ArcadeMaxCrashes = tune.ArcadeMaxCrashes + row.ExtraCrashes;
            t.DensityMult = row.Density;
            t.DensityCap = row.HasDensityCap ? row.DensityCap : row.Density;
            t.SpeedMult = row.Speed;
            t.GatedSpawns = row.Density != 1.0 || maxSpeed > tune.MaxSpeed;
            t.Endless = row.Endless;
            t.BotMissChance = row.BotMiss;
            t.Mode = id;
            return t;
        }

        public static bool IsTravelMode(string mode) => mode == "travel" || mode == "surfTravel";

        /// <summary>§C8.5 forward speed after rampSec of uninterrupted running.</summary>
        public static double SpeedRampAt(double rampSec, Tune tune)
        {
            var steps = Math.Floor(Math.Max(0.0, rampSec) / tune.SpeedEverySec);
            return Math.Min(tune.MaxSpeed, tune.BaseSpeed + tune.SpeedStep * steps);
        }

        /// <summary>§C8.5 points: floor(metres) + coins×2 + near misses×2.</summary>
        public static int SurfScore(double distanceM, int coins, int nearMisses) => Math.Max(0, (int)Math.Floor(distanceM) + coins * 2 + nearMisses * 2);

        public static (int coins, bool clean) TravelReward(int coinsCollected, int crashes, Tune tune)
        {
            var clean = Math.Max(0, crashes) == 0;
            var capped = Math.Min(tune.TravelCoinCap, Math.Max(0, coinsCollected));
            return (capped + (clean ? tune.TravelCleanBonus : 0), clean);
        }

        private static bool HasGap(Chunk def) => def.Hazards.Any(h => h.Kind == "gap");

        private static List<int> EligibleChunks(double startM, int lastIndex, Tune tune)
        {
            var eligible = new List<int>();
            for (var i = 0; i < Chunks.Length; i++)
            {
                if (i == lastIndex) continue;
                if (startM < Chunks[i].MinM) continue;
                if (HasGap(Chunks[i]) && startM < tune.GapMinDistanceM) continue;
                eligible.Add(i);
            }
            return eligible;
        }

        /// <summary>§C8.1 seeded chunk pick; the warm-up opens every run.</summary>
        public static int PickNextChunk(GoobyRng rng, double startM, int lastIndex, Tune tune)
        {
            if (startM <= 0.0) return 0;
            var eligible = EligibleChunks(startM, lastIndex, tune);
            return eligible[(int)Math.Floor(rng.Next() * eligible.Count) % eligible.Count];
        }

        public static (List<Hazard> hazards, List<CoinRow> coins) ExpandChunk(Chunk def, double startM) =>
            (def.Hazards.Select(h => h.At(startM)).ToList(), def.Coins.Select(c => c.At(startM)).ToList());

        /// <summary>Obstacle density at a distance; only endless ramps up to DensityCap.</summary>
        public static double DensityMultAt(double distanceM, Tune tune)
        {
            if (!tune.Endless || tune.DensityCap <= tune.DensityMult) return tune.DensityMult;
            var t = Math.Max(0.0, Math.Min(1.0, distanceM / tune.DensityRampFullM));
            return tune.DensityMult + (tune.DensityCap - tune.DensityMult) * t;
        }

        /// <summary>Every ramp speed of a tune (BASE → MAX) — the validator's probe set.</summary>
        public static List<double> ValidatorProbeSpeeds(Tune tune)
        {
            var speeds = new List<double>();
            var v = tune.BaseSpeed;
            while (v <= tune.MaxSpeed + 1e-9) { speeds.Add(v); v += tune.SpeedStep; }
            if (speeds[speeds.Count - 1] < tune.MaxSpeed - 1e-9) speeds.Add(tune.MaxSpeed);
            return speeds;
        }

        /// <summary>
        /// Validator-backed pick (dense modes): the same single seeded draw, then rotation through the pool until a
        /// candidate is survivable at EVERY ramp speed together with the hazards still alive before it; −1 = none fits.
        /// </summary>
        public static int PickNextSurvivableChunk(GoobyRng rng, double startM, int lastIndex, List<Hazard> recent, Tune tune)
        {
            if (startM <= 0.0) return 0;
            var eligible = EligibleChunks(startM, lastIndex, tune);
            var first = (int)Math.Floor(rng.Next() * eligible.Count) % eligible.Count;
            var speeds = ValidatorProbeSpeeds(tune);
            for (var k = 0; k < eligible.Count; k++)
            {
                var idx = eligible[(first + k) % eligible.Count];
                var seq = new List<Hazard>(recent);
                seq.AddRange(ExpandChunk(Chunks[idx], startM).hazards);
                var ok = true;
                foreach (var v in speeds)
                    if (!IsSequenceSurvivable(seq, v, tune)) { ok = false; break; }
                if (ok) return idx;
            }
            return -1;
        }

        public sealed class Row
        {
            public double T;
            public string[] Lanes;
        }

        /// <summary>Bundle absolute hazards into arrival rows at a constant probe speed (puddles are soft and ignored).</summary>
        public static List<Row> HazardRows(IEnumerable<Hazard> hazards, double speed, Tune tune)
        {
            var events = new List<(double t, int[] lanes, string pass)>();
            foreach (var h in hazards)
            {
                if (!tune.Obstacles.TryGetValue(h.Kind, out var def)) continue;
                if (def.Pass == "soft") continue;
                var own = def.OwnSpeed;
                var meetM = Math.Max(h.AtM * speed / (speed + own), h.AtM - tune.SpawnAheadM * own / (speed + own));
                int[] lanes;
                if (h.Kind == "npc" || h.Kind == "gap") lanes = new[] { 0, 1, 2 };
                else if (h.Kind == "awning") lanes = h.Lanes;
                else lanes = new[] { h.Lane ?? 1 };
                events.Add((meetM / speed, lanes, def.Pass));
            }
            events = events.OrderBy(e => e.t).ToList();
            var rows = new List<Row>();
            foreach (var ev in events)
            {
                var merged = rows.Count > 0 && ev.t - rows[rows.Count - 1].T <= tune.ValidatorRowEpsSec;
                var target = merged ? rows[rows.Count - 1] : new Row { T = ev.t, Lanes = new string[tune.Lanes] };
                foreach (var lane in ev.lanes)
                {
                    var prev = target.Lanes[lane];
                    if (prev == "none") continue;
                    if (ev.pass == "none" || (prev != null && prev != ev.pass)) target.Lanes[lane] = "none";
                    else target.Lanes[lane] = ev.pass;
                }
                if (!merged) rows.Add(target);
            }
            return rows;
        }

        /// <summary>§C8.7 never-impossible proof: DP reachability over the lanes row by row.</summary>
        public static bool IsSequenceSurvivable(IEnumerable<Hazard> hazards, double speed, Tune tune)
        {
            var lanes = tune.Lanes;
            var rows = HazardRows(hazards, speed, tune);
            var react = tune.ValidatorReactSec;
            var reachable = new bool[lanes];
            var prevAction = new string[lanes];
            for (var i = 0; i < lanes; i++) reachable[i] = true;
            var prevT = -react;
            foreach (var row in rows)
            {
                var dt = row.T - prevT;
                var next = new bool[lanes];
                var nextAction = new string[lanes];
                for (var to = 0; to < lanes; to++)
                {
                    var need = row.Lanes[to];
                    if (need == "none") continue;
                    for (var from = 0; from < lanes; from++)
                    {
                        if (!reachable[from]) continue;
                        var busy = 0.0;
                        if (need != null && prevAction[from] != null) busy = prevAction[from] == "jump" ? tune.JumpSec : tune.SlideSec;
                        var cost = Math.Max(react, busy) + tune.ValidatorLaneCostSec * Math.Abs(to - from);
                        if (need != null) cost += tune.ValidatorActionLeadSec;
                        if (dt >= cost)
                        {
                            next[to] = true;
                            if (need == "jump" || need == "slide") nextAction[to] = need;
                            break;
                        }
                    }
                }
                reachable = next;
                prevAction = nextAction;
                if (!reachable.Contains(true)) return false;
                prevT = row.T;
            }
            return true;
        }

        /// <summary>§C8.4 next power-up kind: never the same twice, turbo at most every 400 m.</summary>
        public static string PlanPowerupKind(GoobyRng rng, string lastKind, double sinceTurboM, Tune tune)
        {
            var pool = new List<string>();
            foreach (var k in PowerupKinds)
            {
                if (k == lastKind) continue;
                if (k == "turbo" && sinceTurboM < tune.TurboMinGapM) continue;
                pool.Add(k);
            }
            return pool[(int)Math.Floor(rng.Next() * pool.Count) % pool.Count];
        }

        public static double PlanPowerupGap(GoobyRng rng, Tune tune) => tune.PowerupGapMinM + rng.Next() * (tune.PowerupGapMaxM - tune.PowerupGapMinM);

        /// <summary>Coin rain: a fractional COIN_RATE draws ONE seeded Bernoulli; COIN_RATE 1 draws nothing.</summary>
        public static int CoinRowCount(GoobyRng rng, int n, Tune tune)
        {
            var expected = Math.Max(0.0, n * tune.CoinRate);
            var whole = (int)Math.Floor(expected);
            var fraction = expected - whole;
            return whole + (fraction > 0.0 && rng.Next() < fraction ? 1 : 0);
        }
    }
}
