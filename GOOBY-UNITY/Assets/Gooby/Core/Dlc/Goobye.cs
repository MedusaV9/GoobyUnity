using System;
using System.Collections.Generic;
using System.Linq;
using Gooby.Core.Content;
using Gooby.Core.Json;
using Gooby.Core.Life;
using Gooby.Core.Random;
using Newtonsoft.Json.Linq;

namespace Gooby.Core.Dlc
{
    /// <summary>
    /// The Goo und Bye range (port of goobye_katalog.gd): six ware groups, each coded by shape AND colour and with its own
    /// babble pitch for the till melody, and the wares with their guide price (REHWEI ids — what customers buy is real
    /// Gooby food). Balance (price, unlock level, margins) from content/dlc/data/balance → values.gooundbye.
    /// </summary>
    public static class GoobyeKatalog
    {
        public const string PackPath = "content/dlc/data/goobye_sortiment";
        public const string BalancePath = "content/dlc/data/balance";
        public const int DefaultPreis = 2500;
        public const int DefaultFreischaltLevel = 12;

        private static JObject Bal() => J.Path(ContentSource.LoadObject(BalancePath), "values", "gooundbye") as JObject ?? new JObject();

        public static int Preis() => Math.Max(0, J.Int(Bal()["preis"], DefaultPreis));

        public static int FreischaltLevel() => Math.Max(1, J.Int(Bal()["freischalt_level"], DefaultFreischaltLevel));

        /// <summary>Wholesale price = this share of the recommended retail price (§2.2).</summary>
        public static double EkFaktor() => J.Clamp(J.NumOr(Bal()["ek_faktor"], 0.6), 0.05, 1.0);

        /// <summary>Price slider span ±x around the guide price (§2.2).</summary>
        public static double PreisSpanne() => J.Clamp(J.NumOr(Bal()["preis_spanne"], 0.3), 0.0, 0.9);

        public static double EigenmarkeRabatt() => J.Clamp(J.NumOr(Bal()["eigenmarke_rabatt"], 0.2), 0.0, 0.9);

        public static double BioAufschlag() => J.Clamp(J.NumOr(Bal()["bio_aufschlag"], 0.1), 0.0, 0.9);

        private static IEnumerable<JObject> Items() => ContentSource.LoadItems(PackPath).OfType<JObject>();

        public static List<JObject> Gruppen() => Items().Where(i => J.Str(i["typ"]) == "gruppe").ToList();

        public static JObject Gruppe(string id) => Items().FirstOrDefault(i => J.Str(i["typ"]) == "gruppe" && J.Str(i["id"]) == id);

        public static List<JObject> Waren() => Items().Where(i => J.Str(i["typ"]) == "ware").ToList();

        public static JObject Ware(string id) => Items().FirstOrDefault(i => J.Str(i["typ"]) == "ware" && J.Str(i["id"]) == id);

        public static List<JObject> WarenDerGruppe(string gruppeId) => Waren().Where(w => J.Str(w["gruppe"]) == gruppeId).ToList();

        public static string GruppeVon(string wareId) => J.Str(Ware(wareId)?["gruppe"]);

        /// <summary>The opening package (the 300-coin voucher of §2.2) from the pack's start fields.</summary>
        public static Dictionary<string, int> Startlager()
        {
            var outp = new Dictionary<string, int>();
            foreach (var w in Waren())
            {
                var n = J.Int(w["start"]);
                if (n > 0) outp[J.Str(w["id"])] = n;
            }
            return outp;
        }

        /// <summary>The till melody's pitch for a ware — its group's babble tone, 1.0 when unknown.</summary>
        public static double TonFuer(string wareId)
        {
            var w = Ware(wareId);
            if (w == null) return 1.0;
            var g = Gruppe(J.Str(w["gruppe"]));
            return J.Clamp(J.NumOr(g?["ton"], 1.0), 0.9, 1.6);
        }
    }

    /// <summary>Prices and margins (port of goobye_preis.gd): one felt curve, no spreadsheet.</summary>
    public static class GoobyePreis
    {
        public const double GriffSteigung = 1.2, GriffMin = 0.3, GriffMax = 1.0, SpontanSteigung = 0.8, SpontanMax = 0.24;

        /// <summary>GDScript roundi: half away from zero.</summary>
        public static int Rund(double x) => (int)Math.Round(x, MidpointRounding.AwayFromZero);

        /// <summary>The "recommended price" button: guide price, house brand −20 %, organic +10 %, never below 1.</summary>
        public static int EmpfohlenerPreis(JObject ware)
        {
            var basis = Math.Max(0.0, J.NumOr(ware?["vk"], 0));
            if (J.IsTrue(ware?["eigenmarke"])) basis *= 1.0 - GoobyeKatalog.EigenmarkeRabatt();
            if (J.IsTrue(ware?["bio"])) basis *= 1.0 + GoobyeKatalog.BioAufschlag();
            return Math.Max(1, Rund(basis));
        }

        public static int Einkaufspreis(JObject ware) => Math.Max(1, Rund(EmpfohlenerPreis(ware) * GoobyeKatalog.EkFaktor()));

        public static double FaktorBegrenzen(double faktor)
        {
            var spanne = GoobyeKatalog.PreisSpanne();
            return J.Clamp(faktor, 1.0 - spanne, 1.0 + spanne);
        }

        public static int Verkaufspreis(JObject ware, double faktor = 1.0) => Math.Max(1, Rund(EmpfohlenerPreis(ware) * FaktorBegrenzen(faktor)));

        public static int Marge(JObject ware, double faktor = 1.0) => Verkaufspreis(ware, faktor) - Einkaufspreis(ware);

