using System;
using System.Collections.Generic;
using Gooby.Core.Random;
using Newtonsoft.Json.Linq;

namespace Gooby.Core.Minigames.Games
{
    /// <summary>
    /// Hafen-Hopser — numerically identical port of harbor_hopper_logic.gd (web harborHopper.logic.js, PLAN3 §C10.1 #4).
    /// A fishing cutter sails down the harbour canal on its own (6 m/s); dragging steers it sideways with inertia.
    /// Crates +4, net rings +2, buoys/piers −3 and a slow-down. Wave bands roll towards the boat: crossing one on its
    /// foam crest = +30 % speed for 2 s (chainable). A gull steals the top crate after 4 s in one lane (warning call
    /// first). The horn clears buoys in a 6-m cone, 2 charges. Coin row /5, 4..30, target 110.
    /// </summary>
    public static class HarborHopper
    {
        public sealed class Tune
        {
            public double DurationSec = 120.0, BaseSpeed = 6.0, ChannelHalfW = 3.2;
            public int Lanes = 3;
            public double SteerAccel = 6.5, SteerDamping = 2.6, MaxLateralSpeed = 3.4;
            public int CratePoints = 4, RingPoints = 2, BumpPenalty = -3;
            public double CrateRadius = 0.8, RingRadius = 0.85, HitboxScale = 0.7, BuoyRadius = 0.75, BoatRadius = 0.6;
            public double PierReachM = 2.1, PierDepthM = 1.1;
            public double SlowFactor = 0.55, SlowSec = 1.4, BumpIframesSec = 1.0, BumpShove = 2.2;
            public double RowGapMin = 11.0, RowGapMax = 15.0;
            public double CrateChance = 0.44, RingChance = 0.2, BuoyChance = 0.26;
            public double PierEveryMin = 70.0, PierEveryMax = 110.0;
            public double LookaheadM = 60.0;
            public double WaveEverySec = 6.0, WaveSpeed = 2.5, WaveSpawnAheadM = 34.0, SweetHalfW = 1.05;
            public double BoostFactor = 1.3, BoostSec = 2.0;
            public double GullIdleSec = 4.0, GullWarnSec = 1.5;
            public int HornCharges = 2;
            public double HornConeM = 6.0, HornConeBase = 0.9, HornConeSpread = 0.45;
            public double MaxDt = 1.0 / 20.0;
            public double BotScanM = 16.0, BotDodgeM = 6.5, BotWaveM = 12.0, BotHornM = 4.5;
            public double BotCrateValue = 4.0, BotRingValue = 2.0, BotReachXPerM = 0.5, BotGullDodgeAtSec = 4.6;
            public bool Endless;
            public int EndlessBumpLimit = 3;
            public double EndlessAccelPerM = 0.004, EndlessMaxSpeed = 9.6;
            public double ValidatorReactSec = 0.35, ValidatorDodgeMarginM = 0.35;
            public double PickupRate = 1.0, ScoreMult = 1.0, PickupRadiusMult = 1.0, RenderScaleMult = 1.0;
            public double BotFocusSec = 1.9, BotLapseEverySec = 14.0;
            public string Mode = "normal";
            public Tune Clone() => (Tune)MemberwiseClone();
        }

        public static readonly Tune Harbor = new Tune();

        private struct DiffRow
        {
            public double Speed, Density, Duration, BotFocus, BotLapse;
        }

        private static readonly Dictionary<string, DiffRow> Difficulty = new Dictionary<string, DiffRow>
        {
            ["easy"] = new DiffRow { Speed = 0.85, Density = 0.85, Duration = 1.2, BotFocus = 1.5, BotLapse = 34.0 },
            ["normal"] = new DiffRow { Speed = 1.0, Density = 1.0, Duration = 1.0, BotFocus = 1.9, BotLapse = 14.0 },
            ["hard"] = new DiffRow { Speed = 1.2, Density = 1.15, Duration = 1.0, BotFocus = 1.9, BotLapse = 12.5 },
            ["endless"] = new DiffRow { Speed = 1.2, Density = 1.15, Duration = 1.0, BotFocus = 1.9, BotLapse = 12.5 },
        };

