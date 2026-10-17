using System;
using System.Collections.Generic;
using Gooby.Core.Content;
using Gooby.Core.Json;
using Gooby.Core.Life;
using Gooby.Core.State;
using Newtonsoft.Json.Linq;

namespace Gooby.Core.City
{
    /// <summary>
    /// Wochenmarkt (port of markt_preise.gd + the market's selling counter): Greta buys the player's harvest at the
    /// compost base price plus the market bonus, but every unit of a crop sold today pushes its price down by the
    /// elasticity until the floor. A new local day resets the counters (save: city.markt = {tag, verkauft}).
    /// Open Saturdays 8–14 according to the town plan (CityMap.IstOffen).
    /// </summary>
    public static class MarktLogic
    {
        public sealed class Sorte
        {
            public string Id, NameDe;
            public int Basis;
        }

        public sealed class Angebot
        {
            public Sorte Sorte;
            public int Vorrat, Preis, VollerPreis, HeuteVerkauft;
        }

        private static JObject Daten(string path) => ContentSource.LoadObject(path);

        public static List<Sorte> Sorten(string path = ShopLogic.MarktPreisePath)
        {
            var outp = new List<Sorte>();
            foreach (var t in Daten(path)["ernte"] as JArray ?? new JArray())
                if (t is JObject o && J.Str(o["id"]).Length > 0)
                    outp.Add(new Sorte { Id = J.Str(o["id"]), NameDe = J.Str(o["name_de"], J.Str(o["id"])), Basis = J.Int(o["basis"]) });
            return outp;
        }

        public static Sorte SorteVon(string id, string path = ShopLogic.MarktPreisePath)
        {
            foreach (var s in Sorten(path)) if (s.Id == id) return s;
            return null;
        }

        /// <summary>Full market price of one unit (base + bonus), 0 for unknown crops.</summary>
        public static int Marktpreis(string id, string path = ShopLogic.MarktPreisePath)
        {
            var s = SorteVon(id, path);
            if (s == null) return 0;
            var bonus = J.NumOr(Daten(path)["markt_bonus"], 0.15);
            return Math.Max(1, (int)Math.Round(s.Basis * (1.0 + bonus), MidpointRounding.AwayFromZero));
        }

        /// <summary>Price of the NEXT unit when `schonVerkauft` went over the counter today.</summary>
        public static int Stueckpreis(string id, int schonVerkauft, string path = ShopLogic.MarktPreisePath)
        {
            var voll = Marktpreis(id, path);
            if (voll <= 0) return 0;
            var d = Daten(path);
            var elastizitaet = J.NumOr(d["elastizitaet_pro_stueck"], 0.05);
            var boden = J.NumOr(d["preis_boden"], 0.5);
            var faktor = Math.Max(boden, 1.0 - elastizitaet * Math.Max(0, schonVerkauft));
            return Math.Max(1, (int)Math.Round(voll * faktor, MidpointRounding.AwayFromZero));
        }

        /// <summary>Proceeds of `menge` units in one go (each unit pushes the next price further).</summary>
        public static int Erloes(string id, int menge, int schonVerkauft, string path = ShopLogic.MarktPreisePath)
        {
            var summe = 0;
            for (var i = 0; i < Math.Max(0, menge); i++) summe += Stueckpreis(id, schonVerkauft + i, path);
            return summe;
        }

        /// <summary>Local day key YYYY-MM-DD — a new key resets the elasticity.</summary>
        public static string TagesKey(long nowMs, int biasMinutes)
        {
            var (y, m, d, _) = Time.GameClock.LocalDateTime(nowMs, biasMinutes);
            return $"{y:D4}-{m:D2}-{d:D2}";
        }

        /// <summary>Today's market slice {tag, verkauft{}} — fresh on a new day.</summary>
        public static JObject SliceHeute(JObject state, string heute)
        {
            var raw = J.Path(state, "city", "markt") as JObject;
            if (raw == null || J.Str(raw["tag"]) != heute || !(raw["verkauft"] is JObject))
                return new JObject { ["tag"] = heute, ["verkauft"] = new JObject() };
            return new JObject { ["tag"] = heute, ["verkauft"] = raw["verkauft"].DeepClone() };
        }

        public static int HeuteVerkauft(GameStateStore gs, string heute, string id) =>
            J.Int(SliceHeute(gs?.State, heute)["verkauft"][id]);

        /// <summary>What the player can sell right now: harvest in inventory.food with today's next-unit price.</summary>
        public static List<Angebot> AngebotDesSpielers(GameStateStore gs, string heute, string path = ShopLogic.MarktPreisePath)
        {
            var outp = new List<Angebot>();
            if (gs == null) return outp;
            var verkauft = SliceHeute(gs.State, heute)["verkauft"] as JObject;
            foreach (var s in Sorten(path))
            {
                var vorrat = J.Int(J.Path(gs.State, "inventory", "food", s.Id));
                if (vorrat <= 0) continue;
                var schon = J.Int(verkauft?[s.Id]);
                outp.Add(new Angebot { Sorte = s, Vorrat = vorrat, HeuteVerkauft = schon, Preis = Stueckpreis(s.Id, schon, path), VollerPreis = Marktpreis(s.Id, path) });
            }
            return outp;
        }

        /// <summary>Sells up to `menge` units: food out, coins in, day counter up, `sells` achievement counter up.</summary>
        public static (bool ok, int menge, int erloes) Verkaufen(GameStateStore gs, string heute, string id, int menge, string path = ShopLogic.MarktPreisePath)
        {
            if (gs == null || menge <= 0 || SorteVon(id, path) == null) return (false, 0, 0);
            var vorrat = J.Int(J.Path(gs.State, "inventory", "food", id));
            var echt = Math.Min(menge, vorrat);
            if (echt <= 0) return (false, 0, 0);
            var slice = SliceHeute(gs.State, heute);
            var verkauft = (JObject)slice["verkauft"];
            var schon = J.Int(verkauft[id]);
            var summe = Erloes(id, echt, schon, path);
            verkauft[id] = schon + echt;
            gs.Update(state =>
            {
                var food = J.EnsureDict(J.EnsureDict(state, "inventory"), "food");
                food[id] = Math.Max(0, J.Int(food[id]) - echt);
                Economy.Award(J.EnsureDict(state, "economy"), summe, "wochenmarkt");
                J.EnsureDict(state, "city")["markt"] = slice;
                var counters = J.EnsureDict(J.EnsureDict(state, "achievements"), "counters");
                counters["sells"] = J.Int(counters["sells"]) + echt;
            });
            gs.NotifySliceChanged("inventory");
            gs.NotifySliceChanged("economy");
            gs.NotifySliceChanged("city");
            return (true, echt, summe);
        }
    }
}