        /// <summary>Grab chance: sure at or below the guide price, falling linearly above it.</summary>
        public static double GriffChance(double faktor = 1.0) =>
            J.Clamp(GriffMax - Math.Max(0.0, FaktorBegrenzen(faktor) - 1.0) * GriffSteigung, GriffMin, GriffMax);

        /// <summary>Impulse bonus below the guide price: cheaper = bigger baskets.</summary>
        public static double SpontanBonus(double faktor = 1.0) => J.Clamp((1.0 - FaktorBegrenzen(faktor)) * SpontanSteigung, 0.0, SpontanMax);
    }

    /// <summary>
    /// The shelf (port of goobye_regal.gd): one row of slots, each holding one ware up to the slot cap. No spoilage — an
    /// empty shelf is a to-do note, never a penalty. Unity keeps it in the save (dlc.goobye.regal) so stocked shelves
    /// wait for the next visit.
    /// </summary>
    public sealed class GoobyeRegal
    {
        public const int StandardSlots = 5;
        public const int MaxJeSlot = 8;

        public sealed class Slot
        {
            public string Ware = "";
            public int Menge;
        }

        public sealed class Zeile
        {
            public string Id;
            public int Bestand;
            public double Faktor = 1.0;
            public bool Angebot;
        }

        public readonly List<Slot> Slots = new List<Slot>();

        public GoobyeRegal(int slots = StandardSlots)
        {
            for (var i = 0; i < Math.Max(1, slots); i++) Slots.Add(new Slot());
        }

        /// <summary>Self-heal from save data: types repaired, slot cap applied, always the full slot count.</summary>
        public static GoobyeRegal Normalisieren(JToken raw, int slots = StandardSlots)
        {
            var regal = new GoobyeRegal(slots);
            if (!(raw is JObject o) || !(o["slots"] is JArray quelle)) return regal;
            for (var i = 0; i < Math.Min(quelle.Count, regal.Slots.Count); i++)
            {
                if (!(quelle[i] is JObject s)) continue;
                var ware = J.Str(s["ware"]);
                var menge = J.ClampI(J.Int(s["menge"]), 0, MaxJeSlot);
                if (ware.Length == 0 || menge <= 0) continue;
                regal.Slots[i].Ware = ware;
                regal.Slots[i].Menge = menge;
            }
            return regal;
        }

        public JObject ToJson() => new JObject
        {
            ["slots"] = new JArray(Slots.Select(s => new JObject { ["ware"] = s.Menge > 0 ? s.Ware : "", ["menge"] = Math.Max(0, s.Menge) })),
        };

        /// <summary>Stocking: moves up to <paramref name="menge"/> of the ware from the stores into the slot (empty or the same ware).</summary>
        public int Einraeumen(int slotIdx, string wareId, int menge, Dictionary<string, int> lager)
        {
            if (slotIdx < 0 || slotIdx >= Slots.Count || string.IsNullOrEmpty(wareId) || menge <= 0) return 0;
            var slot = Slots[slotIdx];
            if (slot.Ware.Length > 0 && slot.Ware != wareId) return 0;
            lager.TryGetValue(wareId, out var vorrat);
            vorrat = Math.Max(0, vorrat);
            var bewegt = Math.Min(menge, Math.Min(vorrat, MaxJeSlot - slot.Menge));
            if (bewegt <= 0) return 0;
            slot.Ware = wareId;
            slot.Menge += bewegt;
            lager[wareId] = vorrat - bewegt;
            if (lager[wareId] <= 0) lager.Remove(wareId);
            return bewegt;
        }

        /// <summary>A customer takes up to <paramref name="menge"/> pieces; emptied slots are cleared.</summary>
        public int Entnehmen(string wareId, int menge = 1)
        {
            var rest = Math.Max(0, menge);
            foreach (var slot in Slots)
            {
                if (rest <= 0) break;
                if (slot.Ware != wareId) continue;
                var weg = Math.Min(rest, slot.Menge);
                slot.Menge -= weg;
                rest -= weg;
                if (slot.Menge <= 0) { slot.Ware = ""; slot.Menge = 0; }
            }
            return menge - rest;
        }

        /// <summary>Empties a slot back into the stores (nothing is lost).</summary>
        public int Ausraeumen(int slotIdx, Dictionary<string, int> lager)
        {
            if (slotIdx < 0 || slotIdx >= Slots.Count) return 0;
            var slot = Slots[slotIdx];
            if (slot.Ware.Length == 0 || slot.Menge <= 0) return 0;
            lager.TryGetValue(slot.Ware, out var vorrat);
            lager[slot.Ware] = vorrat + slot.Menge;
            var n = slot.Menge;
            slot.Ware = "";
            slot.Menge = 0;
            return n;
        }

        public int Bestand(string wareId) => Slots.Where(s => s.Ware == wareId).Sum(s => Math.Max(0, s.Menge));

        public int GesamtBestand() => Slots.Sum(s => Math.Max(0, s.Menge));

        /// <summary>The listed rows for the market day: one per stocked ware with its price factor.</summary>
        public List<Zeile> SortimentVon(IDictionary<string, double> faktoren = null)
        {
            var gesehen = new HashSet<string>();
            var outp = new List<Zeile>();
            foreach (var slot in Slots)
            {
                if (slot.Ware.Length == 0 || slot.Menge <= 0 || !gesehen.Add(slot.Ware)) continue;
                var f = 1.0;
                if (faktoren != null && faktoren.TryGetValue(slot.Ware, out var ff)) f = ff;
                outp.Add(new Zeile { Id = slot.Ware, Bestand = Bestand(slot.Ware), Faktor = f });
            }
            return outp;
        }

        /// <summary>Refill compass: indices of empty slots while the stores still hold something.</summary>
        public List<int> NachfuellVorschlag(IDictionary<string, int> lager)
        {
            if (lager == null || !lager.Values.Any(n => n > 0)) return new List<int>();
            return Enumerable.Range(0, Slots.Count).Where(i => Slots[i].Ware.Length == 0).ToList();
        }
    }

