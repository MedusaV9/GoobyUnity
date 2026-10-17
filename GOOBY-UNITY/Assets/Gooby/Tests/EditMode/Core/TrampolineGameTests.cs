using System;
using System.Collections.Generic;
using Gooby.Core.Minigames.Games;
using Gooby.Core.Minigames.Play;
using NUnit.Framework;

namespace Gooby.Tests.EditMode.Core
{
    public class TrampolineGameTests
    {
        private const double Dt = 1.0 / 120.0;

        private static TrampolineGame Started(string mode = "normal")
        {
            var g = new TrampolineGame(mode, 1);
            g.Tick(TrampolineGame.IntroSec + 0.01);
            return g;
        }

        private static void Run(TrampolineGame g, double seconds)
        {
            for (var t = 0.0; t < seconds && !g.Finished; t += Dt) g.Tick(Dt);
        }

        /// <summary>Ticks until Gooby has left the mat again after the next landing (a new flight has started).</summary>
        private static void UntilNextLaunch(TrampolineGame g)
        {
            var landings = 0;
            g.Landed += _ => landings++;
            var butts = 0;
            g.ButtLanded += () => butts++;
            for (var i = 0; i < 20000 && landings == 0 && butts == 0 && !g.Finished; i++) g.Tick(Dt);
        }

        /// <summary>Falls until the landing window opens, taps, and rides the bounce to the next launch.</summary>
        private static void TapInWindow(TrampolineGame g)
        {
            for (var i = 0; i < 20000 && !g.InWindow && !g.Finished; i++) g.Tick(Dt);
            Assert.That(g.Tap(), Is.EqualTo("boost"));
            UntilNextLaunch(g);
        }

        [Test]
        public void GoobyStartsOnTheBaseBounceAndTheIntroHoldsTheClock()
        {
            var g = new TrampolineGame("normal", 1);
            Assert.That(g.Vy, Is.EqualTo(5.0));
            Assert.That(g.Apex, Is.EqualTo(25.0 / 18.0).Within(1e-9), "v²/2g");
            Assert.That(g.Multiplier, Is.EqualTo(1));
            g.Tick(1.0);
            Assert.That(g.Elapsed, Is.EqualTo(0.0));
            Assert.That(g.Height, Is.EqualTo(0.0));
            g.Tick(0.6);
            Assert.That(g.Running, Is.True);
            g.Tick(0.2);
            Assert.That(g.Height, Is.GreaterThan(0.5), "airborne right away");
            Assert.That(g.Tap(), Is.EqualTo("ignore"), "no tap counts while rising");
        }

        [Test]
        public void WithoutATapTheBounceDecaysToTheFloorSpeed()
        {
            var g = Started();
            var landings = new List<double>();
            g.Landed += v => landings.Add(v);
            Run(g, 12.0);
            Assert.That(landings.Count, Is.GreaterThanOrEqualTo(8));
            Assert.That(g.Vy <= 4.2 + 1e-9 || g.Apex <= Trampoline.ApexFor(4.2, 9.0) + 1e-6, Is.True, "decayed to MinVy");
            Assert.That(g.Failures, Is.EqualTo(0), "not tapping is no failure");
            Assert.That(g.Score, Is.EqualTo(0), "bounces alone score nothing");
        }

        [Test]
        public void ATapInsideTheWindowBoostsTheNextBounceAndTapsClimbTheHeightTiers()
        {
            var g = Started();
            var boosts = 0;
            g.Boosted += () => boosts++;
            var tiers = new List<int>();
            g.TierUp += t => tiers.Add(t);
            var vyBefore = g.Vy;
            TapInWindow(g);
            Assert.That(boosts, Is.EqualTo(1));
            Assert.That(g.Vy, Is.EqualTo(Math.Min(8.6, vyBefore * 1.16 + 0.35)).Within(0.05), "the impact speed of the integrated fall, boosted");
            for (var i = 0; i < 12 && g.Vy < 8.6 - 1e-6; i++) TapInWindow(g);
            Assert.That(g.Vy, Is.EqualTo(8.6).Within(1e-6), "capped at MaxVy");
            Assert.That(g.Multiplier, Is.EqualTo(3), "8.6²/18 = 4.1 m — tier 3");
            Assert.That(tiers, Is.EqualTo(new[] { 2, 3 }));
            Assert.That(g.WindowSec, Is.LessThan(g.Tune.WindowBaseSec), "the window shrinks with height");
            Assert.That(g.WindowSec, Is.GreaterThanOrEqualTo(g.Tune.WindowMinSec));
        }

