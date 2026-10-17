using System;
using System.Collections.Generic;
using System.Text;
using Gooby.Core.Random;
using Newtonsoft.Json.Linq;

namespace Gooby.Core.Minigames.Gvz
{
    /// <summary>
    /// Goobys vs Zombies (Doc G §4) — port of gvz_logic.gd: a pure, deterministic 20-Hz simulation on INT arithmetic
    /// only (positions in milli-cells, times in ticks), randomness only from <see cref="GoobyRng"/>. Tower behaviour
    /// lives in <see cref="GvzCombat"/>, zombie behaviour in <see cref="GvzZombies"/> (same run, same rules).
    /// Lanes 0..4 from the top, columns 0..8; x in milli-cells (cell c covers [c·1000, (c+1)·1000)); zombies walk
    /// right → left (9000 → 0), the house is at x &lt; 0.
    /// </summary>
    public static class GvzLogic
    {
        public const int CellMm = 1000;
        public const int Cols = 9;
        public const int SpawnX = 9200;
        public const int HouseX = -500;

        /// <summary>Fresh run. goldi = the code-gated Goldi tower (Doc G §4.2).</summary>
        public static GvzRun NewRun(JObject level, JObject balance, string difficulty = "normal", long seed = 1, bool goldi = false)
        {
            var diffs = balance?["difficulty"] as JObject ?? new JObject();
            var diff = diffs[difficulty] != null ? difficulty : "normal";
            var diffRow = diffs[diff] as JObject ?? new JObject();
            var economy = balance?["economy"] as JObject ?? new JObject();
            var lanes = level["lanes"] is JArray ? GvzJson.Ints(level["lanes"]) : new List<int> { 0, 1, 2, 3, 4 };
            var run = new GvzRun
            {
                Rng = new GoobyRng(seed),
                Seed = seed,
                Level = level,
                Balance = balance,
                Diff = diff,
                Lanes = lanes,
                Mods = level["mods"] as JObject ?? new JObject(),
                Nutella = (level["start_nutella"] != null ? GvzJson.I(level, "start_nutella", 0) : GvzJson.I(economy, "start_nutella", 50))
                          + GvzJson.I(diffRow, "start_nutella_bonus", 0),
                Goldi = goldi,
            };
            foreach (var lane in lanes) run.Mowers[lane] = new GvzMower();
            run.Conveyor = InitConveyor(level);
            run.SpawnPlan = PlanSpawns(level);
            run.WavePlan = PlanWaves(level);
            run.NextDropTick = FirstDropTick(run.Mods, economy);
            return run;
        }

        /// <summary>One 20-Hz tick. Mutates the run and returns the tick's events.</summary>
        public static List<GvzEvent> Tick(GvzRun run)
        {
            run.Events = new List<GvzEvent>();
            if (run.Outcome != "") return run.Events;
            run.Tick += 1;
            EconomyTick(run);
            ConveyorTick(run);
            SpawnerTick(run);
            GvzCombat.ActTowers(run);
            GvzCombat.StepProjectiles(run);
            GvzZombies.Step(run);
            GvzZombies.StepBoss(run);
            GvzZombies.StepMowers(run);
            CheckOutcome(run);
            return run.Events;
        }

        public readonly struct Check
        {
            public readonly bool Ok;
            public readonly string Reason;
            public Check(bool ok, string reason) { Ok = ok; Reason = reason; }
        }

