using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using S = Gooby.Core.Minigames.Gvz.GvzBotSenses;

namespace Gooby.Core.Minigames.Gvz
{
    /// <summary>
    /// Deterministic auto-player of GvZ (port of gvz_bot.gd): collects every blob at once and places at most one tower
    /// per tick by heuristic. Purpose: the certification that every level is playable (Doc G §4.7), difficulty tests
    /// and the QA autoplay. Lane sorts are tiny (≤ 5) — Godot's insertion sort is stable there, like LINQ's OrderBy.
    /// </summary>
    public static class GvzBot
    {
        private static readonly string[] ShooterPref = { "doppelmoehre", "moehrenschuetze" };
        private static readonly string[] ShooterPrefWide = { "moehrenschuetze", "doppelmoehre" };
        private static readonly int[] ShooterCols = { 1, 2, 3 };
        private static readonly int[] EconomyCols = { 0, 1 };
        private static readonly string[] ExpendableTypes = { "nutella_sammler", "magnet_gooby", "trampolin_gooby", "goldi" };
        public const int MaxTicks = 14400;

        public sealed class Result
        {
            public bool Won;
            public string Outcome;
            public int Ticks, Kills, Score, MowersUsed, Stars;
        }

        /// <summary>A whole level run.</summary>
        public static Result Simulate(JObject level, JObject balance, long seed, string difficulty = "normal", bool goldi = false)
        {
            var run = GvzLogic.NewRun(level, balance, difficulty, seed, goldi);
            while (!GvzLogic.IsOver(run) && run.Tick < MaxTicks)
            {
                GvzLogic.Tick(run);
                Act(run);
            }
            return ResultOf(run);
        }

        public static Result ResultOf(GvzRun run)
        {
            var used = MowersUsed(run);
            return new Result
            {
                Won = run.Outcome == "won",
                Outcome = run.Outcome,
                Ticks = run.Tick,
                Kills = run.Kills,
                Score = run.Score,
                MowersUsed = used,
                Stars = run.Outcome == "won" ? GvzProgress.StarsFor(used) : 0,
            };
        }

        public static int MowersUsed(GvzRun run)
        {
            var used = 0;
            foreach (var m in run.Mowers.Values) if (m.Used) used++;
            return used;
        }

        /// <summary>One bot move: collect everything + at most ONE placement (priority ladder; saves for emergencies).</summary>
        public static void Act(GvzRun run)
        {
            CollectAll(run);
            if (GvzLogic.IsOver(run)) return;
            if (run.Conveyor != null)
            {
                if (ActConveyor(run)) return;
                if (GvzLogic.ConveyorOnly(run)) return;
            }
            // air before ground: a balloon that slips through is an INSTANT loss without the panic-Gooby backstop
            if (RespondToFlying(run)) return;
            if (PreparePlannedBalloons(run)) return;
            if (RespondToDeficit(run)) return;
            if (PlaceCollector(run, S.BaseEco(run))) return;
            var lane = ThreatLaneWanting(run, 1);
            if (lane >= 0)
            {
                if (!TryShooter(run, lane)) KnolleStopper(run, lane);
                return;
            }
            if (RespondToBoss(run)) return;
            if (PlaceMagnet(run)) return;
            if (GvzJson.I(run.Level, "id", 1) >= 10 && run.Tick >= 1800 && PlaceKnolleFarm(run, 50)) return;
            if (PlaceCollector(run, 4)) return;
            if (ShowcaseNewTower(run)) return;
            if (S.BaseEcoReady(run))
            {
                lane = IdleLaneWanting(run);
                if (lane >= 0 && run.Nutella >= 150)
                {
                    TryShooter(run, lane);
                    return;
                }
            }
            lane = ThreatLaneWanting(run, 2);
            if (lane >= 0)
            {
                TryShooter(run, lane, true);
                return;
            }
            if (PlaceCollector(run, S.EcoTarget(run))) return;
            if (PlaceKnolleFarm(run)) return;
            if (PlaceLobber(run)) return;
            if (PlaceUtility(run)) return;
            if (run.Nutella >= 200)
            {
                lane = IdleLaneWanting(run);
                if (lane >= 0)
                {
                    TryShooter(run, lane);
                    return;
                }
            }
            if (run.Nutella >= 300)
            {
                lane = ThreatLaneWanting(run, 3);
                if (lane >= 0) TryShooter(run, lane, true);
            }
        }

