using System;
using System.Collections.Generic;
using System.Linq;
using Gooby.Core.Content;
using Gooby.Core.Json;
using Gooby.Core.Life;
using Newtonsoft.Json.Linq;

namespace Gooby.Core.Minigames.Ranch
{
    /// <summary>
    /// The ranch's quest catalog (port of rquest_katalog.gd over content/ranch_quests, the Godot pack 1:1): ten main quests
    /// in chapters and the side quests of the thirteen ranch people, each with its giver, goals in order, rewards (coins,
    /// items, friendship points, unlocks) and prerequisites (quests done, hearts with someone, the chapter). The daily
    /// tasks of the pack are <see cref="RanchTage"/>'s.
    /// </summary>
    public static class RanchQuestKatalog
    {
        public const string Pfad = "minigames/ranch/ranch_quests";
        public const string Haupt = "haupt", Neben = "neben";
        public static readonly string[] ZielTypen = { "gehe_zu", "sprich_mit", "sammle", "pflege", "reite_strecke", "gewinne_wettbewerb", "warte_bis" };

        public sealed class Ziel
        {
            public string Typ = "", Wert = "";
            public int N = 1, Platz = 99, DauerMin;
        }

        public sealed class Quest
        {
            public string Id, Typ, Geber;
            public int Kapitel;
            public List<Ziel> Ziele = new List<Ziel>();
            public int Muenzen;
            public Dictionary<string, int> Items = new Dictionary<string, int>(), Herzen = new Dictionary<string, int>(), VorHerzen = new Dictionary<string, int>();
            public List<string> Freischalten = new List<string>(), VorQuests = new List<string>();
            public int VorKapitel;
        }

        private static List<Quest> _alle;

        public static void ResetCache() => _alle = null;

        /// <summary>Main and side quests in pack order.</summary>
        public static IReadOnlyList<Quest> Alle
        {
            get
            {
                if (_alle != null) return _alle;
                _alle = new List<Quest>();
                var roh = ContentSource.LoadObject(Pfad);
                var items = roh["items"] as JArray ?? (JToken.Parse("[]") as JArray);
                foreach (var e in items.OfType<JObject>())
                {
                    var typ = J.Str(e["typ"]);
                    if (typ != Haupt && typ != Neben) continue;
                    var q = new Quest { Id = J.Str(e["id"]), Typ = typ, Geber = J.Str(e["geber"]), Kapitel = J.Int(e["kapitel"]) };
                    foreach (var z in (e["ziele"] as JArray ?? new JArray()).OfType<JObject>())
                    {
                        var zt = J.Str(z["typ"]);
                        q.Ziele.Add(new Ziel
                        {
                            Typ = zt,
                            Wert = J.Str(z[zt == "gehe_zu" ? "ort" : zt == "sprich_mit" ? "npc" : zt == "sammle" ? "item" : zt == "pflege" ? "aktion" : zt == "reite_strecke" ? "strecke" : "disziplin"]),
                            N = Math.Max(1, J.Int(z["n"], 1)),
                            Platz = J.Int(z["platz"], 99),
                            DauerMin = J.Int(z["dauerMin"]),
                        });
                    }
                    var b = J.Dict(e["belohnung"]);
                    q.Muenzen = Math.Max(0, J.Int(b["muenzen"]));
                    foreach (var p in J.Dict(b["items"]).Properties()) q.Items[p.Name] = J.Int(p.Value);
                    foreach (var p in J.Dict(b["herzen"]).Properties()) q.Herzen[p.Name] = J.Int(p.Value);
                    q.Freischalten.AddRange((b["freischalten"] as JArray ?? new JArray()).Select(t => J.Str(t)));
                    var vor = J.Dict(e["voraussetzungen"]);
                    q.VorQuests.AddRange((vor["quests"] as JArray ?? new JArray()).Select(t => J.Str(t)));
                    foreach (var p in J.Dict(vor["herzen"]).Properties()) q.VorHerzen[p.Name] = J.Int(p.Value);
                    q.VorKapitel = J.Int(vor["kapitel"]);
                    _alle.Add(q);
                }
                return _alle;
            }
        }

