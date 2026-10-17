using System;
using System.Collections.Generic;
using System.Linq;
using Gooby.Core.Json;
using Gooby.Core.State;
using Newtonsoft.Json.Linq;

namespace Gooby.Core.Meta
{
    /// <summary>
    /// Gooby's look back at every fifth level (port of recap_engine.gd + recap_service.gd ← web systems/recap.js): at
    /// levels 5, 10 … 40 the lowest open milestone is queued (recap.pendingLevel) and plays the next time Gooby is home;
    /// it shows what changed since the last look back — days, games played, coins earned, tickles, meals, harvests,
    /// stickers … (the 18-line catalogue, "days" always first, then by weight, at most 12 lines, zeros left out) — and
    /// finishing it writes a history row, resets the baseline and counts recapsSeen.
    /// Save: recap = {lastRecapLevel, baseline{}, baselineAt, pendingLevel, history[≤8]}.
    /// </summary>
    public static class Recap
    {
        public const int MilestoneStep = 5;
        public const int FirstMilestone = 5;
        public const int LastMilestone = 40;
        public const int HistoryMax = 8;
        public const int MaxLines = 12;
        public const int WeightAlwaysFirst = 999;
        public const double DayMs = 86_400_000.0;

        public static readonly (string id, int weight)[] Catalog =
        {
            ("days", WeightAlwaysFirst), ("games", 10), ("coinsEarned", 9), ("tickles", 9), ("feeds", 8), ("harvests", 8),
            ("stickers", 8), ("quests", 7), ("washes", 6), ("sleeps", 6), ("trips", 6), ("distance", 5), ("photos", 5),
            ("deliveries", 4), ("cures", 4), ("cakes", 4), ("nougat", 3), ("coinsSpent", 3),
        };

        private static readonly Dictionary<string, string> LineSource = new Dictionary<string, string>
        {
            ["games"] = "playsTotal", ["coinsEarned"] = "coinsEarned", ["tickles"] = "tickles", ["feeds"] = "feeds", ["harvests"] = "harvests",
            ["stickers"] = "stickerCount", ["quests"] = "questsDone", ["washes"] = "washes", ["sleeps"] = "sleeps", ["trips"] = "trips",
            ["distance"] = "distanceM", ["photos"] = "photos", ["deliveries"] = "deliveries", ["cures"] = "cures", ["cakes"] = "cakesServed",
            ["nougat"] = "nougatGlobs", ["coinsSpent"] = "coinsSpent",
        };

        private static readonly string[] SnapshotCounters =
        {
            "feeds", "washes", "sleeps", "tickles", "trips", "harvests", "plantings", "waterings", "questsDone", "deliveries", "cures",
            "nougatGlobs", "cakesServed", "surfRuns",
        };

        public sealed class Line
        {
            public string Id;
            public long N;
        }

        public static JObject SliceOf(JObject state)
        {
            var raw = state?["recap"] as JObject ?? new JObject();
            return new JObject
            {
                ["lastRecapLevel"] = Math.Max(0, J.Int(raw["lastRecapLevel"])),
                ["baseline"] = raw["baseline"] is JObject b ? b.DeepClone() : new JObject(),
                ["baselineAt"] = Math.Max(0.0, J.Num(raw["baselineAt"])),
                ["pendingLevel"] = Math.Max(0, J.Int(raw["pendingLevel"])),
                ["history"] = raw["history"] is JArray h ? h.DeepClone() : new JArray(),
            };
        }

        /// <summary>Highest milestone at or below a level (L23 → 20, L4 → 0) — the retro-safe start for old saves.</summary>
        public static int HighestMilestone(int level) => Math.Max(0, Math.Min(level, LastMilestone)) / MilestoneStep * MilestoneStep;

        /// <summary>The lowest open milestone crossed by prev → next (0 = none).</summary>
        public static int MilestoneCrossed(int prevLevel, int newLevel, int lastRecapLevel)
        {
            for (var m = FirstMilestone; m <= LastMilestone; m += MilestoneStep)
                if (m > lastRecapLevel && m > prevLevel && m <= newLevel) return m;
            return 0;
        }

