using System.Linq;
using Gooby.Core.City;
using Gooby.Core.Home;
using Gooby.Core.Json;
using Gooby.Core.Life;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Gooby.Tests.Core
{
    /// <summary>
    /// GOOBY FREE at the gate (port of flughafen.gd kaufe_gfree / gooby_free_offen and the W19 souvenir window): open only
    /// between booking and boarding or in the 24-hour window after a pickup, Godot's assortment and prices, deco into the
    /// storage and snacks into the fridge atomically, and a boarding pass that follows the booking.
    /// </summary>
    public class GoobyFreeTests
    {
        private const long D = Vacation.MsPerDay;
        private const long T0 = 1_760_000_000_000L;
        private const long Min = 60_000L;

        [SetUp]
        public void Content()
        {
            Fixtures.InstallContentSource();
            FurnitureCatalog.ResetCache();
        }

        private static JObject Booked(string ziel, long bookedAt, int coins = 300) => new JObject
        {
            ["economy"] = new JObject { ["coins"] = coins },
            ["inventory"] = new JObject { ["items"] = new JObject(), ["food"] = new JObject() },
            ["home"] = new JObject { ["storage"] = new JArray(), ["storageCapacity"] = 100 },
            ["vacation"] = new JObject
            {
                ["phase"] = Vacation.PhaseAway, ["destId"] = ziel, ["bookedAt"] = bookedAt,
                ["returnAt"] = bookedAt + 3 * D, ["pickupBy"] = bookedAt + 3 * D + Vacation.PickupWindowMs,
            },
        };

        [Test]
        public void TheGateIsOpenFromTheBookingUntilBoardingCloses()
        {
            var s = Booked("beach", T0);
            Assert.That(GoobyFree.AmGate(s, T0), Is.True);
            Assert.That(GoobyFree.BoardingRestMin(s, T0 + Min), Is.EqualTo(19));
            Assert.That(GoobyFree.AmGate(s, T0 + GoobyFree.BoardingMs - 1), Is.True);
            Assert.That(GoobyFree.AmGate(s, T0 + GoobyFree.BoardingMs), Is.False, "boarding closed: Gooby is in the air");
            Assert.That(GoobyFree.BoardingRestMin(s, T0 + GoobyFree.BoardingMs), Is.EqualTo(0));
            Assert.That(GoobyFree.AmGate(s, T0 - 1), Is.False);
            Assert.That(GoobyFree.Offen(new JObject(), T0), Is.False, "no booking, no duty-free");
            s["vacation"]["phase"] = Vacation.PhaseNone;
            Assert.That(GoobyFree.AmGate(s, T0 + Min), Is.False);
        }

        [Test]
        public void TheShelfHasGodotsAssortmentThatNoOtherShopSells()
        {
            var offer = GoobyFree.Angebot(Booked("space", T0), T0 + Min);
            Assert.That(offer.Select(w => w.Id + ":" + w.Preis), Is.EqualTo(new[]
            {
                "gfree_shuttle:149", "gfree_flitzer:129", "gfree_meteorit:99", "gfree_sternmoehre:88", "gfree_mondstein:66", "sandwich:24", "cookie:13",
            }));
            foreach (var w in offer.Where(w => w.Typ == GoobyFree.TypMoebel))
            {
                var def = FurnitureCatalog.Def(w.Id);
                Assert.That(def, Is.Not.Null, w.Id);
                Assert.That(MoebelShop.Sellable(def), Is.False, w.Id + " stays exclusive (catalog price 0)");
                Assert.That(def.Verkaufswert, Is.GreaterThan(0).And.LessThan(w.Preis), w.Id + " sells back for less");
            }
            Assert.That(MoebelShop.Filter("").Any(d => d.Id.StartsWith("gfree_")), Is.False);
            Assert.That(GoobyFree.Angebot(Booked("space", T0), T0 + GoobyFree.BoardingMs), Is.Empty);
        }

        [Test]
        public void DecoGoesIntoTheStorageAndSnacksIntoTheFridgeOrNothingHappens()
        {
            var s = Booked("harbor", T0);
            var now = T0 + 2 * Min;
            Assert.That(GoobyFree.Kaufe(s, "gfree_meteorit", now), Is.EqualTo(GoobyFree.Ok));
            Assert.That(J.Int(J.Path(s, "economy", "coins")), Is.EqualTo(201));
            Assert.That(StorageLogic.CountOf((JArray)J.Path(s, "home", "storage"), "gfree_meteorit"), Is.EqualTo(1));
            Assert.That(GoobyFree.Kaufe(s, "sandwich", now), Is.EqualTo(GoobyFree.Ok));
            Assert.That(J.Int(J.Path(s, "inventory", "food", "sandwich")), Is.EqualTo(1));
            Assert.That(J.Int(J.Path(s, "economy", "coins")), Is.EqualTo(177));

            Assert.That(GoobyFree.Kaufe(s, "gfree_shuttle", now), Is.EqualTo(GoobyFree.Ok));
            Assert.That(GoobyFree.Kaufe(s, "gfree_flitzer", now), Is.EqualTo(GoobyFree.Pleite), "28 coins left");
            Assert.That(J.Int(J.Path(s, "economy", "coins")), Is.EqualTo(28));
            Assert.That(StorageLogic.CountOf((JArray)J.Path(s, "home", "storage"), "gfree_flitzer"), Is.EqualTo(0));

            var full = Booked("harbor", T0, 500);
            full["home"]["storageCapacity"] = 1;
            ((JArray)full["home"]["storage"]).Add(new JObject { ["item"] = "chair", ["variant"] = "default", ["count"] = 1 });
            Assert.That(GoobyFree.Kaufe(full, "gfree_mondstein", now), Is.EqualTo(GoobyFree.Voll));
            Assert.That(J.Int(J.Path(full, "economy", "coins")), Is.EqualTo(500), "a full storage keeps the coins");
            Assert.That(GoobyFree.Kaufe(full, "cookie", now), Is.EqualTo(GoobyFree.Ok), "a snack still fits");
        }

        [Test]
        public void ClosedAfterBoardingAndUnknownWaresAreRefused()
        {
            var s = Booked("bakery", T0);
            Assert.That(GoobyFree.Kaufe(s, "gfree_shuttle", T0 + GoobyFree.BoardingMs + Min), Is.EqualTo(GoobyFree.Zu));
            Assert.That(GoobyFree.Kaufe(s, "sofa", T0 + Min), Is.EqualTo(GoobyFree.Unbekannt));
            Assert.That(J.Int(J.Path(s, "economy", "coins")), Is.EqualTo(300));
        }

        [Test]
        public void TheSouvenirWindowAfterAPickupOpensItToo()
        {
            var pickedUp = T0 + 3 * D;
            var s = new JObject
            {
                ["economy"] = new JObject { ["coins"] = 100 },
                ["inventory"] = new JObject { ["items"] = new JObject() },
                ["vacation"] = new JObject { ["phase"] = Vacation.PhaseNone, ["heimkehrZiel"] = "toyRoom", ["heimkehrAbflug"] = T0, ["heimkehrAt"] = pickedUp },
            };
            var now = pickedUp + 3_600_000;
            Assert.That(GoobyFree.AmGate(s, now), Is.False);
            Assert.That(GoobyFree.Offen(s, now), Is.True);
            var offer = GoobyFree.Angebot(s, now);
            Assert.That(offer.Count, Is.EqualTo(2));
            Assert.That(offer.All(w => w.Typ == GoobyFree.TypMitbringsel && w.ZielId == "toyRoom"), Is.True);
            var cheap = offer.OrderBy(w => w.Preis).First();
            Assert.That(GoobyFree.Kaufe(s, cheap.Id, now), Is.EqualTo(GoobyFree.Ok));
            Assert.That(J.Int(J.Path(s, "inventory", "items", cheap.Id)), Is.EqualTo(1));
            Assert.That(GoobyFree.Offen(s, pickedUp + Urlaub.FensterMs), Is.False, "closed after 24 h");
        }

        [Test]
        public void TheBoardingPassFollowsTheBooking()
        {
            var pass = GoobyFree.Karte(Booked("space", T0));
            Assert.That(pass, Is.Not.Null);
            Assert.That(pass.Von + "→" + pass.Nach, Is.EqualTo("SON→ALL"));
            Assert.That(pass.Flug, Does.Match(@"^GB \d{3}$"));
            Assert.That(pass.Gate, Does.Match(@"^[ABC][1-9]$"));
            Assert.That(pass.Sitz, Does.Match(@"^\d{1,2}[A-F]$"));
            Assert.That(pass.BoardingBis, Is.EqualTo(T0 + GoobyFree.BoardingMs));
            var again = GoobyFree.Karte(Booked("space", T0));
            Assert.That(again.Flug + again.Gate + again.Sitz, Is.EqualTo(pass.Flug + pass.Gate + pass.Sitz), "same booking, same pass");
            var flights = Enumerable.Range(1, 12).Select(i => GoobyFree.Karte(Booked("beach", T0 + i * 7919L)).Flug).Distinct().Count();
            Assert.That(flights, Is.GreaterThan(6), "other bookings print other flights");
            foreach (var ziel in Travel.Ziele) Assert.That(GoobyFree.Codes.ContainsKey(ziel), Is.True, ziel);
            Assert.That(GoobyFree.Karte(new JObject()), Is.Null, "no trip, no pass");
        }
    }
}