        private static void CollectAll(GvzRun run)
        {
            foreach (var drop in run.Drops.ToArray()) GvzLogic.CollectDrop(run, drop.Id);
        }

        private static bool Place(GvzRun run, string type, (int lane, int col) cell) => GvzLogic.PlaceTower(run, type, cell.lane, cell.col).Ok;

        // ── conveyor ──

        private static bool ActConveyor(GvzRun run)
        {
            var queue = run.Conveyor.Queue;
            if (queue.Count == 0) return false;
            var type = queue[0];
            var cell = ConveyorCellFor(run, type);
            return cell.HasValue && Place(run, type, cell.Value);
        }

        private static (int lane, int col)? ConveyorCellFor(GvzRun run, string type)
        {
            switch (type)
            {
                case "dicker_bert":
                    return FreeCellInCols(run, NeediestLane(run), 5, 6, 4);
                case "boom_beere":
                {
                    // need before greed: a deep balloon or an unreachable roller is an instant-loss threat, the boss a race
                    var air = BalloonBoomCell(run);
                    if (air.HasValue) return air;
                    var ground = GroundEmergencyCell(run);
                    if (ground.HasValue) return ground;
                    var bossCell = BossBoomCell(run);
                    if (bossCell.HasValue) return BossBoomTimingOk(run) ? bossCell : null;
                    var cluster = BestCluster(run, 2);
                    if (cluster.HasValue) return cluster;
                    if (run.Conveyor.Queue.Count < run.Conveyor.MaxQueue) return null;
                    return FreeCellInCols(run, NeediestLane(run), 4, 3, 5);
                }
                case "schnarch_knolle":
                    return FreeCellInCols(run, NeediestLane(run), 4, 5, 3);
                case "pust_gooby":
                {
                    var airLane = PustTargetLane(run);
                    if (airLane < 0) airLane = NeediestLane(run);
                    return FreeCellInCols(run, airLane, 1, 2, 0, 3, 4, 5);
                }
                default:
                {
                    var lane = ThreatLaneWanting(run, 3);
                    if (lane < 0) lane = IdleLaneWanting(run);
                    if (lane < 0) lane = NeediestLane(run);
                    return FreeCellInCols(run, lane, 1, 2, 3, 0, 4);
                }
            }
        }

        // ── deficits ──

        /// <summary>Emergency answer per lane (approaching HP &gt; kill capacity): Knolle → Boom → Dicker Bert. true = saving allowed.</summary>
        private static bool RespondToDeficit(GvzRun run)
        {
            if (RespondToMoles(run)) return true;
            var saving = false;
            var lanes = run.Lanes.OrderBy(l => LaneUrgency(run, l)).ToList();
            foreach (var lane in lanes)
            {
                var threat = S.LaneThreatHp(run, lane);
                if (threat <= 0) continue;
                var front = S.NearestZombieX(run, lane);
                if (front < 3500 && !S.LaneCanHit(run, lane, front))
                {
                    switch (EmergencyUnblock(run, lane, front))
                    {
                        case "placed": return true;
                        case "save": saving = true; break;
                    }
                    continue;
                }
                if (threat < 550 && S.LaneShooterCount(run, lane) == 0 && front > 5500) continue;
                if ((long)S.LaneCapacity(run, lane, front) * 100 >= (long)threat * 115) continue;
                switch (DeficitAction(run, lane, front))
                {
                    case "placed": return true;
                    case "save": saving = true; break;
                    default:
                        if (BreakStalemate(run, lane, front)) return true;
                        break;
                }
            }
            return saving;
        }

        /// <summary>Triage key: depth of the deepest zombie, +6000 with an unused panic Gooby, +1000 per shooter.</summary>
        private static int LaneUrgency(GvzRun run, int lane)
        {
            var front = S.NearestZombieX(run, lane);
            if (front >= 99999) return front;
            var mower = run.Mowers.Get(lane);
            if (mower != null && !mower.Used) front += 6000;
            return front + S.LaneShooterCount(run, lane) * 1000;
        }

        /// <summary>Stalemate breaker: no firepower and no free cell that could still hit → shovel an expendable cell.</summary>
        private static bool BreakStalemate(GvzRun run, int lane, int front)
        {
            if (S.LaneShooterCount(run, lane) > 0) return false;
            var maxX = GvzLogic.Cols * GvzLogic.CellMm - 1;
            var maxCol = Math.Min(3, GvzLogic.ColOf(Math.Min(Math.Max(front - 400, 0), maxX)));
            var cols = new List<int>();
            for (var col = maxCol; col >= 0; col--) cols.Add(col);
            if (FreeCellInCols(run, lane, cols.ToArray()).HasValue) return false;
            var type = AffordableShooter(run);
            if (type == "") return true;
            var cell = ShovelExpendable(run, lane, maxCol);
            return cell.HasValue && Place(run, type, cell.Value);
        }

