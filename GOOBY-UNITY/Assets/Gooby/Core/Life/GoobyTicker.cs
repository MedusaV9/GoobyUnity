using System;
using System.Collections.Generic;
using Gooby.Core.Json;
using Gooby.Core.Time;
using Newtonsoft.Json.Linq;

namespace Gooby.Core.Life
{
    /// <summary>Deterministic outdoor weather oracle (SoulWetter): is it raining/snowing at a local date+hour?</summary>
    public interface IWeatherOracle
    {
        bool IsWetOutside(string localDate, double localHour);
    }

    /// <summary>Arcade modifier scheduler hook (ModifierEngine.tick): returns event strings, mutates state["modifiers"].</summary>
    public interface IModifierScheduler
    {
        List<string> Tick(JObject state, long nowMs);
    }

    /// <summary>
    /// The ONE adapter between the v5 schema (gooby.stats / progression / economy / vacation) and the
    /// flat web-shaped state the pure logic ports work on — port of scripts/state/gooby_ticker.gd.
    /// GameStateStore calls <see cref="CatchUp"/> on load/resume and <see cref="LiveTick"/> every few seconds.
    /// </summary>
    public static class GoobyTicker
    {
        public const double ChillHygieneBelow = 40.0;

        public static IWeatherOracle Weather { get; set; }
        public static IModifierScheduler Modifiers { get; set; }
        /// <summary>Time-zone bias in minutes used for the chill check (tests inject 0).</summary>
        public static Func<long, int> TimeZoneBiasMinutes { get; set; } = ms => new GameClock().UtcOffsetMinutes(ms);

        public static JObject FlatView(JObject state)
        {
            var gooby = J.Dict(state["gooby"]);
            var prog = J.Dict(state["progression"]);
            var econ = J.Dict(state["economy"]);
            return new JObject
            {
                ["stats"] = J.Dict(gooby["stats"]),
                ["sleep"] = J.Dict(gooby["sleep"]),
                ["grumpyUntil"] = gooby["grumpyUntil"] ?? 0,
                ["lastTickAt"] = gooby["lastTickAt"] ?? 0,
                ["xp"] = prog["xp"] ?? 0,
                ["level"] = prog["level"] ?? 1,
                ["coins"] = econ["coins"] ?? 0,
                ["vacation"] = J.Dict(state["vacation"]),
                ["achievements"] = J.Dict(state["achievements"]),
            };
        }

        public static void WriteBack(JObject state, JObject flat)
        {
            var gooby = J.EnsureDict(state, "gooby");
            gooby["stats"] = J.Dict(flat["stats"]);
            gooby["sleep"] = J.Dict(flat["sleep"]);
            gooby["grumpyUntil"] = flat["grumpyUntil"] ?? 0;
            gooby["lastTickAt"] = flat["lastTickAt"] ?? 0;
            var prog = J.EnsureDict(state, "progression");
            prog["xp"] = flat["xp"] ?? 0;
            prog["level"] = flat["level"] ?? 1;
            var econ = J.EnsureDict(state, "economy");
            econ["coins"] = flat["coins"] ?? 0;
            state["vacation"] = J.Dict(flat["vacation"]);
            state["achievements"] = J.Dict(flat["achievements"]);
        }

        /// <summary>Offline catch-up (§E4) incl. illness/weight advance and the recovery boost. Mutates in place.</summary>
        public static List<string> CatchUp(JObject state, long nowMs)
        {
            var flat = FlatView(state);
            var last = J.Num(flat["lastTickAt"]);
            var wasSleeping = Sleep.IsSleeping(flat);
            var wakeAt = wasSleeping ? J.Num(J.Dict(flat["sleep"])["wakeAt"]) : 0.0;
            var wasAway = Vacation.IsAway(flat);
            var (res, events) = Offline.SimulateOffline(flat, nowMs);
            WriteBack(state, res);
            if (!wasAway && last > 0.0 && nowMs > last)
            {
                var elapsedMs = nowMs - last;
                var awakeMs = elapsedMs;
                if (wasSleeping) awakeMs = elapsedMs - Math.Max(0.0, Math.Min(elapsedMs, wakeAt - last));
                var awakeMin = Math.Min(Math.Max(0.0, awakeMs) / 60000.0, Offline.AwakeCapMin);
                if (awakeMin > 0.0)
                {
                    events.AddRange(AdvanceCare(state, awakeMin, Offline.AwakeRateMult, nowMs, asleep: false));
                    var awakeStart = wasSleeping ? J.Clamp(wakeAt, last, nowMs) : last;
                    ErholungsBoostOffline(state, awakeStart, awakeMin, nowMs);
                }
            }
            events.AddRange(TickModifiers(state, nowMs));
            return events;
        }

        /// <summary>One live tick while the app is open. Mutates in place; returns web event strings.</summary>
        public static List<string> LiveTick(JObject state, long nowMs)
        {
            var events = LiveTickCore(state, nowMs);
            events.AddRange(TickModifiers(state, nowMs));
            return events;
        }