    /// <summary>
    /// The deterministic market day (port of goobye_markttag.gd): (seed, range, price factors) → the whole day plan with
    /// customers and receipts. All lots are drawn up front in a fixed block per customer, so prices and stock only change
    /// comparisons, never the random sequence — cheaper never sells less. Customer 0 is always Uncle Alwin and buys
    /// exactly one carrot; list Goobys keep a strict list, families add a pester-power sweet at the till, the hamster
    /// buys the rest of one ware.
    /// </summary>
    public static class GoobyeMarkttag
    {
        public const string ArchetypAlwin = "alwin", ArchetypListe = "listen_gooby", ArchetypFamilie = "familie", ArchetypHamster = "hamster_gooby";
        public const int TagMinuten = 720, AlwinMinute = 60;
        public const string AlwinWare = "carrot";
        public const int KundenMin = 3, KundenMax = 5, MaxPositionen = 8, HamsterMax = 4;
        public const double QuengelChance = 0.6;

        public sealed class Position
        {
            public string Ware;
            public int Preis;
            public bool Quengel;
        }

        public sealed class Bon
        {
            public int Kunde, Minute, Summe;
            public string Archetyp;
            public readonly List<Position> Positionen = new List<Position>();
            /// <summary>Wares the customer reached for in front of an empty shelf (the sad tick of the list Gooby).</summary>
            public readonly List<string> Verpasst = new List<string>();
        }

        public sealed class Plan
        {
            public int Kundenzahl, Umsatz, Verpasst;
            public readonly List<Bon> Bons = new List<Bon>();
            public readonly Dictionary<string, int> Verkauft = new Dictionary<string, int>();
        }

        private sealed class Lose
        {
            public double Archetyp, Jitter, Anzahl, Quengel;
            public readonly double[][] Positionen = new double[MaxPositionen][];
        }

        private sealed class Reihe
        {
            public string Id;
            public JObject Ware;
            public double Faktor;
            public bool Angebot;
        }

        /// <summary>A stable day seed from the day key ("YYYY-MM-DD") — Godot's String.hash.</summary>
        public static long TagesSeed(string tagKey) => GodotRng.StringHash(tagKey ?? "");

        public static Plan TagPlanen(long seed, IList<GoobyeRegal.Zeile> sortiment, int kundenMin = KundenMin, int kundenMax = KundenMax)
        {
            var reihen = new List<Reihe>();
            foreach (var z in sortiment ?? new List<GoobyeRegal.Zeile>())
            {
                if (z == null) continue;
                var ware = GoobyeKatalog.Ware(z.Id);
                if (ware == null) continue;
                reihen.Add(new Reihe { Id = z.Id, Ware = ware, Faktor = z.Faktor, Angebot = z.Angebot });
            }
            var bestand = new Dictionary<string, int>();
            foreach (var z in sortiment ?? new List<GoobyeRegal.Zeile>())
                if (z != null && GoobyeKatalog.Ware(z.Id) != null) bestand[z.Id] = Math.Max(0, z.Bestand);
            var rng = new GodotRng(seed);
            var plan = new Plan { Kundenzahl = rng.RandIRange(kundenMin, kundenMax) };
            var lose = new List<Lose>();
            for (var i = 0; i < plan.Kundenzahl; i++)
            {
                var block = new Lose { Archetyp = rng.RandF(), Jitter = rng.RandF(), Anzahl = rng.RandF(), Quengel = rng.RandF() };
                for (var p = 0; p < MaxPositionen; p++) block.Positionen[p] = new double[] { rng.RandF(), rng.RandF() };
                lose.Add(block);
            }
            for (var i = 0; i < plan.Kundenzahl; i++)
            {
                var bon = Einkaufen(i, plan.Kundenzahl, lose[i], reihen, bestand);
                plan.Verpasst += bon.Verpasst.Count;
                foreach (var pos in bon.Positionen)
                {
                    plan.Verkauft.TryGetValue(pos.Ware, out var n);
                    plan.Verkauft[pos.Ware] = n + 1;
                    plan.Umsatz += pos.Preis;
                }
                bon.Summe = bon.Positionen.Sum(p => p.Preis);
                plan.Bons.Add(bon);
            }
            return plan;
        }

        /// <summary>The till melody of a receipt: per position its group's babble pitch.</summary>
        public static List<double> Melodie(Bon bon) => bon.Positionen.Select(p => GoobyeKatalog.TonFuer(p.Ware)).ToList();

        private static Bon Einkaufen(int index, int kundenzahl, Lose lose, List<Reihe> reihen, Dictionary<string, int> bestand)
        {
            var bon = new Bon
            {
                Kunde = index,
                Archetyp = ArchetypFuer(index, lose.Archetyp),
                Minute = J.ClampI((int)((index + lose.Jitter) * TagMinuten / Math.Max(1, kundenzahl)), 0, TagMinuten - 1),
            };
            switch (bon.Archetyp)
            {
                case ArchetypAlwin:
                    bon.Minute = AlwinMinute;
                    Greife(AlwinWare, reihen, bestand, bon);
                    break;
                case ArchetypHamster:
                    Hamster(lose, reihen, bestand, bon);
                    break;
                default:
                    Liste(bon.Archetyp, lose, reihen, bestand, bon);
                    break;
            }
            return bon;
        }

        private static string ArchetypFuer(int index, double los)
        {
            if (index == 0) return ArchetypAlwin;
            if (los < 0.45) return ArchetypListe;
            if (los < 0.85) return ArchetypFamilie;
            return ArchetypHamster;
        }

