using System;
using System.Collections.Generic;
using Gooby.Core.Random;

namespace Gooby.Core.Minigames.Games
{
    /// <summary>
    /// Einkaufs-Surf run simulation — numerically identical port of shopping_surf_run.gd (§C8.2–§C8.7). Pure maths: the
    /// view feeds edge-triggered inputs and plays the returned events as sound/particles.
    /// </summary>
    public static class ShoppingSurfRun
    {
        public sealed class Obstacle
        {
            public int Id;
            public string Kind;
            public ShoppingSurf.ObDef Def;
            public int? Lane;
            public int[] Lanes;
            public double Z, X, HalfW;
            public bool Telegraphed, Hit, Passed;
            public double MinClear = double.PositiveInfinity;
        }

        public sealed class CoinItem
        {
            public int Id, Lane;
            public double X, Y, Z;
            public bool Attracted;
        }

        public sealed class PowerupItem
        {
            public int Id, Lane;
            public string Kind;
            public double X, Z;
        }

        public struct Ev
        {
            public string Type, Kind;
            public int Id, Dir, Crashes, Streak, Value, CoinsCollected;
            public double X, Y, Z;
            public Obstacle Ob;
        }

        public struct Input
        {
            public bool Left, Right, Jump, Slide;
            public bool Any => Left || Right || Jump || Slide;
        }

        public sealed class Run
        {
            public ShoppingSurf.Tune Tune;
            public GoobyRng Rng;
            public string Mode;
            public double Elapsed, DistanceM, RampSec, Speed, SlowT;
            public int Lane = 1;
            public double FromX, LaneT = 1.0, JumpT = -1.0, SlideT = -1.0;
            public bool FastDrop;
            public double FastDropY;
            public string BufferedType;
            public double BufferedT;
            public int Crashes;
            public double StumbleT, InvulnT;
            public int Coins, NearMisses, NearStreak, PowerupsCollected;
            public double MagnetT, X2T, TurboT;
            public bool Shield;
            public string LastPowerupKind;
            public double LastTurboAtM = double.NegativeInfinity, NextPowerupAtM;
            public bool Jog, Finished, Ended;
            public double ChunksEndM;
            public int LastChunk = -1;
            public List<ShoppingSurf.Hazard> RecentHazards = new List<ShoppingSurf.Hazard>();
            public List<ShoppingSurf.Hazard> PendingHazards = new List<ShoppingSurf.Hazard>();
            public List<ShoppingSurf.CoinRow> PendingCoins = new List<ShoppingSurf.CoinRow>();
            public readonly List<Obstacle> Obstacles = new List<Obstacle>();
            public readonly List<CoinItem> CoinItems = new List<CoinItem>();
            public readonly List<PowerupItem> PowerupItems = new List<PowerupItem>();
            public int NextId = 1;
        }

        public static Run CreateRun(GoobyRng rng, string mode, ShoppingSurf.Tune tune) => new Run
        {
            Tune = tune, Rng = rng, Mode = ShoppingSurf.IsTravelMode(mode) ? "travel" : "arcade",
            Speed = tune.BaseSpeed, FromX = tune.LaneX[1],
        };

        /// <summary>Smoothed lane x of the player (m).</summary>
        public static double PlayerX(Run run)
        {
            var t = Math.Min(1.0, run.LaneT);
            var e = t * t * (3.0 - 2.0 * t);
            var target = run.Tune.LaneX[run.Lane];
            return run.FromX + (target - run.FromX) * e;
        }

        /// <summary>Jump height of the player (m).</summary>
        public static double PlayerY(Run run)
        {
            if (run.JumpT < 0.0) return 0.0;
            if (run.FastDrop) return run.FastDropY;
            return run.Tune.JumpHeight * Math.Sin(run.JumpT / run.Tune.JumpSec * Math.PI);
        }