        private static (int lane, int col)? ShovelExpendable(GvzRun run, int lane, int maxCol)
        {
            var bestCol = -1;
            var bestCost = 99999;
            foreach (var tower in run.Towers.Values)
            {
                if (tower.Lane != lane || tower.Col > maxCol) continue;
                if (Array.IndexOf(ExpendableTypes, tower.Type) < 0) continue;
                var cost = run.Balance?["towers"]?[tower.Type] != null ? GvzJson.I(run.TowerRow(tower.Type), "cost", 99999) : 99999;
                if (cost < bestCost) { bestCost = cost; bestCol = tower.Col; }
            }
            if (bestCol < 0) return null;
            GvzLogic.RemoveTower(run, lane, bestCol);
            return (lane, bestCol);
        }

        // ── boss ──

        private static bool RespondToBoss(GvzRun run)
        {
            if (!BossBoomCell(run).HasValue) return false;
            if (!GvzLogic.AvailableTowers(run).Contains("boom_beere")) return false;
            if (GvzLogic.CooldownLeft(run, "boom_beere") > 0) return false;
            if (!S.CanBuy(run, "boom_beere")) return run.Nutella >= 100;
            if (!BossBoomTimingOk(run)) return true;
            var cell = BossBoomCell(run);
            GvzLogic.PlaceTower(run, "boom_beere", cell.Value.lane, cell.Value.col);
            return true;
        }

        /// <summary>Lay the berry right after the boss changed lanes (else it fizzles).</summary>
        private static bool BossBoomTimingOk(GvzRun run)
        {
            var fuse = GvzJson.I(run.TowerRow("boom_beere"), "fuse_ticks", 24);
            return (run.Boss?.NextMove ?? 0) - run.Tick > fuse + 2;
        }

        private static (int lane, int col)? BossBoomCell(GvzRun run)
        {
            var boss = run.Boss;
            if (boss == null || boss.Hp <= 0) return null;
            var col = GvzLogic.ColOf(Math.Min(Math.Max(boss.X, 0), GvzLogic.Cols * GvzLogic.CellMm - 1));
            foreach (var c in new[] { col - 1, col })
            {
                if (c < 0 || c >= GvzLogic.Cols) continue;
                if (run.Towers.ContainsKey(GvzLogic.CellKey(boss.Lane, c))) continue;
                if (CellHasBiter(run, boss.Lane, c)) continue;
                return (boss.Lane, c);
            }
            return null;
        }

        private static bool CellHasBiter(GvzRun run, int lane, int col)
        {
            var lo = col * GvzLogic.CellMm - 300;
            var hi = (col + 1) * GvzLogic.CellMm + 300;
            foreach (var z in run.Zombies)
            {
                if (z.Dead || z.Lane != lane || z.Flying || z.State == "dig") continue;
                if (z.X >= lo && z.X <= hi) return true;
            }
            return false;
        }

        // ── moles ──

        private static bool RespondToMoles(GvzRun run)
        {
            foreach (var z in run.Zombies)
            {
                if (z.Dead) continue;
                if (z.State == "dig")
                {
                    if (MoleKnolle(run, z.Lane) != "none") return true;
                }
                else if (z.Dir > 0 && z.X < 8200)
                {
                    if (MoleBoom(run, z) != "none") return true;
                }
            }
            return false;
        }

        private static string MoleKnolle(GvzRun run, int lane)
        {
            if (!GvzLogic.AvailableTowers(run).Contains("schnarch_knolle")) return "none";
            foreach (var t in run.Towers.Values)
                if (t.Lane == lane && t.Type == "schnarch_knolle" && t.Col <= 3) return "none";
            var cell = FreeCellInCols(run, lane, 0, 1, 2, 3);
            if (!cell.HasValue || GvzLogic.CooldownLeft(run, "schnarch_knolle") > 0) return "none";
            if (S.CanBuy(run, "schnarch_knolle"))
            {
                Place(run, "schnarch_knolle", cell.Value);
                return "placed";
            }
            return "save";
        }