        /// <summary>Derived tune; normal returns the base table unchanged.</summary>
        public static Tune ApplyDifficulty(Tune tune, string mode = "normal")
        {
            if (mode == "normal" || mode == null || !Difficulty.TryGetValue(mode, out var row)) return tune;
            var t = tune.Clone();
            t.BaseSpeed = tune.BaseSpeed * row.Speed;
            t.DurationSec = tune.DurationSec * row.Duration;
            t.BuoyChance = tune.BuoyChance * row.Density;
            t.PierEveryMin = tune.PierEveryMin / row.Density;
            t.PierEveryMax = tune.PierEveryMax / row.Density;
            t.BotFocusSec = row.BotFocus;
            t.BotLapseEverySec = row.BotLapse;
            t.Endless = mode == "endless";
            t.Mode = mode;
            return t;
        }

        /// <summary>§G5.3 guard rail: does a full buoy dodge fit between two rows? ≥ 1 = always avoidable.</summary>
        public static double RowReachability(Tune tune)
        {
            var maxBase = tune.Endless ? Math.Max(tune.BaseSpeed, tune.EndlessMaxSpeed) : tune.BaseSpeed;
            var worst = maxBase * tune.BoostFactor;
            var rowSec = tune.RowGapMin / worst;
            var dodgeM = (tune.BuoyRadius + tune.BoatRadius) * tune.HitboxScale + tune.ValidatorDodgeMarginM;
            var needSec = tune.ValidatorReactSec + dodgeM / tune.MaxLateralSpeed;
            return rowSec / needSec;
        }

        public static int HopperScore(State s, Tune tune) => (int)Math.Round(s.Score * tune.ScoreMult, MidpointRounding.AwayFromZero);

        /// <summary>Lane 0·1·2 across the canal (only for the gull's lane-idle detection).</summary>
        public static int LaneOf(double x, Tune tune)
        {
            var w = tune.ChannelHalfW * 2.0 / tune.Lanes;
            return Math.Max(0, Math.Min(tune.Lanes - 1, (int)Math.Floor((x + tune.ChannelHalfW) / w)));
        }

        /// <summary>Current forward speed: base × boost × slow (endless accelerates with distance).</summary>
        public static double SpeedOf(State s, Tune tune)
        {
            var v = tune.BaseSpeed;
            if (tune.Endless) v = Math.Min(tune.EndlessMaxSpeed, v + s.Z * tune.EndlessAccelPerM);
            if (s.BoostT > 0.0) v *= tune.BoostFactor;
            if (s.SlowT > 0.0) v *= tune.SlowFactor;
            return v;
        }

        /// <summary>Circle against circle with the 70 % obstacle forgiveness.</summary>
        public static bool Hits(double bx, double bz, Item item, double radius, bool forgiving, Tune tune)
        {
            var r = forgiving ? radius * tune.HitboxScale : radius;
            var dx = item.X - bx;
            var dz = item.Z - bz;
            return dx * dx + dz * dz <= r * r;
        }

        /// <summary>A pier finger reaches PierReachM from its side into the canal, PierDepthM deep.</summary>
        public static bool HitsPier(double bx, double bz, Pier pier, Tune tune)
        {
            var depth = (tune.PierDepthM / 2.0 + tune.BoatRadius) * tune.HitboxScale;
            if (Math.Abs(bz - pier.Z) > depth) return false;
            var inner = tune.ChannelHalfW - tune.PierReachM * tune.HitboxScale;
            return pier.Side < 0 ? bx <= -inner : bx >= inner;
        }

        /// <summary>The horn cone: 6 m ahead, widening.</summary>
        public static bool InHornCone(double bx, double bz, Item buoy, Tune tune)
        {
            var dz = buoy.Z - bz;
            if (dz < -0.5 || dz > tune.HornConeM) return false;
            return Math.Abs(buoy.X - bx) <= tune.HornConeBase + Math.Max(0.0, dz) * tune.HornConeSpread;
        }

        public static int ApplyScore(int score, int delta) => Math.Max(0, score + delta);

        public sealed class Item
        {
            public string Type;
            public double X, Z;
            public bool Gone;
        }

        public sealed class Pier
        {
            public int Side;
            public double Z;
            public bool Hit;
        }

        public sealed class Wave
        {
            public double Z, SweetX;
            public bool Ridden;
        }

        public sealed class State
        {
            public double X, Vx, Z;
            public int Score, Crates, Rings, Bumps, Steals;
            public double BoostT;
            public int BoostChain;
            public double SlowT, IframesT;
            public int HornCharges;
            public double IdleT;
            public int Lane;
            public string GullPhase = "idle";
            public double GullT;
            public double Elapsed;
            public bool Ended;
            public string EndReason = "";
        }

        public struct Ev
        {
            public string Type;
            public int Count, Chain;
            public string What;
            public Item Item;
            public double SweetX;
        }