        public static double CurrentSpeed(Run run)
        {
            var tune = run.Tune;
            if (run.Ended) return 0.0;
            if (run.Jog) return tune.TravelJogSpeed;
            var v = ShoppingSurf.SpeedRampAt(run.RampSec, tune);
            if (run.TurboT > 0.0) v *= tune.TurboSpeedMult;
            if (run.SlowT > 0.0) v *= tune.Obstacles["puddle"].SlowMult;
            if (run.StumbleT > 0.0) v *= tune.StumbleSpeedMult;
            return v;
        }

        private static bool TryLane(Run run, string type, List<Ev> events)
        {
            if (run.LaneT < 0.65) return false;
            var dir = type == "left" ? -1 : 1;
            var next = Math.Max(0, Math.Min(run.Tune.Lanes - 1, run.Lane + dir));
            if (next != run.Lane)
            {
                run.FromX = PlayerX(run);
                run.Lane = next;
                run.LaneT = 0.0;
                events.Add(new Ev { Type = "lane", Dir = dir });
            }
            return true;
        }

        private static bool TrySlide(Run run, List<Ev> events)
        {
            if (run.JumpT >= 0.0)
            {
                // a swipe down in the air is a fast drop (freeze the height BEFORE the flag: PlayerY reads FastDropY then)
                if (!run.FastDrop)
                {
                    run.FastDropY = PlayerY(run);
                    run.FastDrop = true;
                    events.Add(new Ev { Type = "fastDrop" });
                }
                return true;
            }
            if (run.SlideT >= 0.0) return false;
            run.SlideT = 0.0;
            events.Add(new Ev { Type = "slide" });
            return true;
        }

        private static bool TryAction(Run run, string type, List<Ev> events)
        {
            if (type == "left" || type == "right") return TryLane(run, type, events);
            if (type == "jump")
            {
                if (run.JumpT >= 0.0 || run.SlideT >= 0.0) return false;
                run.JumpT = 0.0;
                run.FastDrop = false;
                events.Add(new Ev { Type = "jump" });
                return true;
            }
            if (type == "slide") return TrySlide(run, events);
            return false;
        }

        private static void ApplyInput(Run run, Input input, List<Ev> events)
        {
            if (!input.Any || run.Ended || run.Finished) return;
            var wants = new List<string>();
            if (input.Left) wants.Add("left");
            if (input.Right) wants.Add("right");
            if (input.Jump) wants.Add("jump");
            if (input.Slide) wants.Add("slide");
            foreach (var type in wants)
                if (!TryAction(run, type, events)) { run.BufferedType = type; run.BufferedT = 0.0; }
            if (run.BufferedType != null && TryAction(run, run.BufferedType, events)) run.BufferedType = null;
        }

        private static void EnqueueChunks(Run run)
        {
            var tune = run.Tune;
            var chunkLen = tune.ChunkLenM;
            var gated = tune.GatedSpawns;
            while (run.ChunksEndM < run.DistanceM + tune.SpawnAheadM + chunkLen)
            {
                var startM = run.ChunksEndM;
                int idx;
                if (gated)
                {
                    var window = new List<ShoppingSurf.Hazard>();
                    foreach (var h in run.RecentHazards) if (h.AtM > startM - chunkLen * 2.0) window.Add(h);
                    run.RecentHazards = window;
                    idx = ShoppingSurf.PickNextSurvivableChunk(run.Rng, startM, run.LastChunk, window, tune);
                    if (idx < 0) { run.ChunksEndM = startM + chunkLen * 0.35; continue; }
                }
                else idx = ShoppingSurf.PickNextChunk(run.Rng, startM, run.LastChunk, tune);
                var parts = ShoppingSurf.ExpandChunk(ShoppingSurf.Chunks[idx], startM);
                run.PendingHazards.AddRange(parts.hazards);
                run.PendingCoins.AddRange(parts.coins);
                run.LastChunk = idx;
                if (gated)
                {
                    run.RecentHazards.AddRange(parts.hazards);
                    run.ChunksEndM = startM + chunkLen / ShoppingSurf.DensityMultAt(startM, tune);
                }
                else run.ChunksEndM = startM + chunkLen;
            }
        }