        private static string MoleBoom(GvzRun run, GvzZombie zombie)
        {
            var lane = zombie.Lane;
            var maxX = GvzLogic.Cols * GvzLogic.CellMm - 1;
            var col = GvzLogic.ColOf(Math.Min(Math.Max(zombie.X, 0), maxX));
            foreach (var t in run.Towers.Values)
                if (t.Lane == lane && t.Type == "schnarch_knolle" && t.Col >= col) return "none";
            if (!GvzLogic.AvailableTowers(run).Contains("boom_beere")) return "none";
            var cell = FreeCellInCols(run, lane, col + 1, col, col - 1);
            if (!cell.HasValue || GvzLogic.CooldownLeft(run, "boom_beere") > 0) return "none";
            if (S.CanBuy(run, "boom_beere"))
            {
                Place(run, "boom_beere", cell.Value);
                return "placed";
            }
            return "save";
        }

        /// <summary>Lane blockade: boom on the unreachable front zombie ("placed"/"save") — or "none": let the panic Gooby handle it.</summary>
        private static string EmergencyUnblock(GvzRun run, int lane, int front)
        {
            var mower = run.Mowers.Get(lane);
            if (mower != null && !mower.Used) return "none";
            if (!GvzLogic.AvailableTowers(run).Contains("boom_beere")) return "none";
            var col = GvzLogic.ColOf(front);
            var cell = FreeCellInCols(run, lane, col, col + 1, col - 1);
            if (!cell.HasValue) return "none";
            if (GvzLogic.CooldownLeft(run, "boom_beere") > 0) return "save";
            if (S.CanBuy(run, "boom_beere"))
            {
                Place(run, "boom_beere", cell.Value);
                return "placed";
            }
            return "save";
        }

        // ── balloons ──

        /// <summary>Planned balloons: build the gust BEFORE the scripted spawn (a human knows the plan after one try too).</summary>
        private static bool PreparePlannedBalloons(GvzRun run)
        {
            if (!GvzLogic.AvailableTowers(run).Contains("pust_gooby")) return false;
            var now = run.Tick;
            for (var i = run.SpawnIdx; i < run.SpawnPlan.Count; i++)
            {
                var spawn = run.SpawnPlan[i];
                var eta = spawn.Tick - now;
                if (eta > 900) return false;
                if (spawn.Type != "ballon") continue;
                var target = spawn.Lane;
                if (target < 0 || S.LaneHasType(run, target, "pust_gooby")) continue;
                if (TryPust(run, target)) return true;
                return eta <= 400;
            }
            return false;
        }

        /// <summary>Showcase the level's NEW tower at least once (saving for it in quiet moments of minutes 1–3).</summary>
        private static bool ShowcaseNewTower(GvzRun run)
        {
            foreach (var t in GvzJson.Strings(run.Level["new_towers"]))
            {
                if (S.CountType(run, t) > 0) continue;
                if (!GvzLogic.AvailableTowers(run).Contains(t)) continue;
                if (GvzLogic.CooldownLeft(run, t) > 0) continue;
                var row = run.TowerRow(t);
                var cols = GvzJson.Has(row, "projectile") ? ShooterCols : new[] { 4, 3, 5, 2 };
                if (GvzJson.Has(row, "produce_amount")) cols = EconomyCols;
                var cell = FreeCellInCols(run, NeediestLane(run), cols);
                if (!cell.HasValue) continue;
                var reserve = IdleLaneWanting(run) >= 0 ? 150 : 25;
                if (run.Nutella >= GvzLogic.TowerCost(run, t) + reserve) return Place(run, t, cell.Value);
                if (run.Diff == "hard") return false;
                return run.Tick >= 1200 && run.Tick < 3600;
            }
            return false;
        }

        /// <summary>Balloons stop at no panic Gooby — a gust is MANDATORY (saved for hard), deepest balloon first.</summary>
        private static bool RespondToFlying(GvzRun run)
        {
            foreach (var (lane, x) in FlyingLanesByDepth(run))
            {
                if (S.LaneHasType(run, lane, "pust_gooby")) continue;
                if (TryPust(run, lane)) return true;
                if (x < 3500 && BoomBalloon(run, lane, x)) return true;
                if (GvzLogic.AvailableTowers(run).Contains("pust_gooby")) return true;
            }
            return false;
        }