        public struct Input
        {
            public double? TargetX;
            public bool Horn;
        }

        /// <summary>The deterministic harbour machine (web createEngine); view and bot drive the SAME class.</summary>
        public sealed class Engine
        {
            public readonly State S;
            public readonly List<Item> Items = new List<Item>();
            public readonly List<Pier> Piers = new List<Pier>();
            public readonly List<Wave> Waves = new List<Wave>();
            private readonly Tune _tune;
            private readonly GoobyRng _rng;
            private double _genZ = 14.0;
            private double _nextPierZ;
            private double _nextWaveAt;

            public Engine(GoobyRng rng, Tune tune)
            {
                _rng = rng;
                _tune = tune;
                S = new State { HornCharges = tune.HornCharges, Lane = LaneOf(0.0, tune) };
                _nextPierZ = tune.PierEveryMin + _rng.Next() * (tune.PierEveryMax - tune.PierEveryMin);
                _nextWaveAt = tune.WaveEverySec * 0.6;
                GenerateAhead(tune.LookaheadM);
            }

            /// <summary>One frame; returns the semantic events for view and tests.</summary>
            public List<Ev> Step(Input input, double dt)
            {
                var events = new List<Ev>();
                if (S.Ended) return events;
                dt = Math.Min(_tune.MaxDt, Math.Max(0.0, dt));
                S.Elapsed += dt;
                if (!_tune.Endless && S.Elapsed >= _tune.DurationSec)
                {
                    S.Ended = true;
                    S.EndReason = "time";
                    events.Add(new Ev { Type = "ended" });
                    return events;
                }

                if (S.BoostT > 0.0) S.BoostT = Math.Max(0.0, S.BoostT - dt);
                if (S.BoostT == 0.0) S.BoostChain = 0;
                if (S.SlowT > 0.0) S.SlowT = Math.Max(0.0, S.SlowT - dt);
                if (S.IframesT > 0.0) S.IframesT = Math.Max(0.0, S.IframesT - dt);

                if (input.Horn)
                {
                    if (S.HornCharges > 0)
                    {
                        S.HornCharges -= 1;
                        var cleared = 0;
                        foreach (var item in Items)
                        {
                            if (item.Gone || item.Type != "buoy") continue;
                            if (InHornCone(S.X, S.Z, item, _tune)) { item.Gone = true; cleared++; }
                        }
                        events.Add(new Ev { Type = "buoyCleared", Count = cleared });
                    }
                    else events.Add(new Ev { Type = "hornEmpty" });
                }

                if (input.TargetX.HasValue)
                {
                    var target = Math.Max(-_tune.ChannelHalfW + 0.35, Math.Min(_tune.ChannelHalfW - 0.35, input.TargetX.Value));
                    var err = target - S.X;
                    S.Vx += Math.Max(-1.0, Math.Min(1.0, err / 0.9)) * _tune.SteerAccel * dt;
                }
                S.Vx -= S.Vx * Math.Min(1.0, _tune.SteerDamping * dt);
                S.Vx = Math.Max(-_tune.MaxLateralSpeed, Math.Min(_tune.MaxLateralSpeed, S.Vx));
                S.X += S.Vx * dt;
                var wallX = _tune.ChannelHalfW - 0.35;
                if (S.X < -wallX) { S.X = -wallX; S.Vx = Math.Abs(S.Vx) * 0.25; }
                else if (S.X > wallX) { S.X = wallX; S.Vx = -Math.Abs(S.Vx) * 0.25; }

                var prevBoatZ = S.Z;
                S.Z += SpeedOf(S, _tune) * dt;
                GenerateAhead(S.Z + _tune.LookaheadM);

                if (S.Elapsed >= _nextWaveAt)
                {
                    var sweet = (_rng.Next() * 2.0 - 1.0) * (_tune.ChannelHalfW - _tune.SweetHalfW);
                    Waves.Add(new Wave { Z = S.Z + _tune.WaveSpawnAheadM, SweetX = sweet });
                    events.Add(new Ev { Type = "waveSpawn", SweetX = sweet });
                    _nextWaveAt += _tune.WaveEverySec;
                }
                foreach (var wave in Waves)
                {
                    var prevZ = wave.Z;
                    wave.Z = prevZ - _tune.WaveSpeed * dt;
                    if (!wave.Ridden && prevZ >= prevBoatZ && wave.Z <= S.Z)
                    {
                        if (Math.Abs(S.X - wave.SweetX) <= _tune.SweetHalfW && S.SlowT == 0.0)
                        {
                            S.BoostChain += 1;
                            S.BoostT = _tune.BoostSec;
                            events.Add(new Ev { Type = "boost", Chain = S.BoostChain });
                        }
                        wave.Ridden = true;
                    }
                }
                for (var i = Waves.Count - 1; i >= 0; i--)
                    if (Waves[i].Z < S.Z - 8.0) Waves.RemoveAt(i);

                foreach (var item in Items)
                {
                    if (item.Gone || item.Z < S.Z - 2.0 || item.Z > S.Z + 3.0) continue;
                    if (item.Type == "crate" && Hits(S.X, S.Z, item, _tune.CrateRadius + _tune.BoatRadius, false, _tune))
                    {
                        item.Gone = true;
                        S.Crates += 1;
                        S.Score = ApplyScore(S.Score, _tune.CratePoints);
                        events.Add(new Ev { Type = "crate", Item = item });
                    }
                    else if (item.Type == "ring" && Hits(S.X, S.Z, item, _tune.RingRadius + _tune.BoatRadius, false, _tune))
                    {
                        item.Gone = true;
                        S.Rings += 1;
                        S.Score = ApplyScore(S.Score, _tune.RingPoints);
                        events.Add(new Ev { Type = "ring", Item = item });
                    }
                    else if (item.Type == "buoy" && S.IframesT == 0.0 && Hits(S.X, S.Z, item, _tune.BuoyRadius + _tune.BoatRadius, true, _tune))
                    {
                        var away = S.X <= item.X ? -1.0 : 1.0;
                        S.Vx = away * _tune.BumpShove;
                        Bump(events, "buoy");
                    }
                }
                if (S.IframesT == 0.0)
                {
                    foreach (var pier in Piers)
                    {
                        if (Math.Abs(pier.Z - S.Z) > 3.0) continue;
                        if (HitsPier(S.X, S.Z, pier, _tune))
                        {
                            pier.Hit = true;
                            S.Vx = -pier.Side * _tune.BumpShove;
                            Bump(events, "pier");
                        }
                    }
                }
                for (var i = Items.Count - 1; i >= 0; i--)
                    if (Items[i].Z < S.Z - 10.0) Items.RemoveAt(i);

                var lane = LaneOf(S.X, _tune);
                if (lane != S.Lane)
                {
                    S.Lane = lane;
                    S.IdleT = 0.0;
                    if (S.GullPhase == "warn")
                    {
                        S.GullPhase = "idle";
                        S.GullT = 0.0;
                        events.Add(new Ev { Type = "gullLeave" });
                    }
                }
                else if (S.Crates > 0)
                {
                    S.IdleT += dt;
                    if (S.GullPhase == "idle" && S.IdleT >= _tune.GullIdleSec)
                    {
                        S.GullPhase = "warn";
                        S.GullT = 0.0;
                        events.Add(new Ev { Type = "gullWarn" });
                    }
                    else if (S.GullPhase == "warn")
                    {
                        S.GullT += dt;
                        if (S.GullT >= _tune.GullWarnSec)
                        {
                            S.GullPhase = "idle";
                            S.GullT = 0.0;
                            S.IdleT = 0.0;
                            S.Crates -= 1;
                            S.Steals += 1;
                            S.Score = ApplyScore(S.Score, -_tune.CratePoints);
                            events.Add(new Ev { Type = "gullSteal" });
                        }
                    }
                }
                else
                {
                    S.IdleT = 0.0;
                    if (S.GullPhase == "warn")
                    {
                        S.GullPhase = "idle";
                        S.GullT = 0.0;
                        events.Add(new Ev { Type = "gullLeave" });
                    }
                }
                return events;
            }

