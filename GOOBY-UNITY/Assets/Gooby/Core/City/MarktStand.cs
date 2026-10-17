using System;
using System.Collections.Generic;
using System.Linq;
using Gooby.Core.Content;
using Gooby.Core.Home;
using Gooby.Core.Json;
using Gooby.Core.Life;
using Gooby.Core.Random;
using Gooby.Core.State;
using Newtonsoft.Json.Linq;

namespace Gooby.Core.City
{
    /// <summary>
    /// Wares of the player's own market stall (city/markt_waren.json) — port of markt_waren.gd. Three sources: garden
    /// harvest ("ernte", inventory.food, base = the market's buying price), workshop pieces ("craft": furniture in
    /// home.storage at its resale value, the herb bundle in inventory.items) and souvenirs ("souvenir", inventory.food).
    /// Taking and putting back is always lossless — putting back never checks the storage capacity.
    /// </summary>
    public static class MarktWaren
    {
        public sealed class Eintrag
        {
            public string Id, Kategorie, Lager, NameDe, NameEn;
            public int Basis;
        }

        public sealed class Angebot
        {
            public string Id, Kategorie;
            public int Vorrat, Basis;
        }

        private static readonly string[] Kategorien = { "ernte", "craft", "souvenir" };
        private static readonly string[] LagerTypen = { "food", "items", "storage" };
        private static List<Eintrag> _cache;

        public static IReadOnlyList<Eintrag> Waren()
        {
            if (_cache != null) return _cache;
            _cache = new List<Eintrag>();
            foreach (var t in ContentSource.LoadObject(ShopLogic.MarktWarenPath)["waren"] as JArray ?? new JArray())
            {
                if (!(t is JObject o)) continue;
                var id = J.Str(o["id"]);
                var kategorie = J.Str(o["kategorie"]);
                var lager = J.Str(o["lager"]);
                if (id.Length == 0 || Array.IndexOf(Kategorien, kategorie) < 0 || Array.IndexOf(LagerTypen, lager) < 0) continue;
                _cache.Add(new Eintrag
                {
                    Id = id, Kategorie = kategorie, Lager = lager, Basis = Math.Max(0, J.Int(o["basis"])),
                    NameDe = J.Str(o["name_de"]), NameEn = J.Str(o["name_en"], J.Str(o["name_de"])),
                });
            }
            return _cache;
        }

        public static Eintrag Ware(string id) => Waren().FirstOrDefault(w => w.Id == id);

        public static void ResetCache() => _cache = null;

        /// <summary>Base value of ONE unit (middle of the price slider): explicit, furniture resale value or the market price.</summary>
        public static int Basis(string id)
        {
            var w = Ware(id);
            if (w == null) return 0;
            if (w.Basis > 0) return w.Basis;
            if (w.Lager == "storage") return Math.Max(1, FurnitureCatalog.Def(id)?.Verkaufswert ?? 0);
            return MarktLogic.Marktpreis(id);
        }

        /// <summary>German/English fallback name (the UI prefers its own string keys where they exist).</summary>
        public static string Anzeigename(string id, string locale = "de")
        {
            var w = Ware(id);
            if (w == null) return id;
            var eigen = locale == "en" ? w.NameEn : w.NameDe;
            if (!string.IsNullOrEmpty(eigen)) return eigen;
            if (w.Lager == "storage") return FurnitureCatalog.Def(id)?.DisplayName(locale) ?? id;
            return MarktLogic.SorteVon(id)?.NameDe ?? id;
        }

        public static int Vorrat(GameStateStore gs, string id)
        {
            if (gs == null) return 0;
            switch (Ware(id)?.Lager)
            {
                case "food": return Math.Max(0, J.Int(J.Path(gs.State, "inventory", "food", id)));
                case "items": return Math.Max(0, J.Int(J.Path(gs.State, "inventory", "items", id)));
                case "storage": return StorageLogic.CountOf(HomeState.Storage(gs), id);
            }
            return 0;
        }

        /// <summary>What could go on the stall right now: every ware with stock, in catalogue order.</summary>
        public static List<Angebot> AngebotDesSpielers(GameStateStore gs)
        {
            var outp = new List<Angebot>();
            foreach (var w in Waren())
            {
                var n = Vorrat(gs, w.Id);
                if (n > 0) outp.Add(new Angebot { Id = w.Id, Kategorie = w.Kategorie, Vorrat = n, Basis = Basis(w.Id) });
            }
            return outp;
        }

