using System.Linq;
using Gooby.Core.Json;
using Gooby.Core.Life;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Gooby.Tests.Core
{
    /// <summary>
    /// The holiday as an experience (ports of test_w15_urlaub*.gd and test_w19_mitbringsel.gd): every destination has a
    /// scene, a visit only while Gooby is there, tap spots count once, one find a day at the souvenir spot, the reunion is
    /// celebrated exactly once with the same souvenir for the same trip, and the airport window sells the other two for
    /// 24 hours.
    /// </summary>
    public class UrlaubTests
    {
        private const long D = Vacation.MsPerDay;
        private const long T0 = 1_760_000_000_000L;

        private static JObject Away(string ziel, long bookedAt, int days = 3) => new JObject
        {
            ["economy"] = new JObject { ["coins"] = 100 },
            ["inventory"] = new JObject { ["items"] = new JObject() },
            ["vacation"] = new JObject
            {
                ["phase"] = Vacation.PhaseAway, ["destId"] = ziel, ["bookedAt"] = bookedAt,
                ["returnAt"] = bookedAt + days * D, ["pickupBy"] = bookedAt + days * D + Vacation.PickupWindowMs,
            },
        };

        private static JObject Home(string ziel, long abflug, long abgeholt) => new JObject
        {
            ["economy"] = new JObject { ["coins"] = 100 },
            ["inventory"] = new JObject { ["items"] = new JObject() },
            ["vacation"] = new JObject { ["phase"] = Vacation.PhaseNone, ["heimkehrZiel"] = ziel, ["heimkehrAbflug"] = abflug, ["heimkehrAt"] = abgeholt },
        };

        [Test]
        public void EveryDestinationHasAScene()
        {
            foreach (var ziel in Gooby.Core.City.Travel.Ziele)
                Assert.That(Urlaub.ArchetypFuer(ziel), Is.Not.Empty, ziel);
            Assert.That(Urlaub.ArchetypFuer("beach"), Is.EqualTo(Urlaub.Strand));
            Assert.That(Urlaub.ArchetypFuer("space"), Is.EqualTo(Urlaub.Weltraum));
            Assert.That(Urlaub.ArchetypFuer("mars"), Is.Empty);
        }

        [Test]
        public void AVisitOnlyWhileGoobyIsThere()
        {
            var s = Away("beach", T0);
            Assert.That(Urlaub.Verfuegbar(s, T0 + D), Is.True);
            Assert.That(Urlaub.Verfuegbar(s, T0 + 3 * D + 1), Is.False, "back at the airport: no visit");
            Assert.That(Urlaub.Verfuegbar(new JObject(), T0), Is.False);
            Urlaub.MerkeBesuch(s, "beach");
            Assert.That(J.IsTrue(J.Path(s, "vacation", "besuche", "beach")), Is.True);
            Assert.That(J.Str(J.Path(s, "vacation", "phase")), Is.EqualTo(Vacation.PhaseAway), "the trip stays as it is");
        }

        [Test]
        public void TapSpotsCountOnce()
        {
            var spots = new Urlaub.TapSpots();
            for (var i = 0; i < 4; i++) Assert.That(spots.Tippe(i), Is.True);
            Assert.That(spots.Tippe(2), Is.False, "a second tap does not count");
            Assert.That(spots.Tippe(9), Is.False);
            Assert.That(spots.Fertig, Is.False);
            Assert.That(spots.Tippe(4), Is.True);
            Assert.That(spots.Fertig, Is.True);
            Assert.That(Urlaub.BestellungIndex(7, 3), Is.EqualTo(1));
            Assert.That(Urlaub.BestellungIndex(-1, 3), Is.EqualTo(2));
        }

        [Test]
        public void OneFindADayAtTheSouvenirSpot()
        {
            var s = Away("harbor", T0);
            Assert.That(Urlaub.SouvenirEinloesen(s, "harbor", T0 + D), Is.True);
            Assert.That(J.Int(J.Path(s, "inventory", "items", "souvenir_harbor")), Is.EqualTo(1));
            Assert.That(J.Int(J.Path(s, "economy", "coins")), Is.EqualTo(100 + Urlaub.SouvenirCoins));
            Assert.That(Urlaub.SouvenirEinloesen(s, "harbor", T0 + D + 3_600_000), Is.False, "same day: empty");
            Assert.That(Urlaub.SouvenirEinloesen(s, "harbor", T0 + 2 * D), Is.True, "the next day: a new find");
            Assert.That(J.Int(J.Path(s, "inventory", "items", "souvenir_harbor")), Is.EqualTo(2));
        }

        [Test]
        public void TheReunionIsCelebratedOnceWithTheTripsOwnSouvenir()
        {
            var s = Home("bigCity", T0, T0 + 4 * D);
            Assert.That(Urlaub.HeimkehrAusstehend(s), Is.True);
            var r = Urlaub.Feiern(s);
            Assert.That(r.Ok, Is.True);
            Assert.That(r.ItemId, Is.EqualTo("mitbringsel_bigCity_" + r.Typ));
            Assert.That(Urlaub.Typen, Does.Contain(r.Typ));
            Assert.That(r.SpruchKey, Is.EqualTo("heimkehr.spruch.1").Or.EqualTo("heimkehr.spruch.2"));
            Assert.That(J.Int(J.Path(s, "inventory", "items", r.ItemId)), Is.EqualTo(1));
            Assert.That(Urlaub.Feiern(s).Ok, Is.False, "exactly once");
            Assert.That(Urlaub.MitbringselTyp("bigCity", T0), Is.EqualTo(r.Typ), "same trip, same souvenir");
            var typen = Enumerable.Range(0, 30).Select(i => Urlaub.MitbringselTyp("beach", T0 + i * 7919L)).Distinct().Count();
            Assert.That(typen, Is.EqualTo(3), "all three kinds come up over many trips");
            Assert.That(Urlaub.HeimkehrAusstehend(Away("beach", T0)), Is.False, "still away");
        }

        [Test]
        public void TheAirportWindowSellsTheOtherTwoForADay()
        {
            var pickedUp = T0 + 3 * D;
            var s = Home("meadowTrip", T0, pickedUp);
            var mitgebracht = Urlaub.MitbringselTyp("meadowTrip", T0);
            var offer = Urlaub.FensterSortiment(s, pickedUp + 3_600_000);
            Assert.That(offer.Select(w => w.Typ), Is.EquivalentTo(Urlaub.Typen.Where(t => t != mitgebracht)));
            Assert.That(Urlaub.FensterRestH(s, pickedUp + 3_600_000), Is.EqualTo(23));
            var cheap = offer.OrderBy(w => w.Preis).First();
            Assert.That(Urlaub.Kaufe(s, cheap.Id, pickedUp + 3_600_000), Is.EqualTo(Urlaub.KaufOk));
            Assert.That(J.Int(J.Path(s, "economy", "coins")), Is.EqualTo(100 - cheap.Preis));
            Assert.That(J.Int(J.Path(s, "inventory", "items", cheap.Id)), Is.EqualTo(1));
            Assert.That(Urlaub.Kaufe(s, Urlaub.MitbringselId("meadowTrip", mitgebracht), pickedUp + 3_600_000), Is.EqualTo(Urlaub.KaufZu), "the brought one is not on offer");
            s["economy"]["coins"] = 10;
            var other = offer.First(w => w.Id != cheap.Id);
            Assert.That(Urlaub.Kaufe(s, other.Id, pickedUp + 3_600_000), Is.EqualTo(Urlaub.KaufPleite));
            Assert.That(J.Int(J.Path(s, "inventory", "items", other.Id)), Is.EqualTo(0), "nothing without coins");
            Assert.That(Urlaub.FensterSortiment(s, pickedUp + Urlaub.FensterMs), Is.Empty, "closed after 24 h");
        }

        [Test]
        public void KeepsakesAreListed()
        {
            var s = new JObject
            {
                ["inventory"] = new JObject
                {
                    ["items"] = new JObject
                    {
                        ["souvenir_beach"] = 2, ["mitbringsel_toyRoom_wimpel"] = 1, ["mitbringsel_mars_wimpel"] = 1,
                        ["mitbringsel_beach_quatsch"] = 1, ["seed"] = 4, ["souvenir_space"] = 0,
                    },
                },
            };
            var list = Urlaub.Sammlung(s);
            Assert.That(list.Select(a => a.ItemId + ":" + a.Typ + ":" + a.Anzahl), Is.EqualTo(new[] { "mitbringsel_toyRoom_wimpel:wimpel:1", "souvenir_beach:fund:2" }));
        }
    }
}
