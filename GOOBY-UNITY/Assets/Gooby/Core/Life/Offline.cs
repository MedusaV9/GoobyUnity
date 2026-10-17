using System;
using System.Collections.Generic;
using Gooby.Core.Json;
using Newtonsoft.Json.Linq;

namespace Gooby.Core.Life
{
    /// <summary>
    /// Offline catch-up simulation — port of GOOBY/src/systems/offline.js (§E4) / scripts/logic/offline.gd.
    /// Works on the FLAT web-shaped state. Golden proof: golden_values.json offline.cases.
    /// </summary>
    public static class Offline
    {
        public const double AwakeRateMult = 0.3;
        public const double AwakeCapMin = 480.0;

        public static (JObject state, List<string> events) SimulateOffline(JObject state, long nowMs)
        {
            var events = new List<string>();
            var last = J.Num(state["lastTickAt"]);
            if (last == 0.0) last = nowMs;
            var elapsedMs = nowMs - last;
            if (elapsedMs <= 0.0)
            {
                var unchanged = J.CloneObj(state);
                unchanged["lastTickAt"] = nowMs;
                return (unchanged, events);
            }

            var statsBefore = J.CloneObj(J.Dict(state["stats"]));
            var s = J.CloneObj(state);
            var awakeMs = elapsedMs;

            var vacAway = Vacation.IsAway(s);
            if (vacAway) awakeMs = 0.0;

            if (!vacAway && Sleep.IsSleeping(s))
            {
                var wakeAt = J.Num(J.Dict(s["sleep"])["wakeAt"]);
                var asleepMs = Math.Max(0.0, Math.Min(elapsedMs, wakeAt - last));
                s["stats"] = Stats.ApplyTick(J.Dict(s["stats"]), asleepMs / 60000.0, asleep: true);
                if (nowMs >= wakeAt)
                {
                    var (woken, wokenEvents) = Sleep.WakeUp(s, (long)wakeAt, early: false);
                    s = woken;
                    events.AddRange(wokenEvents);
                    awakeMs = elapsedMs - asleepMs;
                }
                else
                {
                    awakeMs = 0.0;
                }
            }

            var awakeMin = Math.Min(awakeMs / 60000.0, AwakeCapMin);
            if (awakeMin > 0.0)
                s["stats"] = Stats.ApplyTick(J.Dict(s["stats"]), awakeMin, asleep: false, rateMult: AwakeRateMult);
            s["lastTickAt"] = nowMs;

            var after = J.Dict(s["stats"]);
            foreach (var k in Stats.Keys)
            {
                var before = J.Num(statsBefore[k]);
                if (before >= Stats.LowStat && J.Num(after[k]) < Stats.LowStat) events.Add("statLow:" + k);
            }

            var (changes, vacEvents) = Vacation.Tick(s, nowMs);
            if (changes != null) s["vacation"] = changes;
            foreach (var ev in vacEvents)
            {
                switch (ev.Type)
                {
                    case "postcard": events.Add("vacationPostcard"); break;
                    case "returnReady": events.Add("vacationReturnReady"); break;
                    case "overdue": events.Add("vacationOverdue"); break;
                }
            }
            return (s, events);
        }
    }
}
