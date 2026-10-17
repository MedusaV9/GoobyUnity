using System;
using System.Collections.Generic;
using Gooby.Core.Json;
using Newtonsoft.Json.Linq;

namespace Gooby.Core.Life
{
    /// <summary>
    /// Sleep state machine — 1:1 port of GOOBY/src/systems/sleep.js (§C1.4) / scripts/logic/sleep.gd.
    /// Works on the FLAT web-shaped state (stats/sleep/grumpyUntil/lastTickAt/xp/level/coins/achievements).
    /// All transforms are pure (deep-clone in, new object out).
    /// </summary>
    public static class Sleep
    {
        public const double StartBelowEnergy = 70.0;
        public const double DurationBaseMin = 30.0;
        public const int DurationMinMin = 10;
        public const int EarlyWakeAfterMin = 0;
        public const double EarlyWakeMoodDebuff = 15.0;
        public const int EarlyWakeDebuffMin = 10;
        public const double NapBelowEnergy = 90.0;
        public const int NapMin = 20;
        public const double TiredVisibleBelow = 45.0;

        public static int SleepDurationMin(double energy)
        {
            var e = Math.Min(Stats.Max, Math.Max(Stats.Min, energy));
            var raw = (int)Math.Ceiling(DurationBaseMin * (Stats.Max - e) / Stats.Max);
            return Math.Max(DurationMinMin, raw);
        }

        public static bool IsSleeping(JObject state)
        {
            if (state?["sleep"] is JObject sleep) return J.IsTrue(sleep["sleeping"]);
            return false;
        }

        public static bool CanSleep(JObject state)
        {
            if (IsSleeping(state)) return false;
            if (!(state?["stats"] is JObject stats)) return false;
            return J.Num(stats["energy"]) < StartBelowEnergy;
        }

        public static JObject StartSleep(JObject state, long nowMs)
        {
            var dur = SleepDurationMin(J.Num(J.Dict(state["stats"])["energy"]));
            var s = J.CloneObj(state);
            s["sleep"] = SleepSlice(true, nowMs, nowMs + dur * 60000L);
            return s;
        }

        public static bool CanNap(JObject state)
        {
            if (IsSleeping(state)) return false;
            if (!(state?["stats"] is JObject stats)) return false;
            return J.Num(stats["energy"]) < NapBelowEnergy;
        }

        public static JObject StartNap(JObject state, long nowMs)
        {
            var full = SleepDurationMin(J.Num(J.Dict(state["stats"])["energy"]));
            var dur = Math.Min(NapMin, full);
            var s = J.CloneObj(state);
            s["sleep"] = SleepSlice(true, nowMs, nowMs + dur * 60000L);
            return s;
        }

        public static bool CanWakeEarly(JObject state, long nowMs)
        {
            if (!IsSleeping(state)) return false;
            return nowMs - J.Long(J.Dict(state["sleep"])["startedAt"]) >= EarlyWakeAfterMin * 60000L;
        }

        public static long SleepRemainingMs(JObject state, long nowMs)
        {
            if (!IsSleeping(state)) return 0;
            return Math.Max(0, J.Long(J.Dict(state["sleep"])["wakeAt"]) - nowMs);
        }

        /// <summary>Grants for a COMPLETED sleep (§C1.5): XP +10 (level-ups) and counters.sleeps += 1.</summary>
        public static JObject ApplyCompletedSleepGrants(JObject state)
        {
            var progress = Leveling.ApplyXp(
                new JObject { ["xp"] = state["xp"] ?? 0, ["level"] = state["level"] ?? 1 }, Leveling.XpCompletedSleep);
            var s = J.CloneObj(state);
            s["xp"] = progress.Xp;
            s["level"] = progress.Level;
            s["coins"] = (long)J.Num(s["coins"]) + progress.CoinsAwarded;
            var achievements = J.EnsureDict(s, "achievements");
            var counters = J.EnsureDict(achievements, "counters");
            counters["sleeps"] = (long)J.Num(counters["sleeps"]) + 1;
            return s;
        }

