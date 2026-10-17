using System;
using System.Collections.Generic;
using System.Linq;
using Gooby.Core.Minigames.Play;
using NUnit.Framework;

namespace Gooby.Tests.EditMode.Core
{
    public class VeggieChopGameTests
    {
        private static VeggieChopGame Started(string mode = "normal", int seed = 3)
        {
            var g = new VeggieChopGame(mode, seed);
            g.Tick(VeggieChopGame.IntroSec + 0.01);
            return g;
        }

        private static void Run(VeggieChopGame g, double seconds, double dt = 1.0 / 60.0)
        {
            for (var t = 0.0; t < seconds && !g.Finished; t += dt) g.Tick(dt);
        }

        /// <summary>A short vertical swipe right through the piece where it is now — the way a finger crosses a flying veggie.</summary>
        private static int SwipeThrough(VeggieChopGame g, VeggieChopGame.Flying e)
        {
            g.BeginSwipe();
            return g.Cut(e.X, e.Y - 0.3, e.X, e.Y + 0.3);
        }

        /// <summary>Cuts every veggie in the air (junk too when asked) — skipped while dizzy, so the knife retries next tick.</summary>
        private static void CutAll(VeggieChopGame g, bool junkToo)
        {
            foreach (var e in g.Items.ToList())
            {
                if (g.Stunned || g.Finished) return;
                if (e.Item.Kind == "veggie" || junkToo) SwipeThrough(g, e);
            }
        }

        [Test]
        public void TheQaBotSlicesVeggiesHighInTheirArcAndNeverSmashesJunk()
        {
            var g = new VeggieChopGame("normal", 12);
            g.SetAspect(390.0 / 844.0);
            var chopped = 0;
            g.Chopped += (e, pts, combo) => { chopped++; Assert.That(e.Item.Kind, Is.Not.EqualTo("junk")); };
            for (var t = 0.0; t < 40.0 && !g.Finished; t += 1.0 / 60.0)
            {
                g.ApplyBot();
                g.Tick(1.0 / 60.0);
            }
            Assert.That(chopped, Is.GreaterThanOrEqualTo(20));
            Assert.That(g.JunkHits, Is.EqualTo(0));
            Assert.That(g.Misses, Is.LessThanOrEqualTo(1), "nearly every veggie is caught on the way up");
        }

        [Test]
        public void TheIntroHoldsTheThrowsAndThenTheFirstWaveFliesUpFromBelowTheGround()
        {
            var g = new VeggieChopGame("normal", 3);
            g.Tick(1.0);
            Assert.That(g.Items, Is.Empty);
            Assert.That(g.Elapsed, Is.EqualTo(0.0));
            g.Tick(0.6);
            Assert.That(g.Items, Is.Empty, "the tick that ends the intro throws nothing yet");
            g.Tick(1.0 / 60.0);
            Assert.That(g.Items.Count, Is.GreaterThanOrEqualTo(1));
            foreach (var e in g.Items)
            {
                Assert.That(e.Y, Is.LessThan(VeggieChopGame.GroundY + 0.5), "throws start from the pan below the ground");
                Assert.That(e.Arc.Vy, Is.GreaterThan(0.0), "and fly upwards");
                Assert.That(Math.Abs(e.X), Is.LessThanOrEqualTo(g.HalfW));
            }
        }

        [Test]
        public void TheViewMappingRoundTripsAndPutsTheGroundAtTheBottomEdge()
        {
            var g = Started();
            var (gx, gy) = g.ToView(0.0, VeggieChopGame.GroundY);
            Assert.That(gx, Is.EqualTo(0.5).Within(1e-9));
            Assert.That(gy, Is.EqualTo(1.0).Within(1e-9));
            var (wx, wy) = g.ToWorld(0.25, 0.4);
            var (bx, by) = g.ToView(wx, wy);
            Assert.That(bx, Is.EqualTo(0.25).Within(1e-9));
            Assert.That(by, Is.EqualTo(0.4).Within(1e-9));
        }

