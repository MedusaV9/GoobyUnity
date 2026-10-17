using System;
using System.Collections.Generic;
using System.Linq;
using Gooby.Core.Content;
using Gooby.Core.Json;
using Gooby.Core.Life;
using Gooby.Core.State;
using Newtonsoft.Json.Linq;

namespace Gooby.Core.Meta
{
    /// <summary>Quest definition (content/quests/data/quests.json item). Kept as JObject-backed for pack flexibility.</summary>
    public sealed class QuestDef
    {
        public string Id, Kategorie, TitelKey, TextKey;
        public JObject Messung, Braucht;
        public int Ziel, Muenzen, Xp;
        public JObject Raw;

        public static QuestDef From(JObject o) => new QuestDef
        {
            Id = J.Str(o["id"]), Kategorie = J.Str(o["kategorie"]),
            Messung = o["messung"] as JObject ?? new JObject(), Braucht = o["braucht"] as JObject,
            Ziel = J.Int(o["ziel"], 1), Muenzen = J.Int(o["muenzen"]), Xp = J.Int(o["xp"]),
            TitelKey = J.Str(o["titel_key"]), TextKey = J.Str(o["text_key"]), Raw = o,
        };

        public string TitleKey => TitelKey.Length > 0 ? TitelKey : $"quests.q.{Id}.titel";
        public string BodyKey => TextKey.Length > 0 ? TextKey : $"quests.q.{Id}.text";
    }

    /// <summary>Roll context for the `braucht` filter: level, unlocked minigames, garden.</summary>
    public sealed class QuestCtx
    {
        public int Level = 1;
        public HashSet<string> Minigames = new HashSet<string>();
        public bool Garden = true;
    }

    /// <summary>Port of quest_catalog.gd — the built-in pool from the quests content pack.</summary>
    public static class DailyQuestCatalog
    {
        public const string DataPath = "content/quests/data/quests";
        private static List<QuestDef> _cache;

        public static IReadOnlyList<QuestDef> Pool()
        {
            if (_cache != null) return _cache;
            var items = new List<QuestDef>();
            foreach (var t in ContentSource.LoadItems(DataPath)) if (t is JObject o) items.Add(QuestDef.From(o));
            return _cache = items;
        }

        public static void ResetCache() => _cache = null;
    }

    /// <summary>
    /// Port of quest_engine.gd — deterministic daily board (mulberry32(hash32(localDay)), web-identical), baseline-frozen
    /// progress measurement, once-per-day reroll of untouched quests, claim + completion bonus bookkeeping.
    /// </summary>
    public static class DailyQuestEngine
    {
        public const int QuestsPerDay = 3;
        public const int MinCategories = 2;
        public const int BonusCoins = 50;
        public const int BonusXp = 25;

        public static bool RequiresMet(JObject braucht, QuestCtx ctx)
        {
            if (braucht == null || !braucht.HasValues) return true;
            if (braucht["minigame"] != null && !ctx.Minigames.Contains(J.Str(braucht["minigame"]))) return false;
            if (J.IsTrue(braucht["garden"]) && !ctx.Garden) return false;
            if (braucht["level"] != null && ctx.Level < J.Int(braucht["level"])) return false;
            return true;
        }

        public static List<QuestDef> EligibleDefs(IEnumerable<QuestDef> pool, QuestCtx ctx) => pool.Where(d => RequiresMet(d.Braucht, ctx)).ToList();

        public static bool RollNeeded(JObject slice, string day)
        {
            if (J.Str(slice["day"]) != day) return true;
            return !(slice["active"] is JArray active) || active.Count == 0;
        }

        public static bool RollToday(JObject slice, string day, IReadOnlyList<QuestDef> pool, QuestCtx ctx, JObject state)
        {
            if (!RollNeeded(slice, day)) return false;
            var rng = new Rng(Hash32(day));
            var picks = PickSet(rng, EligibleDefs(pool, ctx), new List<QuestDef>(), QuestsPerDay);
            var active = new JArray();
            foreach (var def in picks) active.Add(MakeEntry(def, state));
            slice["day"] = day;
            slice["active"] = active;
            slice["bonusDay"] = J.Str(slice["bonusDay"]);
            return true;
        }

