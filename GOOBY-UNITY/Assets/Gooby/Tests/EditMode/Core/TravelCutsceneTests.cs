using Gooby.Core.City;
using NUnit.Framework;

namespace Gooby.Tests.Core
{
    /// <summary>Travel cutscene timings (reise_cutscene.gd): skip after 2 s, 5 departure shots, ~12 s return, cards.</summary>
    public class TravelCutsceneTests
    {
        [Test]
        public void DepartureShotsSumToTheGodotWaitTotals()
        {
            Assert.That(TravelCutscene.AbflugShots.Length, Is.EqualTo(5));
            Assert.That(TravelCutscene.TotalSeconds(TravelCutscene.ModusAbflug, false), Is.EqualTo(22.1).Within(1e-9));
            Assert.That(TravelCutscene.At(TravelCutscene.ModusAbflug, 0.5, false).Id, Is.EqualTo("door"));
            Assert.That(TravelCutscene.At(TravelCutscene.ModusAbflug, 4.2, false).Id, Is.EqualTo("driveway"));
            Assert.That(TravelCutscene.At(TravelCutscene.ModusAbflug, 22.0, false).Id, Is.EqualTo("airport"));
            Assert.That(TravelCutscene.At(TravelCutscene.ModusAbflug, 22.1, false), Is.Null);
            Assert.That(TravelCutscene.At(TravelCutscene.ModusAbflug, 3.0, true), Is.Null, "skip ends the reel");
        }

        [Test]
        public void ReturnIsAboutTwelveSeconds()
        {
            Assert.That(TravelCutscene.TotalSeconds(TravelCutscene.ModusRueckkehr, false), Is.EqualTo(10.7).Within(1e-9));
            Assert.That(TravelCutscene.At(TravelCutscene.ModusRueckkehr, 0.1, false).Id, Is.EqualTo("landing"));
            Assert.That(TravelCutscene.At(TravelCutscene.ModusRueckkehr, 4.1, false).Id, Is.EqualTo("walk"));
            Assert.That(TravelCutscene.At(TravelCutscene.ModusRueckkehr, 8.0, false).Id, Is.EqualTo("celebrate"));
        }

        [Test]
        public void SkipIsHiddenUntilTwoSecondsThenFinishes()
        {
            Assert.That(TravelCutscene.SkipVisible(1.9), Is.False);
            Assert.That(TravelCutscene.SkipVisible(2.0), Is.True);
            Assert.That(TravelCutscene.Finished(TravelCutscene.ModusAbflug, 2.0, true, false), Is.True);
            Assert.That(TravelCutscene.Finished(TravelCutscene.ModusAbflug, 1.9, true, false), Is.False, "skip before 2 s is ignored");
            Assert.That(TravelCutscene.Finished(TravelCutscene.ModusAbflug, 22.1, false, false), Is.True);
        }

        [Test]
        public void ReducedMotionShowsTheAuthoredCards()
        {
            Assert.That(TravelCutscene.CardKeys(TravelCutscene.ModusAbflug).Length, Is.EqualTo(5));
            Assert.That(TravelCutscene.CardKeys(TravelCutscene.ModusAbflug)[0], Is.EqualTo("travel.cutscene.karte_1"));
            Assert.That(TravelCutscene.CardKeys(TravelCutscene.ModusAbflug)[4], Is.EqualTo("travel.cutscene.karte_5"));
            Assert.That(TravelCutscene.CardKeys(TravelCutscene.ModusRueckkehr), Is.EqualTo(new[] { "travel.cutscene.karte_rueckkehr" }));
            Assert.That(TravelCutscene.TotalSeconds(TravelCutscene.ModusAbflug, true), Is.EqualTo(5 * TravelCutscene.CardHoldS).Within(1e-9));
            Assert.That(TravelCutscene.CardIndex(0.0, 5), Is.EqualTo(0));
            Assert.That(TravelCutscene.CardIndex(1.6, 5), Is.EqualTo(1));
            Assert.That(TravelCutscene.CardIndex(99.0, 5), Is.EqualTo(4));
        }
    }
}
