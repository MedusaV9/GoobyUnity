using Gooby.Core.Garden;
using Gooby.Core.Home;
using Gooby.Core.Json;
using Gooby.Core.Save;
using Gooby.Core.State;
using NUnit.Framework;

namespace Gooby.Tests.Core
{
    /// <summary>The garage: one purchase on the field (fail-closed), the roller door's state machine and the drive-off timeline.</summary>
    public class GarageLogicTests
    {
        private const long NowMs = 1_768_478_400_000L;

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
        public void BuyingPlacesTheTwoByThreeGarageAndCharges1200()
        {
            var gs = Store(1500);
            Assert.That(GarageLogic.Footprint, Is.EqualTo((2, 3)));
            Assert.That(GarageLogic.Gebaut(gs), Is.False);
            Assert.That(GarageLogic.Kaufen(gs, new Cell(1, 1)).Ok, Is.True);
            Assert.That(Coins(gs), Is.EqualTo(300));
            Assert.That(GarageLogic.Gebaut(gs), Is.True);
            var s = GarageLogic.Struktur(gs);
            Assert.That(s.Kind, Is.EqualTo("garage"));
            Assert.That(s.At, Is.EqualTo(new Cell(1, 1)));
            Assert.That(GardenState.Grid(gs).StructureAt(new Cell(2, 3))?.Kind, Is.EqualTo("garage"), "the far corner of the 2×3 footprint");
            Assert.That(GardenState.Grid(gs).StructureAt(new Cell(3, 1)), Is.Null);
        }

        [Test]
        public void OneGarageIsPlenty()
        {
            var gs = Store(5000);
            Assert.That(GarageLogic.Kaufen(gs, new Cell(0, 0)).Ok, Is.True);
            var check = GarageLogic.Kaufen(gs, new Cell(3, 0));
            Assert.That(check.Ok, Is.False);
            Assert.That(check.Reason, Is.EqualTo(GarageLogic.ReasonSchonGebaut));
            Assert.That(Coins(gs), Is.EqualTo(3800), "the refusal costs nothing");
            Assert.That(GardenState.Grid(gs).StructuresOfKind("garage").Count, Is.EqualTo(1));
        }

        [Test]
        public void TooPoorOrNoRoomLeavesTheSaveUntouched()
        {
            var poor = Store(1199);
            var check = GarageLogic.Kaufen(poor, new Cell(0, 0));
            Assert.That(check.Reason, Is.EqualTo(GarageLogic.ReasonZuTeuer));
            Assert.That(Coins(poor), Is.EqualTo(1199));
            Assert.That(GarageLogic.Gebaut(poor), Is.False);

            var rich = Store(3000);
            Assert.That(GarageLogic.Kaufen(rich, new Cell(5, 0)).Reason, Is.EqualTo(GardenGrid.ReasonOob), "two cells wide do not fit into the last column of the 6×5 field");
            Assert.That(GardenWorld.Bauen(rich, "sprinkler", new Cell(1, 2)).Ok, Is.True);
            var coins = Coins(rich);
            Assert.That(GarageLogic.Kaufen(rich, new Cell(0, 0)).Reason, Is.EqualTo(GardenGrid.ReasonOccupied));
            Assert.That(Coins(rich), Is.EqualTo(coins));
            Assert.That(GarageLogic.Pruefen(rich, new Cell(3, 0)).Ok, Is.True, "free cells further east");
        }

        [Test]
        public void RollerDoorTurnsRoundMidRoll()
        {
            Assert.That(GarageLogic.Toggle(GarageLogic.Zu), Is.EqualTo(GarageLogic.Oeffnet));
            Assert.That(GarageLogic.Toggle(GarageLogic.Oeffnet), Is.EqualTo(GarageLogic.Schliesst), "a tap while it rolls up sends it down");
            Assert.That(GarageLogic.Toggle(GarageLogic.Schliesst), Is.EqualTo(GarageLogic.Oeffnet));
            Assert.That(GarageLogic.Toggle(GarageLogic.Offen), Is.EqualTo(GarageLogic.Schliesst));
            Assert.That(GarageLogic.Ende(GarageLogic.Oeffnet), Is.EqualTo(GarageLogic.Offen));
            Assert.That(GarageLogic.Ende(GarageLogic.Schliesst), Is.EqualTo(GarageLogic.Zu));
            Assert.That(GarageLogic.Ende(GarageLogic.Offen), Is.EqualTo(GarageLogic.Offen));
            Assert.That(GarageLogic.Ende(GarageLogic.Zu), Is.EqualTo(GarageLogic.Zu));
            Assert.That(GarageLogic.ZielAnteil(GarageLogic.Oeffnet), Is.EqualTo(1.0));
            Assert.That(GarageLogic.ZielAnteil(GarageLogic.Offen), Is.EqualTo(1.0));
            Assert.That(GarageLogic.ZielAnteil(GarageLogic.Schliesst), Is.EqualTo(0.0));
            Assert.That(GarageLogic.ZielAnteil(GarageLogic.Zu), Is.EqualTo(0.0));
            Assert.That(GarageLogic.IstOffen(GarageLogic.Offen), Is.True);
            Assert.That(GarageLogic.IstOffen(GarageLogic.Oeffnet), Is.False, "still rolling");
        }

        [Test]
        public void DriveOffRollsTheDoorFirstThenTheCarAccelerates()
        {
            Assert.That(GarageLogic.AbfahrtDauerS(false), Is.EqualTo(2.5).Within(1e-9));
            var start = GarageLogic.Abfahrt(0.0);
            Assert.That(start.tor, Is.EqualTo(0.0).Within(1e-9));
            Assert.That(start.fahrt, Is.EqualTo(0.0));
            Assert.That(start.fertig, Is.False);
            var half = GarageLogic.Abfahrt(0.35);
            Assert.That(half.tor, Is.EqualTo(0.5).Within(1e-9), "eased: half the time is half the way");
            Assert.That(half.fahrt, Is.EqualTo(0.0), "the car waits for the door");
            var up = GarageLogic.Abfahrt(0.8);
            Assert.That(up.tor, Is.EqualTo(1.0).Within(1e-9));
            Assert.That(up.fahrt, Is.EqualTo(0.0).Within(1e-9));
            var mid = GarageLogic.Abfahrt(0.8 + 0.8);
            Assert.That(mid.fahrt, Is.EqualTo(0.25).Within(1e-9), "ease-in: half the time covers a quarter of the run");
            var end = GarageLogic.Abfahrt(2.5);
            Assert.That(end.fahrt, Is.EqualTo(1.0).Within(1e-9));
            Assert.That(end.fertig, Is.True);
            Assert.That(GarageLogic.Abfahrt(2.4).fertig, Is.False);
            Assert.That(GarageLogic.Abfahrt(0.0, torStart: 1.0).tor, Is.EqualTo(1.0), "an open door stays up");
            Assert.That(GarageLogic.Abfahrt(0.0, reduziert: true), Is.EqualTo((1.0, 1.0, true)), "reduced motion: straight to the city");
        }
    }
}
