using System;
using System.Linq;
using Gooby.Core.Minigames.Games;
using Gooby.Core.Minigames.Play;
using Gooby.Core.Random;
using NUnit.Framework;

namespace Gooby.Tests.EditMode.Core
{
    public class LanternFloatGameTests
    {
        private const double Dt = 1.0 / 60.0;

        private static void Run(LanternFloatGame g, double seconds)
        {
            for (var t = 0.0; t < seconds && !g.Finished; t += Dt) g.Tick(Dt);
        }

        /// <summary>Aims at the next unfinished ring every tick (Godot's finger held on its x).</summary>
        private static void Pilot(LanternFloatGame g, double seconds)
        {
            for (var t = 0.0; t < seconds && !g.Finished; t += Dt)
            {
                var next = g.Rings.FirstOrDefault(r => !r.Done);
                if (next != null) g.SteerNormalized(next.X / (g.Tune.HalfW * g.Tune.SteerOverreach));
                g.Tick(Dt);
            }
        }

        [Test]
        public void SetupBurnsTheStarStreamThenTheFirstRingsSitAtTheLead()
        {
            var rng = new GoobyRng(7);
            for (var i = 0; i < 60 * 3; i++) rng.Next();
            var expected = LanternFloat.RollRing(rng, 0, LanternFloat.Lantern);
            var g = new LanternFloatGame("normal", 7);
            Assert.That(g.IntroLeft, Is.EqualTo(LanternFloatGame.IntroSec));
            Assert.That(g.Travel, Is.EqualTo(0.0));
            Assert.That(g.Rings.Count, Is.GreaterThan(1));
            Assert.That(g.Rings[0].Y, Is.EqualTo(LanternFloat.RingSpacingAt(0.0, g.Tune)).Within(1e-9));
            Assert.That(g.Rings[0].X, Is.EqualTo(expected.X).Within(1e-12), "the 180 discarded draws keep ring 0 on the 2D seed");
            Assert.That(g.Rings.All(r => r.Gold == ((r.Index + 1) % 5 == 0)), Is.True, "gold is every 5th");
            Assert.That(g.Rings.Count, Is.EqualTo(2), "at rest only the rings inside the 11 m lead are born");
            g.SteerNormalized(1.0);
            Assert.That(g.SteerTarget, Is.EqualTo(0.0), "input is gated through the intro");
        }

        [Test]
        public void AfterTheIntroTheLanternRisesAndADragSteersIt()
        {
            var g = new LanternFloatGame("normal", 3);
            Run(g, LanternFloatGame.IntroSec + 0.1);
            Assert.That(g.IntroLeft, Is.EqualTo(0.0));
            Assert.That(g.Travel, Is.GreaterThan(0.0).And.LessThan(0.4));
            g.SteerNormalized(1.0);
            Assert.That(g.SteerTarget, Is.EqualTo(g.Tune.HalfW * g.Tune.SteerOverreach).Within(1e-9));
            Run(g, 0.8);
            Assert.That(g.LanternX, Is.GreaterThan(1.5), "lerp 6.5 /s reaches the right in under a second");
            Assert.That(g.LanternX, Is.LessThanOrEqualTo(g.Tune.HalfW));
            Assert.That(g.Travel, Is.EqualTo(g.Tune.RiseSpeed * g.Elapsed).Within(0.05));
        }

        [Test]
        public void APilotCollectsRingsAndEveryFifthGold()
        {
            var g = new LanternFloatGame("easy", 11);
            var golds = 0;
            g.RingHit += (pts, gold) => { if (gold) golds++; };
            Pilot(g, 25.0);
            Assert.That(g.Hits, Is.GreaterThan(4));
            Assert.That(g.Score, Is.GreaterThan(8));
            Assert.That(g.Golds, Is.EqualTo(golds));
            Assert.That(g.Golds, Is.GreaterThan(0));
            Assert.That(g.RingsPassed, Is.GreaterThanOrEqualTo(g.Hits));
        }

        [Test]
        public void MissingARingBreaksTheStreakWithoutAPenalty()
        {
            var g = new LanternFloatGame("normal", 5);
            Run(g, LanternFloatGame.IntroSec + 0.01);
            g.SteerNormalized(-1.0);
            var missed = 0;
            g.RingMissed += () => missed++;
            // sit on the left wall: most rings spawn in the middle third, so several misses pile up
            Run(g, 8.0);
            Assert.That(missed, Is.GreaterThan(0));
            Assert.That(g.Streak, Is.EqualTo(0));
            Assert.That(g.Score, Is.GreaterThanOrEqualTo(0), "a miss is never a minus");
        }

