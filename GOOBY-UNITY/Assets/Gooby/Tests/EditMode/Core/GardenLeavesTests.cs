using System.Collections.Generic;
using System.Linq;
using Gooby.Core.Garden;
using Gooby.Core.Home;
using Gooby.Core.Save;
using Gooby.Core.State;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Gooby.Tests.Core
{
    /// <summary>
    /// Leaves (boards 05/08): the day's fallen leaves on the lawn are deterministic and only on free cells, the season
    /// decides how many, each leaf counts once a day (golden five), building over a leaf does not make the lawn endless,
    /// bed care is capped per day, and the exchange trades leaves for garden things only when they fit the storage.
    /// </summary>
    public class GardenLeavesTests
    {
        private const long October2 = 1_790_942_400_000L; // 2026-10-02 12:00 UTC
        private const long OneDay = 86_400_000L;
        private const string Day = "2026-10-02";

        [SetUp]
        public void Setup()
        {
            Fixtures.InstallContentSource();
            HomeState.RegisterSlice();
            GardenLeaves.RegisterSlice();
            RoomDefs.ResetCache();
            FurnitureCatalog.ResetCache();
        }

        private static GameStateStore Store()
        {
            var store = new GameStateStore { Manager = { Io = new MemorySaveIo() } };
            store.Clock.Pin(October2);
            store.Initialize("user/save_v5.json", 0);
            HomeState.EnsureInitialized(store);
            return store;
        }

        private static bool AllFree(int x, int y) => true;

        [Test]
        public void TheDaysLeavesAreDeterministicAndLieOnFreeCells()
        {
            var a = GardenLeaves.Spots(Day, 28, 14, AllFree);
            var b = GardenLeaves.Spots(Day, 28, 14, AllFree);
            Assert.That(a.Select(s => $"{s.Id}@{s.X:F3},{s.Y:F3}"), Is.EqualTo(b.Select(s => $"{s.Id}@{s.X:F3},{s.Y:F3}")));
            Assert.That(a.Count, Is.EqualTo(9), "an October lawn has nine leaves");
            Assert.That(a.Select(s => ((int)s.X, (int)s.Y)).Distinct().Count(), Is.EqualTo(a.Count), "one leaf per cell");
            Assert.That(a.All(s => s.X >= 1 && s.X < 27 && s.Y >= 1 && s.Y < 13), Is.True, "a cell clear of the fence");
            Assert.That(a.Count(s => s.Golden), Is.LessThanOrEqualTo(1));
            var other = GardenLeaves.Spots("2026-10-03", 28, 14, AllFree);
            Assert.That(other.Select(s => s.X), Is.Not.EqualTo(a.Select(s => s.X)), "another day, other leaves");

            // the west half is built over: the leaves move east, the ones already in the east keep their ids
            var east = GardenLeaves.Spots(Day, 28, 14, (x, y) => x >= 14);
            Assert.That(east.Count, Is.EqualTo(9));
            Assert.That(east.All(s => s.X >= 14), Is.True);
            foreach (var s in a.Where(s => s.X >= 14))
                Assert.That(east.Any(e => e.Id == s.Id && e.X == s.X), Is.True, s.Id + " keeps its place");
        }

        [Test]
        public void TheSeasonDecidesHowManyLeavesFall()
        {
            Assert.That(GardenLeaves.CountFor("2026-10-02"), Is.EqualTo(9));
            Assert.That(GardenLeaves.CountFor("2027-01-10"), Is.EqualTo(3));
            Assert.That(GardenLeaves.CountFor("2026-04-20"), Is.EqualTo(5));
            Assert.That(GardenLeaves.CountFor("2026-07-01"), Is.EqualTo(6));
            Assert.That(GardenLeaves.Season("2026-11-30"), Is.EqualTo("herbst"));
            Assert.That(GardenLeaves.Season("2026-12-01"), Is.EqualTo("winter"));
            Assert.That(GardenLeaves.CountFor("kaputt"), Is.EqualTo(3), "a broken date never crashes");
            var golden = Enumerable.Range(1, 30).Count(d => GardenLeaves.Spots($"2026-10-{d:D2}", 28, 14, AllFree).Any(s => s.Golden));
            Assert.That(golden, Is.InRange(8, 22), "about every second October day has a golden leaf");
        }

        [Test]
        public void EachLeafCountsOnceADay()
        {
            var gs = Store();
            Assert.That(GardenLeaves.Count(gs), Is.EqualTo(0));
            var lying = GardenLeaves.Lying(gs, Day, 28, 14, AllFree);
            Assert.That(lying.Count, Is.EqualTo(9));
            var leaf = lying[0];
            Assert.That(GardenLeaves.Pick(gs, Day, leaf), Is.EqualTo(leaf.Value));
            Assert.That(GardenLeaves.Pick(gs, Day, leaf), Is.EqualTo(0), "picked already");
            Assert.That(GardenLeaves.Count(gs), Is.EqualTo(leaf.Value));
            Assert.That(GardenLeaves.Lying(gs, Day, 28, 14, AllFree).Select(s => s.Id), Has.No.Member(leaf.Id));

            var sum = leaf.Value;
            foreach (var s in lying.Skip(1)) sum += GardenLeaves.Pick(gs, Day, s);
            Assert.That(sum, Is.EqualTo(lying.Sum(s => s.Value)));
            Assert.That(GardenLeaves.Count(gs), Is.EqualTo(sum));
            Assert.That(GardenLeaves.Total(gs), Is.EqualTo(sum));
            Assert.That(GardenLeaves.Lying(gs, Day, 28, 14, AllFree), Is.Empty);

            // the next morning the lawn is full again
            gs.Clock.Advance(OneDay);
            Assert.That(GardenLeaves.Lying(gs, "2026-10-03", 28, 14, AllFree).Count, Is.EqualTo(9));
            Assert.That(GardenLeaves.Picked(gs, "2026-10-03"), Is.Empty);
        }

        [Test]
        public void BuildingOverALeafDoesNotMakeTheLawnEndless()
        {
            var gs = Store();
            foreach (var s in GardenLeaves.Lying(gs, Day, 28, 14, AllFree)) GardenLeaves.Pick(gs, Day, s);
            var count = GardenLeaves.Count(gs);
            // furniture now covers the first leaves' cells: later candidates slide in, but the day is used up
            var moved = GardenLeaves.Spots(Day, 28, 14, (x, y) => x >= 20);
            Assert.That(GardenLeaves.Lying(gs, Day, 28, 14, (x, y) => x >= 20), Is.Empty);
            foreach (var s in moved) Assert.That(GardenLeaves.Pick(gs, Day, s), Is.EqualTo(0));
            Assert.That(GardenLeaves.Count(gs), Is.EqualTo(count));
        }

        [Test]
        public void BedCareIsCappedPerDay()
        {
            var gs = Store();
            Assert.That(GardenLeaves.ForCare(gs, "water"), Is.EqualTo(1));
            Assert.That(GardenLeaves.ForCare(gs, "plant"), Is.EqualTo(1));
            Assert.That(GardenLeaves.ForCare(gs, "harvest"), Is.EqualTo(2));
            Assert.That(GardenLeaves.ForCare(gs, "dance"), Is.EqualTo(0));
            var total = 4;
            for (var i = 0; i < 10; i++) total += GardenLeaves.ForCare(gs, "harvest");
            Assert.That(total, Is.EqualTo(GardenLeaves.CareCapPerDay));
            Assert.That(GardenLeaves.Count(gs), Is.EqualTo(GardenLeaves.CareCapPerDay));
            Assert.That(GardenLeaves.ForCare(gs, "water"), Is.EqualTo(0), "capped");
            // the lawn's leaves are not part of the care cap
            var leaf = GardenLeaves.Lying(gs, Day, 28, 14, AllFree)[0];
            Assert.That(GardenLeaves.Pick(gs, Day, leaf), Is.EqualTo(leaf.Value));
            gs.Clock.Advance(OneDay);
            Assert.That(GardenLeaves.ForCare(gs, "water"), Is.EqualTo(1), "a new day");
        }

        [Test]
        public void TheExchangeTradesLeavesForGardenThingsThatFit()
        {
            var gs = Store();
            var offers = GardenLeaves.Offers();
            Assert.That(offers.Take(8).Select(o => o.ItemId), Is.EqualTo(GardenLeaves.OfferItems.Take(8)), "board 05's row first");
            Assert.That(offers.First(o => o.ItemId == "flowerRed").Price, Is.EqualTo(3), "15 coins → 3 leaves");
            Assert.That(offers.First(o => o.ItemId == "pathStonesShort").Price, Is.EqualTo(2), "never under two");
            Assert.That(offers.First(o => o.ItemId == "gardenFountain").Price, Is.EqualTo(104));

            Assert.That(GardenLeaves.Buy(gs, "flowerRed"), Is.EqualTo(GardenLeaves.ResultTooFew));
            Assert.That(GardenLeaves.Buy(gs, "sofaGold"), Is.EqualTo(GardenLeaves.ResultUnknown));
            gs.Update(s => s[GardenLeaves.SliceId]["count"] = 10);
            var before = StorageLogic.CountOf(HomeState.Storage(gs), "flowerRed");
            Assert.That(GardenLeaves.Buy(gs, "flowerRed"), Is.EqualTo(GardenLeaves.ResultOk));
            Assert.That(StorageLogic.CountOf(HomeState.Storage(gs), "flowerRed"), Is.EqualTo(before + 1));
            Assert.That(GardenLeaves.Count(gs), Is.EqualTo(7));

            // a full storage keeps the leaves
            gs.Update(s => s["home"]["storageCapacity"] = HomeState.StoragePointsUsed(gs));
            Assert.That(GardenLeaves.Buy(gs, "flowerRed"), Is.EqualTo(GardenLeaves.ResultStorageFull));
            Assert.That(GardenLeaves.Count(gs), Is.EqualTo(7));
        }

        [Test]
        public void TheSliceIsThereAndRepairsItself()
        {
            var gs = Store();
            Assert.That(gs.State[GardenLeaves.SliceId], Is.InstanceOf<JObject>(), "registered slices exist in a fresh save");
            var fixedUp = GardenLeaves.NormalizeSlice(new JObject { ["count"] = -5, ["picked"] = "b1", ["care"] = "x", ["total"] = 2 });
            Assert.That((long)fixedUp["count"], Is.EqualTo(0));
            Assert.That(fixedUp["picked"], Is.InstanceOf<JArray>());
            Assert.That((int)fixedUp["care"], Is.EqualTo(0));
            var keep = GardenLeaves.NormalizeSlice(new JObject { ["count"] = 40, ["total"] = 12 });
            Assert.That((long)keep["total"], Is.EqualTo(40), "the lifetime total never lags behind the balance");
        }
    }
}
