using System;
using System.Collections.Generic;
using Gooby.Core.Random;
using Newtonsoft.Json.Linq;

namespace Gooby.Core.Minigames.Gvz
{
    /// <summary>
    /// Zombie behaviour of GvZ (port of gvz_zombies.gd): movement, eating, armour, the special cases (jumper, mole,
    /// balloon, crusher, grandpa), boss "Zombie-König Knurps" and the panic Goobys (the lawn-mower equivalent).
    /// Loops over the zombie list run by index like GDScript's for-in: a mini thrown mid-loop is visited this tick.
    /// </summary>
    public static class GvzZombies
    {
        public static readonly string[] MagnetStealable = { "eimer", "huetchen", "schild" };

        /// <summary>Spawn a zombie (the boss summon and the crusher's mini use this too).</summary>
        public static GvzZombie Spawn(GvzRun run, string type, int lane, int x)
        {
            var row = run.ZombieRow(type);
            var diff = run.DiffRow;
            var speedPct = GvzJson.I(row, "speed_pct", 100) + GvzJson.I(diff, "zombie_speed_bonus_pct", 0);
            if (run.Mod("rain")) speedPct += 10;
            var hp = GvzJson.I(row, "hp", 200) * GvzJson.I(diff, "zombie_hp_pct", 100) / 100;
            var zombie = new GvzZombie
            {
                Id = GvzLogic.NextId(run),
                Type = type,
                Lane = lane,
                X = x,
                Hp = hp,
                MaxHp = hp,
                ArmorHp = GvzJson.I(row, "armor_hp", 0),
                Armor = GvzJson.S(row, "armor", ""),
                SpeedPct = speedPct,
                State = GvzJson.B(row, "digger", false) ? "dig" : "walk",
                Dir = -1,
                Jumps = GvzJson.I(row, "jumps", 0),
                Flying = GvzJson.B(row, "flying", false),
                Crusher = GvzJson.B(row, "crusher", false),
            };
            run.Zombies.Add(zombie);
            GvzLogic.Push(run, new GvzEvent { Kind = "spawn", Id = zombie.Id, Type = type, Lane = lane });
            return zombie;
        }

        /// <summary>Movement + eating + special behaviour of every zombie for one tick.</summary>
        public static void Step(GvzRun run)
        {
            var laneSlow = PustLanes(run);
            for (var i = 0; i < run.Zombies.Count; i++)
            {
                var zombie = run.Zombies[i];
                if (zombie.Dead) continue;
                StepOne(run, zombie, laneSlow);
            }
            FlushDead(run);
        }

        /// <summary>
        /// Apply damage (kind: carrot|frost|star|melon|blast|mower). The shield blocks straight shots completely; star
        /// and melon bypass ONLY the shield; head armour always absorbs first.
        /// </summary>
        public static void Damage(GvzRun run, GvzZombie zombie, int amount, string kind)
        {
            if (zombie.Dead || amount <= 0) return;
            var armor = zombie.Armor;
            var toHp = amount;
            if (zombie.ArmorHp > 0)
            {
                var bypassesShield = armor == "schild" && (kind == "star" || kind == "melon" || kind == "blast");
                if (kind == "mower") zombie.ArmorHp = 0;
                else if (!bypassesShield)
                {
                    var absorbed = Math.Min(zombie.ArmorHp, amount);
                    zombie.ArmorHp -= absorbed;
                    toHp = amount - absorbed;
                    if (armor == "schild") toHp = 0;
                    if (zombie.ArmorHp <= 0) OnArmorBroken(run, zombie);
                }
            }
            if (toHp <= 0)
            {
                GvzLogic.Push(run, new GvzEvent { Kind = "hit", Id = zombie.Id, HitKind = kind, Armor = true, Lane = zombie.Lane, X = zombie.X });
                return;
            }
            zombie.Hp -= toHp;
            GvzLogic.Push(run, new GvzEvent { Kind = "hit", Id = zombie.Id, HitKind = kind, Armor = false, Lane = zombie.Lane, X = zombie.X });
            MaybeThrowMini(run, zombie);
            if (zombie.Hp <= 0) Kill(run, zombie, kind);
        }

