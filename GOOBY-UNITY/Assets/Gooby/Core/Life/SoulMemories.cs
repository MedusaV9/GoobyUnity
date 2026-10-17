using System;
using System.Collections.Generic;
using System.Linq;
using Gooby.Core.Json;
using Newtonsoft.Json.Linq;

namespace Gooby.Core.Life
{
    /// <summary>
    /// Gooby's personal memories (port of soul_memories.gd, FB-6/SEELE + SEELE-2 + W20): he only mentions things that
    /// REALLY happened — every candidate is built from save data (records, holidays, counters, streaks, favourite game
    /// and food, the market stall, ghosts, souvenirs …); no data, no memory. Plus his four small wishes ("I always
    /// wanted to …") whose conditions read the same real data. Pure statics: the state and time are handed in, the pick
    /// is a roll in 0..1. Titles resolve through the strings table when one is given (else the raw id, never empty).
    /// </summary>
    public static class SoulMemories
    {
        public const long MemoryCooldownMs = 3 * Soul.MsPerDay;

        public const int MinTickles = 20;
        public const int MinHarvests = 10;
        public const int MinStreak = 3;
        public const int MinPlaytimeMin = 600;
        public const int MinVorliebeSpiele = 2;
        public const int MinVorliebeEssen = 3;
        public const int MinGoobyeUmsatz = 100;
        public const int MinSchichten = 3;
        public const int MinBestwert = 80;
        public const int MinFunde = 3;
        public const int MinGeister = 2;
        public const int MinMitbringsel = 2;

        /// <summary>Gooby's small own goals; the order is the stable pick base.</summary>
        public static readonly string[] WunschIds = { "funkelpark", "urlaub", "streak7", "kissenturm" };

        public sealed class Memory
        {
            public string Id, TextKey;
            public readonly Dictionary<string, object> Args = new Dictionary<string, object>();
        }

        /// <summary>Every memory possible right now, in a stable (deterministic) order.</summary>
        public static List<Memory> Candidates(JObject state, Gooby.Core.I18n.I18n strings = null)
        {
            var outp = new List<Memory>();
            if (state == null) return outp;
            AddBestMinigame(outp, state, strings);
            AddVacation(outp, state, strings);
            AddCounters(outp, state);
            AddStreak(outp, state);
            AddPark(outp, state);
            AddPlaytime(outp, state);
            AddVorlieben(outp, state, strings);
            AddGoobye(outp, state);
            AddMcGooby(outp, state);
            AddRanchWelt(outp, state, strings);
            AddGeist(outp, state, strings);
            AddHeimkehr(outp, state, strings);
            AddSpotlight(outp, state);
            return outp;
        }

        /// <summary>A candidate not shown for <see cref="MemoryCooldownMs"/>, picked by roll; null when all are fresh.</summary>
        public static Memory Pick(IList<Memory> candidates, JObject shownAt, long nowMs, double roll)
        {
            var allowed = candidates.Where(c =>
            {
                var last = J.Long(shownAt?[c.Id]);
                return last <= 0 || nowMs - last >= MemoryCooldownMs;
            }).ToList();
            if (allowed.Count == 0) return null;
            return allowed[(int)(Math.Min(Math.Max(roll, 0.0), 0.999999) * allowed.Count)];
        }

        // ── wishes ──

        /// <summary>Is this wish still OPEN (its condition read from real data)?</summary>
        public static bool WunschOffen(JObject state, string wunschId)
        {
            switch (wunschId)
            {
                case "funkelpark":
                    return (int)J.Num(J.Path(state, "park", "visits")) <= 0;
                case "urlaub":
                    return !(J.Path(state, "vacation", "visited") is JObject visited) || !visited.HasValues;
                case "streak7":
                    return (int)J.Num(J.Path(state, "daily", "streak")) < 7;
                case "kissenturm":
                    return !(J.Path(state, "soul", "surpriseAt") is JObject seen) || seen["sup_turm"] == null;
            }
            return false;
        }

        public static bool WunschErfuellt(JObject state, string wunschId) =>
            Array.IndexOf(WunschIds, wunschId) >= 0 && !WunschOffen(state, wunschId);

        /// <summary>Every wish Gooby could form NOW: open and never celebrated as fulfilled (stable order).</summary>
        public static List<string> OffeneWuensche(JObject state, JObject slice)
        {
            var gefeiert = slice?["wunschErfuellt"] as JObject;
            return WunschIds.Where(id => WunschOffen(state, id) && gefeiert?[id] == null).ToList();
        }

        // ── sources ──

        private static Memory M(string id, string textKey, params (string key, object value)[] args)
        {
            var m = new Memory { Id = id, TextKey = textKey };
            foreach (var (key, value) in args) m.Args[key] = value;
            return m;
        }

        /// <summary>The highest score of a {id: score} board (sorted ids, first wins a tie); ("", 0) when none counts.</summary>
        private static (string id, int score) Top(JObject board)
        {
            var topId = "";
            var top = 0;
            foreach (var id in board.Properties().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal))
            {
                var score = (int)J.Num(board[id]);
                if (score > top) { top = score; topId = id; }
            }
            return (topId, top);
        }

