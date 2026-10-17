using System;

namespace Gooby.Core.Minigames.Gvz
{
    /// <summary>
    /// Read-only world model of the GvZ bot (port of gvz_bot_senses.gd): queries only, no placements. The pessimist
    /// view (E11 root cause): threats are never estimated gentler than on "normal".
    /// </summary>
    public static class GvzBotSenses
    {
        /// <summary>Can any shooter still hit this point? (Shots start at col·1000+600 and fly right; stars reach ±1 lane.)</summary>
        public static bool LaneCanHit(GvzRun run, int lane, int x)
        {
            foreach (var tower in run.Towers.Values)
            {
                var row = run.TowerRow(tower.Type);
                if (!GvzJson.Has(row, "projectile")) continue;
                var reach = GvzJson.S(row, "projectile", "") == "star" ? 1 : 0;
                if (Math.Abs(tower.Lane - lane) > reach) continue;
                if (tower.Col * GvzLogic.CellMm + 400 <= x) return true;
            }
            return false;
        }

        /// <summary>Is the front ground zombie of the lane a crusher? Any wall against it is wasted.</summary>
        public static bool FrontIsCrusher(GvzRun run, int lane)
        {
            var bestX = 99999;
            var crusher = false;
            foreach (var z in run.Zombies)
            {
                if (z.Dead || z.Lane != lane || z.Dir > 0 || z.Flying || z.State == "dig") continue;
                if (z.X < bestX) { bestX = z.X; crusher = z.Crusher; }
            }
            return crusher;
        }

        public static bool LaneHasStealable(GvzRun run, int lane)
        {
            foreach (var z in run.Zombies)
            {
                if (z.Dead || z.Lane != lane || z.ArmorHp <= 0) continue;
                if (Array.IndexOf(GvzZombies.MagnetStealable, z.Armor) >= 0) return true;
            }
            return false;
        }

        public static bool MagnetCovers(GvzRun run, int lane)
        {
            foreach (var t in run.Towers.Values) if (t.Type == "magnet_gooby" && Math.Abs(t.Lane - lane) <= 1) return true;
            return false;
        }

        /// <summary>Heaviest approaching ground zombie of the lane (null = none).</summary>
        public static GvzZombie HeaviestZombie(GvzRun run, int lane)
        {
            GvzZombie best = null;
            var bestHp = 0;
            foreach (var z in run.Zombies)
            {
                if (z.Dead || z.Lane != lane || z.Dir > 0 || z.Flying || z.State == "dig") continue;
                var hp = z.Hp + z.ArmorHp;
                if (hp > bestHp) { bestHp = hp; best = z; }
            }
            return best;
        }

        /// <summary>Approaching ground HP of a lane (balloons don't count — that is the gust's job).</summary>
        public static int LaneThreatHp(GvzRun run, int lane)
        {
            var total = 0;
            foreach (var z in run.Zombies)
            {
                if (z.Dead || z.Lane != lane || z.Dir > 0 || z.Flying) continue;
                total += PessimistHp(run, z.Hp) + z.ArmorHp;
            }
            return total;
        }

        /// <summary>Threats are never gentler than on normal: easy HP rebates are reverted, hard surcharges count.</summary>
        public static int PessimistHp(GvzRun run, int raw)
        {
            var pct = GvzJson.I(run.DiffRow, "zombie_hp_pct", 100);
            return pct >= 100 ? raw : raw * 100 / Math.Max(1, pct);
        }

        /// <summary>Rough kill capacity of the lane until the front zombie reaches the house (dmg/tick × ticks left).</summary>
        public static int LaneCapacity(GvzRun run, int lane, int frontX)
        {
            var ticksLeft = Math.Max(0, frontX) / Math.Max(1, LaneMaxSpeed(run, lane));
            var dmgPer100Ticks = 0;
            foreach (var tower in run.Towers.Values)
            {
                if (tower.Lane != lane) continue;
                var row = run.TowerRow(tower.Type);
                if (!GvzJson.Has(row, "projectile")) continue;
                var interval = Math.Max(1, GvzJson.I(row, "fire_interval_ticks", 28));
                var dmg = GvzJson.I(row, "damage", 20) * GvzJson.I(row, "volley", 1);
                dmgPer100Ticks += dmg * 100 / interval;
            }
            return (int)((long)dmgPer100Ticks * ticksLeft / 100);
        }

