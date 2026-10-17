using System.Linq;
using Gooby.Core.City;
using Gooby.Core.Json;
using Gooby.Core.Save;
using Gooby.Core.State;
using NUnit.Framework;

namespace Gooby.Tests.Core
{
    /// <summary>The counters' promises (<see cref="ShopTreue"/>): loyalty stamps and a present, the day's deal, the receipt.</summary>
    public class ShopTreueTests
    {
        private const long NowMs = 1_768_478_400_000L; // Thu 2026-01-15 12:00 UTC

        [SetUp]
        public void Setup() => Fixtures.InstallContentSource();

        private static GameStateStore Store(int coins = 5000)
        {
            var store = new GameStateStore { Manager = { Io = new MemorySaveIo() } };
            store.Clock.Pin(NowMs);
            store.Initialize("user/save_v5.json", 0);
            store.Update(s => { J.EnsureDict(s, "economy")["coins"] = coins; });
            return store;
        }

        private static int Coins(GameStateStore gs) => J.Int(J.Path(gs.State, "economy", "coins"));

        [Test]
        public void TenPurchasesFillTheCardAndThePresentStartsItAgain()
        {
            var gs = Store();
            var apfel = ShopTreue.Regal(ShopLogic.RehweiPath).First(w => w.IsFood);
            for (var i = 0; i < 9; i++) Assert.That(ShopLogic.Kaufe(gs, apfel, "mart"), Is.True);
            Assert.That(ShopTreue.Stempel(gs.State, "mart"), Is.EqualTo(9));
            Assert.That(ShopTreue.GeschenkAbholen(gs.State, "mart", ShopLogic.RehweiPath), Is.Null, "nine stamps are not ten");
            ShopLogic.Kaufe(gs, apfel, "mart");
            ShopLogic.Kaufe(gs, apfel, "mart");
            Assert.That(ShopTreue.Stempel(gs.State, "mart"), Is.EqualTo(ShopTreue.KarteStempel), "the card stops at ten");
            Assert.That(ShopTreue.Voll(gs.State, "mart"), Is.True);
            Assert.That(ShopTreue.Stempel(gs.State, "goobyman"), Is.EqualTo(0), "every shop has its own card");
            var erwartet = ShopTreue.GeschenkWare(ShopLogic.RehweiPath, "mart", 0);
            var preise = ShopTreue.Regal(ShopLogic.RehweiPath).Select(w => w.Preis).OrderBy(p => p).ToList();
            Assert.That(erwartet.Preis, Is.GreaterThanOrEqualTo(preise[preise.Count / 2]), "a present from the dearer half of the shelf");
            var vorher = ShopLogic.Vorrat(gs, erwartet);
            Ware geschenk = null;
            gs.Update(s => geschenk = ShopTreue.GeschenkAbholen(s, "mart", ShopLogic.RehweiPath));
            Assert.That(geschenk?.Id, Is.EqualTo(erwartet.Id));
            Assert.That(ShopLogic.Vorrat(gs, geschenk), Is.EqualTo(vorher + geschenk.Menge), "the present is on the shelf at home");
            Assert.That(ShopTreue.Stempel(gs.State, "mart"), Is.EqualTo(0));
            Assert.That(ShopTreue.Geschenke(gs.State, "mart"), Is.EqualTo(1));
        }

        [Test]
        public void TheDayHasOneDealThirtyPercentOff()
        {
            var tag = "2026-01-15";
            var deal = ShopTreue.Angebot(ShopLogic.RehweiPath, "mart", tag);
            Assert.That(deal, Is.Not.Null);
            Assert.That(ShopTreue.Angebot(ShopLogic.RehweiPath, "mart", tag).Id, Is.EqualTo(deal.Id), "the same all day");
            Assert.That(Enumerable.Range(10, 14).Select(d => ShopTreue.Angebot(ShopLogic.RehweiPath, "mart", "2026-01-" + d).Id).Distinct().Count(), Is.GreaterThan(2), "a different one most days");
            Assert.That(deal.NurEinmal || deal.IsEffekt || deal.BrauchtRezept, Is.False);
            var preis = ShopTreue.AngebotPreis(deal);
            Assert.That(preis, Is.EqualTo((int)System.Math.Round(deal.Preis * 0.7, System.MidpointRounding.AwayFromZero)));
            var gs = Store(1000);
            Assert.That(ShopLogic.Kaufe(gs, deal, "mart", preis), Is.True);
            Assert.That(Coins(gs), Is.EqualTo(1000 - preis));
            var zeile = ShopTreue.Bon(gs.State, "mart").Single();
            Assert.That((zeile.Id, zeile.Preis, zeile.Angebot), Is.EqualTo((deal.Id, preis, true)));
            Assert.That(ShopTreue.Angebot(ShopLogic.BaumarktPath, "baumarkt", tag).Kategorie, Is.EqualTo("material"), "Bodo's deal is a material pack, never a blueprint");
        }

        [Test]
        public void TheReceiptKeepsTheLastSixAndShopsWithoutACardStayClean()
        {
            var gs = Store();
            var regal = ShopTreue.Regal(ShopLogic.RehweiPath);
            for (var i = 0; i < 8; i++) ShopLogic.Kaufe(gs, regal[i % regal.Count], "mart");
            var bon = ShopTreue.Bon(gs.State, "mart");
            Assert.That(bon.Count, Is.EqualTo(ShopTreue.BonZeilen));
            Assert.That(bon.Last().Id, Is.EqualTo(regal[7 % regal.Count].Id), "newest last");
            ShopLogic.Kaufe(gs, regal[0], "autohaus");
            ShopLogic.Kaufe(gs, regal[0]);
            Assert.That(J.Path(gs.State, "city", "treue", "autohaus"), Is.Null);
            Assert.That(ShopTreue.Bon(gs.State, "mart").Count, Is.EqualTo(ShopTreue.BonZeilen), "a purchase without a shop stamps nothing");
            Assert.That(ShopLogic.Kaufe(Store(0), regal[0], "mart"), Is.False, "no coins, no stamp");
        }
    }
}