        public static bool RerollToday(JObject slice, string day, IReadOnlyList<QuestDef> pool, QuestCtx ctx, JObject state)
        {
            if (J.Str(slice["day"]) != day || J.Str(slice["rerolledDay"]) == day) return false;
            var byId = PoolById(pool);
            var active = slice["active"] as JArray ?? new JArray();
            var replaceIdx = new List<int>();
            var kept = new List<QuestDef>();
            var takenIds = new HashSet<string>();
            for (var i = 0; i < active.Count; i++)
            {
                if (!(active[i] is JObject entry)) { replaceIdx.Add(i); continue; }
                var id = J.Str(entry["id"]);
                takenIds.Add(id);
                byId.TryGetValue(id, out var def);
                var untouched = !J.IsTrue(entry["claimed"]) && (def == null || ProgressOf(entry, def, state) <= 0);
                if (untouched) replaceIdx.Add(i);
                else if (def != null) kept.Add(def);
            }
            if (replaceIdx.Count == 0) return false;
            var eligible = EligibleDefs(pool, ctx).Where(d => !takenIds.Contains(d.Id)).ToList();
            var picks = PickSet(new Rng(Hash32(day + ":r")), eligible, kept, replaceIdx.Count);
            for (var k = 0; k < picks.Count; k++) active[replaceIdx[k]] = MakeEntry(picks[k], state);
            slice["active"] = active;
            slice["rerolledDay"] = day;
            return true;
        }

        public static JObject MakeEntry(QuestDef def, JObject state) => new JObject { ["id"] = def.Id, ["claimed"] = false, ["base"] = BaselineOf(def, state) };

        public static JObject BaselineOf(QuestDef def, JObject state)
        {
            var m = def.Messung;
            switch (J.Str(m["typ"]))
            {
                case "counter": return new JObject { ["n"] = Counter(state, J.Str(m["key"])) };
                case "spiele_gesamt": return new JObject { ["n"] = PlaysSum(state) };
                case "spiele_verschieden": return new JObject { ["m"] = PlaysMap(state) };
                case "spiel_runden":
                case "spiel_punkte": return new JObject { ["n"] = PlaysOf(state, J.Str(m["spiel"])) };
                case "muenzen_verdient": return new JObject { ["n"] = EconInt(state, "coinsEarned") };
                case "muenzen_ausgegeben": return new JObject { ["n"] = EconInt(state, "coinsSpent") };
            }
            return new JObject();
        }

        public static int ProgressOf(JObject entry, QuestDef def, JObject state)
        {
            var ziel = TargetOf(def);
            var b = entry["base"] as JObject ?? new JObject();
            return J.ClampI(RawProgress(def.Messung, b, state, ziel), 0, ziel);
        }

        public static bool IsComplete(JObject entry, QuestDef def, JObject state) => ProgressOf(entry, def, state) >= TargetOf(def);

        /// <summary>QA: the quest counts as done — its baseline moves back by the goal (counter-like measurements only).</summary>
        public static bool QaFinish(JObject entry, QuestDef def, JObject state)
        {
            var m = def.Messung;
            int now;
            switch (J.Str(m["typ"]))
            {
                case "counter": now = Counter(state, J.Str(m["key"])); break;
                case "spiele_gesamt": now = PlaysSum(state); break;
                case "spiel_runden": now = PlaysOf(state, J.Str(m["spiel"])); break;
                case "muenzen_verdient": now = EconInt(state, "coinsEarned"); break;
                case "muenzen_ausgegeben": now = EconInt(state, "coinsSpent"); break;
                default: return false;
            }
            entry["base"] = new JObject { ["n"] = now - TargetOf(def) };
            return true;
        }

        public static int TargetOf(QuestDef def) => Math.Max(1, def.Ziel);

        public struct ClaimResult
        {
            public bool Ok;
            public int Muenzen, Xp;
        }

        public static ClaimResult Claim(JObject slice, string id, QuestDef def, JObject state)
        {
            var entry = EntryOf(slice, id);
            if (entry == null || def == null) return default;
            if (J.IsTrue(entry["claimed"]) || !IsComplete(entry, def, state)) return default;
            entry["claimed"] = true;
            slice["completedTotal"] = (int)J.NumOr(slice["completedTotal"], 0.0) + 1;
            return new ClaimResult { Ok = true, Muenzen = Math.Max(0, def.Muenzen), Xp = Math.Max(0, def.Xp) };
        }

        public static bool AllClaimed(JObject slice)
        {
            if (!(slice["active"] is JArray active) || active.Count < QuestsPerDay) return false;
            return active.All(e => e is JObject o && J.IsTrue(o["claimed"]));
        }

