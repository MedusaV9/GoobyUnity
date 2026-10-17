using System;
using Gooby.Core.Minigames.Games;
using Gooby.Core.Minigames.Play;
using Gooby.Core.Random;
using NUnit.Framework;

namespace Gooby.Tests.EditMode.Core
{
    public class CityDriveGameTests
    {
        private const double Dt = 1.0 / 60.0;

        private static void Run(CityDriveGame g, double seconds)
        {
            for (var t = 0.0; t < seconds && !g.Finished; t += Dt) g.Tick(Dt);
        }

        private static void Bot(CityDriveGame g, double seconds)
        {
            for (var t = 0.0; t < seconds && !g.Finished; t += Dt)
            {
                g.ApplyBot();
                g.Tick(Dt);
            }
        }

        [Test]
        public void RulesMatchTheGodotNumbers()
        {
            var tune = CityDriveArcade.Arcade;
            Assert.That(CityDriveArcade.TargetSpeed(0.0, tune), Is.EqualTo(9.0));
            Assert.That(CityDriveArcade.TargetSpeed(20.0, tune), Is.EqualTo(9.0), "the ramp waits 20 s");
            Assert.That(CityDriveArcade.TargetSpeed(31.0, tune), Is.EqualTo(12.0).Within(1e-9));
            Assert.That(CityDriveArcade.TargetSpeed(60.0, tune), Is.EqualTo(15.0));
            Assert.That(CityDriveArcade.StepSpeed(0.0, 9.0, 1.0, tune), Is.EqualTo(5.5), "5.5 m/s² up");
            Assert.That(CityDriveArcade.StepSpeed(15.0, 9.0, 0.5, tune), Is.EqualTo(10.5), "9 m/s² down");
            Assert.That(CityDriveArcade.RoundScore(20, 4, 0, tune), Is.EqualTo(200 + 120 + 50));
            Assert.That(CityDriveArcade.RoundScore(20, 4, 1, tune), Is.EqualTo(320), "no zero-crash bonus after a crash");
            Assert.That(CityDriveArcade.RoundOver(89.9, tune), Is.False);
            Assert.That(CityDriveArcade.RoundOver(90.0, tune), Is.True);
            var endless = CityDriveArcade.ApplyDifficulty(tune, "endless");
            Assert.That(CityDriveArcade.RoundOver(500.0, endless), Is.False, "endless ends only with the third strike");
            Assert.That(CityDriveArcade.ApplyDifficulty(tune, "hard").SpeedMult, Is.EqualTo(1.2));
            Assert.That(CityDriveArcade.ApplyDifficulty(tune, "easy").TrafficDensityMult, Is.EqualTo(0.85));
        }

        [Test]
        public void TheTownIsARingWithACrossAndCoinsLandOnIt()
        {
            Assert.That(CityDriveArcade.IsRoad(1, 1), Is.True);
            Assert.That(CityDriveArcade.IsRoad(3, 2), Is.True, "cross street");
            Assert.That(CityDriveArcade.IsRoad(2, 2), Is.False, "a block");
            Assert.That(CityDriveArcade.IsRoad(0, 3), Is.False, "the rim");
            Assert.That(CityDriveArcade.LayoutColliders().Count, Is.EqualTo(4), "four blocks inside the ring");
            var coins = CityDriveArcade.ScatterCoins(new GoobyRng(11), 40);
            foreach (var c in coins)
            {
                var tile = CityDriveArcade.WorldToTile(c.x, c.z);
                Assert.That(CityDriveArcade.IsRoad(tile.r, tile.c), Is.True, c.ToString());
            }
            var cp = CityDriveArcade.NextCheckpoint(new GoobyRng(5), (0.0, 40.0));
            Assert.That(CityDriveArcade.Dist(cp, (0.0, 40.0)), Is.GreaterThanOrEqualTo(CityDriveArcade.CheckpointMinDistM));
        }

        [Test]
        public void CarStatsMapToClampedMultipliers()
        {
            Assert.That(CarStats.SpeedMult(3), Is.EqualTo(1.0));
            Assert.That(CarStats.SpeedMult(10), Is.EqualTo(1.21).Within(1e-9));
            Assert.That(CarStats.HandlingMult(1), Is.EqualTo(0.95).Within(1e-9));
            Assert.That(CarStats.BoostMult(99), Is.EqualTo(1.21).Within(1e-9), "stats clamp to 10");
            Assert.That(CarStats.SpeedMult(-5), Is.EqualTo(0.94).Within(1e-9), "stats clamp to 1");
        }