            private void Bump(List<Ev> events, string what)
            {
                S.Score = ApplyScore(S.Score, _tune.BumpPenalty);
                S.Bumps += 1;
                S.SlowT = _tune.SlowSec;
                S.IframesT = _tune.BumpIframesSec;
                S.BoostT = 0.0;
                S.BoostChain = 0;
                events.Add(new Ev { Type = "bump", What = what });
                if (_tune.Endless && S.Bumps >= _tune.EndlessBumpLimit)
                {
                    S.Ended = true;
                    S.EndReason = "bumps";
                    events.Add(new Ev { Type = "ended", What = "bumps" });
                }
            }

            private void GenerateAhead(double untilZ)
            {
                var half = _tune.ChannelHalfW;
                while (_genZ < untilZ)
                {
                    var roll = _rng.Next();
                    var x = (_rng.Next() * 2.0 - 1.0) * (half - 0.55);
                    if (roll < _tune.CrateChance) Items.Add(new Item { Type = "crate", X = x, Z = _genZ });
                    else if (roll < _tune.CrateChance + _tune.RingChance) Items.Add(new Item { Type = "ring", X = x, Z = _genZ });
                    else if (roll < _tune.CrateChance + _tune.RingChance + _tune.BuoyChance) Items.Add(new Item { Type = "buoy", X = x, Z = _genZ });
                    var gap = _tune.RowGapMin + _rng.Next() * (_tune.RowGapMax - _tune.RowGapMin);
                    _genZ += gap;
                    if (_tune.PickupRate > 1.0 && _rng.Next() < (_tune.PickupRate - 1.0) * (_tune.CrateChance + _tune.RingChance))
                    {
                        var px = (_rng.Next() * 2.0 - 1.0) * (half - 0.55);
                        var isCrate = _rng.Next() < _tune.CrateChance / (_tune.CrateChance + _tune.RingChance);
                        Items.Add(new Item { Type = isCrate ? "crate" : "ring", X = px, Z = _genZ - gap / 2.0 });
                    }
                    if (_genZ >= _nextPierZ)
                    {
                        int side;
                        if (Piers.Count % 2 == 0) side = _rng.Next() < 0.5 ? -1 : 1;
                        else side = -Piers[Piers.Count - 1].Side;
                        Piers.Add(new Pier { Side = side, Z = _nextPierZ });
                        _nextPierZ += _tune.PierEveryMin + _rng.Next() * (_tune.PierEveryMax - _tune.PierEveryMin);
                    }
                }
            }
        }

