using System.Collections.Generic;
using System.Linq;

namespace Gooby.Core.Minigames.Gvz
{
    /// <summary>
    /// QA/test bot for the online PvP: it only ever PLANS through the <see cref="GvzPvpLockstep"/> (never touches the sim,
    /// so both devices stay identical), one decision every <see cref="DecisionTicks"/> sim ticks — longer than the input
    /// delay, so a decision sees the effect of the previous one. Gooby side: collect every blob, a shooter in every
    /// threatened lane, a collector at the start of every lane, a second shooter, then a wall. Zombie side: the most
    /// expensive ready type it can afford, lane by lane.
    /// </summary>
    public sealed class GvzPvpBot
    {
        public const int DecisionTicks = 10;
        private static readonly string[] Shooters = { "moehrenschuetze", "doppelmoehre", "eis_gooby", "sternchen_gooby", "melonen_meier" };
        private int _nextTick;
        private int _lane;

        public void Plan(GvzPvpLockstep net)
        {
            var run = net?.State;
            if (run == null || net.IsMatchOver || net.Desynced || run.Tick < _nextTick) return;
            _nextTick = run.Tick + DecisionTicks;
            if (net.Side == GvzPvpLockstep.SideZombie) PlanZombie(net);
            else PlanGooby(net);
        }

        private void PlanZombie(GvzPvpLockstep net)
        {
            var lanes = net.State.Lanes;
            if (lanes.Count == 0) return;
            var lane = lanes[_lane % lanes.Count];
            string pick = null;
            foreach (var type in net.ZombieTypes())
                if (net.CanSpawn(type, lane).Ok && (pick == null || net.ZombieCost(type) > net.ZombieCost(pick))) pick = type;
            if (pick == null) return;
            net.ScheduleSpawn(pick, lane);
            _lane += 1;
        }

        private void PlanGooby(GvzPvpLockstep net)
        {
            var run = net.State;
            foreach (var drop in run.Drops)
                if (!net.IsCollectPlanned(drop.Id)) net.ScheduleCollect(drop.Id);
            var towers = run.Towers.Keys.Select(k => run.Towers[k]).ToList();
            var threatened = run.Zombies.Where(z => !z.Dead).Select(z => z.Lane).Distinct().OrderBy(l => l).ToList();
            foreach (var lane in threatened)
                if (!towers.Any(t => t.Lane == lane && Shooters.Contains(t.Type)) && TryPlace(net, "moehrenschuetze", lane, 1, 4)) return;
            foreach (var lane in run.Lanes)
                if (!towers.Any(t => t.Lane == lane && t.Type == "nutella_sammler") && TryPlace(net, "nutella_sammler", lane, 0, 0)) return;
            foreach (var lane in threatened)
                if (towers.Count(t => t.Lane == lane && Shooters.Contains(t.Type)) < 2 && TryPlace(net, "moehrenschuetze", lane, 1, 5)) return;
            if (run.Nutella >= 300)
                foreach (var lane in threatened)
                    if (!towers.Any(t => t.Lane == lane && t.Type == "dicker_bert") && TryPlace(net, "dicker_bert", lane, 6, 7)) return;
        }

        private static bool TryPlace(GvzPvpLockstep net, string type, int lane, int fromCol, int toCol)
        {
            for (var col = fromCol; col <= toCol; col++)
            {
                var check = GvzLogic.CanPlace(net.State, type, lane, col);
                if (check.Ok)
                {
                    net.SchedulePlace(type, lane, col);
                    return true;
                }
                if (check.Reason != "cell_occupied") return false; // cooldown / nutella: wait for the next decision
            }
            return false;
        }
    }
}
