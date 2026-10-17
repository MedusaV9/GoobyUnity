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
    /// GOOBERANDO delivery state machine (port of gooberando_state.gd, Doc E §5): idle → bestellt (prep + fee 3)
    /// → vorDerTuer (patience 300 s) → trinkgeld (5 coins, 30 % 2 h drain ×0.75) / abgestellt. Pure timestamps
    /// so a restart at the same clock lands in the same phase.
    /// </summary>
    public static class GooberandoLogic
    {
        public const int Liefergebuehr = 3;
        public const int TrinkgeldBetrag = 5;
        public const double BuffChance = 0.3;
        public const long BuffDauerMs = 2L * 3600 * 1000;
        public const double BuffDrainFaktor = 0.75;
        public const int PrepMinS = 120, PrepMaxS = 300;
        public const int GeduldS = 300;
        public const int HinweisNachOhne = 3;

        public const string StateIdle = "idle";
        public const string StateBestellt = "bestellt";
        public const string StateVorDerTuer = "vorDerTuer";
        public const string StateTrinkgeld = "trinkgeld";

        private static readonly HashSet<string> States = new HashSet<string> { StateIdle, StateBestellt, StateVorDerTuer, StateTrinkgeld };

        public static JObject DefaultSlice() => new JObject
        {
            ["state"] = StateIdle, ["bestelltAt"] = 0, ["fertigAt"] = 0, ["gerichtId"] = "",
            ["gerichte"] = new JArray(), ["restaurantId"] = "", ["lieferungen"] = 0, ["trinkgelder"] = 0,
            ["ohneTrinkgeldFolge"] = 0, ["hinweisGezeigt"] = false, ["buffBis"] = 0,
        };

        public static JObject SliceOf(JObject state) => Normalize(state?["gooberando"]);

        public static JObject Normalize(JToken raw)
        {
            var d = DefaultSlice();
            if (!(raw is JObject o)) return d;
            var st = J.Str(o["state"], StateIdle);
            d["state"] = States.Contains(st) ? st : StateIdle;
            d["bestelltAt"] = Math.Max(0L, (long)J.Num(o["bestelltAt"]));
            d["fertigAt"] = Math.Max(0L, (long)J.Num(o["fertigAt"]));
            d["gerichtId"] = J.Str(o["gerichtId"]);
            d["gerichte"] = GerichtIds(o);
            d["restaurantId"] = J.Str(o["restaurantId"]);
            d["lieferungen"] = Math.Max(0, J.Int(o["lieferungen"]));
            d["trinkgelder"] = Math.Max(0, J.Int(o["trinkgelder"]));
            d["ohneTrinkgeldFolge"] = Math.Max(0, J.Int(o["ohneTrinkgeldFolge"]));
            d["hinweisGezeigt"] = J.IsTrue(o["hinweisGezeigt"]);
            d["buffBis"] = Math.Max(0L, (long)J.Num(o["buffBis"]));
            return d;
        }

        private static JArray GerichtIds(JObject raw)
        {
            var ids = new JArray();
            if (raw["gerichte"] is JArray arr)
                foreach (var t in arr) { var id = t?.ToString() ?? ""; if (id.Length > 0) ids.Add(id); }
            if (ids.Count == 0)
            {
                var one = J.Str(raw["gerichtId"]);
                if (one.Length > 0) ids.Add(one);
            }
            return ids;
        }

        public static bool KannBestellen(JObject slice) => J.Str(Normalize(slice)["state"]) == StateIdle;

        public sealed class OrderResult
        {
            public bool Ok;
            public JObject Slice;
            public int Kosten;
        }

        public sealed class TickEvent
        {
            public string Typ, GerichtId = "";
            public List<string> Gerichte = new List<string>();
        }

        public sealed class TickResult
        {
            public JObject Slice;
            public List<TickEvent> Events = new List<TickEvent>();
        }

        public sealed class HandoverResult
        {
            public bool Ok;
            public JObject Slice;
            public string GerichtId = "";
            public List<string> Gerichte = new List<string>();
        }

        public sealed class TipResult
        {
            public JObject Slice;
            public int Kosten;
            public bool Buff, Hinweis;
        }

        /// <summary>Single-dish order (W3a signature): price + delivery fee 3, doorbell notification at fertigAt.</summary>
        public static OrderResult Bestellen(JObject slice, long nowMs, int prepS, string gerichtId, int preis, string restaurantId = "") =>
            BestellenKorb(slice, nowMs, prepS, new[] { (gerichtId, preis) }, restaurantId);

        public static OrderResult BestellenKorb(JObject slice, long nowMs, int prepS, IList<(string id, int preis)> gerichte, string restaurantId = "")
        {
            var s = Normalize(slice);
            var fail = new OrderResult { Ok = false, Slice = s, Kosten = 0 };
            if (!KannBestellen(s) || gerichte == null || gerichte.Count == 0) return fail;
            var ids = new JArray();
            var kosten = Liefergebuehr;
            foreach (var g in gerichte)
            {
                if (string.IsNullOrEmpty(g.id)) continue;
                ids.Add(g.id);
                kosten += g.preis;
            }
            if (ids.Count == 0) return fail;
            s["state"] = StateBestellt;
            s["bestelltAt"] = nowMs;
            s["fertigAt"] = nowMs + Math.Max(1, prepS) * 1000L;
            s["gerichtId"] = ids[0].ToString();
            s["gerichte"] = ids;
            s["restaurantId"] = restaurantId ?? "";
            return new OrderResult { Ok = true, Slice = s, Kosten = kosten };
        }

        public static TickResult Tick(JObject slice, long nowMs)
        {
            var s = Normalize(slice);
            var events = new List<TickEvent>();
            var fertig = (long)J.Num(s["fertigAt"]);
            if (J.Str(s["state"]) == StateBestellt && nowMs >= fertig)
            {
                s["state"] = StateVorDerTuer;
                events.Add(new TickEvent { Typ = "vor_der_tuer" });
            }
            if (J.Str(s["state"]) == StateVorDerTuer && nowMs > fertig + GeduldS * 1000L)
            {
                var gerichtId = J.Str(s["gerichtId"]);
                var ids = Ids(s);
                s = NachLieferung(s, 0);
                events.Add(new TickEvent { Typ = "abgestellt", GerichtId = gerichtId, Gerichte = ids });
            }
            return new TickResult { Slice = s, Events = events };
        }

        public static HandoverResult Uebergabe(JObject slice, long nowMs)
        {
            var ticked = Tick(slice, nowMs);
            var s = ticked.Slice;
            if (J.Str(s["state"]) != StateVorDerTuer)
                return new HandoverResult { Ok = false, Slice = s };
            s["state"] = StateTrinkgeld;
            return new HandoverResult { Ok = true, Slice = s, GerichtId = J.Str(s["gerichtId"]), Gerichte = Ids(s) };
        }

        /// <summary>Tip decision; <paramref name="roll"/> is 0..1 (injectable). Buff at roll &lt; 0.3 when tipping.</summary>
        public static TipResult Trinkgeld(JObject slice, long nowMs, bool geben, double roll)
        {
            var s = Normalize(slice);
            if (J.Str(s["state"]) != StateTrinkgeld)
                return new TipResult { Slice = s, Kosten = 0, Buff = false, Hinweis = false };
            var buff = false;
            if (geben)
            {
                s["trinkgelder"] = J.Int(s["trinkgelder"]) + 1;
                if (roll < BuffChance) { buff = true; s["buffBis"] = nowMs + BuffDauerMs; }
            }
            s = NachLieferung(s, geben ? -1 : 1);
            var hinweis = false;
            if (!geben && J.Int(s["ohneTrinkgeldFolge"]) >= HinweisNachOhne && !J.IsTrue(s["hinweisGezeigt"]))
            {
                s["hinweisGezeigt"] = true;
                hinweis = true;
            }
            return new TipResult { Slice = s, Kosten = geben ? TrinkgeldBetrag : 0, Buff = buff, Hinweis = hinweis };
        }

        public static bool BuffAktiv(JObject slice, long nowMs) => nowMs < (long)J.Num(Normalize(slice)["buffBis"]);

        public static double EnergieDrainFaktor(JObject slice, long nowMs) => BuffAktiv(slice, nowMs) ? BuffDrainFaktor : 1.0;

        public static int LieferRestS(JObject slice, long nowMs)
        {
            var s = Normalize(slice);
            if (J.Str(s["state"]) != StateBestellt) return 0;
            return Math.Max(0, (int)Math.Ceiling(((long)J.Num(s["fertigAt"]) - nowMs) / 1000.0));
        }

        /// <summary>Pays the order (coins out) and writes the slice. Caller supplies prep seconds (kitchen + drive).</summary>
        public static bool Buchen(GameStateStore gs, long nowMs, int prepS, string gerichtId, int preis, string restaurantId)
        {
            if (gs == null) return false;
            var ok = false;
            gs.Update(state =>
            {
                var ordered = Bestellen(SliceOf(state), nowMs, prepS, gerichtId, preis, restaurantId);
                if (!ordered.Ok) return;
                if (!Economy.Spend(J.EnsureDict(state, "economy"), ordered.Kosten, "gooberando")) return;
                state["gooberando"] = ordered.Slice;
                ok = true;
            });
            if (ok) { gs.NotifySliceChanged("economy"); gs.NotifySliceChanged("gooberando"); }
            return ok;
        }

        /// <summary>Door opened: food into inventory, then the tip prompt.</summary>
        public static HandoverResult Annehmen(GameStateStore gs, long nowMs)
        {
            var result = new HandoverResult();
            if (gs == null) return result;
            gs.Update(state =>
            {
                result = Uebergabe(SliceOf(state), nowMs);
                if (!result.Ok) return;
                var food = J.EnsureDict(J.EnsureDict(state, "inventory"), "food");
                foreach (var id in result.Gerichte) food[id] = J.Int(food[id]) + 1;
                state["gooberando"] = result.Slice;
            });
            if (result.Ok)
            {
                gs.NotifySliceChanged("inventory");
                gs.NotifySliceChanged("gooberando");
            }
            return result;
        }

        public static TipResult TrinkgeldGeben(GameStateStore gs, long nowMs, bool geben, double roll)
        {
            var result = new TipResult { Slice = DefaultSlice() };
            if (gs == null) return result;
            gs.Update(state =>
            {
                result = Trinkgeld(SliceOf(state), nowMs, geben, roll);
                if (result.Kosten > 0) Economy.Spend(J.EnsureDict(state, "economy"), result.Kosten, "trinkgeld");
                state["gooberando"] = result.Slice;
            });
            gs.NotifySliceChanged("economy");
            gs.NotifySliceChanged("gooberando");
            return result;
        }

        /// <summary>Catch-up tick (doorbell / left-at-the-door). Writes the slice only when something happened.</summary>
        public static TickResult CatchUp(GameStateStore gs, long nowMs)
        {
            var result = new TickResult { Slice = DefaultSlice() };
            if (gs == null) return result;
            var pending = Tick(SliceOf(gs.State), nowMs);
            if (pending.Events.Count == 0) { result.Slice = pending.Slice; return result; }
            gs.Update(state =>
            {
                result = Tick(SliceOf(state), nowMs);
                foreach (var e in result.Events)
                {
                    if (e.Typ != "abgestellt") continue;
                    var food = J.EnsureDict(J.EnsureDict(state, "inventory"), "food");
                    foreach (var id in e.Gerichte) food[id] = J.Int(food[id]) + 1;
                }
                state["gooberando"] = result.Slice;
            });
            gs.NotifySliceChanged("gooberando");
            if (HasAbgestellt(result)) gs.NotifySliceChanged("inventory");
            return result;
        }

        private static bool HasAbgestellt(TickResult r)
        {
            foreach (var e in r.Events) if (e.Typ == "abgestellt") return true;
            return false;
        }

        private static List<string> Ids(JObject s)
        {
            var outp = new List<string>();
            if (s["gerichte"] is JArray arr)
                foreach (var t in arr) { var id = t?.ToString() ?? ""; if (id.Length > 0) outp.Add(id); }
            return outp;
        }

        private static JObject NachLieferung(JObject s, int folgeDelta)
        {
            s["lieferungen"] = J.Int(s["lieferungen"]) + 1;
            if (folgeDelta < 0) s["ohneTrinkgeldFolge"] = 0;
            else if (folgeDelta > 0) s["ohneTrinkgeldFolge"] = J.Int(s["ohneTrinkgeldFolge"]) + 1;
            s["state"] = StateIdle;
            s["bestelltAt"] = 0;
            s["fertigAt"] = 0;
            s["gerichtId"] = "";
            s["gerichte"] = new JArray();
            s["restaurantId"] = "";
            return s;
        }
    }

    /// <summary>The three virtual kitchens of gooberando_restaurants.json (Doc E §5.1).</summary>
    public static class GooberandoRestaurants
    {
        public sealed class Gericht { public string Id, NameDe; public int Preis; }
        public sealed class Restaurant
        {
            public string Id, NameDe, Sterne, GagKey;
            public int PrepMinS, PrepMaxS;
            public int StreetR, StreetC;
            public List<Gericht> Gerichte = new List<Gericht>();
        }

        private static List<Restaurant> _all;

        public static void Reset() => _all = null;

        public static IReadOnlyList<Restaurant> All()
        {
            if (_all != null) return _all;
            _all = new List<Restaurant>();
            var root = ContentSource.TryLoadObject("city/gooberando_restaurants");
            foreach (var t in root?["restaurants"] as JArray ?? new JArray())
            {
                if (!(t is JObject o)) continue;
                var r = new Restaurant
                {
                    Id = J.Str(o["id"]), NameDe = J.Str(o["name_de"]), Sterne = J.Str(o["sterne"]),
                    GagKey = J.Str(o["gag_key"]), PrepMinS = J.Int(o["prep_min_s"]), PrepMaxS = J.Int(o["prep_max_s"]),
                };
                var strasse = o["strasse"] as JArray;
                if (strasse != null && strasse.Count >= 2) { r.StreetR = J.Int(strasse[0]); r.StreetC = J.Int(strasse[1]); }
                foreach (var g in o["gerichte"] as JArray ?? new JArray())
                {
                    if (!(g is JObject go)) continue;
                    r.Gerichte.Add(new Gericht { Id = J.Str(go["id"]), NameDe = J.Str(go["name_de"]), Preis = J.Int(go["preis"]) });
                }
                if (r.Id.Length > 0) _all.Add(r);
            }
            return _all;
        }

        public static Restaurant ById(string id)
        {
            foreach (var r in All()) if (r.Id == id) return r;
            return null;
        }

        public static (int r, int c) StreetTile(string restaurantId)
        {
            var r = ById(restaurantId);
            if (r != null) return (r.StreetR, r.StreetC);
            var k = CityGrid.KitchenStreet;
            return (k[0], k[1]);
        }

        /// <summary>Kitchen wait used for a live order: midpoint of the restaurant range (no RNG).</summary>
        public static int PrepS(Restaurant r) => r == null ? GooberandoLogic.PrepMinS : (r.PrepMinS + r.PrepMaxS) / 2;
    }
}