        public static bool BonusDue(JObject slice, string day) => AllClaimed(slice) && J.Str(slice["bonusDay"]) != day;

        public static void MarkBonusPaid(JObject slice, string day) => slice["bonusDay"] = day;

        public static int ClaimableCount(JObject slice, IReadOnlyDictionary<string, QuestDef> byId, JObject state)
        {
            var n = 0;
            foreach (var e in slice["active"] as JArray ?? new JArray())
            {
                if (!(e is JObject entry) || J.IsTrue(entry["claimed"])) continue;
                if (byId.TryGetValue(J.Str(entry["id"]), out var def) && IsComplete(entry, def, state)) n++;
            }
            return n;
        }

        public static Dictionary<string, QuestDef> PoolById(IEnumerable<QuestDef> pool)
        {
            var map = new Dictionary<string, QuestDef>();
            foreach (var d in pool) map[d.Id] = d;
            return map;
        }

        /// <summary>Web hash32 (murmur-style string mix) — seeds the daily board identically on every platform.</summary>
        public static uint Hash32(string text)
        {
            var h = (uint)(1779033703 ^ text.Length);
            foreach (var ch in text)
            {
                h = Imul32(h ^ ch, 3432918353u);
                h = (h << 13) | (h >> 19);
            }
            h = Imul32(h ^ (h >> 16), 2246822507u);
            h = Imul32(h ^ (h >> 13), 3266489909u);
            return h ^ (h >> 16);
        }

        private static uint Imul32(uint a, uint b) => unchecked(a * b);

        /// <summary>mulberry32 stream (same as GoobyRng but seeded from hash32 here).</summary>
        public sealed class Rng
        {
            private uint _a;
            public Rng(uint seed) { _a = seed; }

            public double Next()
            {
                _a = unchecked(_a + 0x6D2B79F5u);
                var t = Imul32(_a ^ (_a >> 15), _a | 1u);
                t = unchecked(t + Imul32(t ^ (t >> 7), t | 61u)) ^ t;
                return (t ^ (t >> 14)) / 4294967296.0;
            }
        }

        private static List<QuestDef> PickSet(Rng rng, List<QuestDef> eligible, List<QuestDef> kept, int count)
        {
            var chosen = new List<QuestDef>();
            var taken = new HashSet<string>(kept.Select(d => d.Id));
            for (var i = 0; i < count; i++)
            {
                var candidates = eligible.Where(d => !taken.Contains(d.Id)).ToList();
                if (candidates.Count == 0) break;
                if (i == count - 1) candidates = PreferNewCategory(candidates, kept.Concat(chosen).ToList());
                var pick = candidates[(int)(rng.Next() * candidates.Count) % candidates.Count];
                chosen.Add(pick);
                taken.Add(pick.Id);
            }
            return chosen;
        }

        private static List<QuestDef> PreferNewCategory(List<QuestDef> candidates, List<QuestDef> board)
        {
            var cats = new HashSet<string>(board.Select(d => d.Kategorie));
            if (cats.Count == 0 || cats.Count >= MinCategories) return candidates;
            var fresh = candidates.Where(d => !cats.Contains(d.Kategorie)).ToList();
            return fresh.Count > 0 ? fresh : candidates;
        }

        private static int RawProgress(JObject m, JObject b, JObject state, int ziel)
        {
            var baseN = (int)J.NumOr(b["n"], 0.0);
            switch (J.Str(m["typ"]))
            {
                case "counter": return Counter(state, J.Str(m["key"])) - baseN;
                case "streicheln_heute": return Counter(state, "petsToday");
                case "spiele_gesamt": return PlaysSum(state) - baseN;
                case "spiele_verschieden": return DistinctNewPlays(state, b);
                case "spiel_runden": return PlaysOf(state, J.Str(m["spiel"])) - baseN;
                case "spiel_punkte":
                {
                    var spiel = J.Str(m["spiel"]);
                    if (PlaysOf(state, spiel) <= baseN) return 0;
                    return Math.Min(BestOf(state, spiel), ziel);
                }
                case "muenzen_verdient": return EconInt(state, "coinsEarned") - baseN;
                case "muenzen_ausgegeben": return EconInt(state, "coinsSpent") - baseN;
            }
            return 0;
        }

        private static int Counter(JObject state, string key) => (int)J.NumOr(J.Path(state, "achievements", "counters", key), 0.0);

