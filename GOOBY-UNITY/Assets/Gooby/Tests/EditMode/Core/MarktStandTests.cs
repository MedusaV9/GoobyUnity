using System.Collections.Generic;
using System.Linq;
using Gooby.Core.City;
using Gooby.Core.Garden;
using Gooby.Core.Home;
using Gooby.Core.Json;
using Gooby.Core.Save;
using Gooby.Core.State;
using NUnit.Framework;

namespace Gooby.Tests.Core
{
    /// <summary>
    /// The own market stall (port of test_w15_markt.gd): the ware catalogue and its base values, a deterministic and
    /// price-monotone market day, the exact settlement card, and the stall's week — stock on Monday, wait, run on
    /// Saturday morning (still restockable), settle once, leftovers back, coins in.
    /// </summary>
    public class MarktStandTests
    {
        private const long Monday = 1_791_158_400_000L + 10 * 3_600_000L;   // 2026-10-05 10:00 UTC
        private const long Saturday = 1_791_590_400_000L;                   // 2026-10-10 00:00 UTC
        private const long Hour = 3_600_000L;

        [SetUp]
        public void Setup()
        {
            Fixtures.InstallContentSource();
            HomeState.RegisterSlice();
            RoomDefs.ResetCache();
            FurnitureCatalog.ResetCache();
            MarktWaren.ResetCache();
        }

        private static GameStateStore Store()
        {
            var store = new GameStateStore { Manager = { Io = new MemorySaveIo() } };
            store.Clock.Pin(Monday);
            store.Initialize("user/save_v5.json", 0);
            HomeState.EnsureInitialized(store);
            store.Update(s =>
            {
                J.EnsureDict(s, "economy")["coins"] = 100;
                var food = J.EnsureDict(J.EnsureDict(s, "inventory"), "food");
                food["carrot"] = 12;
                food["pumpkin"] = 3;
                J.EnsureDict(J.EnsureDict(s, "inventory"), "items")["kraeuter"] = 2;
            });
            HomeState.StoreItem(store, "stool_rustic");
            return store;
        }

        private static int Coins(GameStateStore gs) => J.Int(J.Path(gs.State, "economy", "coins"));
        private static int Food(GameStateStore gs, string id) => J.Int(J.Path(gs.State, "inventory", "food", id));

        [Test]
        public void TheWareCatalogueKnowsHarvestCraftAndSouvenirs()
        {
            Assert.That(MarktWaren.Waren().Count, Is.EqualTo(15));
            Assert.That(MarktWaren.Basis("carrot"), Is.EqualTo(MarktLogic.Marktpreis("carrot")), "harvest = the market's buying price");
            Assert.That(MarktWaren.Basis("kraeuter"), Is.EqualTo(18));
            Assert.That(MarktWaren.Basis("stool_rustic"), Is.EqualTo(30), "furniture at its resale value");
            Assert.That(MarktWaren.Basis("gibtEsNicht"), Is.EqualTo(0));
            Assert.That(MarktWaren.Anzeigename("weltraumMoehre", "en"), Is.EqualTo("Space Carrot"));
            var gs = Store();
            var angebot = MarktWaren.AngebotDesSpielers(gs).Select(a => a.Id + ":" + a.Vorrat).ToList();
            Assert.That(angebot, Is.EqualTo(new[] { "carrot:12", "pumpkin:3", "kraeuter:2", "stool_rustic:1" }));
        }

        [Test]
        public void TheMarketDayIsDeterministicAndCheaperNeverSellsLess()
        {
            var seed = MarktSim.TagesSeed("2026-10-10");
            List<MarktSim.Slot> Slots(double f) => new List<MarktSim.Slot>
            {
                new MarktSim.Slot { Ware = "carrot", Menge = 10, Faktor = f },
                new MarktSim.Slot { Ware = "pumpkin", Menge = 3, Faktor = f },
            };
            var a = MarktSim.Simulate(Slots(1.0), seed);
            var b = MarktSim.Simulate(Slots(1.0), seed);
            Assert.That(a.Kunden, Is.InRange(MarktSim.KundenMin, MarktSim.KundenMax));
            Assert.That(a.Erloes, Is.EqualTo(b.Erloes));
            Assert.That(a.Events.Select(e => e.Minute + e.Ware + e.Gekauft), Is.EqualTo(b.Events.Select(e => e.Minute + e.Ware + e.Gekauft)));
            Assert.That(a.Events.Select(e => e.Minute), Is.Ordered, "customers arrive through the morning");
            var cheap = MarktSim.Simulate(Slots(0.5), seed);
            var dear = MarktSim.Simulate(Slots(1.5), seed);
            foreach (var ware in new[] { "carrot", "pumpkin" })
            {
                cheap.Verkauft.TryGetValue(ware, out var c);
                a.Verkauft.TryGetValue(ware, out var m);
                dear.Verkauft.TryGetValue(ware, out var d);
                Assert.That(c, Is.GreaterThanOrEqualTo(m), ware + ": half price sells at least as much");
                Assert.That(m, Is.GreaterThanOrEqualTo(d), ware + ": base price sells at least as much as 150 %");
            }
            Assert.That(MarktSim.Stueckpreis("pumpkin", 1.5), Is.EqualTo((int)System.Math.Round(MarktWaren.Basis("pumpkin") * 1.5, System.MidpointRounding.AwayFromZero)));
            Assert.That(MarktSim.Attraktivitaet(0.5), Is.GreaterThan(MarktSim.Attraktivitaet(1.0)));
        }