        /// <summary>Frost hit: slow (Eis-Gooby).</summary>
        public static void ApplySlow(GvzRun run, GvzZombie zombie, int slowTicks) => zombie.SlowUntil = run.Tick + slowTicks;

        /// <summary>Pop a balloon (Pust-Gooby) → a normal ground zombie.</summary>
        public static void PopBalloon(GvzRun run, GvzZombie zombie)
        {
            if (!zombie.Flying) return;
            zombie.Flying = false;
            GvzLogic.Push(run, new GvzEvent { Kind = "pop", Id = zombie.Id, Lane = zombie.Lane, X = zombie.X });
        }

        /// <summary>Magnet-Gooby: steal the armour of the nearest armoured zombie in range.</summary>
        public static bool MagnetSteal(GvzRun run, GvzTower tower, int rangeCols)
        {
            var centerX = tower.Col * GvzLogic.CellMm + 500;
            GvzZombie best = null;
            var bestDist = rangeCols * GvzLogic.CellMm + 1;
            foreach (var zombie in run.Zombies)
            {
                if (zombie.Dead || zombie.ArmorHp <= 0) continue;
                if (Array.IndexOf(MagnetStealable, zombie.Armor) < 0) continue;
                if (Math.Abs(zombie.Lane - tower.Lane) > 1) continue;
                var dist = Math.Abs(zombie.X - centerX);
                if (dist < bestDist) { bestDist = dist; best = zombie; }
            }
            if (best == null) return false;
            best.ArmorHp = 0;
            OnArmorBroken(run, best);
            GvzLogic.Push(run, new GvzEvent { Kind = "magnet", Id = best.Id, Lane = best.Lane, X = best.X });
            return true;
        }

        /// <summary>The boss rolls in (L15).</summary>
        public static void BossEnter(GvzRun run, JObject def)
        {
            var type = GvzJson.S(def, "type", "boss_knurps");
            var row = run.ZombieRow(type);
            var hp = GvzJson.I(row, "hp", 9000) * GvzJson.I(run.DiffRow, "zombie_hp_pct", 100) / 100;
            run.Boss = new GvzBoss
            {
                Type = type,
                Hp = hp,
                MaxHp = hp,
                Phase = 1,
                Lane = run.Lanes[run.Lanes.Count / 2],
                X = 8600,
                NextMove = run.Tick + GvzJson.I(row, "lane_move_interval_ticks", 90),
                NextToss = run.Tick + GvzJson.I(row, "toss_interval_ticks", 200),
                NextSummon = run.Tick + GvzJson.I(row, "summon_interval_ticks", 340),
            };
            GvzLogic.Push(run, new GvzEvent { Kind = "boss_enter", Lane = run.Boss.Lane });
        }

        /// <summary>Boss behaviour: lane changes, bin toss, summons, phases.</summary>
        public static void StepBoss(GvzRun run)
        {
            var boss = run.Boss;
            if (boss == null || boss.Hp <= 0 || run.Outcome != "") return;
            var row = run.ZombieRow(boss.Type);
            var now = run.Tick;
            BossPhaseCheck(run, boss, row);
            var rng = run.Rng;
            var lanes = run.Lanes;
            if (now >= boss.NextMove)
            {
                boss.NextMove = now + BossInterval(row, "lane_move_interval_ticks", boss);
                boss.Lane = lanes[(int)(rng.NextU32() % (uint)lanes.Count)];
                GvzLogic.Push(run, new GvzEvent { Kind = "boss_move", Lane = boss.Lane });
            }
            if (now >= boss.NextToss)
            {
                boss.NextToss = now + BossInterval(row, "toss_interval_ticks", boss);
                BossToss(run, rng);
            }
            if (now >= boss.NextSummon)
            {
                boss.NextSummon = now + BossInterval(row, "summon_interval_ticks", boss);
                BossSummon(run, row, rng, lanes);
            }
        }

        /// <summary>Boss damage (projectiles hit the bin lorry in its lane).</summary>
        public static void DamageBoss(GvzRun run, int amount)
        {
            var boss = run.Boss;
            if (boss == null || boss.Hp <= 0) return;
            boss.Hp = Math.Max(0, boss.Hp - amount);
            GvzLogic.Push(run, new GvzEvent { Kind = "boss_hit", Hp = boss.Hp, MaxHp = boss.MaxHp, Lane = boss.Lane });
        }

