using System;
using System.Collections.Generic;
using Gooby.Core.Minigames.Games;
using Gooby.Core.Minigames.Play;
using NUnit.Framework;

namespace Gooby.Tests.EditMode.Core
{
    public class BasketBounceGameTests
    {
        private static BasketBounceGame Started(string mode = "normal", int seed = 7)
        {
            var g = new BasketBounceGame(mode, seed);
            g.Tick(BasketBounceGame.IntroSec + 0.01);
            return g;
        }

        private static void Run(BasketBounceGame g, double seconds, double dt = 1.0 / 60.0)
        {
            for (var t = 0.0; t < seconds && !g.Finished; t += dt) g.Tick(dt);
        }

        /// <summary>Ticks until the ball has landed and the reset has passed — the game is aiming again (or over).</summary>
        private static void WaitForNextShot(BasketBounceGame g)
        {
            for (var i = 0; i < 2000 && !g.Finished && !g.Aiming; i++) g.Tick(1.0 / 60.0);
        }

        [Test]
        public void TheIntroHoldsTheBallOnTheSpawnSpotAndTheHoopStartsFivePointTwoMetresOut()
        {
            var g = new BasketBounceGame("normal", 7);
            Assert.That(g.Aiming, Is.False, "not before the intro is over");
            g.Tick(1.0);
            Assert.That(g.Elapsed, Is.EqualTo(0.0));
            g.Tick(0.6);
            Assert.That(g.Aiming, Is.True);
            Assert.That(g.Phase, Is.EqualTo(BasketBounceGame.PhaseAim));
            Assert.That(g.BallWorld(), Is.EqualTo((g.Tune.SpawnX, g.Tune.SpawnY, g.Tune.SpawnZ)));
            var hoop = g.HoopNow();
            Assert.That(hoop.X, Is.EqualTo(0.0));
            Assert.That(g.Tune.SpawnZ - hoop.Z, Is.EqualTo(5.2).Within(1e-9));
            Assert.That(g.MovingHoop, Is.False);
        }

        [Test]
        public void AGentleDragIsNoThrowAndAnUpwardFlickLaunchesTheBall()
        {
            var g = Started();
            g.BeginDrag(195, 700);
            for (var i = 1; i <= 6; i++) { g.Tick(1.0 / 60.0); g.Drag(195, 700 - i * 2); }   // 120 px/s — far under the 320 px/s minimum
            Assert.That(g.EndDrag(), Is.False);
            Assert.That(g.Phase, Is.EqualTo(BasketBounceGame.PhaseAim));
            Assert.That(g.Dragging, Is.False);

            BasketBounce.Vel launched = default;
            g.Launched += v => launched = v;
            g.BeginDrag(195, 700);
            for (var i = 1; i <= 6; i++) { g.Tick(1.0 / 60.0); g.Drag(195 + i * 2, 700 - i * 22); }   // ~1320 px/s upward
            var (fx, fy) = g.FlickVelocity();
            Assert.That(fy, Is.LessThan(-1000.0), "the flick goes up the view");
            Assert.That(fx, Is.GreaterThan(0.0));
            Assert.That(g.EndDrag(), Is.True);
            Assert.That(g.Phase, Is.EqualTo(BasketBounceGame.PhaseFly));
            Assert.That(launched.Valid, Is.True);
            Assert.That(launched.Y, Is.GreaterThan(0.0), "up");
            Assert.That(launched.Z, Is.LessThan(0.0), "and towards the hoop");
            Assert.That(launched.X, Is.GreaterThan(0.0), "a little to the right, as the finger went");
            Assert.That(Math.Sqrt(launched.X * launched.X + launched.Y * launched.Y + launched.Z * launched.Z), Is.LessThanOrEqualTo(g.Tune.FlickMaxSpeed + 1e-9));
            g.Tick(0.1);
            Assert.That(g.Ball.Py, Is.GreaterThan(g.Tune.SpawnY), "the ball climbs");
            Assert.That(g.Trail.Count, Is.GreaterThan(0));
            g.BeginDrag(0, 0);
            Assert.That(g.Dragging, Is.False, "no new drag while the ball flies");
        }

        [Test]
        public void OnASlowDeviceWithOneSampleInTheWindowTheStretchToThePreviousSampleStillThrows()
        {
            var g = Started();
            g.BeginDrag(195, 700);
            g.Tick(0.2); g.Drag(195, 560);      // frames 0.2 s apart: the 0.13 s window holds the last sample alone
            g.Tick(0.2); g.Drag(195, 420);
            var (_, fy) = g.FlickVelocity();
            Assert.That(fy, Is.EqualTo(-700.0).Within(1e-6), "140 px up over the last 0.2 s");
            Assert.That(g.EndDrag(), Is.True);
            Assert.That(g.Phase, Is.EqualTo(BasketBounceGame.PhaseFly));
        }

        [Test]
        public void ASolvedShotDropsInPaysThreeAndBacksTheHoopOffByThirtyFiveCentimetres()
        {
            var g = Started();
            var scored = new List<(int points, bool swish, bool bank)>();
            g.Scored += (p, s, b) => scored.Add((p, s, b));
            var distBefore = g.HoopDistance;
            Assert.That(g.ShootAtHoop(), Is.True);
            Assert.That(g.Phase, Is.EqualTo(BasketBounceGame.PhaseFly));
            WaitForNextShot(g);
            Assert.That(g.Baskets, Is.EqualTo(1));
            Assert.That(g.Shots, Is.EqualTo(1));
            Assert.That(scored.Count, Is.EqualTo(1));
            Assert.That(scored[0].points, Is.GreaterThanOrEqualTo(3));
            if (scored[0].bank) Assert.That(scored[0].points, Is.EqualTo(5)); else Assert.That(scored[0].points, Is.EqualTo(3), "the first swish alone is no streak yet");
            Assert.That(g.Score, Is.EqualTo(scored[0].points));
            Assert.That(g.HoopDistance - distBefore, Is.EqualTo(0.35).Within(1e-9));
            Assert.That(g.MissStreak, Is.EqualTo(0));
            Assert.That(g.Aiming, Is.True, "back on the spot after the reset");
        }