        private static Obstacle Materialize(Run run, ShoppingSurf.Hazard h)
        {
            var tune = run.Tune;
            var def = tune.Obstacles[h.Kind];
            var x = tune.LaneX[h.Lane ?? 1];
            var halfW = h.Kind == "gap" ? 99.0 : def.HalfW;
            if (h.Kind == "npc") x = -(tune.LaneX[tune.Lanes - 1] + 1.0);
            else if (h.Kind == "awning")
            {
                var lo = int.MaxValue; var hi = int.MinValue;
                foreach (var l in h.Lanes) { lo = Math.Min(lo, l); hi = Math.Max(hi, l); }
                x = (tune.LaneX[lo] + tune.LaneX[hi]) / 2.0;
                halfW = ((hi - lo) * tune.LaneW + tune.LaneW * 0.92) / 2.0;
            }
            run.NextId += 1;
            return new Obstacle { Id = run.NextId - 1, Kind = h.Kind, Def = def, Lane = h.Lane, Lanes = h.Lanes, Z = -(h.AtM - run.DistanceM), X = x, HalfW = halfW };
        }

        private static void SpawnHazards(Run run, double horizon, List<Ev> events)
        {
            var noHazards = run.Jog || run.Finished;
            var finishM = run.Tune.TravelDistanceM;
            var keep = new List<ShoppingSurf.Hazard>();
            foreach (var h in run.PendingHazards)
            {
                if (h.AtM > horizon) { keep.Add(h); continue; }
                if (noHazards) continue;
                if (run.Mode == "travel" && h.AtM >= finishM - 4.0) continue;
                var ob = Materialize(run, h);
                run.Obstacles.Add(ob);
                events.Add(new Ev { Type = "spawn", Ob = ob, Id = ob.Id, Kind = ob.Kind });
            }
            run.PendingHazards = keep;
        }

        private static void SpawnCoins(Run run, double horizon)
        {
            var tune = run.Tune;
            var step = tune.CoinStepM;
            var keep = new List<ShoppingSurf.CoinRow>();
            foreach (var c in run.PendingCoins)
            {
                if (c.AtM > horizon) { keep.Add(c); continue; }
                var n = ShoppingSurf.CoinRowCount(run.Rng, c.N, tune);
                for (var i = 0; i < n; i++)
                {
                    var zOff = (i - (n - 1) / 2.0) * step;
                    var y = tune.CoinY;
                    if (c.Arc)
                    {
                        var phase = zOff / (n * step / 2.0) * Math.PI / 2.0;
                        y += tune.JumpHeight * 0.85 * Math.Pow(Math.Cos(phase), 2.0);
                    }
                    run.NextId += 1;
                    run.CoinItems.Add(new CoinItem { Id = run.NextId - 1, Lane = c.Lane, X = tune.LaneX[c.Lane], Y = y, Z = -(c.AtM - run.DistanceM) + zOff });
                }
            }
            run.PendingCoins = keep;
        }

        private static void SpawnPowerup(Run run, double horizon)
        {
            var tune = run.Tune;
            if (run.Finished || run.NextPowerupAtM > horizon) return;
            var atM = run.NextPowerupAtM;
            var sinceTurbo = atM - run.LastTurboAtM;
            var kind = ShoppingSurf.PlanPowerupKind(run.Rng, run.LastPowerupKind, sinceTurbo, tune);
            var lane = (int)Math.Floor(run.Rng.Next() * tune.Lanes) % tune.Lanes;
            if (!(run.Mode == "travel" && atM >= tune.TravelDistanceM - 6.0))
            {
                run.NextId += 1;
                run.PowerupItems.Add(new PowerupItem { Id = run.NextId - 1, Kind = kind, Lane = lane, X = tune.LaneX[lane], Z = -(atM - run.DistanceM) });
            }
            if (kind == "turbo") run.LastTurboAtM = atM;
            run.LastPowerupKind = kind;
            run.NextPowerupAtM = atM + ShoppingSurf.PlanPowerupGap(run.Rng, tune);
        }