        [Test]
        public void TheIntroGatesTheClockAndSteeringThenTheCarRolls()
        {
            var g = new CityDriveGame("normal", 7);
            Assert.That(g.Coins.Count, Is.EqualTo(26));
            Assert.That(g.Traffic.Count, Is.EqualTo(5));
            Assert.That(g.SetSteer(1.0), Is.False, "intro gates steering");
            Run(g, 1.0);
            Assert.That(g.Elapsed, Is.EqualTo(0.0));
            Run(g, 0.6);
            Assert.That(g.SetSteer(0.0), Is.True);
            var x0 = g.X;
            Run(g, 1.0);
            Assert.That(g.Speed, Is.GreaterThan(3.0));
            Assert.That(g.X, Is.GreaterThan(x0 + 1.0), "the start heads east on the south ring");
            var h = g.Heading;
            g.SetSteer(-1.0);
            Run(g, 0.3);
            Assert.That(g.Heading, Is.LessThan(h), "left turns the nose left");
        }

        [Test]
        public void ThePilotCollectsCoinsAndTheTimerEndsTheRound()
        {
            var g = new CityDriveGame("easy", 3);
            var picked = 0;
            g.CoinPicked += (pts, x, z) => { Assert.That(pts, Is.EqualTo(10)); picked++; };
            Run(g, CityDriveGame.IntroSec + 0.01);
            Bot(g, 95.0);
            Assert.That(g.Finished, Is.True);
            Assert.That(picked, Is.GreaterThan(5), "the coin chaser collects coins");
            Assert.That(g.Coins.Count, Is.EqualTo(26), "collected coins respawn");
            Assert.That(g.FinalScore, Is.EqualTo(g.Pickups * 10 + g.Checkpoints * 30 + (g.Crashes == 0 ? 50 : 0)));
        }

        [Test]
        public void ThreeCrashesTowGoobyHome()
        {
            var g = new CityDriveGame("endless", 9);
            var towed = false;
            g.TowedHome += () => towed = true;
            Run(g, CityDriveGame.IntroSec + 0.01);
            var guard = 0;
            while (!g.Finished && guard++ < 20000)
            {
                // park the car in a traffic car's path: every grace period runs out into the next strike
                var car = g.Traffic[0];
                var p = CityDriveGame.TrafficPos(car);
                typeof(CityDriveGame).GetProperty("X").GetSetMethod(true).Invoke(g, new object[] { p.x });
                typeof(CityDriveGame).GetProperty("Z").GetSetMethod(true).Invoke(g, new object[] { p.z });
                g.Tick(Dt);
            }
            Assert.That(g.Finished, Is.True);
            Assert.That(towed, Is.True);
            Assert.That(g.Strikes, Is.EqualTo(3));
            Assert.That(g.Crashes, Is.EqualTo(3));
            Assert.That(g.FinalScore, Is.EqualTo(g.Score), "no zero-crash bonus on a tow");
        }

        [Test]
        public void SameSeedSameRound()
        {
            var a = new CityDriveGame("normal", 21);
            var b = new CityDriveGame("normal", 21);
            Run(a, CityDriveGame.IntroSec + 0.01);
            Run(b, CityDriveGame.IntroSec + 0.01);
            Bot(a, 30.0);
            Bot(b, 30.0);
            Assert.That(a.Score, Is.EqualTo(b.Score));
            Assert.That(a.X, Is.EqualTo(b.X));
            Assert.That(a.Checkpoint, Is.EqualTo(b.Checkpoint));
        }

        [Test]
        public void AutoplayModelIsMonotonicPerSeed()
        {
            for (var seed = 1; seed < 12; seed++)
            {
                var easy = (int)CityDriveArcade.SimulateAutoplay(seed, "easy")["score"];
                var normal = (int)CityDriveArcade.SimulateAutoplay(seed, "normal")["score"];
                var hard = (int)CityDriveArcade.SimulateAutoplay(seed, "hard")["score"];
                Assert.That(easy, Is.LessThan(normal), "seed " + seed);
                Assert.That(normal, Is.LessThan(hard), "seed " + seed);
            }
        }
    }
}
