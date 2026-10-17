using System;
using Gooby.Core.Minigames.Games;
using Gooby.Core.Minigames.Play;
using Gooby.Core.Random;
using NUnit.Framework;

namespace Gooby.Tests.EditMode.Core
{
    public class MiniGolfGameTests
    {
        private const double Dt = 1.0 / 60.0;

        private static void Run(MiniGolfGame g, double seconds)
        {
            for (var t = 0.0; t < seconds && !g.Finished; t += Dt) g.Tick(Dt);
        }

        private static void UntilStoppedOrHoled(MiniGolfGame g, double cap = 12.0)
        {
            for (var t = 0.0; t < cap && !g.Finished && g.Phase == "roll"; t += Dt) g.Tick(Dt);
        }

        /// <summary>Aim at the cup with the certified power-for-distance and wait the ball out.</summary>
        private static void Caddy(MiniGolfGame g)
        {
            if (g.Phase != "aim" || g.IntroLeft > 0.0) return;
            var h = g.Current;
            var dx = h.HoleAt.x - g.Ball.X;
            var dz = h.HoleAt.z - g.Ball.Z;
            var dist = Math.Sqrt(dx * dx + dz * dz);
            if (dist < 1e-6) return;
            var p = MiniGolf.PowerForDistance(dist);
            g.PuttWorld(dx / dist * p, dz / dist * p);
        }

        [Test]
        public void SetupRollsSixHolesAndTheIntroGatesThePuttAndTheMill()
        {
            var rng = new GoobyRng(7);
            var expected = MiniGolf.GenerateCourse(() => rng.Next(), MiniGolf.Golf);
            var g = new MiniGolfGame("normal", 7);
            Assert.That(g.Course.Count, Is.EqualTo(6));
            Assert.That(g.Course[0].Id, Is.EqualTo("straight"));
            Assert.That(g.Course[1].Id, Is.EqualTo(expected[1].Id));
            Assert.That(g.Course[1].Cells, Is.EqualTo(expected[1].Cells), "corner direction is the first seed draw");
            Assert.That(g.IntroLeft, Is.EqualTo(MiniGolfGame.IntroSec));
            Assert.That(g.PuttWorld(0.0, 4.0), Is.False, "intro gates the putt");
            Assert.That(g.Strokes, Is.EqualTo(0));
            var theta = g.Theta;
            Run(g, 1.0);
            Assert.That(g.Theta, Is.EqualTo(theta), "the mill waits out the intro");
            Assert.That(g.Phase, Is.EqualTo("aim"));
        }

        [Test, Timeout(4000)]
        public void ADownwardDragOnTheStraightHoleAcesForThirty()
        {
            var g = new MiniGolfGame("normal", 3);
            Run(g, MiniGolfGame.IntroSec + 0.05);
            Assert.That(g.Current.Id, Is.EqualTo("straight"));
            // drag 120 px down: pull.y = -120 → world +z, power from 120 px on a 390×844 view
            Assert.That(g.PuttPull(0.0, -120.0), Is.True);
            Assert.That(g.Strokes, Is.EqualTo(1));
            Assert.That(g.Phase, Is.EqualTo("roll"));
            UntilStoppedOrHoled(g);
            Assert.That(g.Ball.Done, Is.True, "a full 120 px pull covers the 3 m straight");
            Assert.That(g.Score, Is.EqualTo(30));
            Assert.That(g.Phase, Is.EqualTo("pause"));
            Assert.That(g.PuttWorld(0.0, 1.0), Is.False, "pause gates the next putt");
        }

        [Test, Timeout(4000)]
        public void ACaddyAcesTheOpenerThenThePauseServesTheCorner()
        {
            var g = new MiniGolfGame("normal", 11);
            var aces = 0;
            g.Holed += (_, ace) => { if (ace) aces++; };
            Run(g, MiniGolfGame.IntroSec + 0.02);
            Caddy(g);
            UntilStoppedOrHoled(g);
            Assert.That(aces, Is.EqualTo(1));
            Assert.That(g.Score, Is.EqualTo(MiniGolf.Golf.ScoreAce));
            Run(g, MiniGolfGame.HolePauseSec + 0.05);
            Assert.That(g.Phase, Is.EqualTo("aim"));
            Assert.That(g.HoleIndex, Is.EqualTo(1));
            Assert.That(g.Current.Id, Is.EqualTo("corner"));
            Assert.That(g.Strokes, Is.EqualTo(0));
        }