        /// <summary>Greedy autoplay skipper (§C10.1: crate path + crest centring).</summary>
        public sealed class Bot
        {
            private readonly Tune _tune;
            private readonly double _reachPerM;
            private int _dodgeToLane = -1;

            public Bot(Tune tune)
            {
                _tune = tune;
                _reachPerM = tune.BotReachXPerM * (Harbor.BaseSpeed / tune.BaseSpeed);
            }

            public Input Control(State s, IList<Item> items, IList<Pier> piers, IList<Wave> waves)
            {
                var half = _tune.ChannelHalfW;
                var targetX = 0.0;
                var hasTarget = false;

                Wave bestWave = null;
                foreach (var wave in waves)
                {
                    if (wave.Ridden || wave.Z < s.Z) continue;
                    var dz = wave.Z - s.Z;
                    if (dz <= _tune.BotWaveM && (bestWave == null || dz < bestWave.Z - s.Z)) bestWave = wave;
                }
                if (bestWave != null) { targetX = bestWave.SweetX; hasTarget = true; }

                if (!hasTarget)
                {
                    Item best = null;
                    var bestScore = double.NegativeInfinity;
                    foreach (var item in items)
                    {
                        if (item.Gone || item.Type == "buoy") continue;
                        var dz = item.Z - s.Z;
                        if (dz < 1.0 || dz > _tune.BotScanM) continue;
                        var reach = Math.Abs(item.X - s.X) / Math.Max(1.0, dz * _reachPerM);
                        if (reach > 1.15) continue;
                        var value = (item.Type == "crate" ? _tune.BotCrateValue : _tune.BotRingValue) - dz * 0.12 - reach;
                        if (value > bestScore) { bestScore = value; best = item; }
                    }
                    targetX = best != null ? best.X : s.X * 0.6;
                }

                var horn = false;
                foreach (var item in items)
                {
                    if (item.Gone || item.Type != "buoy") continue;
                    var dz = item.Z - s.Z;
                    if (dz < 0.4 || dz > _tune.BotDodgeM) continue;
                    var clearance = (_tune.BuoyRadius + _tune.BoatRadius) * _tune.HitboxScale + 0.35;
                    if (Math.Abs(item.X - targetX) < clearance)
                    {
                        var off = s.X <= item.X ? -clearance : clearance;
                        targetX = item.X + off;
                    }
                    if (dz <= _tune.BotHornM && Math.Abs(item.X - s.X) < clearance * 0.8 && s.HornCharges > 0) horn = true;
                }
                foreach (var pier in piers)
                {
                    var dz = pier.Z - s.Z;
                    if (dz < 0.0 || dz > _tune.BotDodgeM) continue;
                    var inner = half - _tune.PierReachM - 0.45;
                    if (pier.Side < 0) targetX = Math.Max(targetX, -inner);
                    else targetX = Math.Min(targetX, inner);
                }

                var lanes = _tune.Lanes;
                if (_dodgeToLane < 0 && s.Crates > 0 && (s.GullPhase == "warn" || s.IdleT >= _tune.BotGullDodgeAtSec))
                {
                    var lane = LaneOf(s.X, _tune);
                    if (lane == 0) _dodgeToLane = 1;
                    else if (lane == lanes - 1) _dodgeToLane = lanes - 2;
                    else _dodgeToLane = targetX >= s.X ? lane + 1 : lane - 1;
                }
                if (_dodgeToLane >= 0)
                {
                    if (s.IdleT < 0.2 || s.Crates == 0) _dodgeToLane = -1;
                    else
                    {
                        var laneW = half * 2.0 / lanes;
                        targetX = -half + laneW * (_dodgeToLane + 0.5);
                    }
                }
                return new Input { TargetX = Math.Max(-half + 0.4, Math.Min(half - 0.4, targetX)), Horn = horn };
            }
        }