        private static List<string> LiveTickCore(JObject state, long nowMs)
        {
            var flat = FlatView(state);
            if (Vacation.IsAway(flat))
            {
                flat["lastTickAt"] = nowMs;
                WriteBack(state, flat);
                return new List<string>();
            }
            var last = J.Num(flat["lastTickAt"]);
            var dtMin = last > 0.0 ? Math.Max(0.0, (nowMs - last) / 60000.0) : 0.0;
            if (Sleep.IsSleeping(flat))
            {
                var (res, sleepEvents) = Sleep.TickSleep(flat, nowMs);
                WriteBack(state, res);
                var evs = new List<string>(sleepEvents);
                if (dtMin > 0.0) evs.AddRange(AdvanceCare(state, dtMin, 1.0, nowMs, asleep: true));
                return evs;
            }
            if (last <= 0.0 || nowMs <= last)
            {
                flat["lastTickAt"] = nowMs;
                WriteBack(state, flat);
                return new List<string>();
            }
            var before = J.CloneObj(J.Dict(flat["stats"]));
            var stats = Stats.ApplyTick(before, dtMin);
            // §C3.3: queasy/sick → fun decays ×1.25 (the extra share applied here).
            if (Health.Grade(J.Dict(state["gooby"])["health"]) > 0)
            {
                var extra = Math.Abs(Stats.RatesAwake["fun"]) * (Health.QueasyFunDecayMult - 1.0);
                stats["fun"] = Stats.ClampStat(J.Num(stats["fun"]) - extra * dtMin);
            }
            // Recovery boost: energy drains 20 % slower for 48 h after a vacation pickup.
            var drain = Vacation.EnergieDrainFaktor(J.Dict(state["vacation"]), nowMs);
            if (drain < 1.0)
            {
                var back = Math.Abs(Stats.RatesAwake["energy"]) * (1.0 - drain) * dtMin;
                stats["energy"] = Stats.ClampStat(J.Num(stats["energy"]) + back);
            }
            flat["stats"] = stats;
            flat["lastTickAt"] = nowMs;
            WriteBack(state, flat);
            var events = new List<string>();
            foreach (var k in Stats.Keys)
            {
                var was = J.Num(before[k]);
                if (was >= Stats.LowStat && J.Num(stats[k]) < Stats.LowStat) events.Add("statLow:" + k);
            }
            events.AddRange(AdvanceCare(state, dtMin, 1.0, nowMs, asleep: false));
            return events;
        }

        private static List<string> TickModifiers(JObject state, long nowMs) =>
            Modifiers != null ? Modifiers.Tick(state, nowMs) : new List<string>();

        private static void ErholungsBoostOffline(JObject state, double awakeStart, double awakeMin, long nowMs)
        {
            var erholtBis = J.Num(J.Dict(state["vacation"])["erholtBis"]);
            var boostMin = Math.Min((Math.Min(nowMs, erholtBis) - awakeStart) / 60000.0, awakeMin);
            if (boostMin <= 0.0) return;
            var stats = J.EnsureDict(J.EnsureDict(state, "gooby"), "stats");
            var back = Math.Abs(Stats.RatesAwake["energy"]) * (1.0 - Vacation.ErholungsDrainFaktor) * Offline.AwakeRateMult * boostMin;
            stats["energy"] = Stats.ClampStat(J.Num(stats["energy"]) + back);
        }

        /// <summary>Advance illness + weight by dtMin (mult = offline factor). Mutates state.gooby; returns health events.</summary>
        private static List<string> AdvanceCare(JObject state, double dtMin, double mult, long nowMs, bool asleep)
        {
            var gooby = J.EnsureDict(state, "gooby");
            var stats = J.Dict(gooby["stats"]);
            var opts = new Health.TickOptions
            {
                Mult = mult,
                NowMs = nowMs,
                Exhausted = !asleep && Stats.IsExhausted(stats),
                Chill = !asleep && ChillActive(stats, nowMs),
            };
            var (h, events) = Health.Tick(gooby["health"], dtMin, Health.LowStatCount(stats), opts);
            gooby["health"] = h;
            gooby["weight"] = Weight.Tick(J.NumOr(gooby["weight"], Weight.Default), dtMin, mult);
            return events;
        }

        /// <summary>Is Gooby freezing? Rain/snow outside (deterministic per day) AND clammy hygiene.</summary>
        private static bool ChillActive(JObject stats, long nowMs)
        {
            if (J.Num(stats["hygiene"]) >= ChillHygieneBelow) return false;
            if (Weather == null) return false;
            var (y, m, d, hour) = GameClock.LocalDateTime(nowMs, TimeZoneBiasMinutes(nowMs));
            return Weather.IsWetOutside($"{y:D4}-{m:D2}-{d:D2}", hour);
        }
    }
}
