using System.Collections.Generic;
using System.Linq;
using Gooby.Core.Dlc;
using Gooby.Core.Json;
using Gooby.Core.Random;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Gooby.Tests.Core
{
    /// <summary>
    /// Goo und Bye (ports of test_dlc_goobye_logik.gd, test_dlc_goobye.gd, test_w19_goobye_b.gd and test_w19_goobye_c.gd):
    /// the range pack, prices and margins, Godot's golden market day (seed 20260801 → 3 customers, 68 coins, the exact
    /// receipts), Alwin's carrot, cheaper-never-sells-less, the shelf, the daily deal, cooler room, the wholesale run with
    /// its phases, the store level, the healed save slice and buying the store.
    /// </summary>
    public class GoobyeTests
    {
        private const long GoldenSeed = 20260801;
        private const long T0 = 1_780_000_000_000;

        private static List<GoobyeRegal.Zeile> Golden() => new List<GoobyeRegal.Zeile>
        {
            new GoobyeRegal.Zeile { Id = "carrot", Bestand = 8 },
            new GoobyeRegal.Zeile { Id = "apple", Bestand = 6 },
            new GoobyeRegal.Zeile { Id = "cookie", Bestand = 5, Faktor = 0.9 },
            new GoobyeRegal.Zeile { Id = "cheese", Bestand = 4, Faktor = 1.2 },
            new GoobyeRegal.Zeile { Id = "bread", Bestand = 5 },
        };

        private static JObject State(int coins = 5000, int level = 12) => new JObject
        {
            ["economy"] = new JObject { ["coins"] = coins },
            ["progression"] = new JObject { ["level"] = level },
        };

        [SetUp]
        public void Setup() => Fixtures.InstallContentSource();

        [Test]
        public void GodotRngMatchesThePcg32Reference()
        {
            // the pcg32 reference sequence (pcg32-demo: seed 42, stream 54) through the same seeding path
            var reference = new GodotRng(42UL, 54UL);
            Assert.That(new[] { reference.RandI(), reference.RandI(), reference.RandI(), reference.RandI(), reference.RandI(), reference.RandI() },
                Is.EqualTo(new uint[] { 0xa15c02b7, 0x7b47f409, 0xba1d3330, 0x83d2f293, 0xbfa4784b, 0xcbed606e }));
            var rng = new GodotRng(0UL);
            var a = rng.RandI();
            var again = new GodotRng(0UL);
            Assert.That(again.RandI(), Is.EqualTo(a), "same seed, same stream");
            for (var i = 0; i < 1000; i++)
            {
                var f = rng.RandF();
                Assert.That(f, Is.GreaterThanOrEqualTo(0f).And.LessThanOrEqualTo(1f));
                var r = rng.RandIRange(3, 5);
                Assert.That(r, Is.InRange(3, 5));
            }
            Assert.That(GodotRng.StringHash(""), Is.EqualTo(5381u));
            Assert.That(GodotRng.StringHash("a"), Is.EqualTo(5381u * 33 + 'a'));
        }

        [Test]
        public void TheRangePackIsShapeAndColourCoded()
        {
            var gruppen = GoobyeKatalog.Gruppen();
            Assert.That(gruppen.Count, Is.EqualTo(6), "six ware groups");
            foreach (var g in gruppen)
            {
                Assert.That(J.Str(g["form"]), Is.Not.Empty, "shape");
                Assert.That(J.Str(g["farbe"]), Does.StartWith("#"), "colour");
                Assert.That(J.Num(g["ton"]), Is.InRange(0.9, 1.6), "babble pitch");
            }
            var waren = GoobyeKatalog.Waren();
            Assert.That(waren.Count, Is.InRange(20, 40));
            Assert.That(waren.All(w => GoobyeKatalog.Gruppe(J.Str(w["gruppe"])) != null), Is.True);
            Assert.That(J.Int(GoobyeKatalog.Ware("carrot")["vk"]), Is.EqualTo(5));
            Assert.That(GoobyeKatalog.Ware("gibtsnicht"), Is.Null);
            Assert.That(GoobyeKatalog.Gruppe("carrot"), Is.Null, "a ware is no group");
            Assert.That(GoobyeKatalog.WarenDerGruppe("obst").Count, Is.EqualTo(5));
            var start = GoobyeKatalog.Startlager();
            Assert.That(start["carrot"], Is.EqualTo(8));
            Assert.That(start["apple"], Is.EqualTo(6));
            Assert.That(start.ContainsKey("cake"), Is.False);
            Assert.That(GoobyeKatalog.TonFuer("cookie"), Is.EqualTo(1.35).Within(1e-9));
            Assert.That(GoobyeKatalog.TonFuer("carrot"), Is.EqualTo(1.1).Within(1e-9));
            Assert.That(GoobyeKatalog.TonFuer("gibtsnicht"), Is.EqualTo(1.0));
            Assert.That(GoobyeKatalog.Preis(), Is.EqualTo(2500));
            Assert.That(GoobyeKatalog.FreischaltLevel(), Is.EqualTo(12));
        }

        [Test]
        public void PricesAndMargins()
        {
            var apple = GoobyeKatalog.Ware("apple");
            Assert.That(GoobyePreis.EmpfohlenerPreis(apple), Is.EqualTo(6));
            Assert.That(GoobyePreis.Einkaufspreis(apple), Is.EqualTo(4), "60 % of 6 → 4");
            Assert.That(GoobyePreis.Marge(apple), Is.EqualTo(2));
            Assert.That(GoobyePreis.EmpfohlenerPreis(GoobyeKatalog.Ware("gb_hoppel_pops")), Is.EqualTo(6), "house brand −20 %");
            Assert.That(GoobyePreis.EmpfohlenerPreis(new JObject { ["vk"] = 10, ["bio"] = true }), Is.EqualTo(11), "organic +10 %");
            Assert.That(GoobyePreis.FaktorBegrenzen(0.1), Is.EqualTo(0.7).Within(1e-9));
            Assert.That(GoobyePreis.FaktorBegrenzen(9.9), Is.EqualTo(1.3).Within(1e-9));
            Assert.That(GoobyePreis.Verkaufspreis(apple, 1.3), Is.EqualTo(8));
            Assert.That(GoobyePreis.Verkaufspreis(apple, 0.7), Is.EqualTo(4));
            Assert.That(GoobyePreis.Marge(apple, 0.7), Is.EqualTo(0));
            Assert.That(GoobyePreis.Verkaufspreis(new JObject { ["vk"] = 0 }), Is.GreaterThanOrEqualTo(1));
            Assert.That(GoobyePreis.GriffChance(1.0), Is.EqualTo(1.0));
            Assert.That(GoobyePreis.GriffChance(0.7), Is.EqualTo(1.0));
            var vorher = 2.0;
            foreach (var f in new[] { 1.0, 1.1, 1.2, 1.3 })
            {
                Assert.That(GoobyePreis.GriffChance(f), Is.LessThanOrEqualTo(vorher));
                vorher = GoobyePreis.GriffChance(f);
            }
            Assert.That(GoobyePreis.GriffChance(1.3), Is.GreaterThanOrEqualTo(0.3));
            Assert.That(GoobyePreis.SpontanBonus(1.0), Is.EqualTo(0.0));
            Assert.That(GoobyePreis.SpontanBonus(0.7), Is.EqualTo(0.24).Within(1e-9));
        }

        [Test]
        public void GodotsGoldenMarketDay()
        {
            var plan = GoobyeMarkttag.TagPlanen(GoldenSeed, Golden());
            Assert.That(plan.Kundenzahl, Is.EqualTo(3), "3 customers on the golden day");
            Assert.That(plan.Umsatz, Is.EqualTo(68), "golden takings 68");
            Assert.That(plan.Verpasst, Is.EqualTo(0));
            Assert.That(plan.Verkauft, Is.EquivalentTo(new Dictionary<string, int> { ["apple"] = 2, ["bread"] = 2, ["carrot"] = 3, ["cheese"] = 1, ["cookie"] = 1 }));
            Assert.That(plan.Bons.Count, Is.EqualTo(3));
            var alwin = plan.Bons[0];
            Assert.That(alwin.Archetyp, Is.EqualTo("alwin"));
            Assert.That(alwin.Minute, Is.EqualTo(60), "Alwin comes at nine");
            Assert.That(alwin.Positionen.Select(p => (p.Ware, p.Preis)), Is.EqualTo(new[] { ("carrot", 5) }), "exactly one carrot");
            Assert.That(alwin.Summe, Is.EqualTo(5));
            var zweiter = plan.Bons[1];
            Assert.That(zweiter.Archetyp, Is.EqualTo("listen_gooby"));
            Assert.That(zweiter.Minute, Is.EqualTo(345));
            Assert.That(zweiter.Summe, Is.EqualTo(28));
            Assert.That(zweiter.Positionen.Select(p => (p.Ware, p.Preis)), Is.EqualTo(new[] { ("cookie", 7), ("bread", 10), ("carrot", 5), ("apple", 6) }), "slider prices (cookie ×0.9 → 7)");
            Assert.That(plan.Bons[2].Summe, Is.EqualTo(35), "with cheese ×1.2 → 14");
            Assert.That(GoobyeMarkttag.Melodie(alwin), Is.EqualTo(new[] { 1.1 }).Within(1e-9), "Alwin's carrot beeps in the vegetable tone");
        }

        [Test]
        public void MarketDayIsDeterministicAndAlwinKeepsHisDeal()
        {
            var a = GoobyeMarkttag.TagPlanen(GoldenSeed, Golden());
            var b = GoobyeMarkttag.TagPlanen(GoldenSeed, Golden());
            Assert.That(b.Umsatz, Is.EqualTo(a.Umsatz));
            Assert.That(b.Bons.Select(x => x.Archetyp + x.Summe), Is.EqualTo(a.Bons.Select(x => x.Archetyp + x.Summe)));
            Assert.That(GoobyeMarkttag.TagesSeed("2026-08-01"), Is.EqualTo(GoobyeMarkttag.TagesSeed("2026-08-01")));
            foreach (var seed in new long[] { 1, 7, 42, 999, 123456 })
            {
                var plan = GoobyeMarkttag.TagPlanen(seed, Golden());
                Assert.That(plan.Bons[0].Archetyp, Is.EqualTo("alwin"), "customer 0 is Alwin (seed " + seed + ")");
                Assert.That(plan.Bons[0].Positionen.Count, Is.LessThanOrEqualTo(1));
            }
            var kaputt = GoobyeMarkttag.TagPlanen(GoldenSeed, new List<GoobyeRegal.Zeile>
            {
                new GoobyeRegal.Zeile { Id = "gibtsnicht", Bestand = 5 }, null, new GoobyeRegal.Zeile { Id = "apple", Bestand = 2 },
            });
            Assert.That(kaputt.Bons.SelectMany(x => x.Positionen).All(p => p.Ware == "apple"), Is.True, "only catalog wares on a receipt");
        }

        [Test]
        public void CheaperNeverSellsLess()
        {
            foreach (var seed in new long[] { 3, 11, 77, 2024, 55555 })
            {
                var billig = Golden().Select(z => { z.Faktor = 0.7; return z; }).ToList();
                var teuer = Golden().Select(z => { z.Faktor = 1.3; return z; }).ToList();
                var nBillig = GoobyeMarkttag.TagPlanen(seed, billig).Verkauft.Values.Sum();
                var nTeuer = GoobyeMarkttag.TagPlanen(seed, teuer).Verkauft.Values.Sum();
                Assert.That(nBillig, Is.GreaterThanOrEqualTo(nTeuer), "seed " + seed);
            }
        }

        [Test]
        public void ShelfStockingAndTaking()
        {
            var regal = new GoobyeRegal();
            Assert.That(regal.Slots.Count, Is.EqualTo(5));
            var lager = new Dictionary<string, int> { ["apple"] = 10, ["carrot"] = 3 };
            Assert.That(regal.Einraeumen(0, "apple", 99, lager), Is.EqualTo(8), "slot cap 8");
            Assert.That(lager["apple"], Is.EqualTo(2));
            Assert.That(regal.Einraeumen(0, "carrot", 1, lager), Is.EqualTo(0), "another ware blocks");
            Assert.That(regal.Einraeumen(1, "carrot", 2, lager), Is.EqualTo(2));
            Assert.That(regal.Einraeumen(1, "carrot", 5, lager), Is.EqualTo(1), "the rest of the stores");
            Assert.That(lager.ContainsKey("carrot"), Is.False);
            Assert.That(regal.Einraeumen(9, "apple", 1, lager), Is.EqualTo(0));
            Assert.That(regal.GesamtBestand(), Is.EqualTo(11));
            Assert.That(regal.Entnehmen("carrot", 2), Is.EqualTo(2));
            Assert.That(regal.Entnehmen("carrot", 5), Is.EqualTo(1));
            Assert.That(regal.Slots[1].Ware, Is.EqualTo(""), "an emptied slot is free again");
            var sortiment = regal.SortimentVon(new Dictionary<string, double> { ["apple"] = 1.2 });
            Assert.That(sortiment.Count, Is.EqualTo(1));
            Assert.That(sortiment[0].Bestand, Is.EqualTo(8));
            Assert.That(sortiment[0].Faktor, Is.EqualTo(1.2));
            Assert.That(regal.Ausraeumen(0, lager), Is.EqualTo(8), "emptying puts everything back");
            Assert.That(lager["apple"], Is.EqualTo(10));
            var heil = GoobyeRegal.Normalisieren(JObject.Parse("{\"slots\":[{\"ware\":\"apple\",\"menge\":99},\"unfug\",{\"ware\":\"\",\"menge\":3}]}"));
            Assert.That(heil.Slots.Count, Is.EqualTo(5));
            Assert.That(heil.Bestand("apple"), Is.EqualTo(8));
            Assert.That(GoobyeRegal.Normalisieren(new JValue("kaputt")).GesamtBestand(), Is.EqualTo(0));
            var r2 = new GoobyeRegal();
            r2.Einraeumen(2, "apple", 4, new Dictionary<string, int> { ["apple"] = 4 });
            Assert.That(r2.NachfuellVorschlag(new Dictionary<string, int> { ["carrot"] = 2 }), Is.EqualTo(new[] { 0, 1, 3, 4 }));
            Assert.That(r2.NachfuellVorschlag(new Dictionary<string, int>()), Is.Empty);
        }

        [Test]
        public void TheDailyDeal()
        {
            var apple = GoobyeKatalog.Ware("apple");
            Assert.That(GoobyeAngebot.AngebotsPreis(apple), Is.EqualTo(5), "−15 % on 6");
            Assert.That(GoobyeAngebot.PreisFuerZeile(apple, 1.0, false), Is.EqualTo(6));
            Assert.That(GoobyeAngebot.PreisFuerZeile(apple, 1.0, true), Is.EqualTo(5));
            var s = State();
            Assert.That(GoobyeAngebot.AktiveGruppe(s, "2026-10-11"), Is.EqualTo(""));
            Assert.That(GoobyeAngebot.Waehle(s, "obst", "2026-10-11"), Is.EqualTo(GoobyeAngebot.Ok));
            Assert.That(GoobyeAngebot.AktiveGruppe(s, "2026-10-11"), Is.EqualTo("obst"));
            Assert.That(GoobyeAngebot.Waehle(s, "obst", "2026-10-11"), Is.EqualTo(GoobyeAngebot.Ok), "the same group again is fine");
            Assert.That(GoobyeAngebot.Waehle(s, "gemuese", "2026-10-11"), Is.EqualTo(GoobyeAngebot.SchonGewaehlt));
            Assert.That(GoobyeAngebot.Waehle(s, "gibtsnicht", "2026-10-11"), Is.EqualTo(GoobyeAngebot.Unbekannt));
            Assert.That(GoobyeAngebot.AktiveGruppe(s, "2026-10-12"), Is.EqualTo(""), "it runs out at midnight");
            Assert.That(GoobyeAngebot.Waehle(s, "gemuese", "2026-10-12"), Is.EqualTo(GoobyeAngebot.Ok));
            Assert.That(GoobyeAngebot.SchildVariante("2026-10-12", "gemuese"), Is.InRange(0, 5));
            var gemischt = new List<GoobyeRegal.Zeile> { new GoobyeRegal.Zeile { Id = "apple", Bestand = 4 }, new GoobyeRegal.Zeile { Id = "carrot", Bestand = 4 } };
            var markiert = GoobyeAngebot.SortimentMarkieren(gemischt, "obst");
            Assert.That(markiert[0].Angebot, Is.True);
            Assert.That(markiert[1].Angebot, Is.False);
            Assert.That(gemischt[0].Angebot, Is.False, "the source stays untouched");
            int summeMit = 0, summeOhne = 0;
            for (var seed = 1; seed <= 20; seed++)
            {
                var ohne = GoobyeMarkttag.TagPlanen(seed, Golden().Select(z => { z.Faktor = 1.3; return z; }).ToList());
                var mit = GoobyeMarkttag.TagPlanen(seed, GoobyeAngebot.SortimentMarkieren(Golden().Select(z => { z.Faktor = 1.3; return z; }), "obst"));
                Assert.That(mit.Kundenzahl, Is.EqualTo(ohne.Kundenzahl), "the customers stay");
                Assert.That(mit.Bons.Select(b => b.Archetyp), Is.EqualTo(ohne.Bons.Select(b => b.Archetyp)));
                mit.Verkauft.TryGetValue("apple", out var aMit);
                ohne.Verkauft.TryGetValue("apple", out var aOhne);
                Assert.That(aMit, Is.GreaterThanOrEqualTo(aOhne), "a deal never sells less (seed " + seed + ")");
                summeMit += aMit;
                summeOhne += aOhne;
            }
            Assert.That(summeMit, Is.GreaterThan(summeOhne), "+40 % grab is felt over 20 days");
        }

        [Test]
        public void CoolerRoomAndModules()
        {
            Assert.That(GoobyeKuehl.Kapazitaet(1), Is.EqualTo(8));
            Assert.That(GoobyeKuehl.Kapazitaet(3), Is.EqualTo(24));
            Assert.That(GoobyeKuehl.IstKuehlware("cheese"), Is.True);
            Assert.That(GoobyeKuehl.IstKuehlware("ice-cream"), Is.True);
            Assert.That(GoobyeKuehl.IstKuehlware("apple"), Is.False);
            var regal = new GoobyeRegal();
            var lager = new Dictionary<string, int> { ["cheese"] = 6 };
            regal.Einraeumen(0, "cheese", 6, lager);
            Assert.That(GoobyeKuehl.KuehlStueck(regal), Is.EqualTo(6));
            Assert.That(GoobyeKuehl.Einraeumbar(regal, "ice-cream", 8, 1), Is.EqualTo(2));
            Assert.That(GoobyeKuehl.Einraeumbar(regal, "ice-cream", 8, 2), Is.EqualTo(8));
            Assert.That(GoobyeKuehl.Einraeumbar(regal, "apple", 5, 1), Is.EqualTo(5), "dry goods unlimited");
            var s = State(coins: 160);
            Assert.That(GoobyeKuehl.ModuleVon(s), Is.EqualTo(1));
            Assert.That(GoobyeKuehl.KaufeModul(s), Is.EqualTo(GoobyeKuehl.Ok));
            Assert.That(J.Int(s["economy"]["coins"]), Is.EqualTo(10));
            Assert.That(GoobyeKuehl.ModuleVon(s), Is.EqualTo(2));
            Assert.That(GoobyeKuehl.KaufeModul(s), Is.EqualTo(GoobyeKuehl.Pleite));
            Assert.That(J.Int(s["economy"]["coins"]), Is.EqualTo(10), "broke books nothing");
            Assert.That(GoobyeKuehl.ModuleVon(s), Is.EqualTo(2));
        }

        [Test]
        public void TrunkSizes()
        {
            Assert.That(GoobyeTransport.KistenKapazitaet("sedan"), Is.EqualTo(12));
            Assert.That(GoobyeTransport.KistenKapazitaet("hatchback-sports"), Is.EqualTo(12));
            Assert.That(GoobyeTransport.KistenKapazitaet("suv"), Is.EqualTo(24));
            Assert.That(GoobyeTransport.KistenKapazitaet("van"), Is.EqualTo(24));
            Assert.That(GoobyeTransport.KistenKapazitaet("sedan", true), Is.EqualTo(48));
            var s = State();
            Assert.That(GoobyeTransport.KapazitaetFuer(s), Is.EqualTo(12), "the start car");
            s["city"] = new JObject { ["aktivesAuto"] = "van", ["autos"] = new JObject { ["van"] = "#FFFFFF" } };
            Assert.That(GoobyeTransport.KapazitaetFuer(s), Is.EqualTo(24));
            s["city"]["aktivesAuto"] = "suv";
            Assert.That(GoobyeTransport.KapazitaetFuer(s), Is.EqualTo(12), "a car you don't own falls back to the start car");
        }

        [Test]
        public void TheWholesaleRunPhasesAndUnloading()
        {
            var dauer = GoobyeTransport.FahrzeitMs(3);
            Assert.That(dauer, Is.EqualTo(40_000 + 3 * 2_000 + 40_000));
            var ankunft = GoobyeTransport.AnkunftMs(T0, 3);
            Assert.That(GoobyeTransport.StatusBei(T0, ankunft, T0 + 1_000).Phase, Is.EqualTo(GoobyeTransport.PhaseHinfahrt));
            Assert.That(GoobyeTransport.StatusBei(T0, ankunft, T0 + 1_000).RestMs, Is.EqualTo(ankunft - (T0 + 1_000)));
            Assert.That(GoobyeTransport.StatusBei(T0, ankunft, T0 + 42_000).Phase, Is.EqualTo(GoobyeTransport.PhaseBeladen));
            Assert.That(GoobyeTransport.StatusBei(T0, ankunft, ankunft - 1_000).Phase, Is.EqualTo(GoobyeTransport.PhaseRueckfahrt));
            var da = GoobyeTransport.StatusBei(T0, ankunft, ankunft);
            Assert.That(da.Phase, Is.EqualTo(GoobyeTransport.PhaseDa));
            Assert.That(da.RestMs, Is.EqualTo(0));
            Assert.That(da.Fortschritt, Is.EqualTo(1.0));

            var s = State(coins: 100);
            Assert.That(GoobyeTransport.Bestelle(s, new Dictionary<string, int>(), T0), Is.EqualTo(GoobyeTransport.Leer));
            Assert.That(GoobyeTransport.Bestelle(s, new Dictionary<string, int> { ["apple"] = 13 }, T0), Is.EqualTo(GoobyeTransport.ZuViel));
            Assert.That(J.Int(s["economy"]["coins"]), Is.EqualTo(100), "failed tries book nothing");
            var korb = new Dictionary<string, int> { ["apple"] = 2, ["cheese"] = 1, ["gibtsnicht"] = 4, ["bread"] = 0 };
            Assert.That(GoobyeTransport.Kosten(GoobyeTransport.KorbHeilen(korb)), Is.EqualTo(15), "2×4 + 7");
            Assert.That(GoobyeTransport.Bestelle(s, korb, T0), Is.EqualTo(GoobyeTransport.Ok));
            Assert.That(J.Int(s["economy"]["coins"]), Is.EqualTo(85));
            var fahrt = GoobyeTransport.UnterwegsVon(s);
            Assert.That(J.Long(fahrt["bestelltAt"]), Is.EqualTo(T0));
            Assert.That(J.Long(fahrt["ankunftAt"]), Is.EqualTo(GoobyeTransport.AnkunftMs(T0, 3)));
            Assert.That(((JObject)fahrt["warenkorb"]).Properties().Select(p => p.Name + p.Value), Is.EquivalentTo(new[] { "apple2", "cheese1" }));
            Assert.That(GoobyeTransport.Bestelle(s, new Dictionary<string, int> { ["apple"] = 1 }, T0), Is.EqualTo(GoobyeTransport.Unterwegs));
            Assert.That(J.Int(s["economy"]["coins"]), Is.EqualTo(85));
            Assert.That(GoobyeTransport.Ausladen(s, T0 + 1_000).grund, Is.EqualTo(GoobyeTransport.NichtDa), "not before arrival");
            var aus = GoobyeTransport.Ausladen(s, GoobyeTransport.AnkunftMs(T0, 3));
            Assert.That(aus.ok, Is.True);
            Assert.That(aus.kisten, Is.EqualTo(3));
            Assert.That(GoobyeState.LagerVon(s)["apple"], Is.EqualTo(2));
            Assert.That(GoobyeTransport.UnterwegsVon(s), Is.Null, "trip cleared");
            Assert.That(GoobyeTransport.Ausladen(s, T0 + 999_999).ok, Is.False, "no double unloading");
            var arm = State(coins: 3);
            Assert.That(GoobyeTransport.Bestelle(arm, new Dictionary<string, int> { ["apple"] = 1 }, T0), Is.EqualTo(GoobyeTransport.Pleite));
            Assert.That(GoobyeTransport.UnterwegsVon(arm), Is.Null);
        }

        [Test]
        public void ExpressDeliveryCostsHalfAgainTheWholesalePrice()
        {
            var s = State(coins: 20);
            Assert.That(GoobyeTransport.EilPreis(GoobyeKatalog.Ware("apple")), Is.EqualTo(6), "4 × 1.5");
            Assert.That(GoobyeTransport.EilKauf(s, "apple", 2), Is.EqualTo(GoobyeTransport.Ok));
            Assert.That(J.Int(s["economy"]["coins"]), Is.EqualTo(8));
            Assert.That(GoobyeState.LagerVon(s)["apple"], Is.EqualTo(2));
            Assert.That(GoobyeTransport.EilKauf(s, "apple", 2), Is.EqualTo(GoobyeTransport.Pleite));
            Assert.That(GoobyeState.LagerVon(s)["apple"], Is.EqualTo(2));
        }

        [Test]
        public void StoreLevelAndTheCompanyVan()
        {
            Assert.That(GoobyeLevel.LevelVon(0, 0), Is.EqualTo(1));
            Assert.That(GoobyeLevel.LevelVon(2, 59), Is.EqualTo(1), "both thresholds count");
            Assert.That(GoobyeLevel.LevelVon(2, 60), Is.EqualTo(2));
            Assert.That(GoobyeLevel.LevelVon(5, 200), Is.EqualTo(3));
            Assert.That(GoobyeLevel.LevelVon(9, 420), Is.EqualTo(4));
            Assert.That(GoobyeLevel.LevelVon(14, 800), Is.EqualTo(5));
            Assert.That(GoobyeLevel.LevelVon(99, 99_999), Is.EqualTo(5));
            Assert.That(GoobyeLevel.NameKey(1), Is.EqualTo("dlc_goobye.level.marktstand"));
            Assert.That(GoobyeLevel.NameKey(5), Is.EqualTo("dlc_goobye.level.xxl"));
            var s = State(coins: 2000);
            Assert.That(GoobyeTransport.KaufeLieferwagen(s), Is.EqualTo(GoobyeTransport.Gesperrt));
            var next = GoobyeLevel.Naechste(s).Value;
            Assert.That(next.stufe.Level, Is.EqualTo(2));
            Assert.That(next.fehltTage, Is.EqualTo(2));
            Assert.That(next.fehltUmsatz, Is.EqualTo(60));
            for (var i = 0; i < 14; i++) GoobyeState.UmsatzVerbuchen(s, 60);
            Assert.That(GoobyeLevel.LevelFuer(s), Is.EqualTo(5));
            Assert.That(GoobyeLevel.Naechste(s), Is.Null);
            Assert.That(GoobyeState.LevelAufstieg(s), Is.EqualTo(5), "the new level is celebrated once");
            Assert.That(GoobyeState.LevelAufstieg(s), Is.EqualTo(0));
            Assert.That(GoobyeTransport.KaufeLieferwagen(s), Is.EqualTo(GoobyeTransport.Ok));
            Assert.That(J.Int(s["economy"]["coins"]), Is.EqualTo(500));
            Assert.That(GoobyeTransport.KapazitaetFuer(s), Is.EqualTo(48));
            Assert.That(GoobyeTransport.KaufeLieferwagen(s), Is.EqualTo(GoobyeTransport.SchonDa));
            Assert.That(GoobyeLevel.KundenFuer(1), Is.EqualTo((2, 3)));
            Assert.That(GoobyeLevel.KundenFuer(5).max, Is.GreaterThan(GoobyeLevel.KundenFuer(1).max), "a bigger store, more customers");
        }

        [Test]
        public void TheSaveSliceHealsAndKeepsItsSiblings()
        {
            var s = JObject.Parse(@"{""dlc"":{""mcgooby"":{""x"":1},""goobye"":{""gekauft"":1,""lager"":{""apple"":3,""carrot"":-2,""x"":""y""},
                ""umsatz"":{""tage"":-1,""gesamt"":""viel""},""transport"":{""lieferwagen"":1,""unterwegs"":{""bestelltAt"":-5,""ankunftAt"":-9,""warenkorb"":{""apple"":2,""kaputt"":0}}},
                ""tagesangebot"":""unfug"",""kuehlModule"":0,""regal"":{""slots"":[{""ware"":""apple"",""menge"":50}]},""runde"":7}}}");
            var g = GoobyeState.Slice(s);
            Assert.That(J.IsTrue(g["gekauft"]), Is.True);
            Assert.That(((JObject)g["lager"]).Properties().Select(p => p.Name), Is.EqualTo(new[] { "apple" }));
            Assert.That(J.Int(g["umsatz"]["tage"]), Is.EqualTo(0));
            Assert.That(J.IsTrue(g["transport"]["lieferwagen"]), Is.True);
            Assert.That(J.Long(g["transport"]["unterwegs"]["bestelltAt"]), Is.EqualTo(0));
            Assert.That(J.Long(g["transport"]["unterwegs"]["ankunftAt"]), Is.EqualTo(0));
            Assert.That(((JObject)g["transport"]["unterwegs"]["warenkorb"]).Properties().Select(p => p.Name), Is.EqualTo(new[] { "apple" }));
            Assert.That(J.Str(g["tagesangebot"]["gruppe"]), Is.EqualTo(""));
            Assert.That(J.Int(g["kuehlModule"]), Is.EqualTo(1));
            Assert.That(GoobyeState.RegalVon(s).Bestand("apple"), Is.EqualTo(8));
            Assert.That(J.Int(s["dlc"]["mcgooby"]["x"]), Is.EqualTo(1), "sibling DLC keys stay verbatim");
            var leer = JObject.Parse(@"{""dlc"":{""goobye"":{""transport"":{""unterwegs"":{""bestelltAt"":5,""warenkorb"":{}}}}}}");
            Assert.That(((JObject)GoobyeState.Slice(leer)["transport"]["unterwegs"]).HasValues, Is.False, "an empty basket clears the trip");
            Assert.That(GoobyeState.ErstbesuchMerken(s), Is.True);
            Assert.That(GoobyeState.ErstbesuchMerken(s), Is.False);
        }

        [Test]
        public void MarketRoundsOfADayEachGetTheirOwnSeed()
        {
            var s = State();
            var tag = "2026-10-11";
            var erste = GoobyeState.RundenSeed(s, tag);
            Assert.That(erste, Is.EqualTo(GoobyeMarkttag.TagesSeed(tag)), "the first round is Godot's day seed");
            GoobyeState.RundeVerbuchen(s, tag);
            var zweite = GoobyeState.RundenSeed(s, tag);
            Assert.That(zweite, Is.Not.EqualTo(erste));
            GoobyeState.RundeVerbuchen(s, tag);
            Assert.That(GoobyeState.RundenSeed(s, tag), Is.Not.EqualTo(zweite));
            Assert.That(GoobyeState.RundenSeed(s, "2026-10-12"), Is.EqualTo(GoobyeMarkttag.TagesSeed("2026-10-12")), "a new day starts over");
        }

        [Test]
        public void BuyingTheStore()
        {
            Assert.That(GoobyeKauf.Check(State(level: 11)), Is.EqualTo(GoobyeKauf.Gesperrt));
            Assert.That(GoobyeKauf.Check(State(coins: 2499)), Is.EqualTo(GoobyeKauf.Pleite));
            var s = State(coins: 2600);
            Assert.That(GoobyeKauf.Kaufe(s, T0), Is.EqualTo(GoobyeKauf.Ok));
            Assert.That(J.Int(s["economy"]["coins"]), Is.EqualTo(100));
            Assert.That(GoobyeState.IstGekauft(s), Is.True);
            Assert.That(J.Long(s["dlc"]["goobye"]["gekauftAm"]), Is.EqualTo(T0));
            var lager = GoobyeState.LagerVon(s);
            Assert.That(lager["carrot"], Is.EqualTo(8), "the opening package");
            Assert.That(lager["apple"], Is.EqualTo(6));
            Assert.That(GoobyeKauf.Kaufe(s, T0), Is.EqualTo(GoobyeKauf.Schon));
            Assert.That(J.Int(s["economy"]["coins"]), Is.EqualTo(100));
            var spaeter = State();
            GoobyeState.AngebotVerschieben(spaeter);
            Assert.That(J.IsTrue(spaeter["dlc"]["goobye"]["angebotVerschoben"]), Is.True);
        }
    }
}
