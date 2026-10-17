using System;
using Gooby.Core.Minigames.Games;
using Gooby.Core.Minigames.Play;
using NUnit.Framework;

namespace Gooby.Tests.EditMode.Core
{
    public class RunnerGameTests
    {
        private const double Dt = 1.0 / 60.0;

        private static void Run(RunnerGame g, double seconds)
        {
            for (var t = 0.0; t < seconds && !g.Finished; t += Dt) g.Tick(Dt);
        }

        private static void FlyBot(RunnerGame g, double seconds)
        {
            for (var t = 0.0; t < seconds && !g.Finished; t += Dt)
            {
                g.ApplyBot();
                g.Tick(Dt);
            }
        }

        [Test]
        public void SetupLeavesTheSeedAloneAndTheIntroGatesSwipes()
        {
            var g = new RunnerGame("normal", 7);
            Assert.That(g.Lane, Is.EqualTo(1));
            Assert.That(g.Meters, Is.EqualTo(0.0));
            Assert.That(g.Obstacles.Count, Is.EqualTo(0));
            Assert.That(g.Swipe(-80.0, 0.0), Is.False, "intro gates the swipe");
            Assert.That(g.Jump(), Is.False);
            Run(g, 1.0);
            Assert.That(g.Meters, Is.EqualTo(0.0), "the clock waits out the intro");
            Assert.That(g.Elapsed, Is.EqualTo(0.0));
        }

        [Test]
        public void AfterTheIntroTheCorridorScrollsAndASwipeChangesLane()
        {
            var g = new RunnerGame("normal", 3);
            Run(g, RunnerGame.IntroSec + 0.02);
            Assert.That(g.Swipe(-80.0, 0.0), Is.True);
            Assert.That(g.Lane, Is.EqualTo(0));
            Assert.That(g.Swipe(80.0, 0.0), Is.True);
            Assert.That(g.Lane, Is.EqualTo(1));
            Assert.That(g.Swipe(0.0, -80.0), Is.True, "up is jump");
            Assert.That(g.JumpT, Is.GreaterThanOrEqualTo(0.0));
            Assert.That(g.Swipe(0.0, 80.0), Is.False, "cannot slide while jumping");
            Run(g, g.Tune.JumpSec + 0.05);
            Assert.That(g.JumpT, Is.EqualTo(-1.0));
            Assert.That(g.Swipe(0.0, 80.0), Is.True, "down is slide");
            Assert.That(g.Sliding, Is.True);
            Run(g, 2.0);
            Assert.That(g.Meters, Is.EqualTo(g.Tune.BaseSpeed * g.Elapsed).Within(0.2));
            Assert.That(g.Obstacles.Count, Is.GreaterThan(0), "the first rows are in view after the gap");
        }

        [Test]
        public void SameSeedDealsTheSameOpenerRow()
        {
            var a = new RunnerGame("normal", 11);
            var b = new RunnerGame("normal", 11);
            Run(a, RunnerGame.IntroSec + 3.0);
            Run(b, RunnerGame.IntroSec + 3.0);
            Assert.That(a.Obstacles.Count, Is.EqualTo(b.Obstacles.Count));
            Assert.That(a.Obstacles[0].Kind, Is.EqualTo(b.Obstacles[0].Kind));
            Assert.That(a.Obstacles[0].Lane, Is.EqualTo(b.Obstacles[0].Lane));
            var expected = Runner.GenerateRow(new Gooby.Core.Random.GoobyRng(11), 0.0, new System.Collections.Generic.List<Runner.Row>(), Runner.Run);
            Assert.That(a.Obstacles[0].Kind, Is.EqualTo(expected.Lanes[a.Obstacles[0].Lane]));
        }

        [Test, Timeout(8000)]
        public void TheBotRunsTheOpenerAndTheMetresPay()
        {
            var g = new RunnerGame("easy", 11);
            var coins = 0;
            g.CoinTaken += _ => coins++;
            Run(g, RunnerGame.IntroSec + 0.02);
            FlyBot(g, 25.0);
            Assert.That(g.Finished, Is.False, "the PD autoplay keeps the easy corridor alive");
            Assert.That(g.Meters, Is.GreaterThan(80.0));
            Assert.That(g.Score, Is.EqualTo(Runner.FinalScore(g.Meters, g.CoinPoints, g.Tune)));
            Assert.That(g.Score, Is.GreaterThanOrEqualTo((int)g.Meters));
            Assert.That(coins, Is.EqualTo(g.Coins));
        }

        [Test]
        public void AnIdlePilotStumblesThenWipesOut()
        {
            var g = new RunnerGame("normal", 3);
            var stumbled = 0;
            g.Stumbled += () => stumbled++;
            Run(g, RunnerGame.IntroSec + 0.02);
            Run(g, 40.0);
            Assert.That(g.Finished, Is.True, "standing in the middle lane eats two hits");
            Assert.That(g.Hits, Is.GreaterThanOrEqualTo(g.Tune.MaxHits));
            Assert.That(stumbled, Is.GreaterThanOrEqualTo(1));
            Assert.That(g.Swipe(-40.0, 0.0), Is.False);
            Assert.That(g.FinalScore, Is.EqualTo(Runner.FinalScore(g.Meters, g.CoinPoints, g.Tune)));
        }

        [Test]
        public void EasyGrantsAnExtraHitAndHardIsDenser()
        {
            var easy = new RunnerGame("easy", 5);
            var hard = new RunnerGame("hard", 5);
            Assert.That(easy.Tune.MaxHits, Is.EqualTo(3));
            Assert.That(hard.Tune.MaxHits, Is.EqualTo(2));
            Assert.That(hard.Tune.BaseSpeed, Is.GreaterThan(easy.Tune.BaseSpeed));
            Assert.That(Runner.MagnetCollects(2.9, 0, 0, 0, 0, 0, true, Runner.Run), Is.True);
            Assert.That(Runner.MagnetCollects(3.1, 0, 0, 0, 0, 0, true, Runner.Run), Is.False);
            var st = Runner.ActivateMysteryPower(new Runner.MysteryState(), "magnet", Runner.Run);
            Assert.That(st.MagnetT, Is.EqualTo(4.0));
            st = Runner.ActivateMysteryPower(st, "shield", Runner.Run);
            Assert.That(st.Shield, Is.True);
        }

        [Test]
        public void AJumpClearsAConeAndASlideClearsAnOverhead()
        {
            Assert.That(Runner.ActionPasses("cone", "jump"), Is.True);
            Assert.That(Runner.ActionPasses("overhead", "slide"), Is.True);
            Assert.That(Runner.ActionPasses("car", "jump"), Is.False);
            Assert.That(Runner.HitsObstacle(1, 0.0, false, 1, "cone", 0.0, Runner.Run), Is.True, "standing hits the cone");
            Assert.That(Runner.HitsObstacle(1, 1.0, false, 1, "cone", 0.0, Runner.Run), Is.False, "a full jump clears 0.45 m");
            Assert.That(Runner.HitsObstacle(1, 0.0, true, 1, "overhead", 0.0, Runner.Run), Is.False, "sliding under the scaffold");
            Assert.That(Runner.HitsObstacle(1, 0.0, false, 1, "overhead", 0.0, Runner.Run), Is.True);
        }
    }
}
