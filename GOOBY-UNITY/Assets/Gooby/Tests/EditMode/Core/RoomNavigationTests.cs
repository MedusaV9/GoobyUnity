using System.Collections.Generic;
using Gooby.Core.Home;
using Gooby.Core.Save;
using Gooby.Core.State;
using NUnit.Framework;

namespace Gooby.Tests.Core
{
    /// <summary>Walking through doors: zone lookup, arrival points, the closed door graph of rooms.json, current room.</summary>
    public class RoomNavigationTests
    {
        private const long NowMs = 1_768_478_400_000L;

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
            store.Clock.Pin(NowMs);
            store.Initialize("user/save_v5.json", 0);
            HomeState.EnsureInitialized(store);
            return store;
        }

        [Test]
        public void DoorGraphIsClosedAndInsideTheGrids()
        {
            Assert.That(RoomNavigation.ValidateGraph(), Is.Empty);
        }

        [Test]
        public void DoorAtFindsTheTwoByTwoZoneOnly()
        {
            var living = RoomDefs.Get("living");
            var kitchenDoor = RoomDefs.GetDoor("living", "living_kueche"); // N wall, offset 2
            Assert.That(RoomNavigation.DoorAt(living, new Cell(2, 0)), Is.SameAs(kitchenDoor));
            Assert.That(RoomNavigation.DoorAt(living, new Cell(3, 1)), Is.SameAs(kitchenDoor));
            Assert.That(RoomNavigation.DoorAt(living, new Cell(4, 0)), Is.Null);
            Assert.That(RoomNavigation.DoorAt(living, new Cell(2, 2)), Is.Null);
            Assert.That(RoomNavigation.DoorAt(living, new Cell(5, 5)), Is.Null);
            var gardenDoor = RoomDefs.GetDoor("living", "living_garten"); // E wall, offset 5
            Assert.That(RoomNavigation.DoorAt(living, new Cell(11, 5)), Is.SameAs(gardenDoor));
            Assert.That(RoomNavigation.DoorAt(living, new Cell(10, 6)), Is.SameAs(gardenDoor));
        }

        [Test]
        public void CounterpartLeadsBackThroughTheMatchingDoor()
        {
            var door = RoomDefs.GetDoor("living", "living_kueche");
            var back = RoomNavigation.Counterpart(door);
            Assert.That(back.Id, Is.EqualTo("kueche_living"));
            Assert.That(back.To, Is.EqualTo("living"));
            Assert.That(RoomNavigation.RoomOf(back).Id, Is.EqualTo("kitchen"));
        }

        [Test]
        public void ArrivalPointLiesJustInsideTheDoorZoneOnEveryWall()
        {
            var kitchen = RoomDefs.Get("kitchen");
            var west = RoomDefs.GetDoor("kitchen", "kueche_living"); // W, offset 3 → zone x 0..1, y 3..4
            var (wx, wy) = RoomNavigation.ArrivalPoint(kitchen, west);
            Assert.That(wx, Is.EqualTo(2.5));
            Assert.That(wy, Is.EqualTo(4.0));
            Assert.That(RoomNavigation.DoorAt(kitchen, GridData.CellOf(wx * GridData.CellSize, wy * GridData.CellSize)), Is.Null, "arrival is outside the zone");

            var bedroom = RoomDefs.Get("bedroom");
            var north = RoomDefs.GetDoor("bedroom", "schlafzimmer_bad"); // N, offset 6
            var (nx, ny) = RoomNavigation.ArrivalPoint(bedroom, north);
            Assert.That(nx, Is.EqualTo(7.0));
            Assert.That(ny, Is.EqualTo(2.5));
            var east = RoomDefs.GetDoor("bedroom", "schlafzimmer_living"); // E, offset 3 → zone x 8..9
            var (ex, ey) = RoomNavigation.ArrivalPoint(bedroom, east);
            Assert.That(ex, Is.EqualTo(bedroom.GridW - 2.5));
            Assert.That(ey, Is.EqualTo(4.0));
        }