        private static bool TryPust(GvzRun run, int lane)
        {
            if (!GvzLogic.AvailableTowers(run).Contains("pust_gooby")) return false;
            if (GvzLogic.CooldownLeft(run, "pust_gooby") > 0) return false;
            if (!S.CanBuy(run, "pust_gooby")) return false;
            var cell = FreeCellInCols(run, lane, 2, 1, 3, 0, 4, 5, 6, 7, 8);
            if (!cell.HasValue) cell = ShovelForPust(run, lane);
            if (!cell.HasValue) return false;
            Place(run, "pust_gooby", cell.Value);
            return true;
        }

        private static (int lane, int col)? ShovelForPust(GvzRun run, int lane)
        {
            var bestKey = -1;
            var bestCost = 99999;
            var bestCol = -1;
            foreach (var key in run.Towers.Keys)
            {
                var tower = run.Towers[key];
                if (tower.Lane != lane) continue;
                var type = tower.Type;
                if (type == "schnarch_knolle" || type == "nutella_sammler" || type == "pust_gooby") continue;
                var cost = run.Balance?["towers"]?[type] != null ? GvzJson.I(run.TowerRow(type), "cost", 99999) : 99999;
                if (cost < bestCost) { bestCost = cost; bestKey = key; bestCol = tower.Col; }
            }
            if (bestKey < 0) return null;
            GvzLogic.RemoveTower(run, lane, bestCol);
            return (lane, bestCol);
        }

        private static (int lane, int col)? BalloonBoomCell(GvzRun run)
        {
            foreach (var (lane, x) in FlyingLanesByDepth(run))
            {
                if (S.LaneHasType(run, lane, "pust_gooby")) continue;
                var col = GvzLogic.ColOf(Math.Min(Math.Max(x - 260, 0), GvzLogic.Cols * GvzLogic.CellMm - 1));
                var cell = FreeCellInCols(run, lane, col, col - 1, col + 1);
                if (cell.HasValue) return cell;
            }
            return null;
        }

        private static (int lane, int col)? GroundEmergencyCell(GvzRun run)
        {
            foreach (var lane in run.Lanes)
            {
                var mower = run.Mowers.Get(lane);
                if (mower == null || !mower.Used) continue;
                var front = S.NearestZombieX(run, lane);
                if (front >= 3500 || S.LaneCanHit(run, lane, front)) continue;
                var col = GvzLogic.ColOf(Math.Max(0, front - 260));
                var cell = FreeCellInCols(run, lane, col, col - 1, col + 1);
                if (cell.HasValue) return cell;
            }
            return null;
        }

        private static int PustTargetLane(GvzRun run)
        {
            foreach (var (lane, _) in FlyingLanesByDepth(run))
                if (!S.LaneHasType(run, lane, "pust_gooby")) return lane;
            for (var i = run.SpawnIdx; i < run.SpawnPlan.Count; i++)
            {
                var spawn = run.SpawnPlan[i];
                if (spawn.Type != "ballon") continue;
                if (spawn.Lane >= 0 && !S.LaneHasType(run, spawn.Lane, "pust_gooby")) return spawn.Lane;
            }
            return -1;
        }

        /// <summary>Lanes with balloons, deepest first (smallest x).</summary>
        private static List<(int lane, int x)> FlyingLanesByDepth(GvzRun run)
        {
            var deepest = new OrderedMap<int, int>();
            foreach (var z in run.Zombies)
            {
                if (z.Dead || !z.Flying) continue;
                if (!deepest.ContainsKey(z.Lane) || z.X < deepest[z.Lane]) deepest[z.Lane] = z.X;
            }
            var output = new List<(int lane, int x)>();
            foreach (var lane in deepest.Keys) output.Add((lane, deepest[lane]));
            return output.OrderBy(e => e.x).ToList();
        }

        private static bool BoomBalloon(GvzRun run, int lane, int x)
        {
            if (!GvzLogic.AvailableTowers(run).Contains("boom_beere")) return false;
            if (GvzLogic.CooldownLeft(run, "boom_beere") > 0) return false;
            var col = GvzLogic.ColOf(Math.Max(0, x - 260));
            var cell = FreeCellInCols(run, lane, col, col - 1, col + 1);
            if (!cell.HasValue) return false;
            if (!S.CanBuy(run, "boom_beere")) return true;
            Place(run, "boom_beere", cell.Value);
            return true;
        }

