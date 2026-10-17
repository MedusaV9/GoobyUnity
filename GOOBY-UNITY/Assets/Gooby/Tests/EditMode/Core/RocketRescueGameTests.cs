using System;
using Gooby.Core.Minigames.Games;
using Gooby.Core.Minigames.Play;
using Gooby.Core.Random;
using NUnit.Framework;

namespace Gooby.Tests.EditMode.Core
{
    public class RocketRescueGameTests
    {
        private const double Dt = 1.0 / 60.0;

        private static void Run(RocketRescueGame g, double seconds)
        {
            for (var t = 0.0; t < seconds && !g.Finished; t += Dt) g.Tick(Dt);
        }

        private static void FlyBot(RocketRescueGame g, double seconds)
        {
            for (var t = 0.0; t < seconds && !g.Finished; t += Dt)
            {
                g.ApplyBot();
                g.Tick(Dt);
            }
        }

        [Test]
        public void SetupBuildsFivePadsBeforeTheIntroAndGatesThrust()
        {
            var rng = new GoobyRng(7);
            var expected = RocketRescue.CreateLayout(() => rng.Next(), RocketRescue.Rocket);
            var g = new RocketRescueGame("normal", 7);
            Assert.That(g.Engine.Layout.Platforms.Count, Is.EqualTo(5));
            Assert.That(g.Engine.Layout.FuelPickups.Count, Is.EqualTo(8));
            Assert.That(g.Engine.Layout.Platforms[0].X, Is.EqualTo(expected.Platforms[0].X).Within(1e-12), "layout is the first seed draws");
            Assert.That(g.Engine.Layout.Platforms[0].Y, Is.EqualTo(expected.Platforms[0].Y).Within(1e-12));
            Assert.That(g.Engine.Y, Is.EqualTo(g.Engine.Layout.PadY));
            Assert.That(g.Engine.LandedOn, Is.EqualTo(RocketRescue.PadId));
            Assert.That(g.SetTouch(true, 0.0), Is.False, "intro gates thrust");
            Assert.That(g.Thrust, Is.False);
            var y = g.Engine.Y;
            Run(g, 1.0);
            Assert.That(g.Engine.Y, Is.EqualTo(y), "the lander waits out the intro");
            Assert.That(g.Engine.Elapsed, Is.EqualTo(0.0));
        }

        [Test]
        public void AfterTheIntroAHoldLiftsOffThePad()
        {
            var g = new RocketRescueGame("normal", 3);
            Run(g, RocketRescueGame.IntroSec + 0.02);
            Assert.That(g.SetTouch(true, 0.0), Is.True);
            Assert.That(g.Thrust, Is.True);
            Assert.That(g.TiltDir, Is.EqualTo(0));
            Run(g, 0.45);
            Assert.That(g.Engine.LandedOn, Is.Null, "thrust clears the pad");
            Assert.That(g.Engine.Y, Is.GreaterThan(0.15));
            Assert.That(g.Engine.Fuel, Is.LessThan(g.Tune.FuelMax));
        }

        [Test]
        public void LeftAndRightThirdsTiltTheLander()
        {
            var g = new RocketRescueGame("normal", 5);
            Run(g, RocketRescueGame.IntroSec + 0.02);
            Assert.That(g.SetTouch(true, -0.8), Is.True);
            Assert.That(g.TiltDir, Is.EqualTo(-1));
            Assert.That(g.SetTouch(true, 0.8), Is.True);
            Assert.That(g.TiltDir, Is.EqualTo(1));
            Assert.That(g.SetTouch(false, 0.8), Is.True);
            Assert.That(g.Thrust, Is.False);
            Assert.That(g.TiltDir, Is.EqualTo(0));
        }

        [Test, Timeout(8000)]
        public void TheBotRescuesABunnyAndTheScoreCountsThirtyPlusFuel()
        {
            var g = new RocketRescueGame("easy", 11);
            var aboard = 0;
            var saved = 0;
            g.BunnyAboard += () => aboard++;
            g.Rescued_ += _ => saved++;
            Run(g, RocketRescueGame.IntroSec + 0.02);
            FlyBot(g, 70.0);
            Assert.That(aboard, Is.GreaterThan(0), "the PD bot picks up a stranded bunny");
            Assert.That(saved, Is.GreaterThan(0), "then delivers it to the station");
            Assert.That(g.Engine.Rescued, Is.GreaterThanOrEqualTo(1));
            Assert.That(g.Score, Is.GreaterThanOrEqualTo(30));
            Assert.That(g.Score, Is.EqualTo(RocketRescue.RoundScore(g.Engine.Rescued, g.Engine.Fuel, g.Engine.SoftLandings, g.Tune)));
        }

        [Test]
        public void AHardDropOnThePadBouncesAndBurnsTenFuel()
        {
            var g = new RocketRescueGame("normal", 3);
            var hard = 0;
            g.HardLanded += () => hard++;
            Run(g, RocketRescueGame.IntroSec + 0.02);
            g.Engine.LandedOn = null;
            g.Engine.DepartedFrom = null;
            g.Engine.X = g.Engine.Layout.PadX;
            g.Engine.Y = 0.03;
            g.Engine.Vy = -3.0;
            g.Engine.Vx = 0.0;
            var fuel = g.Engine.Fuel;
            g.Tick(Dt);
            Assert.That(hard, Is.EqualTo(1));
            Assert.That(g.Engine.HardLandings, Is.EqualTo(1));
            Assert.That(g.Engine.Fuel, Is.EqualTo(fuel - g.Tune.HardFuelPenalty).Within(1e-9));
            Assert.That(g.Engine.Vy, Is.GreaterThan(0.0), "bounce restitution kicks the lander up");
            Assert.That(g.Finished, Is.False, "a hard landing is never death");
        }

        [Test]
        public void AnEmptyTankAwayFromThePadTowsHomeAndEndsTheRound()
        {
            var g = new RocketRescueGame("normal", 3);
            var towed = 0;
            g.OutOfFuel += () => towed++;
            Run(g, RocketRescueGame.IntroSec + 0.02);
            g.Engine.LandedOn = null;
            g.Engine.X = 3.0;
            g.Engine.Y = 4.0;
            g.Engine.Fuel = 0.0;
            g.Tick(Dt);
            Assert.That(towed, Is.EqualTo(1));
            Assert.That(g.Engine.Towing, Is.True);
            Run(g, 4.0);
            Assert.That(g.Finished, Is.True);
            Assert.That(g.Engine.EndReason, Is.EqualTo("fuel"));
            Assert.That(g.Engine.X, Is.EqualTo(g.Engine.Layout.PadX).Within(0.05));
        }

        [Test]
        public void SittingOnThePadTheClockRunsOutAtOneHundredTwentySeconds()
        {
            var g = new RocketRescueGame("normal", 3);
            Run(g, RocketRescueGame.IntroSec + 0.02);
            Assert.That(g.Engine.LandedOn, Is.EqualTo(RocketRescue.PadId));
            Run(g, 121.0);
            Assert.That(g.Finished, Is.True);
            Assert.That(g.Engine.EndReason, Is.EqualTo("time"));
            Assert.That(g.Engine.Elapsed, Is.GreaterThanOrEqualTo(g.Tune.DurationSec));
        }

        [Test]
        public void EndlessDoesNotEndOnTheClock()
        {
            var g = new RocketRescueGame("endless", 3);
            Run(g, RocketRescueGame.IntroSec + 125.0);
            Assert.That(g.Tune.Endless, Is.True);
            Assert.That(g.Finished, Is.False);
            Assert.That(g.Engine.Elapsed, Is.GreaterThan(120.0));
        }
    }
}