        [Test]
        public void ASwipeThroughAVeggiePaysTwoAndEveryFurtherPieceInTheSameSwipePaysThree()
        {
            var g = Started();
            Run(g, 0.4);
            var veggies = g.Items.Where(e => e.Item.Kind == "veggie").ToList();
            Assume.That(veggies.Count, Is.GreaterThanOrEqualTo(1));
            var got = new List<(int points, int combo)>();
            g.Chopped += (e, p, c) => got.Add((p, c));
            var first = veggies[0];
            Assert.That(SwipeThrough(g, first), Is.EqualTo(1));
            Assert.That(got[0].points, Is.EqualTo(2));
            Assert.That(got[0].combo, Is.EqualTo(1));
            Assert.That(g.Score, Is.EqualTo(2));
            Assert.That(g.Items, Has.No.Member(first));
            if (veggies.Count > 1)
            {
                var second = veggies[1];
                Assert.That(g.Cut(second.X - 1.0, second.Y, second.X + 1.0, second.Y), Is.EqualTo(1), "still the same swipe — no BeginSwipe in between");
                Assert.That(got[1].points, Is.EqualTo(3));
                Assert.That(got[1].combo, Is.EqualTo(2));
                Assert.That(g.Score, Is.EqualTo(5));
            }
        }

        [Test]
        public void ANewSwipeStartsTheComboOverAndAStrokeThatMissesEverythingCutsNothing()
        {
            var g = Started();
            Run(g, 0.4);
            var v = g.Items.First(e => e.Item.Kind == "veggie");
            SwipeThrough(g, v);
            Assert.That(g.SwipeCombo, Is.EqualTo(1));
            g.BeginSwipe();
            Assert.That(g.SwipeCombo, Is.EqualTo(0));
            Assert.That(g.Cut(-g.HalfW, VeggieChopGame.HalfH - 0.05, g.HalfW, VeggieChopGame.HalfH - 0.05), Is.EqualTo(0), "the very top edge is empty right after the throw");
            Assert.That(g.Cut(0.0, 0.0, 0.0, 0.0), Is.EqualTo(0), "a zero-length stroke cuts nothing");
        }

        [Test]
        public void JunkCostsThreeStunsForHalfASecondAndTheStunnedKnifeCutsNothing()
        {
            var g = Started();
            var junk = (VeggieChopGame.Flying)null;
            for (var i = 0; i < 4000 && junk == null && !g.Finished; i++)
            {
                g.Tick(1.0 / 60.0);
                junk = g.Items.FirstOrDefault(e => e.Item.Kind == "junk");
                if (junk == null) CutAll(g, false);   // keep the round alive (and bank points) until the first can or boot flies
            }
            Assert.That(junk, Is.Not.Null, "junk shows up within the first minute");
            Assert.That(g.Score, Is.GreaterThan(3), "points banked on the way");
            var before = g.Score;
            var junkHits = 0;
            g.JunkHit += _ => junkHits++;
            Assert.That(SwipeThrough(g, junk), Is.EqualTo(1));
            Assert.That(junkHits, Is.EqualTo(1));
            Assert.That(g.JunkHits, Is.EqualTo(1));
            Assert.That(g.Score, Is.EqualTo(Math.Max(0, before - 3)));
            Assert.That(g.SwipeCombo, Is.EqualTo(0), "junk breaks the swipe combo");
            Assert.That(g.Stunned, Is.True);
            Assert.That(g.StunUntil - g.Elapsed, Is.EqualTo(0.5).Within(1e-9));
            Assert.That(g.Cut(-g.HalfW, 0.0, g.HalfW, 0.0), Is.EqualTo(0), "no cutting while dizzy");
            Run(g, 0.55);
            Assert.That(g.Stunned, Is.False);
        }

        [Test]
        public void AVeggieThatFallsBackUncutIsAMissAndThreeMissesEndTheRoundEarly()
        {
            var g = Started();
            var left = new List<int>();
            g.Missed += n => left.Add(n);
            Run(g, 12.0);
            Assert.That(g.Finished, Is.True, "nobody swipes, three veggies fall back within seconds");
            Assert.That(g.Misses, Is.EqualTo(3));
            Assert.That(left, Is.EqualTo(new[] { 2, 1, 0 }));
            Assert.That(g.Elapsed, Is.LessThan(20.0));
            Assert.That(g.FinalScore, Is.EqualTo(0));
        }