        public static Quest Fuer(string id) => Alle.FirstOrDefault(q => q.Id == id);

        public static List<Quest> Hauptreihe => Alle.Where(q => q.Typ == Haupt).OrderBy(q => q.Kapitel).ToList();

        /// <summary>The thirteen ranch people who give quests (pack order of first appearance).</summary>
        public static List<string> Geber => Alle.Select(q => q.Geber).Distinct().ToList();
    }

    /// <summary>
    /// The quest machine of the ranch (port of rquest_engine.gd + rquest_state.gd) on <c>ranch.quests</c>: locked →
    /// available → active (⇄ waiting) → ready to hand in → done; only the running quests and the done list are saved,
    /// the rest is derived. Goals run in order; game systems report events (<see cref="Melde"/>) that count on the CURRENT
    /// goal only; a wait goal parks the quest until its time (<see cref="Tick"/>), never blocking others. Handing in pays
    /// the coins, puts the items into the farm store (carrots into the fridge, the rest into the quest bag), adds the
    /// friendship points and records the unlocks. Unity goes further than Godot here: every goal type is wired to the game.
    /// </summary>
    public static class RanchQuests
    {
        public const string Key = "quests";
        public const string Gesperrt = "gesperrt", Verfuegbar = "verfuegbar", Aktiv = "aktiv", Wartend = "wartend", Erfuellbar = "erfuellbar", Erledigt = "erledigt";
        private const long MsProMin = 60_000L;

        public static JObject Slice(JObject state)
        {
            var ranch = J.EnsureDict(state, RanchHof.SliceId);
            var q = J.EnsureDict(ranch, Key);
            q["v"] = Math.Max(1, J.Int(q["v"], 1));
            if (!(q["aktiv"] is JObject)) q["aktiv"] = new JObject();
            foreach (var p in ((JObject)q["aktiv"]).Properties().ToList())
                if (RanchQuestKatalog.Fuer(p.Name) == null || !(p.Value is JObject)) p.Remove();
            foreach (var key in new[] { "erledigt", "freigeschaltet", "geliefert" })
                q[key] = new JArray((q[key] as JArray ?? new JArray()).Select(t => J.Str(t)).Where(s => s.Length > 0).Distinct());
            if (!(q["items"] is JObject)) q["items"] = new JObject();
            return q;
        }

        private static JObject Lauf(JObject state, string id) => J.Path(state, RanchHof.SliceId, Key, "aktiv", id) as JObject;

        public static List<string> ErledigtListe(JObject state) =>
            (J.Path(state, RanchHof.SliceId, Key, "erledigt") as JArray ?? new JArray()).Select(t => J.Str(t)).ToList();

        public static List<string> Freigeschaltet(JObject state) =>
            (J.Path(state, RanchHof.SliceId, Key, "freigeschaltet") as JArray ?? new JArray()).Select(t => J.Str(t)).ToList();

        /// <summary>How many of a quest item are in the bag (horseshoes, wool, flowers …).</summary>
        public static int Item(JObject state, string item) => J.Int(J.Path(state, RanchHof.SliceId, Key, "items", item));

        public static string Status(JObject state, RanchQuestKatalog.Quest q)
        {
            if (q == null) return Gesperrt;
            if (ErledigtListe(state).Contains(q.Id)) return Erledigt;
            var lauf = Lauf(state, q.Id);
            if (lauf != null) return J.Str(lauf["status"], Aktiv);
            return Voraussetzungen(state, q) ? Verfuegbar : Gesperrt;
        }

        public static bool Voraussetzungen(JObject state, RanchQuestKatalog.Quest q)
        {
            var erledigt = ErledigtListe(state);
            if (q.VorQuests.Any(v => !erledigt.Contains(v))) return false;
            if (q.VorHerzen.Any(h => RanchNpc.HerzenVon(state, h.Key) < h.Value)) return false;
            return q.VorKapitel <= 0 || Kapitel(state) >= q.VorKapitel;
        }

        /// <summary>The chapter now: main quests done + 1 (11 = the series is through).</summary>
        public static int Kapitel(JObject state)
        {
            var erledigt = ErledigtListe(state);
            return RanchQuestKatalog.Hauptreihe.Count(q => erledigt.Contains(q.Id)) + 1;
        }

