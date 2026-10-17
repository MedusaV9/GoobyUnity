using System;
using System.Collections.Generic;
using System.Linq;
using Gooby.Core.Json;
using Gooby.Core.Minigames.Ranch;
using Gooby.Unity.App;
using Gooby.Unity.World;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace Gooby.Unity.UI.Screens
{
    /// <summary>
    /// The ranch quests on screen (port of rquest_log_ui.gd and the quest part of rnpc_dialog.gd): quest cards (title,
    /// giver, story, the current goal with its progress or the wait left, the reward, Annehmen/Abgeben), the log with the
    /// main series and the side quests, a ranch person's sheet (hearts, a line from their dialog tree, chat, gifts,
    /// stories, their quests), and the one place where game events reach the quests (<see cref="Melde"/>): a finished goal
    /// or a quest ready to hand in gets a toast, handing in rains coins and confetti.
    /// </summary>
    public static class RanchQuestUi
    {
        private static readonly Dictionary<string, JObject> Dialoge = new Dictionary<string, JObject>();

        private static string T(GameHost host, string key, params (string, object)[] args) => host.Strings.T(key, args);

        public static string Name(GameHost host, string npc) => T(host, "rnpc." + npc + ".name");
        public static string Rolle(GameHost host, string npc) => T(host, "rnpc." + npc + ".rolle");
        public static string Titel(GameHost host, RanchQuestKatalog.Quest q) => T(host, "rquest.q." + q.Id + ".titel");

        /// <summary>A line of a ranch person's dialog tree (following "next" for stories); {spieler} is the player's name.</summary>
        public static string Zeile(GameHost host, string npc, string knoten, int kette = 1)
        {
            var lang = host.Settings.Locale == "en" ? "en" : "de";
            var key = lang + "/" + npc;
            if (!Dialoge.TryGetValue(key, out var d))
            {
                var asset = Resources.Load<TextAsset>("gooby/ranch_npc/" + key) ?? Resources.Load<TextAsset>("gooby/ranch_npc/de/" + npc);
                d = asset != null ? JObject.Parse(asset.text) : new JObject();
                Dialoge[key] = d;
            }
            var nodes = J.Dict(d["nodes"]);
            var teile = new List<string>();
            var k = knoten;
            for (var i = 0; i < kette && nodes[k] is JObject n; i++)
            {
                if (n["text"] is JArray a) teile.AddRange(a.Select(t => J.Str(t)));
                else teile.Add(J.Str(n["text"]));
                var next = J.Str(n["next"]);
                if (next.Length == 0 || next == "hub" || next == "tschuess") break;
                k = next;
            }
            return string.Join(" ", teile.Where(t => t.Length > 0)).Replace("{spieler}", J.Str(J.Path(host.Store.State, "meta", "playerName"), "Gooby"));
        }

        private static string Wert(GameHost host, RanchQuestKatalog.Ziel z)
        {
            switch (z.Typ)
            {
                case "gehe_zu": return T(host, "rquest.ort." + z.Wert);
                case "sprich_mit": return Name(host, z.Wert);
                case "sammle": return T(host, "rquest.item." + z.Wert);
                case "pflege": return T(host, "rquest.aktion." + z.Wert);
                case "reite_strecke": return T(host, "rquest.strecke." + z.Wert);
                case "gewinne_wettbewerb": return T(host, "rquest.disziplin." + z.Wert);
                default: return z.Wert;
            }
        }

        /// <summary>The current goal as a line ("Sprich mit Rosi Zügelfest", "Sammle Hufeisen (1/3)", "Bereit in 2 Std 10 Min").</summary>
        public static string ZielText(GameHost host, RanchQuestKatalog.Quest q, long nowMs)
        {
            var s = host.Store.State;
            var status = RanchQuests.Status(s, q);
            if (status == RanchQuests.Erfuellbar) return T(host, "rquest.ui.abholen", ("name", Name(host, q.Geber)));
            if (status == RanchQuests.Wartend)
            {
                var rest = RanchQuests.BereitIn(s, q, nowMs);
                var min = (int)Math.Ceiling(rest / 60000.0);
                var text = min <= 0 ? T(host, "rquest.warte.gleich") : min >= 60 ? T(host, "rquest.warte.stunden", ("h", min / 60), ("m", min % 60)) : T(host, "rquest.warte.minuten", ("m", min));
                return T(host, "rquest.log.wartend_bis", ("rest", text));
            }
            var z = status == RanchQuests.Aktiv ? RanchQuests.AktuellesZiel(s, q) : q.Ziele.FirstOrDefault();
            if (z == null) return "";
            var (ist, soll, _, _) = RanchQuests.Fortschritt(s, q);
            var wert = Wert(host, z);
            switch (z.Typ)
            {
                case "gehe_zu": return T(host, "rquest.ziel.gehe_zu", ("ort", wert));
                case "sprich_mit": return T(host, "rquest.ziel.sprich_mit", ("npc", wert));
                case "sammle": return T(host, "rquest.ziel.sammle", ("item", wert), ("ist", ist), ("soll", soll));
                case "pflege": return T(host, "rquest.ziel.pflege", ("aktion", wert), ("ist", ist), ("soll", soll));
                case "reite_strecke": return T(host, "rquest.ziel.reite_strecke", ("strecke", wert));
                case "gewinne_wettbewerb": return T(host, "rquest.ziel.gewinne_wettbewerb", ("platz", z.Platz), ("disziplin", wert));
                default: return T(host, "rquest.ziel.warte_bis");
            }
        }

        public static string BelohnungText(GameHost host, RanchQuestKatalog.Quest q)
        {
            var teile = new List<string>();
            if (q.Muenzen > 0) teile.Add(T(host, "rquest.log.muenzen", ("n", q.Muenzen)));
            foreach (var it in q.Items) teile.Add(it.Value + " × " + T(host, "rquest.item." + it.Key));
            foreach (var h in q.Herzen) teile.Add("♥ " + Name(host, h.Key).Split(' ')[0]);
            foreach (var f in q.Freischalten) teile.Add(T(host, "rquest.frei." + f));
            return string.Join(" · ", teile);
        }

        // ── events from the game ──

        /// <summary>
        /// A game event on the running quests: a goal finished → "✓" toast; a quest ready → toast with its giver and a
        /// little fanfare. Returns true when anything moved.
        /// </summary>
        public static bool Melde(GameHost host, string typ, string wert, int n = 1, int platz = 99)
        {
            if (host?.Store == null || string.IsNullOrEmpty(wert)) return false;
            var now = host.Store.Clock.NowMs();
            var ok = Ereignis(host, s => RanchQuests.Melde(s, typ, wert, now, n, platz));
            if (ok) Debug.Log("[quest] " + typ + " " + wert);
            return ok;
        }

        /// <summary>Any change that may move quests (a pick-up, a report): booked in the store, then announced.</summary>
        public static bool Ereignis(GameHost host, Func<JObject, List<string>> was)
        {
            if (host?.Store == null) return false;
            var vorher = Schnappschuss(host.Store.State);
            List<string> ids = null;
            host.Store.Update(s => ids = was(s), GameHost.TicksMs);
            if (ids == null || ids.Count == 0) return false;
            host.Store.NotifySliceChanged(RanchHof.SliceId);
            Ankuendigen(host, vorher, ids);
            return true;
        }

        /// <summary>Due waits end (call from the farm's and the region's refresh).</summary>
        public static void Tick(GameHost host)
        {
            if (host?.Store == null) return;
            var vorher = Schnappschuss(host.Store.State);
            var now = host.Store.Clock.NowMs();
            List<string> ids = null;
            host.Store.Update(s => ids = RanchQuests.Tick(s, now), GameHost.TicksMs);
            if (ids == null || ids.Count == 0) return;
            host.Store.NotifySliceChanged(RanchHof.SliceId);
            Ankuendigen(host, vorher, ids);
        }

        private static Dictionary<string, (int ziel, string status)> Schnappschuss(JObject s) =>
            RanchQuests.MitStatus(s, RanchQuests.Aktiv, RanchQuests.Wartend, RanchQuests.Erfuellbar)
                .ToDictionary(q => q.Id, q => (RanchQuests.Fortschritt(s, q).ziel, RanchQuests.Status(s, q)));

        private static void Ankuendigen(GameHost host, Dictionary<string, (int ziel, string status)> vorher, List<string> ids)
        {
            var s = host.Store.State;
            foreach (var id in ids)
            {
                var q = RanchQuestKatalog.Fuer(id);
                var status = RanchQuests.Status(s, q);
                vorher.TryGetValue(id, out var alt);
                if (status == RanchQuests.Erfuellbar && alt.status != RanchQuests.Erfuellbar)
                {
                    Sfx.Play("ranch_fanfare", 4f, 0.5f);
                    host.Ui.Toast(T(host, "rquest.ui.fertig", ("quest", Titel(host, q)), ("name", Name(host, q.Geber).Split(' ')[0])), "star", 4f);
                }
                else if (RanchQuests.Fortschritt(s, q).ziel > alt.ziel)
                {
                    Sfx.PlayUi("soft_tick");
                    host.Ui.Toast("✓ " + Titel(host, q) + " — " + ZielText(host, q, host.Store.Clock.NowMs()), "check", 3f);
                }
            }
        }

        // ── handing in, accepting ──

        public static bool Annehmen(GameHost host, string id)
        {
            var now = host.Store.Clock.NowMs();
            var ok = false;
            host.Store.Update(s => ok = RanchQuests.Annehmen(s, id, now), GameHost.TicksMs);
            if (!ok) { Sfx.PlayUi("soft_error"); return false; }
            host.Store.NotifySliceChanged(RanchHof.SliceId);
            Sfx.PlayUi("soft_confirm");
            var q = RanchQuestKatalog.Fuer(id);
            host.Ui.Toast(T(host, "rquest.ui.angenommen", ("quest", Titel(host, q))), "star");
            Debug.Log("[quest] angenommen " + id);
            // you are with the giver while accepting: a first "talk to them" goal is done right away
            if (q.Ziele.Count > 0 && q.Ziele[0].Typ == "sprich_mit" && q.Ziele[0].Wert == q.Geber) Melde(host, "sprich_mit", q.Geber);
            return true;
        }

        /// <summary>Hands a quest in: coins fly to <paramref name="coinPill"/>, hearts and unlocks get their toast.</summary>
        public static bool Abgeben(GameHost host, string id, Vector2 from, VisualElement coinPill)
        {
            var now = host.Store.Clock.NowMs();
            RanchQuests.Belohnung b = null;
            host.Store.Update(s => b = RanchQuests.Abgeben(s, id, now), GameHost.TicksMs);
            if (b == null) { Sfx.PlayUi("soft_error"); return false; }
            host.Store.NotifySliceChanged(RanchHof.SliceId);
            host.Store.NotifySliceChanged("economy");
            host.Store.NotifySliceChanged("inventory");
            var q = RanchQuestKatalog.Fuer(id);
            Sfx.Play("ranch_fanfare_sieg", 0f, 0.7f);
            Haptics.Win();
            host.Ui.Confetti(70);
            if (b.Muenzen > 0)
            {
                host.Ui.FloatText(from + new Vector2(0f, -40f), "+" + b.Muenzen, new Color(0.98f, 0.8f, 0.3f), 40f);
                if (coinPill != null) host.Ui.FlyIcons(from, coinPill, "coin", Math.Min(12, Math.Max(4, b.Muenzen / 25)), _ => Sfx.PlayUi("soft_collect"));
            }
            host.Ui.Toast(T(host, "rquest.ui.abgegeben", ("quest", Titel(host, q)), ("text", BelohnungText(host, q))), "trophy", 4.5f);
            foreach (var (npc, vorher, nachher) in b.Herzen)
                if (nachher > vorher) host.Ui.Toast(T(host, "rnpc.toast_herz", ("name", Name(host, npc).Split(' ')[0]), ("n", nachher)), "heart");
            foreach (var f in b.Freischalten)
            {
                var was = T(host, "rquest.frei." + f);
                host.Ui.Toast(T(host, "rquest.ui.frei_toast", ("was", was)), "sparkle", 4f);
                var art = RanchBelohnungen.Fuer(f)?.Art;
                if (art != null) host.Ui.Toast(T(host, "rquest.ui.frei_" + art, ("was", was)), art == RanchBelohnungen.Gear ? "horseshoe" : art == RanchBelohnungen.Deko ? "hammer" : "star", 4.5f);
            }
            if (b.Geliefert.Count > 0) Debug.Log("[quest] geliefert " + string.Join(",", b.Geliefert));
            Debug.Log("[quest] abgegeben " + id + " +" + b.Muenzen);
            return true;
        }

        // ── cards ──

        /// <summary>One quest as a card; <paramref name="neu"/> rebuilds the surrounding list after a button.</summary>
        public static VisualElement Karte(GameHost host, RanchQuestKatalog.Quest q, Action neu, VisualElement coinPill)
        {
            var s = host.Store.State;
            var status = RanchQuests.Status(s, q);
            var card = Ux.Col("gb-rquest-karte");
            card.name = "rquest-" + q.Id;
            card.EnableInClassList("gb-rquest-karte--haupt", q.Typ == RanchQuestKatalog.Haupt);
            card.EnableInClassList("gb-rquest-karte--bereit", status == RanchQuests.Erfuellbar);
            var kopf = Ux.Row("gb-rquest-kopf");
            var titel = Ux.Text(Titel(host, q), "gb-rquest-titel");
            kopf.Add(titel);
            var chip = Ux.Text(T(host, "rquest.log.status_" + status), "gb-rquest-status");
            chip.AddToClassList("gb-rquest-status--" + status);
            kopf.Add(chip);
            card.Add(kopf);
            var geber = q.Typ == RanchQuestKatalog.Haupt
                ? T(host, "rquest.log.kapitel", ("n", q.Kapitel)) + " · " + T(host, "rquest.log.geber", ("name", Name(host, q.Geber)))
                : T(host, "rquest.log.geber", ("name", Name(host, q.Geber)));
            card.Add(Ux.Text(geber, "gb-xs", "gb-mild"));
            if (status == RanchQuests.Verfuegbar)
            {
                var text = Ux.Text(T(host, "rquest.q." + q.Id + ".text"), "gb-sm");
                text.style.whiteSpace = WhiteSpace.Normal;
                text.style.marginTop = 4;
                card.Add(text);
            }
            if (status != RanchQuests.Erledigt)
            {
                var ziel = Ux.Row("gb-rquest-ziel");
                ziel.Add(Ux.Icon(status == RanchQuests.Erfuellbar ? "gift" : status == RanchQuests.Wartend ? "stopwatch" : "pin"));
                var zt = Ux.Text(ZielText(host, q, host.Store.Clock.NowMs()), "gb-sm", "gb-bold");
                zt.style.whiteSpace = WhiteSpace.Normal;
                zt.style.flexShrink = 1;
                ziel.Add(zt);
                card.Add(ziel);
                if (status == RanchQuests.Aktiv || status == RanchQuests.Wartend || status == RanchQuests.Erfuellbar)
                {
                    var (_, _, idx, n) = RanchQuests.Fortschritt(s, q);
                    var bar = Ux.Bar(Mathf.Clamp01((float)(status == RanchQuests.Erfuellbar ? n : idx) / Mathf.Max(1, n)), out _);
                    bar.AddToClassList("gb-rquest-bar");
                    card.Add(bar);
                }
            }
            var unten = Ux.Row("gb-rquest-unten");
            var belohnung = Ux.Text(T(host, "rquest.log.belohnung", ("text", BelohnungText(host, q))), "gb-xs");
            belohnung.style.flexShrink = 1;
            belohnung.style.whiteSpace = WhiteSpace.Normal;
            unten.Add(belohnung);
            if (status == RanchQuests.Verfuegbar)
            {
                var an = Ux.Btn(T(host, "rquest.log.annehmen"), () => { if (Annehmen(host, q.Id)) neu?.Invoke(); }, "gb-btn-primary", "gb-btn-sm");
                an.name = "rquest-annehmen-" + q.Id;
                unten.Add(an);
            }
            else if (status == RanchQuests.Erfuellbar)
            {
                var ab = Ux.IconTextBtn("gift", T(host, "rquest.log.abgeben"), () =>
                {
                    var p = card.worldBound.center;
                    if (Abgeben(host, q.Id, p, coinPill)) neu?.Invoke();
                }, "gb-btn-primary", "gb-btn-sm");
                ab.name = "rquest-abgeben-" + q.Id;
                unten.Add(ab);
            }
            card.Add(unten);
            return card;
        }

        /// <summary>The quest log into <paramref name="body"/>: ready first, then running, then what is offered; the series' chapter on top.</summary>
        public static void Log(GameHost host, VisualElement body, Action neu, VisualElement coinPill, string tab)
        {
            body.Clear();
            var s = host.Store.State;
            var kapitel = RanchQuests.Kapitel(s);
            var tabs = Ux.Row("gb-rquest-tabs");
            foreach (var (id, key) in new[] { ("haupt", "rquest.log.tab_haupt"), ("neben", "rquest.log.tab_neben") })
            {
                var tid = id;
                var b = Ux.Btn(T(host, key), () => { Sfx.PlayUi("soft_chip"); Log(host, body, neu, coinPill, tid); }, tab == id ? "gb-btn-primary" : "gb-btn-secondary", "gb-btn-sm");
                b.name = "rquest-tab-" + id;
                tabs.Add(b);
            }
            body.Add(tabs);
            if (tab == "haupt") body.Add(Ux.Text(kapitel > 10 ? T(host, "rquest.ui.reihe_fertig") : T(host, "rquest.log.kapitel", ("n", kapitel)), "gb-ranchhof-sec"));
            var reihe = new[] { RanchQuests.Erfuellbar, RanchQuests.Aktiv, RanchQuests.Wartend, RanchQuests.Verfuegbar };
            var liste = RanchQuestKatalog.Alle.Where(q => (tab == "haupt") == (q.Typ == RanchQuestKatalog.Haupt))
                .Select(q => (q, st: RanchQuests.Status(s, q))).Where(x => Array.IndexOf(reihe, x.st) >= 0)
                .OrderBy(x => Array.IndexOf(reihe, x.st)).ThenBy(x => x.q.Kapitel).Select(x => x.q).ToList();
            if (liste.Count == 0) body.Add(Ux.Text(T(host, "rquest.log.leer"), "gb-sm", "gb-mild"));
            foreach (var q in liste) body.Add(Karte(host, q, () => Log(host, body, neu, coinPill, tab), coinPill));
            var erledigt = RanchQuests.ErledigtListe(s).Count(id => (tab == "haupt") == (RanchQuestKatalog.Fuer(id)?.Typ == RanchQuestKatalog.Haupt));
            if (erledigt > 0) body.Add(Ux.Text(T(host, "rquest.ui.erledigt_n", ("n", erledigt)), "gb-xs", "gb-mild"));
        }

        /// <summary>A ranch person's sheet into <paramref name="body"/>: hearts, a line, chat/gift/story, and their quests.</summary>
        public static void Person(GameHost host, VisualElement body, string npc, Action neu, VisualElement coinPill, Action<string> gesagt = null)
        {
            body.Clear();
            var s = host.Store.State;
            var tag = host.Store.Clock.LocalDay(host.Store.Clock.NowMs());
            var herzen = Ux.Row("gb-ranchdorf-herzen");
            var n = RanchNpc.HerzenVon(s, npc);
            for (var i = 0; i < RanchNpc.HerzMax; i++)
            {
                var h = Ux.Icon("heart");
                h.AddToClassList("gb-ranchdorf-herz");
                h.EnableInClassList("gb-ranchdorf-herz--an", i < n);
                herzen.Add(h);
            }
            body.Add(herzen);
            var stunde = DateTimeOffset.FromUnixTimeMilliseconds(host.Store.Clock.NowMs()).ToLocalTime().Hour;
            var offen = RanchQuests.VomGeber(s, npc, RanchQuests.Verfuegbar);
            var bereit = RanchQuests.VomGeber(s, npc, RanchQuests.Erfuellbar);
            var knoten = bereit.Count > 0 ? "quest_abgabe" : offen.Count > 0 ? "quest_vergabe" : n >= 4 ? "gruss_freund" : stunde < 11 ? "gruss_morgen" : stunde >= 18 ? "gruss_abend" : "gruss_tag";
            var zeile = Ux.Text("„" + Zeile(host, npc, knoten) + "“", "gb-md");
            zeile.name = "rnpc-zeile";
            zeile.style.whiteSpace = WhiteSpace.Normal;
            zeile.style.marginTop = 6;
            body.Add(zeile);
            var knoepfe = Ux.Row("gb-ranchdorf-freund");
            var reden = Ux.IconTextBtn("chat", T(host, "rdorf.freund.plaudern"), () =>
            {
                (int vorher, int nachher) res = (0, 0);
                var now = host.Store.Clock.NowMs();
                host.Store.Update(st => res = RanchNpc.Reden(st, npc, tag, now), GameHost.TicksMs);
                host.Store.NotifySliceChanged(RanchHof.SliceId);
                zeile.text = "„" + Zeile(host, npc, new[] { "smalltalk_a", "smalltalk_b", "smalltalk_c" }[(int)(now / 1000 % 3)]) + "“";
                Sfx.PlayUi("soft_tick");
                gesagt?.Invoke(npc);
                if (res.nachher > res.vorher) { host.Ui.Toast(T(host, "rnpc.toast_herz", ("name", Name(host, npc).Split(' ')[0]), ("n", res.nachher)), "heart"); host.Ui.Confetti(30); }
            }, "gb-btn-secondary", "gb-btn-sm");
            reden.name = "rnpc-plaudern";
            knoepfe.Add(reden);
            foreach (var item in RanchNpc.Geschenke)
            {
                var it = item;
                var vorrat = RanchPflege.Vorrat(s, item);
                if (vorrat <= 0) continue;
                var icon = item == "heu" ? "hay" : item == "apfel" ? "food_apple" : item == "karotte" ? "food_carrot" : item == "hafer" ? "sprout" : "candy";
                var g = Ux.IconTextBtn(icon, vorrat.ToString(), () =>
                {
                    (string reaktion, int vorher, int nachher) res = (null, 0, 0);
                    var now = host.Store.Clock.NowMs();
                    host.Store.Update(st => res = RanchNpc.Schenken(st, npc, it, tag, now), GameHost.TicksMs);
                    if (res.reaktion == null) { Sfx.PlayUi("soft_error"); return; }
                    host.Store.NotifySliceChanged(RanchHof.SliceId);
                    host.Store.NotifySliceChanged("inventory");
                    Sfx.PlayUi(res.reaktion == "liebt" ? "soft_levelup" : "soft_collect");
                    if (res.nachher > res.vorher) host.Ui.Toast(T(host, "rnpc.toast_herz", ("name", Name(host, npc).Split(' ')[0]), ("n", res.nachher)), "heart");
                    Person(host, body, npc, neu, coinPill, gesagt);
                    var z = body.Q<Label>("rnpc-zeile");
                    if (z != null) z.text = "„" + Zeile(host, npc, "geschenk_" + res.reaktion) + "“";
                }, "gb-btn-secondary", "gb-btn-sm");
                g.name = "rnpc-gib-" + item;
                g.tooltip = T(host, "rhof.futter." + item);
                g.style.marginLeft = 6;
                knoepfe.Add(g);
            }
            if (n >= 3)
            {
                var story = Ux.IconTextBtn("book", T(host, "rdorf.freund.geschichte"), () => { zeile.text = "„" + Zeile(host, npc, n >= 5 ? "geschichte_2" : "geschichte_1", 4) + "“"; Sfx.PlayUi("soft_open"); }, "gb-btn-secondary", "gb-btn-sm");
                story.style.marginLeft = 6;
                knoepfe.Add(story);
            }
            body.Add(knoepfe);
            var quests = RanchQuests.VomGeber(s, npc, RanchQuests.Erfuellbar, RanchQuests.Verfuegbar, RanchQuests.Aktiv, RanchQuests.Wartend);
            if (quests.Count > 0) body.Add(Ux.Text(T(host, "rquest.log.titel"), "gb-ranchhof-sec"));
            foreach (var q in quests.OrderBy(q => RanchQuests.Status(s, q) == RanchQuests.Erfuellbar ? 0 : 1))
                body.Add(Karte(host, q, () => { Person(host, body, npc, neu, coinPill, gesagt); neu?.Invoke(); }, coinPill));
        }

        /// <summary>What a ranch person's marker shows: "!" an offer, "?" a quest to hand in, "" nothing.</summary>
        public static string Marke(JObject state, string npc)
        {
            if (RanchQuests.VomGeber(state, npc, RanchQuests.Erfuellbar).Count > 0) return "?";
            return RanchQuests.VomGeber(state, npc, RanchQuests.Verfuegbar).Count > 0 ? "!" : "";
        }

        /// <summary>The tracker's line: the pinned quest's goal, else the next main quest on offer, else the hint to Rosi.</summary>
        public static string TrackerText(GameHost host, long nowMs)
        {
            var s = host.Store.State;
            var q = Verfolgt(s);
            if (q != null)
            {
                var ziel = RanchQuests.AktuellesZiel(s, q);
                var wen = RanchQuests.Status(s, q) == RanchQuests.Erfuellbar ? q.Geber : ziel?.Typ == "sprich_mit" ? ziel.Wert : null;
                return Titel(host, q) + " · " + ZielText(host, q, nowMs) + MitWo(host, wen, nowMs);
            }
            var neu = RanchQuests.MitStatus(s, RanchQuests.Verfuegbar).OrderBy(x => x.Typ == RanchQuestKatalog.Haupt ? 0 : 1).ThenBy(x => x.Kapitel).FirstOrDefault();
            return neu != null
                ? T(host, "rquest.ui.tracker_neu", ("quest", Titel(host, neu)), ("name", Name(host, neu.Geber).Split(' ')[0])) + MitWo(host, neu.Geber, nowMs)
                : T(host, "rquest.ui.tracker_leer");
        }

        private static string MitWo(GameHost host, string npc, long nowMs)
        {
            var wo = Wo(host, npc, nowMs);
            return wo.Length > 0 ? " · " + wo : "";
        }

        /// <summary>
        /// Where someone is at this hour of their day ("Rosi ist gerade am Trog", "… ist zu Hause – ab 6 Uhr am Stall");
        /// "" for Hufingen's shopkeepers, who stay in their shops.
        /// </summary>
        public static string Wo(GameHost host, string npc, long nowMs)
        {
            if (string.IsNullOrEmpty(npc) || !string.IsNullOrEmpty(RanchNpc.Fuer(npc)?.Laden)) return "";
            var routine = RanchNpcRoutine.Routine(npc);
            var stunde = RanchOffline.StundeLokal(nowMs, host.Store.Clock.UtcOffsetMinutes(nowMs));
            var jetzt = RanchNpcRoutine.StationBei(routine, stunde);
            if (jetzt == null) return "";
            var name = Name(host, npc).Split(' ')[0];
            if (jetzt.Ort != "haus") return T(host, "rquest.wo.jetzt", ("name", name), ("ort", T(host, "rquest.bei." + jetzt.Ort)));
            var naechste = RanchNpcRoutine.Naechste(routine, stunde);
            return naechste == null ? T(host, "rquest.wo.jetzt", ("name", name), ("ort", T(host, "rquest.bei.haus")))
                : T(host, "rquest.wo.bis", ("name", name), ("ort", T(host, "rquest.bei.haus")), ("zeit", (int)naechste.Von), ("danach", T(host, "rquest.bei." + naechste.Ort)));
        }

        /// <summary>The pinned quest for the tracker: ready first, then the main series, then the oldest running side quest.</summary>
        public static RanchQuestKatalog.Quest Verfolgt(JObject state)
        {
            var laufend = RanchQuests.MitStatus(state, RanchQuests.Erfuellbar, RanchQuests.Aktiv, RanchQuests.Wartend);
            return laufend.OrderBy(q => RanchQuests.Status(state, q) == RanchQuests.Erfuellbar ? 0 : q.Typ == RanchQuestKatalog.Haupt ? 1 : 2).FirstOrDefault();
        }

        /// <summary>The farm's and region's people (Hufingen's keepers live in their shops): where they stand and their look.</summary>
        public static readonly Dictionary<string, Dictionary<string, string>> Look = new Dictionary<string, Dictionary<string, string>>
        {
            ["rosi"] = new Dictionary<string, string> { ["fell"] = "caramel", ["hut"] = "cap" },
            ["moehrchen"] = new Dictionary<string, string> { ["fell"] = "rose", ["hals"] = "scarfRed" },
            ["timmi"] = new Dictionary<string, string> { ["fell"] = "fell_himmel", ["hut"] = "beanie" },
            ["luna"] = new Dictionary<string, string> { ["fell"] = "ash", ["hut"] = "strawHat" },
            ["knips"] = new Dictionary<string, string> { ["fell"] = "caramel", ["hut"] = "beret" },
            ["punktabzug"] = new Dictionary<string, string> { ["fell"] = "ash", ["hals"] = "bowtie" },
            ["eilfried"] = new Dictionary<string, string> { ["fell"] = "rose", ["hut"] = "cap", ["hals"] = "scarfRed" },
        };
    }
}
