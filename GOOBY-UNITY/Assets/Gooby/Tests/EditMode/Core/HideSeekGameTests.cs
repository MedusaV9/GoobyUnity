using System.Linq;
using Gooby.Core.Minigames.Games;
using Gooby.Core.Minigames.Play;
using NUnit.Framework;

namespace Gooby.Tests.EditMode.Core
{
    public class HideSeekGameTests
    {
        private const double Dt = 1.0 / 60.0;

        private static void Run(HideSeekGame g, double seconds)
        {
            for (var t = 0.0; t < seconds && !g.Finished; t += Dt) g.Tick(Dt);
        }

        /// <summary>Taps every still-hidden hideout the moment its critter peeks (or, if none peek, the spots themselves).</summary>
        private static void Seek(HideSeekGame g, double seconds)
        {
            for (var t = 0.0; t < seconds && !g.Finished; t += Dt)
            {
                g.Tick(Dt);
                if (g.Phase != "play") continue;
                var peek = g.Critters.Values.FirstOrDefault(c => !c.Found && c.PeekT > 0.0);
                if (peek != null) g.Tap(peek.Spot);
                else if (g.Hidden.Count > 0) g.Tap(g.Hidden.First());
            }
        }

        [Test]
        public void TheQaBotFindsPeekingCrittersABeatLateAndTriesAnEmptyHideoutNowAndThen()
        {
            var g = new HideSeekGame("normal", 3);
            var found = 0; var empty = 0;
            g.Found += (spot, pts) => found++;
            g.Empty += spot => empty++;
            for (var t = 0.0; t < 40.0 && !g.Finished; t += Dt)
            {
                g.ApplyBot();
                foreach (var c in g.Critters.Values)
                    Assert.That(c.Found || c.PeekT <= 0.0 || c.PeekT > g.Tune.PeekDurationSec - 0.3 - Dt * 2, Is.True, "a critter is found within its peek, a beat in");
                g.Tick(Dt);
            }
            Assert.That(found, Is.GreaterThanOrEqualTo(6));
            Assert.That(empty, Is.GreaterThanOrEqualTo(1));
            Assert.That(empty, Is.LessThanOrEqualTo(found / 4 + 1));
            Assert.That(g.Wave, Is.GreaterThanOrEqualTo(1), "it clears a wave");
        }

        [Test]
        public void SetupRollsThreeHidersAndTheIntroGatesTapsAndPeeks()
        {
            var g = new HideSeekGame("normal", 7);
            Assert.That(g.Spots, Is.EqualTo(12));
            Assert.That(g.Hidden.Count, Is.EqualTo(3), "wave 0 hides three");
            Assert.That(g.Wave, Is.EqualTo(0));
            Assert.That(g.Tap(g.Hidden.First()), Is.Null, "intro gates taps");
            Assert.That(g.FoundTotal, Is.EqualTo(0));
            Run(g, 1.0);
            Assert.That(g.Elapsed, Is.EqualTo(0.0), "the clock waits out the intro");
            Assert.That(g.Critters.Values.All(c => c.PeekT <= 0.0), Is.True, "nobody peeks during the beat");
        }

        [Test]
        public void ATapOnAHiderFindsItForTwoAndAnEmptySpotIsEmpty()
        {
            var g = new HideSeekGame("normal", 3);
            Run(g, HideSeekGame.IntroSec + 0.05);
            var hiders = g.Hidden.ToList();
            Assert.That(g.Tap(hiders[0]), Is.EqualTo("found"));
            Assert.That(g.Score, Is.EqualTo(2));
            Assert.That(g.FoundTotal, Is.EqualTo(1));
            Assert.That(g.Hidden.Contains(hiders[0]), Is.False);
            Assert.That(g.Critters[hiders[0]].Found, Is.True);
            var empty = Enumerable.Range(0, g.Spots).First(i => !hiders.Contains(i) && i != hiders[0]);
            Assert.That(g.Tap(empty), Is.EqualTo("empty"));
            Assert.That(g.Score, Is.EqualTo(2), "an empty tap costs nothing");
        }

