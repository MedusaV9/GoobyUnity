using System;
using System.Collections.Generic;
using System.Linq;
using Gooby.Core.Json;
using Gooby.Core.Life;
using Gooby.Core.Random;
using Gooby.Core.State;
using Newtonsoft.Json.Linq;

namespace Gooby.Core.Home
{
    /// <summary>
    /// Goobay haggling (port of goobay_logic.gd, Doc D §5.4) — pure, the dice come in. Hidden market value V = sellBase ×
    /// the day's demand, hidden budget B = V × 0.95–1.35, hidden patience 2–4 rounds (chatty buyers one more), opening
    /// offer V × 0.55–0.75. "Höher!" asks 18–25 % more and sours the buyer's mood a step (the readable signal); past the
    /// budget or the patience a coin flip decides: "ALLERletztes Angebot!" or the buyer is gone.
    /// </summary>
    public static class GoobayLogic
    {
        public const string StatusOffen = "offen";
        public const string StatusFinal = "final";
        public const string StatusDeal = "deal";
        public const string StatusAbbruch = "abbruch";
        public const int StimmungMax = 4;
        public const double NachfrageMin = 0.8, NachfrageMax = 1.3;
        public const double BudgetMin = 0.95, BudgetMax = 1.35;
        public const double EroeffnungMin = 0.55, EroeffnungMax = 0.75;
        public const double NachfassMin = 0.18, NachfassMax = 0.25;
        public const int GeduldMin = 2, GeduldMax = 4;
        public const double AbbruchMalus = 0.1;
        public const string TypVielredner = "vielredner";
        public const string TypKnapp = "knapp";

        public sealed class Session
        {
            public string Item, Status = StatusOffen, Typ = TypKnapp;
            public int Wert, Budget, Geduld, Runde, Angebot, Stimmung, Erloes;
        }

        public static double TagesNachfrage(GoobyRng rng) => Math.Round(rng.RangeF(NachfrageMin, NachfrageMax), 2);

        public static Session Start(string itemId, int sellBase, double nachfrage, GoobyRng rng)
        {
            var wert = Math.Max(1, (int)Math.Round(sellBase * Math.Max(0.1, nachfrage), MidpointRounding.AwayFromZero));
            var typ = rng.Next() < 0.5 ? TypVielredner : TypKnapp;
            var geduld = rng.RangeI(GeduldMin, GeduldMax);
            if (typ == TypVielredner) geduld = Math.Min(GeduldMax, geduld + 1);
            return new Session
            {
                Item = itemId, Wert = wert, Typ = typ, Geduld = geduld,
                Budget = Math.Max(1, (int)Math.Round(wert * rng.RangeF(BudgetMin, BudgetMax), MidpointRounding.AwayFromZero)),
                Angebot = Math.Max(1, (int)Math.Round(wert * rng.RangeF(EroeffnungMin, EroeffnungMax), MidpointRounding.AwayFromZero)),
            };
        }

        /// <summary>"Höher!" — the counter-offer (mutates and returns the session).</summary>
        public static Session Hoeher(Session s, GoobyRng rng)
        {
            if (s.Status != StatusOffen)
            {
                // after the very last offer there is nothing more to get
                if (s.Status == StatusFinal) s.Status = StatusAbbruch;
                return s;
            }
            var forderung = (int)Math.Ceiling(s.Angebot * (1.0 + rng.RangeF(NachfassMin, NachfassMax)));
            s.Runde++;
            s.Stimmung = Math.Min(StimmungMax, s.Stimmung + 1);
            var geduldWeg = s.Runde >= s.Geduld;
            if (forderung > s.Budget || geduldWeg)
            {
                if (rng.Next() < 0.5)
                {
                    s.Status = StatusFinal;
                    s.Angebot = Math.Min(forderung, s.Budget);
                }
                else s.Status = StatusAbbruch;
                return s;
            }
            s.Angebot = forderung;
            return s;
        }

        public static Session Annehmen(Session s)
        {
            if (s.Status != StatusOffen && s.Status != StatusFinal) return s;
            s.Status = StatusDeal;
            s.Erloes = s.Angebot;
            return s;
        }

        public static Session Abbrechen(Session s)
        {
            if (s.Status != StatusDeal) s.Status = StatusAbbruch;
            return s;
        }

        public static bool IstBeendet(Session s) => s.Status == StatusDeal || s.Status == StatusAbbruch;

        /// <summary>Shipping via the post instead of pickup: +10 %, rounded up in integers (100 → 110).</summary>
        public static int PostBonus(int erloes) => (erloes * 11 + 9) / 10;

        public static double NachfrageNachAbbruch(double nachfrage) => Math.Max(NachfrageMin, Math.Round(nachfrage - AbbruchMalus, 2));
    }

    /// <summary>
    /// Goobay save binding and listing (port of goobay_state.gd), home.goobay = {nachfrage{kategorie: f}, nachfrageTag,
    /// gesperrt{itemId: tag}}. Storage pieces are listed except the last bed / couch / fridge (placed + stored count) and
    /// what fell through today; demand is rolled once a day per category.
    /// </summary>
    public static class GoobayState
    {
        public sealed class Angebot
        {
            public string Item, Kategorie;
            public int Count, Wert;
        }

        public static JObject Slice(GameStateStore gs)
        {
            var raw = J.Path(gs?.State, "home", "goobay") as JObject;
            var outp = raw != null ? (JObject)raw.DeepClone() : new JObject();
            if (!(outp["nachfrage"] is JObject)) outp["nachfrage"] = new JObject();
            if (!(outp["gesperrt"] is JObject)) outp["gesperrt"] = new JObject();
            outp["nachfrageTag"] = J.Str(outp["nachfrageTag"]);
            return outp;
        }

