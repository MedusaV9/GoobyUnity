using Gooby.Core.Life;
using NUnit.Framework;

namespace Gooby.Tests.Core
{
    /// <summary>Strong feelings get a short camera moment: which ones, how it moves, how rarely (moment_regie.gd).</summary>
    public class MomentRegieTests
    {
        [Test]
        public void OnlyTheFourStrongFeelingsAreStaged()
        {
            foreach (var e in new[] { "schreck", "begeisterung", "stolz", "verliebtheit" }) Assert.That(MomentRegie.Stark(e), Is.True, e);
            foreach (var e in new[] { "freude", "ueberraschung", "verlegenheit", "trotz", "traurigkeit", "muedigkeit", "neugier", "angst" })
                Assert.That(MomentRegie.Stark(e), Is.False, e);
            Assert.That(MomentRegie.Farbe("verliebtheit"), Is.EqualTo("#FF6FA0"));
            Assert.That(MomentRegie.Farbe("schreck"), Is.EqualTo("#CFE0FF"));
        }

        [Test]
        public void ThePunchInEasesInHoldsAndReturnsExactly()
        {
            Assert.That(MomentRegie.DauerS, Is.EqualTo(1.22).Within(1e-9));
            Assert.That(MomentRegie.Zoom(0.0), Is.EqualTo(1.0));
            Assert.That(MomentRegie.Zoom(0.11), Is.LessThan(0.85), "ease-out: most of the way after half the zoom-in");
            Assert.That(MomentRegie.Zoom(0.22), Is.EqualTo(0.82).Within(1e-9));
            Assert.That(MomentRegie.Zoom(0.6), Is.EqualTo(0.82).Within(1e-9), "held");
            Assert.That(MomentRegie.Zoom(0.77 + 0.225), Is.EqualTo(0.91).Within(1e-9), "half way back at half the zoom-out");
            Assert.That(MomentRegie.Zoom(1.22), Is.EqualTo(1.0));
            Assert.That(MomentRegie.Gewicht(5.0), Is.EqualTo(0.0));
        }

        [Test]
        public void SlowMotionIsABriefBreathAndTheMomentStaysRare()
        {
            Assert.That(MomentRegie.Zeitskala(0.1), Is.EqualTo(0.55));
            Assert.That(MomentRegie.Zeitskala(0.4), Is.EqualTo(1.0));
            Assert.That(MomentRegie.Zeitskala(-1.0), Is.EqualTo(1.0));
            Assert.That(MomentRegie.Bereit(100.0, 144.9), Is.False);
            Assert.That(MomentRegie.Bereit(100.0, 145.0), Is.True);
        }
    }
}