        /// <summary>The numbers a look back compares (game totals, counters, stickers, economy, profile).</summary>
        public static JObject Snapshot(JObject state, double nowMs)
        {
            var economy = J.Dict(state?["economy"]);
            var profile = J.Dict(state?["profile"]);
            var counters = J.Dict(J.Path(state, "achievements", "counters"));
            var playsTotal = 0.0;
            foreach (var p in J.Dict(J.Path(state, "minigames", "plays")).Properties()) playsTotal += Math.Max(0.0, J.Num(p.Value));
            var snap = new JObject
            {
                ["at"] = nowMs,
                ["level"] = J.Int(J.Path(state, "progression", "level"), 1),
                ["coinsEarned"] = Math.Max(0.0, J.Num(economy["coinsEarned"])),
                ["coinsSpent"] = Math.Max(0.0, J.Num(economy["coinsSpent"])),
                ["distanceM"] = Math.Max(0.0, J.Num(profile["distanceM"])),
                ["photos"] = Math.Max(0.0, J.Num(profile["photos"]) + J.Num(counters["photosTaken"])),
                ["playsTotal"] = playsTotal,
                ["stickerCount"] = J.Dict(J.Path(state, "stickers", "unlocked")).Count,
            };
            foreach (var k in SnapshotCounters) snap[k] = Math.Max(0.0, J.Num(counters[k]));
            return snap;
        }

        /// <summary>The lines of a look back: days first, then by weight (catalogue order breaks ties), zeros out, ≤ 12.</summary>
        public static List<Line> Lines(JObject baseline, double baselineAt, JObject now, double nowMs)
        {
            var outp = new List<(Line line, int weight, int order)>();
            for (var i = 0; i < Catalog.Length; i++)
            {
                var (id, weight) = Catalog[i];
                long n;
                if (id == "days")
                {
                    var since = baselineAt > 0 ? baselineAt : J.Num(baseline?["at"]);
                    n = since > 0 ? (long)Math.Floor(Math.Max(0.0, nowMs - since) / DayMs) : 0;
                }
                else
                {
                    var key = LineSource[id];
                    n = (long)Math.Round(Math.Max(0.0, J.Num(now?[key]) - J.Num(baseline?[key])));
                }
                if (n <= 0) continue;
                outp.Add((new Line { Id = id, N = n }, weight, i));
            }
            return outp.OrderByDescending(x => x.weight).ThenBy(x => x.order).Take(MaxLines).Select(x => x.line).ToList();
        }

        /// <summary>A level change: queues the lowest open milestone crossed (kept if a lower one is already waiting).</summary>
        public static int QueueIfMilestone(GameStateStore gs, int prevLevel, int newLevel)
        {
            if (gs == null) return 0;
            var slice = SliceOf(gs.State);
            var milestone = MilestoneCrossed(prevLevel, newLevel, J.Int(slice["lastRecapLevel"]));
            if (milestone <= 0) return 0;
            var pending = J.Int(slice["pendingLevel"]);
            var queued = pending > 0 ? Math.Min(pending, milestone) : milestone;
            gs.Update(s =>
            {
                var r = SliceOf(s);
                r["pendingLevel"] = queued;
                if (J.Num(r["baselineAt"]) <= 0 && ((JObject)r["baseline"]).Count == 0)
                {
                    // first look back ever: measure from the save's creation (the web baseline starts at 0)
                    r["baselineAt"] = J.Num(J.Path(s, "meta", "createdAt"));
                }
                s["recap"] = r;
            });
            gs.NotifySliceChanged("recap");
            return queued;
        }

        public static int PendingLevel(GameStateStore gs) => J.Int(SliceOf(gs?.State)["pendingLevel"]);

        /// <summary>The look back for the pending milestone (or a given level): its lines against the stored baseline.</summary>
        public static List<Line> LinesFor(GameStateStore gs, double nowMs)
        {
            var slice = SliceOf(gs.State);
            return Lines((JObject)slice["baseline"], J.Num(slice["baselineAt"]), Snapshot(gs.State, nowMs), nowMs);
        }

        /// <summary>Finished watching: history row (≤ 8), lastRecapLevel, a fresh baseline, pendingLevel cleared, recapsSeen +1.</summary>
        public static void Complete(GameStateStore gs, int level, double nowMs)
        {
            if (gs == null || level <= 0) return;
            var lines = LinesFor(gs, nowMs);
            gs.Update(s =>
            {
                var r = SliceOf(s);
                var history = (JArray)r["history"];
                var stats = new JObject();
                foreach (var l in lines) stats[l.Id] = l.N;
                history.Add(new JObject { ["level"] = level, ["at"] = nowMs, ["stats"] = stats });
                while (history.Count > HistoryMax) history.RemoveAt(0);
                r["lastRecapLevel"] = Math.Max(J.Int(r["lastRecapLevel"]), level);
                r["baseline"] = Snapshot(s, nowMs);
                r["baselineAt"] = nowMs;
                var next = MilestoneCrossed(level, J.Int(J.Path(s, "progression", "level"), 1), level);
                r["pendingLevel"] = next;
                s["recap"] = r;
                var counters = J.EnsureDict(J.EnsureDict(s, "achievements"), "counters");
                counters["recapsSeen"] = J.Int(counters["recapsSeen"]) + 1;
            });
            gs.NotifySliceChanged("recap");
            gs.NotifySliceChanged("achievements");
        }
    }
}