        /// <summary>Emergency ladder for one lane: "placed", "save" (applicable but not affordable yet) or "none".</summary>
        private static string DeficitAction(GvzRun run, int lane, int front)
        {
            var hv = S.HeaviestZombie(run, lane);
            var hvHp = hv != null ? S.PessimistHp(run, hv.Hp) + hv.ArmorHp : 0;
            var heavy = hvHp >= 550;
            var hx = hv != null ? hv.X : 99999;
            if (heavy && hx >= 3800 && !KnolleInWindow(run, lane, hx))
            {
                var col = Math.Min(Math.Max(GvzLogic.ColOf(Math.Min(hx, 8999)) - 4, 0), GvzLogic.Cols - 1);
                var cell = FreeCellInCols(run, lane, col, col + 1, col - 1);
                if (cell.HasValue && GvzLogic.AvailableTowers(run).Contains("schnarch_knolle") && GvzLogic.CooldownLeft(run, "schnarch_knolle") == 0)
                {
                    if (S.CanBuy(run, "schnarch_knolle"))
                    {
                        Place(run, "schnarch_knolle", cell.Value);
                        return "placed";
                    }
                    return "save";
                }
            }
            var cluster = LaneFrontCluster(run, lane, 2);
            var boomCell = cluster ?? BoomCellForHeavy(run, lane, hx, heavy);
            if (boomCell.HasValue && GvzLogic.AvailableTowers(run).Contains("boom_beere") && GvzLogic.CooldownLeft(run, "boom_beere") == 0)
            {
                if (S.CanBuy(run, "boom_beere"))
                {
                    Place(run, "boom_beere", boomCell.Value);
                    return "placed";
                }
                return "save";
            }
            if (front < 4500 && !S.LaneHasType(run, lane, "dicker_bert"))
            {
                if (S.FrontIsCrusher(run, lane)) return "none";
                var wallCol = Math.Max(2, GvzLogic.ColOf(front) - 1);
                var cell = FreeCellInCols(run, lane, wallCol, wallCol - 1, wallCol + 1);
                if (cell.HasValue && GvzLogic.AvailableTowers(run).Contains("dicker_bert") && GvzLogic.CooldownLeft(run, "dicker_bert") == 0)
                {
                    if (S.CanBuy(run, "dicker_bert"))
                    {
                        Place(run, "dicker_bert", cell.Value);
                        return "placed";
                    }
                    return "save";
                }
            }
            var towers = GvzLogic.AvailableTowers(run);
            if (!towers.Contains("schnarch_knolle") && !towers.Contains("boom_beere"))
            {
                if (TryShooter(run, lane, true)) return "placed";
                if (FreeCellInCols(run, lane, ShooterCols).HasValue) return "save";
            }
            return "none";
        }

        private static bool KnolleInWindow(GvzRun run, int lane, int front)
        {
            var lo = GvzLogic.ColOf(front) - 4;
            var hi = GvzLogic.ColOf(front);
            foreach (var t in run.Towers.Values)
                if (t.Lane == lane && t.Type == "schnarch_knolle" && t.Col >= lo && t.Col <= hi) return true;
            return false;
        }

        private static bool PlaceMagnet(GvzRun run)
        {
            if (!GvzLogic.AvailableTowers(run).Contains("magnet_gooby")) return false;
            if (S.CountType(run, "magnet_gooby") >= 2) return false;
            foreach (var lane in run.Lanes)
            {
                if (!S.LaneHasStealable(run, lane)) continue;
                if (S.MagnetCovers(run, lane)) continue;
                if (!S.CanBuy(run, "magnet_gooby")) return false;
                var cell = FreeCellInCols(run, lane, 3, 4, 2);
                if (!cell.HasValue) continue;
                return Place(run, "magnet_gooby", cell.Value);
            }
            return false;
        }

        private static (int lane, int col)? BoomCellForHeavy(GvzRun run, int lane, int front, bool heavy)
        {
            if (!heavy || front >= 6000) return null;
            var col = GvzLogic.ColOf(front);
            return FreeCellInCols(run, lane, col, col - 1, col + 1);
        }

        private static (int lane, int col)? LaneFrontCluster(GvzRun run, int lane, int minCount)
        {
            var front = S.NearestZombieX(run, lane);
            if (front >= 99999) return null;
            var col = GvzLogic.ColOf(front);
            var count = 0;
            foreach (var z in run.Zombies)
            {
                if (z.Dead || z.Lane != lane || z.State == "dig") continue;
                if (Math.Abs(GvzLogic.ColOf(z.X) - col) <= 1) count++;
            }
            if (count < minCount) return null;
            return FreeCellInCols(run, lane, col, col + 1, col - 1);
        }

