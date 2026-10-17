using System;
using Gooby.Core.Minigames.Games;
using Gooby.Core.Minigames.Play;
using Gooby.Core.Random;
using NUnit.Framework;

namespace Gooby.Tests.EditMode.Core
{
    public class DeliveryRushGameTests
    {
        private const double Dt = 1.0 / 60.0;

        private static void Run(DeliveryRushGame g, double seconds)
        {
            for (var t = 0.0; t < seconds && !g.Finished; t += Dt) g.Tick(Dt);
        }

        private static void FlyBot(DeliveryRushGame g, double seconds)
        {
            for (var t = 0.0; t < seconds && !g.Finished; t += Dt)
            {
                g.ApplyBot();
                g.Tick(Dt);
            }
        }

        [Test]
        public void SetupDealsThreeTargetsBeforeTheIntroAndGatesSteer()
        {
            var ids = new System.Collections.Generic.List<string>();
            foreach (var lm in DeliveryRush.Landmarks) ids.Add(lm.Id);
            var expected = DeliveryRush.PickDeliveries(new GoobyRng(7), ids);
            var g = new DeliveryRushGame("normal", 7);
            Assert.That(g.Targets.Count, Is.EqualTo(3));
            Assert.That(g.Targets[0], Is.EqualTo(expected[0]), "the opener is the first seed draws");
            Assert.That(g.Targets[1], Is.EqualTo(expected[1]));
            Assert.That(g.DropPoints.Count, Is.EqualTo(3));
            Assert.That(g.SetSteer(1.0), Is.False, "intro gates steering");
            Assert.That(g.ApplyBot(), Is.False);
            Run(g, 1.0);
            Assert.That(g.Elapsed, Is.EqualTo(0.0), "the clock waits out the intro");
            Assert.That(g.Drops, Is.EqualTo(0));
        }

        [Test]
        public void AfterTheIntroTheVanRollsAndSteerTurnsIt()
        {
            var g = new DeliveryRushGame("normal", 3);
            Run(g, DeliveryRushGame.IntroSec + 0.02);
            var x0 = g.VanX;
            var z0 = g.VanZ;
            Assert.That(g.SetSteer(0.0), Is.True);
            Run(g, 0.4);
            Assert.That(g.Speed, Is.GreaterThan(1.0));
            Assert.That(Math.Abs(g.VanX - x0) + Math.Abs(g.VanZ - z0), Is.GreaterThan(0.5));
            var heading = g.Heading;
            Assert.That(g.SetSteer(1.0), Is.True);
            Run(g, 0.3);
            Assert.That(g.Heading, Is.Not.EqualTo(heading));
        }

        [Test]
        public void SameSeedDealsTheSameOpenerTargets()
        {
            var a = new DeliveryRushGame("normal", 11);
            var b = new DeliveryRushGame("normal", 11);
            Assert.That(a.Targets, Is.EqualTo(b.Targets));
            Assert.That(a.FragileIndex, Is.EqualTo(b.FragileIndex));
            Assert.That(a.DropPoints[0].x, Is.EqualTo(b.DropPoints[0].x).Within(1e-12));
        }

        [Test, Timeout(12000)]
        public void TheBotDeliversTheOpenerThenTheTimeBonusClosesTheRound()
        {
            var g = new DeliveryRushGame("easy", 11);
            var drops = 0;
            g.Dropped += _ => drops++;
            Run(g, DeliveryRushGame.IntroSec + 0.02);
            FlyBot(g, 90.0);
            Assert.That(g.Drops + (g.Finished ? g.Tune.Parcels : 0), Is.GreaterThanOrEqualTo(1), "the autopilot finds at least one ring");
            Assert.That(g.Score, Is.GreaterThan(0), "a drop is +50, crashes may nibble it");
            if (!g.Finished) FlyBot(g, 90.0);
            Assert.That(g.Finished, Is.True, "three drops plus the time bonus end the round");
            Assert.That(g.FinalScore, Is.EqualTo(g.Score));
            Assert.That(g.SetSteer(1.0), Is.False);
            Assert.That(drops, Is.GreaterThanOrEqualTo(1));
        }

        [Test]
        public void EasyIsSlowerAndHardIsDenser()
        {
            var easy = new DeliveryRushGame("easy", 5);
            var hard = new DeliveryRushGame("hard", 5);
            Assert.That(easy.Tune.SpeedMult, Is.LessThan(hard.Tune.SpeedMult));
            Assert.That(hard.Tune.TrafficDensityMult, Is.GreaterThan(easy.Tune.TrafficDensityMult));
            Assert.That(easy.Tune.CrashAllowance, Is.EqualTo(1));
            Assert.That(DeliveryRush.ApplyDrop(0), Is.EqualTo(50));
            Assert.That(DeliveryRush.ApplyCrash(12), Is.EqualTo(7));
            Assert.That(DeliveryRush.TimeBonus(40.0, DeliveryRush.Delivery), Is.EqualTo(80));
            Assert.That(DeliveryRush.SegmentHitsDrop(0, 0, 3, 0, 4, 0, 4.0), Is.True);
        }

        [Test]
        public void ACrashCoolsDownAndNeverDropsTheScoreBelowZero()
        {
            var g = new DeliveryRushGame("normal", 3);
            Run(g, DeliveryRushGame.IntroSec + 0.02);
            Assert.That(DeliveryRush.ApplyCrash(3), Is.EqualTo(0));
            var st = DeliveryRush.CreateEndlessState(3);
            Assert.That(DeliveryRush.RecordExpiry(st), Is.False);
            Assert.That(st.Expired, Is.EqualTo(1));
            DeliveryRush.RecordExpiry(st);
            Assert.That(DeliveryRush.RecordExpiry(st), Is.True);
        }

        [Test]
        public void CrossingTheRingCountsADrop()
        {
            var g = new DeliveryRushGame("normal", 3);
            Run(g, DeliveryRushGame.IntroSec + 0.02);
            var drop = g.CurrentDrop;
            Assert.That(DeliveryRush.SegmentHitsDrop(drop.x - 5, drop.z, drop.x + 5, drop.z, drop.x, drop.z, g.Tune.DropRadiusM), Is.True);
            Assert.That(g.Targets.Count, Is.EqualTo(3));
            CollectionAssert.AllItemsAreUnique(g.Targets);
        }
    }
}
