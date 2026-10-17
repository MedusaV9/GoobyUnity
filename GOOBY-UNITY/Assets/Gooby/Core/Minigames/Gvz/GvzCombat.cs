using System;

namespace Gooby.Core.Minigames.Gvz
{
    /// <summary>
    /// Tower actions + projectile / blast logic of GvZ (port of gvz_combat.gd): fire cadence, production, magnet and
    /// gust ticks, boom-berry fuses. Contact cases (Knolle, trampoline) live in <see cref="GvzZombies"/>.
    /// </summary>
    public static class GvzCombat
    {
        public const int RangeEnd = 9600;

        private static bool HitsFlying(string kind) => kind == "star";

        /// <summary>Every tower acts (production, shots, magnet, gust, fuse).</summary>
        public static void ActTowers(GvzRun run)
        {
            var now = run.Tick;
            foreach (var key in run.Towers.KeysSnapshot())
            {
                if (!run.Towers.TryGetValue(key, out var tower)) continue;
                var row = run.TowerRow(tower.Type);
                if (tower.FuseAt.HasValue && now >= tower.FuseAt.Value)
                {
                    Boom(run, tower, row);
                    continue;
                }
                if (GvzJson.Has(row, "produce_amount") && now >= tower.NextAct)
                {
                    tower.NextAct = now + GvzJson.I(row, "produce_interval_ticks", 480);
                    GvzLogic.SpawnDrop(run, tower.Lane, tower.Col, GvzJson.I(row, "produce_amount", 0), "produce");
                    continue;
                }
                if (GvzJson.Has(row, "steal_interval_ticks") && now >= tower.NextAct)
                {
                    if (GvzZombies.MagnetSteal(run, tower, GvzJson.I(row, "range_cols", 4)))
                        tower.NextAct = now + GvzJson.I(row, "steal_interval_ticks", 0);
                    continue;
                }
                if (GvzJson.Has(row, "gust_interval_ticks") && now >= tower.NextAct)
                {
                    if (Gust(run, tower)) tower.NextAct = now + GvzJson.I(row, "gust_interval_ticks", 0);
                    continue;
                }
                if (GvzJson.Has(row, "projectile") && now >= tower.NextAct)
                    if (Fire(run, tower, row)) tower.NextAct = now + GvzJson.I(row, "fire_interval_ticks", 28);
            }
        }

        /// <summary>Move projectiles and resolve hits (incl. the boss bin lorry).</summary>
        public static void StepProjectiles(GvzRun run)
        {
            var speed = GvzJson.I(run.Combat, "projectile_speed_mm_per_tick", 250);
            for (var i = run.Projectiles.Count - 1; i >= 0; i--)
            {
                var proj = run.Projectiles[i];
                var prevX = proj.X;
                var newX = prevX + speed;
                proj.X = newX;
                var victim = FirstTarget(run, proj, prevX, newX);
                if (victim != null)
                {
                    ResolveHit(run, proj, victim);
                    run.Projectiles.RemoveAt(i);
                    continue;
                }
                if (HitsBoss(run, proj, newX))
                {
                    GvzZombies.DamageBoss(run, proj.Dmg);
                    run.Projectiles.RemoveAt(i);
                    continue;
                }
                if (newX > RangeEnd) run.Projectiles.RemoveAt(i);
            }
        }

        /// <summary>
        /// Blast around (lane, col) with a radius in cells (0 = own cell only). Balloons are popped FIRST and then hurt;
        /// diggers are spared; the boss gets caught too. excludeId: a zombie already hit directly (the Knolle biter).
        /// </summary>
        public static void Blast(GvzRun run, int lane, int col, int radius, int damage, int excludeId = -1)
        {
            GvzLogic.Push(run, new GvzEvent { Kind = "blast", Lane = lane, Col = col, Radius = radius });
            foreach (var zombie in run.Zombies.ToArray())
            {
                if (zombie.Dead || zombie.State == "dig" || zombie.Id == excludeId) continue;
                if (Math.Abs(zombie.Lane - lane) > radius) continue;
                if (Math.Abs(GvzLogic.ColOf(zombie.X) - col) > radius) continue;
                if (zombie.Flying) GvzZombies.PopBalloon(run, zombie);
                GvzZombies.Damage(run, zombie, damage, "blast");
            }
            var boss = run.Boss;
            if (boss != null && boss.Hp > 0 && Math.Abs(boss.Lane - lane) <= radius && Math.Abs(GvzLogic.ColOf(boss.X) - col) <= radius)
                GvzZombies.DamageBoss(run, damage);
        }

        private static void Boom(GvzRun run, GvzTower tower, Newtonsoft.Json.Linq.JObject row)
        {
            GvzLogic.DestroyTower(run, GvzLogic.CellKey(tower.Lane, tower.Col), "boom");
            Blast(run, tower.Lane, tower.Col, GvzJson.I(row, "radius_cells", 1), GvzJson.I(row, "damage", 1800));
        }