        private static void SpawnStep(Run run, List<Ev> events)
        {
            if (run.NextPowerupAtM == 0.0) run.NextPowerupAtM = ShoppingSurf.PlanPowerupGap(run.Rng, run.Tune);
            EnqueueChunks(run);
            var horizon = run.DistanceM + run.Tune.SpawnAheadM;
            SpawnHazards(run, horizon, events);
            SpawnCoins(run, horizon);
            SpawnPowerup(run, horizon);
        }

        private static bool HitsNow(Run run, Obstacle ob, double px, double py, bool sliding)
        {
            var tune = run.Tune;
            var def = ob.Def;
            if (Math.Abs(ob.Z) > def.HalfDepth + tune.PlayerHalfDepth) return false;
            if (ob.Kind == "gap") return py < 0.12 && Math.Abs(ob.Z) < def.HalfDepth;
            if (Math.Abs(px - ob.X) >= ob.HalfW + tune.PlayerHalfW) return false;
            if (def.Pass == "jump") return py < def.ClearY;
            if (def.Pass == "slide") return !(sliding && tune.SlideHeight <= def.GapY);
            return def.Pass == "soft" ? py < 0.1 : true;
        }

        private static bool SweepHits(Run run, Obstacle ob, double dz, double px, double py, bool sliding)
        {
            var steps = Math.Max(1, (int)Math.Ceiling(Math.Abs(dz) / run.Tune.MaxSweepStepM));
            var z0 = ob.Z;
            var hit = false;
            for (var i = 1; i < steps + 1; i++)
            {
                ob.Z = z0 + dz * i / steps;
                if (HitsNow(run, ob, px, py, sliding)) { hit = true; break; }
            }
            ob.Z = z0;
            return hit;
        }

        private static void HandleHit(Run run, Obstacle ob, List<Ev> events)
        {
            var tune = run.Tune;
            if (ob.Kind == "puddle")
            {
                if (ob.Hit || run.TurboT > 0.0) return;
                ob.Hit = true;
                run.SlowT = tune.Obstacles["puddle"].SlowSec;
                events.Add(new Ev { Type = "puddle", Id = ob.Id });
                return;
            }
            if (run.TurboT > 0.0 || run.InvulnT > 0.0) return;
            ob.Hit = true;
            if (run.Shield)
            {
                run.Shield = false;
                run.InvulnT = tune.InvulnSec;
                events.Add(new Ev { Type = "shieldPop", Id = ob.Id });
                return;
            }
            run.Crashes += 1;
            run.StumbleT = tune.StumbleSec;
            run.InvulnT = tune.InvulnSec;
            run.RampSec = 0.0;
            run.NearStreak = 0;
            events.Add(new Ev { Type = "crash", Id = ob.Id, Kind = ob.Kind, Crashes = run.Crashes });
            if (run.Mode == "arcade" && run.Crashes >= tune.ArcadeMaxCrashes)
            {
                run.Ended = true;
                events.Add(new Ev { Type = "wipeout" });
            }
            else if (run.Mode == "travel" && run.Crashes >= tune.ArcadeMaxCrashes && !run.Jog)
            {
                run.Jog = true;
                run.Obstacles.Clear();
                run.PendingHazards.Clear();
                events.Add(new Ev { Type = "jogStart" });
            }
        }

        private static void AgeBuffer(Run run, double dt)
        {
            if (run.BufferedType == null) return;
            run.BufferedT += dt;
            if (run.BufferedT > run.Tune.BufferSec) run.BufferedType = null;
        }

        private static void StepTimers(Run run, double dt, List<Ev> events)
        {
            run.InvulnT = Math.Max(0.0, run.InvulnT - dt);
            run.StumbleT = Math.Max(0.0, run.StumbleT - dt);
            run.SlowT = Math.Max(0.0, run.SlowT - dt);
            if (run.MagnetT > 0.0) { run.MagnetT -= dt; if (run.MagnetT <= 0.0) { run.MagnetT = 0.0; events.Add(new Ev { Type = "powerupEnd", Kind = "magnet" }); } }
            if (run.X2T > 0.0) { run.X2T -= dt; if (run.X2T <= 0.0) { run.X2T = 0.0; events.Add(new Ev { Type = "powerupEnd", Kind = "x2" }); } }
            if (run.TurboT > 0.0) { run.TurboT -= dt; if (run.TurboT <= 0.0) { run.TurboT = 0.0; events.Add(new Ev { Type = "powerupEnd", Kind = "turbo" }); } }
        }