        /// <summary>
        /// Placement check without side effects. Reasons: locked|code_gate|lane|col|cell_occupied|cooldown|nutella|
        /// conveyor_missing|outcome|unknown_tower.
        /// </summary>
        public static Check CanPlace(GvzRun run, string type, int lane, int col)
        {
            if (run.Outcome != "") return new Check(false, "outcome");
            var towers = run.Balance?["towers"] as JObject;
            if (towers == null || towers[type] == null) return new Check(false, "unknown_tower");
            if (!AvailableTowers(run).Contains(type))
                return new Check(false, GvzJson.B(towers[type] as JObject, "code_gate", false) ? "code_gate" : "locked");
            if (!run.Lanes.Contains(lane)) return new Check(false, "lane");
            if (col < 0 || col >= Cols) return new Check(false, "col");
            if (run.Towers.ContainsKey(CellKey(lane, col))) return new Check(false, "cell_occupied");
            if (run.Conveyor != null)
            {
                // conveyor items are free and cooldown-free — in the hybrid mode too (else the belt clogs once nutella runs short)
                if (run.Conveyor.Queue.Contains(type)) return new Check(true, "");
                if (ConveyorOnly(run)) return new Check(false, "conveyor_missing");
            }
            if (CooldownLeft(run, type) > 0) return new Check(false, "cooldown");
            if (run.Nutella < TowerCost(run, type)) return new Check(false, "nutella");
            return new Check(true, "");
        }

        /// <summary>Place a tower (cost + cooldown, or a conveyor item).</summary>
        public static Check PlaceTower(GvzRun run, string type, int lane, int col)
        {
            var check = CanPlace(run, type, lane, col);
            if (!check.Ok) return check;
            var queue = run.Conveyor?.Queue;
            if (queue != null && queue.Contains(type)) queue.Remove(type);
            else if (ConveyorOnly(run)) return new Check(false, "conveyor_missing");
            else
            {
                var row = run.TowerRow(type);
                run.Nutella -= TowerCost(run, type);
                run.Cooldowns[type] = run.Tick + GvzJson.I(row, "cooldown_ticks", 0);
            }
            var tower = MakeTower(run, type, lane, col);
            run.Towers[CellKey(lane, col)] = tower;
            if (type == "eis_gooby") BumpStat(run, "eis_placed");
            else if (type == "dicker_bert") BumpStat(run, "bert_placed");
            Push(run, new GvzEvent { Kind = "place", Type = type, Lane = lane, Col = col });
            return new Check(true, "");
        }

        /// <summary>Remove a tower (the shovel; no refund).</summary>
        public static bool RemoveTower(GvzRun run, int lane, int col)
        {
            var key = CellKey(lane, col);
            if (!run.Towers.ContainsKey(key)) return false;
            DestroyTower(run, key, "removed");
            return true;
        }

        /// <summary>Destroy a tower (eaten / crushed / bin) + event.</summary>
        public static void DestroyTower(GvzRun run, int key, string cause)
        {
            if (!run.Towers.TryGetValue(key, out var tower)) return;
            run.Towers.Remove(key);
            Push(run, new GvzEvent { Kind = "tower_gone", Type = tower.Type, Lane = tower.Lane, Col = tower.Col, Cause = cause });
        }

        /// <summary>Collect a nutella blob → amount (or -1 when gone / unknown).</summary>
        public static int CollectDrop(GvzRun run, int dropId)
        {
            for (var i = 0; i < run.Drops.Count; i++)
            {
                var drop = run.Drops[i];
                if (drop.Id != dropId) continue;
                var maxN = GvzJson.I(run.Economy, "max_nutella", 9975);
                run.Nutella = Math.Min(maxN, run.Nutella + drop.Amount);
                BumpStat(run, "drops_collected");
                Push(run, new GvzEvent { Kind = "collect", Lane = drop.Lane, Col = drop.Col, Amount = drop.Amount });
                run.Drops.RemoveAt(i);
                return drop.Amount;
            }
            return -1;
        }

        /// <summary>Towers unlocked by this level (Goldi only with the code flag).</summary>
        public static List<string> AvailableTowers(GvzRun run)
        {
            var list = GvzJson.Strings(run.Level["unlock_towers"]);
            if (run.Goldi && run.Balance?["towers"]?["goldi"] != null) list.Add("goldi");
            return list;
        }

        public static int TowerCost(GvzRun run, string type) => GvzJson.I(run.TowerRow(type), "cost", 0);