        [Test]
        public void JunkThatFallsBackIsNotAMiss()
        {
            var g = Started();
            var missesFromJunk = 0;
            for (var i = 0; i < 6000 && !g.Finished; i++)
            {
                var before = g.Misses;
                var junkBefore = g.Items.Where(e => e.Item.Kind == "junk").ToList();
                g.Tick(1.0 / 60.0);
                var vanished = junkBefore.Where(j => !g.Items.Contains(j)).ToList();
                if (vanished.Count > 0 && g.Misses > before) missesFromJunk++;
                // cut every veggie as it appears so only junk ever falls
                CutAll(g, false);
            }
            Assert.That(missesFromJunk, Is.EqualTo(0));
            Assert.That(g.Misses, Is.EqualTo(0));
        }

        [Test]
        public void EveryTwentyFiveSecondsAFrenzyThrowsExactlyEightVeggiesAndNoJunk()
        {
            var g = Started();
            var frenzies = 0;
            g.Frenzy += () => frenzies++;
            var thrownDuringFrenzy = new List<VeggieChopGame.Flying>();
            var seen = new HashSet<VeggieChopGame.Flying>();
            for (var t = 0.0; t < 30.0 && !g.Finished; t += 1.0 / 60.0)
            {
                g.Tick(1.0 / 60.0);
                foreach (var e in g.Items)
                {
                    if (seen.Add(e) && g.Elapsed >= 25.0 && g.Elapsed <= 25.0 + 3.0 + 0.05) thrownDuringFrenzy.Add(e);
                }
                CutAll(g, false);
            }
            Assert.That(frenzies, Is.EqualTo(1));
            Assert.That(g.Frenzies, Is.EqualTo(1));
            Assert.That(thrownDuringFrenzy.Count, Is.GreaterThanOrEqualTo(8));
            Assert.That(thrownDuringFrenzy.Take(8).All(e => e.Item.Kind == "veggie"), Is.True);
        }

        [Test]
        public void ASteadyHandFinishesTheMinuteWithAScoreAndTheEndlessModeStopsAtThreeJunkHits()
        {
            var g = Started();
            for (var t = 0.0; t < 70.0 && !g.Finished; t += 1.0 / 60.0)
            {
                g.Tick(1.0 / 60.0);
                CutAll(g, false);
            }
            Assert.That(g.Finished, Is.True);
            Assert.That(g.Misses, Is.EqualTo(0));
            Assert.That(g.Elapsed, Is.GreaterThanOrEqualTo(60.0));
            Assert.That(g.Score, Is.GreaterThan(60));
            Assert.That(g.FinalScore, Is.EqualTo(g.Score));

            var endless = Started("endless", 5);
            Assert.That(endless.Tune.Endless, Is.True);
            for (var t = 0.0; t < 240.0 && !endless.Finished; t += 1.0 / 60.0)
            {
                endless.Tick(1.0 / 60.0);
                CutAll(endless, true);
            }
            Assert.That(endless.Finished, Is.True);
            Assert.That(endless.JunkHits, Is.EqualTo(3));
            Assert.That(endless.Misses, Is.LessThan(3));
        }

        [Test]
        public void TheMovingCircleTestCatchesAPieceThatJumpedAcrossTheStrokeBetweenTwoFrames()
        {
            Assert.That(VeggieChopGame.SegmentHitsMovingCircle(-1.0, 0.0, 1.0, 0.0, 0.0, -2.0, 0.0, 2.0, 0.1), Is.True, "its path crossed the stroke");
            Assert.That(VeggieChopGame.SegmentHitsMovingCircle(-1.0, 0.0, 1.0, 0.0, 0.0, 0.3, 0.0, 0.5, 0.42), Is.True, "within the radius");
            Assert.That(VeggieChopGame.SegmentHitsMovingCircle(-1.0, 0.0, 1.0, 0.0, 3.0, 0.0, 3.5, 0.0, 0.42), Is.False, "well to the side");
        }
    }
}