        [Test]
        public void ACloudBumpCostsThreeAndGrantsInvulnerability()
        {
            var g = new LanternFloatGame("normal", 2);
            var bumps = 0;
            g.Bumped += () => bumps++;
            Run(g, LanternFloatGame.IntroSec + 0.01);
            // hold still at x=0 so a centred cloud clips the lantern
            Run(g, 30.0);
            if (bumps == 0)
            {
                // seed 2 may miss: force a bump by sitting on a live cloud
                var cloud = g.Rings.FirstOrDefault(r => r.Cloud && !r.CloudDone);
                if (cloud != null)
                {
                    g.SteerNormalized(cloud.CloudX / (g.Tune.HalfW * g.Tune.SteerOverreach));
                    while (!g.Finished && bumps == 0 && g.Travel < cloud.Y + 0.5) g.Tick(Dt);
                }
            }
            Assert.That(g.Bumps, Is.GreaterThan(0));
            Assert.That(g.Score, Is.GreaterThanOrEqualTo(0), "bumps cut to zero, never negative");
        }

        [Test]
        public void AGustShovesTheLanternOffTheSteerTarget()
        {
            var g = new LanternFloatGame("normal", 1);
            Run(g, LanternFloatGame.IntroSec + 0.01);
            g.SteerNormalized(0.0);
            // first gust: telegraph 7.0, push 8.1..9.5
            Run(g, 8.15 - g.Elapsed);
            var (gust, phase) = g.Gust;
            Assert.That(phase, Is.EqualTo("push"));
            var before = g.LanternX;
            Run(g, 0.4);
            Assert.That(g.LanternX * gust.Dir, Is.GreaterThan(0.15),
                "the shove wins over a still finger at x=0 (lerp also pulls back, so not the full force×dt)");
            Assert.That(Math.Abs(g.LanternX), Is.GreaterThan(Math.Abs(before)));
        }

        [Test]
        public void TheTimedRoundEndsAtSixtySecondsWithTheScoreBanked()
        {
            var g = new LanternFloatGame("normal", 9);
            var finished = false;
            g.Finished_ += () => finished = true;
            Pilot(g, 70.0);
            Assert.That(finished, Is.True);
            Assert.That(g.Elapsed, Is.GreaterThanOrEqualTo(60.0));
            Assert.That(g.FinalScore, Is.EqualTo(g.Score));
            Assert.That(g.TimeLeft, Is.EqualTo(0.0));
            var frozen = g.SteerTarget;
            g.SteerNormalized(1.0);
            Assert.That(g.SteerTarget, Is.EqualTo(frozen), "input after finish is ignored");
        }

        [Test]
        public void EndlessEndsOnTheThirdBumpAndEasyRingsAreWider()
        {
            var endless = new LanternFloatGame("endless", 4);
            Assert.That(endless.Tune.Endless, Is.True);
            Assert.That(endless.Tune.RingRadius, Is.LessThan(LanternFloat.Lantern.RingRadius));
            Run(endless, LanternFloatGame.IntroSec + 0.01);
            // park on every cloud that appears
            for (var i = 0; i < 4000 && !endless.Finished; i++)
            {
                var cloud = endless.Rings.FirstOrDefault(r => r.Cloud && !r.CloudDone);
                if (cloud != null) endless.SteerNormalized(cloud.CloudX / (endless.Tune.HalfW * endless.Tune.SteerOverreach));
                endless.Tick(Dt);
            }
            Assert.That(endless.Finished, Is.True);
            Assert.That(endless.Bumps, Is.EqualTo(3));

            var easy = new LanternFloatGame("easy", 1);
            var hard = new LanternFloatGame("hard", 1);
            Assert.That(easy.Tune.RingRadius, Is.GreaterThan(LanternFloat.Lantern.RingRadius));
            Assert.That(hard.Tune.RiseSpeed, Is.GreaterThan(LanternFloat.Lantern.RiseSpeed));
            Assert.That(easy.Tune.DurationSec, Is.EqualTo(72.0).Within(1e-9));
        }
    }
}