        public static List<RanchQuestKatalog.Quest> MitStatus(JObject state, params string[] status) =>
            RanchQuestKatalog.Alle.Where(q => status.Contains(Status(state, q))).ToList();

        /// <summary>What a ranch person has for you: offers, running quests, quests to hand in.</summary>
        public static List<RanchQuestKatalog.Quest> VomGeber(JObject state, string npc, params string[] status) =>
            RanchQuestKatalog.Alle.Where(q => q.Geber == npc && (status.Length == 0 || status.Contains(Status(state, q)))).ToList();

        public static RanchQuestKatalog.Ziel AktuellesZiel(JObject state, RanchQuestKatalog.Quest q)
        {
            var lauf = Lauf(state, q?.Id ?? "");
            if (lauf == null) return null;
            var i = J.Int(lauf["zielIndex"]);
            return i >= 0 && i < q.Ziele.Count ? q.Ziele[i] : null;
        }

        public static (int ist, int soll, int ziel, int ziele) Fortschritt(JObject state, RanchQuestKatalog.Quest q)
        {
            var lauf = Lauf(state, q.Id);
            var z = AktuellesZiel(state, q);
            return (J.Int(lauf?["zaehler"]), z?.N ?? 1, J.Int(lauf?["zielIndex"]), q.Ziele.Count);
        }

        /// <summary>Milliseconds until a waiting quest is ready (0 when not waiting).</summary>
        public static long BereitIn(JObject state, RanchQuestKatalog.Quest q, long nowMs)
        {
            var lauf = Lauf(state, q.Id);
            return lauf != null && J.Str(lauf["status"]) == Wartend ? Math.Max(0L, J.Long(lauf["bereitAt"]) - nowMs) : 0L;
        }

        public static bool Annehmen(JObject state, string id, long nowMs)
        {
            var q = RanchQuestKatalog.Fuer(id);
            if (q == null || Status(state, q) != Verfuegbar) return false;
            var lauf = new JObject { ["status"] = Aktiv, ["zielIndex"] = 0, ["zaehler"] = 0, ["bereitAt"] = 0, ["startAt"] = nowMs };
            WartePruefen(q, lauf, nowMs);
            ((JObject)Slice(state)["aktiv"])[id] = lauf;
            return true;
        }

        /// <summary>
        /// A game event on every running quest — "gehe_zu teich", "sprich_mit rosi", "sammle hufeisen 1", "pflege striegeln",
        /// "reite_strecke see_runde", "gewinne_wettbewerb parcours" with a place. Returns the quests that moved on.
        /// </summary>
        public static List<string> Melde(JObject state, string typ, string wert, long nowMs, int n = 1, int platz = 99)
        {
            var geaendert = new List<string>();
            var aktiv = J.Path(state, RanchHof.SliceId, Key, "aktiv") as JObject;
            if (aktiv == null) return geaendert;
            if (typ == "sammle")
            {
                var bag = (JObject)Slice(state)["items"];
                bag[wert] = J.Int(bag[wert]) + Math.Max(1, n);
            }
            foreach (var p in aktiv.Properties().ToList())
            {
                var q = RanchQuestKatalog.Fuer(p.Name);
                var lauf = p.Value as JObject;
                if (q == null || lauf == null || J.Str(lauf["status"]) != Aktiv) continue;
                var z = AktuellesZiel(state, q);
                if (z == null || z.Typ != typ || z.Wert != wert) continue;
                if (typ == "gewinne_wettbewerb" && platz > z.Platz) continue;
                lauf["zaehler"] = J.Int(lauf["zaehler"]) + Math.Max(1, n);
                if (J.Int(lauf["zaehler"]) >= z.N) ZielWeiter(q, lauf, nowMs);
                geaendert.Add(q.Id);
            }
            return geaendert;
        }

