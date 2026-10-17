using System.Linq;
using Gooby.Core.Garden;
using Gooby.Core.Home;
using Gooby.Core.Json;
using Gooby.Core.Save;
using Gooby.Core.State;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Gooby.Tests.Core
{
    /// <summary>Garden structures for coins: prices, placement + door, effects on planting, tree wood, shed, expansion.</summary>
    public class GardenWorldTests
    {
        private const long NowMs = 1_768_478_400_000L;
        private const double NowS = 1_768_478_400.0;

        [SetUp]
        public void Setup()
        {
            Fixtures.InstallContentSource();
            HomeState.RegisterSlice();
            RoomDefs.ResetCache();
            FurnitureCatalog.ResetCache();
            GardenCrops.ResetCache();
            GardenWorld.ResetCache();
        }

        private static GameStateStore Store(int coins)
        {
            var store = new GameStateStore { Manager = { Io = new MemorySaveIo() } };
            store.Clock.Pin(NowMs);
            store.Initialize("user/save_v5.json", 0);
            HomeState.EnsureInitialized(store);
            store.Update(s => J.EnsureDict(s, "economy")["coins"] = coins);
            return store;
        }

        private static int Coins(GameStateStore gs) => J.Int(J.Path(gs.State, "economy", "coins"));

        [Test]
        public void BaumarktPricesComeFromMaterialsJson()
        {
            Assert.That(GardenWorld.Preis("gewaechshaus"), Is.EqualTo(1800));
            Assert.That(GardenWorld.Preis("sprinkler"), Is.EqualTo(1200));
            Assert.That(GardenWorld.Preis("baum"), Is.EqualTo(250));
            Assert.That(GardenWorld.Preis("werkstatt"), Is.EqualTo(2500));
            Assert.That(GardenWorld.Preis("shed"), Is.EqualTo(0), "the shed is priced by ShedLogic");
            Assert.That(GardenWorld.Kaufbar, Is.EquivalentTo(new[] { "werkstatt", "gewaechshaus", "sprinkler", "baum" }));
        }

        [Test]
        public void FenceEdgesTakeFenceWoodFromTheStorageAndSurviveTheSave()
        {
            // port of test_home_garden.gd: no parts → no fence; direction must be E or S; parts are consumed per cell
            var gs = Store(100);
            Assert.That(GardenWorld.ZaunSetzen(gs, new Cell(0, 0), "E", 2).Reason, Is.EqualTo(GardenWorld.ReasonKeineZaunteile), "keine Zaunteile im Lager");
            for (var i = 0; i < 5; i++) Assert.That(HomeState.StoreItem(gs, GardenWorld.ZaunItem), Is.True);
            Assert.That(GardenWorld.Zaunteile(gs), Is.EqualTo(5));
            Assert.That(GardenWorld.ZaunSetzen(gs, new Cell(0, 0), "X", 2).Reason, Is.EqualTo(GardenWorld.ReasonRichtung), "Richtung muss E oder S sein");
            Assert.That(GardenWorld.ZaunSetzen(gs, new Cell(40, 0), "E", 1).Reason, Is.EqualTo(GardenGrid.ReasonOob));
            Assert.That(GardenWorld.ZaunSetzen(gs, new Cell(0, 0), "E", 2).Ok, Is.True);
            Assert.That(GardenWorld.ZaunSetzen(gs, new Cell(3, 1), "S", 3).Ok, Is.True);
            Assert.That(GardenWorld.Zaunteile(gs), Is.EqualTo(0), "five parts fenced five cells");
            Assert.That(GardenWorld.ZaunSetzen(gs, new Cell(1, 1), "E", 1).Reason, Is.EqualTo(GardenWorld.ReasonKeineZaunteile));
            var grid = GardenState.Grid(gs);
            Assert.That(grid.Edges.Count, Is.EqualTo(2));
            Assert.That(grid.Edges[0].From, Is.EqualTo(new Cell(0, 0)));
            Assert.That(grid.Edges[0].Dir, Is.EqualTo("E"));
            Assert.That(grid.Edges[0].Len, Is.EqualTo(2));
            Assert.That(grid.Edges[1].Dir, Is.EqualTo("S"));
            Assert.That(grid.Edges[1].Len, Is.EqualTo(3));
            Assert.That(grid.Edges[1].Fence, Is.EqualTo("fence_wood"));
            // the save carries the edges (home.garden.edges) in the Godot format
            var saved = J.Path(gs.State, "home", "garden", "edges") as JArray;
            Assert.That(saved, Is.Not.Null);
            Assert.That(saved.Count, Is.EqualTo(2));
            Assert.That(J.Str(saved[1]["dir"]), Is.EqualTo("S"));
            Assert.That(J.Int(saved[1]["len"]), Is.EqualTo(3));
        }

        [Test]
        public void BuildingSpendsCoinsAndRefusesWhenPoorOrBlocked()
        {
            var gs = Store(2000);
            Assert.That(GardenWorld.Bauen(gs, "sprinkler", new Cell(2, 2)).Ok, Is.True);
            Assert.That(Coins(gs), Is.EqualTo(800));
            Assert.That(GardenState.Grid(gs).StructureAt(new Cell(2, 2))?.Kind, Is.EqualTo("sprinkler"));
            Assert.That(GardenWorld.Bauen(gs, "sprinkler", new Cell(2, 2)).Reason, Is.EqualTo(GardenGrid.ReasonOccupied));
            Assert.That(GardenWorld.Bauen(gs, "gewaechshaus", new Cell(0, 0)).Reason, Is.EqualTo(GardenWorld.ReasonZuTeuer), "800 coins do not buy a 1800 greenhouse");
            Assert.That(GardenWorld.Bauen(gs, "baum", new Cell(9, 9)).Reason, Is.EqualTo(GardenGrid.ReasonOob), "outside the 6×5 field");
            Assert.That(GardenWorld.Bauen(gs, "garage", new Cell(0, 0)).Reason, Is.EqualTo(GardenGrid.ReasonUnknown), "not for sale here");
            Assert.That(Coins(gs), Is.EqualTo(800), "refusals cost nothing");
        }

        [Test]
        public void GreenhouseGetsADoorAndGrowsExoticCrops()
        {
            var gs = Store(5000);
            Assert.That(GardenState.Pflanzen(gs, new Cell(0, 0), "ananas"), Is.False, "exotics need a greenhouse");
            var check = GardenWorld.Bauen(gs, "gewaechshaus", new Cell(0, 0));
            Assert.That(check.Ok, Is.True, check.Reason);
            var grid = GardenState.Grid(gs);
            var gh = grid.StructuresOfKind("gewaechshaus").Single();
            Assert.That(gh.Door.HasValue, Is.True, "a door cell was picked automatically");
            var inside = grid.GreenhouseCells();
            Assert.That(inside.Count, Is.EqualTo(5), "2×3 footprint minus the door");
            Assert.That(inside, Has.No.Member(gh.Door.Value));
            var bed = inside.First();
            Assert.That(GardenState.Pflanzen(gs, bed, "ananas"), Is.True, "pineapple grows under glass");
            Assert.That(GardenCrops.Plantable(imGewaechshaus: true).Any(c => c.Exot), Is.True);
            Assert.That(GardenWorld.Pruefen(gs, "sprinkler", gh.Door.Value).Reason, Is.EqualTo(GardenGrid.ReasonOccupied), "the door cell belongs to the greenhouse");
        }

        [Test]
        public void SprinklerWatersItsNeighboursForGrowth()
        {
            var gs = Store(5000);
            Assert.That(GardenWorld.Bauen(gs, "sprinkler", new Cell(2, 2)).Ok, Is.True);
            var grid = GardenState.Grid(gs);
            Assert.That(grid.SprinklerCells(), Has.Member(new Cell(3, 3)).And.Member(new Cell(1, 1)).And.No.Member(new Cell(4, 4)));
            Assert.That(GardenState.Pflanzen(gs, new Cell(3, 3), "carrot"), Is.True);
            var f = GardenGrowth.FaktorenOf(GardenState.Grid(gs), new Cell(3, 3), NowS, regen: false, GardenGrowth.SchattenZellen(GardenState.Grid(gs)));
            Assert.That(f.Wasser, Is.EqualTo(GardenGrowth.WasserNass), "never dry next to the sprinkler");
        }

        [Test]
        public void TreeGivesWoodEveryTwoDays()
        {
            var gs = Store(1000);
            var at = new Cell(5, 4);
            Assert.That(GardenWorld.Bauen(gs, "baum", at).Ok, Is.True);
            Assert.That(Coins(gs), Is.EqualTo(750));
            Assert.That(GardenWorld.BaumReif(gs, at, NowS), Is.True, "a fresh tree (stamp 0) is ripe like in garden_world.gd");
            Assert.That(GardenWorld.BaumErnten(gs, at, NowS), Is.EqualTo(GardenWorld.BaumHolz));
            Assert.That(GardenWorld.Holz(gs), Is.EqualTo(3));
            Assert.That(GardenWorld.BaumErnten(gs, at, NowS + 3600.0), Is.EqualTo(0), "regrowing");
            Assert.That(GardenWorld.BaumRestS(gs, at, NowS + 3600.0), Is.EqualTo(GardenWorld.BaumReifeS - 3600.0).Within(0.5));
            Assert.That(GardenWorld.BaumErnten(gs, at, NowS + GardenWorld.BaumReifeS), Is.EqualTo(3));
            Assert.That(GardenWorld.Holz(gs), Is.EqualTo(6));
            Assert.That(GardenWorld.BaumRestS(gs, new Cell(0, 0), NowS), Is.EqualTo(-1.0), "no tree here");
        }

        [Test]
        public void ShedIsPlacedOnceAndThenUpgradedInPlace()
        {
            var gs = Store(600);
            Assert.That(GardenWorld.ShedBauen(gs, new Cell(0, 0)).Ok, Is.True);
            Assert.That(Coins(gs), Is.EqualTo(100));
            Assert.That(HomeState.ShedStufe(gs), Is.EqualTo(1));
            Assert.That(HomeState.StorageCapacity(gs), Is.EqualTo(150));
            Assert.That(GardenState.Grid(gs).StructuresOfKind("shed").Count, Is.EqualTo(1));
            Assert.That(GardenWorld.ShedBauen(gs, new Cell(3, 3)).Reason, Is.EqualTo(GardenWorld.ReasonZuTeuer), "stage 2 costs 1500");
            gs.Update(s => s["economy"]["coins"] = 2000);
            Assert.That(GardenWorld.ShedBauen(gs, new Cell(3, 3)).Ok, Is.True);
            Assert.That(GardenState.Grid(gs).StructuresOfKind("shed").Count, Is.EqualTo(1), "an upgrade does not place a second shed");
            Assert.That(HomeState.ShedStufe(gs), Is.EqualTo(2));
        }

        [Test]
        public void ExpandingTheFieldCostsCoinsAndMovesTheBedsNorthWest()
        {
            var gs = Store(700);
            Assert.That(GardenWorld.Erweitern(gs).Reason, Is.EqualTo(GardenWorld.ReasonZuTeuer));
            gs.Update(s => s["economy"]["coins"] = 900);
            Assert.That(GardenWorld.Erweitern(gs).Ok, Is.True);
            Assert.That(Coins(gs), Is.EqualTo(100));
            Assert.That(((JArray)GardenState.Slice(gs)["size"]).Select(t => (int)t), Is.EqualTo(new[] { 8, 6 }));
            Assert.That(GardenField.Origin(gs), Is.EqualTo((19, 7)), "the field keeps its south-east anchor");
            Assert.That(HomeState.LoadRoomGrid(gs, "garden").Items.Count, Is.EqualTo(RoomDefs.DefaultLayout("garden").Count), "default items stay clear of the bigger field");
        }
    }
}