        private static void StepPlayer(Run run, double dt, List<Ev> events)
        {
            var tune = run.Tune;
            if (run.JumpT >= 0.0)
            {
                bool landed;
                if (run.FastDrop)
                {
                    run.FastDropY -= tune.FastDropSpeed * dt;
                    landed = run.FastDropY <= 0.0;
                }
                else
                {
                    run.JumpT += dt;
                    landed = run.JumpT >= tune.JumpSec;
                }
                if (landed)
                {
                    run.JumpT = -1.0;
                    run.FastDrop = false;
                    events.Add(new Ev { Type = "land" });
                    FlushBuffer(run, events);
                }
            }
            if (run.SlideT >= 0.0)
            {
                run.SlideT += dt;
                if (run.SlideT >= tune.SlideSec)
                {
                    run.SlideT = -1.0;
                    FlushBuffer(run, events);
                }
            }
            if (run.LaneT < 1.0) run.LaneT = Math.Min(1.0, run.LaneT + dt / tune.LaneChangeSec);
        }

        private static void FlushBuffer(Run run, List<Ev> events)
        {
            if (run.BufferedType != null && TryAction(run, run.BufferedType, events)) run.BufferedType = null;
        }

        private static void TrackNearMiss(Run run, Obstacle ob, double px, double py, List<Ev> events)
        {
            var tune = run.Tune;
            var def = ob.Def;
            var window = def.HalfDepth + tune.PlayerHalfDepth + 0.4;
            var z = ob.Z;
            if (Math.Abs(z) <= window)
            {
                var latClear = Math.Abs(px - ob.X) - (ob.HalfW + tune.PlayerHalfW);
                var clear = double.PositiveInfinity;
                if (latClear > 0.0) clear = latClear;
                else if (def.Pass == "jump" && py > def.ClearY) clear = py - def.ClearY;
                ob.MinClear = Math.Min(ob.MinClear, clear);
            }
            else if (z > window && !ob.Passed)
            {
                ob.Passed = true;
                if (ob.MinClear > 0.0 && ob.MinClear <= tune.NearMissM)
                {
                    run.NearMisses += 1;
                    run.NearStreak += 1;
                    events.Add(new Ev { Type = "nearMiss", Id = ob.Id, Streak = run.NearStreak });
                }
            }
        }

        private static void StepObstacles(Run run, double dt, double speed, List<Ev> events)
        {
            var tune = run.Tune;
            var px = PlayerX(run);
            var py = PlayerY(run);
            var sliding = run.SlideT >= 0.0;
            var obstacles = run.Obstacles;
            var i = obstacles.Count - 1;
            while (i >= 0)
            {
                var ob = obstacles[i];
                var def = ob.Def;
                var approach = speed + def.OwnSpeed;
                var telegraph = def.TelegraphSec;
                if (!ob.Telegraphed && telegraph > 0.0 && -ob.Z / Math.Max(0.001, approach) <= telegraph)
                {
                    ob.Telegraphed = true;
                    events.Add(new Ev { Type = "telegraph", Id = ob.Id, Kind = ob.Kind });
                }
                var dz = approach * dt;
                var soft = def.Pass == "soft";
                var hit = !run.Finished && !ob.Hit && (soft || run.InvulnT <= 0.0) && SweepHits(run, ob, dz, px, py, sliding);
                ob.Z += dz;
                if (ob.Kind == "npc") ob.X += def.CrossSpeed * dt;
                if (!ob.Hit && (ob.Kind == "cart" || ob.Kind == "crate" || ob.Kind == "npc")) TrackNearMiss(run, ob, px, py, events);
                if (hit) HandleHit(run, ob, events);
                if (run.Ended || i >= obstacles.Count) break;
                if (ob.Z > tune.DespawnZ) obstacles.RemoveAt(i);
                i--;
            }
        }

