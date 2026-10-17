using System.Linq;
using Gooby.Core.Garden;
using Gooby.Core.Home;
using Gooby.Core.Save;
using Gooby.Core.State;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Gooby.Tests.Core
{
    /// <summary>The vegetable beds on the lawn: placement in the furniture grid, blocking, bed lookup, growth status.</summary>
    public class GardenFieldTests
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
        public void FieldSitsInTheSouthEastCornerAndGrowsWithTheGarden()
        {
            var garden = RoomDefs.Get("garden"); // 28×14
            Assert.That(GardenField.Origin(garden, 6, 5), Is.EqualTo((21, 8)));
            Assert.That(GardenField.Origin(garden, 12, 10), Is.EqualTo((15, 3)), "the biggest garden still fits with its margin");
            var gs = Store();
            Assert.That(GardenField.Origin(gs), Is.EqualTo((21, 8)), "a fresh garden is stufe 0 (6×5)");
            var blocked = GardenField.BlockedCells(gs);
            Assert.That(blocked.Count, Is.EqualTo(30));
            Assert.That(blocked, Has.Member((21, 8)).And.Member((26, 12)));
            Assert.That(blocked, Has.No.Member((20, 8)).And.No.Member((27, 8)));
        }

        [Test]
        public void BedsBlockFurnitureButStayWalkableAndDefaultsAvoidThem()
        {
            var gs = Store();
            var grid = HomeState.LoadRoomGrid(gs, "garden");
            var def = FurnitureCatalog.Def("gardenBench");
            Assert.That(def, Is.Not.Null);
            Assert.That(grid.CanPlace(def, new Cell(22, 9), 0).Reason, Is.EqualTo(GridData.ReasonBlocked), "no bench on the soil");
            Assert.That(grid.Walkable(new Cell(22, 9)), Is.True, "Gooby may walk between the beds");
            Assert.That(grid.Items.Count, Is.EqualTo(RoomDefs.DefaultLayout("garden").Count), "no default item lands on the field");
            Assert.That(HomeState.Storage(gs).Any(r => J("treeFat", r)), Is.False, "the fat tree was not pushed into storage");
        }

        private static bool J(string id, JToken row) => Gooby.Core.Json.J.Str(row["item"]) == id;

        [Test]
        public void BedLookupMapsFurnitureCellsToBedsAndBack()
        {
            var gs = Store();
            Assert.That(GardenField.BedAt(gs, new Cell(21, 8)), Is.EqualTo(new Cell(0, 0)));
            Assert.That(GardenField.BedAt(gs, new Cell(26, 12)), Is.EqualTo(new Cell(5, 4)));
            Assert.That(GardenField.BedAt(gs, new Cell(20, 8)), Is.Null);
            Assert.That(GardenField.BedAt(gs, new Cell(21, 13)), Is.Null);
        }

        [Test]
        public void StatusFollowsPlantingWateringAndGrowth()
        {
            var gs = Store();
            var bed = new Cell(1, 1);
            var (stage0, _, watered0, _) = GardenField.Status(GardenState.Grid(gs).Cell(bed), NowS);
            Assert.That(stage0, Is.EqualTo(-1), "empty bed");
            Assert.That(watered0, Is.False);

            Assert.That(GardenState.Pflanzen(gs, bed, "carrot"), Is.True);
            var (stage, stages, watered, ripe) = GardenField.Status(GardenState.Grid(gs).Cell(bed), NowS);
            Assert.That((stage, stages, watered, ripe), Is.EqualTo((0, 3, false, false)));

            Assert.That(GardenState.Giessen(gs, bed, NowS), Is.True);
            Assert.That(GardenField.Status(GardenState.Grid(gs).Cell(bed), NowS).watered, Is.True);
            Assert.That(Gooby.Core.Json.J.Int(Gooby.Core.Json.J.Path(gs.State, "achievements", "counters", "waterings")), Is.EqualTo(1), "the Gieß-Runde counter moved");

            // 4.5 watered hours (the edge bed grows at the 0.85 wind rate) → all three stages → ripe
            GardenState.Tick(gs, NowS);
            GardenState.Tick(gs, NowS + 4.5 * 3600.0);
            var grown = GardenField.Status(GardenState.Grid(gs).Cell(bed), NowS + 4.5 * 3600.0);
            Assert.That(grown.stage, Is.EqualTo(3));
            Assert.That(grown.ripe, Is.True);
            Assert.That(GardenState.Ernten(gs, bed), Is.EqualTo(2), "two carrots");
            Assert.That(GardenField.Status(GardenState.Grid(gs).Cell(bed), NowS).stage, Is.EqualTo(-1), "bed is empty again");
        }
    }
}
