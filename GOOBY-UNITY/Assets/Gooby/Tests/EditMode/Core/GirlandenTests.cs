using Gooby.Core.Home;
using Gooby.Core.Json;
using Gooby.Core.Save;
using Gooby.Core.State;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Gooby.Tests.Core
{
    /// <summary>Garlands spanned under the ceiling (port of the girlanden tests of test_w13b_*.gd / girlanden_bau.gd's rules).</summary>
    public class GirlandenTests
    {
        [SetUp]
        public void Setup()
        {
            Fixtures.InstallContentSource();
            HomeState.RegisterSlice();
            RoomDefs.ResetCache();
            FurnitureCatalog.ResetCache();
        }

        private static GameStateStore Store()
        {
            var store = new GameStateStore { Manager = { Io = new MemorySaveIo() } };
            store.Clock.Pin(1_791_158_400_000L);
            store.Initialize("user/save_v5.json", 0);
            HomeState.EnsureInitialized(store);
            return store;
        }

        [Test]
        public void TheThreeKindsAreGarlands()
        {
            Assert.That(Girlanden.Ist(Girlanden.Wimpel), Is.True);
            Assert.That(Girlanden.Ist(Girlanden.Lichter), Is.True);
            Assert.That(Girlanden.Ist(Girlanden.Pompons), Is.True);
            Assert.That(Girlanden.Ist("lampionStanding"), Is.False);
            Assert.That(Girlanden.Ist(""), Is.False);
        }

        [Test]
        public void SpanningTakesOneFromTheStorageAndTakingDownPutsItBack()
        {
            var gs = Store();
            Assert.That(Girlanden.Spannen(gs, "living", Girlanden.Wimpel, new Cell(1, 1), new Cell(6, 1), 12, 10), Is.EqualTo(Girlanden.LagerLeer), "none in the storage yet");
            Assert.That(HomeState.StoreItem(gs, Girlanden.Wimpel), Is.True);
            Assert.That(Girlanden.Spannen(gs, "living", Girlanden.Wimpel, new Cell(1, 1), new Cell(6, 1), 12, 10), Is.EqualTo(Girlanden.Gespannt));
            Assert.That(StorageLogic.CountOf(HomeState.Storage(gs), Girlanden.Wimpel), Is.EqualTo(0));
            var liste = Girlanden.Liste(gs, "living");
            Assert.That(liste.Count, Is.EqualTo(1));
            Assert.That(J.Str(liste[0]["typ"]), Is.EqualTo(Girlanden.Wimpel));
            Assert.That(Girlanden.Zelle(liste[0], "zelle_b"), Is.EqualTo(new Cell(6, 1)));
            Assert.That(Girlanden.AnkerBei(liste, new Cell(6, 1)), Is.EqualTo(0));
            Assert.That(Girlanden.AnkerBei(liste, new Cell(3, 1)), Is.EqualTo(-1), "the middle is no anchor");
            Assert.That(Girlanden.Abnehmen(gs, "living", 0), Is.True);
            Assert.That(Girlanden.Liste(gs, "living").Count, Is.EqualTo(0));
            Assert.That(StorageLogic.CountOf(HomeState.Storage(gs), Girlanden.Wimpel), Is.EqualTo(1));
            Assert.That(Girlanden.Abnehmen(gs, "living", 0), Is.False, "nothing left to take down");
        }

        [Test]
        public void SpanningNeedsTwoDifferentCellsInsideARoom()
        {
            var gs = Store();
            HomeState.StoreItem(gs, Girlanden.Pompons);
            Assert.That(Girlanden.Spannen(gs, "living", Girlanden.Pompons, new Cell(2, 2), new Cell(2, 2), 12, 10), Is.EqualTo(Girlanden.Ungueltig), "same cell");
            Assert.That(Girlanden.Spannen(gs, "living", Girlanden.Pompons, new Cell(2, 2), new Cell(12, 2), 12, 10), Is.EqualTo(Girlanden.Ungueltig), "outside the room");
            Assert.That(Girlanden.Spannen(gs, "garden", Girlanden.Pompons, new Cell(2, 2), new Cell(4, 2), 28, 14), Is.EqualTo(Girlanden.Ungueltig), "the lawn has no ceiling");
            Assert.That(Girlanden.Spannen(gs, "living", "lampionStanding", new Cell(2, 2), new Cell(4, 2), 12, 10), Is.EqualTo(Girlanden.Ungueltig), "not a garland");
            Assert.That(StorageLogic.CountOf(HomeState.Storage(gs), Girlanden.Pompons), Is.EqualTo(1), "refusals keep the piece");
        }

        [Test]
        public void LoadingHealsBrokenEntriesAndSendsOldOneCellGarlandsToTheStorage()
        {
            var home = HomeState.NormalizeSlice(JObject.Parse(@"{""rooms"":{""living"":{""items"":[],""girlanden"":[
                {""typ"":""girlande_lichter"",""zelle_a"":[1,1],""zelle_b"":[4,2]},
                {""typ"":"""",""zelle_a"":[1,1],""zelle_b"":[4,2]},
                {""typ"":""girlande_wimpel"",""zelle_a"":[1]},
                ""kaputt""]}}}"));
            var liste = (JArray)home["rooms"]["living"]["girlanden"];
            Assert.That(liste.Count, Is.EqualTo(1), "only the good entry stays");
            Assert.That(J.Str(liste[0]["typ"]), Is.EqualTo(Girlanden.Lichter));

            var gs = Store();
            gs.Update(s => ((JArray)s["home"]["rooms"]["living"]["items"]).Add(new JObject { ["item"] = Girlanden.Wimpel, ["at"] = new JArray(3, 3), ["rot"] = 0, ["uid"] = "i-g1" }));
            var grid = HomeState.LoadRoomGrid(gs, "living");
            Assert.That(grid.GetItem("i-g1"), Is.Null, "a garland is never a grid piece");
            Assert.That(StorageLogic.CountOf(HomeState.Storage(gs), Girlanden.Wimpel), Is.EqualTo(1), "back into the storage to be spanned");
        }
    }
}