        private static double Hypot3(double a, double b, double c) => Math.Sqrt(a * a + b * b + c * c);

        private static void StepCoins(Run run, double dt, double speed, List<Ev> events)
        {
            var tune = run.Tune;
            var px = PlayerX(run);
            var py = PlayerY(run);
            var coinValue = run.X2T > 0.0 ? 2 : 1;
            var magnetR = run.MagnetT > 0.0 ? tune.MagnetRadius : 0.0;
            var coins = run.CoinItems;
            for (var i = coins.Count - 1; i >= 0; i--)
            {
                var c = coins[i];
                if (!c.Attracted)
                {
                    c.Z += speed * dt;
                    var d = Hypot3(c.X - px, c.Z, c.Y - (py + 0.6));
                    if (magnetR > 0.0 && d < magnetR) c.Attracted = true;
                    else if (run.TurboT > 0.0 && Math.Abs(c.X - px) < 1.0 && c.Z > -2.5 && c.Z < 1.0) c.Attracted = true;
                }
                else PullCoin(c, px, py + 0.6, tune.MagnetPullSpeed * dt, speed * dt * 0.2);
                bool collected;
                if (c.Attracted) collected = Hypot3(c.X - px, c.Z, c.Y - (py + 0.6)) < 0.55;
                else collected = Math.Abs(c.Z) < 0.6 && Math.Abs(c.X - px) < 0.7 && Math.Abs(py + tune.CoinY - c.Y) < 0.85;
                if (collected)
                {
                    run.Coins += coinValue;
                    events.Add(new Ev { Type = "coin", X = c.X, Y = c.Y, Z = c.Z, Value = coinValue });
                    coins.RemoveAt(i);
                }
                else if (c.Z > tune.DespawnZ) coins.RemoveAt(i);
            }
        }

        private static void PullCoin(CoinItem c, double tx, double ty, double step, double drift)
        {
            var dx = tx - c.X;
            var dy = ty - c.Y;
            var dz = -c.Z;
            var d = Hypot3(dx, dy, dz);
            if (d == 0.0) d = 1.0;
            c.X += dx / d * step;
            c.Y += dy / d * step;
            c.Z += dz / d * step + drift;
        }

        private static void StepPowerups(Run run, double dt, double speed, List<Ev> events)
        {
            var tune = run.Tune;
            var px = PlayerX(run);
            var py = PlayerY(run);
            var items = run.PowerupItems;
            for (var i = items.Count - 1; i >= 0; i--)
            {
                var p = items[i];
                p.Z += speed * dt;
                if (Math.Abs(p.Z) < 0.8 && Math.Abs(p.X - px) < 0.9 && py < 1.6)
                {
                    if (p.Kind == "magnet") run.MagnetT = tune.MagnetSec;
                    else if (p.Kind == "x2") run.X2T = tune.X2Sec;
                    else if (p.Kind == "shield") run.Shield = true;
                    else if (p.Kind == "turbo") run.TurboT = tune.TurboSec;
                    run.PowerupsCollected += 1;
                    events.Add(new Ev { Type = "powerup", Kind = p.Kind });
                    items.RemoveAt(i);
                }
                else if (p.Z > tune.DespawnZ) items.RemoveAt(i);
            }
        }

