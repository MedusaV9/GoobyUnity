using System.Linq;
using Gooby.Core.Json;
using Gooby.Core.Minigames.Ranch;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Gooby.Tests.Core
{
    /// <summary>
    /// The ranch farm (ports of test_ranchplay_care.gd, test_ranchplay_offline.gd, test_ranch_kauf.gd and the economy
    /// rules of ranch_wirtschaft.gd): care maths, the offline day, buying the ranch with its start animals, the feed
    /// store, extensions and gear, and one care action booked into the save.
    /// </summary>
    public class RanchHofTests
    {
        /// <summary>12:00 UTC on day 3, far from the phase switches.</summary>
        private const long Mittag = (3L * 24 + 12) * 3_600_000L;

        private static JObject Werte(double hunger, double durst, double sauberkeit) =>
            new JObject { ["hunger"] = hunger, ["durst"] = durst, ["sauberkeit"] = sauberkeit };

        private static double W(JObject werte, string k) => (double)werte[k];

        [Test]
        public void CareClampsAndDecays()
        {
            Assert.That(RanchCare.ClampWert(150.0), Is.EqualTo(100.0));
            Assert.That(RanchCare.ClampWert(-3.0), Is.EqualTo(0.0));
            Assert.That(RanchCare.ClampWert(double.NaN), Is.EqualTo(0.0));
            Assert.That(RanchCare.ClampWert(new JValue("kaputt")), Is.EqualTo(0.0));
            var halb = RanchCare.ClampWerte(new JObject { ["hunger"] = 120, ["durst"] = -4 });
            Assert.That(W(halb, "hunger"), Is.EqualTo(100.0));
            Assert.That(W(halb, "sauberkeit"), Is.EqualTo(0.0), "missing values fall to 0");

            var wach = RanchCare.Tick(Werte(80, 85, 75), 10);
            Assert.That(W(wach, "hunger"), Is.EqualTo(77.5).Within(1e-9));
            Assert.That(W(wach, "durst"), Is.EqualTo(81.0).Within(1e-9));
            Assert.That(W(wach, "sauberkeit"), Is.EqualTo(73.5).Within(1e-9));
            var schlaf = RanchCare.Tick(Werte(80, 85, 75), 10, schlaeft: true);
            Assert.That(W(schlaf, "hunger"), Is.EqualTo(79.2).Within(1e-9));
            Assert.That(W(schlaf, "durst"), Is.EqualTo(83.5).Within(1e-9));
            Assert.That(W(schlaf, "sauberkeit"), Is.EqualTo(73.0).Within(1e-9));
            Assert.That(W(RanchCare.Tick(Werte(50, 50, 50), 10, schlaeft: true, stallSauberkeit: 30), "sauberkeit"), Is.EqualTo(46.0).Within(1e-9), "a dirty stable doubles");
            Assert.That(W(RanchCare.Tick(Werte(50, 50, 50), 10, sauberkeitMult: 0.8), "sauberkeit"), Is.EqualTo(48.8).Within(1e-9), "the fence only helps awake");
            Assert.That(W(RanchCare.Tick(Werte(50, 50, 50), 10, schlaeft: true, sauberkeitMult: 0.8), "sauberkeit"), Is.EqualTo(48.0).Within(1e-9));
            Assert.That(W(RanchCare.Tick(Werte(50, 50, 50), 10, rateMult: 0.3), "hunger"), Is.EqualTo(49.25).Within(1e-9));
        }

        [Test]
        public void MoodBandsBondAndPerks()
        {
            var mitte = Werte(50, 50, 50);
            Assert.That(RanchCare.Laune(mitte, 50), Is.EqualTo(50.0).Within(1e-9));
            Assert.That(RanchCare.Laune(mitte, 100), Is.EqualTo(55.0).Within(1e-9));
            Assert.That(RanchCare.Laune(mitte, 0), Is.EqualTo(45.0).Within(1e-9));
            Assert.That(RanchCare.Laune(Werte(90, 15, 90), 50), Is.EqualTo(39.0).Within(1e-9), "very thirsty caps at 39");
            Assert.That(RanchCare.Laune(Werte(90, 16, 90), 50), Is.GreaterThan(39.0));
            Assert.That(RanchCare.LauneBand(80), Is.EqualTo("strahlend"));
            Assert.That(RanchCare.LauneBand(79.9), Is.EqualTo("froh"));
            Assert.That(RanchCare.LauneBand(40), Is.EqualTo("zufrieden"));
            Assert.That(RanchCare.LauneBand(25), Is.EqualTo("muffig"));
            Assert.That(RanchCare.LauneBand(10), Is.EqualTo("elend"));
            Assert.That(RanchCare.BindungStufe(90), Is.EqualTo("seelenpferd"));
            Assert.That(RanchCare.BindungStufe(69.9), Is.EqualTo("freund"));
            Assert.That(RanchCare.BindungStufe(0), Is.EqualTo("fremd"));

            var w = Werte(50, 50, 50);
            Assert.That(W(RanchCare.Fuettern(w, "heu"), "hunger"), Is.EqualTo(80.0));
            Assert.That(W(RanchCare.Fuettern(w, "apfel"), "durst"), Is.EqualTo(54.0));
            Assert.That(W(RanchCare.Fuettern(w, "karotte"), "hunger"), Is.EqualTo(58.0));
            Assert.That(JToken.DeepEquals(RanchCare.Fuettern(w, "pizza"), RanchCare.ClampWerte(w)), Is.True);
            Assert.That(W(RanchCare.Traenken(w), "durst"), Is.EqualTo(95.0));
            Assert.That(W(RanchCare.Striegeln(w), "sauberkeit"), Is.EqualTo(85.0));

            double bindung = 0, heute = 0, gesamt = 0;
            for (var i = 0; i < 5; i++)
            {
                var b = RanchCare.BondNachAktion(bindung, heute, "striegeln");
                bindung = b.Bindung;
                heute = b.BondHeute;
                gesamt += b.Gewinn;
            }
            Assert.That(gesamt, Is.EqualTo(RanchCare.BondTagesDeckel).Within(1e-9), "5 × 3 capped at 12");
            Assert.That(RanchCare.BondNachAktion(bindung, heute, "heu").Gewinn, Is.EqualTo(0.0));
            Assert.That(RanchCare.BondNachAktion(bindung, heute, "striegeln", tagGewechselt: true).Gewinn, Is.EqualTo(3.0));
            Assert.That(RanchCare.BondVerfall(50, 0, 2880), Is.EqualTo(50.0).Within(1e-9), "48 h grace");
            Assert.That(RanchCare.BondVerfall(50, 0, 4320), Is.EqualTo(49.0).Within(1e-9));
            Assert.That(RanchCare.BondVerfall(50, 2880, 1440), Is.EqualTo(49.0).Within(1e-9));
            Assert.That(RanchCare.StallTick(100, 60, 1), Is.EqualTo(97.0).Within(1e-9));
            Assert.That(RanchCare.StallTick(100, 60, 0), Is.EqualTo(100.0));
            Assert.That(RanchCare.ReitPerks(45).tempoMult, Is.EqualTo(1.0).Within(1e-9));
            Assert.That(RanchCare.ReitPerks(100).tempoMult, Is.EqualTo(1.06).Within(1e-9));
            Assert.That(RanchCare.ReitPerks(100).ausdauerRegenMult, Is.EqualTo(1.25).Within(1e-9));
        }

        private static JObject Tiere(JObject werte, double bindung = 50, long pflegeAt = 0, long last = 0)
        {
            var p = RanchHof.NeuesPferd("Testpferd", "braun");
            p["werte"] = werte;
            p["bindung"] = bindung;
            p["letztePflegeAt"] = pflegeAt;
            return new JObject { ["pferde"] = new JObject { ["p1"] = p }, ["stall"] = new JObject { ["sauberkeit"] = 100.0 }, ["lastTickAt"] = last };
        }

        private static double Wert(RanchOffline.Ergebnis r, string k) => (double)r.Tiere["pferde"]["p1"]["werte"][k];

        [Test]
        public void OfflineDayAndNight()
        {
            Assert.That(RanchOffline.IstNacht(21), Is.True);
            Assert.That(RanchOffline.IstNacht(6.99), Is.True);
            Assert.That(RanchOffline.IstNacht(7), Is.False);
            Assert.That(RanchOffline.IstNacht(27), Is.True, "hours past 24 fold");
            Assert.That(RanchOffline.Wohin(10), Is.EqualTo("weide"));
            Assert.That(RanchOffline.StundeLokal(Mittag), Is.EqualTo(12.0).Within(1e-9));
            Assert.That(RanchOffline.StundeLokal(Mittag, 120), Is.EqualTo(14.0).Within(1e-9));
            Assert.That(RanchOffline.MsBisPhasenwechsel(Mittag), Is.EqualTo(9 * 3_600_000L));
            Assert.That(RanchOffline.MsBisPhasenwechsel(Mittag + 10 * 3_600_000L), Is.EqualTo(9 * 3_600_000L), "22:00 → 07:00");

            var erst = RanchOffline.Simulate(Tiere(Werte(80, 80, 80)), Mittag);
            Assert.That((long)erst.Tiere["lastTickAt"], Is.EqualTo(Mittag), "the first run only stamps");
            Assert.That(Wert(erst, "hunger"), Is.EqualTo(80.0));

            var stunde = RanchOffline.Simulate(Tiere(Werte(80, 80, 80), last: Mittag), Mittag + 60 * 60_000L);
            Assert.That(Wert(stunde, "hunger"), Is.EqualTo(80.0 - 0.25 * 60 * 0.3).Within(1e-4));
            Assert.That(Wert(stunde, "durst"), Is.EqualTo(80.0 - 0.4 * 60 * 0.3).Within(1e-4));

            var morgen = 3 * 86_400_000L + 7 * 3_600_000L;
            var cap = RanchOffline.Simulate(Tiere(Werte(90, 90, 90), last: morgen), morgen + 840 * 60_000L);
            Assert.That(Wert(cap, "hunger"), Is.EqualTo(90.0 - 0.25 * 480 * 0.3).Within(1e-4), "only 480 pasture minutes count");

            var nacht = 3 * 86_400_000L + 22 * 3_600_000L;
            var schlaf = RanchOffline.Simulate(Tiere(Werte(90, 90, 90), last: nacht), nacht + 4 * 3_600_000L);
            Assert.That(Wert(schlaf, "hunger"), Is.EqualTo(90.0 - 0.08 * 240).Within(1e-4));
            Assert.That((double)schlaf.Tiere["stall"]["sauberkeit"], Is.EqualTo(100.0 - 0.05 * 240).Within(1e-4));

            var frisch = Tiere(Werte(90, 90, 90), 60, Mittag, Mittag);
            Assert.That((double)RanchOffline.Simulate(frisch, Mittag + 24 * 3_600_000L).Tiere["pferde"]["p1"]["bindung"], Is.EqualTo(60.0).Within(1e-6));
            Assert.That((double)RanchOffline.Simulate(frisch, Mittag + 72 * 3_600_000L).Tiere["pferde"]["p1"]["bindung"], Is.EqualTo(59.0).Within(1e-4));

            var warn = RanchOffline.Simulate(Tiere(Werte(26, 90, 20), last: Mittag), Mittag + 3 * 3_600_000L);
            Assert.That(warn.Events, Does.Contain("hungrig:p1"));
            Assert.That(warn.Events, Does.Not.Contain("stinkig:p1"), "already low stays quiet");

            var dreck = Tiere(Werte(90, 90, 90), last: 3 * 86_400_000L + 21 * 3_600_000L);
            dreck["stall"]["sauberkeit"] = 41.0;
            Assert.That(RanchOffline.Simulate(dreck, 3 * 86_400_000L + 22 * 3_600_000L).Events, Does.Contain("stallDreckig"));

            var voll = Tiere(Werte(100, 100, 100), last: Mittag);
            Assert.That(Wert(RanchOffline.Simulate(voll, Mittag + 30 * 86_400_000L), "hunger"),
                Is.EqualTo(Wert(RanchOffline.Simulate(voll, Mittag + 7 * 86_400_000L), "hunger")).Within(1e-6), "older than 7 days does not count");
            var rueck = RanchOffline.Simulate(Tiere(Werte(80, 80, 80), last: Mittag), Mittag - 3_600_000L);
            Assert.That(Wert(rueck, "hunger"), Is.EqualTo(80.0));
            Assert.That((long)rueck.Tiere["lastTickAt"], Is.EqualTo(Mittag - 3_600_000L));
        }

        private static JObject Spieler(int level, int coins) => new JObject
        {
            ["progression"] = new JObject { ["level"] = level },
            ["economy"] = new JObject { ["coins"] = coins },
            ["ranch"] = new JObject { ["comp"] = new JObject { ["keep"] = true } },
        };

        [Test]
        public void BuyingTheRanchBringsTheAnimals()
        {
            Assert.That(RanchKauf.Check(Spieler(14, 9999)), Is.EqualTo(RanchKauf.Gesperrt));
            Assert.That(RanchKauf.Check(Spieler(15, 2499)), Is.EqualTo(RanchKauf.Pleite));
            var s = Spieler(15, 3000);
            Assert.That(RanchKauf.Kaufe(s, Mittag), Is.EqualTo(RanchKauf.Ok));
            Assert.That(J.Int(s["economy"]["coins"]), Is.EqualTo(500));
            Assert.That(RanchHof.IstGekauft(s), Is.True);
            Assert.That(RanchHof.PferdIds(s), Is.EqualTo(new[] { "tier_pferd_beere", "tier_pferd_karamell", "tier_pferd_wolke" }));
            var karamell = RanchHof.Pferd(s, "tier_pferd_karamell");
            Assert.That(J.Str(karamell["name"]), Is.EqualTo("Karamell"));
            Assert.That(J.Str(karamell["farbe"]), Is.EqualTo("palomino"));
            Assert.That(J.Str(karamell["farbeHex"]), Is.EqualTo("#D9A066"));
            Assert.That((double)karamell["werte"]["durst"], Is.EqualTo(85.0));
            Assert.That(((JArray)s["ranch"]["hoftiere"]).Count, Is.EqualTo(6), "two cows, three sheep, one hen");
            Assert.That(J.Int(RanchHof.Lager(s)["heu"]), Is.EqualTo(4));
            Assert.That(J.IsTrue(s["ranch"]["comp"]["keep"]), Is.True, "the competitions' keys stay");
            Assert.That(RanchKauf.Kaufe(s, Mittag), Is.EqualTo(RanchKauf.Schon));
            Assert.That(J.Int(s["economy"]["coins"]), Is.EqualTo(500));

            var heil = new JObject { ["ranch"] = new JObject { ["tiere"] = new JObject { ["pferde"] = new JObject { ["x"] = new JObject { ["werte"] = new JObject { ["hunger"] = "voll" }, ["xp"] = 100, ["eigen"] = 7 } } } } };
            RanchHof.Slice(heil);
            var x = RanchHof.Pferd(heil, "x");
            Assert.That((double)x["werte"]["hunger"], Is.EqualTo(0.0));
            Assert.That(J.Int(x["level"]), Is.EqualTo(RanchPferd.LevelFuerXp(100)));
            Assert.That(J.Int(x["eigen"]), Is.EqualTo(7), "foreign keys survive");
            Assert.That(J.Int(x["stats"]["tempo"]), Is.EqualTo(10));
        }

        [Test]
        public void FeedStoreExtensionsAndGear()
        {
            var s = Spieler(20, 1000);
            RanchHof.Slice(s);
            Assert.That(RanchWirtschaft.HeuKaufen(s, 5), Is.EqualTo(RanchWirtschaft.Ok));
            Assert.That(J.Int(RanchHof.Lager(s)["heu"]), Is.EqualTo(9));
            Assert.That(J.Int(s["economy"]["coins"]), Is.EqualTo(960));

            Assert.That(RanchWirtschaft.HeuErnten(s, Mittag), Is.EqualTo((RanchWirtschaft.Ok, 3)));
            Assert.That(RanchWirtschaft.HeuErnten(s, Mittag + 1000).code, Is.EqualTo(RanchWirtschaft.NichtReif));
            Assert.That(RanchWirtschaft.HeuBereitIn(s, Mittag), Is.EqualTo(360 * 60_000L));
            Assert.That(RanchWirtschaft.HeuErnten(s, Mittag + 360 * 60_000L).code, Is.EqualTo(RanchWirtschaft.Ok));
            Assert.That(RanchWirtschaft.ApfelPfluecken(s, 1, Mittag), Is.EqualTo((RanchWirtschaft.Ok, 2)));
            Assert.That(RanchWirtschaft.ApfelPfluecken(s, 1, Mittag).code, Is.EqualTo(RanchWirtschaft.NichtReif));
            Assert.That(RanchWirtschaft.ApfelPfluecken(s, 3, Mittag).code, Is.EqualTo(RanchWirtschaft.Unbekannt));
            Assert.That(J.Int(RanchHof.Lager(s)["apfel"]), Is.EqualTo(4));

            Assert.That(RanchWirtschaft.AusbauKaufen(s, "boxen3"), Is.EqualTo(RanchWirtschaft.Vorstufe));
            Assert.That(RanchWirtschaft.AusbauKaufen(s, "boxen2"), Is.EqualTo(RanchWirtschaft.Ok));
            Assert.That(RanchWirtschaft.BoxenKapazitaet(s), Is.EqualTo(4));
            Assert.That(RanchWirtschaft.AusbauKaufen(s, "boxen2"), Is.EqualTo(RanchWirtschaft.Schon));
            Assert.That(RanchWirtschaft.AusbauKaufen(s, "boxen3"), Is.EqualTo(RanchWirtschaft.ZuTeuer), "560 left, 900 needed");
            Assert.That(RanchWirtschaft.AusbauKaufen(s, "weidezaun"), Is.EqualTo(RanchWirtschaft.Ok));
            Assert.That(RanchWirtschaft.WeideSauberkeitMult(s), Is.EqualTo(0.8));
            Assert.That(J.Int(s["economy"]["coins"]), Is.EqualTo(310));

            Assert.That(RanchWirtschaft.GearPreis("sattel", "gold"), Is.EqualTo(240));
            Assert.That(RanchWirtschaft.GearKaufen(s, "sattel_pink"), Is.EqualTo(RanchWirtschaft.Unbekannt));
            Assert.That(RanchWirtschaft.GearKaufen(s, "sattel_rot"), Is.EqualTo(RanchWirtschaft.Ok));
            Assert.That(RanchWirtschaft.GearKaufen(s, "sattel_rot"), Is.EqualTo(RanchWirtschaft.Schon));
            Assert.That(RanchWirtschaft.GearAnlegen(s, "p", "decke", "blau"), Is.EqualTo(RanchWirtschaft.NichtGekauft));
            Assert.That(RanchWirtschaft.GearAnlegen(s, "p", "sattel", "rot"), Is.EqualTo(RanchWirtschaft.Ok));
            Assert.That(RanchWirtschaft.GearAn(s, "p", "sattel"), Is.EqualTo("rot"));
            Assert.That(RanchWirtschaft.GearAnlegen(s, "p", "sattel", null), Is.EqualTo(RanchWirtschaft.Ok));
            Assert.That(RanchWirtschaft.GearAn(s, "p", "sattel"), Is.Null);
        }

        [Test]
        public void CareIsBookedIntoTheSave()
        {
            var s = Spieler(15, 2500);
            RanchKauf.Kaufe(s, Mittag);
            s["inventory"] = new JObject { ["food"] = new JObject { ["carrot"] = 1 } };
            const string id = "tier_pferd_wolke";
            var heu = RanchPflege.Pflegen(s, id, RanchPflege.Heu, Mittag, "2026-10-13");
            Assert.That(heu.Ok, Is.True);
            Assert.That(heu.Gewinn, Is.EqualTo(1.0));
            Assert.That((double)RanchHof.Pferd(s, id)["werte"]["hunger"], Is.EqualTo(100.0), "80 + 30, clamped");
            Assert.That(J.Int(RanchHof.Lager(s)["heu"]), Is.EqualTo(3));
            Assert.That(RanchPflege.Pflegen(s, id, RanchPflege.Karotte, Mittag, "2026-10-13").Ok, Is.True);
            Assert.That(RanchPflege.Pflegen(s, id, RanchPflege.Karotte, Mittag, "2026-10-13").Code, Is.EqualTo(RanchWirtschaft.LagerLeer));
            Assert.That(J.Int(s["inventory"]["food"]["carrot"]), Is.EqualTo(0));

            s["ranch"]["tiere"]["stall"]["sauberkeit"] = 20.0;
            var mist = RanchPflege.Pflegen(s, id, RanchPflege.Ausmisten, Mittag + 5, "2026-10-13");
            Assert.That(mist.Ok, Is.True);
            Assert.That(RanchHof.Stall(s), Is.EqualTo(100.0));
            Assert.That((double)RanchHof.Pferd(s, id)["bindung"], Is.EqualTo(5.0), "heu 1 + karotte 2 + ausmisten 2");
            Assert.That(J.Long(RanchHof.Pferd(s, id)["letztePflegeAt"]), Is.EqualTo(Mittag + 5));
            Assert.That(J.Str(RanchHof.Pferd(s, id)["bondTag"]), Is.EqualTo("2026-10-13"));

            var events = RanchPflege.Aufholen(s, Mittag + 3_600_000L, 0);
            Assert.That(events, Is.Empty);
            Assert.That((double)RanchHof.Pferd(s, id)["werte"]["durst"], Is.LessThan(100.0));
            Assert.That(RanchPflege.Pflegen(s, "nix", RanchPflege.Heu, Mittag, "x").Ok, Is.False);
        }
    }
}