        private static void Liste(string archetyp, Lose lose, List<Reihe> reihen, Dictionary<string, int> bestand, Bon bon)
        {
            if (reihen.Count == 0) { bon.Verpasst.Add(""); return; }
            var deckel = archetyp == ArchetypListe ? 6 : 4;
            var minimum = archetyp == ArchetypListe ? 2 : 1;
            var maximal = Math.Min(deckel, reihen.Count);
            var anzahl = J.ClampI(minimum + (int)(lose.Anzahl * maximal), minimum, Math.Max(minimum, maximal));
            var gewaehlt = new List<int>();
            for (var p = 0; p < anzahl; p++)
            {
                var paar = lose.Positionen[p];
                var idx = DistinctIndex(paar[0], reihen.Count, gewaehlt);
                if (idx < 0) break;
                gewaehlt.Add(idx);
                var r = reihen[idx];
                var chance = GoobyePreis.GriffChance(r.Faktor) + GoobyePreis.SpontanBonus(r.Faktor);
                if (r.Angebot) chance += GoobyeAngebot.GriffBonus;
                if (paar[1] < chance) Greife(r.Id, reihen, bestand, bon);
            }
            if (archetyp == ArchetypFamilie) Quengelware(lose, reihen, bestand, bon);
        }

        private static void Hamster(Lose lose, List<Reihe> reihen, Dictionary<string, int> bestand, Bon bon)
        {
            if (reihen.Count == 0) { bon.Verpasst.Add(""); return; }
            var idx = J.ClampI((int)(lose.Positionen[0][0] * reihen.Count), 0, reihen.Count - 1);
            var id = reihen[idx].Id;
            bestand.TryGetValue(id, out var da);
            var menge = Math.Min(HamsterMax, da);
            if (menge <= 0) { bon.Verpasst.Add(id); return; }
            for (var n = 0; n < menge; n++) Greife(id, reihen, bestand, bon);
        }

        private static void Quengelware(Lose lose, List<Reihe> reihen, Dictionary<string, int> bestand, Bon bon)
        {
            Reihe beste = null;
            var besterPreis = 0;
            foreach (var r in reihen)
            {
                if (J.Str(r.Ware["gruppe"]) != "suesses") continue;
                var preis = GoobyeAngebot.PreisFuerZeile(r.Ware, r.Faktor, r.Angebot);
                if (beste == null || preis < besterPreis) { beste = r; besterPreis = preis; }
            }
            if (beste == null) return;
            var chance = QuengelChance + GoobyePreis.SpontanBonus(beste.Faktor);
            if (lose.Quengel >= chance) return;
            var vorher = bon.Positionen.Count;
            Greife(beste.Id, reihen, bestand, bon);
            if (bon.Positionen.Count > vorher) bon.Positionen[bon.Positionen.Count - 1].Quengel = true;
        }

        private static void Greife(string wareId, List<Reihe> reihen, Dictionary<string, int> bestand, Bon bon)
        {
            var r = reihen.FirstOrDefault(x => x.Id == wareId);
            if (r == null || !bestand.TryGetValue(wareId, out var da) || da <= 0) { bon.Verpasst.Add(wareId); return; }
            bestand[wareId] = da - 1;
            bon.Positionen.Add(new Position { Ware = wareId, Preis = GoobyeAngebot.PreisFuerZeile(r.Ware, r.Faktor, r.Angebot) });
        }

        private static int DistinctIndex(double los, int groesse, List<int> gewaehlt)
        {
            if (gewaehlt.Count >= groesse) return -1;
            var idx = J.ClampI((int)(los * groesse), 0, groesse - 1);
            for (var s = 0; s < groesse; s++)
            {
                if (!gewaehlt.Contains(idx)) return idx;
                idx = (idx + 1) % groesse;
            }
            return -1;
        }
    }

    /// <summary>
    /// The daily deal (port of goobye_angebot.gd): one ware group per day at −15 %, grabbed 40 % more eagerly — only
    /// comparisons and prices change, never the market day's random sequence. The paper sign over the shelf is one of six
    /// doodle styles, fixed by day and group.
    /// </summary>
    public static class GoobyeAngebot
    {
        public const double Rabatt = 0.15, GriffBonus = 0.4;
        public const int SchildVarianten = 6;
        public const string Ok = "ok", SchonGewaehlt = "schon_gewaehlt", Unbekannt = "unbekannte_gruppe";

        public static int AngebotsPreis(JObject ware, double faktor = 1.0) =>
            Math.Max(1, GoobyePreis.Rund(GoobyePreis.EmpfohlenerPreis(ware) * GoobyePreis.FaktorBegrenzen(faktor) * (1.0 - Rabatt)));

        public static int PreisFuerZeile(JObject ware, double faktor, bool angebot) =>
            angebot ? AngebotsPreis(ware, faktor) : GoobyePreis.Verkaufspreis(ware, faktor);

        public static string AktiveGruppe(JObject state, string tag)
        {
            var a = J.Path(state, GoobyeState.SliceId, GoobyeState.Key, "tagesangebot");
            var gruppe = J.Str(a?["gruppe"]);
            return gruppe.Length > 0 && J.Str(a?["tag"]) == tag ? gruppe : "";
        }

        /// <summary>Flags the deal group's rows (fresh copies, the source stays untouched).</summary>
        public static List<GoobyeRegal.Zeile> SortimentMarkieren(IEnumerable<GoobyeRegal.Zeile> sortiment, string gruppeId) =>
            sortiment.Select(z => new GoobyeRegal.Zeile
            {
                Id = z.Id, Bestand = z.Bestand, Faktor = z.Faktor,
                Angebot = z.Angebot || (gruppeId.Length > 0 && GoobyeKatalog.GruppeVon(z.Id) == gruppeId),
            }).ToList();

        public static int SchildVariante(string tag, string gruppeId) => (int)(GodotRng.StringHash(tag + "|" + gruppeId) % SchildVarianten);