        private static JObject PlaysMap(JObject state)
        {
            var outp = new JObject();
            if (J.Path(state, "minigames", "plays") is JObject plays)
                foreach (var p in plays.Properties()) outp[p.Name] = (int)J.NumOr(p.Value, 0.0);
            return outp;
        }

        private static int PlaysSum(JObject state) => PlaysMap(state).Properties().Sum(p => J.Int(p.Value));

        private static int PlaysOf(JObject state, string spiel) => (int)J.NumOr(J.Path(state, "minigames", "plays", spiel), 0.0);

        private static int DistinctNewPlays(JObject state, JObject b)
        {
            var before = b["m"] as JObject ?? new JObject();
            var now = PlaysMap(state);
            return now.Properties().Count(p => J.Int(p.Value) > (int)J.NumOr(before[p.Name], 0.0));
        }

        private static int BestOf(JObject state, string spiel)
        {
            var best = (int)J.NumOr(J.Path(state, "minigames", "legacy", "best", spiel), 0.0);
            best = Math.Max(best, (int)J.NumOr(J.Path(state, "minigames", "legacy", "endlessBest", spiel), 0.0));
            if (J.Path(state, "minigames", "legacy", "bestByDiff", spiel) is JObject byDiff)
                foreach (var p in byDiff.Properties()) best = Math.Max(best, (int)J.NumOr(p.Value, 0.0));
            return best;
        }

        private static int EconInt(JObject state, string key) => (int)J.NumOr(J.Path(state, "economy", key), 0.0);

        private static JObject EntryOf(JObject slice, string id)
        {
            foreach (var e in slice["active"] as JArray ?? new JArray())
                if (e is JObject o && J.Str(o["id"]) == id) return o;
            return null;
        }
    }

    /// <summary>Store-facing part of quest_service.gd: today's roll, board rows, claim (+bonus), reroll.</summary>
    public sealed class DailyQuestService
    {
        private readonly GameStateStore _gs;
        private readonly Func<QuestCtx> _ctx;

        public event Action QuestsChanged;

        public DailyQuestService(GameStateStore gs, Func<QuestCtx> ctx)
        {
            _gs = gs;
            _ctx = ctx;
        }

        public IReadOnlyList<QuestDef> Pool => DailyQuestCatalog.Pool();

        public string Today => _gs.Clock.LocalDay();

        private static JObject SliceOf(JObject state) => J.EnsureDict(state, "quests");

        public void EnsureRoll()
        {
            var day = Today;
            var ctx = _ctx();
            var rolled = false;
            _gs.Update(state => rolled = DailyQuestEngine.RollToday(SliceOf(state), day, Pool, ctx, state));
            if (rolled)
            {
                _gs.NotifySliceChanged("quests");
                QuestsChanged?.Invoke();
            }
        }

        public sealed class Row
        {
            public QuestDef Def;
            public JObject Entry;
            public int Progress, Target;
            public bool Complete, Claimed;
        }

        public List<Row> Board()
        {
            var rows = new List<Row>();
            var byId = DailyQuestEngine.PoolById(Pool);
            var slice = SliceOf(_gs.State);
            foreach (var e in slice["active"] as JArray ?? new JArray())
            {
                if (!(e is JObject entry) || !byId.TryGetValue(J.Str(entry["id"]), out var def)) continue;
                rows.Add(new Row
                {
                    Def = def, Entry = entry,
                    Progress = DailyQuestEngine.ProgressOf(entry, def, _gs.State), Target = DailyQuestEngine.TargetOf(def),
                    Complete = DailyQuestEngine.IsComplete(entry, def, _gs.State), Claimed = J.IsTrue(entry["claimed"]),
                });
            }
            return rows;
        }

        /// <summary>
        /// Board 05 quest card: unclaimed garden dailies first (Gieß-Runde / water4, then plant2 / harvest2).
        /// Other dailies (memory, care, …) stay on the home HUD and do not steal this slot.
        /// </summary>
        public static Row PickGardenCard(IEnumerable<Row> board)
        {
            var rows = board?.Where(r => r != null && r.Def != null && !r.Claimed).ToList() ?? new List<Row>();
            foreach (var id in new[] { "water4", "plant2", "harvest2" })
            {
                var hit = rows.FirstOrDefault(r => r.Def.Id == id);
                if (hit != null) return hit;
            }
            return rows.FirstOrDefault(r => r.Def.Kategorie == "garden");
        }

