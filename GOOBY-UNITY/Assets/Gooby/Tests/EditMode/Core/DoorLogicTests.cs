using Gooby.Core.Home;
using NUnit.Framework;

namespace Gooby.Tests.Core
{
    /// <summary>The door gag (port of test_door_logic.gd's rules): chance, never twice, the tap mash with its resistance curve.</summary>
    public class DoorLogicTests
    {
        [Test]
        public void StuckOnlyByChanceAnimatedAndNeverTwice()
        {
            Assert.That(new DoorLogic(true, false, 0.05).WillStick, Is.True);
            Assert.That(new DoorLogic(true, false, 0.2).WillStick, Is.False);
            Assert.That(new DoorLogic(true, true, 0.0).WillStick, Is.False, "never twice in a row");
            Assert.That(new DoorLogic(false, false, 0.0).WillStick, Is.False, "not without door animations");
            var cut = new DoorLogic(false, false, 0.0);
            Assert.That(cut.Begin(), Is.EqualTo(DoorLogic.State.Travel), "no animation → straight cut");
            Assert.That(new DoorLogic(true, false, 1.0, 0.0).TapsRequired, Is.EqualTo(5));
            Assert.That(new DoorLogic(true, false, 1.0, 0.999).TapsRequired, Is.EqualTo(8));
        }

        [Test]
        public void MashingPopsHimThroughAgainstTheResistance()
        {
            var d = new DoorLogic(true, false, 0.0, 0.0);
            Assert.That(d.Begin(), Is.EqualTo(DoorLogic.State.Opening));
            Assert.That(d.Skip(), Is.EqualTo(DoorLogic.State.Travel), "skipping the opening goes straight through");

            d = new DoorLogic(true, false, 0.0, 0.0);
            d.Begin();
            d.DoorOpened();
            Assert.That(d.ReachedDoor(), Is.EqualTo(DoorLogic.State.Stuck));
            Assert.That(d.Skip(), Is.EqualTo(DoorLogic.State.Stuck), "no skipping while stuck");
            for (var i = 0; i < 4; i++) d.TapMash();
            Assert.That(d.IstLetzterTap, Is.True);
            var langsam = new DoorLogic(true, false, 0.0, 0.0);
            langsam.Begin(); langsam.DoorOpened(); langsam.ReachedDoor();
            langsam.TapMash();
            Assert.That(d.DecayRate, Is.GreaterThan(langsam.DecayRate), "the closer to popping, the faster it drains");
            d.MashDecay(10.0);
            Assert.That(d.MashRatio, Is.EqualTo(0.0), "one tap now and then is not enough");
            for (var i = 0; i < 4; i++) d.TapMash();
            Assert.That(d.TapMash(), Is.EqualTo(DoorLogic.State.Popping));
            Assert.That(d.PopFinished(), Is.EqualTo(DoorLogic.State.Travel));
            Assert.That(d.IsTraveling, Is.True);
        }
    }
}