        /// <summary>Economy: collectors at the back, safest lane first.</summary>
        private static bool PlaceCollector(GvzRun run, int target)
        {
            if (!GvzLogic.AvailableTowers(run).Contains("nutella_sammler")) return false;
            if (S.CountType(run, "nutella_sammler") >= target) return false;
            if (!S.CanBuy(run, "nutella_sammler")) return false;
            var lanes = run.Lanes.OrderByDescending(l => S.NearestZombieX(run, l)).ToList();
            foreach (var lane in lanes)
            {
                if (S.NearestZombieX(run, lane) < 3500) continue;
                var cell = FreeCellInCols(run, lane, EconomyCols);
                if (!cell.HasValue) continue;
                return Place(run, "nutella_sammler", cell.Value);
            }
            return false;
        }

        /// <summary>Knolle farm: cheap sleeping mines in the middle (columns 5/6, armed before the first bite).</summary>
        private static bool PlaceKnolleFarm(GvzRun run, int minNutella = 75)
        {
            if (run.Nutella < minNutella || !S.CanBuy(run, "schnarch_knolle")) return false;
            foreach (var lane in run.Lanes)
            {
                if (S.LaneHasType(run, lane, "schnarch_knolle")) continue;
                var cell = FreeCellInCols(run, lane, 5, 6);
                if (!cell.HasValue) continue;
                return Place(run, "schnarch_knolle", cell.Value);
            }
            return false;
        }

        /// <summary>Anti-shield: bouncer lanes need a lobber (melon) or stars.</summary>
        private static bool PlaceLobber(GvzRun run)
        {
            foreach (var lane in run.Lanes)
            {
                if (!S.LaneHasArmor(run, lane, "schild")) continue;
                if (S.LaneHasType(run, lane, "melonen_meier") || S.LaneHasType(run, lane, "sternchen_gooby")) continue;
                foreach (var type in new[] { "melonen_meier", "sternchen_gooby" })
                {
                    if (!S.CanBuy(run, type)) continue;
                    var cell = FreeCellInCols(run, lane, ShooterCols);
                    if (!cell.HasValue) break;
                    if (Place(run, type, cell.Value)) return true;
                }
            }
            return false;
        }

        /// <summary>Shooter into a lane — false = not affordable (yet) → SAVE. A deep zombie gets a shooter BEHIND it.</summary>
        private static bool TryShooter(GvzRun run, int lane, bool dense = false)
        {
            var type = AffordableShooter(run, dense);
            if (type == "") return false;
            var cols = ShooterCols;
            var front = S.NearestZombieX(run, lane);
            if (front < 4500)
            {
                var safe = Math.Min(Math.Max(GvzLogic.ColOf(front) - 2, 0), 2);
                cols = new[] { safe, Math.Max(0, safe - 1), Math.Min(2, safe + 1), 3 };
            }
            var cell = FreeCellInCols(run, lane, cols);
            return cell.HasValue && Place(run, type, cell.Value);
        }

        /// <summary>Cheap trickle stopper (25): a Knolle with arming reserve in the approach while the first shooter is saved for.</summary>
        private static bool KnolleStopper(GvzRun run, int lane)
        {
            if (!GvzLogic.AvailableTowers(run).Contains("schnarch_knolle")) return false;
            if (GvzLogic.CooldownLeft(run, "schnarch_knolle") > 0) return false;
            if (!S.CanBuy(run, "schnarch_knolle")) return false;
            if (S.LaneHasType(run, lane, "schnarch_knolle")) return false;
            var front = S.NearestZombieX(run, lane);
            if (front >= 99999) return false;
            var arm = GvzJson.I(run.TowerRow("schnarch_knolle"), "arm_ticks", 280);
            var col = (front - 1250 - (arm + 60) * S.LaneMaxSpeed(run, lane)) / GvzLogic.CellMm;
            if (col < 0) return false;
            col = Math.Min(col, GvzLogic.Cols - 2);
            var cell = FreeCellInCols(run, lane, col, col - 1, col - 2);
            if (!cell.HasValue || cell.Value.col > col) return false;
            return Place(run, "schnarch_knolle", cell.Value);
        }

