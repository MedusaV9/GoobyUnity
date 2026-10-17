using System.Linq;
using Gooby.Core.Json;
using Gooby.Core.Meta;
using Gooby.Core.Save;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Gooby.Tests.EditMode.Core
{
    /// <summary>
    /// The guided first quarter hour (port of test_rest2_onboarding_guide.gd): nine steps from the arrival to the outlook,
    /// fulfilment over the real save counters against a baseline, the freshness check that spares older saves, skipping
    /// for good and resuming a tour under way.
    /// </summary>
    public class OnboardingGuideTests
    {
        private const long Now = 1_768_478_400_000L;

        private static JObject MovedIn()
        {
            var s = SaveSchema.DefaultState(Now);
            J.EnsureDict(s, "onboarding")["done"] = true;
            return s;
        }

        private static void Bump(JObject s, string counter) =>
            J.EnsureDict(J.EnsureDict(s, "achievements"), "counters")[counter] = J.Int(J.Path(s, "achievements", "counters", counter)) + 1;

        [Test]
        public void TheStepsStartWithTheArrivalAndEndWithTheOutlook()
        {
            Assert.That(OnboardingGuide.Steps.Count, Is.EqualTo(9));
            Assert.That(OnboardingGuide.Steps[0].Id + ":" + OnboardingGuide.Steps[0].Art, Is.EqualTo("ankunft:manuell"));
            Assert.That(OnboardingGuide.Steps[8].Id + ":" + OnboardingGuide.Steps[8].Art, Is.EqualTo("ausblick:manuell"));
            Assert.That(OnboardingGuide.Steps.Skip(1).Take(7).Select(s => s.Id), Is.EqualTo(new[] { "streicheln", "fuettern", "waschen", "muenzen", "minispiel", "moebel", "sticker" }));
            Assert.That(OnboardingGuide.Steps.Skip(1).Take(7).All(s => s.Art == OnboardingGuide.Auto), Is.True, "the middle is done by doing");
        }

        [Test]
        public void FulfilmentRunsOverTheRealCountersSinceTheStepBegan()
        {
            var s = MovedIn();
            var b = OnboardingGuide.Snapshot(s);
            Assert.That(OnboardingGuide.Satisfied("fuettern", b, s), Is.False);
            Bump(s, "feeds");
            Assert.That(OnboardingGuide.Satisfied("fuettern", b, s), Is.True, "feeding counts");
            Assert.That(OnboardingGuide.Satisfied("waschen", b, s), Is.False);
            Bump(s, "washes");
            Assert.That(OnboardingGuide.Satisfied("waschen", b, s), Is.True, "washing counts");
            Bump(s, "petsToday");
            Assert.That(OnboardingGuide.Satisfied("streicheln", b, s), Is.True, "petting counts");
            Assert.That(OnboardingGuide.Satisfied("muenzen", b, s), Is.False);
            s["economy"]["coinsEarned"] = J.Int(s["economy"]["coinsEarned"]) + 5;
            Assert.That(OnboardingGuide.Satisfied("muenzen", b, s), Is.True, "coins count");
            Assert.That(OnboardingGuide.Satisfied("minispiel", b, s), Is.False);
            J.EnsureDict(J.EnsureDict(s, "minigames"), "plays")["teaParty"] = 1;
            Assert.That(OnboardingGuide.Satisfied("minispiel", b, s), Is.True, "a round counts");
            Assert.That(OnboardingGuide.Satisfied("moebel", b, s), Is.False);
            s["economy"]["coinsSpent"] = J.Int(s["economy"]["coinsSpent"]) + 10;
            Assert.That(OnboardingGuide.Satisfied("moebel", b, s), Is.True, "a purchase counts");
            Assert.That(OnboardingGuide.Satisfied("sticker", b, s), Is.False);
            J.EnsureDict(J.EnsureDict(s, "stickers"), "unlocked")["s1"] = Now;
            Assert.That(OnboardingGuide.Satisfied("sticker", b, s), Is.True, "a sticker counts");
            Assert.That(OnboardingGuide.Satisfied("ankunft", b, s), Is.False, "manual steps wait for their button");
        }

        [Test]
        public void FreshSavesGetTheTourOlderOnesAreMarkedDoneQuietly()
        {
            var s = MovedIn();
            Assert.That(OnboardingGuide.Decide(s), Is.EqualTo(OnboardingGuide.DecideStart));
            Assert.That(OnboardingGuide.Decide(SaveSchema.DefaultState(Now)), Is.EqualTo(OnboardingGuide.DecideNone), "not before moving in");
            J.EnsureDict(s, "progression")["level"] = 7;
            Assert.That(OnboardingGuide.Decide(s), Is.EqualTo(OnboardingGuide.DecideQuiet), "level 7 is no beginner");
            OnboardingGuide.MarkDoneQuietly(s);
            Assert.That(OnboardingGuide.Finished(s), Is.True);
            Assert.That(OnboardingGuide.Decide(s), Is.EqualTo(OnboardingGuide.DecideNone));
        }

        [Test]
        public void TheTourAdvancesSkipsAStepAndEndsForGood()
        {
            var s = MovedIn();
            OnboardingGuide.Begin(s);
            Assert.That(OnboardingGuide.Current(s).Id, Is.EqualTo("ankunft"));
            Assert.That(OnboardingGuide.Active(s), Is.True);
            Assert.That(OnboardingGuide.Advance(s).Id, Is.EqualTo("streicheln"));
            Assert.That(J.Int(J.Path(s, "onboarding", "guide", "step")), Is.EqualTo(1), "progress is saved");
            var b = (JObject)J.Path(s, "onboarding", "guide", "base");
            Bump(s, "petsToday");
            Assert.That(OnboardingGuide.Satisfied("streicheln", b, s), Is.True);
            Assert.That(OnboardingGuide.Advance(s).Id, Is.EqualTo("fuettern"));
            Assert.That(OnboardingGuide.Advance(s).Id, Is.EqualTo("waschen"), "skipping a step is just the next one");
            OnboardingGuide.Skip(s);
            Assert.That(J.IsTrue(J.Path(s, "onboarding", "guide", "skipped")) && J.IsTrue(J.Path(s, "onboarding", "guide", "done")), Is.True);
            Assert.That(OnboardingGuide.Decide(s), Is.EqualTo(OnboardingGuide.DecideNone), "a skipped tour never returns");
            Assert.That(OnboardingGuide.Active(s), Is.False);

            var all = MovedIn();
            OnboardingGuide.Begin(all);
            for (var i = 0; i < 8; i++) Assert.That(OnboardingGuide.Advance(all), Is.Not.Null);
            Assert.That(OnboardingGuide.Current(all).Id, Is.EqualTo("ausblick"));
            Assert.That(OnboardingGuide.Advance(all), Is.Null, "past the outlook the tour is done");
            Assert.That(OnboardingGuide.Finished(all), Is.True);
        }

        [Test]
        public void ATourUnderWayResumesEvenWhenTheSaveIsNoLongerFresh()
        {
            var s = MovedIn();
            s["onboarding"]["guide"] = new JObject { ["done"] = false, ["skipped"] = false, ["step"] = 3, ["base"] = new JObject { ["washes"] = 0 } };
            J.EnsureDict(s, "progression")["level"] = 9;
            Assert.That(OnboardingGuide.Decide(s), Is.EqualTo(OnboardingGuide.DecideResume));
            Assert.That(OnboardingGuide.Current(s).Id, Is.EqualTo("waschen"));
            var loaded = JObject.Parse(s.ToString());
            Assert.That(OnboardingGuide.Current(loaded).Id, Is.EqualTo("waschen"), "survives a save round trip");
        }
    }
}