        /// <summary>One group per day: the same group again is a quiet ok, another one waits for tomorrow.</summary>
        public static string Waehle(JObject state, string gruppeId, string tag)
        {
            if (state == null || GoobyeKatalog.Gruppe(gruppeId) == null) return Unbekannt;
            var aktiv = AktiveGruppe(state, tag);
            if (aktiv.Length > 0) return aktiv == gruppeId ? Ok : SchonGewaehlt;
            GoobyeState.Slice(state)["tagesangebot"] = new JObject { ["gruppe"] = gruppeId, ["tag"] = tag };
            return Ok;
        }
    }

    /// <summary>Cooling capacity (port of goobye_kuehl.gd): chilled wares need cooler room on the shelf instead of a freshness timer.</summary>
    public static class GoobyeKuehl
    {
        public const string GruppeKuehl = "kuehlware";
        public const int StueckJeModul = 8, ModulPreis = 150;
        public const string Ok = "ok", Pleite = "not_enough_coins";

        public static int Kapazitaet(int module) => Math.Max(0, module) * StueckJeModul;

        public static bool IstKuehlware(string wareId) => GoobyeKatalog.GruppeVon(wareId) == GruppeKuehl;

        public static int KuehlStueck(GoobyeRegal regal) => regal.Slots.Where(s => IstKuehlware(s.Ware)).Sum(s => Math.Max(0, s.Menge));

        /// <summary>How many of the ware may go onto the shelf now — dry goods unlimited, chilled up to the free cooler room.</summary>
        public static int Einraeumbar(GoobyeRegal regal, string wareId, int wunsch, int module)
        {
            if (wunsch <= 0) return 0;
            if (!IstKuehlware(wareId)) return wunsch;
            return J.ClampI(Kapazitaet(module) - KuehlStueck(regal), 0, wunsch);
        }

        public static int ModuleVon(JObject state) => Math.Max(1, J.Int(J.Path(state, GoobyeState.SliceId, GoobyeState.Key, "kuehlModule"), 1));

        /// <summary>Buys a cooler module: coins and counter in one step, or nothing.</summary>
        public static string KaufeModul(JObject state)
        {
            if (state == null || !Economy.Spend(J.EnsureDict(state, "economy"), ModulPreis, "gooundbye_kuehlmodul")) return Pleite;
            var s = GoobyeState.Slice(state);
            s["kuehlModule"] = Math.Max(1, J.Int(s["kuehlModule"], 1)) + 1;
            return Ok;
        }
    }

    /// <summary>
    /// The wholesale run (port of goobye_transport.gd): ordering pays and puts a trip into the save; the trip's phase is a
    /// pure function of the save's timestamps and the clock (out to the REHWEI ramp, loading per crate, back). One crate =
    /// one piece; the trunk decides how much fits in one run: small car 12, estate 24, the Goo und Bye van 48.
    /// </summary>
    public static class GoobyeTransport
    {
        public const int KistenKlein = 12, KistenKombi = 24, KistenLieferwagen = 48;
        public static readonly string[] KombiAutos = { "suv", "van" };
        public const long HinMs = 40_000, RueckMs = 40_000, BeladenJeKisteMs = 2_000;
        public const string PhaseHinfahrt = "hinfahrt", PhaseBeladen = "beladen", PhaseRueckfahrt = "rueckfahrt", PhaseDa = "da";
        public const string Ok = "ok", Leer = "korb_leer", ZuViel = "kofferraum_voll", Unterwegs = "schon_unterwegs", Pleite = "not_enough_coins",
            NichtDa = "noch_unterwegs", Gesperrt = "laden_level_zu_niedrig", SchonDa = "schon_gekauft";
        public const int LieferwagenPreis = 1500, LieferwagenLevel = 5;
        /// <summary>The express delivery (§4.1 Notlieferung): straight onto the stores at wholesale +50 %.</summary>
        public const double EilAufschlag = 0.5;

        public sealed class Status
        {
            public string Phase;
            public double Fortschritt;
            public long RestMs;
        }

        public static int KistenKapazitaet(string autoId, bool lieferwagen = false) =>
            lieferwagen ? KistenLieferwagen : Array.IndexOf(KombiAutos, autoId ?? "") >= 0 ? KistenKombi : KistenKlein;

        public static bool LieferwagenFrei(JObject state) => J.IsTrue(J.Path(state, GoobyeState.SliceId, GoobyeState.Key, "transport", "lieferwagen"));

        public static string AktivesAutoId(JObject state)
        {
            var id = J.Str(J.Path(state, "city", "aktivesAuto"));
            if (id.Length == 0) return "sedan";
            var eigene = J.Path(state, "city", "autos") as JObject;
            return id == "sedan" || (eigene != null && eigene[id] != null) ? id : "sedan";
        }

        public static int KapazitaetFuer(JObject state) => KistenKapazitaet(AktivesAutoId(state), LieferwagenFrei(state));

        public static Dictionary<string, int> KorbHeilen(IDictionary<string, int> korb)
        {
            var heil = new Dictionary<string, int>();
            if (korb == null) return heil;
            foreach (var kv in korb)
                if (kv.Value > 0 && GoobyeKatalog.Ware(kv.Key) != null) heil[kv.Key] = kv.Value;
            return heil;
        }

        public static int KistenImKorb(IDictionary<string, int> korb) => korb?.Values.Sum(n => Math.Max(0, n)) ?? 0;

        public static int Kosten(IDictionary<string, int> korb)
        {
            var summe = 0;
            if (korb == null) return 0;
            foreach (var kv in korb)
            {
                var w = GoobyeKatalog.Ware(kv.Key);
                if (w != null) summe += GoobyePreis.Einkaufspreis(w) * Math.Max(0, kv.Value);
            }
            return summe;
        }

