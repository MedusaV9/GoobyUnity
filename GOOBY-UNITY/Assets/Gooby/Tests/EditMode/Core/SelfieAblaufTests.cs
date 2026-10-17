using Gooby.Core.Net;
using NUnit.Framework;

namespace Gooby.Tests.Core
{
    /// <summary>The visit selfie's countdown (port of test_w13c_fotowerk.gd test_selfie_countdown_statemaschine_zeitinjiziert).</summary>
    public class SelfieAblaufTests
    {
        [Test]
        public void PoseBeatThenThreeTwoOneThenClick()
        {
            var a = new SelfieAblauf();
            Assert.That(a.Takt(0.79), Is.Empty, "the pose beat is still running");
            Assert.That(a.Takt(0.02), Is.EqualTo(new[] { "zeige_3" }));
            Assert.That(a.Takt(0.9), Is.Empty, "the second is not over yet");
            Assert.That(a.Takt(0.11), Is.EqualTo(new[] { "zeige_2" }));
            Assert.That(a.Takt(1.0), Is.EqualTo(new[] { "zeige_1" }));
            Assert.That(a.Takt(1.0), Is.EqualTo(new[] { "ausloesen" }));
            Assert.That(a.Phase, Is.EqualTo("fertig"));
            Assert.That(a.Takt(99.0), Is.Empty, "done stays done");
        }

        [Test]
        public void ALongFrameCatchesUpInOrder()
        {
            Assert.That(new SelfieAblauf().Takt(60.0), Is.EqualTo(new[] { "zeige_3", "zeige_2", "zeige_1", "ausloesen" }));
            Assert.That(new SelfieAblauf().Takt(-5.0), Is.Empty, "negative time does nothing");
        }
    }
}
