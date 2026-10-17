using System.Linq;
using Gooby.Core.Home;
using Gooby.Core.Json;
using Gooby.Core.Meta;
using Gooby.Core.Save;
using Gooby.Core.State;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Gooby.Tests.Core
{
    /// <summary>
    /// The level look back (port of test_fix4_recap.gd): milestones every five levels and only the lowest open one is
    /// queued, the lines come days-first then by weight without zeros, and finishing writes history, a fresh baseline and
    /// moves on to the next milestone if Gooby already passed it.
    /// </summary>
    public class RecapTests
    {
        private const long NowMs = 1_791_158_400_000L;

        [SetUp]
        public void Setup()
        {
            Fixtures.InstallContentSource();
            HomeState.RegisterSlice();
        }

        private static GameStateStore Store()
        {
            var store = new GameStateStore { Manager = { Io = new MemorySaveIo() } };
            store.Clock.Pin(NowMs);
            store.Initialize("user/save_v5.json", 0);
            return store;
        }

        [Test]
        public void MilestonesComeEveryFiveLevels()
        {
            Assert.That(Recap.MilestoneCrossed(4, 5, 0), Is.EqualTo(5));
            Assert.That(Recap.MilestoneCrossed(4, 11, 0), Is.EqualTo(5), "the lowest open one first");
            Assert.That(Recap.MilestoneCrossed(6, 11, 5), Is.EqualTo(10));
            Assert.That(Recap.MilestoneCrossed(5, 6, 5), Is.EqualTo(0));
            Assert.That(Recap.MilestoneCrossed(40, 41, 40), Is.EqualTo(0), "no look back past 40");
            Assert.That(Recap.HighestMilestone(23), Is.EqualTo(20));
            Assert.That(Recap.HighestMilestone(4), Is.EqualTo(0));
        }

        [Test]
        public void LinesAreDaysFirstThenByWeightWithoutZeros()
        {
            var baseline = new JObject { ["playsTotal"] = 2, ["coinsEarned"] = 100, ["feeds"] = 5, ["washes"] = 0 };
            var now = new JObject { ["playsTotal"] = 9, ["coinsEarned"] = 340, ["feeds"] = 5, ["washes"] = 3, ["coinsSpent"] = 50 };
            var lines = Recap.Lines(baseline, NowMs - 3 * Recap.DayMs, now, NowMs);
            Assert.That(lines.Select(l => l.Id + ":" + l.N), Is.EqualTo(new[] { "days:3", "games:7", "coinsEarned:240", "washes:3", "coinsSpent:50" }));
        }

        [Test]
        public void FinishingWritesHistoryAndMovesOnToTheNextMilestone()
        {
            var gs = Store();
            gs.Update(s =>
            {
                J.EnsureDict(s, "progression")["level"] = 11;
                J.EnsureDict(J.EnsureDict(s, "achievements"), "counters")["tickles"] = 12;
            });
            Assert.That(Recap.QueueIfMilestone(gs, 4, 11), Is.EqualTo(5));
            Assert.That(Recap.QueueIfMilestone(gs, 9, 10), Is.EqualTo(5), "a lower waiting one stays");
            Assert.That(Recap.PendingLevel(gs), Is.EqualTo(5));
            var lines = Recap.LinesFor(gs, NowMs);
            Assert.That(lines.Any(l => l.Id == "tickles" && l.N == 12), Is.True);
            Recap.Complete(gs, 5, NowMs);
            var slice = Recap.SliceOf(gs.State);
            Assert.That(J.Int(slice["lastRecapLevel"]), Is.EqualTo(5));
            Assert.That(J.Int(slice["pendingLevel"]), Is.EqualTo(10), "level 11 already passed the next milestone");
            Assert.That(((JArray)slice["history"]).Count, Is.EqualTo(1));
            Assert.That(J.Int(J.Path(gs.State, "achievements", "counters", "recapsSeen")), Is.EqualTo(1));
            Assert.That(Recap.LinesFor(gs, NowMs).Any(l => l.Id == "tickles"), Is.False, "the new baseline starts from here");
            Recap.Complete(gs, 10, NowMs + 1000);
            Assert.That(Recap.PendingLevel(gs), Is.EqualTo(0));
        }
    }
}