        [Test]
        public void ThetaAdvancesOnlyAfterTheIntroAndAShortPuttIsIgnored()
        {
            var g = new MiniGolfGame("normal", 5);
            Run(g, MiniGolfGame.IntroSec + 0.02);
            var before = g.Theta;
            Run(g, 0.5);
            Assert.That(g.Theta, Is.GreaterThan(before));
            Assert.That(g.Theta, Is.EqualTo(before + Math.PI * 2.0 * MiniGolf.Golf.WindmillRps * 0.5).Within(0.02));
            Assert.That(g.PuttPull(0.0, -4.0), Is.False, "under 8 px is a twitch, not a putt");
            Assert.That(g.Strokes, Is.EqualTo(0));
        }

        /// <summary>A sideways dribble into the rail: enough to count as a stroke, never long enough to reach the cup.</summary>
        private static void DribbleOut(MiniGolfGame g, int capTicks = 20000)
        {
            for (var i = 0; i < capTicks && !g.Finished && g.Phase != "pause"; i++)
            {
                if (g.Phase == "aim") Assert.That(g.PuttWorld(0.45, 0.0), Is.True);
                g.Tick(Dt);
            }
        }

        [Test, Timeout(8000)]
        public void TenStrokesWithoutAHoleGivesUpThenTheRoundEndsAfterSixHoles()
        {
            var g = new MiniGolfGame("normal", 9);
            var gave = 0;
            g.GaveUp += () => gave++;
            Run(g, MiniGolfGame.IntroSec + 0.02);
            for (var i = 0; i < 80000 && !g.Finished; i++)
            {
                if (g.Phase == "aim") g.PuttWorld(0.45, 0.0);
                g.Tick(Dt);
            }
            Assert.That(g.Finished, Is.True);
            Assert.That(gave, Is.EqualTo(6));
            Assert.That(g.Results.Count, Is.EqualTo(6));
            Assert.That(g.Score, Is.EqualTo(6 * MiniGolf.Golf.ScoreOther));
            Assert.That(g.FinalScore, Is.EqualTo(g.Score));
        }

        [Test, Timeout(8000)]
        public void EasyParIsAStrokeMoreAndEndlessEndsOnTheThirdOverPar()
        {
            var easy = new MiniGolfGame("easy", 1);
            Assert.That(easy.CurrentPar, Is.EqualTo(3), "straight par 2 plus the easy bonus");
            Assert.That(easy.Tune.HoleR, Is.GreaterThan(MiniGolf.Golf.HoleR));

            var endless = new MiniGolfGame("endless", 4);
            Assert.That(endless.Tune.Endless, Is.True);
            Run(endless, MiniGolfGame.IntroSec + 0.02);
            for (var n = 0; n < 3 && !endless.Finished; n++)
            {
                DribbleOut(endless);
                if (!endless.Finished) Run(endless, MiniGolfGame.HolePauseSec + 0.05);
            }
            Assert.That(endless.Finished, Is.True);
            Assert.That(endless.Endless.OverPar, Is.EqualTo(3));
        }

        [Test, Timeout(4000)]
        public void ABankOnTheStraightRailsFiresTheBankEvent()
        {
            var g = new MiniGolfGame("normal", 2);
            var banks = 0;
            g.BallEvent += tag => { if (tag == "bank") banks++; };
            Run(g, MiniGolfGame.IntroSec + 0.02);
            g.PuttWorld(4.0, 0.2); // hard into the right rail of the 1-cell-wide fairway
            UntilStoppedOrHoled(g, 4.0);
            Assert.That(banks, Is.GreaterThan(0));
        }
    }
}