        /// <summary>
        /// Panic Goobys: triggered by a break-in, their ride, the loss check. Balloons float OVER the roller (Doc G §4.3)
        /// — they trigger nothing and mean an immediate loss at the house.
        /// </summary>
        public static void StepMowers(GvzRun run)
        {
            if (run.Outcome != "") return;
            var speed = GvzJson.I(run.Combat, "mower_speed_mm_per_tick", 160);
            for (var i = 0; i < run.Zombies.Count; i++)
            {
                var zombie = run.Zombies[i];
                if (zombie.Dead || zombie.X >= 0 || zombie.Dir > 0) continue;
                if (zombie.Flying)
                {
                    if (zombie.X < GvzLogic.HouseX)
                    {
                        run.Outcome = "lost";
                        GvzLogic.Push(run, new GvzEvent { Kind = "lost", Lane = zombie.Lane });
                        return;
                    }
                    continue;
                }
                var mower = run.Mowers.Get(zombie.Lane);
                if (mower == null) continue;
                if (!mower.Used)
                {
                    mower.Used = true;
                    mower.Active = true;
                    mower.X = -400;
                    GvzLogic.Push(run, new GvzEvent { Kind = "mower", Lane = zombie.Lane });
                }
                else if (!mower.Active && zombie.X < GvzLogic.HouseX)
                {
                    run.Outcome = "lost";
                    GvzLogic.Push(run, new GvzEvent { Kind = "lost", Lane = zombie.Lane });
                    return;
                }
            }
            foreach (var lane in run.Mowers.KeysSnapshot())
            {
                var mower = run.Mowers[lane];
                if (!mower.Active) continue;
                mower.X += speed;
                for (var i = 0; i < run.Zombies.Count; i++)
                {
                    var zombie = run.Zombies[i];
                    if (zombie.Dead || zombie.Lane != lane || zombie.Flying) continue;
                    if (zombie.X <= mower.X) Damage(run, zombie, 99999, "mower");
                }
                if (mower.X > GvzLogic.SpawnX) mower.Active = false;
            }
            FlushDead(run);
        }

        private static void StepOne(GvzRun run, GvzZombie zombie, Dictionary<int, int> laneSlow)
        {
            var move = MoveMm(run, zombie, laneSlow);
            if (zombie.State == "dig")
            {
                zombie.X -= move;
                if (zombie.X <= SurfaceX(run, zombie))
                {
                    zombie.State = "walk";
                    zombie.Dir = 1;
                    GvzLogic.Push(run, new GvzEvent { Kind = "surface", Id = zombie.Id, Lane = zombie.Lane, X = zombie.X });
                }
                return;
            }
            var target = TowerAhead(run, zombie);
            if (target == null)
            {
                zombie.State = "walk";
                zombie.X += zombie.Dir * move;
                if (zombie.Dir > 0 && zombie.X > GvzLogic.SpawnX)
                {
                    zombie.Dead = true;
                    GvzLogic.Push(run, new GvzEvent { Kind = "escape", Id = zombie.Id, Lane = zombie.Lane });
                }
                return;
            }
            if (SpecialContact(run, zombie, target)) return;
            zombie.State = "eat";
            var bite = GvzJson.I(run.Combat, "bite_damage_per_tick", 5);
            target.Hp -= bite;
            if (target.Hp <= 0) GvzLogic.DestroyTower(run, GvzLogic.CellKey(target.Lane, target.Col), "eaten");
        }

