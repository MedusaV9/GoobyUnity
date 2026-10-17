using Gooby.Core.City;
using Gooby.Core.Json;
using Gooby.Core.Life;
using Gooby.Core.Save;
using Gooby.Core.State;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Gooby.Tests.Core
{
    /// <summary>GOOB-1: unlock only after space, low-G hop numbers, synthetic place, Astro-Snack purchase.</summary>
    public class RaumstationTests
    {
        private const long NowMs = 1_768_478_400_000L;

        [SetUp]
        public void Setup()
        {
            Fixtures.InstallContentSource();
            CityMap.Reset();
            DriveModel.Reset();
        }

        [Test]
        public void UnlockNeedsTheSpaceVisitNotTheOtherEight()
        {
            Assert.That(Raumstation.Freigeschaltet(new JObject()), Is.False, "fresh save: no way to GOOB-1");
            var eight = SaveSchema.DefaultState(NowMs);
            var visited = (JObject)eight["vacation"]["visited"];
            foreach (var d in Vacation.Catalog)
                if (d.Id != "space") visited[d.Id] = true;
            Assert.That(Raumstation.Freigeschaltet(eight), Is.False, "8 destinations without space are not enough");
            visited["space"] = true;
            Assert.That(Raumstation.Freigeschaltet(eight), Is.True, "space visited → shuttle free");
        }

        [Test]
        public void LowGHopIsHigherAndSlowerThanTheGroundHop()
        {
            var hop = Raumstation.HopParameter(Raumstation.HopBasisHoehe, Raumstation.HopBasisDauer);
            Assert.That(hop.Hoehe, Is.EqualTo(0.22 * Raumstation.LowGHoeheMult).Within(1e-9));
            Assert.That(hop.Dauer, Is.EqualTo(0.9 * Raumstation.LowGDauerMult).Within(1e-9));
            Assert.That(hop.Hoehe, Is.GreaterThan(0.22));
            Assert.That(hop.Dauer, Is.GreaterThan(0.9));
        }

        [Test]
        public void TheStationIsNotOnTheTownPlanAndPlaceByIdSynthesisesIt()
        {
            Assert.That(DriveModel.Places, Has.No.Member(DriveModel.PlaceById("raumstation")));
            foreach (var p in DriveModel.Places) Assert.That(p.Id, Is.Not.EqualTo("raumstation"));
            Assert.That(CityMap.Get("raumstation"), Is.Null, "deliberately absent from city_map.json");
            var station = DriveModel.PlaceById("raumstation");
            Assert.That(station, Is.Not.Null);
            Assert.That(station.Id, Is.EqualTo("raumstation"));
            Assert.That(station.Dialog, Is.EqualTo("raumstation"));
            Assert.That(station.Typ, Is.EqualTo("station"));
            var airport = DriveModel.PlaceById("flughafen");
            Assert.That(station.X, Is.EqualTo(airport.X).Within(1e-9), "QA parks at the airport kerb");
        }

        [Test]
        public void TheAstroSnackSellsTheSpaceCarrotAtTheAuthoredSurcharge()
        {
            var store = new GameStateStore { Manager = { Io = new MemorySaveIo() } };
            store.Clock.Pin(NowMs);
            store.Initialize("user/save_v5.json", 0);
            store.Update(s => J.EnsureDict(s, "economy")["coins"] = 20);
            Assert.That(Raumstation.MoehrePreis, Is.EqualTo(14));
            Assert.That(Raumstation.KaufeMoehre(store), Is.True);
            Assert.That(ShopLogic.Coins(store), Is.EqualTo(6));
            Assert.That(J.Int(J.Path(store.State, "inventory", "food", Raumstation.MoehreId)), Is.EqualTo(1));
            store.Update(s => J.EnsureDict(s, "economy")["coins"] = 5);
            Assert.That(Raumstation.KaufeMoehre(store), Is.False, "5 coins cannot cover 14");
        }

        [Test]
        public void TheStationDialogTreeWalksAndOpensTheAutomat()
        {
            var tree = DialogRunner.Laden("raumstation", "de");
            Assert.That(tree, Is.Not.Null);
            Assert.That(J.Str(tree["start"]), Is.EqualTo("hallo"));
            var snacks = (JObject)tree["nodes"]["snacks"];
            var effekt = snacks["effekt"] as JArray;
            Assert.That(effekt, Is.Not.Null);
            var found = false;
            foreach (var t in effekt) if (t.ToString() == "laden") found = true;
            Assert.That(found, Is.True, "snacks node opens the Astro-Snack automaton");
            Assert.That(J.Str(snacks["sprecher"]), Is.EqualTo("kosmo"));
        }

        [Test]
        public void CameraGateMatchesFotoModusFrei()
        {
            var state = SaveSchema.DefaultState(NowMs);
            Assert.That(Raumstation.KameraFrei(state), Is.False);
            J.EnsureDict(state, "camera")["owned"] = true;
            Assert.That(Raumstation.KameraFrei(state), Is.True);
        }
    }
}