        /// <summary>Advance the run by dt; returns the view events (lane/jump/slide/fastDrop · spawn · telegraph · crash ·
        /// wipeout · jogStart · shieldPop · puddle · nearMiss · coin · powerup · powerupEnd · finish).</summary>
        public static List<Ev> StepRun(Run run, double dt, Input input)
        {
            var tune = run.Tune;
            var events = new List<Ev>();
            if (run.Ended) return events;
            run.Elapsed += dt;
            if (!run.Finished) run.RampSec += dt;
            AgeBuffer(run, dt);
            ApplyInput(run, input, events);
            StepTimers(run, dt, events);
            StepPlayer(run, dt, events);

            var speed = CurrentSpeed(run);
            run.Speed = speed;
            run.DistanceM += speed * dt;

            if (run.Mode == "travel" && !run.Finished && run.DistanceM >= tune.TravelDistanceM)
            {
                run.Finished = true;
                events.Add(new Ev { Type = "finish", CoinsCollected = run.Coins, Crashes = run.Crashes });
            }

            SpawnStep(run, events);
            StepObstacles(run, dt, speed, events);
            if (run.Ended) return events;
            StepCoins(run, dt, speed, events);
            StepPowerups(run, dt, speed, events);
            return events;
        }

        /// <summary>§C8.5 score (× the turbo modifier's SCORE_MULT).</summary>
        public static int RunScore(Run run) =>
            FrameworkLogic.JsRound(ShoppingSurf.SurfScore(run.DistanceM, run.Coins, run.NearMisses) * run.Tune.ScoreMult);

        // ── bot ───────────────────────────────────────────────────────────

        private struct Threat
        {
            public bool Any;
            public double Tta;
            public string Pass;
        }

        private static Threat BlocksLaneAt(Obstacle ob, double laneX, ShoppingSurf.Tune tune, double aheadSec, double speed)
        {
            var def = ob.Def;
            if (def.Pass == "soft") return default;
            var tta = -ob.Z / Math.Max(0.001, speed + def.OwnSpeed);
            if (tta < -0.05 || tta > aheadSec) return default;
            var x = ob.X;
            if (ob.Kind == "npc") x += def.CrossSpeed * tta;
            var halfW = ob.Kind == "gap" ? 99.0 : ob.HalfW;
            if (Math.Abs(x - laneX) >= halfW + tune.PlayerHalfW + 0.05) return default;
            return new Threat { Any = true, Tta = tta, Pass = def.Pass };
        }

        private static Threat[] ThreatTable(Run run, double planSec, double speed)
        {
            var tune = run.Tune;
            var threats = new Threat[tune.Lanes];
            for (var lane = 0; lane < tune.Lanes; lane++)
            {
                var best = default(Threat);
                foreach (var ob in run.Obstacles)
                {
                    var b = BlocksLaneAt(ob, tune.LaneX[lane], tune, planSec, speed);
                    if (b.Any && (!best.Any || b.Tta < best.Tta)) best = b;
                }
                threats[lane] = best;
            }
            return threats;
        }

        private static double TtaOf(Threat t) => t.Any ? t.Tta : 99.0;

        private static Input? BotUrgent(Run run, Threat[] threats)
        {
            var tune = run.Tune;
            var myLane = run.Lane;
            var my = threats[myLane];
            var free = run.JumpT < 0.0 && run.SlideT < 0.0;
            if (!my.Any) return null;
            var tta = my.Tta;
            if (my.Pass == "jump" && tta <= tune.JumpSec * 0.5 && free) return new Input { Jump = true };
            if (my.Pass == "slide" && tta <= tune.SlideSec * 0.55 && free) return new Input { Slide = true };
            if (my.Pass != "none") return null;
            if (tta > tune.ValidatorReactSec + tune.ValidatorLaneCostSec + 0.7) return null;
            var options = new List<int>();
            foreach (var l in new[] { myLane - 1, myLane + 1 }) if (l >= 0 && l < tune.Lanes) options.Add(l);
            // web: sort ascending by tta, THEN reverse — ties come out in reversed order (right before left)
            if (options.Count == 2 && TtaOf(threats[options[1]]) < TtaOf(threats[options[0]])) options.Reverse();
            options.Reverse();
            foreach (var l in options)
            {
                var t = threats[l];
                if (!t.Any || TtaOf(t) > tta + 0.5 || t.Pass != "none") return l < myLane ? new Input { Left = true } : new Input { Right = true };
            }
            return null;
        }

