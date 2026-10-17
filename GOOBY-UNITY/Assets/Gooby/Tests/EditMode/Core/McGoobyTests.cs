using System.Linq;
using Gooby.Core.Dlc;
using Gooby.Core.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Gooby.Tests.Core
{
    /// <summary>
    /// McGooby (ports of test_dlc_mcgooby.gd and test_w19_mcgooby_c.gd): the menu pack, the golden order streams and bot
    /// shifts (grill menu seeds 4711/1234, full menu 4711/134), the timing windows of grill, fryer and shake, the
    /// assembly ticket, the till with its combo tips, the save slice, the star ladder and buying the restaurant.
    /// </summary>
    public class McGoobyTests
    {
        private const long Gold = 4711;

        [SetUp]
        public void Setup() => Fixtures.InstallContentSource();

        [Test]
        public void TheMenuPackIsComplete()
        {
            var rezepte = McGoobyKatalog.Rezepte();
            Assert.That(rezepte.Count, Is.EqualTo(10), "full start menu");
            Assert.That(McGoobyKatalog.RezepteFuer("grill").Count, Is.EqualTo(4));
            foreach (var r in rezepte)
            {
                Assert.That(J.Str(r["name_de"]), Is.Not.Empty);
                Assert.That(McGoobySchicht.PhasenVon(r).All(s => McGoobyKatalog.StationIds.Contains(s)), Is.True, J.Str(r["id"]));
            }
            Assert.That(McGoobySchicht.GrillSchritte(McGoobyKatalog.Rezept("gooby_mac")), Is.EqualTo(2), "a double-decker");
            Assert.That(McGoobySchicht.GrillSchritte(McGoobyKatalog.Rezept("pommes_klassik")), Is.EqualTo(0));
            Assert.That(McGoobyKatalog.Preis(), Is.EqualTo(3000));
            Assert.That(McGoobyKatalog.FreischaltLevel(), Is.EqualTo(14));
            Assert.That(McGoobyKatalog.Raenge().Count, Is.EqualTo(5));
            var moehren = McGoobyKatalog.Rezept("moehren_pommes");
            Assert.That(McGoobyKatalog.TimingFuer("fritteuse", moehren).FensterSec, Is.LessThan(McGoobyKatalog.TimingFuer("fritteuse").FensterSec), "fenster_mult narrows");
        }

        [Test]
        public void GoldenOrderStreams()
        {
            var bal = McGoobyKatalog.Balance();
            var grill = McGoobyKatalog.RezepteFuer("grill");
            var f = McGoobySchicht.BestellFolge(Gold, grill, bal);
            Assert.That(f.Select(b => b.RezeptId + "/" + b.Patties), Is.EqualTo(new[] { "garten_gooby/1", "gooby_mac/2" }));
            Assert.That(f.Select(b => b.Nr), Is.EqualTo(new[] { 1, 2 }));
            Assert.That(McGoobySchicht.BestellFolge(1234, grill, bal).Select(b => b.RezeptId),
                Is.EqualTo(new[] { "kaese_knusperle", "gooby_mac", "garten_gooby", "gurken_deluxe" }));
            var voll = McGoobySchicht.BestellFolge(Gold, McGoobyKatalog.Rezepte(), bal);
            Assert.That(voll[0].Patties, Is.GreaterThan(0), "the first customer wants a burger");
            Assert.That(voll[1].Patties, Is.EqualTo(0), "the second orders at the new counter");
        }

        [Test]
        public void GoldenBotShifts()
        {
            var bal = McGoobyKatalog.Balance();
            var grill = McGoobyKatalog.RezepteFuer("grill");
            var a = McGoobySchicht.SimulateAutoplay(Gold, grill, bal);
            Assert.That((a.Bestellungen, a.Perfekt, a.Roestaroma, a.Fehlgriffe, a.Punkte, a.Trinkgeld, a.Muenzen), Is.EqualTo((2, 3, 0, 1, 118, 2, 31)));
            var b = McGoobySchicht.SimulateAutoplay(1234, grill, bal);
            Assert.That((b.Punkte, b.Roestaroma, b.Fehlgriffe, b.Muenzen), Is.EqualTo((239, 0, 5, 32)));
            var menu = McGoobyKatalog.Rezepte();
            var c = McGoobySchicht.SimulateAutoplay(Gold, menu, bal);
            Assert.That((c.Bestellungen, c.Perfekt, c.Fehlgriffe, c.Dunkel, c.Schaum, c.Punkte, c.Trinkgeld, c.Muenzen), Is.EqualTo((2, 3, 1, 0, 0, 83, 2, 22)));
            var d = McGoobySchicht.SimulateAutoplay(134, menu, bal);
            Assert.That((d.Bestellungen, d.Roestaroma, d.Fehlgriffe, d.Dunkel, d.Schaum, d.Perfekt, d.Punkte, d.Muenzen), Is.EqualTo((4, 1, 1, 1, 1, 6, 168, 32)));
        }

        [Test]
        public void TimingWindows()
        {
            var bal = McGoobyKatalog.Balance();
            var grill = McGoobyKatalog.TimingFuer("grill");
            Assert.That(McGoobySchicht.Bewerte("grill", 3.9, grill, bal).Punkte, Is.EqualTo(0), "too early costs nothing");
            Assert.That(McGoobySchicht.Bewerte("grill", 4.0, grill, bal).Art, Is.EqualTo(McGoobySchicht.Perfekt), "window start inclusive");
            Assert.That(McGoobySchicht.Bewerte("grill", 5.39, grill, bal).Art, Is.EqualTo(McGoobySchicht.Perfekt));
            var spaet = McGoobySchicht.Bewerte("grill", 5.4, grill, bal);
            Assert.That((spaet.Art, spaet.Punkte), Is.EqualTo((McGoobySchicht.Roestaroma, 5)), "half points — never lost");
            Assert.That(McGoobySchicht.Zustand("grill", 4.5, grill), Is.EqualTo(McGoobySchicht.Goldbraun));
            var fry = McGoobyKatalog.TimingFuer("fritteuse");
            Assert.That(McGoobySchicht.Zustand("fritteuse", 4.99, fry), Is.EqualTo(McGoobySchicht.Blass));
            Assert.That(McGoobySchicht.Zustand("fritteuse", 6.19, fry), Is.EqualTo(McGoobySchicht.Goldgelb));
            Assert.That(McGoobySchicht.Zustand("fritteuse", 6.2, fry), Is.EqualTo(McGoobySchicht.Dunkel));
            var shake = McGoobyKatalog.TimingFuer("shake");
            Assert.That(McGoobySchicht.Zustand("shake", 3.5, shake), Is.EqualTo(McGoobySchicht.Krone));
            Assert.That(McGoobySchicht.Zustand("shake", 5.1, shake), Is.EqualTo(McGoobySchicht.Schaum));
            Assert.That(McGoobySchicht.Bewerte("shake", 4.0, shake, bal).Punkte, Is.EqualTo(10));
            Assert.That(McGoobySchicht.Fortschritt(99, grill), Is.EqualTo(1.0));
        }

        [Test]
        public void TheAssemblyTicket()
        {
            var bal = McGoobyKatalog.Balance();
            var ticket = McGoobySchicht.TicketVon(McGoobyKatalog.Rezept("kaese_knusperle"));
            Assert.That(ticket, Is.EqualTo(new[] { "broetchen", "patty", "kaese", "kaese", "broetchen" }));
            Assert.That(McGoobySchicht.Leiste(ticket), Is.EqualTo(new[] { "broetchen", "patty", "kaese" }));
            var (falsch, p0) = McGoobySchicht.BewerteWisch(ticket, 0, "kaese", bal);
            Assert.That((falsch.Art, falsch.Punkte, p0), Is.EqualTo((McGoobySchicht.Falsch, -2, 0)));
            var (richtig, p1) = McGoobySchicht.BewerteWisch(ticket, 0, "broetchen", bal);
            Assert.That((richtig.Art, richtig.Punkte, p1), Is.EqualTo((McGoobySchicht.Richtig, 5, 1)));
            Assert.That(McGoobySchicht.AktionenFuer(McGoobyKatalog.Rezept("nugget_woelkchen"), "fritteuse").Count, Is.EqualTo(3));
        }

        [Test]
        public void TheTillPaysComboTips()
        {
            var bal = McGoobyKatalog.Balance();
            Assert.That(McGoobySchicht.ComboMult(5, bal), Is.EqualTo(1.5).Within(1e-9));
            Assert.That(McGoobySchicht.ComboMult(20, bal), Is.EqualTo(2.0).Within(1e-9));
            Assert.That(McGoobySchicht.MuenzenFuer(0, bal), Is.EqualTo(6));
            Assert.That(McGoobySchicht.MuenzenFuer(40, bal), Is.EqualTo(10));
            Assert.That(McGoobySchicht.MuenzenFuer(9999, bal), Is.EqualTo(30));
            var k = McGoobySchicht.Abrechnung(new[] { (35, true), (25, true), (20, false), (45, true) }, bal);
            Assert.That((k.Punkte, k.Trinkgeld, k.MuenzenBasis, k.Muenzen, k.Fehlerfreie), Is.EqualTo((125, 6, 30, 36, 3)));
            Assert.That(k.ComboMaxMult, Is.EqualTo(1.2).Within(1e-9));
        }

        [Test]
        public void SliceStarsAndBuying()
        {
            var state = JObject.Parse(@"{""mcgooby"":{""schichten"":{""gespielt"":3,""bestwert"":""x""}},""progression"":{""level"":3},""economy"":{""coins"":5000}}");
            var s = McGoobyState.Slice(state);
            Assert.That(J.Int(s["schichten"]["gespielt"]), Is.EqualTo(3), "kept verbatim");
            Assert.That(J.Int(s["schichten"]["bestwert"]), Is.EqualTo(0), "junk heals");
            Assert.That(McGoobyState.SchichtErlaubt(state, "2026-10-11"), Is.True);
            McGoobyState.DemoVerbuchen(state, "2026-10-11");
            Assert.That(McGoobyState.SchichtErlaubt(state, "2026-10-11"), Is.False, "one demo a day");
            Assert.That(McGoobyState.SchichtErlaubt(state, "2026-10-12"), Is.True);
            Assert.That(McGoobyState.Check(state), Is.EqualTo(McGoobyState.KaufGesperrt), "level 3 < 14");
            state["progression"]["level"] = 14;
            Assert.That(McGoobyState.Kaufe(state, 99), Is.EqualTo(McGoobyState.KaufOk));
            Assert.That(J.Int(state["economy"]["coins"]), Is.EqualTo(2000));
            Assert.That(McGoobyState.Kaufe(state, 100), Is.EqualTo(McGoobyState.KaufSchon));
            Assert.That(McGoobyState.SchichtErlaubt(state, "2026-10-11"), Is.True, "bought: no limit");

            var fresh = new JObject();
            Assert.That(McGoobyState.Sterne(fresh), Is.EqualTo(0));
            Assert.That(McGoobyState.Naechster(fresh).FehltSchichten, Is.EqualTo(1));
            McGoobyState.SchichtVerbuchen(fresh, 90, true);
            McGoobyState.SchichtVerbuchen(fresh, 60, false);
            Assert.That(McGoobyState.Stand(fresh), Is.EqualTo((2, 90, 1)), "best never goes down");
            Assert.That(McGoobyState.Sterne(fresh), Is.EqualTo(1));
            var next = McGoobyState.Naechster(fresh);
            Assert.That((next.Stern, next.FehltSchichten, next.FehltBestwert), Is.EqualTo((2, 3, 0)));
            Assert.That(McGoobyState.AufstiegPruefen(fresh, 0), Is.EqualTo((true, 1)));
            Assert.That(McGoobyState.AufstiegPruefen(fresh, 0), Is.EqualTo((false, 1)), "celebrated once");
            var voll = JObject.Parse(@"{""mcgooby"":{""schichten"":{""gespielt"":40,""bestwert"":200,""perfekt"":3}}}");
            Assert.That(McGoobyState.Sterne(voll), Is.EqualTo(5));
            Assert.That(McGoobyState.Naechster(voll), Is.Null);
        }
    }
}
