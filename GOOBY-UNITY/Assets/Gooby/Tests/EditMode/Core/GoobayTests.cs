using System.Linq;
using Gooby.Core.Home;
using Gooby.Core.Json;
using Gooby.Core.Random;
using Gooby.Core.Save;
using Gooby.Core.State;
using NUnit.Framework;

namespace Gooby.Tests.Core
{
    /// <summary>
    /// Goobay (port of test_home_goobay.gd): the hidden bands of a new haggle, every "Höher!" asks more and sours the mood
    /// until a last offer or the buyer leaves, the post bonus, the last-of-its-slot rule, deals and failed deals.
    /// </summary>
    public class GoobayTests
    {
        private const long NowMs = 1_791_158_400_000L;

        [SetUp]
        public void Setup()
        {
            Fixtures.InstallContentSource();
            HomeState.RegisterSlice();
            FurnitureCatalog.ResetCache();
        }

        private static GameStateStore Store()
        {
            var store = new GameStateStore { Manager = { Io = new MemorySaveIo() } };
            store.Clock.Pin(NowMs);
            store.Initialize("user/save_v5.json", 0);
            HomeState.EnsureInitialized(store);
            return store;
        }

        [Test]
        public void ANewHaggleDrawsItsHiddenValuesInsideTheBands()
        {
            for (var seed = 1; seed < 60; seed++)
            {
                var s = GoobayLogic.Start("stool_rustic", 30, 1.2, new GoobyRng(seed));
                Assert.That(s.Wert, Is.EqualTo(36));
                Assert.That(s.Angebot, Is.InRange((int)(36 * 0.55) - 1, (int)(36 * 0.75) + 1));
                Assert.That(s.Budget, Is.InRange((int)(36 * 0.95) - 1, (int)(36 * 1.35) + 1));
                Assert.That(s.Geduld, Is.InRange(GoobayLogic.GeduldMin, GoobayLogic.GeduldMax));
                Assert.That(s.Status, Is.EqualTo(GoobayLogic.StatusOffen));
            }
        }

        [Test]
        public void AskingForMoreRaisesTheOfferUntilALastOfferOrTheBuyerLeaves()
        {
            var rng = new GoobyRng(4);
            var s = GoobayLogic.Start("vogelhaus", 35, 1.0, rng);
            var before = s.Angebot;
            var steps = 0;
            while (s.Status == GoobayLogic.StatusOffen && steps++ < 10)
            {
                var offer = s.Angebot;
                GoobayLogic.Hoeher(s, rng);
                Assert.That(s.Stimmung, Is.EqualTo(System.Math.Min(GoobayLogic.StimmungMax, steps)), "every ask sours the mood");
                if (s.Status == GoobayLogic.StatusOffen) Assert.That(s.Angebot, Is.GreaterThan(offer));
            }
            Assert.That(s.Status, Is.EqualTo(GoobayLogic.StatusFinal).Or.EqualTo(GoobayLogic.StatusAbbruch));
            if (s.Status == GoobayLogic.StatusFinal)
            {
                Assert.That(s.Angebot, Is.LessThanOrEqualTo(s.Budget), "the last offer never tops the budget");
                Assert.That(s.Angebot, Is.GreaterThanOrEqualTo(before));
                var deal = GoobayLogic.Annehmen(s);
                Assert.That(deal.Status, Is.EqualTo(GoobayLogic.StatusDeal));
                Assert.That(deal.Erloes, Is.EqualTo(deal.Angebot));
            }
            Assert.That(GoobayLogic.IstBeendet(GoobayLogic.Abbrechen(GoobayLogic.Start("x", 10, 1.0, new GoobyRng(1)))), Is.True);
            var finalOne = new GoobayLogic.Session { Status = GoobayLogic.StatusFinal, Angebot = 20 };
            Assert.That(GoobayLogic.Hoeher(finalOne, rng).Status, Is.EqualTo(GoobayLogic.StatusAbbruch), "after the very last offer the buyer leaves");
            Assert.That(GoobayLogic.PostBonus(100), Is.EqualTo(110));
            Assert.That(GoobayLogic.PostBonus(1), Is.EqualTo(2), "rounded up");
            Assert.That(GoobayLogic.NachfrageNachAbbruch(0.85), Is.EqualTo(GoobayLogic.NachfrageMin));
        }

        [Test]
        public void TheLastFridgeStaysAndDealsPayAndCount()
        {
            var gs = Store();
            Assert.That(GoobayState.IstPflichtGesperrt(gs, "kitchenFridge"), Is.True, "the only fridge in the kitchen");
            Assert.That(HomeState.StoreItem(gs, "kitchenFridge"), Is.True);
            Assert.That(GoobayState.IstPflichtGesperrt(gs, "kitchenFridge"), Is.False, "a second one may go");
            Assert.That(HomeState.StoreItem(gs, "stool_rustic"), Is.True);
            var today = "2026-10-05";
            var listing = GoobayState.Angebote(gs, today);
            Assert.That(listing.Any(a => a.Item == "stool_rustic" && a.Wert == 30), Is.True);
            Assert.That(listing.Any(a => a.Item == "kitchenFridge"), Is.True);

            var coins = J.Int(J.Path(gs.State, "economy", "coins"));
            Assert.That(GoobayState.DealAbschliessen(gs, "stool_rustic", 40, versand: true), Is.EqualTo(44));
            Assert.That(J.Int(J.Path(gs.State, "economy", "coins")), Is.EqualTo(coins + 44));
            Assert.That(StorageLogic.CountOf(HomeState.Storage(gs), "stool_rustic"), Is.EqualTo(0));
            Assert.That(J.Int(J.Path(gs.State, "achievements", "counters", "goobaySales")), Is.EqualTo(1));
            Assert.That(GoobayState.DealAbschliessen(gs, "stool_rustic", 40, false), Is.EqualTo(0), "nothing left to sell");

            var n = GoobayState.Nachfrage(gs, "kueche", today, new GoobyRng(9));
            Assert.That(n, Is.InRange(GoobayLogic.NachfrageMin, GoobayLogic.NachfrageMax));
            Assert.That(GoobayState.Nachfrage(gs, "kueche", today, new GoobyRng(77)), Is.EqualTo(n), "stable for the day");
            GoobayState.AbbruchMerken(gs, "kitchenFridge", "kueche", today);
            Assert.That(GoobayState.Angebote(gs, today).Any(a => a.Item == "kitchenFridge"), Is.False, "not again today");
            Assert.That(GoobayState.Angebote(gs, "2026-10-06").Any(a => a.Item == "kitchenFridge"), Is.True, "tomorrow again");
            Assert.That(GoobayState.Nachfrage(gs, "kueche", today, new GoobyRng(3)), Is.EqualTo(GoobayLogic.NachfrageNachAbbruch(n)).Within(1e-9));
        }
    }
}