        /// <summary>Takes up to <paramref name="menge"/> out of the right store (mutates state). Returns what was really taken.</summary>
        public static int Nimm(JObject state, string id, int menge)
        {
            if (menge <= 0) return 0;
            switch (Ware(id)?.Lager)
            {
                case "food": return NimmAus(J.EnsureDict(J.EnsureDict(state, "inventory"), "food"), id, menge);
                case "items": return NimmAus(J.EnsureDict(J.EnsureDict(state, "inventory"), "items"), id, menge);
                case "storage":
                    var storage = StorageOf(state);
                    var genommen = 0;
                    for (var i = 0; i < menge && StorageLogic.Take(storage, id); i++) genommen++;
                    return genommen;
            }
            return 0;
        }

        /// <summary>Puts units back (mutates state) — lossless, even into a full furniture storage.</summary>
        public static void Zurueck(JObject state, string id, int menge)
        {
            if (menge <= 0) return;
            switch (Ware(id)?.Lager)
            {
                case "food":
                    var food = J.EnsureDict(J.EnsureDict(state, "inventory"), "food");
                    food[id] = Math.Max(0, J.Int(food[id])) + menge;
                    break;
                case "items":
                    var items = J.EnsureDict(J.EnsureDict(state, "inventory"), "items");
                    items[id] = Math.Max(0, J.Int(items[id])) + menge;
                    break;
                case "storage":
                    var storage = StorageOf(state);
                    for (var i = 0; i < menge; i++) StorageLogic.Add(storage, id);
                    break;
            }
        }

        private static int NimmAus(JObject map, string id, int menge)
        {
            var da = Math.Max(0, J.Int(map[id]));
            var genommen = Math.Min(da, menge);
            map[id] = da - genommen;
            return genommen;
        }

        private static JArray StorageOf(JObject state)
        {
            var home = J.EnsureDict(state, "home");
            if (!(home["storage"] is JArray)) home["storage"] = new JArray();
            return (JArray)home["storage"];
        }
    }

    /// <summary>
    /// The deterministic selling day of the own stall — port of markt_sim.gd (pure, no clock). A market day is fixed by
    /// (slots, day seed): the seed draws every customer's arrival, wish and purchase roll up front; the price sliders only
    /// change the buying chance, never the random sequence. Same seed + same prices = same result, and cheaper never
    /// sells less. Chance = price appeal × the day's favourite × saturation (5 % less per unit already sold, floor 50 %).
    /// </summary>
    public static class MarktSim
    {
        public const int KundenMin = 16;
        public const int KundenMax = 28;
        public const double NachfrageMult = 1.5;
        public const double AttraktivAchse = 1.25;
        public const double AttraktivSteigung = 0.75;
        public const double PMin = 0.05;
        public const double PMax = 0.95;
        private const long SeedSalt = 0x4D41524B;

        public sealed class Slot
        {
            public string Ware;
            public int Menge;
            public double Faktor = 1.0;
        }

        public sealed class Event
        {
            public int Minute, Preis;
            public string Ware;
            public bool Gekauft;
        }

        public sealed class Ergebnis
        {
            public List<Event> Events = new List<Event>();
            public Dictionary<string, int> Verkauft = new Dictionary<string, int>();
            public int Erloes, Kunden;
            public string Lieblingsware = "";
        }

        public sealed class Zeile
        {
            public string Ware;
            public int Bestueckt, Verkauft, Uebrig, Stueckpreis, Erloes;
        }

        public sealed class Abrechnung
        {
            public List<Zeile> Zeilen = new List<Zeile>();
            public int Erloes;
            public string BesteWare = "", Lieblingsware = "";
        }

        public static long TagesSeed(string tagKey) => RanchWetter.TagesSeed(tagKey ?? "", SeedSalt);

        /// <summary>"Today everyone loves …!" — drawn from the whole catalogue, so the sign shows it even when the player has none.</summary>
        public static string Lieblingsware(long seed)
        {
            var alle = MarktWaren.Waren();
            if (alle.Count == 0) return "";
            return alle[(int)(((seed % alle.Count) + alle.Count) % alle.Count)].Id;
        }

        public static int Stueckpreis(string ware, double faktor)
        {
            var basis = MarktWaren.Basis(ware);
            return basis <= 0 ? 0 : Math.Max(1, (int)Math.Round(basis * faktor, MidpointRounding.AwayFromZero));
        }

        public static double Attraktivitaet(double faktor) => J.Clamp(AttraktivAchse - AttraktivSteigung * faktor, PMin, PMax);