        /// <summary>Wake Gooby up. early=true: grumpy debuff + 'wokeEarly'; else grants + 'wokeUp'.</summary>
        public static (JObject state, List<string> events) WakeUp(JObject state, long nowMs, bool early = false)
        {
            var s = J.CloneObj(state);
            s["sleep"] = SleepSlice(false, 0, 0);
            if (early)
            {
                s["grumpyUntil"] = nowMs + EarlyWakeDebuffMin * 60000L;
                return (s, new List<string> { "wokeEarly" });
            }
            s = ApplyCompletedSleepGrants(s);
            return (s, new List<string> { "wokeUp" });
        }

        /// <summary>Pure asleep tick: asleep rates from lastTickAt for min(now, wakeAt); auto-wakes at wakeAt or energy ≥ 100.</summary>
        public static (JObject state, List<string> events) TickSleep(JObject state, long nowMs)
        {
            if (!IsSleeping(state)) return (state, new List<string>());
            var from = J.Num(state["lastTickAt"]);
            if (from == 0.0) from = nowMs;
            var wakeAt = J.Num(J.Dict(state["sleep"])["wakeAt"]);
            var until = Math.Min(nowMs, wakeAt);
            var dtMin = Math.Max(0.0, (until - from) / 60000.0);
            var s = J.CloneObj(state);
            s["stats"] = Stats.ApplyTick(J.Dict(state["stats"]), dtMin, asleep: true);
            s["lastTickAt"] = nowMs;
            if (nowMs >= wakeAt || J.Num(J.Dict(s["stats"])["energy"]) >= Stats.Max)
                return WakeUp(s, nowMs, early: false);
            return (s, new List<string>());
        }

        public static double GrumpyDebuff(JObject state, long nowMs) =>
            nowMs < J.Num(state?["grumpyUntil"]) ? EarlyWakeMoodDebuff : 0.0;

        public static double CurrentMood(JObject state, long nowMs) =>
            Stats.Mood(J.Dict(state["stats"]), GrumpyDebuff(state, nowMs));

        public static double Tiredness01(JObject stats)
        {
            if (stats == null) return 0.0;
            var energy = J.Num(stats["energy"]);
            return J.Clamp((TiredVisibleBelow - energy) / TiredVisibleBelow, 0.0, 1.0);
        }

        public static JObject SleepSlice(bool sleeping, long startedAt, long wakeAt) => new JObject
        {
            ["sleeping"] = sleeping, ["startedAt"] = startedAt, ["wakeAt"] = wakeAt,
        };

        // ── v5 save helpers (bed interactable) ─────────────────────────────

        /// <summary>Flat read-only view of the v5 gooby slice for the can_* checks.</summary>
        public static JObject FlatOf(JObject v5State)
        {
            if (!(v5State?["gooby"] is JObject gooby))
                return new JObject { ["stats"] = new JObject(), ["sleep"] = new JObject(), ["grumpyUntil"] = 0 };
            return new JObject
            {
                ["stats"] = gooby["stats"] ?? new JObject(),
                ["sleep"] = gooby["sleep"] ?? new JObject(),
                ["grumpyUntil"] = gooby["grumpyUntil"] ?? 0,
            };
        }

        /// <summary>Start a sleep (nap=false) or nap (nap=true) directly in the v5 save. Mutates in place.</summary>
        public static bool StartSleepState(JObject v5State, long nowMs, bool nap = false)
        {
            if (!(v5State?["gooby"] is JObject gooby)) return false;
            var flat = FlatOf(v5State);
            if (!(flat["stats"] is JObject st) || !st.HasValues) return false;
            if (nap)
            {
                if (!CanNap(flat)) return false;
                gooby["sleep"] = StartNap(flat, nowMs)["sleep"];
            }
            else
            {
                if (!CanSleep(flat)) return false;
                gooby["sleep"] = StartSleep(flat, nowMs)["sleep"];
            }
            return true;
        }

        /// <summary>Early manual wake in the v5 save (grumpy debuff, no grants). Returns events ([] = nothing).</summary>
        public static List<string> WakeEarlyState(JObject v5State, long nowMs)
        {
            if (!(v5State?["gooby"] is JObject gooby)) return new List<string>();
            var flat = FlatOf(v5State);
            if (!IsSleeping(flat) || !CanWakeEarly(flat, nowMs)) return new List<string>();
            var (res, events) = WakeUp(flat, nowMs, early: true);
            gooby["sleep"] = res["sleep"];
            gooby["grumpyUntil"] = res["grumpyUntil"];
            return events;
        }
    }
}