        private static JObject SliceOf(JObject state)
        {
            var home = J.EnsureDict(state, "home");
            var g = J.EnsureDict(home, "goobay");
            if (!(g["nachfrage"] is JObject)) g["nachfrage"] = new JObject();
            if (!(g["gesperrt"] is JObject)) g["gesperrt"] = new JObject();
            if (!J.IsString(g["nachfrageTag"])) g["nachfrageTag"] = "";
            return g;
        }

        private static JArray PlacedItems(GameStateStore gs)
        {
            var outp = new JArray();
            if (J.Path(gs?.State, "home", "rooms") is JObject rooms)
                foreach (var room in rooms.Properties())
                    foreach (var e in J.Path(room.Value, "items") as JArray ?? new JArray()) outp.Add(e.DeepClone());
            return outp;
        }

        /// <summary>Unsellable while it is the LAST piece of its mandatory slot (placed + stored, Doc D §2.4).</summary>
        public static bool IstPflichtGesperrt(GameStateStore gs, string itemId)
        {
            var slot = FurnitureCatalog.Def(itemId)?.Pflicht ?? "";
            if (slot.Length == 0) return false;
            FurnitureCatalog.MandatoryCounts(PlacedItems(gs)).TryGetValue(slot, out var total);
            foreach (var e in HomeState.Storage(gs))
                if (e is JObject o && FurnitureCatalog.Def(J.Str(o["item"]))?.Pflicht == slot) total += Math.Max(0, J.Int(o["count"]));
            return total <= 1;
        }

        public static int Verkaufswert(FurnitureDef def) =>
            def == null ? 0 : def.Verkaufswert > 0 ? def.Verkaufswert : Math.Max(1, (int)Math.Round(def.Preis * 0.35, MidpointRounding.AwayFromZero));

        public static List<Angebot> Angebote(GameStateStore gs, string heute)
        {
            var gesperrt = Slice(gs)["gesperrt"] as JObject;
            var outp = new List<Angebot>();
            foreach (var e in HomeState.Storage(gs))
            {
                if (!(e is JObject o)) continue;
                var id = J.Str(o["item"]);
                var def = FurnitureCatalog.Def(id);
                if (def == null || IstPflichtGesperrt(gs, id)) continue;
                if (!string.IsNullOrEmpty(heute) && J.Str(gesperrt?[id]) == heute) continue;
                var existing = outp.FirstOrDefault(a => a.Item == id);
                if (existing != null) { existing.Count += Math.Max(1, J.Int(o["count"], 1)); continue; }
                outp.Add(new Angebot { Item = id, Count = Math.Max(1, J.Int(o["count"], 1)), Wert = Verkaufswert(def), Kategorie = def.Kategorie });
            }
            return outp.OrderBy(a => a.Item, StringComparer.Ordinal).ToList();
        }

        /// <summary>The day's demand of a category (rolled once a day, then stable).</summary>
        public static double Nachfrage(GameStateStore gs, string kategorie, string tag, GoobyRng rng)
        {
            var slice = Slice(gs);
            if (J.Str(slice["nachfrageTag"]) == tag && slice["nachfrage"][kategorie] != null) return J.Num(slice["nachfrage"][kategorie]);
            var wert = GoobayLogic.TagesNachfrage(rng);
            gs.Update(state =>
            {
                var g = SliceOf(state);
                if (J.Str(g["nachfrageTag"]) != tag)
                {
                    g["nachfrage"] = new JObject();
                    g["nachfrageTag"] = tag;
                }
                ((JObject)g["nachfrage"])[kategorie] = wert;
            });
            gs.NotifySliceChanged("home");
            return wert;
        }

        /// <summary>Deal: the piece leaves storage, coins come in (+10 % shipped via the post). Returns the coins paid.</summary>
        public static int DealAbschliessen(GameStateStore gs, string itemId, int erloes, bool versand)
        {
            if (gs == null || StorageLogic.CountOf(HomeState.Storage(gs), itemId) <= 0) return 0;
            var betrag = versand ? GoobayLogic.PostBonus(erloes) : erloes;
            var paid = 0;
            gs.Update(state =>
            {
                var home = J.EnsureDict(state, "home");
                if (!(home["storage"] is JArray storage) || !StorageLogic.Take(storage, itemId)) return;
                paid = Economy.Award(J.EnsureDict(state, "economy"), betrag, "goobay");
                var counters = J.EnsureDict(J.EnsureDict(state, "achievements"), "counters");
                counters["goobaySales"] = J.Int(counters["goobaySales"]) + 1;
            });
            gs.NotifySliceChanged("home");
            gs.NotifySliceChanged("achievements");
            return paid;
        }

        /// <summary>A failed deal: the piece cannot be listed again today and the category's demand drops.</summary>
        public static void AbbruchMerken(GameStateStore gs, string itemId, string kategorie, string tag)
        {
            gs.Update(state =>
            {
                var g = SliceOf(state);
                ((JObject)g["gesperrt"])[itemId] = tag;
                var tabelle = (JObject)g["nachfrage"];
                if (tabelle[kategorie] != null) tabelle[kategorie] = GoobayLogic.NachfrageNachAbbruch(J.Num(tabelle[kategorie]));
            });
            gs.NotifySliceChanged("home");
        }
    }
}
