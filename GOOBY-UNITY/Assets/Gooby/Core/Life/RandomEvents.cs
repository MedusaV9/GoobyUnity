using System;
using System.Collections.Generic;
using Gooby.Core.Content;
using Gooby.Core.Json;
using Newtonsoft.Json.Linq;

namespace Gooby.Core.Life
{
    /// <summary>
    /// Random events at home (port of random_events.gd, W3d CONTENT, Doc F §4): data from the pack content/events — each
    /// def has a weight, a cooldown in days, a trigger window ("HH:MM"–"HH:MM", may wrap past midnight), a probability, a
    /// notification text, a timeout (minutes or a [min, max] range), a fail text and a reward (stat + value). Rolled on
    /// app start/resume; at most one event is active; a missed one leaves the fail line for the next start ("Gooby hat
    /// es schon alleine hingekriegt -_-"). Defs carry an optional context ("home" default, "ranch" for the ranch host).
    /// Pure statics on the "events" slice; time and rolls are handed in.
    /// </summary>
    public static class RandomEvents
    {
        public const string SliceId = "events";
        public const string DefsPath = "content/events/data/events";
        public const long MsPerMin = 60_000L, MsPerDay = 86_400_000L;

        public static List<JObject> Defs()
        {
            var list = new List<JObject>();
            foreach (var t in ContentSource.LoadItems(DefsPath)) if (t is JObject o) list.Add(o);
            return list;
        }

        public static JObject DefById(IEnumerable<JObject> defs, string id)
        {
            foreach (var d in defs) if (J.Str(d["id"]) == id) return d;
            return null;
        }

        /// <summary>The events slice with every field in place.</summary>
        public static JObject Slice(JObject state)
        {
            var s = J.EnsureDict(state, SliceId);
            if (!(s["active"] is JObject)) s["active"] = new JObject();
            if (!(s["cooldowns"] is JObject)) s["cooldowns"] = new JObject();
            if (!J.IsString(s["failPending"])) s["failPending"] = "";
            s["resolvedTotal"] = Math.Max(0, J.Int(s["resolvedTotal"]));
            return s;
        }

        public static JObject Active(JObject state) => Slice(state)["active"] as JObject;

        public static bool HasActive(JObject state) => Active(state).HasValues;

        // ── the pure core ──

        /// <summary>"HH:MM" → minutes of the day (−1 for a broken value).</summary>
        public static int ParseMinutes(string hhmm)
        {
            var parts = (hhmm ?? "").Split(':');
            if (parts.Length != 2 || !int.TryParse(parts[0], out var h) || !int.TryParse(parts[1], out var m)) return -1;
            return h < 0 || h > 23 || m < 0 || m > 59 ? -1 : h * 60 + m;
        }

        /// <summary>Inside the trigger window (a window that wraps past midnight, e.g. 22:30–03:00, works; no window = always).</summary>
        public static bool WindowContains(JArray window, int minutesOfDay)
        {
            if (window == null || window.Count != 2) return true;
            var start = ParseMinutes(J.Str(window[0]));
            var end = ParseMinutes(J.Str(window[1]));
            if (start < 0 || end < 0) return true;
            return start <= end ? minutesOfDay >= start && minutesOfDay <= end : minutesOfDay >= start || minutesOfDay <= end;
        }

        public static bool IsAvailable(JObject def, JObject slice, long nowMs, int minutesOfDay, string context = "home")
        {
            if (J.Str(def["context"], "home") != context) return false;
            if ((slice["active"] as JObject)?.HasValues == true) return false;
            if (!WindowContains(def["trigger_window"] as JArray, minutesOfDay)) return false;
            return nowMs >= J.Long((slice["cooldowns"] as JObject)?[J.Str(def["id"])]);
        }

        /// <summary>A weighted pick among the available defs, then its probability gate (null: nothing happens).</summary>
        public static JObject PickEvent(IEnumerable<JObject> defs, JObject slice, long nowMs, int minutesOfDay, double rollPick, double rollGate, string context = "home")
        {
            var available = new List<JObject>();
            var total = 0.0;
            foreach (var d in defs)
            {
                if (!IsAvailable(d, slice, nowMs, minutesOfDay, context)) continue;
                available.Add(d);
                total += Math.Max(0.0, J.NumOr(d["weight"], 1.0));
            }
            if (available.Count == 0 || total <= 0.0) return null;
            var at = Math.Min(Math.Max(rollPick, 0.0), 0.999999) * total;
            var chosen = available[0];
            foreach (var d in available)
            {
                var w = Math.Max(0.0, J.NumOr(d["weight"], 1.0));
                if (at < w) { chosen = d; break; }
                at -= w;
            }
            return rollGate >= J.NumOr(chosen["wahrscheinlichkeit"], 1.0) ? null : chosen;
        }