        /// <summary>Contact cases: crusher squashes, trampoline launches, jumper hops (once), Knolle explodes. true = no bite.</summary>
        private static bool SpecialContact(GvzRun run, GvzZombie zombie, GvzTower tower)
        {
            var key = GvzLogic.CellKey(tower.Lane, tower.Col);
            if (tower.Type == "schnarch_knolle" && run.Tick >= (tower.ArmedAt ?? 0))
            {
                var dmg = GvzJson.I(run.TowerRow("schnarch_knolle"), "damage", 1800);
                GvzLogic.Push(run, new GvzEvent { Kind = "knolle", Lane = tower.Lane, Col = tower.Col });
                GvzLogic.DestroyTower(run, key, "knolle");
                // the biter's body still stands in the NEIGHBOUR cell — it takes the hit directly, the cell blast the rest
                GvzCombat.Blast(run, tower.Lane, tower.Col, 0, dmg, zombie.Id);
                Damage(run, zombie, dmg, "blast");
                return true;
            }
            if (zombie.Crusher)
            {
                GvzLogic.Push(run, new GvzEvent { Kind = "crush", Lane = tower.Lane, Col = tower.Col });
                GvzLogic.DestroyTower(run, key, "crushed");
                return true;
            }
            if (tower.Type == "trampolin_gooby" && (tower.Charges ?? 0) > 0)
            {
                tower.Charges = (tower.Charges ?? 0) - 1;
                zombie.X = 8600;
                zombie.Jumps = 0;
                GvzLogic.Push(run, new GvzEvent { Kind = "bounce", Id = zombie.Id, Lane = zombie.Lane, Col = tower.Col });
                if (tower.Charges <= 0) GvzLogic.DestroyTower(run, key, "spent");
                return true;
            }
            if (zombie.Jumps > 0 && zombie.Dir < 0)
            {
                zombie.Jumps -= 1;
                zombie.X = tower.Col * GvzLogic.CellMm - 250;
                GvzLogic.Push(run, new GvzEvent { Kind = "jump", Id = zombie.Id, Lane = zombie.Lane, X = zombie.X });
                return true;
            }
            return false;
        }

        /// <summary>Effective movement in mm/tick (frost 50 % + gust aura, floor slow_floor_pct; balloons ignore frost).</summary>
        private static int MoveMm(GvzRun run, GvzZombie zombie, Dictionary<int, int> laneSlow)
        {
            var combat = run.Combat;
            var pct = 100;
            if (zombie.SlowUntil > run.Tick && !zombie.Flying) pct -= 50;
            if (laneSlow.TryGetValue(zombie.Lane, out var slow)) pct -= slow;
            pct = Math.Max(GvzJson.I(combat, "slow_floor_pct", 20), pct);
            var speedPct = zombie.SpeedPct;
            if (zombie.Raged) speedPct = GvzJson.I(run.ZombieRow(zombie.Type), "rage_speed_pct", speedPct);
            var basis = GvzJson.I(combat, "zombie_speed_mm_per_tick", 10);
            return Math.Max(1, basis * speedPct * pct / 10000);
        }

        /// <summary>Tower in the cell the zombie bites into. Flyers and diggers ignore towers.</summary>
        private static GvzTower TowerAhead(GvzRun run, GvzZombie zombie)
        {
            if (zombie.Flying || zombie.State == "dig") return null;
            var x = zombie.X;
            if (x < 0 || x >= GvzLogic.Cols * GvzLogic.CellMm) return null;
            var biteX = zombie.Dir < 0 ? x - 250 : x + 250;
            var col = GvzLogic.ColOf(Math.Min(Math.Max(biteX, 0), GvzLogic.Cols * GvzLogic.CellMm - 1));
            return run.Towers.Get(GvzLogic.CellKey(zombie.Lane, col));
        }

        /// <summary>A mole surfaces behind the rearmost tower of its lane (at least column 0).</summary>
        private static int SurfaceX(GvzRun run, GvzZombie zombie)
        {
            var minCol = GvzLogic.Cols;
            foreach (var tower in run.Towers.Values)
                if (tower.Lane == zombie.Lane) minCol = Math.Min(minCol, tower.Col);
            if (minCol >= GvzLogic.Cols) return 400;
            return Math.Max(400, minCol * GvzLogic.CellMm - 600);
        }

        /// <summary>Lanes with a Pust-Gooby → the slow percent of its aura.</summary>
        private static Dictionary<int, int> PustLanes(GvzRun run)
        {
            var output = new Dictionary<int, int>();
            var slow = GvzJson.I(run.TowerRow("pust_gooby"), "lane_slow_pct", 20);
            foreach (var tower in run.Towers.Values)
                if (tower.Type == "pust_gooby") output[tower.Lane] = slow;
            return output;
        }