        public static int EilPreis(JObject ware) => Math.Max(1, GoobyePreis.Rund(GoobyePreis.Einkaufspreis(ware) * (1.0 + EilAufschlag)));

        public static long FahrzeitMs(int kisten) => HinMs + Math.Max(0, kisten) * BeladenJeKisteMs + RueckMs;

        public static long AnkunftMs(long bestelltMs, int kisten) => bestelltMs + FahrzeitMs(kisten);

        public static Status StatusBei(long bestelltMs, long ankunftMs, long nowMs)
        {
            if (nowMs >= ankunftMs) return new Status { Phase = PhaseDa, Fortschritt = 1.0, RestMs = 0 };
            var dauer = Math.Max(1, ankunftMs - bestelltMs);
            var fortschritt = J.Clamp((double)(nowMs - bestelltMs) / dauer, 0.0, 1.0);
            var hinEnde = bestelltMs + Math.Min(HinMs, dauer);
            var rueckStart = ankunftMs - Math.Min(RueckMs, dauer);
            var phase = nowMs < hinEnde ? PhaseHinfahrt : nowMs >= rueckStart ? PhaseRueckfahrt : PhaseBeladen;
            return new Status { Phase = phase, Fortschritt = fortschritt, RestMs = ankunftMs - nowMs };
        }

        /// <summary>The running trip (null = none).</summary>
        public static JObject UnterwegsVon(JObject state)
        {
            var u = J.Path(state, GoobyeState.SliceId, GoobyeState.Key, "transport", "unterwegs") as JObject;
            return u != null && u["warenkorb"] is JObject k && k.HasValues ? u : null;
        }

        public static Status StatusVon(JObject state, long nowMs)
        {
            var u = UnterwegsVon(state);
            return u == null ? null : StatusBei(J.Long(u["bestelltAt"]), J.Long(u["ankunftAt"]), nowMs);
        }

        /// <summary>Orders a run: checks basket, trunk and a running trip, then pays and books the trip in one step.</summary>
        public static string Bestelle(JObject state, IDictionary<string, int> korb, long nowMs)
        {
            if (state == null) return Leer;
            var heil = KorbHeilen(korb);
            if (heil.Count == 0) return Leer;
            var kisten = KistenImKorb(heil);
            if (kisten > KapazitaetFuer(state)) return ZuViel;
            if (UnterwegsVon(state) != null) return Unterwegs;
            if (!Economy.Spend(J.EnsureDict(state, "economy"), Kosten(heil), "gooundbye_grossmarkt")) return Pleite;
            var warenkorb = new JObject();
            foreach (var kv in heil) warenkorb[kv.Key] = kv.Value;
            ((JObject)GoobyeState.Slice(state)["transport"])["unterwegs"] = new JObject
            {
                ["bestelltAt"] = nowMs, ["ankunftAt"] = AnkunftMs(nowMs, kisten), ["warenkorb"] = warenkorb,
            };
            return Ok;
        }

        /// <summary>"Alles ausladen": only after arrival — the whole basket goes into the stores, the trip is cleared.</summary>
        public static (bool ok, string grund, int kisten, Dictionary<string, int> warenkorb) Ausladen(JObject state, long nowMs)
        {
            var u = UnterwegsVon(state);
            if (u == null) return (false, Leer, 0, new Dictionary<string, int>());
            if (nowMs < J.Long(u["ankunftAt"])) return (false, NichtDa, 0, new Dictionary<string, int>());
            var korb = new Dictionary<string, int>();
            foreach (var p in ((JObject)u["warenkorb"]).Properties()) korb[p.Name] = Math.Max(0, J.Int(p.Value));
            var s = GoobyeState.Slice(state);
            var lager = (JObject)s["lager"];
            foreach (var kv in korb) lager[kv.Key] = J.Int(lager[kv.Key]) + kv.Value;
            ((JObject)s["transport"])["unterwegs"] = new JObject();
            return (true, Ok, KistenImKorb(korb), korb);
        }

        /// <summary>The express delivery: one piece straight into the stores at wholesale +50 %.</summary>
        public static string EilKauf(JObject state, string wareId, int menge = 1)
        {
            var w = GoobyeKatalog.Ware(wareId);
            if (state == null || w == null || menge <= 0) return Leer;
            if (!Economy.Spend(J.EnsureDict(state, "economy"), EilPreis(w) * menge, "gooundbye_eil")) return Pleite;
            var lager = (JObject)GoobyeState.Slice(state)["lager"];
            lager[wareId] = J.Int(lager[wareId]) + menge;
            return Ok;
        }

        /// <summary>The company van: from store level 5, coins and switch in one step.</summary>
        public static string KaufeLieferwagen(JObject state)
        {
            if (state == null) return Pleite;
            if (LieferwagenFrei(state)) return SchonDa;
            if (GoobyeLevel.LevelFuer(state) < LieferwagenLevel) return Gesperrt;
            if (!Economy.Spend(J.EnsureDict(state, "economy"), LieferwagenPreis, "gooundbye_lieferwagen")) return Pleite;
            ((JObject)GoobyeState.Slice(state)["transport"])["lieferwagen"] = true;
            return Ok;
        }
    }

    /// <summary>
    /// The store level 1–5 (port of goobye_level.gd): a pure function of the receipts book — market days and total
    /// takings, both only ever grow, so the level never drops.
    /// </summary>
    public static class GoobyeLevel
    {
        public sealed class Stufe
        {
            public int Level, Tage, Gesamt;
            public string NameKey;
        }

        public static readonly Stufe[] Stufen =
        {
            new Stufe { Level = 2, Tage = 2, Gesamt = 60, NameKey = "dlc_goobye.level.minimarkt" },
            new Stufe { Level = 3, Tage = 5, Gesamt = 200, NameKey = "dlc_goobye.level.frischemarkt" },
            new Stufe { Level = 4, Tage = 9, Gesamt = 420, NameKey = "dlc_goobye.level.supermarkt" },
            new Stufe { Level = 5, Tage = 14, Gesamt = 800, NameKey = "dlc_goobye.level.xxl" },
        };

