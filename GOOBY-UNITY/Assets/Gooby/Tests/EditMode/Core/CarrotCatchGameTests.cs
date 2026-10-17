using System.Collections.Generic;
using Gooby.Core.Minigames.Games;
using Gooby.Core.Minigames.Play;
using NUnit.Framework;

namespace Gooby.Tests.Core
{
    public class CarrotCatchGameTests
    {
        private static int RunPerfectPlayer(string mode, int seed, double dt = 1.0 / 60.0)
        {
            var g = new CarrotCatchGame(mode, seed);
            var steps = 0;
            while (!g.Finished && steps < 60 * 400)
            {
                // perfect player: chase the lowest good item, dodge junk
                CarrotCatchGame.FallingItem target = null;
                foreach (var it in g.Items)
                    if ((it.Kind == "good" || it.Kind == "golden") && (target == null || it.Y > target.Y)) target = it;
                if (target != null) g.TargetX = target.X;
                else
                {
                    CarrotCatchGame.FallingItem junk = null;
                    foreach (var it in g.Items) if (junk == null || it.Y > junk.Y) junk = it;
                    if (junk != null) g.TargetX = junk.X > 0 ? junk.X - 2.0 : junk.X + 2.0;
                }
                g.Tick(dt);
                steps++;
            }
            Assert.That(g.Finished, Is.True, "round ends");
            return g.FinalScore;
        }

        [Test]
        public void TimedRoundEndsAtDurationAndIsDeterministic()
        {
            var a = RunPerfectPlayer("normal", 7);
            var b = RunPerfectPlayer("normal", 7);
            Assert.That(a, Is.EqualTo(b));
            Assert.That(a, Is.GreaterThan(0));
            var g = new CarrotCatchGame("normal", 3);
            for (var i = 0; i < 60 * 70; i++) g.Tick(1.0 / 60.0);
            Assert.That(g.Finished, Is.True);
            Assert.That(g.Elapsed, Is.GreaterThanOrEqualTo(g.Tune.DurationSec));
            Assert.That(g.IntroLeft, Is.EqualTo(0.0));
        }

        [Test]
        public void IntroFreezesTheSimAndInputMovesTheBasket()
        {
            var g = new CarrotCatchGame("normal", 1);
            g.Tick(0.5);
            Assert.That(g.Elapsed, Is.EqualTo(0.0), "intro beat: clock waits");
            Assert.That(g.Items, Is.Empty);
            g.Tick(1.1);
            g.SetTargetFromNormalizedX(1.0);
            for (var i = 0; i < 60; i++) g.Tick(1.0 / 60.0);
            Assert.That(g.BasketX, Is.GreaterThan(2.0));
            Assert.That(g.BasketX, Is.LessThanOrEqualTo(CarrotCatchGame.WorldHalfW - 0.5), "clamped inside the view");
        }

        [Test]
        public void JunkMakesDizzyAndResetsCombo()
        {
            var g = new CarrotCatchGame("hard", 11);
            var caughtKinds = new List<string>();
            g.Caught += (item, _) => caughtKinds.Add(item.Kind);
            var dizzySeen = false;
            var steps = 0;
            while (!g.Finished && steps++ < 60 * 90)
            {
                CarrotCatchGame.FallingItem lowest = null;
                foreach (var it in g.Items) if (lowest == null || it.Y > lowest.Y) lowest = it;
                if (lowest != null) g.TargetX = lowest.X; // greedy: catches junk too
                g.Tick(1.0 / 60.0);
                if (g.Dizzy) dizzySeen = true;
            }
            Assert.That(caughtKinds, Does.Contain("junk").Or.Contain("rotten"));
            Assert.That(dizzySeen, Is.True);
            Assert.That(g.FinalScore, Is.EqualTo(CarrotCatch.FinalScore(g.Score, g.Tune)));
        }

        [Test]
        public void EndlessEndsAfterThreeMissedCarrots()
        {
            var g = new CarrotCatchGame("endless", 5);
            var steps = 0;
            while (!g.Finished && steps++ < 60 * 300) { g.TargetX = -3.0; g.Tick(1.0 / 60.0); }
            Assert.That(g.Finished, Is.True);
            Assert.That(g.MissedCarrots, Is.EqualTo(g.Tune.EndlessMissedCarrots));
        }
    }
}