        private static void OnArmorBroken(GvzRun run, GvzZombie zombie)
        {
            var armor = zombie.Armor;
            if (armor == "") return;
            zombie.Armor = "";
            GvzLogic.Push(run, new GvzEvent { Kind = "armor_gone", Id = zombie.Id, Which = armor, Lane = zombie.Lane, X = zombie.X });
            if (zombie.Type == "zeitungsopa" && !zombie.Raged)
            {
                zombie.Raged = true;
                GvzLogic.Push(run, new GvzEvent { Kind = "rage", Id = zombie.Id, Lane = zombie.Lane, X = zombie.X });
            }
        }

        /// <summary>The crusher throws a mini-schlurfi forward once at 50 % HP.</summary>
        private static void MaybeThrowMini(GvzRun run, GvzZombie zombie)
        {
            var row = run.ZombieRow(zombie.Type);
            if (!GvzJson.Has(row, "throw_mini_at_pct") || zombie.ThrewMini || zombie.Dead) return;
            var threshold = zombie.MaxHp * GvzJson.I(row, "throw_mini_at_pct", 0) / 100;
            if (zombie.Hp > threshold || zombie.Hp <= 0) return;
            zombie.ThrewMini = true;
            var miniX = Math.Max(500, zombie.X - 3000);
            Spawn(run, GvzJson.S(row, "mini_type", "schlurfi"), zombie.Lane, miniX);
            GvzLogic.Push(run, new GvzEvent { Kind = "mini_throw", Lane = zombie.Lane, X = miniX });
        }

        private static void Kill(GvzRun run, GvzZombie zombie, string kind)
        {
            zombie.Dead = true;
            run.Kills += 1;
            run.Score += GvzJson.I(run.Balance?["score"] as JObject, "kill", 2);
            GvzLogic.Push(run, new GvzEvent { Kind = "die", Id = zombie.Id, Type = zombie.Type, Lane = zombie.Lane, By = kind, X = zombie.X });
        }

        private static void FlushDead(GvzRun run)
        {
            for (var i = run.Zombies.Count - 1; i >= 0; i--)
                if (run.Zombies[i].Dead) run.Zombies.RemoveAt(i);
        }

        private static void BossPhaseCheck(GvzRun run, GvzBoss boss, JObject row)
        {
            var phases = GvzJson.I(row, "phases", 3);
            var third = boss.MaxHp / phases;
            var phase = phases - Math.Max(0, boss.Hp - 1) / third;
            phase = Math.Min(Math.Max(phase, 1), phases);
            if (phase <= boss.Phase) return;
            boss.Phase = phase;
            GvzLogic.Push(run, new GvzEvent { Kind = "boss_phase", Phase = phase });
        }

        /// <summary>Intervals speed up per phase (a smoking bin lorry).</summary>
        private static int BossInterval(JObject row, string key, GvzBoss boss)
        {
            var basis = GvzJson.I(row, key, 200);
            switch (boss.Phase)
            {
                case 2: return basis * GvzJson.I(row, "phase2_speedup_pct", 75) / 100;
                case 3: return basis * GvzJson.I(row, "phase3_speedup_pct", 55) / 100;
                default: return basis;
            }
        }

        private static void BossToss(GvzRun run, GoobyRng rng)
        {
            var keys = run.Towers.KeysSnapshot();
            if (keys.Length == 0) return;
            var key = keys[(int)(rng.NextU32() % (uint)keys.Length)];
            var tower = run.Towers[key];
            GvzLogic.Push(run, new GvzEvent { Kind = "boss_toss", Lane = tower.Lane, Col = tower.Col });
            GvzLogic.DestroyTower(run, key, "muelltonne");
        }

        private static void BossSummon(GvzRun run, JObject row, GoobyRng rng, List<int> lanes)
        {
            var lo = GvzJson.I(row, "summon_count_min", 2);
            var hi = GvzJson.I(row, "summon_count_max", 4);
            var count = lo + (int)(rng.NextU32() % (uint)(hi - lo + 1));
            var pool = row["summon_types"] is JArray ? GvzJson.Strings(row["summon_types"]) : new List<string> { "schlurfi" };
            GvzLogic.Push(run, new GvzEvent { Kind = "boss_summon", Count = count });
            for (var i = 0; i < count; i++)
            {
                var type = pool[(int)(rng.NextU32() % (uint)pool.Count)];
                var lane = lanes[(int)(rng.NextU32() % (uint)lanes.Count)];
                Spawn(run, type, lane, GvzLogic.SpawnX);
            }
        }
    }
}