        public static double Saettigung(int schonVerkauft)
        {
            var d = ContentSource.LoadObject(ShopLogic.MarktPreisePath);
            var elastizitaet = J.NumOr(d["elastizitaet_pro_stueck"], 0.05);
            var boden = J.NumOr(d["preis_boden"], 0.5);
            return Math.Max(boden, 1.0 - elastizitaet * Math.Max(0, schonVerkauft));
        }

        public static double KaufChance(Slot slot, int schonVerkauft, string lieblingsware)
        {
            var p = Attraktivitaet(slot.Faktor);
            if (lieblingsware == slot.Ware) p *= NachfrageMult;
            p *= Saettigung(schonVerkauft);
            return J.Clamp(p, 0.0, 0.98);
        }

        public static Ergebnis Simulate(IList<Slot> slots, long seed, int vonStunde = 8, int bisStunde = 14)
        {
            var rng = new GoobyRng(seed);
            var outp = new Ergebnis { Lieblingsware = Lieblingsware(seed) };
            outp.Kunden = rng.RangeI(KundenMin, KundenMax);
            var minuten = Math.Max(1, (bisStunde - vonStunde) * 60);
            // the whole random sequence first (arrival jitter, wish, purchase roll) — it must never depend on prices
            var lose = new double[outp.Kunden, 3];
            for (var i = 0; i < outp.Kunden; i++)
                for (var k = 0; k < 3; k++) lose[i, k] = rng.Next();
            if (slots == null || slots.Count == 0) return outp;
            for (var i = 0; i < outp.Kunden; i++)
            {
                var minute = (int)((i + lose[i, 0]) * minuten / outp.Kunden);
                var slot = slots[Math.Min((int)(lose[i, 1] * slots.Count), slots.Count - 1)];
                outp.Verkauft.TryGetValue(slot.Ware, out var schon);
                // sold out: the customer moves on (no switching keeps the day explainable and the monotony provable)
                if (schon >= slot.Menge) continue;
                var gekauft = lose[i, 2] < KaufChance(slot, schon, outp.Lieblingsware);
                var preis = Stueckpreis(slot.Ware, slot.Faktor);
                if (gekauft)
                {
                    outp.Verkauft[slot.Ware] = schon + 1;
                    outp.Erloes += preis;
                }
                outp.Events.Add(new Event { Minute = Math.Max(0, Math.Min(minuten - 1, minute)), Ware = slot.Ware, Preis = preis, Gekauft = gekauft });
            }
            return outp;
        }

        /// <summary>The settlement card: one exact row per ware (sold × unit price), total, leftovers and the best ware.</summary>
        public static Abrechnung Abrechnen(IList<Slot> slots, Ergebnis ergebnis)
        {
            var karte = new Abrechnung { Lieblingsware = ergebnis?.Lieblingsware ?? "" };
            var besteErloes = -1;
            foreach (var slot in slots ?? new List<Slot>())
            {
                var verkauft = 0;
                ergebnis?.Verkauft.TryGetValue(slot.Ware, out verkauft);
                var weg = Math.Min(verkauft, slot.Menge);
                var preis = Stueckpreis(slot.Ware, slot.Faktor);
                var zeile = new Zeile { Ware = slot.Ware, Bestueckt = slot.Menge, Verkauft = weg, Uebrig = slot.Menge - weg, Stueckpreis = preis, Erloes = weg * preis };
                karte.Erloes += zeile.Erloes;
                if (zeile.Erloes > besteErloes && weg > 0)
                {
                    besteErloes = zeile.Erloes;
                    karte.BesteWare = slot.Ware;
                }
                karte.Zeilen.Add(zeile);
            }
            return karte;
        }
    }

    /// <summary>
    /// The player's own market stall — port of markt_stand.gd. Stock it with wares from the stores and set a price per
    /// ware (±50 % around the base value); it binds to the next market day (Saturday in the town plan's window,
    /// 8–14). While the market runs the stall can still be restocked and repriced — the day is simulated only when the
    /// player watches or collects, and that settlement books proceeds, puts leftovers back and empties the stall.
    /// Save: city.marktstand = {tag: "YYYY-MM-DD", slots: [{ware, menge, faktor}]}.
    /// </summary>
    public static class MarktStand
    {
        public const int SlotMax = 8;
        public const double FaktorMin = 0.5;
        public const double FaktorMax = 1.5;
        public const string StatusLeer = "leer";
        public const string StatusWartet = "wartet";
        public const string StatusLaeuft = "laeuft";
        public const string StatusFertig = "fertig";
        /// <summary>achievements.counters key: settled market days at the own stall.</summary>
        public const string CounterMarktTage = "marktTage";