        /// <summary>Card cooldown left in ticks (0 = ready).</summary>
        public static int CooldownLeft(GvzRun run, string type) => Math.Max(0, (run.Cooldowns.TryGetValue(type, out var ready) ? ready : 0) - run.Tick);

        public static bool IsOver(GvzRun run) => run.Outcome != "";

        public static int CellKey(int lane, int col) => lane * Cols + col;

        /// <summary>Column of an x position (clamped to 0..Cols-1).</summary>
        public static int ColOf(int x) => Math.Min(Math.Max(x / CellMm, 0), Cols - 1);

        /// <summary>Deterministic state hash (rng/level/balance/events excluded — their effect lives in the fields).</summary>
        public static string StateHash(GvzRun run)
        {
            var sb = new StringBuilder(1024);
            sb.Append(run.Tick).Append('|').Append(run.Nutella).Append('|');
            foreach (var key in run.Towers.Keys)
            {
                var t = run.Towers[key];
                sb.Append(key).Append(':').Append(t.Id).Append(t.Type).Append(',').Append(t.Hp).Append(',').Append(t.NextAct)
                  .Append(',').Append(t.Charges ?? -1).Append(',').Append(t.ArmedAt ?? -1).Append(',').Append(t.FuseAt ?? -1).Append(';');
            }
            sb.Append('|');
            foreach (var z in run.Zombies)
                sb.Append(z.Id).Append(z.Type).Append(',').Append(z.Lane).Append(',').Append(z.X).Append(',').Append(z.Hp).Append(',').Append(z.ArmorHp)
                  .Append(z.Armor).Append(',').Append(z.State).Append(z.Dir).Append(z.Jumps).Append(z.Flying ? 'f' : 'g').Append(z.Raged ? 'r' : 'c')
                  .Append(z.ThrewMini ? 't' : 'n').Append(z.SlowUntil).Append(z.Dead ? 'd' : 'a').Append(';');
            sb.Append('|');
            foreach (var p in run.Projectiles) sb.Append(p.Id).Append(p.Kind).Append(p.Lane).Append(',').Append(p.X).Append(';');
            sb.Append('|');
            foreach (var d in run.Drops) sb.Append(d.Id).Append(',').Append(d.Lane).Append(',').Append(d.Col).Append(',').Append(d.Amount).Append(',').Append(d.Expires).Append(';');
            sb.Append('|');
            foreach (var lane in run.Mowers.Keys) { var m = run.Mowers[lane]; sb.Append(lane).Append(m.Used ? 'u' : 'f').Append(m.Active ? 'a' : 'i').Append(m.X).Append(';'); }
            sb.Append('|');
            var cds = new List<string>(run.Cooldowns.Keys);
            cds.Sort(StringComparer.Ordinal);
            foreach (var k in cds) sb.Append(k).Append(run.Cooldowns[k]).Append(';');
            sb.Append('|');
            if (run.Conveyor != null) sb.Append(string.Join(",", run.Conveyor.Queue)).Append('@').Append(run.Conveyor.NextTick);
            sb.Append('|');
            if (run.Boss != null) sb.Append(run.Boss.Hp).Append(',').Append(run.Boss.Phase).Append(',').Append(run.Boss.Lane).Append(',').Append(run.Boss.NextMove).Append(',').Append(run.Boss.NextToss).Append(',').Append(run.Boss.NextSummon);
            sb.Append('|').Append(run.SpawnIdx).Append(',').Append(run.WaveIdx).Append(',').Append(run.Kills).Append(',').Append(run.Score).Append(',').Append(run.Outcome);
            ulong h = 14695981039346656037UL;
            foreach (var ch in sb.ToString()) { h ^= ch; h *= 1099511628211UL; }
            return h.ToString("x16");
        }

        /// <summary>Bump a run statistic (also from GvzCombat).</summary>
        public static void BumpStat(GvzRun run, string key, int amount = 1) =>
            run.Stats[key] = (run.Stats.TryGetValue(key, out var v) ? v : 0) + amount;

