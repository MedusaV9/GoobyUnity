using System.Collections.Generic;
using System.Linq;
using Gooby.Core.Content;
using Gooby.Core.Json;
using Gooby.Core.Life;
using Gooby.Core.State;
using Newtonsoft.Json.Linq;

namespace Gooby.Core.City
{
    /// <summary>One shop row (rehwei_sortiment.json waren/saatgut/buecher entry).</summary>
    public sealed class Ware
    {
        public string Id, NameDe, NameEn, Inventar, Kategorie;
        public int Preis;
        public int Menge = 1;          // units per purchase (Baumarkt materials: 5 boards, 20 nails)
        public bool BrauchtRezept, Gooberando, Einmalig;
        public JObject Raw;

        public static Ware From(JObject o) => new Ware
        {
            Id = J.Str(o["id"]), NameDe = J.Str(o["name_de"], J.Str(o["id"])), NameEn = J.Str(o["name_en"], J.Str(o["name_de"], J.Str(o["id"]))),
            Inventar = J.Str(o["inventar"]), Kategorie = J.Str(o["kategorie"], "food"), Preis = J.Int(o["preis"]),
            Menge = System.Math.Max(1, J.Int(o["menge"], 1)),
            BrauchtRezept = J.IsTrue(o["braucht_rezept"]), Gooberando = J.IsTrue(o["gooberando"]), Einmalig = J.IsTrue(o["einmalig"]), Raw = o,
        };

        /// <summary>Food rows land in inventory.food under their id; everything else in inventory.items under `inventar`.</summary>
        public bool IsFood => string.IsNullOrEmpty(Inventar) && !IsEffekt;

        /// <summary>Instant-effect rows (GOOBYMAN plaster: heals health pressure on the spot, once a day).</summary>
        public bool IsEffekt => Kategorie == "pflaster";

        /// <summary>Bought exactly once: story books, the sleep mask, blueprints, the camera.</summary>
        public bool NurEinmal => Einmalig || Kategorie == "buch" || Kategorie == "bauplan" || Kategorie == "einmalig";

        /// <summary>Localized shelf name (assortments are DE-authored, EN falls back to the German name).</summary>
        public string Name(string locale) => locale == "en" ? NameEn : NameDe;
    }

    /// <summary>
    /// Port of city/sortiment.gd + haendler_sheet.gd purchase rules — the generic counter of every shop along the
    /// street: REHWEI grocery/seeds/books, GOOBYTHEKE remedies, GOOBYMAN drugstore, Baumarkt materials and
    /// blueprints. POW!'s daily offers, the market, the car dealer, the park and the airport have their own logic.
    /// </summary>
    public static class ShopLogic
    {
        public const string RehweiPath = "city/rehwei_sortiment";
        public const string GoobythekePath = "city/goobytheke_sortiment";
        public const string GoobymanPath = "city/goobyman_sortiment";
        public const string PowPath = "city/pow_sortiment";
        public const string BaumarktPath = "city/baumarkt_katalog";
        public const string CarsPath = "city/cars";
        public const string MarktWarenPath = "city/markt_waren";
        public const string MarktPreisePath = "city/markt_preise";
        public const string FlagRezept = "rezept_tropfen";

        public static List<Ware> Waren(string path = RehweiPath) => Liste(path, "waren");
        public static List<Ware> Saatgut(string path = RehweiPath) => Liste(path, "saatgut");
        public static List<Ware> Buecher(string path = RehweiPath) => Liste(path, "buecher");

        /// <summary>Bodo Balken's materials (stackable, `menge` units per pack) and blueprints (flag items, once).</summary>
        public static List<Ware> Materialien(string path = BaumarktPath) => Liste(path, "materialien", "material");
        public static List<Ware> Bauplaene(string path = BaumarktPath) => Liste(path, "bauplaene", "bauplan");

        private static List<Ware> Liste(string path, string feld, string kategorie = null)
        {
            var outp = new List<Ware>();
            foreach (var t in ContentSource.LoadObject(path)[feld] as JArray ?? new JArray())
            {
                if (!(t is JObject o) || J.Str(o["id"]).Length == 0) continue;
                var w = Ware.From(o);
                if (kategorie != null && !o.ContainsKey("kategorie")) w.Kategorie = kategorie;
                outp.Add(w);
            }
            return outp;
        }