        [Test]
        public void TheBallRestsOnTheSpawnSpotForTheResetBeatBeforeTheNextThrow()
        {
            var g = Started();
            Assert.That(g.ShootAtHoop(), Is.True);
            for (var i = 0; i < 2000 && g.Phase == BasketBounceGame.PhaseFly; i++) g.Tick(1.0 / 60.0);
            Assert.That(g.Phase, Is.EqualTo(BasketBounceGame.PhaseReset));
            Assert.That(g.ResetLeft, Is.EqualTo(g.Tune.ShotResetSec).Within(1e-9));
            Assert.That(g.BallWorld(), Is.EqualTo((g.Tune.SpawnX, g.Tune.SpawnY, g.Tune.SpawnZ)));
            Assert.That(g.ShootAtHoop(), Is.False, "no throw during the reset");
            Run(g, g.Tune.ShotResetSec + 0.05);
            Assert.That(g.Aiming, Is.True);
        }

        [Test]
        public void AThrowStraightUpFallsToTheFloorAsAMissAndThreeInARowEndTheEndlessRound()
        {
            var g = Started("endless", 3);
            Assert.That(g.Tune.Endless, Is.True);
            var misses = 0;
            g.MissedShot += () => misses++;
            for (var shot = 0; shot < 3; shot++)
            {
                Assert.That(g.Shoot(new BasketBounce.Vel { X = 0.0, Y = 4.0, Z = 0.0, Valid = true }), Is.True);
                for (var i = 0; i < 2000 && g.Phase == BasketBounceGame.PhaseFly; i++) g.Tick(1.0 / 60.0);
                Assert.That(misses, Is.EqualTo(shot + 1));
                Assert.That(g.MissStreak, Is.EqualTo(shot + 1));
                WaitForNextShot(g);
            }
            Assert.That(g.Finished, Is.True, "three misses in a row end the endless round");
            Assert.That(g.Score, Is.EqualTo(0));
            Assert.That(g.FinalScore, Is.EqualTo(0));
        }

        [Test]
        public void TheQaBotThrowsABeatAfterTheResetScoresAndFallsShortEveryFifthThrow()
        {
            var g = Started("normal", 5);
            var made = 0; var missed = 0;
            g.Scored += (p, swish, bank) => made++;
            g.MissedShot += () => missed++;
            g.ApplyBot();
            Assert.That(g.Phase, Is.EqualTo(BasketBounceGame.PhaseAim), "it waits a beat on the spot first");
            for (var t = 0.0; t < 30.0 && !g.Finished; t += 1.0 / 60.0)
            {
                g.ApplyBot();
                g.Tick(1.0 / 60.0);
            }
            Assert.That(g.Shots, Is.GreaterThanOrEqualTo(10));
            Assert.That(made, Is.GreaterThanOrEqualTo(g.Shots * 3 / 5), "the solved throws drop in");
            Assert.That(missed, Is.GreaterThanOrEqualTo(g.Shots / 5), "every fifth throw is short");
            Assert.That(g.Baskets, Is.EqualTo(made));
        }

        [Test]
        public void TheTimedRoundEndsAfterSixtySecondsWhateverTheMissStreak()
        {
            var g = Started();
            var finished = false;
            g.Finished_ += () => finished = true;
            Run(g, 59.0);
            Assert.That(g.Finished, Is.False);
            Assert.That(g.TimeLeft, Is.EqualTo(1.0).Within(0.05));
            Run(g, 1.2);
            Assert.That(finished, Is.True);
            Assert.That(g.Finished, Is.True);
            Assert.That(g.EndDrag(), Is.False);
            Assert.That(g.ShootAtHoop(), Is.False);
        }

        [Test]
        public void TenBasketsSetTheHoopSlidingAndAMovingSwishPaysDouble()
        {
            var g = Started("easy", 11);   // the easy gate is wider, so the solver's shots keep dropping
            for (var shot = 0; shot < 40 && g.Baskets < 10 && !g.Finished; shot++)
            {
                if (!g.ShootAtHoop()) break;
                WaitForNextShot(g);
            }
            Assume.That(g.Baskets, Is.GreaterThanOrEqualTo(10), "the solver sinks ten within the round");
            Assert.That(g.MovingHoop, Is.True);
            var x0 = g.HoopNow().X;
            Run(g, g.Tune.SlidePeriodSec * 0.25);
            Assert.That(Math.Abs(g.HoopNow().X - x0), Is.GreaterThan(0.3), "the hoop slides sideways");
            Assert.That(Math.Abs(g.HoopNow().X), Is.LessThanOrEqualTo(g.Tune.SlideAmplitude + 1e-9));
            // ScoreShot itself: a swish streak on the moving hoop is doubled
            var (points, streak) = BasketBounce.ScoreShot(true, true, false, 1, true, g.Tune);
            Assert.That(streak, Is.EqualTo(2));
            Assert.That(points, Is.EqualTo((3 + 2) * 2));
        }
    }
}