        /// <summary>Fastest approaching zombie of the lane in mm/tick (easy speed rebates reverted; rage replaces the pace).</summary>
        public static int LaneMaxSpeed(GvzRun run, int lane)
        {
            var basis = GvzJson.I(run.Combat, "zombie_speed_mm_per_tick", 10);
            var rebate = Math.Min(0, GvzJson.I(run.DiffRow, "zombie_speed_bonus_pct", 0));
            var pct = 100;
            foreach (var z in run.Zombies)
            {
                if (z.Dead || z.Lane != lane || z.Dir > 0) continue;
                var zPct = z.SpeedPct - rebate;
                if (z.Raged) zPct = GvzJson.I(run.ZombieRow(z.Type), "rage_speed_pct", zPct);
                pct = Math.Max(pct, zPct);
            }
            return basis * pct / 100;
        }

        /// <summary>Base economy before the first shooter: more at night (no sky!).</summary>
        public static int BaseEco(GvzRun run) => run.Mod("night") ? 4 : 2;

        public static bool BaseEcoReady(GvzRun run)
        {
            if (!GvzLogic.AvailableTowers(run).Contains("nutella_sammler")) return true;
            return CountType(run, "nutella_sammler") >= BaseEco(run);
        }

        /// <summary>Collector target by level (night levels live ONLY from collectors; the late campaign needs reserves).</summary>
        public static int EcoTarget(GvzRun run)
        {
            var id = GvzJson.I(run.Level, "id", 1);
            if (id <= 2) return 2;
            if (id <= 4) return 4;
            return id >= 13 ? 8 : 6;
        }

        public static bool CanBuy(GvzRun run, string type)
        {
            if (!GvzLogic.AvailableTowers(run).Contains(type)) return false;
            if (GvzLogic.CooldownLeft(run, type) > 0) return false;
            return run.Nutella >= GvzLogic.TowerCost(run, type);
        }

        public static int CountType(GvzRun run, string type)
        {
            var count = 0;
            foreach (var t in run.Towers.Values) if (t.Type == type) count++;
            return count;
        }

        public static bool LaneHasType(GvzRun run, int lane, string type)
        {
            foreach (var t in run.Towers.Values) if (t.Lane == lane && t.Type == type) return true;
            return false;
        }

        public static int LaneShooterCount(GvzRun run, int lane)
        {
            var count = 0;
            foreach (var t in run.Towers.Values)
                if (t.Lane == lane && GvzJson.Has(run.TowerRow(t.Type), "projectile")) count++;
            return count;
        }

        public static bool LaneHasFlying(GvzRun run, int lane)
        {
            foreach (var z in run.Zombies) if (!z.Dead && z.Lane == lane && z.Flying) return true;
            return false;
        }

        public static int ArmoredCount(GvzRun run)
        {
            var count = 0;
            foreach (var z in run.Zombies) if (!z.Dead && z.ArmorHp > 0) count++;
            return count;
        }

        public static int NearestZombieX(GvzRun run, int lane)
        {
            var best = 99999;
            foreach (var z in run.Zombies)
            {
                if (z.Dead || z.Lane != lane || z.Dir > 0 || z.State == "dig") continue;
                best = Math.Min(best, z.X);
            }
            return best;
        }

        public static bool LaneHasArmor(GvzRun run, int lane, string armor)
        {
            foreach (var z in run.Zombies)
            {
                if (z.Dead || z.Lane != lane) continue;
                if (z.Armor == armor && z.ArmorHp > 0) return true;
            }
            return false;
        }
    }
}
