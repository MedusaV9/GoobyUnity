using System;
using System.Collections.Generic;
using Gooby.Core.Json;
using Newtonsoft.Json.Linq;

namespace Gooby.Core.Life
{
    /// <summary>
    /// Illness state machine — port of GOOBY/src/systems/health.js (§B5/§C3) / scripts/logic/health.gd.
    /// healthy → queasy → sick (one upward step per tick); cures jump down. Pure slice-in/slice-out.
    /// </summary>
    public static class Health
    {
        public static readonly string[] States = { "healthy", "queasy", "sick" };

        public const double JunkEat = 1.0;
        public const double HealthyEat = -0.5;
        public const double JunkDecayPerMin = 1.0 / 120.0;
        public const int NeglectMinStats = 2;
        public const double NeglectStatBelow = 15.0;
        public const double QueasyJunk = 5.0;
        public const double QueasyNeglectMin = 120.0;
        public const double SickJunk = 8.0;
        public const double SickNeglectMin = 360.0;
        public const double RecoverMin = 60.0;
        public const double RecoverJunkBelow = 3.0;
        public const double WarnJunk = 4.0;
        public const double QueasyFunDecayMult = 1.25;
        public const double TiredQueasyMin = 240.0;
        public const double TiredSickMin = 720.0;
        public const double ChillQueasyMin = 180.0;
        public const double ChillSickMin = 540.0;
        public const int VetCurePrice = 120;
        public const double VetCureStatBonus = 10.0;
        public const int VetCheckupPrice = 30;

        public struct TickOptions
        {
            public double Mult;
            public long? NowMs;
            public bool Exhausted;
            public bool Chill;
            public static TickOptions Default => new TickOptions { Mult = 1.0 };
        }

        public static JObject Normalize(JToken h)
        {
            var src = J.Dict(h);
            var state = J.Str(src["state"], "healthy");
            if (Array.IndexOf(States, state) < 0) state = "healthy";
            return new JObject
            {
                ["state"] = state,
                ["junkScore"] = Math.Max(0.0, J.Num(src["junkScore"])),
                ["neglectMin"] = Math.Max(0.0, J.Num(src["neglectMin"])),
                ["recoverMin"] = Math.Max(0.0, J.Num(src["recoverMin"])),
                ["since"] = Math.Max(0.0, J.Num(src["since"])),
                ["tiredMin"] = Math.Max(0.0, J.Num(src["tiredMin"])),
                ["chillMin"] = Math.Max(0.0, J.Num(src["chillMin"])),
                ["tummyWarnPending"] = J.IsTrue(src["tummyWarnPending"]),
            };
        }

        /// <summary>Feeding: junk +1 junkScore and restarts the 60-min recovery window; healthy −0.5 (floor 0).</summary>
        public static JObject OnEat(JToken h, bool junk)
        {
            var s = Normalize(h);
            if (junk)
            {
                var before = J.Num(s["junkScore"]);
                s["junkScore"] = before + JunkEat;
                s["recoverMin"] = 0.0;
                if (J.Str(s["state"]) == "healthy" && before < WarnJunk && J.Num(s["junkScore"]) >= WarnJunk)
                    s["tummyWarnPending"] = true;
            }
            else
            {
                s["junkScore"] = Math.Max(0.0, J.Num(s["junkScore"]) + HealthyEat);
            }
            return s;
        }

        /// <summary>Advance the illness machine by dtMin minutes. Returns the new slice and event strings.</summary>
        public static (JObject h, List<string> events) Tick(JToken h, double dtMin, int lowStatCount, TickOptions opts)
        {
            var s = Normalize(h);
            var events = new List<string>();
            var mult = opts.Mult;
            var effMin = Math.Max(0.0, dtMin) * mult;

            s["junkScore"] = Math.Max(0.0, J.Num(s["junkScore"]) - effMin * JunkDecayPerMin);
            s["neglectMin"] = lowStatCount >= NeglectMinStats ? J.Num(s["neglectMin"]) + effMin : 0.0;
            s["tiredMin"] = opts.Exhausted ? J.Num(s["tiredMin"]) + effMin : 0.0;
            s["chillMin"] = opts.Chill ? J.Num(s["chillMin"]) + effMin : 0.0;
            var clean = J.Num(s["junkScore"]) < RecoverJunkBelow
                        && J.Num(s["neglectMin"]) == 0.0
                        && J.Num(s["tiredMin"]) == 0.0
                        && J.Num(s["chillMin"]) == 0.0;
            s["recoverMin"] = J.Str(s["state"]) == "queasy" && clean ? J.Num(s["recoverMin"]) + effMin : 0.0;

            var from = J.Str(s["state"]);
            if (from == "healthy")
            {
                if (QueasyDue(s))
                {
                    s["state"] = "queasy";
                    events.Add("becameQueasy");
                }
            }
            else if (from == "queasy")
            {
                if (SickDue(s))
                {
                    s["state"] = "sick";
                    s["recoverMin"] = 0.0;
                    events.Add("becameSick");
                }
                else if (J.Num(s["recoverMin"]) >= RecoverMin)
                {
                    s["state"] = "healthy";
                    s["recoverMin"] = 0.0;
                    events.Add("recovered");
                }
            }

            if (J.Str(s["state"]) != from && opts.NowMs.HasValue) s["since"] = (double)opts.NowMs.Value;

            if (J.IsTrue(s["tummyWarnPending"]))
            {
                s["tummyWarnPending"] = false;
                if (J.Str(s["state"]) == "healthy") events.Add("tummyWarning");
            }
            return (s, events);
        }

