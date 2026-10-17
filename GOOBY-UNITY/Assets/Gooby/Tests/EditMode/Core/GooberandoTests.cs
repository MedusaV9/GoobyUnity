using System.Linq;
using Gooby.Core.City;
using Gooby.Core.Json;
using Gooby.Core.Save;
using Gooby.Core.State;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Gooby.Tests.Core
{
    /// <summary>GOOBERANDO: order timer, doorbell, handover, tip buff, left-at-the-door, driver on the lattice.</summary>
    public class GooberandoTests
    {
        private const long Now = 1_768_478_400_000L;

        [SetUp]
        public void Setup()
        {
            Fixtures.InstallContentSource();
            CityMap.Reset();
            CityGrid.Reset();
            DriveModel.Reset();
            GooberandoRestaurants.Reset();
        }

        private static JObject Ordered(int prepS = 180) =>
            GooberandoLogic.Bestellen(GooberandoLogic.DefaultSlice(), Now, prepS, "pizza", 30).Slice;

        [Test]
        public void OrderCostsPricePlusFeeAndRejectsASecondOne()
        {
            var res = GooberandoLogic.Bestellen(GooberandoLogic.DefaultSlice(), Now, 180, "pizza", 30);
            Assert.That(res.Ok, Is.True);
            Assert.That(res.Kosten, Is.EqualTo(33), "price 30 + delivery fee 3");
            Assert.That(J.Str(res.Slice["state"]), Is.EqualTo("bestellt"));
            Assert.That((long)J.Num(res.Slice["fertigAt"]), Is.EqualTo(Now + 180_000));
            Assert.That(GooberandoLogic.Bestellen(res.Slice, Now, 180, "pizza", 30).Ok, Is.False, "no double order");
        }

        [Test]
        public void TickRingsAtFertigAtAndCountsDown()
        {
            var slice = Ordered();
            Assert.That(GooberandoLogic.LieferRestS(slice, Now + 60_000), Is.EqualTo(120));
            var res = GooberandoLogic.Tick(slice, Now + 180_000);
            Assert.That(J.Str(res.Slice["state"]), Is.EqualTo("vorDerTuer"));
            Assert.That(res.Events[0].Typ, Is.EqualTo("vor_der_tuer"));
        }

        [Test]
        public void HandoverThenTipBuffsForTwoHours()
        {
            var slice = GooberandoLogic.Tick(Ordered(), Now + 180_000).Slice;
            var ueb = GooberandoLogic.Uebergabe(slice, Now + 200_000);
            Assert.That(ueb.Ok, Is.True);
            Assert.That(ueb.GerichtId, Is.EqualTo("pizza"));
            Assert.That(J.Str(ueb.Slice["state"]), Is.EqualTo("trinkgeld"));
            var tg = GooberandoLogic.Trinkgeld(ueb.Slice, Now + 210_000, true, 0.29);
            Assert.That(tg.Kosten, Is.EqualTo(5));
            Assert.That(tg.Buff, Is.True, "roll 0.29 < 30 %");
            Assert.That(J.Str(tg.Slice["state"]), Is.EqualTo("idle"));
            Assert.That(J.Int(tg.Slice["lieferungen"]), Is.EqualTo(1));
            Assert.That(J.Int(tg.Slice["trinkgelder"]), Is.EqualTo(1));
            Assert.That(GooberandoLogic.BuffAktiv(tg.Slice, Now + 210_000 + 7_199_000), Is.True);
            Assert.That(GooberandoLogic.BuffAktiv(tg.Slice, Now + 210_000 + 7_200_001), Is.False, "2 h over");
            Assert.That(GooberandoLogic.EnergieDrainFaktor(tg.Slice, Now + 300_000), Is.EqualTo(0.75).Within(1e-9));
        }

        [Test]
        public void AHighRollDoesNotBuff()
        {
            var slice = GooberandoLogic.Uebergabe(GooberandoLogic.Tick(Ordered(), Now + 180_000).Slice, Now + 200_000).Slice;
            var tg = GooberandoLogic.Trinkgeld(slice, Now + 210_000, true, 0.31);
            Assert.That(tg.Buff, Is.False);
            Assert.That(GooberandoLogic.EnergieDrainFaktor(tg.Slice, Now), Is.EqualTo(1.0).Within(1e-9));
        }

        [Test]
        public void TheHintFiresOnceAfterThreeTipsSkipped()
        {
            var slice = GooberandoLogic.DefaultSlice();
            for (var i = 0; i < 3; i++)
            {
                slice = GooberandoLogic.Bestellen(slice, Now, 60, "pizza", 30).Slice;
                slice = GooberandoLogic.Tick(slice, Now + 60_000).Slice;
                slice = GooberandoLogic.Uebergabe(slice, Now + 61_000).Slice;
                var tg = GooberandoLogic.Trinkgeld(slice, Now + 62_000, false, 0.9);
                slice = tg.Slice;
                if (i < 2) Assert.That(tg.Hinweis, Is.False, "hint only on the third skip");
                else Assert.That(tg.Hinweis, Is.True);
            }
            slice = GooberandoLogic.Bestellen(slice, Now, 60, "pizza", 30).Slice;
            slice = GooberandoLogic.Tick(slice, Now + 60_000).Slice;
            slice = GooberandoLogic.Uebergabe(slice, Now + 61_000).Slice;
            Assert.That(GooberandoLogic.Trinkgeld(slice, Now + 62_000, false, 0.9).Hinweis, Is.False, "only once");
        }

        [Test]
        public void LeftAtTheDoorStillDeliversAndDoesNotCountAsSkippedTip()
        {
            var slice = GooberandoLogic.Tick(Ordered(), Now + 180_000).Slice;
            var res = GooberandoLogic.Tick(slice, Now + 180_000 + 301_000);
            Assert.That(J.Str(res.Slice["state"]), Is.EqualTo("idle"));
            Assert.That(res.Events[0].Typ, Is.EqualTo("abgestellt"));
            Assert.That(res.Events[0].GerichtId, Is.EqualTo("pizza"));
            Assert.That(J.Int(res.Slice["ohneTrinkgeldFolge"]), Is.EqualTo(0), "left-at-door is not a skipped tip");
            Assert.That(J.Int(res.Slice["lieferungen"]), Is.EqualTo(1));
        }

        [Test]
        public void CatchUpOverARestartFiresBothEvents()
        {
            var res = GooberandoLogic.Tick(Ordered(), Now + 3_600_000);
            Assert.That(res.Events.Select(e => e.Typ).ToArray(), Is.EqualTo(new[] { "vor_der_tuer", "abgestellt" }));
            Assert.That(J.Str(res.Slice["state"]), Is.EqualTo("idle"));
        }

        [Test]
        public void NormalizeHealsJunkAndTheCatalogHasThreeKitchens()
        {
            Assert.That(J.Str(GooberandoLogic.Normalize(42)["state"]), Is.EqualTo("idle"));
            var healed = GooberandoLogic.Normalize(new JObject { ["state"] = "kaputt", ["lieferungen"] = -2 });
            Assert.That(J.Str(healed["state"]), Is.EqualTo("idle"));
            Assert.That(J.Int(healed["lieferungen"]), Is.EqualTo(0));
            Assert.That(GooberandoRestaurants.All().Count, Is.EqualTo(3));
            foreach (var r in GooberandoRestaurants.All())
            {
                Assert.That(r.Gerichte.Count, Is.GreaterThanOrEqualTo(4));
                Assert.That(CityGrid.IsStreet(r.StreetR, r.StreetC), Is.True, r.Id + " street tile");
            }
            Assert.That(ShopLogic.GooberandoGerichte().Count, Is.EqualTo(3), "REHWEI marks 3 app dishes");
        }

        [Test]
        public void TheDriverIsAPureFunctionOfTimeOnTheKitchenToHomePath()
        {
            var start = GooberandoRestaurants.StreetTile("moehrenschmiede");
            var home = (CityGrid.HomeStreet[0], CityGrid.HomeStreet[1]);
            var route = GooberandoFahrerSim.RouteWorld(start, home);
            Assert.That(route.Count, Is.GreaterThanOrEqualTo(2));
            Assert.That(route[0].X, Is.EqualTo(CityGrid.World(start.r, start.c).x).Within(1e-9));
            var last = CityGrid.World(home.Item1, home.Item2);
            Assert.That(route[route.Count - 1].X, Is.EqualTo(last.x).Within(1e-9));
            Assert.That(route[route.Count - 1].Z, Is.EqualTo(last.z).Within(1e-9));

            var fahrzeitMs = (long)(GooberandoFahrerSim.FahrzeitS(route) * 1000.0);
            Assert.That(fahrzeitMs, Is.GreaterThan(0));
            var fertig = Now + fahrzeitMs + 120_000;
            var probe = Now + 200_000;
            var a = GooberandoFahrerSim.At(route, Now, fertig, probe);
            GooberandoFahrerSim.At(route, Now, fertig, Now + 250_000);
            var b = GooberandoFahrerSim.At(route, Now, fertig, probe);
            Assert.That(a.X, Is.EqualTo(b.X).Within(1e-9), "same time = same position");
            Assert.That(a.Fortschritt, Is.EqualTo(b.Fortschritt).Within(1e-12));
            Assert.That(a.Phase, Is.EqualTo(b.Phase));

            var los = GooberandoFahrerSim.AbfahrtMs(Now, fertig, route);
            Assert.That(los, Is.EqualTo(fertig - fahrzeitMs));
            Assert.That(GooberandoFahrerSim.At(route, Now, fertig, los - 1000).Phase, Is.EqualTo(GooberandoFahrerSim.PhaseKueche));
            var frueh = GooberandoFahrerSim.At(route, Now, fertig, los + fahrzeitMs / 4);
            var spaet = GooberandoFahrerSim.At(route, Now, fertig, los + fahrzeitMs / 2);
            Assert.That(frueh.Phase, Is.EqualTo(GooberandoFahrerSim.PhaseUnterwegs));
            Assert.That(frueh.Fortschritt, Is.LessThan(spaet.Fortschritt));
            var da = GooberandoFahrerSim.At(route, Now, fertig, fertig);
            Assert.That(da.Phase, Is.EqualTo(GooberandoFahrerSim.PhaseDa));
            Assert.That(da.X, Is.EqualTo(route[route.Count - 1].X).Within(1e-9));
        }

        [Test]
        public void DepartureNeverPrecedesTheOrder()
        {
            var start = GooberandoRestaurants.StreetTile("pasta_hoppel");
            var home = (CityGrid.HomeStreet[0], CityGrid.HomeStreet[1]);
            var route = GooberandoFahrerSim.RouteWorld(start, home);
            Assert.That(GooberandoFahrerSim.AbfahrtMs(Now, Now + 1000, route), Is.EqualTo(Now));
            Assert.That(GooberandoFahrerSim.At(route, Now, Now + 1000, Now + 1000).Phase, Is.EqualTo(GooberandoFahrerSim.PhaseDa));
        }

        [Test]
        public void BookingSpendsCoinsAndCatchUpCreditsFood()
        {
            var store = new GameStateStore { Manager = { Io = new MemorySaveIo() } };
            store.Clock.Pin(Now);
            store.Initialize("user/save_v5.json", 0);
            store.Update(s => J.EnsureDict(s, "economy")["coins"] = 100);
            Assert.That(GooberandoLogic.Buchen(store, Now, 10, "pizza", 30, "pasta_hoppel"), Is.True);
            Assert.That(ShopLogic.Coins(store), Is.EqualTo(67));
            Assert.That(J.Str(GooberandoLogic.SliceOf(store.State)["state"]), Is.EqualTo("bestellt"));
            store.Clock.Pin(Now + 400_000);
            var caught = GooberandoLogic.CatchUp(store, store.Clock.NowMs());
            Assert.That(caught.Events.Select(e => e.Typ).ToArray(), Is.EqualTo(new[] { "vor_der_tuer", "abgestellt" }));
            Assert.That(J.Int(J.Path(store.State, "inventory", "food", "pizza")), Is.EqualTo(1), "left-at-door still feeds");
        }
    }
}
