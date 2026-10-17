using System.Collections.Generic;
using System.Linq;
using Gooby.Core.Home;
using Gooby.Core.Json;
using Gooby.Core.Save;
using Gooby.Core.State;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Gooby.Tests.Core
{
    /// <summary>Port of tests/unit/test_home_grid.gd + test_home_state.gd.</summary>
    public class HomeGridTests
    {
        private const long NowMs = 1_768_478_400_000L;

        private static FurnitureDef Def(string id, int w, int h, GridLayer layer, bool surface = false, bool? blocks = null, int wallSize = 1) =>
            new FurnitureDef { Id = id, FootprintW = w, FootprintH = h, Layer = layer, Surface = surface, BlocksMovement = blocks ?? layer == GridLayer.Floor, WallSize = wallSize };

        [SetUp]
        public void Setup()
        {
            Fixtures.InstallContentSource();
            HomeState.RegisterSlice();
            RoomDefs.ResetCache();
            FurnitureCatalog.ResetCache();
        }

        [Test]
        public void RotatedFootprintAndCells()
        {
            Assert.That(GridData.RotatedFootprint(4, 2, 0), Is.EqualTo((4, 2)));
            Assert.That(GridData.RotatedFootprint(4, 2, 1), Is.EqualTo((2, 4)));
            Assert.That(GridData.RotatedFootprint(4, 2, 3), Is.EqualTo((2, 4)));
            Assert.That(GridData.CellsFor(new Cell(2, 3), 2, 1, 1), Is.EqualTo(new[] { new Cell(2, 3), new Cell(2, 4) }));
        }

        [Test]
        public void PlacementAndCollision()
        {
            var grid = new GridData(6, 6);
            var sofa = Def("sofa", 4, 2, GridLayer.Floor);
            Assert.That(grid.Place(sofa, new Cell(0, 0), 0, "a").Ok, Is.True);
            var second = grid.Place(sofa, new Cell(2, 1), 0, "b");
            Assert.That(second.Ok, Is.False);
            Assert.That(second.Reason, Is.EqualTo(GridData.ReasonOccupied));
            Assert.That(grid.CanPlace(sofa, new Cell(4, 0), 0).Reason, Is.EqualTo(GridData.ReasonOob));
            Assert.That(grid.ItemAt(new Cell(3, 1), GridLayer.Floor), Is.EqualTo("a"));
            Assert.That(grid.ItemAt(new Cell(3, 3), GridLayer.Floor), Is.EqualTo(""));
            Assert.That(grid.Place(null, new Cell(0, 0), 0, "x").Reason, Is.EqualTo(GridData.ReasonUnknownItem));
            var tisch = Def("tisch", 3, 2, GridLayer.Floor);
            var g2 = new GridData(6, 6);
            Assert.That(g2.Place(tisch, new Cell(0, 0), 1, "t").Ok, Is.True);
            Assert.That(g2.ItemAt(new Cell(1, 2), GridLayer.Floor), Is.EqualTo("t"));
            Assert.That(g2.ItemAt(new Cell(2, 0), GridLayer.Floor), Is.EqualTo(""));
        }

        [Test]
        public void DoorZoneBlocksBuildingButNotWalking()
        {
            var zone = new[] { new Cell(2, 0), new Cell(3, 0), new Cell(2, 1), new Cell(3, 1) };
            var grid = new GridData(6, 6, zone);
            Assert.That(grid.CanPlace(Def("regal", 2, 1, GridLayer.Floor), new Cell(2, 0), 0).Reason, Is.EqualTo(GridData.ReasonBlocked));
            Assert.That(grid.Walkable(new Cell(2, 0)), Is.True);
        }

        [Test]
        public void SurfaceNeedsCarrierAndRugsStack()
        {
            var grid = new GridData(6, 6);
            var lampe = Def("lampe", 1, 1, GridLayer.Surface);
            Assert.That(grid.CanPlace(lampe, new Cell(1, 1), 0).Reason, Is.EqualTo(GridData.ReasonNeedsSurface));
            Assert.That(grid.Place(Def("tisch", 2, 1, GridLayer.Floor, surface: true), new Cell(1, 1), 0, "t").Ok, Is.True);
            Assert.That(grid.Place(lampe, new Cell(1, 1), 0, "l").Ok, Is.True);
            Assert.That(grid.Place(Def("teppich", 3, 3, GridLayer.Rug), new Cell(0, 0), 0, "r").Ok, Is.True, "rug under furniture");
            Assert.That(grid.ItemAt(new Cell(1, 1), GridLayer.Rug), Is.EqualTo("r"));
        }

        [Test]
        public void WallItemsAndDoorSpans()
        {
            var doors = new Dictionary<string, List<(int, int)>> { ["N"] = new List<(int, int)> { (2, 4) } };
            var grid = new GridData(6, 6, null, doors, new Dictionary<string, string> { ["N"] = "strasse" });
            var bild = Def("bild", 1, 1, GridLayer.Wall, wallSize: 2);
            Assert.That(grid.CanPlaceWall(bild, "N", 3, "").Reason, Is.EqualTo(GridData.ReasonBlocked));
            Assert.That(grid.PlaceWall(bild, "N", 0, "b").Ok, Is.True);
            Assert.That(grid.CanPlaceWall(bild, "N", 1, "").Reason, Is.EqualTo(GridData.ReasonBlocked), "door span wins over occupancy");
            Assert.That(grid.PlaceWall(bild, "S", 0, "s").Ok, Is.True);
            Assert.That(grid.CanPlaceWall(bild, "S", 1, "").Reason, Is.EqualTo(GridData.ReasonOccupied));
            Assert.That(grid.CanPlaceWall(bild, "N", 5, "").Reason, Is.EqualTo(GridData.ReasonOob));
            Assert.That(grid.WallItemAt("N", 1), Is.EqualTo("b"));
            var fenster = Def("fenster", 1, 1, GridLayer.Wall);
            fenster.Exterior = true;
            Assert.That(grid.CanPlaceWall(fenster, "S", 0, "").Reason, Is.EqualTo(GridData.ReasonNeedsExterior));
            Assert.That(grid.CanPlaceWall(fenster, "N", 4, "").Ok, Is.True);
        }

        [Test]
        public void TheRoomsWindowsTakeOnlyAWindowPiece()
        {
            var windows = new Dictionary<string, List<(int, int)>> { ["N"] = new List<(int, int)> { (2, 4) } };
            var grid = new GridData(8, 6, null, null, new Dictionary<string, string> { ["N"] = "strasse" }, windows);
            var bild = Def("bild", 1, 1, GridLayer.Wall, wallSize: 2);
            Assert.That(grid.CanPlaceWall(bild, "N", 1, "").Reason, Is.EqualTo(GridData.ReasonWindow), "a picture would cover the glass");
            Assert.That(grid.CanPlaceWall(bild, "N", 3, "").Reason, Is.EqualTo(GridData.ReasonWindow));
            Assert.That(grid.CanPlaceWall(bild, "N", 4, "").Ok, Is.True, "right beside the window");
            Assert.That(grid.CanPlaceWall(bild, "W", 2, "").Ok, Is.True, "other walls have no window here");
            Assert.That(grid.WindowReplaced("N", 2, 4), Is.False);
            var fenster = Def("fenster", 1, 1, GridLayer.Wall, wallSize: 2);
            fenster.Exterior = true;
            Assert.That(grid.PlaceWall(fenster, "N", 2, "f").Ok, Is.True, "a window piece may replace the room's window");
            Assert.That(grid.WindowReplaced("N", 2, 4), Is.True);

            var room = RoomDefs.Get("living");
            var spans = HomeState.WallWindowSpans(room);
            Assert.That(spans["N"], Is.EqualTo(new List<(int, int)> { (8, 10) }), "rooms.json: the living room's window at 8, two slots");
            var defs = new Dictionary<string, FurnitureDef> { ["bild"] = bild };
            var rows = JArray.Parse(@"[{""uid"":""a"",""item"":""bild"",""at"":[8,0],""wall"":""N""},{""uid"":""b"",""item"":""bild"",""at"":[4,0],""wall"":""N""}]");
            var loaded = GridData.FromSave(rows, defs, room.GridW, room.GridH, null, null, null, spans);
            Assert.That(loaded.Grid.WallItemAt("N", 4), Is.EqualTo("b"));
            Assert.That(loaded.Leftovers.Count, Is.EqualTo(1), "an old picture over the window goes back into the storage");
        }

        [Test]
        public void FromSavePlacesCarriersBeforeSurfaceItems()
        {
            var defs = new Dictionary<string, FurnitureDef>
            {
                ["tisch"] = Def("tisch", 1, 1, GridLayer.Floor, surface: true),
                ["lampe"] = Def("lampe", 1, 1, GridLayer.Surface),
            };
            var grid = new GridData(8, 8);
            Assert.That(grid.Place(defs["tisch"], new Cell(3, 3), 0, "i-000020").Ok, Is.True);
            Assert.That(grid.Place(defs["lampe"], new Cell(3, 3), 0, "i-000010").Ok, Is.True);
            var arr = grid.ToItemsArray();
            Assert.That(J.Str(arr[0]["item"]), Is.EqualTo("lampe"));
            var loaded = GridData.FromSave(arr, defs, 8, 8);
            Assert.That(loaded.Leftovers, Is.Empty);
            Assert.That(JToken.DeepEquals(loaded.Grid.ToItemsArray(), arr), Is.True);
            var floating = JArray.Parse("[{\"uid\":\"i-1\",\"item\":\"lampe\",\"at\":[5,5],\"rot\":0}]");
            Assert.That(GridData.FromSave(floating, defs, 8, 8).Leftovers.Count, Is.EqualTo(1));
            var mixed = JArray.Parse("[{\"uid\":\"i-2\",\"item\":\"lampe\",\"at\":[3,3],\"rot\":0},\"muell\",{\"uid\":\"i-3\",\"item\":\"unbekannt\",\"at\":[0,0],\"rot\":0},{\"uid\":\"i-9\",\"item\":\"tisch\",\"at\":[3,3],\"rot\":0}]");
            var repaired = GridData.FromSave(mixed, defs, 8, 8);
            Assert.That(repaired.Leftovers.Count, Is.EqualTo(2));
            Assert.That(repaired.Grid.ToItemsArray().Count, Is.EqualTo(2));
        }

        [Test]
        public void MoveRemoveAndReachability()
        {
            var grid = new GridData(6, 6);
            grid.Place(Def("sofa", 2, 2, GridLayer.Floor), new Cell(0, 0), 0, "a");
            Assert.That(grid.MoveItem("a", new Cell(1, 0), 1).Ok, Is.True);
            Assert.That(grid.ItemAt(new Cell(1, 1), GridLayer.Floor), Is.EqualTo("a"));
            Assert.That(grid.ItemAt(new Cell(0, 0), GridLayer.Floor), Is.EqualTo(""));
            Assert.That(grid.RemoveItem("a").Uid, Is.EqualTo("a"));
            Assert.That(grid.RemoveItem("nope"), Is.Null);
            var g = new GridData(5, 5);
            Assert.That(g.Place(Def("wand", 1, 5, GridLayer.Floor), new Cell(2, 0), 0, "w").Ok, Is.True);
            Assert.That(g.IsReachable(new Cell(0, 0), new Cell(4, 4)), Is.False);
            Assert.That(g.IsReachable(new Cell(0, 0), new Cell(1, 4)), Is.True);
            Assert.That(g.IsZoneReachable(new Cell(0, 0), new[] { new Cell(4, 4), new Cell(1, 3) }), Is.True);
            g.RemoveItem("w");
            g.Place(Def("lampe", 1, 1, GridLayer.Floor, blocks: false), new Cell(2, 2), 0, "l");
            Assert.That(g.Walkable(new Cell(2, 2)), Is.True);
        }

        [Test]
        public void SerializationRoundTripAndWorldCoords()
        {
            var defs = new Dictionary<string, FurnitureDef> { ["sofa"] = Def("sofa", 2, 2, GridLayer.Floor), ["lampe"] = Def("lampe", 1, 1, GridLayer.Wall) };
            var grid = new GridData(6, 6);
            grid.Place(defs["sofa"], new Cell(1, 1), 3, "i-1");
            grid.PlaceWall(defs["lampe"], "W", 2, "i-2");
            var items = grid.ToItemsArray();
            Assert.That(items.ToString(Newtonsoft.Json.Formatting.None), Is.EqualTo("[{\"uid\":\"i-1\",\"item\":\"sofa\",\"at\":[1,1],\"rot\":3},{\"uid\":\"i-2\",\"item\":\"lampe\",\"at\":[2,0],\"rot\":0,\"wall\":\"W\"}]"));
            var loaded = GridData.FromSave(items, defs, 6, 6);
            Assert.That(loaded.Leftovers, Is.Empty);
            Assert.That(JToken.DeepEquals(loaded.Grid.ToItemsArray(), items), Is.True);
            Assert.That(GridData.WorldCenter(new Cell(0, 0), 1, 1, 0), Is.EqualTo((0.25, 0.0, 0.25)));
            Assert.That(GridData.WorldCenter(new Cell(2, 4), 4, 2, 1), Is.EqualTo((1.5, 0.0, 3.0)));
        }

        [Test]
        public void CatalogLoadsAllItemsWithMandatorySlots()
        {
            var defs = FurnitureCatalog.Defs();
            // 226 Godot pieces + the four album-only rewards (proc:goldfishBowl, goldenWateringCan, toyCity, candyJar).
            Assert.That(defs.Count, Is.EqualTo(230));
            Assert.That(defs["bedSingle"].Pflicht, Is.EqualTo("bett"));
            Assert.That(defs["bedKaro"].Pflicht, Is.EqualTo("bett"));
            Assert.That(defs["bedKaro"].FootprintH, Is.EqualTo(3));
            Assert.That(defs["bedSingle"].FootprintH, Is.EqualTo(3));
            Assert.That(defs["chair"].Lagerwert, Is.EqualTo(1));
            Assert.That(defs.Values.Count(d => d.Layer == GridLayer.Wall), Is.GreaterThan(0));
            var items = JArray.Parse("[{\"uid\":\"a\",\"item\":\"bedSingle\"},{\"uid\":\"b\",\"item\":\"chair\"}]");
            Assert.That(FurnitureCatalog.IsLastOfMandatorySlot(items, "a"), Is.True);
            Assert.That(FurnitureCatalog.IsLastOfMandatorySlot(items, "b"), Is.False);
        }

        private static GameStateStore Store()
        {
            var store = new GameStateStore { Manager = { Io = new MemorySaveIo() } };
            store.Clock.Pin(NowMs);
            store.Initialize("user/save_v5.json", 0);
            return store;
        }

        [Test]
        public void FirstMoveInFillsDefaultLayoutIdempotently()
        {
            var gs = Store();
            HomeState.EnsureInitialized(gs);
            var rooms = (JObject)gs.State["home"]["rooms"];
            Assert.That(rooms.Count, Is.EqualTo(5));
            Assert.That(((JArray)rooms["living"]["items"]).Count, Is.EqualTo(RoomDefs.DefaultLayout("living").Count));
            Assert.That(StorageLogic.CountOf(HomeState.Storage(gs), "bedSingle"), Is.GreaterThanOrEqualTo(1));
            Assert.That(HomeState.IsRoomUnlocked(gs, "garden"), Is.True);
            Assert.That(HomeState.IsRoomUnlocked(gs, "hall"), Is.False);
            var ids = HomeState.Storage(gs).Select(r => J.Str(r["item"])).ToHashSet();
            foreach (var id in new[] {
                "gardenBush", "flowerRed", "flowerWhite", "treeSapling", "gardenBench",
                "pathStonesShort", "gardenLantern", "crateWood",
                "loungeSofa", "tableCoffee", "pottedPlant", "rugRound",
                "bookcaseClosedWide", "lampRoundFloor", "bedSingle", "cabinetBed", "bedKaro" })
                Assert.That(ids, Has.Member(id), id);
            Assert.That(FurnitureCatalog.Def("flowerWhite"), Is.Not.Null);
            var before = ((JArray)rooms["living"]["items"]).Count;
            HomeState.EnsureInitialized(gs);
            Assert.That(((JArray)gs.State["home"]["rooms"]["living"]["items"]).Count, Is.EqualTo(before));
        }

        [Test]
        public void GridLoadSaveRoundTripAndLeftoversGoToStorage()
        {
            var gs = Store();
            HomeState.EnsureInitialized(gs);
            var grid = HomeState.LoadRoomGrid(gs, "living");
            Assert.That(grid.ToItemsArray().Count, Is.EqualTo(HomeState.RoomItems(gs, "living").Count), "every default item reconstructs");
            var uid = HomeState.NextUid(gs);
            Assert.That(grid.Place(FurnitureCatalog.Def("chair"), new Cell(0, 0), 0, uid).Ok, Is.True);
            HomeState.SaveRoomGrid(gs, "living", grid);
            var reloaded = HomeState.LoadRoomGrid(gs, "living");
            Assert.That(reloaded.GetItem(uid).At, Is.EqualTo(new Cell(0, 0)));
            gs.Update(s => s["home"]["rooms"]["garden"] = new JObject { ["items"] = JArray.Parse("[{\"uid\":\"i-9001\",\"item\":\"gibtEsNicht\",\"at\":[0,0],\"rot\":0},{\"uid\":\"i-9002\",\"item\":\"chair\",\"at\":[4,4],\"rot\":0},{\"uid\":\"i-9003\",\"item\":\"chair\",\"at\":[4,4],\"rot\":0}]") });
            var garden = HomeState.LoadRoomGrid(gs, "garden");
            Assert.That(garden.ToItemsArray().Count, Is.EqualTo(1));
            var storage = HomeState.Storage(gs);
            Assert.That(StorageLogic.CountOf(storage, "chair"), Is.EqualTo(1));
            Assert.That(storage.Any(e => J.Str(e["item"]) == StorageLogic.UnknownItem && J.Str(e["origId"]) == "gibtEsNicht"), Is.True);
            Assert.That(HomeState.RoomItems(gs, "garden").Count, Is.EqualTo(1), "healed room persisted");
        }

        [Test]
        public void StorageApiFlagsUidsAndShed()
        {
            var gs = Store();
            HomeState.EnsureInitialized(gs);
            var points = HomeState.StoragePointsUsed(gs);
            Assert.That(HomeState.StoreItem(gs, "chair"), Is.True);
            Assert.That(HomeState.StoragePointsUsed(gs), Is.EqualTo(points + 1));
            Assert.That(HomeState.TakeFromStorage(gs, "chair"), Is.True);
            Assert.That(HomeState.TakeFromStorage(gs, "chair"), Is.False);
            gs.Update(s => s["home"]["storageCapacity"] = HomeState.StoragePointsUsed(gs));
            Assert.That(HomeState.StoreItem(gs, "bathtub"), Is.False);
            Assert.That(HomeState.Flag(gs, HomeState.FlagBedPlaced), Is.False);
            HomeState.SetFlag(gs, HomeState.FlagBedPlaced, true);
            Assert.That(HomeState.Flag(gs, HomeState.FlagBedPlaced), Is.True);
            Assert.That(HomeState.NextUid(gs), Is.Not.EqualTo(HomeState.NextUid(gs)));
            Assert.That(HomeState.UpgradeShed(gs), Is.EqualTo(0), "100 coins cannot afford tier 1 (500)");
            gs.Update(s => s["economy"]["coins"] = 600);
            Assert.That(HomeState.UpgradeShed(gs), Is.EqualTo(1));
            Assert.That(J.Int(gs.State["home"]["storageCapacity"]), Is.EqualTo(150));
            Assert.That(J.Int(gs.State["economy"]["coins"]), Is.EqualTo(100));
        }
    }
}