        public const int LevelMin = 1, LevelMax = 5;
        public const string NameKeyStart = "dlc_goobye.level.marktstand";

        public static int LevelVon(int tage, int gesamt)
        {
            var level = LevelMin;
            foreach (var s in Stufen)
                if (tage >= s.Tage && gesamt >= s.Gesamt) level = s.Level;
            return level;
        }

        public static int LevelFuer(JObject state)
        {
            var u = J.Path(state, GoobyeState.SliceId, GoobyeState.Key, "umsatz");
            return LevelVon(Math.Max(0, J.Int(u?["tage"])), Math.Max(0, J.Int(u?["gesamt"])));
        }

        public static string NameKey(int level)
        {
            var l = J.ClampI(level, LevelMin, LevelMax);
            return Stufen.FirstOrDefault(s => s.Level == l)?.NameKey ?? NameKeyStart;
        }

        /// <summary>The next step and what is still missing (null at XXL).</summary>
        public static (Stufe stufe, int fehltTage, int fehltUmsatz)? Naechste(JObject state)
        {
            var level = LevelFuer(state);
            var next = Stufen.FirstOrDefault(s => s.Level == level + 1);
            if (next == null) return null;
            var u = J.Path(state, GoobyeState.SliceId, GoobyeState.Key, "umsatz");
            return (next, Math.Max(0, next.Tage - J.Int(u?["tage"])), Math.Max(0, next.Gesamt - J.Int(u?["gesamt"])));
        }

        /// <summary>Customers per market day grow with the store (Godot's scene ran 2–3 at the corner shop).</summary>
        public static (int min, int max) KundenFuer(int level)
        {
            switch (J.ClampI(level, LevelMin, LevelMax))
            {
                case 1: return (2, 3);
                case 2: return (2, 4);
                case 3: return (3, 4);
                case 4: return (3, 5);
                default: return (4, 6);
            }
        }
    }

    /// <summary>
    /// The store's save (port of goobye_state.gd) under dlc.goobye, healed in place on every access: bought, the offer and
    /// first-visit flags, stores (no spoilage), the receipts book, the wholesale trip, the daily deal, cooler modules —
    /// plus Unity's own keys: the stocked shelf, the market rounds of the day and the last celebrated level.
    /// </summary>
    public static class GoobyeState
    {
        public const string SliceId = "dlc";
        public const string Key = "goobye";

        public static JObject Slice(JObject state)
        {
            if (state == null) return Heal(new JObject());
            if (!(state[SliceId] is JObject dlc))
            {
                dlc = new JObject();
                state[SliceId] = dlc;
            }
            if (!(dlc[Key] is JObject g))
            {
                g = new JObject();
                dlc[Key] = g;
            }
            return Heal(g);
        }

        /// <summary>GDScript bool(): true, or a non-zero number.</summary>
        private static bool Truthy(JToken t) => J.IsTrue(t) || (J.IsNum(t) && J.Num(t) != 0.0);

        private static JObject Heal(JObject g)
        {
            g["v"] = Math.Max(1, J.Int(g["v"], 1));
            foreach (var k in new[] { "gekauft", "angebotGesehen", "angebotVerschoben", "erstbesuchGesehen" }) g[k] = Truthy(g[k]);
            g["gekauftAm"] = Math.Max(0L, J.Long(g["gekauftAm"]));
            g["lager"] = Mengen(g["lager"]);
            var umsatz = g["umsatz"] as JObject;
            if (umsatz == null) { umsatz = new JObject(); g["umsatz"] = umsatz; }
            foreach (var f in new[] { "tage", "gestern", "gesamt", "bester" }) umsatz[f] = Math.Max(0, J.Int(umsatz[f]));
            var transport = g["transport"] as JObject;
            if (transport == null) { transport = new JObject(); g["transport"] = transport; }
            transport["lieferwagen"] = Truthy(transport["lieferwagen"]);
            var u = transport["unterwegs"] as JObject;
            var korb = Mengen(u?["warenkorb"]);
            if (u == null || !korb.HasValues) transport["unterwegs"] = new JObject();
            else
            {
                var bestellt = Math.Max(0L, J.Long(u["bestelltAt"]));
                u["bestelltAt"] = bestellt;
                u["ankunftAt"] = Math.Max(bestellt, J.Long(u["ankunftAt"]));
                u["warenkorb"] = korb;
            }
            var angebot = g["tagesangebot"] as JObject;
            g["tagesangebot"] = new JObject { ["gruppe"] = J.Str(angebot?["gruppe"]), ["tag"] = J.Str(angebot?["tag"]) };
            g["kuehlModule"] = Math.Max(1, J.Int(g["kuehlModule"], 1));
            g["regal"] = GoobyeRegal.Normalisieren(g["regal"]).ToJson();
            var runde = g["runde"] as JObject;
            g["runde"] = new JObject { ["tag"] = J.Str(runde?["tag"]), ["n"] = Math.Max(0, J.Int(runde?["n"])) };
            g["levelGefeiert"] = J.ClampI(J.Int(g["levelGefeiert"], 1), GoobyeLevel.LevelMin, GoobyeLevel.LevelMax);
            var stats = g["stats"] as JObject;
            if (stats == null) { stats = new JObject(); g["stats"] = stats; }
            foreach (var f in new[] { "kunden", "artikel", "alwin", "hamster", "quengel" }) stats[f] = Math.Max(0, J.Int(stats[f]));
            return g;
        }