        [Test]
        public void ATapTooEarlyInTheJudgeZoneIsAButtLandingWithAStaggerAndTheBaseBounceAfter()
        {
            var g = Started();
            TapInWindow(g); TapInWindow(g);
            Assume.That(g.Vy, Is.GreaterThan(5.5));
            var butts = 0;
            g.ButtLanded += () => butts++;
            // fall until inside the judge zone but still before the window
            for (var i = 0; i < 20000 && !(g.Vy < 0 && g.TimeToImpact <= g.Tune.JudgeZoneSec) && !g.Finished; i++) g.Tick(Dt);
            Assert.That(g.TimeToImpact, Is.GreaterThan(g.WindowSec));
            Assert.That(g.ZoneProgress, Is.GreaterThan(0.0));
            Assert.That(g.Tap(), Is.EqualTo("butt"));
            UntilNextLaunch(g);
            Assert.That(butts, Is.EqualTo(1));
            Assert.That(g.Failures, Is.EqualTo(1));
            Assert.That(g.Staggering, Is.True);
            Assert.That(g.Airborne, Is.False);
            Assert.That(g.Height, Is.EqualTo(0.0));
            Assert.That(g.Tap(), Is.EqualTo("ignore"), "nothing while sitting on the mat");
            Assert.That(g.Swipe(80, 0), Is.Null);
            Run(g, g.Tune.ButtStaggerSec + 0.05);
            Assert.That(g.Staggering, Is.False);
            Assert.That(g.Airborne, Is.True);
            Assert.That(g.Vy, Is.LessThanOrEqualTo(5.0), "back to the base bounce");
            Assert.That(g.Multiplier, Is.EqualTo(1));
        }

        [Test]
        public void SwipesInTheAirAreTricksWorthTheirPointsTimesTheTierAndAllThreeKindsPayTheCombo()
        {
            var g = Started();
            var tricks = new List<(string kind, int points)>();
            g.Tricked += (k, p) => tricks.Add((k, p));
            var combo = 0;
            g.Combo += b => combo += b;
            // a few boosts first: a long flight leaves room for three tricks
            for (var i = 0; i < 4; i++) TapInWindow(g);
            Run(g, 0.1);
            var m = g.Multiplier;
            Assert.That(g.TimeToImpact, Is.GreaterThan(1.0));
            Assert.That(g.Swipe(-90, 4), Is.EqualTo("flip"));
            Assert.That(tricks[0], Is.EqualTo(("flip", 2 * m)));
            Assert.That(g.Swipe(90, 0), Is.Null, "no second trick while the first still spins (0.3 s)");
            Run(g, TrampolineGame.TrickSec + 0.01);
            Assert.That(g.Swipe(90, 0), Is.EqualTo("spin"));
            Run(g, TrampolineGame.TrickSec + 0.01);
            Assert.That(g.Swipe(0, -120), Is.EqualTo("twist"));
            Assert.That(tricks[2], Is.EqualTo(("twist", 3 * m)));
            Assert.That(combo, Is.EqualTo(12), "flip + spin + twist in one flight");
            Assert.That(g.Score, Is.EqualTo((2 + 2 + 3) * m + 12));
            Run(g, TrampolineGame.TrickSec + 0.01);
            var before = g.Score;
            if (g.Airborne && g.TimeToImpact > g.Tune.TrickMinAirSec)
            {
                Assert.That(g.Swipe(-50, 0), Is.EqualTo("flip"));
                Assert.That(g.Score, Is.EqualTo(before + 2 * m), "a repeat kind pays its points but no second combo");
            }
            Assert.That(TrampolineGame.TrickKindFor(-10, 3), Is.EqualTo("flip"));
            Assert.That(TrampolineGame.TrickKindFor(10, -3), Is.EqualTo("spin"));
            Assert.That(TrampolineGame.TrickKindFor(3, 40), Is.EqualTo("twist"));
        }

        [Test]
        public void TricksHighUpPayTripleAndNoneAreAllowedJustBeforeTheMat()
        {
            var g = Started();
            for (var i = 0; i < 12 && g.Multiplier < 3; i++) TapInWindow(g);
            Assume.That(g.Multiplier, Is.EqualTo(3));
            Run(g, 0.05);
            Assert.That(g.Swipe(0, 80), Is.EqualTo("twist"));
            Assert.That(g.Score, Is.EqualTo(9), "3 × tier 3");
            // fall to just above the mat: no trick there
            for (var i = 0; i < 20000 && !(g.Vy < 0 && g.TimeToImpact <= g.Tune.TrickMinAirSec) && !g.Finished; i++) g.Tick(Dt);
            Run(g, TrampolineGame.TrickSec);
            Assert.That(g.Swipe(80, 0), Is.Null);
        }

        [Test]
        public void TheTimedRoundEndsAtSixtySecondsAndTheEndlessOneAfterThreeButtLandings()
        {
            var g = Started();
            var finished = false;
            g.Finished_ += () => finished = true;
            Run(g, 59.0);
            Assert.That(g.Finished, Is.False);
            Run(g, 1.2);
            Assert.That(finished, Is.True);
            Assert.That(g.Swipe(80, 0), Is.Null);
            Assert.That(g.Tap(), Is.EqualTo("ignore"));

            var e = Started("endless");
            Assert.That(e.Tune.Endless, Is.True);
            for (var n = 0; n < 3; n++)
            {
                for (var i = 0; i < 20000 && !(e.Vy < 0 && e.TimeToImpact <= e.Tune.JudgeZoneSec && e.TimeToImpact > e.WindowSec) && !e.Finished; i++) e.Tick(Dt);
                Assert.That(e.Tap(), Is.EqualTo("butt"));
                UntilNextLaunch(e);
                Run(e, e.Tune.ButtStaggerSec + 0.05);
            }
            Assert.That(e.Failures, Is.EqualTo(3));
            Assert.That(e.Finished, Is.True);
        }
    }
}
