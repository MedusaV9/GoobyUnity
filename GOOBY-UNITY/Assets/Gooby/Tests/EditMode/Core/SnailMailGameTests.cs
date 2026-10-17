using Gooby.Core.Minigames.Games;
using Gooby.Core.Minigames.Play;
using Gooby.Core.Random;
using NUnit.Framework;

namespace Gooby.Tests.EditMode.Core
{
    public class SnailMailGameTests
    {
        private const double Dt = 1.0 / 60.0;

        private static void Run(SnailMailGame g, double seconds)
        {
            for (var t = 0.0; t < seconds && !g.Finished; t += Dt) g.Tick(Dt);
        }

        private static void FlyBot(SnailMailGame g, double seconds)
        {
            for (var t = 0.0; t < seconds && !g.Finished; t += Dt)
            {
                g.ApplyBot();
                g.Tick(Dt);
            }
        }

        [Test]
        public void SetupDealsLevelZeroBeforeTheIntroAndGatesTheStroke()
        {
            var expected = SnailMail.GenerateLevel(new GoobyRng(7), 0, SnailMail.Snail);
            var g = new SnailMailGame("normal", 7);
            Assert.That(g.Level.Houses.Count, Is.EqualTo(3));
            Assert.That(g.Level.TargetIdx, Is.EqualTo(expected.TargetIdx), "the opener is the first seed draws");
            Assert.That(g.Level.Houses[0].X, Is.EqualTo(expected.Houses[0].X).Within(1e-12));
            Assert.That(g.Level.Houses[0].Y, Is.EqualTo(expected.Houses[0].Y).Within(1e-12));
            Assert.That(g.BeginStroke(g.Tune.PostX, g.Tune.PostY), Is.False, "intro gates the stroke");
            Assert.That(g.ApplyBot(), Is.False);
            Run(g, 1.0);
            Assert.That(g.Elapsed, Is.EqualTo(0.0), "the clock waits out the intro");
            Assert.That(g.Deliveries, Is.EqualTo(0));
        }

        [Test]
        public void AfterTheIntroAStrokeFromThePostToTheDoorDelivers()
        {
            var g = new SnailMailGame("normal", 3);
            Run(g, SnailMailGame.IntroSec + 0.02);
            Assert.That(g.BeginStroke(g.Tune.PostX + 2.0, g.Tune.PostY), Is.False, "must start at the mailbox");
            Assert.That(g.ApplyBot(), Is.True);
            Assert.That(g.Phase, Is.EqualTo(SnailMailGame.PhaseFollow));
            Run(g, 8.0);
            Assert.That(g.Deliveries, Is.GreaterThanOrEqualTo(1));
            Assert.That(g.Score, Is.GreaterThanOrEqualTo(SnailMail.Snail.DeliverPts));
            Assert.That(g.Phase, Is.Not.EqualTo(SnailMailGame.PhaseRetreat));
        }

        [Test]
        public void SameSeedDealsTheSameOpenerHouses()
        {
            var a = new SnailMailGame("normal", 11);
            var b = new SnailMailGame("normal", 11);
            Assert.That(a.Level.TargetIdx, Is.EqualTo(b.Level.TargetIdx));
            Assert.That(a.Level.Houses[1].X, Is.EqualTo(b.Level.Houses[1].X).Within(1e-12));
            Assert.That(a.Level.Puddles.Count, Is.EqualTo(b.Level.Puddles.Count));
        }

        [Test, Timeout(8000)]
        public void TheBotDeliversThenTheClockClosesTheRound()
        {
            var g = new SnailMailGame("easy", 11);
            var dry = 0;
            g.Delivered += (_, isDry) => { if (isDry) dry++; };
            Run(g, SnailMailGame.IntroSec + 0.02);
            FlyBot(g, 25.0);
            Assert.That(g.Finished, Is.False);
            Assert.That(g.Deliveries, Is.GreaterThanOrEqualTo(2));
            Assert.That(g.Score, Is.GreaterThanOrEqualTo(g.Deliveries * 4));
            Assert.That(dry, Is.GreaterThanOrEqualTo(1), "the auto-router stays out of the puddles");
            Run(g, g.Tune.DurationSec);
            Assert.That(g.Finished, Is.True);
            Assert.That(g.FinalScore, Is.EqualTo(g.Score));
            Assert.That(g.BeginStroke(g.Tune.PostX, g.Tune.PostY), Is.False);
        }

        [Test]
        public void AnIdlePilotNeverLeavesTheMailboxAndTheClockRunsOut()
        {
            var g = new SnailMailGame("normal", 3);
            Run(g, SnailMailGame.IntroSec + 0.02);
            Run(g, g.Tune.DurationSec + 0.2);
            Assert.That(g.Finished, Is.True);
            Assert.That(g.Deliveries, Is.EqualTo(0));
            Assert.That(g.FinalScore, Is.EqualTo(0));
        }

        [Test]
        public void EasyGivesMoreTimeAndHardIsFaster()
        {
            var easy = new SnailMailGame("easy", 5);
            var hard = new SnailMailGame("hard", 5);
            Assert.That(easy.Tune.DurationSec, Is.GreaterThan(hard.Tune.DurationSec));
            Assert.That(hard.Tune.Speed, Is.GreaterThan(easy.Tune.Speed));
            Assert.That(SnailMail.DeliveryPoints(false, 2, SnailMail.Snail), Is.EqualTo(8));
            Assert.That(SnailMail.DeliveryPoints(true, 0, SnailMail.Snail), Is.EqualTo(4));
            Assert.That(SnailMail.EndlessShouldEnd(3, SnailMail.ApplyDifficulty(SnailMail.Snail, "endless")), Is.True);
        }

        [Test]
        public void AStrokeThatMissesTheGlowingDoorIsRejected()
        {
            var g = new SnailMailGame("normal", 3);
            Run(g, SnailMailGame.IntroSec + 0.02);
            var rejected = 0;
            g.StrokeRejected += _ => rejected++;
            Assert.That(g.BeginStroke(g.Tune.PostX, g.Tune.PostY), Is.True);
            g.ExtendStroke(g.Tune.PostX, g.Tune.PostY + 0.4);
            g.ExtendStroke(g.Tune.PostX, g.Tune.PostY + 0.8);
            Assert.That(g.CommitStroke(), Is.False, "the tip never reaches a door");
            Assert.That(rejected, Is.GreaterThanOrEqualTo(1));
            Assert.That(g.Phase, Is.EqualTo(SnailMailGame.PhaseDraw));
            Assert.That(g.Raw.Count, Is.EqualTo(0));
        }
    }
}