        public static void Push(GvzRun run, GvzEvent ev) => run.Events.Add(ev);

        /// <summary>Fresh id for zombies / projectiles / drops / towers.</summary>
        public static int NextId(GvzRun run) => run.NextIdValue++;

        private static GvzTower MakeTower(GvzRun run, string type, int lane, int col)
        {
            var row = run.TowerRow(type);
            // difficulty reaches the towers too (E11 monotony): on easy they take more bites
            var hp = GvzJson.I(row, "hp", 300) * GvzJson.I(run.DiffRow, "tower_hp_pct", 100) / 100;
            var now = run.Tick;
            var interval = GvzJson.Has(row, "produce_interval_ticks") ? GvzJson.I(row, "produce_interval_ticks", 0) : GvzJson.I(row, "fire_interval_ticks", 0);
            var tower = new GvzTower { Id = NextId(run), Type = type, Lane = lane, Col = col, Hp = hp, MaxHp = hp, NextAct = now + interval };
            if (GvzJson.Has(row, "charges")) tower.Charges = GvzJson.I(row, "charges", 0);
            if (GvzJson.Has(row, "arm_ticks")) tower.ArmedAt = now + GvzJson.I(row, "arm_ticks", 0);
            if (GvzJson.Has(row, "fuse_ticks")) tower.FuseAt = now + GvzJson.I(row, "fuse_ticks", 0);
            if (GvzJson.Has(row, "steal_interval_ticks")) tower.NextAct = now + GvzJson.I(row, "steal_interval_ticks", 0);
            if (GvzJson.Has(row, "gust_interval_ticks")) tower.NextAct = now + GvzJson.I(row, "gust_interval_ticks", 0);
            return tower;
        }

        /// <summary>Sky nutella + blob decay (night: nothing from the sky, Doc G §4.4 L6).</summary>
        private static void EconomyTick(GvzRun run)
        {
            var economy = run.Economy;
            var now = run.Tick;
            if (run.NextDropTick > 0 && now >= run.NextDropTick)
            {
                run.NextDropTick = now + GvzJson.I(economy, "sky_drop_interval_ticks", 200);
                var lane = run.Lanes[(int)(run.Rng.NextU32() % (uint)run.Lanes.Count)];
                var col = (int)(run.Rng.NextU32() % Cols);
                SpawnDrop(run, lane, col, GvzJson.I(economy, "sky_drop_amount", 25), "sky");
            }
            for (var i = run.Drops.Count - 1; i >= 0; i--)
            {
                if (now < run.Drops[i].Expires) continue;
                Push(run, new GvzEvent { Kind = "drop_gone", Lane = run.Drops[i].Lane, Col = run.Drops[i].Col });
                run.Drops.RemoveAt(i);
            }
        }

        /// <summary>Create a blob (source: sky | produce).</summary>
        public static void SpawnDrop(GvzRun run, int lane, int col, int amount, string source)
        {
            var economy = run.Economy;
            var lifetime = GvzJson.I(economy, "sky_drop_lifetime_ticks", 160);
            if (run.Mod("rain")) lifetime = GvzJson.I(economy, "rain_drop_lifetime_ticks", 90);
            if (source == "produce") lifetime *= 2;
            run.Drops.Add(new GvzDrop { Id = NextId(run), Lane = lane, Col = col, Amount = amount, Expires = run.Tick + lifetime });
            Push(run, new GvzEvent { Kind = "drop", Lane = lane, Col = col, Amount = amount, Source = source });
        }

        private static void ConveyorTick(GvzRun run)
        {
            var conveyor = run.Conveyor;
            if (conveyor == null) return;
            if (run.Tick < conveyor.NextTick || conveyor.Queue.Count >= conveyor.MaxQueue) return;
            conveyor.NextTick = run.Tick + conveyor.Interval;
            var type = conveyor.Pool[(int)(run.Rng.NextU32() % (uint)conveyor.Pool.Count)];
            conveyor.Queue.Add(type);
            Push(run, new GvzEvent { Kind = "conveyor", Type = type });
        }