        [Test]
        public void FreeArrivalStepsAsideWhenFurnitureBlocksTheDoorstep()
        {
            var kitchen = RoomDefs.Get("kitchen");
            var door = RoomDefs.GetDoor("kitchen", "kueche_living"); // arrival (2.5, 4.0) → cell (2,4)
            var zone = new List<Cell>();
            foreach (var (x, y) in RoomDefs.BlockedCells(kitchen)) zone.Add(new Cell(x, y));
            var grid = new GridData(kitchen.GridW, kitchen.GridH, zone);
            Assert.That(RoomNavigation.FreeArrival(kitchen, door, grid), Is.EqualTo((2.5, 4.0)), "free doorstep is used as is");

            var bed = new FurnitureDef { Id = "bett", FootprintW = 1, FootprintH = 1, Layer = GridLayer.Floor, BlocksMovement = true };
            Assert.That(grid.Place(bed, new Cell(2, 4), 0, "b1").Ok, Is.True);
            var (fx, fy) = RoomNavigation.FreeArrival(kitchen, door, grid);
            var cell = new Cell((int)fx, (int)fy);
            Assert.That(cell, Is.Not.EqualTo(new Cell(2, 4)));
            Assert.That(grid.Walkable(cell), Is.True);
            Assert.That(RoomNavigation.DoorAt(kitchen, cell), Is.Null, "never inside a door zone");
            Assert.That(System.Math.Abs(cell.X - 2) + System.Math.Abs(cell.Y - 4), Is.EqualTo(1), "the nearest neighbour wins");

            var rug = new FurnitureDef { Id = "teppich", FootprintW = 1, FootprintH = 1, Layer = GridLayer.Rug, BlocksMovement = false };
            var open = new GridData(kitchen.GridW, kitchen.GridH, zone);
            Assert.That(open.Place(rug, new Cell(2, 4), 0, "r1").Ok, Is.True);
            Assert.That(RoomNavigation.FreeArrival(kitchen, door, open), Is.EqualTo((2.5, 4.0)), "rugs do not block");
        }

        [Test]
        public void NearestFreeKeepsAFreePointAndStepsOffFurniture()
        {
            var living = RoomDefs.Get("living");
            var grid = new GridData(living.GridW, living.GridH, new List<Cell>());
            Assert.That(RoomNavigation.NearestFree(living, grid, 4.3, 6.7), Is.EqualTo((4.3, 6.7)), "a free spot stays exactly where it is");
            var sofa = new FurnitureDef { Id = "sofa", FootprintW = 2, FootprintH = 1, Layer = GridLayer.Floor, BlocksMovement = true };
            Assert.That(grid.Place(sofa, new Cell(4, 6), 0, "s1").Ok, Is.True);
            var (x, y) = RoomNavigation.NearestFree(living, grid, 4.3, 6.7);
            var cell = new Cell((int)x, (int)y);
            Assert.That(grid.Walkable(cell), Is.True, "never on the sofa");
            Assert.That(System.Math.Abs(cell.X - 4) + System.Math.Abs(cell.Y - 6), Is.EqualTo(1), "the next free cell");
        }

        [Test]
        public void CurrentRoomPersistsAndFallsBackToTheLivingRoom()
        {
            var gs = Store();
            Assert.That(RoomNavigation.CurrentRoom(gs), Is.EqualTo("living"));
            RoomNavigation.SetCurrentRoom(gs, "kitchen");
            Assert.That(RoomNavigation.CurrentRoom(gs), Is.EqualTo("kitchen"));
            Assert.That(gs.State["home"]["currentRoom"].ToString(), Is.EqualTo("kitchen"));
            RoomNavigation.SetCurrentRoom(gs, "attic"); // unknown room: ignored
            Assert.That(RoomNavigation.CurrentRoom(gs), Is.EqualTo("kitchen"));
            gs.Update(s => s["home"]["currentRoom"] = "hall"); // known but locked
            Assert.That(RoomNavigation.CurrentRoom(gs), Is.EqualTo("living"));
        }

        [Test]
        public void CanPassNeedsAnUnlockedTargetRoom()
        {
            var gs = Store();
            Assert.That(RoomNavigation.CanPass(gs, RoomDefs.GetDoor("living", "living_kueche")), Is.True);
            Assert.That(RoomNavigation.CanPass(gs, RoomDefs.GetDoor("living", "living_garten")), Is.True);
            Assert.That(RoomNavigation.CanPass(gs, null), Is.False);
            gs.Update(s => ((Newtonsoft.Json.Linq.JArray)s["home"]["unlockedRooms"]).Clear());
            Assert.That(RoomNavigation.CanPass(gs, RoomDefs.GetDoor("living", "living_kueche")), Is.False);
            Assert.That(RoomNavigation.CanPass(gs, RoomDefs.GetDoor("living", "living_garten")), Is.True, "the garden is always open");
        }
    }
}