        private static bool QueasyDue(JObject s) =>
            J.Num(s["junkScore"]) >= QueasyJunk || J.Num(s["neglectMin"]) >= QueasyNeglectMin
            || J.Num(s["tiredMin"]) >= TiredQueasyMin || J.Num(s["chillMin"]) >= ChillQueasyMin;

        private static bool SickDue(JObject s) =>
            J.Num(s["junkScore"]) >= SickJunk || J.Num(s["neglectMin"]) >= SickNeglectMin
            || J.Num(s["tiredMin"]) >= TiredSickMin || J.Num(s["chillMin"]) >= ChillSickMin;

        /// <summary>Medicine: sick → queasy, queasy → healthy; ok=false while healthy (do NOT consume the item).</summary>
        public static (JObject h, bool ok) UseMedicine(JToken h, long nowMs = 0)
        {
            var s = Normalize(h);
            if (J.Str(s["state"]) == "healthy") return (s, false);
            s["state"] = J.Str(s["state"]) == "sick" ? "queasy" : "healthy";
            s["recoverMin"] = 0.0;
            if (nowMs > 0) s["since"] = (double)nowMs;
            return (s, true);
        }

        public static JObject VetCure(JToken h, long nowMs = 0)
        {
            var s = Normalize(h);
            if (J.Str(s["state"]) != "healthy" && nowMs > 0) s["since"] = (double)nowMs;
            s["state"] = "healthy";
            s["junkScore"] = 0.0;
            s["neglectMin"] = 0.0;
            s["recoverMin"] = 0.0;
            s["tiredMin"] = 0.0;
            s["chillMin"] = 0.0;
            s["tummyWarnPending"] = false;
            return s;
        }

        public static JObject VetCheckup(JToken h)
        {
            var s = Normalize(h);
            s["neglectMin"] = 0.0;
            s["tiredMin"] = 0.0;
            s["chillMin"] = 0.0;
            return s;
        }

        /// <summary>Minigame gate (§C3.4): only 'sick' refuses.</summary>
        public static bool CanPlayMinigame(JToken h) => J.Str(Normalize(h)["state"]) != "sick";

        /// <summary>0 = healthy, 1 = queasy, 2 = sick.</summary>
        public static int Grade(JToken h) => Array.IndexOf(States, J.Str(Normalize(h)["state"]));

        public static int LowStatCount(JObject stats)
        {
            var count = 0;
            foreach (var k in Stats.Keys)
                if (J.Num(stats?[k]) < NeglectStatBelow) count++;
            return count;
        }

        // ── v5 state transforms (medicine / vet) ───────────────────────────

        public struct CareResult
        {
            public bool Ok;
            public string Reason;
            public int Total;
        }

        /// <summary>Give one medicine from the inventory (mutates the v5 state). reason: ""|"none"|"healthy".</summary>
        public static CareResult UseMedicineState(JObject state, long nowMs)
        {
            var items = J.EnsureDict(J.EnsureDict(state, "inventory"), "items");
            if ((int)J.Num(items["medicine"]) < 1) return new CareResult { Ok = false, Reason = "none" };
            var gooby = J.EnsureDict(state, "gooby");
            var (h, ok) = UseMedicine(gooby["health"], nowMs);
            if (!ok) return new CareResult { Ok = false, Reason = "healthy" };
            items["medicine"] = (int)J.Num(items["medicine"]) - 1;
            gooby["health"] = h;
            var counters = Counters(state);
            counters["medsGiven"] = (int)J.Num(counters["medsGiven"]) + 1;
            counters["cures"] = (int)J.Num(counters["cures"]) + 1;
            return new CareResult { Ok = true, Reason = "" };
        }

        /// <summary>Pay the vet: kind "cure" (120 c, only queasy/sick) or "checkup" (30 c). Mutates the v5 state.</summary>
        public static CareResult PayVet(JObject state, string kind, long nowMs)
        {
            if (kind != "cure" && kind != "checkup") return new CareResult { Ok = false, Reason = "unknown" };
            var gooby = J.EnsureDict(state, "gooby");
            var health = Normalize(gooby["health"]);
            if (kind == "cure" && J.Str(health["state"]) == "healthy") return new CareResult { Ok = false, Reason = "healthy" };
            var price = kind == "cure" ? VetCurePrice : VetCheckupPrice;
            var econ = J.EnsureDict(state, "economy");
            if (!Economy.Spend(econ, price, "vet:" + kind)) return new CareResult { Ok = false, Reason = "coins" };
            if (kind == "cure")
            {
                gooby["health"] = VetCure(health, nowMs);
                gooby["stats"] = Stats.ApplyDeltas(J.Dict(gooby["stats"]), new Dictionary<string, double>
                {
                    { "hunger", VetCureStatBonus }, { "energy", VetCureStatBonus },
                    { "hygiene", VetCureStatBonus }, { "fun", VetCureStatBonus },
                });
                var counters = Counters(state);
                counters["cures"] = (int)J.Num(counters["cures"]) + 1;
            }
            else
            {
                gooby["health"] = VetCheckup(health);
            }
            return new CareResult { Ok = true, Reason = "", Total = price };
        }

        internal static JObject Counters(JObject state) => J.EnsureDict(J.EnsureDict(state, "achievements"), "counters");
    }
}
