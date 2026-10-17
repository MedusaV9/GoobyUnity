using System.Collections.Generic;
using Gooby.Core.Minigames.Games;
using Gooby.Core.Minigames.Play;
using NUnit.Framework;

namespace Gooby.Tests.Core
{
    /// <summary>The realtime Blasen-Platzer round (port of bubble_pop.gd) over the certified BubblePop rules.</summary>
    public class BubblePopGameTests
    {
        private const double Dt = 1.0 / 60.0;

        private static void Skip(BubblePopGame g, double seconds) { for (var s = 0.0; s < seconds; s += Dt) g.Tick(Dt); }

        /// <summary>A sharp player: pops every target bubble the moment it is inside the view, never touches the rest.</summary>
        private static int RunSharpPlayer(string mode, int seed)
        {
            var g = new BubblePopGame(mode, seed);
            var steps = 0;
            while (!g.Finished && steps++ < 60 * 400)
            {
                g.Tick(Dt);
                if (g.Stunned) continue;
                foreach (var b in new List<BubblePopGame.Bubble>(g.Bubbles))
                    if (b.Kind == "food" && b.Food == g.Target && b.Y > -g.HalfH) { g.Tap(b.X, b.Y); break; }
            }
            Assert.That(g.Finished, Is.True, "round ends");
            return g.FinalScore;
        }

        [Test]
        public void TimedRoundEndsAtDurationAndIsDeterministic()
        {
            var a = RunSharpPlayer("normal", 11);
            var b = RunSharpPlayer("normal", 11);
            Assert.That(a, Is.EqualTo(b));
            Assert.That(a, Is.GreaterThan(20), "a sharp player scores well over the coin floor");
            var g = new BubblePopGame("normal", 3);
            Skip(g, 70.0);
            Assert.That(g.Finished, Is.True);
            Assert.That(g.Elapsed, Is.GreaterThanOrEqualTo(g.Tune.DurationSec));
            Assert.That(g.FinalScore, Is.EqualTo(g.Score));
        }

        [Test]
        public void IntroFreezesTheSimThenBubblesRiseFromBelowAndLeaveAbove()
        {
            var g = new BubblePopGame("normal", 5);
            Skip(g, 1.0);
            Assert.That(g.Elapsed, Is.EqualTo(0.0));
            Assert.That(g.Bubbles, Is.Empty, "nothing spawns during the intro");
            Skip(g, 1.5);
            Assert.That(g.Bubbles.Count, Is.GreaterThan(0), "first bubble 0.9 s after the intro (spawn cadence)");
            var first = g.Bubbles[0];
            Assert.That(first.Y, Is.LessThan(-g.HalfH + 1.0), "enters below the bottom edge");
            Assert.That(System.Math.Abs(first.X), Is.LessThanOrEqualTo(g.HalfW - 0.6));
            var y0 = first.Y;
            Skip(g, 1.0);
            Assert.That(first.Y - y0, Is.EqualTo(BubblePop.RiseSpeedAt(1.0, g.Tune.DurationSec, g.Tune)).Within(0.05), "rises at the ramped speed");
            Skip(g, 20.0);
            Assert.That(g.Bubbles, Has.No.Member(first), "gone above the top");
            foreach (var b in g.Bubbles) Assert.That(b.Y, Is.LessThanOrEqualTo(g.HalfH + 0.7));
        }

        [Test]
        public void PopsScoreByTheRulesAndAWrongBubbleStuns()
        {
            var g = new BubblePopGame("normal", 5);
            Skip(g, BubblePopGame.IntroSec + 0.01);
            var target = g.Target;
            var other = target == "apple" ? "banana" : "apple";
            g.Bubbles.Clear();
            g.Bubbles.Add(new BubblePopGame.Bubble { Kind = "food", Food = target, X = 0.0, Y = 0.0 });
            g.Bubbles.Add(new BubblePopGame.Bubble { Kind = "food", Food = other, X = 1.5, Y = 0.0 });
            g.Bubbles.Add(new BubblePopGame.Bubble { Kind = "spiky", Food = "", X = -1.5, Y = 0.0 });
            var events = new List<string>();
            g.Popped += (b, r, d) => events.Add(r + ":" + d);

            Assert.That(g.Tap(3.0, 3.0), Is.Null, "empty water");
            Assert.That(g.Tap(0.3, 0.2), Is.EqualTo("match"), "inside the food touch radius");
            Assert.That(g.Score, Is.EqualTo(2));
            Assert.That(g.Streak, Is.EqualTo(1));
            Assert.That(g.Bubbles.Count, Is.EqualTo(2), "a popped bubble is gone");
            Assert.That(g.Tap(-1.5, 0.55), Is.EqualTo("spiky"), "spikes reach further than the body");
            Assert.That(g.Score, Is.EqualTo(1));
            Assert.That(g.SpikyPops, Is.EqualTo(1));
            Assert.That(g.Bubbles.Count, Is.EqualTo(2), "spiky bubbles never pop");
            Assert.That(g.Streak, Is.EqualTo(0));
            Assert.That(g.Tap(1.5, 0.0), Is.EqualTo("wrong"));
            Assert.That(g.Score, Is.EqualTo(0), "never below zero");
            Assert.That(g.Stunned, Is.True);
            g.Bubbles.Add(new BubblePopGame.Bubble { Kind = "food", Food = target, X = 0.0, Y = 0.0 });
            Assert.That(g.Tap(0.0, 0.0), Is.Null, "stunned: taps do nothing");
            Skip(g, g.Tune.StunSec + 0.05);
            Assert.That(g.Stunned, Is.False);
            Assert.That(events, Is.EqualTo(new[] { "match:2", "spiky:-1", "wrong:-1" }));
        }