        /// <summary>Headless full round (tests/tuning): machine + greedy bot at a fixed dt.</summary>
        public static JObject SimulateRound(int seed, Tune tune, double dt = 1.0 / 60.0)
        {
            var engine = new Engine(new GoobyRng(seed), tune);
            var bot = new Bot(tune);
            var boosts = 0;
            var guard = (int)Math.Ceiling((tune.DurationSec + 10.0) / dt);
            while (!engine.S.Ended && guard > 0)
            {
                var c = bot.Control(engine.S, engine.Items, engine.Piers, engine.Waves);
                foreach (var ev in engine.Step(c, dt)) if (ev.Type == "boost") boosts++;
                guard--;
            }
            var s = engine.S;
            return new JObject
            {
                ["score"] = s.Score, ["crates"] = s.Crates, ["rings"] = s.Rings, ["bumps"] = s.Bumps, ["steals"] = s.Steals,
                ["boosts"] = boosts, ["distanceM"] = (int)Math.Floor(s.Z), ["hornsUsed"] = tune.HornCharges - s.HornCharges,
            };
        }

        /// <summary>§G5.4 certificate run: a full bot round with an attention model (focus cool-down + seeded lapses).</summary>
        public static JObject SimulateAutoplay(string mode = "normal", int seed = 1, double maxSec = 900.0)
        {
            var tune = ApplyDifficulty(Harbor, mode);
            var engine = new Engine(new GoobyRng(seed), tune);
            var bot = new Bot(tune);
            var lapse = new GoobyRng(unchecked((uint)seed ^ 0x9D2C5681u));
            var lapseEvery = tune.BotLapseEverySec > 0.0 ? tune.BotLapseEverySec : double.PositiveInfinity;
            var nextLapse = lapseEvery * (0.5 + lapse.Next());
            var lapseT = 0.0;
            var boosts = 0;
            var focusT = 0.0;
            const double dt = 1.0 / 60.0;
            var guard = (int)Math.Ceiling(maxSec / dt);
            while (!engine.S.Ended && guard > 0)
            {
                IList<Item> seen = engine.Items;
                if (focusT > 0.0)
                {
                    var onlyBuoys = new List<Item>();
                    foreach (var item in engine.Items) if (item.Type == "buoy") onlyBuoys.Add(item);
                    seen = onlyBuoys;
                }
                var c = bot.Control(engine.S, seen, engine.Piers, engine.Waves);
                nextLapse -= dt;
                if (nextLapse <= 0.0 && lapseT <= 0.0)
                {
                    lapseT = 0.7 + lapse.Next() * 0.9;
                    nextLapse = lapseEvery * (0.5 + lapse.Next());
                }
                if (lapseT > 0.0)
                {
                    lapseT -= dt;
                    c.TargetX = null;
                    c.Horn = false;
                }
                foreach (var ev in engine.Step(c, dt))
                {
                    if (ev.Type == "boost") boosts++;
                    if (ev.Type == "crate" || ev.Type == "ring") focusT = tune.BotFocusSec;
                }
                if (focusT > 0.0) focusT -= dt;
                guard--;
            }
            var s = engine.S;
            return new JObject
            {
                ["score"] = HopperScore(s, tune), ["crates"] = s.Crates, ["rings"] = s.Rings, ["bumps"] = s.Bumps,
                ["steals"] = s.Steals, ["boosts"] = boosts, ["distanceM"] = (int)Math.Floor(s.Z), ["elapsed"] = s.Elapsed,
            };
        }
    }
}