        [Test]
        public void TheSettlementCardIsExact()
        {
            var slots = new List<MarktSim.Slot>
            {
                new MarktSim.Slot { Ware = "carrot", Menge = 5, Faktor = 1.0 },
                new MarktSim.Slot { Ware = "pumpkin", Menge = 2, Faktor = 0.5 },
            };
            var ergebnis = new MarktSim.Ergebnis { Verkauft = { ["carrot"] = 3, ["pumpkin"] = 2 } };
            var karte = MarktSim.Abrechnen(slots, ergebnis);
            Assert.That(karte.Zeilen[0].Verkauft, Is.EqualTo(3));
            Assert.That(karte.Zeilen[0].Uebrig, Is.EqualTo(2));
            Assert.That(karte.Zeilen[0].Erloes, Is.EqualTo(3 * MarktSim.Stueckpreis("carrot", 1.0)));
            Assert.That(karte.Zeilen[1].Erloes, Is.EqualTo(2 * MarktSim.Stueckpreis("pumpkin", 0.5)));
            Assert.That(karte.Erloes, Is.EqualTo(karte.Zeilen.Sum(z => z.Erloes)));
            Assert.That(karte.BesteWare, Is.EqualTo(karte.Zeilen.OrderByDescending(z => z.Erloes).First().Ware));
        }

        [Test]
        public void TheStallsWeekRunsFromMondayToTheSettlement()
        {
            var gs = Store();
            Assert.That(MarktStand.Status(gs, Monday, 0), Is.EqualTo(MarktStand.StatusLeer));
            Assert.That(MarktStand.NaechsterMarktTag(Monday, 0), Is.EqualTo("2026-10-10"));
            Assert.That(MarktStand.Bestuecke(gs, Monday, 0, "carrot", 8, 0.8), Is.EqualTo(8));
            Assert.That(MarktStand.Bestuecke(gs, Monday, 0, "stool_rustic", 5), Is.EqualTo(1), "only what is in storage");
            Assert.That(MarktStand.Bestuecke(gs, Monday, 0, "gibtEsNicht", 1), Is.EqualTo(0));
            Assert.That(Food(gs, "carrot"), Is.EqualTo(4));
            Assert.That(StorageLogic.CountOf(HomeState.Storage(gs), "stool_rustic"), Is.EqualTo(0));
            var slice = MarktStand.SliceVon(gs);
            Assert.That(slice.Tag, Is.EqualTo("2026-10-10"));
            Assert.That(slice.Slots.Select(s => s.Ware + ":" + s.Menge), Is.EqualTo(new[] { "carrot:8", "stool_rustic:1" }));
            Assert.That(slice.Slots[0].Faktor, Is.EqualTo(0.8).Within(1e-9));
            Assert.That(MarktStand.Status(gs, Monday, 0), Is.EqualTo(MarktStand.StatusWartet));
            Assert.That(MarktStand.Abholen(gs, Monday, 0), Is.Null, "nothing to collect before the market");

            Assert.That(MarktStand.SetFaktor(gs, Monday, 0, "carrot", 9.0), Is.True);
            Assert.That(MarktStand.SliceVon(gs).Slots[0].Faktor, Is.EqualTo(MarktStand.FaktorMax).Within(1e-9), "clamped to +50 %");
            Assert.That(MarktStand.SetFaktor(gs, Monday, 0, "carrot", 0.93), Is.True);
            Assert.That(MarktStand.SliceVon(gs).Slots[0].Faktor, Is.EqualTo(0.95).Within(1e-9), "snapped to 5 % steps");
            Assert.That(MarktStand.Entnehme(gs, Monday, 0, "carrot", 2), Is.EqualTo(2));
            Assert.That(Food(gs, "carrot"), Is.EqualTo(6));

            Assert.That(MarktStand.Status(gs, Saturday + 7 * Hour, 0), Is.EqualTo(MarktStand.StatusWartet), "seven o'clock: not open yet");
            var running = Saturday + 10 * Hour;
            Assert.That(MarktStand.Status(gs, running, 0), Is.EqualTo(MarktStand.StatusLaeuft));
            Assert.That(MarktStand.Bestuecke(gs, running, 0, "pumpkin", 3), Is.EqualTo(3), "restocking while the market runs");
            Assert.That(MarktStand.SliceVon(gs).Tag, Is.EqualTo("2026-10-10"));
            var after = Saturday + 15 * Hour;
            Assert.That(MarktStand.Status(gs, after, 0), Is.EqualTo(MarktStand.StatusFertig));
            Assert.That(MarktStand.Bestuecke(gs, after, 0, "carrot", 1), Is.EqualTo(0), "settle first");

            var expected = MarktSim.Abrechnen(MarktStand.SliceVon(gs).Slots, MarktStand.Ergebnis(gs));
            var coins = Coins(gs);
            var carrots = Food(gs, "carrot");
            var karte = MarktStand.Abholen(gs, after, 0);
            Assert.That(karte, Is.Not.Null);
            Assert.That(karte.Erloes, Is.EqualTo(expected.Erloes));
            Assert.That(Coins(gs), Is.EqualTo(coins + karte.Erloes));
            Assert.That(Food(gs, "carrot"), Is.EqualTo(carrots + karte.Zeilen.First(z => z.Ware == "carrot").Uebrig), "leftovers back home");
            Assert.That(J.Int(J.Path(gs.State, "achievements", "counters", "sells")), Is.EqualTo(karte.Zeilen.Sum(z => z.Verkauft)));
            Assert.That(MarktStand.MarktTage(gs), Is.EqualTo(1));
            Assert.That(MarktStand.Status(gs, after, 0), Is.EqualTo(MarktStand.StatusLeer));
            Assert.That(MarktStand.Abholen(gs, after, 0), Is.Null, "settled once");
            Assert.That(MarktStand.NaechsterMarktTag(after, 0), Is.EqualTo("2026-10-17"), "after two o'clock the next market is next week");
        }
    }
}