        [Test]
        public void ThreeOfAKindInTheWindowBurstTheNeighboursAsAChain()
        {
            var g = new BubblePopGame("normal", 9);
            Skip(g, BubblePopGame.IntroSec + 0.01);
            var target = g.Target;
            var other = target == "cheese" ? "carrot" : "cheese";
            g.Bubbles.Clear();
            for (var i = 0; i < 3; i++) g.Bubbles.Add(new BubblePopGame.Bubble { Kind = "food", Food = target, X = i * 1.2 - 1.2, Y = 0.0 });
            // neighbours of the third pop: two of the target within the chain radius, one other food, one too far
            g.Bubbles.Add(new BubblePopGame.Bubble { Kind = "food", Food = target, X = 1.2 + 0.9, Y = 0.4 });
            g.Bubbles.Add(new BubblePopGame.Bubble { Kind = "food", Food = target, X = 1.2, Y = -1.0 });
            g.Bubbles.Add(new BubblePopGame.Bubble { Kind = "food", Food = other, X = 1.2 + 0.5, Y = 0.5 });
            g.Bubbles.Add(new BubblePopGame.Bubble { Kind = "food", Food = target, X = 1.2 + 2.0, Y = 0.0 });
            var chains = new List<int>();
            g.Chain += (b, n) => chains.Add(n);
            Assert.That(g.Tap(-1.2, 0.0), Is.EqualTo("match"));
            Assert.That(g.Tap(0.0, 0.0), Is.EqualTo("match"));
            Assert.That(chains, Is.Empty, "two in a row are not a chain yet");
            Assert.That(g.Tap(1.2, 0.0), Is.EqualTo("match"));
            Assert.That(chains, Is.EqualTo(new[] { 2 }), "the third pop bursts the two target neighbours within 1.25");
            Assert.That(g.Score, Is.EqualTo(3 * 2 + 2 * 2), "MatchPts for each burst bubble too");
            Assert.That(g.Chains, Is.EqualTo(1));
            Assert.That(g.Bubbles.Count, Is.EqualTo(2), "the other food and the far one stay");

            // the window: three pops spread over more than ChainWindowSec do not chain
            var h = new BubblePopGame("normal", 9);
            Skip(h, BubblePopGame.IntroSec + 0.01);
            var t2 = h.Target;
            h.Chain += (b, n) => chains.Add(100 + n);
            for (var i = 0; i < 3; i++)
            {
                h.Bubbles.Clear();
                h.Bubbles.Add(new BubblePopGame.Bubble { Kind = "food", Food = t2, X = 0.0, Y = 0.0 });
                Assert.That(h.Tap(0.0, 0.0), Is.EqualTo("match"));
                Skip(h, h.Tune.ChainWindowSec + 0.2);
            }
            Assert.That(chains, Is.EqualTo(new[] { 2 }));
        }

        [Test]
        public void TheTargetRotatesEveryTwelveSecondsAndTheStreakCelebratesEveryFifth()
        {
            var g = new BubblePopGame("normal", 21);
            Skip(g, BubblePopGame.IntroSec + 0.01);
            var first = g.Target;
            Assert.That(BubblePop.Foods, Does.Contain(first));
            Skip(g, g.Tune.TargetRotateSec);
            Assert.That(g.Target, Is.Not.EqualTo(first), "a new food after the rotation");
            var milestones = new List<int>();
            g.StreakMilestone += n => milestones.Add(n);
            for (var i = 0; i < 10; i++)
            {
                g.Bubbles.Clear();
                g.Bubbles.Add(new BubblePopGame.Bubble { Kind = "food", Food = g.Target, X = 0.0, Y = 0.0 });
                Assert.That(g.Tap(0.0, 0.0), Is.EqualTo("match"));
            }
            Assert.That(milestones, Is.EqualTo(new[] { 5, 10 }));
        }

        [Test]
        public void EndlessEndsOnTheThirdSpikyBubbleAndTapsMapFromTheView()
        {
            var g = new BubblePopGame("endless", 4);
            Assert.That(g.Tune.Endless, Is.True);
            Skip(g, BubblePopGame.IntroSec + 0.01);
            g.SetAspect(390.0 / 844.0);
            Assert.That(g.HalfW, Is.EqualTo(BubblePopGame.WorldHalfH * 390.0 / 844.0).Within(1e-9));
            g.Bubbles.Clear();
            g.Bubbles.Add(new BubblePopGame.Bubble { Kind = "spiky", Food = "", X = 0.0, Y = 0.0 });
            // the middle of the view is the world origin; the top-left corner is (−HalfW, +HalfH)
            Assert.That(g.TapNormalized(0.5, 0.5), Is.EqualTo("spiky"));
            Assert.That(g.TapNormalized(0.0, 0.0), Is.Null);
            g.Bubbles.Add(new BubblePopGame.Bubble { Kind = "spiky", Food = "", X = -g.HalfW, Y = g.HalfH });
            Assert.That(g.TapNormalized(0.0, 0.0), Is.EqualTo("spiky"));
            Assert.That(g.Finished, Is.False);
            Assert.That(g.TapNormalized(0.5, 0.5), Is.EqualTo("spiky"));
            g.Tick(Dt);
            Assert.That(g.SpikyPops, Is.EqualTo(3));
            Assert.That(g.Finished, Is.True, "three spiky pops end an endless round");
        }
    }
}