        /// <summary>A shot (carrot/frost/star/melon). Stars fire into their own AND both neighbour lanes; only with a target.</summary>
        private static bool Fire(GvzRun run, GvzTower tower, Newtonsoft.Json.Linq.JObject row)
        {
            var kind = GvzJson.S(row, "projectile", "carrot");
            var lanes = new System.Collections.Generic.List<int> { tower.Lane };
            if (kind == "star")
                foreach (var offset in new[] { -1, 1 })
                    if (run.Lanes.Contains(tower.Lane + offset)) lanes.Add(tower.Lane + offset);
            var fired = false;
            var startX = tower.Col * GvzLogic.CellMm + 600;
            foreach (var lane in lanes)
            {
                if (!LaneHasTarget(run, lane, kind, startX)) continue;
                fired = true;
                var volley = GvzJson.I(row, "volley", 1);
                if (kind == "carrot" || kind == "frost") GvzLogic.BumpStat(run, "moehren_shots", volley);
                for (var shot = 0; shot < volley; shot++)
                    run.Projectiles.Add(new GvzProjectile
                    {
                        Id = GvzLogic.NextId(run),
                        Lane = lane,
                        X = startX - shot * 260,
                        Kind = kind,
                        Dmg = GvzJson.I(row, "damage", 20),
                        Splash = GvzJson.I(row, "splash_damage", 0),
                        SlowTicks = GvzJson.I(row, "slow_ticks", 0),
                    });
            }
            if (fired) GvzLogic.Push(run, new GvzEvent { Kind = "shoot", Lane = tower.Lane, Col = tower.Col, Proj = kind });
            return fired;
        }

        /// <summary>Pust-Gooby: blow down the nearest balloon of its own lane.</summary>
        private static bool Gust(GvzRun run, GvzTower tower)
        {
            GvzZombie best = null;
            var bestX = RangeEnd + 1;
            foreach (var zombie in run.Zombies)
            {
                if (zombie.Dead || !zombie.Flying || zombie.Lane != tower.Lane) continue;
                if (zombie.X < bestX) { bestX = zombie.X; best = zombie; }
            }
            if (best == null) return false;
            GvzZombies.PopBalloon(run, best);
            return true;
        }

        /// <summary>Is there something in the lane this projectile kind can hit?</summary>
        public static bool LaneHasTarget(GvzRun run, int lane, string kind, int fromX)
        {
            foreach (var zombie in run.Zombies) if (Targetable(zombie, kind, lane, fromX)) return true;
            var boss = run.Boss;
            return boss != null && boss.Hp > 0 && boss.Lane == lane;
        }

        private static bool Targetable(GvzZombie zombie, string kind, int lane, int fromX)
        {
            if (zombie.Dead || zombie.Lane != lane || zombie.State == "dig") return false;
            if (zombie.Flying && !HitsFlying(kind)) return false;
            return zombie.X + 200 >= fromX && zombie.X <= RangeEnd;
        }

        /// <summary>First victim on the flight segment [prevX, newX] (smallest x wins).</summary>
        private static GvzZombie FirstTarget(GvzRun run, GvzProjectile proj, int prevX, int newX)
        {
            GvzZombie best = null;
            var bestX = RangeEnd + 1;
            foreach (var zombie in run.Zombies)
            {
                if (!Targetable(zombie, proj.Kind, proj.Lane, prevX - 200)) continue;
                if (zombie.X - 150 > newX) continue;
                if (zombie.X < bestX) { bestX = zombie.X; best = zombie; }
            }
            return best;
        }

        private static bool HitsBoss(GvzRun run, GvzProjectile proj, int newX)
        {
            var boss = run.Boss;
            if (boss == null || boss.Hp <= 0) return false;
            return boss.Lane == proj.Lane && newX >= boss.X - 400;
        }

        private static void ResolveHit(GvzRun run, GvzProjectile proj, GvzZombie victim)
        {
            if (proj.SlowTicks > 0 && !victim.Flying) GvzZombies.ApplySlow(run, victim, proj.SlowTicks);
            GvzZombies.Damage(run, victim, proj.Dmg, proj.Kind);
            if (proj.Splash <= 0) return;
            var centerX = victim.X;
            foreach (var zombie in run.Zombies.ToArray())
            {
                if (zombie.Dead || zombie == victim || zombie.State == "dig") continue;
                if (zombie.Flying || zombie.Lane != proj.Lane) continue;
                if (Math.Abs(zombie.X - centerX) <= GvzLogic.CellMm) GvzZombies.Damage(run, zombie, proj.Splash, proj.Kind);
            }
        }
    }
}