        public static Ware Find(IEnumerable<Ware> waren, string id) => waren.FirstOrDefault(w => w.Id == id);

        public static List<Ware> GooberandoGerichte() => Waren().Where(w => w.Gooberando).ToList();

        public static int Coins(GameStateStore gs) => J.Int(J.Path(gs.State, "economy", "coins"));

        public static bool HatRezept(GameStateStore gs) => J.IsTrue(J.Path(gs.State, "city", "flags", FlagRezept));

        public static bool KannKaufen(GameStateStore gs, Ware ware)
        {
            if (ware == null || ware.Preis > Coins(gs)) return false;
            if (ware.BrauchtRezept && !HatRezept(gs)) return false;
            return true;
        }

        /// <summary>Books and other once-only rows are bought exactly once — "already on the shelf".</summary>
        public static bool ImRegal(GameStateStore gs, Ware ware)
        {
            var key = ware.Inventar.Length > 0 ? ware.Inventar : ware.Id;
            return J.Int(J.Path(gs.State, "inventory", "items", key)) > 0;
        }

        /// <summary>Owned already and not for sale again (books, sleep mask, blueprints, camera).</summary>
        public static bool Ausverkauft(GameStateStore gs, Ware ware) => ware.NurEinmal && ImRegal(gs, ware);

        /// <summary>Stock of a row at home (food count or item count).</summary>
        public static int Vorrat(GameStateStore gs, Ware ware) => ware.IsFood
            ? J.Int(J.Path(gs.State, "inventory", "food", ware.Id))
            : J.Int(J.Path(gs.State, "inventory", "items", ware.Inventar));

        public const string PflasterTagKey = "pflaster_tag";

        /// <summary>The plaster heals once a day (Heldenregel) — true when today's is used up.</summary>
        public static bool PflasterHeuteVerbraucht(GameStateStore gs) =>
            gs != null && J.Str(J.Path(gs.State, "city", PflasterTagKey)) == gs.Clock.LocalDay();

        /// <summary>
        /// Purchase: coins via Economy.Spend, `menge` units into the inventory, recipe consumed when required. With a
        /// <paramref name="laden"/> the purchase stamps its loyalty card (<see cref="ShopTreue"/>); <paramref name="preis"/>
        /// is the day's deal price.
        /// </summary>
        public static bool Kaufe(GameStateStore gs, Ware ware, string laden = null, int? preis = null)
        {
            if (gs == null || ware == null) return false;
            var zahlt = preis ?? ware.Preis;
            if (zahlt > Coins(gs) || ware.BrauchtRezept && !HatRezept(gs)) return false;
            if (Ausverkauft(gs, ware)) return false;
            if (ware.IsEffekt && PflasterHeuteVerbraucht(gs)) return false;
            var ok = false;
            var today = gs.Clock.LocalDay();
            var now = gs.Clock.NowMs();
            gs.Update(state =>
            {
                if (!Economy.Spend(J.EnsureDict(state, "economy"), zahlt, "laden")) return;
                if (laden != null) ShopTreue.Buche(state, laden, ware.Id, zahlt, now, preis.HasValue);
                var inventory = J.EnsureDict(state, "inventory");
                if (ware.IsEffekt)
                {
                    // GOOBYMAN plaster: `heilt` points off the health pressure right away, remembered per day
                    var gooby = J.EnsureDict(state, "gooby");
                    var health = Health.Normalize(gooby["health"]);
                    health["junkScore"] = System.Math.Max(0.0, J.Num(health["junkScore"]) - J.NumOr(ware.Raw["heilt"], 5.0));
                    gooby["health"] = health;
                    J.EnsureDict(state, "city")[PflasterTagKey] = today;
                }
                else if (ware.IsFood)
                {
                    var food = J.EnsureDict(inventory, "food");
                    food[ware.Id] = J.Int(food[ware.Id]) + ware.Menge;
                }
                else
                {
                    var items = J.EnsureDict(inventory, "items");
                    items[ware.Inventar] = J.Int(items[ware.Inventar]) + ware.Menge;
                }
                if (ware.BrauchtRezept) J.EnsureDict(J.EnsureDict(state, "city"), "flags").Remove(FlagRezept);
                ok = true;
            });
            if (ok) gs.NotifySliceChanged("inventory");
            return ok;
        }
    }
}