        private static Input BotDrift(Run run, Threat[] threats)
        {
            var tune = run.Tune;
            var lanes = tune.Lanes;
            var value = new int[lanes];
            foreach (var c in run.CoinItems) if (c.Z > -14.0 && c.Z < -2.0 && c.Y < 0.9) value[c.Lane] += 1;
            foreach (var p in run.PowerupItems) if (p.Z > -18.0 && p.Z < -2.0) value[p.Lane] += 6;
            var actionGap = tune.LaneChangeSec + tune.ValidatorReactSec + tune.ValidatorActionLeadSec + 0.25;
            var myLane = run.Lane;
            var target = myLane;
            foreach (var l in new[] { myLane, myLane - 1, myLane + 1 })
            {
                if (l < 0 || l >= lanes) continue;
                var t = threats[l];
                var actionable = !t.Any || t.Pass == "jump" || t.Pass == "slide";
                var safe = !t.Any || t.Tta > 1.4 || (actionable && t.Tta > actionGap);
                if (safe && value[l] > value[target] + (l == myLane ? 0 : 1)) target = l;
            }
            if (target == myLane) return default;
            return target < myLane ? new Input { Left = true } : new Input { Right = true };
        }

        /// <summary>§C8.7 deterministic bot: hold a safe lane (planning about one chunk ahead), time jumps/slides, else drift to coins.</summary>
        public static Input BotInput(Run run)
        {
            var tune = run.Tune;
            if (run.Ended || run.Finished) return default;
            var speed = Math.Max(1.0, run.Speed);
            var threats = ThreatTable(run, tune.ChunkLenM / speed, speed);
            var laneChanging = run.LaneT < 1.0;
            if (!laneChanging)
            {
                var urgent = BotUrgent(run, threats);
                if (urgent.HasValue) return urgent.Value;
            }
            var my = threats[run.Lane];
            var calm = !my.Any || my.Tta > 1.6;
            if (laneChanging || run.JumpT >= 0.0 || run.SlideT >= 0.0 || !calm) return default;
            return BotDrift(run, threats);
        }

        public static (Run run, int events, int score) SimulateRun(GoobyRng rng, string mode = "arcade", double maxSec = 120.0, double dt = 1.0 / 30.0, ShoppingSurf.Tune tune = null)
        {
            var run = CreateRun(rng, mode, tune ?? ShoppingSurf.Surf);
            var events = 0;
            var n = (int)Math.Ceiling(maxSec / dt);
            for (var i = 0; i < n; i++)
            {
                events += StepRun(run, dt, BotInput(run)).Count;
                if (run.Ended || (run.Mode == "travel" && run.Finished)) break;
            }
            return (run, events, RunScore(run));
        }

        /// <summary>Mode-aware certification run: the pilot plus seeded lapses (BOT_MISS_CHANCE per 0.5-s window mutes 0.55 s).</summary>
        public static (int score, double distanceM, int coins, int crashes, double elapsed, bool ended) SimulateAutoplay(string mode = "normal", int seed = 1, double maxSec = 180.0)
        {
            var tune = ShoppingSurf.ApplyDifficulty(ShoppingSurf.Surf, mode);
            var run = CreateRun(new GoobyRng((long)seed * 2654435761L + 7), "arcade", tune);
            var lapse = new GoobyRng(seed ^ 0x5F356495);
            const double dt = 1.0 / 30.0;
            var fumbleT = 0.0;
            var nextRollT = 0.0;
            var n = (int)Math.Ceiling(maxSec / dt);
            for (var i = 0; i < n; i++)
            {
                if (run.Elapsed >= nextRollT)
                {
                    nextRollT += 0.5;
                    if (lapse.Next() < tune.BotMissChance) fumbleT = 0.55;
                }
                var input = fumbleT > 0.0 ? default : BotInput(run);
                fumbleT -= dt;
                StepRun(run, dt, input);
                if (run.Ended) break;
            }
            return (RunScore(run), run.DistanceM, run.Coins, run.Crashes, run.Elapsed, run.Ended);
        }
    }
}