        public static int MarktTage(GameStateStore gs) => J.Int(J.Path(gs?.State, "achievements", "counters", CounterMarktTage));

        /// <summary>The day key the stall is (or would be) bound to — for the "everyone loves …" sign.</summary>
        public static string AnzeigeTag(GameStateStore gs, long nowMs, int biasMinutes)
        {
            var tag = SliceVon(gs).Tag;
            return string.IsNullOrEmpty(tag) ? NaechsterMarktTag(nowMs, biasMinutes) : tag;
        }

        public sealed class Slice
        {
            public string Tag = "";
            public List<MarktSim.Slot> Slots = new List<MarktSim.Slot>();
        }

        /// <summary>Market window {von, bis} from the town plan (CityMap opening rule), fallback Saturday 8–14.</summary>
        public static (int von, int bis) Marktfenster()
        {
            var regel = CityMap.Get("wochenmarkt")?.Oeffnung;
            return regel == null ? (8, 14) : ((int)regel.VonStunde, (int)Math.Ceiling(regel.BisStunde));
        }

        public static double ClampFaktor(double faktor) => Math.Round(J.Clamp(faktor, FaktorMin, FaktorMax) / 0.05, MidpointRounding.AwayFromZero) * 0.05;

        public static Slice SliceVon(GameStateStore gs)
        {
            var outp = new Slice();
            if (!(J.Path(gs?.State, "city", "marktstand") is JObject raw)) return outp;
            outp.Tag = J.Str(raw["tag"]);
            foreach (var t in raw["slots"] as JArray ?? new JArray())
            {
                if (!(t is JObject row)) continue;
                var ware = J.Str(row["ware"]);
                var menge = J.Int(row["menge"]);
                if (ware.Length == 0 || menge <= 0) continue;
                outp.Slots.Add(new MarktSim.Slot { Ware = ware, Menge = menge, Faktor = ClampFaktor(J.NumOr(row["faktor"], 1.0)) });
            }
            return outp;
        }

        /// <summary>Next sellable market day as a day key: Saturday before closing = today, else the coming Saturday.</summary>
        public static string NaechsterMarktTag(long nowMs, int biasMinutes)
        {
            var (wochentag, stunde) = CityMap.LokaleZeit(nowMs, biasMinutes);
            var tage = CityMap.TageBisSamstag(wochentag);
            if (tage == 0 && stunde >= Marktfenster().bis) tage = 7;
            return MarktLogic.TagesKey(nowMs + tage * 86400000L, biasMinutes);
        }

        public static string Status(GameStateStore gs, long nowMs, int biasMinutes)
        {
            var slice = SliceVon(gs);
            if (slice.Slots.Count == 0) return StatusLeer;
            var heute = MarktLogic.TagesKey(nowMs, biasMinutes);
            var cmp = string.CompareOrdinal(slice.Tag, heute);
            if (cmp > 0) return StatusWartet;
            if (cmp < 0) return StatusFertig;
            var stunde = CityMap.LokaleZeit(nowMs, biasMinutes).stunde;
            var (von, bis) = Marktfenster();
            if (stunde < von) return StatusWartet;
            return stunde < bis ? StatusLaeuft : StatusFertig;
        }

        /// <summary>Restocking is open until the day is settled — before the market and while it runs.</summary>
        public static bool KannBestuecken(string status) => status == StatusLeer || status == StatusWartet || status == StatusLaeuft;

        /// <summary>Puts up to <paramref name="menge"/> units of a ware on the stall (same ware → same slot). Returns the units moved.</summary>
        public static int Bestuecke(GameStateStore gs, long nowMs, int biasMinutes, string wareId, int menge, double faktor = 1.0)
        {
            if (gs == null || menge <= 0 || MarktWaren.Ware(wareId) == null) return 0;
            var status = Status(gs, nowMs, biasMinutes);
            if (!KannBestuecken(status)) return 0;
            var slice = SliceVon(gs);
            var slot = slice.Slots.FirstOrDefault(s => s.Ware == wareId);
            if (slot == null && slice.Slots.Count >= SlotMax) return 0;
            var tag = status == StatusLeer ? NaechsterMarktTag(nowMs, biasMinutes) : slice.Tag;
            var genommen = 0;
            gs.Update(state =>
            {
                genommen = MarktWaren.Nimm(state, wareId, menge);
                if (genommen <= 0) return;
                if (slot != null)
                {
                    slot.Menge += genommen;
                    slot.Faktor = ClampFaktor(faktor);
                }
                else slice.Slots.Add(new MarktSim.Slot { Ware = wareId, Menge = genommen, Faktor = ClampFaktor(faktor) });
                Schreibe(state, tag, slice.Slots);
            });
            if (genommen <= 0) return 0;
            Notify(gs, MarktWaren.Ware(wareId).Lager);
            return genommen;
        }