        [Test]
        public void ClearingAWavePaysThreeThenServesTheNextWithMoreHiders()
        {
            var g = new HideSeekGame("normal", 11);
            var cleared = 0;
            var started = 0;
            g.WaveCleared += _ => cleared++;
            g.WaveStarted += n => started = n;
            Run(g, HideSeekGame.IntroSec + 0.02);
            foreach (var s in g.Hidden.ToList()) Assert.That(g.Tap(s), Is.EqualTo("found"));
            Assert.That(cleared, Is.EqualTo(1));
            Assert.That(g.Score, Is.EqualTo(3 * 2 + 3), "+2 each plus the wave bonus");
            Assert.That(g.Phase, Is.EqualTo("serve"));
            Assert.That(g.Tap(0), Is.Null, "serve gates taps");
            Run(g, g.Tune.ServeSec + 0.05);
            Assert.That(g.Phase, Is.EqualTo("play"));
            Assert.That(g.Wave, Is.EqualTo(1));
            Assert.That(started, Is.EqualTo(g.Hidden.Count));
            Assert.That(g.Hidden.Count, Is.GreaterThanOrEqualTo(3));
        }

        [Test]
        public void CrittersPeekOnASeededScheduleAfterTheIntro()
        {
            var g = new HideSeekGame("normal", 5);
            Run(g, HideSeekGame.IntroSec + 0.02);
            Assert.That(g.Critters.Values.Any(c => c.PeekT > 0.0), Is.False, "first peek is 0.6 s plus a roll after elapsed starts");
            Run(g, 0.6 + g.Tune.PeekEverySec + 0.05);
            Assert.That(g.Critters.Values.Any(c => c.PeekT > 0.0 || c.NextPeek > g.Elapsed), Is.True);
            var peeking = g.Critters.Values.Where(c => c.PeekT > 0.0).ToList();
            if (peeking.Count > 0)
            {
                Assert.That(g.RiseOf(peeking[0].Spot), Is.GreaterThan(0.1));
                Assert.That(g.IsPeeking(peeking[0].Spot), Is.True);
            }
        }

        [Test]
        public void ASeekerFindsEveryPeekAndTheTimedRoundEndsAtSixty()
        {
            var g = new HideSeekGame("normal", 9);
            Seek(g, 65.0);
            Assert.That(g.Finished, Is.True);
            Assert.That(g.Elapsed, Is.GreaterThanOrEqualTo(60.0 - 0.05));
            Assert.That(g.FoundTotal, Is.GreaterThan(3));
            Assert.That(g.FinalScore, Is.EqualTo(g.Score));
            Assert.That(g.Score, Is.GreaterThan(8));
        }

        [Test]
        public void LettingAWaveTimeOutExpiresItAndEndlessEndsOnTheThird()
        {
            var g = new HideSeekGame("normal", 2);
            var expired = 0;
            g.WaveExpired += () => expired++;
            Run(g, HideSeekGame.IntroSec + g.Tune.WaveSecStart + 0.2);
            Assert.That(expired, Is.EqualTo(1));
            Assert.That(g.Expired, Is.EqualTo(1));
            Assert.That(g.Phase, Is.EqualTo("serve"));
            Assert.That(g.Finished, Is.False);

            var endless = new HideSeekGame("endless", 4);
            Assert.That(endless.Tune.Endless, Is.True);
            Run(endless, HideSeekGame.IntroSec + 50.0);
            Assert.That(endless.Finished, Is.True);
            Assert.That(endless.Expired, Is.EqualTo(3));
        }

        [Test]
        public void EasyWavesLastLongerAndHardPeeksAreShorter()
        {
            var easy = new HideSeekGame("easy", 1);
            var hard = new HideSeekGame("hard", 1);
            Assert.That(easy.Tune.WaveSecStart, Is.GreaterThan(HideSeek.Seek.WaveSecStart));
            Assert.That(easy.Tune.DurationSec, Is.EqualTo(72.0).Within(1e-9));
            Assert.That(hard.Tune.PeekDurationSec, Is.LessThan(HideSeek.Seek.PeekDurationSec));
            Assert.That(hard.Tune.PeekEverySec, Is.GreaterThan(HideSeek.Seek.PeekEverySec));
        }

        [Test]
        public void RiseIsZeroWhenHiddenSineWhilePeekingAndOneOnceFound()
        {
            var g = new HideSeekGame("normal", 1);
            var spot = g.Hidden.First();
            Assert.That(g.RiseOf(spot), Is.EqualTo(0.0));
            g.Critters[spot].PeekT = g.Tune.PeekDurationSec * 0.5;
            Assert.That(g.RiseOf(spot), Is.EqualTo(0.12 + 1.0 * 0.76).Within(1e-6), "half peek is the sine peak");
            Run(g, HideSeekGame.IntroSec + 0.02);
            g.Tap(spot);
            Assert.That(g.RiseOf(spot), Is.EqualTo(1.0));
        }
    }
}
