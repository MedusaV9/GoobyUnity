using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Gooby.Core.Minigames.Gobnom
{
    /// <summary>
    /// Auto-solver / bot (port of gobnom_solver.gd): runs a level's data-driven solution plan (level.solution.actions,
    /// times in seconds) ONLY through the sim's public action API, player tags included — every green run is the
    /// machine proof that the level is solvable and, in coop, that the plan respects the ownership gates.
    /// </summary>
    public static class GobnomSolver
    {
        public const double DefaultTimeoutSec = 40.0;

        public sealed class Report
        {
            public bool Won;
            public string Outcome;
            public int Jars, Stars, Ticks, SideChanges, Denied, ActionsRun;
        }

        public static Report RunSolution(JObject level, JObject balance, long seed = 1)
        {
            var run = GobnomLogic.NewRun(level, balance, seed);
            var actions = SortedActions(level);
            var timeout = GobnomLogic.Num(balance?["limits"] as JObject, "solver_timeout_sec", DefaultTimeoutSec);
            var maxTicks = (int)(timeout * GobnomLogic.Tps);
            var denied = 0;
            var actionsRun = 0;
            var next = 0;
            while (run.Tick < maxTicks && !GobnomLogic.IsOver(run))
            {
                var now = (double)run.Tick / GobnomLogic.Tps;
                while (next < actions.Count && GobnomLogic.Num(actions[next], "t", 0.0) <= now)
                {
                    var result = Execute(run, actions[next]);
                    actionsRun += 1;
                    if (!result.Ok) denied += 1;
                    next += 1;
                }
                GobnomLogic.Step(run);
            }
            return ReportOf(run, denied, actionsRun);
        }

        /// <summary>Greedy fallback without a plan: cut every rope once the candy hangs (nearly) over the mouth.</summary>
        public static Report RunGreedy(JObject level, JObject balance, long seed = 1)
        {
            var run = GobnomLogic.NewRun(level, balance, seed);
            var maxTicks = (int)(DefaultTimeoutSec * GobnomLogic.Tps);
            var mouth = run.Mouth;
            while (run.Tick < maxTicks && !GobnomLogic.IsOver(run))
            {
                var pos = run.CandyPos;
                if (Math.Abs(pos.X - mouth.X) < 20.0 && pos.Y < mouth.Y)
                    foreach (var rope in run.Ropes.ToArray())
                        if (!rope.Cut) GobnomLogic.CutRope(run, rope.Id);
                GobnomLogic.Step(run);
            }
            return ReportOf(run, 0, 0);
        }

        /// <summary>The plan's actions ordered by time (stable for equal times).</summary>
        public static List<JObject> SortedActions(JObject level)
        {
            var raw = level?["solution"]?["actions"] as JArray ?? new JArray();
            return raw.OfType<JObject>().Select(a => (JObject)a.DeepClone()).OrderBy(a => GobnomLogic.Num(a, "t", 0.0)).ToList();
        }

        /// <summary>The first tick whose sim time reaches the action's t — <see cref="RunSolution"/> runs it right before stepping that tick.</summary>
        public static int PlanTick(JObject action)
        {
            var t = GobnomLogic.Num(action, "t", 0.0);
            if (t <= 0.0) return 0;
            var k = (int)Math.Ceiling(t * GobnomLogic.Tps);
            while (k > 0 && (double)(k - 1) / GobnomLogic.Tps >= t) k--;
            while ((double)k / GobnomLogic.Tps < t) k++;
            return k;
        }

        /// <summary>A plan action as a lockstep action: do, element id (-1 = unknown action) and the slide target.</summary>
        public static (string doKind, int id, double? v) ToLockstep(JObject action)
        {
            var kind = GobnomLogic.Str(action, "do", "");
            switch (kind)
            {
                case "cut": return (kind, GobnomLogic.Int(action, "rope", 0), null);
                case "pop": return (kind, GobnomLogic.Int(action, "bubble", 0), null);
                case "puff": return (kind, GobnomLogic.Int(action, "cushion", 0), null);
                case "fan": return (kind, GobnomLogic.Int(action, "fan", 0), null);
                case "slide": return (kind, GobnomLogic.Int(action, "rope", 0), GobnomLogic.Num(action, "to", 0.0));
                default: return (kind, -1, null);
            }
        }

        /// <summary>One plan/lockstep action through the public sim API ({do, rope|bubble|cushion|fan, to, player}).</summary>
        public static GobnomResult Execute(GobnomRun run, JObject action)
        {
            var player = GobnomLogic.Str(action, "player", GobnomLogic.PlayerSolo);
            switch (GobnomLogic.Str(action, "do", ""))
            {
                case "cut": return GobnomLogic.CutRope(run, GobnomLogic.Int(action, "rope", 0), player);
                case "pop": return GobnomLogic.PopBubble(run, GobnomLogic.Int(action, "bubble", 0), player);
                case "puff": return GobnomLogic.PuffCushion(run, GobnomLogic.Int(action, "cushion", 0), player);
                case "fan": return GobnomLogic.ToggleFan(run, GobnomLogic.Int(action, "fan", 0), player);
                case "slide": return GobnomLogic.MoveAnchor(run, GobnomLogic.Int(action, "rope", 0), GobnomLogic.Num(action, "to", 0.0), player);
                default: return GobnomResult.No("unknown_action");
            }
        }

        private static Report ReportOf(GobnomRun run, int denied, int actionsRun) => new Report
        {
            Won = run.Outcome == "won",
            Outcome = run.Outcome,
            Jars = run.JarsTaken,
            Stars = GobnomLogic.StarsFor(run.JarsTaken),
            Ticks = run.Tick,
            SideChanges = run.SideChanges,
            Denied = denied,
            ActionsRun = actionsRun,
        };
    }
}
