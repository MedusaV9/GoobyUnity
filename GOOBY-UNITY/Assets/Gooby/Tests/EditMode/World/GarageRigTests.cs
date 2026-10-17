using Gooby.Core.Garden;
using Gooby.Core.Home;
using Gooby.Core.Json;
using Gooby.Core.State;
using Gooby.Unity.World;
using NUnit.Framework;
using UnityEngine;

namespace Gooby.Tests.World
{
    /// <summary>The garage on the bed field: where it stands, the car inside, the roller door rolling and turning round.</summary>
    public class GarageRigTests
    {
        private const long NowMs = 1_768_478_400_000L;
        private static readonly Vector3 GardenOrigin = new Vector3(5f, 0f, -3.5f);

        [SetUp]
        public void Setup()
        {
            Core.Fixtures.InstallContentSource();
            HomeState.RegisterSlice();
            RoomDefs.ResetCache();
            FurnitureCatalog.ResetCache();
            GardenCrops.ResetCache();
            GardenWorld.ResetCache();
        }

        private static GameStateStore StoreWithGarage(Cell at)
        {
            var store = new GameStateStore { Manager = { Io = new Core.MemorySaveIo() } };
            store.Clock.Pin(NowMs);
            store.Initialize("user/save_v5.json", 0);
            HomeState.EnsureInitialized(store);
            store.Update(s => J.EnsureDict(s, "economy")["coins"] = 2000);
            Assert.That(GarageLogic.Kaufen(store, at).Ok, Is.True);
            return store;
        }

        [Test]
        public void TheGarageStandsOnItsCellsDoorSouthWithTheCarParkedInside()
        {
            var gs = StoreWithGarage(new Cell(1, 1));
            var parent = new GameObject("GardenTest");
            try
            {
                var beds = new GardenBeds(parent.transform, GardenOrigin, 14);
                beds.Refresh(gs, NowMs / 1000.0);
                var rig = beds.Garage;
                Assert.That(rig, Is.Not.Null);
                Assert.That(rig.name, Is.EqualTo("Structure:garage"));
                Assert.That(rig.GetComponent<BedMarker>().Bed, Is.EqualTo(new Cell(1, 1)), "a tap resolves to the garage's anchor cell");
                var a = beds.TileCenter(new Cell(1, 1));
                var b = beds.TileCenter(new Cell(2, 3));
                var centre = new Vector3((a.x + b.x) * 0.5f, rig.transform.position.y, (a.z + b.z) * 0.5f);
                Assert.That(Vector3.Distance(rig.transform.position, centre), Is.LessThan(1e-3f), "centred on its 2×3 cells");
                var box = rig.GetComponent<BoxCollider>();
                Assert.That(box.size.x, Is.EqualTo(1.0f).Within(1e-4f));
                Assert.That(box.size.z, Is.EqualTo(1.5f).Within(1e-4f));
                Assert.That(Vector3.Dot(rig.Model.forward, Vector3.back), Is.GreaterThan(0.99f), "the door faces the garden camera in the south");
                Assert.That(rig.Model.localScale.x, Is.EqualTo(GarageRig.ModelScale));
                Assert.That(rig.Door.localPosition, Is.EqualTo(new Vector3(0f, GarageRig.DoorTop, GarageRig.DoorZ)));
                Assert.That(rig.Door.localScale.y, Is.EqualTo(1f), "a fresh garage is closed");
                Assert.That(rig.Car.localPosition.z, Is.EqualTo(GarageRig.CarParkZ).Within(1e-5f));
                Assert.That(rig.Car.localScale.z * GarageRig.HeroCarLength, Is.EqualTo(GarageRig.CarLength).Within(1e-3f), "the start car is 2.2 m in a 2.9 m garage");
                Assert.That(rig.Car.GetComponent<CarPaint>(), Is.Not.Null, "painted like the car in town");
                rig.SetCarRun(1f);
                Assert.That(rig.Car.localPosition.z - GarageRig.HeroCarLength * 0.5f * rig.Car.localScale.z, Is.GreaterThan(GarageRig.DoorZ), "at the end of its run the whole car is out of the door");
            }
            finally
            {
                Object.DestroyImmediate(parent);
            }
        }

        [Test]
        public void TheDoorRollsUpInOneRollAndTurnsRoundMidWay()
        {
            var go = new GameObject("Rig");
            try
            {
                var rig = go.AddComponent<GarageRig>();
                rig.Door = new GameObject("Door").transform;
                rig.Door.SetParent(go.transform, false);
                var reported = 0;
                rig.RollTo(1f, () => reported++);
                rig.Step(0.35f);
                Assert.That(rig.Open, Is.EqualTo(0.5f).Within(1e-3f));
                Assert.That(rig.Door.localScale.y, Is.EqualTo(Mathf.Lerp(1f, GarageRig.DoorRolledScale, 0.5f)).Within(1e-3f));
                Assert.That(rig.Rolling, Is.True);
                rig.RollTo(0f, () => reported += 10);
                rig.Step(0.7f);
                Assert.That(rig.Open, Is.EqualTo(0f).Within(1e-4f), "a tap mid-way sends it back down from where it was");
                Assert.That(reported, Is.EqualTo(10), "only the roll that ran out reports");
                rig.RollTo(1f);
                rig.Step(1f);
                Assert.That(rig.Door.localScale.y, Is.EqualTo(GarageRig.DoorRolledScale).Within(1e-4f), "rolled up, a band stays under the lintel");
                Assert.That(rig.Rolling, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void ARebuildPutsTheDoorWhereItsStateMachineEnds()
        {
            var gs = StoreWithGarage(new Cell(0, 0));
            var parent = new GameObject("GardenTest");
            try
            {
                var beds = new GardenBeds(parent.transform, GardenOrigin, 14) { GarageZustand = GarageLogic.Oeffnet };
                beds.Refresh(gs, NowMs / 1000.0);
                Assert.That(beds.Garage.Open, Is.EqualTo(1f), "a door that was rolling up is up after the rebuild");
                Assert.That(beds.GarageZustand, Is.EqualTo(GarageLogic.Offen));
            }
            finally
            {
                Object.DestroyImmediate(parent);
            }
        }
    }
}