        /// <summary>Takes units back off the stall into the stores (lossless) — until the day is settled. Returns the units moved.</summary>
        public static int Entnehme(GameStateStore gs, long nowMs, int biasMinutes, string wareId, int menge)
        {
            if (gs == null || menge <= 0) return 0;
            var status = Status(gs, nowMs, biasMinutes);
            if (status != StatusWartet && status != StatusLaeuft) return 0;
            var slice = SliceVon(gs);
            var slot = slice.Slots.FirstOrDefault(s => s.Ware == wareId);
            if (slot == null) return 0;
            var weg = Math.Min(menge, slot.Menge);
            gs.Update(state =>
            {
                MarktWaren.Zurueck(state, wareId, weg);
                slot.Menge -= weg;
                if (slot.Menge <= 0) slice.Slots.Remove(slot);
                Schreibe(state, slice.Slots.Count == 0 ? "" : slice.Tag, slice.Slots);
            });
            Notify(gs, MarktWaren.Ware(wareId)?.Lager);
            return weg;
        }

        public static bool SetFaktor(GameStateStore gs, long nowMs, int biasMinutes, string wareId, double faktor)
        {
            if (gs == null) return false;
            var status = Status(gs, nowMs, biasMinutes);
            if (status != StatusWartet && status != StatusLaeuft) return false;
            var slice = SliceVon(gs);
            var slot = slice.Slots.FirstOrDefault(s => s.Ware == wareId);
            if (slot == null) return false;
            slot.Faktor = ClampFaktor(faktor);
            gs.Update(state => Schreibe(state, slice.Tag, slice.Slots));
            gs.NotifySliceChanged("city");
            return true;
        }

        /// <summary>The bound market day's deterministic result (pure view, books nothing); null while the stall is empty.</summary>
        public static MarktSim.Ergebnis Ergebnis(GameStateStore gs)
        {
            var slice = SliceVon(gs);
            if (slice.Slots.Count == 0) return null;
            var (von, bis) = Marktfenster();
            return MarktSim.Simulate(slice.Slots, MarktSim.TagesSeed(slice.Tag), von, bis);
        }

        /// <summary>
        /// Settles the day (market running or over): proceeds to the purse, leftovers back into the stores, the `sells`
        /// counter up, stall emptied. Null when there is nothing to collect.
        /// </summary>
        public static MarktSim.Abrechnung Abholen(GameStateStore gs, long nowMs, int biasMinutes)
        {
            if (gs == null) return null;
            var status = Status(gs, nowMs, biasMinutes);
            if (status != StatusLaeuft && status != StatusFertig) return null;
            var slice = SliceVon(gs);
            var karte = MarktSim.Abrechnen(slice.Slots, Ergebnis(gs));
            gs.Update(state =>
            {
                foreach (var z in karte.Zeilen) MarktWaren.Zurueck(state, z.Ware, z.Uebrig);
                if (karte.Erloes > 0) Economy.Award(J.EnsureDict(state, "economy"), karte.Erloes, "marktstand");
                var stueck = karte.Zeilen.Sum(z => z.Verkauft);
                var counters = J.EnsureDict(J.EnsureDict(state, "achievements"), "counters");
                if (stueck > 0) counters["sells"] = J.Int(counters["sells"]) + stueck;
                counters[CounterMarktTage] = J.Int(counters[CounterMarktTage]) + 1;
                Schreibe(state, "", new List<MarktSim.Slot>());
            });
            gs.NotifySliceChanged("city");
            gs.NotifySliceChanged("inventory");
            gs.NotifySliceChanged("home");
            gs.NotifySliceChanged("economy");
            gs.NotifySliceChanged("achievements");
            return karte;
        }

        private static void Schreibe(JObject state, string tag, IEnumerable<MarktSim.Slot> slots)
        {
            var arr = new JArray();
            foreach (var s in slots) arr.Add(new JObject { ["ware"] = s.Ware, ["menge"] = s.Menge, ["faktor"] = s.Faktor });
            J.EnsureDict(state, "city")["marktstand"] = new JObject { ["tag"] = tag, ["slots"] = arr };
        }

        private static void Notify(GameStateStore gs, string lager)
        {
            gs.NotifySliceChanged("city");
            gs.NotifySliceChanged(lager == "storage" ? "home" : "inventory");
        }
    }
}