        private static void AddBestMinigame(List<Memory> outp, JObject state, Gooby.Core.I18n.I18n strings)
        {
            if (!(J.Path(state, "minigames", "legacy", "best") is JObject best) || !best.HasValues) return;
            var (id, score) = Top(best);
            if (id == "" || score <= 0) return;
            outp.Add(M("rekord_" + id, "soul.erinnerung.rekord", ("spiel", GameTitle(id, strings)), ("punkte", score)));
        }

        private static void AddVacation(List<Memory> outp, JObject state, Gooby.Core.I18n.I18n strings)
        {
            if (!(J.Path(state, "vacation", "visited") is JObject visited) || !visited.HasValues) return;
            var dest = visited.Properties().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal).First();
            outp.Add(M("urlaub_" + dest, "soul.erinnerung.urlaub", ("ziel", DestTitle(dest, strings))));
        }

        private static void AddCounters(List<Memory> outp, JObject state)
        {
            if (!(J.Path(state, "achievements", "counters") is JObject counters)) return;
            var tickles = (int)J.Num(counters["tickles"]);
            if (tickles >= MinTickles) outp.Add(M("kitzeln", "soul.erinnerung.kitzeln", ("anzahl", tickles)));
            var harvests = (int)J.Num(counters["harvests"]);
            if (harvests >= MinHarvests) outp.Add(M("garten", "soul.erinnerung.garten", ("anzahl", harvests)));
        }

        private static void AddStreak(List<Memory> outp, JObject state)
        {
            var streak = (int)J.Num(J.Path(state, "daily", "streak"));
            if (streak >= MinStreak) outp.Add(M("streak", "soul.erinnerung.streak", ("tage", streak)));
        }

        private static void AddPark(List<Memory> outp, JObject state)
        {
            var visits = (int)J.Num(J.Path(state, "park", "visits"));
            if (visits > 0) outp.Add(M("funkelpark", "soul.erinnerung.funkelpark", ("anzahl", visits)));
        }

        private static void AddPlaytime(List<Memory> outp, JObject state)
        {
            var minutes = (int)J.Num(J.Path(state, "profile", "playtimeMin"));
            if (minutes >= MinPlaytimeMin) outp.Add(M("spielzeit", "soul.erinnerung.spielzeit", ("stunden", minutes / 60)));
        }

        /// <summary>Favourites from real behaviour: the best of ≥2 played games, the most-fed food (≥3 times).</summary>
        private static void AddVorlieben(List<Memory> outp, JObject state, Gooby.Core.I18n.I18n strings)
        {
            if (J.Path(state, "minigames", "legacy", "best") is JObject best && best.Count >= MinVorliebeSpiele)
            {
                var (id, _) = Top(best);
                if (id != "") outp.Add(M("vorliebe_spiel", "soul.erinnerung.vorliebe_spiel", ("spiel", GameTitle(id, strings))));
            }
            if (J.Path(state, "soul", "foodGiven") is JObject food)
            {
                var (foodId, count) = Top(food);
                if (count >= MinVorliebeEssen) outp.Add(M("vorliebe_essen", "soul.erinnerung.vorliebe_essen", ("essen", FoodTitle(foodId, strings))));
            }
        }

        /// <summary>Goo und Bye shop: first market day, the till's balance, the delivery van.</summary>
        private static void AddGoobye(List<Memory> outp, JObject state)
        {
            if (!(J.Path(state, "dlc", "goobye") is JObject goobye)) return;
            var umsatz = goobye["umsatz"] as JObject;
            var tage = (int)J.Num(umsatz?["tage"]);
            var gesamt = (int)J.Num(umsatz?["gesamt"]);
            if (tage >= 1) outp.Add(M("goobye_markttag", "soul.erinnerung.goobye_markttag"));
            if (tage >= 2 && gesamt >= MinGoobyeUmsatz) outp.Add(M("goobye_umsatz", "soul.erinnerung.goobye_umsatz", ("tage", tage), ("muenzen", gesamt)));
            if (J.Path(goobye, "transport", "lieferwagen") is JValue v && v.Type == JTokenType.Boolean && (bool)v)
                outp.Add(M("goobye_lieferwagen", "soul.erinnerung.goobye_lieferwagen"));
        }

        /// <summary>McGooby: shifts played, the best score, a perfect shift.</summary>
        private static void AddMcGooby(List<Memory> outp, JObject state)
        {
            if (!(J.Path(state, "mcgooby", "schichten") is JObject schichten)) return;
            var gespielt = (int)J.Num(schichten["gespielt"]);
            if (gespielt >= MinSchichten) outp.Add(M("mcgooby_schichten", "soul.erinnerung.mcgooby_schichten", ("anzahl", gespielt)));
            var bestwert = (int)J.Num(schichten["bestwert"]);
            if (bestwert >= MinBestwert) outp.Add(M("mcgooby_bestwert", "soul.erinnerung.mcgooby_bestwert", ("punkte", bestwert)));
            if ((int)J.Num(schichten["perfekt"]) >= 1) outp.Add(M("mcgooby_perfekt", "soul.erinnerung.mcgooby_perfekt"));
        }

        private static void AddRanchWelt(List<Memory> outp, JObject state, Gooby.Core.I18n.I18n strings)
        {
            if (!(J.Path(state, "ranch", "welt") is JObject welt)) return;
            var funde = StringList(welt["funde"]);
            if (funde.Count > 0)
            {
                funde.Sort(StringComparer.Ordinal);
                outp.Add(M("ranch_fund_" + funde[0], "soul.erinnerung.ranch_fund", ("ort", Title("rwelt.fund." + funde[0], funde[0], strings))));
            }
            if (funde.Count >= MinFunde) outp.Add(M("ranch_funde", "soul.erinnerung.ranch_funde", ("anzahl", funde.Count)));
            if (StringList(welt["karteGesehen"]).Count > 0) outp.Add(M("ranch_karte", "soul.erinnerung.ranch_karte"));
        }

        /// <summary>Ghost runs (minigames.geist.&lt;game&gt;.score): the best saved ghost and the size of the collection.</summary>
        private static void AddGeist(List<Memory> outp, JObject state, Gooby.Core.I18n.I18n strings)
        {
            if (!(J.Path(state, "minigames", "geist") is JObject geist)) return;
            var topId = "";
            var top = 0;
            var anzahl = 0;
            foreach (var id in geist.Properties().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal))
            {
                if (!(geist[id] is JObject rekord)) continue;
                var score = (int)J.Num(rekord["score"]);
                if (score <= 0) continue;
                anzahl++;
                if (score > top) { top = score; topId = id; }
            }
            if (topId == "") return;
            outp.Add(M("geist_" + topId, "soul.erinnerung.geist_rekord", ("spiel", GameTitle(topId, strings)), ("punkte", top)));
            if (anzahl >= MinGeister) outp.Add(M("geist_sammlung", "soul.erinnerung.geist_sammlung", ("anzahl", anzahl)));
        }

        /// <summary>The celebrated homecoming from a holiday and the souvenir collection (inventory.items mitbringsel_*).</summary>
        private static void AddHeimkehr(List<Memory> outp, JObject state, Gooby.Core.I18n.I18n strings)
        {
            if (state["vacation"] is JObject vacation)
            {
                var ziel = J.Str(vacation["heimkehrZiel"]);
                if (J.IsTrue(vacation["heimkehrGefeiert"]) && ziel != "")
                    outp.Add(M("heimkehr_" + ziel, "soul.erinnerung.heimkehr", ("ziel", DestTitle(ziel, strings))));
            }
            if (!(J.Path(state, "inventory", "items") is JObject items)) return;
            var anzahl = items.Properties().Count(p => p.Name.StartsWith("mitbringsel_", StringComparison.Ordinal) && (int)J.Num(p.Value) > 0);
            if (anzahl >= MinMitbringsel) outp.Add(M("mitbringsel", "soul.erinnerung.mitbringsel", ("anzahl", anzahl)));
        }

        /// <summary>The arcade spotlight bonus really cashed in; the wording follows the day of the month (odd → b).</summary>
        private static void AddSpotlight(List<Memory> outp, JObject state)
        {
            var tag = J.Path(state, "minigames", "spotlightBonusDay");
            if (!J.IsString(tag) || J.Str(tag) == "") return;
            var teile = J.Str(tag).Split('-');
            var variante = teile.Length == 3 && int.TryParse(teile[2], out var d) && d % 2 == 1 ? "b" : "a";
            outp.Add(M("spotlight", "soul.erinnerung.spotlight_" + variante));
        }

        // ── titles ──

        private static string Title(string key, string fallback, Gooby.Core.I18n.I18n strings) =>
            strings != null && strings.HasKey(key) ? strings.T(key) : fallback;

        private static string GameTitle(string gameId, Gooby.Core.I18n.I18n strings) => Title("mg." + gameId + ".title", gameId, strings);

        private static string DestTitle(string destId, Gooby.Core.I18n.I18n strings) => Title("travel.ziel." + destId, destId, strings);

        public static string FoodTitle(string foodId, Gooby.Core.I18n.I18n strings) =>
            Title("soul.essen." + foodId, Title("rewards.food." + foodId, foodId, strings), strings);

        /// <summary>Unique non-empty strings of an array; junk types and duplicates drop out silently.</summary>
        private static List<string> StringList(JToken raw)
        {
            var outp = new List<string>();
            if (!(raw is JArray arr)) return outp;
            foreach (var e in arr)
            {
                if (!J.IsString(e)) continue;
                var id = J.Str(e);
                if (id != "" && !outp.Contains(id)) outp.Add(id);
            }
            return outp;
        }
    }
}