        /// <summary>Waits that are due resolve; returns the quests whose wait just ended.</summary>
        public static List<string> Tick(JObject state, long nowMs)
        {
            var fertig = new List<string>();
            var aktiv = J.Path(state, RanchHof.SliceId, Key, "aktiv") as JObject;
            if (aktiv == null) return fertig;
            foreach (var p in aktiv.Properties().ToList())
            {
                var q = RanchQuestKatalog.Fuer(p.Name);
                var lauf = p.Value as JObject;
                if (q == null || lauf == null || J.Str(lauf["status"]) != Wartend || J.Long(lauf["bereitAt"]) > nowMs) continue;
                lauf["status"] = Aktiv;
                lauf["bereitAt"] = 0;
                ZielWeiter(q, lauf, nowMs);
                fertig.Add(q.Id);
            }
            return fertig;
        }

        public sealed class Belohnung
        {
            public int Muenzen;
            public Dictionary<string, int> Items = new Dictionary<string, int>();
            public List<(string npc, int vorher, int nachher)> Herzen = new List<(string, int, int)>();
            public List<string> Freischalten = new List<string>();

            /// <summary>Unlocks that arrived as things right now (build store, gear cupboard — <see cref="RanchBelohnungen"/>).</summary>
            public List<string> Geliefert = new List<string>();
        }

        /// <summary>Hands in a ready quest and pays it out (null = not ready).</summary>
        public static Belohnung Abgeben(JObject state, string id, long nowMs)
        {
            var q = RanchQuestKatalog.Fuer(id);
            if (q == null || Status(state, q) != Erfuellbar) return null;
            var slice = Slice(state);
            ((JObject)slice["aktiv"]).Remove(id);
            var erledigt = (JArray)slice["erledigt"];
            if (!erledigt.Any(t => J.Str(t) == id)) erledigt.Add(id);
            var b = new Belohnung { Muenzen = q.Muenzen };
            if (q.Muenzen > 0) Economy.Award(J.EnsureDict(state, "economy"), q.Muenzen, "ranch_quest");
            foreach (var it in q.Items)
            {
                b.Items[it.Key] = it.Value;
                if (it.Key == "apfel" || it.Key == "heu")
                {
                    var lager = J.EnsureDict(J.EnsureDict(J.EnsureDict(state, RanchHof.SliceId), "wirtschaft"), "lager");
                    lager[it.Key] = J.Int(lager[it.Key]) + it.Value;
                }
                else if (it.Key == "karotte")
                {
                    var food = J.EnsureDict(J.EnsureDict(state, "inventory"), "food");
                    food["carrot"] = J.Int(food["carrot"]) + it.Value;
                }
                else
                {
                    var bag = (JObject)slice["items"];
                    bag[it.Key] = J.Int(bag[it.Key]) + it.Value;
                }
            }
            foreach (var h in q.Herzen)
            {
                var vorher = RanchNpc.HerzenVon(state, h.Key);
                RanchNpc.Gutschreiben(state, h.Key, h.Value, nowMs);
                b.Herzen.Add((h.Key, vorher, RanchNpc.HerzenVon(state, h.Key)));
            }
            var frei = (JArray)slice["freigeschaltet"];
            foreach (var f in q.Freischalten)
                if (!frei.Any(t => J.Str(t) == f)) { frei.Add(f); b.Freischalten.Add(f); }
            b.Geliefert = RanchBelohnungen.Liefern(state);
            return b;
        }

        private static void ZielWeiter(RanchQuestKatalog.Quest q, JObject lauf, long nowMs)
        {
            lauf["zielIndex"] = J.Int(lauf["zielIndex"]) + 1;
            lauf["zaehler"] = 0;
            WartePruefen(q, lauf, nowMs);
        }

        private static void WartePruefen(RanchQuestKatalog.Quest q, JObject lauf, long nowMs)
        {
            var i = J.Int(lauf["zielIndex"]);
            if (i >= q.Ziele.Count)
            {
                lauf["status"] = Erfuellbar;
                return;
            }
            var z = q.Ziele[i];
            if (z.Typ == "warte_bis")
            {
                lauf["status"] = Wartend;
                lauf["bereitAt"] = nowMs + z.DauerMin * MsProMin;
            }
            else lauf["status"] = Aktiv;
        }
    }
}