        private static JObject Mengen(JToken raw)
        {
            var heil = new JObject();
            if (raw is JObject o)
                foreach (var p in o.Properties())
                {
                    var n = J.Int(p.Value);
                    if (n > 0) heil[p.Name] = n;
                }
            return heil;
        }

        private static JObject Read(JObject state) => Slice((JObject)(state?.DeepClone() ?? new JObject()));

        public static bool IstGekauft(JObject state) => J.IsTrue(J.Path(state, SliceId, Key, "gekauft"));

        public static bool IstFreigeschaltet(JObject state) => J.Int(J.Path(state, "progression", "level"), 1) >= GoobyeKatalog.FreischaltLevel();

        public static void AngebotVerschieben(JObject state)
        {
            var s = Slice(state);
            s["angebotGesehen"] = true;
            s["angebotVerschoben"] = true;
        }

        /// <summary>The key handover card on the first visit: true only for that first call.</summary>
        public static bool ErstbesuchMerken(JObject state)
        {
            var s = Slice(state);
            if (J.IsTrue(s["erstbesuchGesehen"])) return false;
            s["erstbesuchGesehen"] = true;
            return true;
        }

        public static Dictionary<string, int> LagerVon(JObject state)
        {
            var outp = new Dictionary<string, int>();
            foreach (var p in ((JObject)Read(state)["lager"]).Properties()) outp[p.Name] = J.Int(p.Value);
            return outp;
        }

        public static void LagerSetzen(JObject state, IDictionary<string, int> lager)
        {
            var heil = new JObject();
            foreach (var kv in lager)
                if (kv.Value > 0) heil[kv.Key] = kv.Value;
            Slice(state)["lager"] = heil;
        }

        public static GoobyeRegal RegalVon(JObject state) => GoobyeRegal.Normalisieren(Read(state)["regal"]);

        public static void RegalSetzen(JObject state, GoobyeRegal regal) => Slice(state)["regal"] = regal.ToJson();

        /// <summary>The seed of today's next market round: Godot's day seed for the first, then one per further round.</summary>
        public static long RundenSeed(JObject state, string tag)
        {
            var runde = (JObject)Read(state)["runde"];
            var n = J.Str(runde["tag"]) == tag ? J.Int(runde["n"]) : 0;
            return GoobyeMarkttag.TagesSeed(n == 0 ? tag : tag + "#" + n);
        }

        public static void RundeVerbuchen(JObject state, string tag)
        {
            var runde = (JObject)Slice(state)["runde"];
            runde["n"] = J.Str(runde["tag"]) == tag ? J.Int(runde["n"]) + 1 : 1;
            runde["tag"] = tag;
        }

        /// <summary>The till count: books the day's takings into the receipts book (and the day's best).</summary>
        public static void UmsatzVerbuchen(JObject state, int betrag)
        {
            if (betrag < 0) return;
            var u = (JObject)Slice(state)["umsatz"];
            u["tage"] = J.Int(u["tage"]) + 1;
            u["gestern"] = betrag;
            u["gesamt"] = J.Int(u["gesamt"]) + betrag;
            u["bester"] = Math.Max(J.Int(u["bester"]), betrag);
        }

        public static void StatsVerbuchen(JObject state, GoobyeMarkttag.Plan plan)
        {
            if (plan == null) return;
            var st = (JObject)Slice(state)["stats"];
            st["kunden"] = J.Int(st["kunden"]) + plan.Bons.Count;
            st["artikel"] = J.Int(st["artikel"]) + plan.Bons.Sum(b => b.Positionen.Count);
            st["alwin"] = J.Int(st["alwin"]) + plan.Bons.Count(b => b.Archetyp == GoobyeMarkttag.ArchetypAlwin && b.Positionen.Count > 0);
            st["hamster"] = J.Int(st["hamster"]) + plan.Bons.Count(b => b.Archetyp == GoobyeMarkttag.ArchetypHamster && b.Positionen.Count > 0);
            st["quengel"] = J.Int(st["quengel"]) + plan.Bons.Sum(b => b.Positionen.Count(p => p.Quengel));
        }

        /// <summary>A level reached but not yet celebrated (0 = nothing new) — marks it celebrated.</summary>
        public static int LevelAufstieg(JObject state)
        {
            var s = Slice(state);
            var level = GoobyeLevel.LevelFuer(state);
            if (level <= J.Int(s["levelGefeiert"], 1)) return 0;
            s["levelGefeiert"] = level;
            return level;
        }
    }

    /// <summary>
    /// Buying the store (port of goobye_kauf.gd): coins, the bought flag and the opening package's stores in one step —
    /// or nothing.
    /// </summary>
    public static class GoobyeKauf
    {
        public const string Ok = "ok", Gesperrt = "level_zu_niedrig", Schon = "schon_gekauft", Pleite = "not_enough_coins";

        public static string Check(JObject state)
        {
            if (state == null) return Gesperrt;
            if (GoobyeState.IstGekauft(state)) return Schon;
            if (!GoobyeState.IstFreigeschaltet(state)) return Gesperrt;
            if (J.Int(J.Path(state, "economy", "coins")) < GoobyeKatalog.Preis()) return Pleite;
            return Ok;
        }

        public static string Kaufe(JObject state, long nowMs)
        {
            var grund = Check(state);
            if (grund != Ok) return grund;
            if (!Economy.Spend(J.EnsureDict(state, "economy"), GoobyeKatalog.Preis(), "gooundbye")) return Pleite;
            var s = GoobyeState.Slice(state);
            s["gekauft"] = true;
            s["gekauftAm"] = nowMs;
            s["angebotGesehen"] = true;
            s["angebotVerschoben"] = false;
            var lager = (JObject)s["lager"];
            foreach (var kv in GoobyeKatalog.Startlager()) lager[kv.Key] = J.Int(lager[kv.Key]) + kv.Value;
            return Ok;
        }
    }
}