        /// <summary>Fallback progress when water4 is not on today's board (counter / water4.ziel).</summary>
        public (int progress, int target) WateringRound()
        {
            var def = DailyQuestCatalog.Pool().FirstOrDefault(d => d.Id == "water4");
            var target = def != null ? DailyQuestEngine.TargetOf(def) : 4;
            var progress = J.Int(J.Path(_gs.State, "achievements", "counters", "waterings"));
            return (progress, target);
        }

        public int ClaimableCount() => DailyQuestEngine.ClaimableCount(SliceOf(_gs.State), DailyQuestEngine.PoolById(Pool), _gs.State);

        public bool AllClaimed() => DailyQuestEngine.AllClaimed(SliceOf(_gs.State));

        public bool RerollAvailable()
        {
            var slice = SliceOf(_gs.State);
            return J.Str(slice["day"]) == Today && J.Str(slice["rerolledDay"]) != Today;
        }

        public sealed class ClaimOutcome
        {
            public bool Ok;
            public int Muenzen, Xp;
            public bool BonusPaid;
        }

        public ClaimOutcome Claim(string id)
        {
            var day = Today;
            DailyQuestEngine.PoolById(Pool).TryGetValue(id, out var def);
            var result = default(DailyQuestEngine.ClaimResult);
            _gs.Update(state =>
            {
                result = DailyQuestEngine.Claim(SliceOf(state), id, def, state);
                if (!result.Ok) return;
                Pay(state, result.Muenzen, result.Xp, day, "quest");
                var counters = J.EnsureDict(J.EnsureDict(state, "achievements"), "counters");
                counters["questsDone"] = J.Int(counters["questsDone"]) + 1;
            });
            if (!result.Ok) return new ClaimOutcome { Ok = false };
            _gs.NotifySliceChanged("quests");
            var bonus = MaybePayBonus(day);
            QuestsChanged?.Invoke();
            return new ClaimOutcome { Ok = true, Muenzen = result.Muenzen, Xp = result.Xp, BonusPaid = bonus };
        }

        private bool MaybePayBonus(string day)
        {
            var due = false;
            _gs.Update(state =>
            {
                var slice = SliceOf(state);
                due = DailyQuestEngine.BonusDue(slice, day);
                if (!due) return;
                DailyQuestEngine.MarkBonusPaid(slice, day);
                Pay(state, DailyQuestEngine.BonusCoins, DailyQuestEngine.BonusXp, day, "questBonus");
            });
            if (due) _gs.NotifySliceChanged("quests");
            return due;
        }

        /// <summary>QA: the first unclaimed quest of today that can be measured by a counter counts as done.</summary>
        public bool QaFinishFirst()
        {
            var byId = DailyQuestEngine.PoolById(Pool);
            var done = false;
            _gs.Update(state =>
            {
                foreach (var e in SliceOf(state)["active"] as JArray ?? new JArray())
                {
                    if (!(e is JObject entry) || J.IsTrue(entry["claimed"]) || !byId.TryGetValue(J.Str(entry["id"]), out var def)) continue;
                    if (DailyQuestEngine.IsComplete(entry, def, state)) continue;
                    if (DailyQuestEngine.QaFinish(entry, def, state)) { done = true; break; }
                }
            });
            if (done) { _gs.NotifySliceChanged("quests"); QuestsChanged?.Invoke(); }
            return done;
        }

        public bool Reroll()
        {
            var day = Today;
            var ctx = _ctx();
            var done = false;
            _gs.Update(state => done = DailyQuestEngine.RerollToday(SliceOf(state), day, Pool, ctx, state));
            if (done)
            {
                _gs.NotifySliceChanged("quests");
                QuestsChanged?.Invoke();
            }
            return done;
        }

        private static void Pay(JObject state, int coins, int xp, string day, string reason)
        {
            var econ = J.EnsureDict(state, "economy");
            if (coins > 0) Economy.Award(econ, coins, reason, day);
            if (xp <= 0) return;
            var prog = J.EnsureDict(state, "progression");
            var res = Leveling.ApplyXp(J.NumOr(prog["xp"], 0.0), (int)J.NumOr(prog["level"], 1.0), xp);
            prog["xp"] = res.Xp;
            prog["level"] = res.Level;
            if (res.CoinsAwarded > 0) Economy.Award(econ, res.CoinsAwarded, "levelUp", day);
        }
    }
}
