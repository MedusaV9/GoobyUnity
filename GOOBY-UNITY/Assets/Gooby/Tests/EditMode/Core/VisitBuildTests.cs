using System.Collections.Generic;
using System.Linq;
using Gooby.Core.Home;
using Gooby.Core.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Gooby.Tests.Core
{
    /// <summary>
    /// Building while a friend visits (visit_logic.gd apply_build_delta): the BUILD_DELTA format of visit_service.gd, the
    /// guest's best-effort application, the door rule when a piece lands on the guest, and host/guest staying in sync.
    /// </summary>
    public class VisitBuildTests
    {
        [SetUp]
        public void Setup()
        {
            Fixtures.InstallContentSource();
            RoomDefs.ResetCache();
            FurnitureCatalog.ResetCache();
        }

        private static GridData Living()
        {
            var living = RoomDefs.Get("living");
            var blocked = RoomDefs.BlockedCells(living).Select(c => new Cell(c.x, c.y));
            return new GridData(living.GridW, living.GridH, blocked);
        }

        private static readonly Cell Nowhere = new Cell(-99, -99);

        private static JObject Place(string item, int x, int y, int rot = 0) => VisitBuild.Delta("living", VisitBuild.OpPlace, item, new Cell(x, y), rot);

        private static JObject Remove(string item, int x, int y) => VisitBuild.Delta("living", VisitBuild.OpRemove, item, new Cell(x, y), 0);

        private static List<string> Layout(GridData grid) =>
            grid.ToItemsArray().Select(t => J.Str(t["item"]) + "@" + J.Int(t["at"][0]) + "," + J.Int(t["at"][1]) + "/" + J.Int(t["rot"])).OrderBy(s => s).ToList();

        [Test]
        public void DeltaCarriesTheFieldsOfVisitService()
        {
            var d = VisitBuild.Delta("kitchen", VisitBuild.OpPlace, "chair", new Cell(3, 4), 1);
            Assert.That(J.Str(d["op"]), Is.EqualTo("place"));
            Assert.That(J.Str(d["item"]), Is.EqualTo("chair"));
            Assert.That(d["cell"].ToObject<int[]>(), Is.EqualTo(new[] { 3, 4 }));
            Assert.That(J.Int(d["rot"]), Is.EqualTo(1));
            Assert.That(J.Str(d["roomId"]), Is.EqualTo("kitchen"));
            Assert.That(VisitBuild.KindStart, Is.EqualTo("BUILD_START"));
            Assert.That(VisitBuild.KindDelta, Is.EqualTo("BUILD_DELTA"));
        }

        [Test]
        public void PlaceAddsThePieceAndRemoveTakesItAway()
        {
            var grid = Living();
            var placed = VisitBuild.Apply(grid, Place("chair", 6, 6), Nowhere);
            Assert.That(placed.Ok, Is.True, placed.Reason);
            Assert.That(placed.Teleport, Is.False);
            Assert.That(placed.Uid, Does.StartWith("visit-"));
            Assert.That(placed.RoomId, Is.EqualTo("living"));
            Assert.That(grid.ItemAt(new Cell(6, 6), GridLayer.Floor), Is.EqualTo(placed.Uid));

            var again = VisitBuild.Apply(grid, Place("chair", 6, 6), Nowhere);
            Assert.That(again.Ok, Is.False, "the cell is taken — refusals are swallowed");
            Assert.That(again.Reason, Is.EqualTo(GridData.ReasonOccupied));

            var removed = VisitBuild.Apply(grid, Remove("chair", 6, 6), Nowhere);
            Assert.That(removed.Ok, Is.True);
            Assert.That(removed.Uid, Is.EqualTo(placed.Uid));
            Assert.That(grid.ItemAt(new Cell(6, 6), GridLayer.Floor), Is.EqualTo(""));
            Assert.That(VisitBuild.Apply(grid, Remove("chair", 6, 6), Nowhere).Reason, Is.EqualTo(VisitBuild.ReasonNotFound));
        }

        [Test]
        public void RemoveTakesTheNamedPieceNotTheOneOnTop()
        {
            var grid = Living();
            Assert.That(VisitBuild.Apply(grid, Place("tableCoffee", 3, 6), Nowhere).Ok, Is.True);
            var lamp = VisitBuild.Apply(grid, Place("lampRoundTable", 3, 6), Nowhere);
            Assert.That(lamp.Ok, Is.True, "a table lamp stands on the coffee table: " + lamp.Reason);

            // the host moved the table away from under the lamp: its delta names the table
            var table = VisitBuild.Apply(grid, Remove("tableCoffee", 3, 6), Nowhere);
            Assert.That(table.Ok, Is.True);
            Assert.That(grid.ItemAt(new Cell(3, 6), GridLayer.Floor), Is.EqualTo(""));
            Assert.That(grid.ItemAt(new Cell(3, 6), GridLayer.Surface), Is.EqualTo(lamp.Uid), "the lamp stays, like on the host");

            Assert.That(VisitBuild.Apply(grid, Remove("chair", 3, 6), Nowhere).Reason, Is.EqualTo(VisitBuild.ReasonNotFound), "a piece that is not there is left alone");
            var unnamed = VisitBuild.Apply(grid, Remove("", 3, 6), Nowhere);
            Assert.That(unnamed.Ok, Is.True, "senders without an item id get the top-most piece (visit_logic.gd)");
            Assert.That(unnamed.Uid, Is.EqualTo(lamp.Uid));
        }

        [Test]
        public void APieceOverTheGuestSendsItToTheFirstDoor()
        {
            var grid = Living();
            var guest = new Cell(7, 6);
            var miss = VisitBuild.Apply(grid, Place("chair", 4, 6), guest);
            Assert.That(miss.Ok && !miss.Teleport, Is.True, "next to the guest: nobody moves");
            var hit = VisitBuild.Apply(grid, Place("tableCoffee", 6, 6), guest);
            Assert.That(hit.Ok, Is.True, hit.Reason);
            Assert.That(hit.Teleport, Is.True, "the 2×1 table covers the guest's cell");
            var turned = VisitBuild.Apply(grid, Place("bookcaseClosedWide", 9, 2, 1), new Cell(9, 4));
            Assert.That(turned.Ok && turned.Teleport, Is.True, "the turned 3×1 shelf runs down over the guest: " + turned.Reason);

            var living = RoomDefs.Get("living");
            var (sx, sy) = VisitBuild.DoorSpot(living, grid);
            var spot = new Cell((int)System.Math.Floor(sx), (int)System.Math.Floor(sy));
            Assert.That(grid.Walkable(spot), Is.True, "the door spot is free floor");
            Assert.That(RoomNavigation.DoorAt(living, spot), Is.Null, "outside every door zone");
            Assert.That((sx, sy), Is.EqualTo(RoomNavigation.FreeArrival(living, living.Doors[0], grid)), "the room's first door, as in visit_room_view.gd");
        }

        [Test]
        public void RefusedDeltasChangeNothing()
        {
            var grid = Living();
            Assert.That(VisitBuild.Apply(grid, Place("nonsense", 2, 2), Nowhere).Reason, Is.EqualTo(GridData.ReasonUnknownItem));
            Assert.That(VisitBuild.Apply(grid, Place("lampWall", 2, 2), Nowhere).Reason, Is.EqualTo(GridData.ReasonUnknownItem), "wall pieces have no floor cell");
            Assert.That(VisitBuild.Apply(grid, new JObject { ["op"] = "place", ["item"] = "chair" }, Nowhere).Reason, Is.EqualTo(VisitBuild.ReasonBadDelta));
            Assert.That(VisitBuild.Apply(grid, new JObject { ["op"] = "explode", ["cell"] = new JArray(1, 1) }, Nowhere).Reason, Is.EqualTo(VisitBuild.ReasonBadDelta));
            Assert.That(VisitBuild.Apply(null, Place("chair", 2, 2), Nowhere).Reason, Is.EqualTo(VisitBuild.ReasonBadDelta));
            Assert.That(VisitBuild.Apply(grid, null, Nowhere).Reason, Is.EqualTo(VisitBuild.ReasonBadDelta));
            Assert.That(VisitBuild.Apply(grid, Place("chair", 40, 2), Nowhere).Reason, Is.EqualTo(GridData.ReasonOob));
            Assert.That(VisitBuild.Apply(grid, Place("plantSmall1", 5, 5), Nowhere).Reason, Is.EqualTo(GridData.ReasonNeedsSurface));
            Assert.That(grid.Items, Is.Empty);
        }

        [Test]
        public void TheGuestsCopyFollowsEveryHostChange()
        {
            // host and guest start from the same snapshot; the host's build steps travel as deltas (BuildSession order)
            var host = Living();
            var guest = Living();
            var deltas = new List<JObject>();
            var n = 0;
            void HostPlace(string item, int x, int y, int rot = 0)
            {
                Assert.That(host.Place(FurnitureCatalog.Def(item), new Cell(x, y), rot, "h" + n++).Ok, Is.True, item);
                deltas.Add(Place(item, x, y, rot));
            }
            void HostRemove(string uid)
            {
                var it = host.RemoveItem(uid);
                deltas.Add(Remove(it.Def.Id, it.At.X, it.At.Y));
            }
            HostPlace("chair", 6, 6);                   // h0
            HostPlace("tableCoffee", 3, 6);             // h1
            HostPlace("lampRoundTable", 3, 6);          // h2 on the table
            HostPlace("rugRound", 5, 3);                // h3 under everything
            HostRemove("h1"); HostPlace("tableCoffee", 8, 7);      // move the table (the lamp stays behind)
            HostRemove("h0"); HostPlace("chair", 6, 6, 1);         // rotate the chair
            HostPlace("pottedPlant", 6, 4);                        // on the rug
            HostRemove("h3");                                      // the rug goes, the plant on it stays

            foreach (var d in deltas) Assert.That(VisitBuild.Apply(guest, d, Nowhere).Ok, Is.True, d.ToString(Newtonsoft.Json.Formatting.None));
            Assert.That(Layout(guest), Is.EqualTo(Layout(host)));
            Assert.That(Layout(guest), Has.Member("lampRoundTable@3,6/0"));
            Assert.That(Layout(guest), Has.Member("chair@6,6/1"));
            Assert.That(Layout(guest), Has.No.Member("rugRound@5,3/0"));
        }
    }
}