        public static long TimeoutDeadline(JObject def, long startedMs, double roll01 = 0.0)
        {
            double minutes;
            if (def["timeout_min"] is JArray range && range.Count == 2)
            {
                var lo = Math.Max(1.0, J.Num(range[0]));
                var hi = Math.Max(lo, J.Num(range[1]));
                minutes = lo + (hi - lo) * Math.Min(Math.Max(roll01, 0.0), 1.0);
            }
            else minutes = Math.Max(1.0, J.NumOr(def["timeout_min"], 8.0));
            return startedMs + (long)(minutes * MsPerMin);
        }

        public static bool IsTimedOut(JObject active, long nowMs) => active != null && active.HasValues && nowMs >= J.Long(active["timeout_ms"]);

        public static long CooldownUntil(JObject def, long nowMs) => nowMs + (long)(Math.Max(0.0, J.NumOr(def["cooldown_days"], 1.0)) * MsPerDay);

        // ── state glue (mutates the save the caller hands in) ──

        /// <summary>
        /// App start/resume: a timed-out event fails (its line waits for the next look), else maybe a new one starts.
        /// Returns the def that just started (null when nothing did).
        /// </summary>
        public static JObject RollOnStart(JObject state, IList<JObject> defs, long nowMs, int minutesOfDay, double rollPick, double rollGate, double rollTimeout, string context = "home")
        {
            var slice = Slice(state);
            var active = slice["active"] as JObject;
            if (active.HasValues)
            {
                if (IsTimedOut(active, nowMs)) Fail(state, defs);
                return null;
            }
            var chosen = PickEvent(defs, slice, nowMs, minutesOfDay, rollPick, rollGate, context);
            if (chosen == null) return null;
            Activate(state, chosen, nowMs, rollTimeout);
            return chosen;
        }

        public static void Activate(JObject state, JObject def, long nowMs, double timeoutRoll = 0.0)
        {
            var slice = Slice(state);
            var id = J.Str(def["id"]);
            slice["active"] = new JObject
            {
                ["id"] = id, ["started_ms"] = nowMs, ["timeout_ms"] = TimeoutDeadline(def, nowMs, timeoutRoll), ["szene"] = J.Str(def["szene_setup"]),
            };
            ((JObject)slice["cooldowns"])[id] = CooldownUntil(def, nowMs);
        }

        /// <summary>Helped in time: the event ends and its reward lands on Gooby's stat (clamped 0–100). Returns the def.</summary>
        public static JObject Resolve(JObject state, IList<JObject> defs)
        {
            var slice = Slice(state);
            var def = DefById(defs, J.Str((slice["active"] as JObject)?["id"]));
            if (def == null) return null;
            slice["active"] = new JObject();
            slice["resolvedTotal"] = J.Int(slice["resolvedTotal"]) + 1;
            if (def["reward"] is JObject reward)
            {
                var stats = J.EnsureDict(J.EnsureDict(state, "gooby"), "stats");
                var stat = J.Str(reward["stat"], "fun");
                stats[stat] = Math.Max(0.0, Math.Min(100.0, J.Num(stats[stat]) + J.Num(reward["wert"])));
            }
            return def;
        }

        /// <summary>Too late: the event ends without a reward and leaves its fail line for the next look.</summary>
        public static void Fail(JObject state, IList<JObject> defs)
        {
            var slice = Slice(state);
            var def = DefById(defs, J.Str((slice["active"] as JObject)?["id"]));
            slice["active"] = new JObject();
            if (def != null) slice["failPending"] = J.Str(def["fail_text_de"], "Gooby hat es schon alleine hingekriegt -_-");
        }

        /// <summary>The waiting fail line (once; empty when there is none).</summary>
        public static string TakeFailNotice(JObject state)
        {
            var slice = Slice(state);
            var text = J.Str(slice["failPending"]);
            slice["failPending"] = "";
            return text;
        }
    }
}