        /// <summary>Utility: gust against balloons, magnet against armour, frost as support.</summary>
        private static bool PlaceUtility(GvzRun run)
        {
            foreach (var lane in run.Lanes)
            {
                if (!S.LaneHasFlying(run, lane)) continue;
                if (S.LaneHasType(run, lane, "pust_gooby")) continue;
                if (!S.CanBuy(run, "pust_gooby")) break;
                var cell = FreeCellInCols(run, lane, 2, 1, 3);
                if (!cell.HasValue) continue;
                if (Place(run, "pust_gooby", cell.Value)) return true;
            }
            if (S.ArmoredCount(run) >= 2 && S.CountType(run, "magnet_gooby") < 1 && S.CanBuy(run, "magnet_gooby"))
            {
                var mid = run.Lanes[run.Lanes.Count / 2];
                var cell = FreeCellInCols(run, mid, 3, 2, 4);
                if (cell.HasValue && Place(run, "magnet_gooby", cell.Value)) return true;
            }
            if (S.CanBuy(run, "eis_gooby") && run.Nutella >= 300)
            {
                var lane = LaneWantingType(run, "eis_gooby");
                if (lane >= 0)
                {
                    var cell = FreeCellInCols(run, lane, ShooterCols);
                    if (cell.HasValue) return Place(run, "eis_gooby", cell.Value);
                }
            }
            return false;
        }

        /// <summary>Best affordable shooter ("" = none): dense or with 300 in the bank the double carrot, else the cheap one.</summary>
        private static string AffordableShooter(GvzRun run, bool dense = false)
        {
            var pref = dense || run.Nutella >= 300 ? ShooterPref : ShooterPrefWide;
            foreach (var type in pref) if (S.CanBuy(run, type)) return type;
            return "";
        }

        /// <summary>THREATENED lane (a living zombie on the way) below the shooter density — nearest zombie first. -1 = none.</summary>
        private static int ThreatLaneWanting(GvzRun run, int perLane)
        {
            var bestLane = -1;
            var bestX = 99999;
            foreach (var lane in run.Lanes)
            {
                if (S.LaneShooterCount(run, lane) >= perLane) continue;
                if (!FreeCellInCols(run, lane, ShooterCols).HasValue) continue;
                var x = S.NearestZombieX(run, lane);
                if (x < bestX) { bestX = x; bestLane = lane; }
            }
            return bestX < 99999 ? bestLane : -1;
        }

        private static int IdleLaneWanting(GvzRun run)
        {
            foreach (var lane in run.Lanes)
            {
                if (S.LaneShooterCount(run, lane) > 0) continue;
                if (FreeCellInCols(run, lane, ShooterCols).HasValue) return lane;
            }
            return -1;
        }

        private static int LaneWantingType(GvzRun run, string type)
        {
            foreach (var lane in run.Lanes)
                if (!S.LaneHasType(run, lane, type) && FreeCellInCols(run, lane, ShooterCols).HasValue) return lane;
            return -1;
        }

        private static int NeediestLane(GvzRun run)
        {
            var bestLane = run.Lanes[0];
            var bestX = 99999;
            foreach (var lane in run.Lanes)
            {
                var x = S.NearestZombieX(run, lane);
                if (x < bestX) { bestX = x; bestLane = lane; }
            }
            return bestLane;
        }

        private static (int lane, int col)? FreeCellInCols(GvzRun run, int lane, params int[] cols)
        {
            foreach (var col in cols)
            {
                var c = Math.Min(Math.Max(col, 0), GvzLogic.Cols - 1);
                if (!run.Towers.ContainsKey(GvzLogic.CellKey(lane, c))) return (lane, c);
            }
            return null;
        }

        /// <summary>Best 3×3 window with at least minCount zombies (free centre).</summary>
        private static (int lane, int col)? BestCluster(GvzRun run, int minCount)
        {
            (int lane, int col)? best = null;
            var bestCount = minCount - 1;
            foreach (var lane in run.Lanes)
                for (var col = 0; col < GvzLogic.Cols; col++)
                {
                    if (run.Towers.ContainsKey(GvzLogic.CellKey(lane, col))) continue;
                    var count = 0;
                    foreach (var z in run.Zombies)
                    {
                        if (z.Dead || z.State == "dig") continue;
                        if (Math.Abs(z.Lane - lane) > 1) continue;
                        if (Math.Abs(GvzLogic.ColOf(z.X) - col) > 1) continue;
                        count++;
                    }
                    if (count > bestCount) { bestCount = count; best = (lane, col); }
                }
            return best;
        }
    }
}