        /// <summary>Wave banners + scripted spawns from the level plan + the boss entrance.</summary>
        private static void SpawnerTick(GvzRun run)
        {
            var now = run.Tick;
            while (run.WaveIdx < run.WavePlan.Count)
            {
                var wave = run.WavePlan[run.WaveIdx];
                if (now < wave.Tick) break;
                run.WaveIdx += 1;
                Push(run, new GvzEvent { Kind = "wave", N = run.WaveIdx, Huge = wave.Huge });
            }
            while (run.SpawnIdx < run.SpawnPlan.Count)
            {
                var entry = run.SpawnPlan[run.SpawnIdx];
                if (now < entry.Tick) break;
                run.SpawnIdx += 1;
                var lane = entry.Lane;
                if (lane < 0) lane = run.Lanes[(int)(run.Rng.NextU32() % (uint)run.Lanes.Count)];
                GvzZombies.Spawn(run, entry.Type, lane, SpawnX);
            }
            if (run.Level["boss"] is JObject bossDef && run.Boss == null)
                if (now >= GvzJson.I(bossDef, "enter_at", 0) * Tps(run)) GvzZombies.BossEnter(run, bossDef);
        }

        private static void CheckOutcome(GvzRun run)
        {
            if (run.Outcome != "") return;
            var boss = run.Boss;
            if (boss != null && boss.Hp <= 0)
            {
                run.Outcome = "won";
                Push(run, new GvzEvent { Kind = "won", Boss = true });
                return;
            }
            var bossPending = run.Level["boss"] is JObject && boss == null;
            var spawnsDone = run.SpawnIdx >= run.SpawnPlan.Count;
            if (spawnsDone && run.Zombies.Count == 0 && boss == null && !bossPending)
            {
                run.Outcome = "won";
                Push(run, new GvzEvent { Kind = "won", Boss = false });
            }
        }

        private static GvzConveyor InitConveyor(JObject level)
        {
            if (!(level["conveyor"] is JObject def)) return null;
            return new GvzConveyor
            {
                Pool = GvzJson.Strings(def["pool"]),
                Interval = GvzJson.I(def, "interval_ticks", 130),
                MaxQueue = GvzJson.I(def, "max_queue", 5),
                NextTick = GvzJson.I(def, "start_delay_ticks", 20),
            };
        }

        /// <summary>Pure conveyor level (L9) — the hybrid (L15) also allows nutella.</summary>
        public static bool ConveyorOnly(GvzRun run) => run.Mod("conveyor") && !run.Mod("conveyor_hybrid");

        private static int FirstDropTick(JObject mods, JObject economy) =>
            GvzJson.B(mods, "night", false) ? -1 : GvzJson.I(economy, "sky_drop_first_ticks", 120);

        private static List<GvzSpawn> PlanSpawns(JObject level)
        {
            var plan = new List<GvzSpawn>();
            foreach (var t in level["spawns"] as JArray ?? new JArray())
            {
                var spawn = t as JObject ?? new JObject();
                plan.Add(new GvzSpawn
                {
                    Tick = (int)Math.Floor(GvzJson.D(spawn, "t", 0.0) * 20.0),
                    Lane = GvzJson.I(spawn, "lane", -1),
                    Type = GvzJson.S(spawn, "type", "schlurfi"),
                });
            }
            return plan;
        }

        private static List<GvzWave> PlanWaves(JObject level)
        {
            var plan = new List<GvzWave>();
            foreach (var t in level["waves"] as JArray ?? new JArray())
            {
                var wave = t as JObject ?? new JObject();
                plan.Add(new GvzWave { Tick = (int)Math.Floor(GvzJson.D(wave, "t", 0.0) * 20.0), Huge = GvzJson.B(wave, "huge", false) });
            }
            return plan;
        }

        public static int Tps(GvzRun run) => GvzJson.I(run.Balance, "ticks_per_second", 20);
    }
}
