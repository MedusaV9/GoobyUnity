using Gooby.Core.Home;
using Gooby.Core.Json;
using Gooby.Core.Save;
using Gooby.Core.State;
using NUnit.Framework;

namespace Gooby.Tests.Core
{
    /// <summary>
    /// Home interactables (port of test_w3d_bad / interactables_host.gd): which furniture does what, the toilet timer,
    /// the shower-peek window, and that washing and brushing give hygiene back and count for the stickers.
    /// </summary>
    public class InteractablesTests
    {
        private const long NowMs = 1_791_158_400_000L;

        [SetUp]
        public void Setup()
        {
            Fixtures.InstallContentSource();
            HomeState.RegisterSlice();
            BadState.RegisterSlice();
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
        public void FurnitureMapsToItsInteraction()
        {
            string K(string id) => Interactables.Kind(FurnitureCatalog.Def(id));
            Assert.That(K("toilet"), Is.EqualTo(Interactables.Klo));
            Assert.That(K("bathtub"), Is.EqualTo(Interactables.Dusche));
            Assert.That(K("shower"), Is.EqualTo(Interactables.Dusche));
            Assert.That(K("bathroomSink"), Is.EqualTo(Interactables.Zahnputz));
            Assert.That(K("bathroomMirror"), Is.EqualTo(Interactables.Spiegel));
            Assert.That(K("bedSingle"), Is.EqualTo(Interactables.Bett));
            Assert.That(K("kitchenFridge"), Is.EqualTo(Interactables.Kuehlschrank));
            Assert.That(K("televisionModern"), Is.EqualTo(Interactables.Fernseher));
            Assert.That(K("radioRetro"), Is.EqualTo(Interactables.Radio));
            Assert.That(K("lampRoundFloor"), Is.EqualTo(Interactables.Licht), "lamps switch");
            Assert.That(K("loungeSofa"), Is.EqualTo(Interactables.Sitzen));
            Assert.That(K("postkartenWand"), Is.EqualTo(Interactables.Postkarten), "the postcard wall opens the archive");
            Assert.That(K("souvenirRegal"), Is.EqualTo(Interactables.Postkarten));
            Assert.That(K("pottedPlant"), Is.Empty);
            Assert.That(Interactables.Kind(null), Is.Empty);
        }

        [Test]
        public void TheToiletNeedFillsInFourHoursAndThePeekWaits45Seconds()
        {
            Assert.That(BadState.KloNeed01(0, NowMs), Is.EqualTo(0.0), "no timer yet");
            Assert.That(BadState.KloNeed01(NowMs, NowMs + BadState.KloFullMs / 2), Is.EqualTo(0.5).Within(1e-9));
            Assert.That(BadState.KloDue(NowMs, NowMs + BadState.KloFullMs - 1), Is.False);
            Assert.That(BadState.KloDue(NowMs, NowMs + BadState.KloFullMs), Is.True);
            Assert.That(BadState.ShowerPeekDue(NowMs, NowMs + 44_999), Is.False);
            Assert.That(BadState.ShowerPeekDue(NowMs, NowMs + 45_000), Is.True);
            Assert.That(BadState.ShowerPeekDue(0, NowMs), Is.False);
        }

        [Test]
        public void WashingAndBrushingGiveHygieneBackAndCount()
        {
            var gs = Store();
            gs.Update(s => J.Dict(J.Dict(s["gooby"])["stats"])["hygiene"] = 20.0);
            var gained = BadState.MarkWashed(gs);
            Assert.That(gained, Is.EqualTo(BadState.DuschHygiene).Within(1e-6));
            Assert.That(J.Num(J.Path(gs.State, "gooby", "stats", "hygiene")), Is.EqualTo(20.0 + BadState.DuschHygiene).Within(1e-6));
            Assert.That(J.Int(J.Path(gs.State, "achievements", "counters", "washes")), Is.EqualTo(1));

            BadState.MarkWokeUp(gs);
            Assert.That(BadState.NeedsBrushing(gs), Is.True);
            BadState.MarkBrushed(gs, broke: true);
            Assert.That(BadState.NeedsBrushing(gs), Is.False);
            Assert.That(J.Int(J.Path(gs.State, "bad", "brushBrokenCount")), Is.EqualTo(1));
            Assert.That(J.Int(J.Path(gs.State, "achievements", "counters", "teeth_brushed")), Is.EqualTo(1));

            gs.Update(s => J.Dict(J.Dict(s["gooby"])["stats"])["hygiene"] = 95.0);
            Assert.That(BadState.MarkWashed(gs), Is.EqualTo(5.0).Within(1e-6), "capped at 100");

            BadState.EnsureKloTimer(gs, NowMs);
            Assert.That(BadState.KloLastMs(gs), Is.EqualTo(NowMs));
            BadState.EnsureKloTimer(gs, NowMs + 5000);
            Assert.That(BadState.KloLastMs(gs), Is.EqualTo(NowMs), "set once");
            BadState.MarkKloDone(gs, NowMs + 9000);
            Assert.That(BadState.KloLastMs(gs), Is.EqualTo(NowMs + 9000));
        }

        [Test]
        public void LampsAreOnUntilSwitchedOff()
        {
            var gs = Store();
            Assert.That(BadState.LightOn(gs, "u7"), Is.True);
            BadState.SetLightOn(gs, "u7", false);
            Assert.That(BadState.LightOn(gs, "u7"), Is.False);
            Assert.That(BadState.LightOn(gs, "u8"), Is.True, "others stay on");
            var healed = BadState.NormalizeSlice(new Newtonsoft.Json.Linq.JObject { ["lights"] = new Newtonsoft.Json.Linq.JObject { ["x"] = "yes" }, ["kloLastMs"] = -5 });
            Assert.That(J.IsTrue(healed["lights"]["x"]), Is.False, "hostile saves heal to bools");
            Assert.That(J.Long(healed["kloLastMs"]), Is.EqualTo(0));
        }
    }
}
