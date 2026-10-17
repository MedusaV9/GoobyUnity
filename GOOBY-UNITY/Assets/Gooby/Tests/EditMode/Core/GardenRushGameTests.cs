using System;
using System.Collections.Generic;
using System.Linq;
using Gooby.Core.Minigames.Games;
using Gooby.Core.Minigames.Play;
using NUnit.Framework;

namespace Gooby.Tests.EditMode.Core
{
    public class GardenRushGameTests
    {
        private const double Dt = 1.0 / 60.0;

        private static void Run(GardenRushGame g, double seconds)
        {
            for (var t = 0.0; t < seconds - 1e-9 && !g.Finished; t += Dt) g.Tick(Dt);
        }

        private static int FirstPot(GardenRushGame g, string state) => g.Pots.FindIndex(p => p.State == state);

        /// <summary>Ticks until a pot of the wanted state shows up (or the round ends).</summary>
        private static int WaitFor(GardenRushGame g, string state, double patience = 30.0)
        {
            for (var t = 0.0; t < patience && !g.Finished; t += Dt)
            {
                var i = FirstPot(g, state);
                if (i >= 0) return i;
                g.Tick(Dt);
            }
            return -1;
        }

        [Test]
        public void SixPotsStartEmptyAndTheFirstSproutPopsUpAfterAThirdOfASecond()
        {
            var g = new GardenRushGame("normal", 3);
            Assert.That(g.Pots.Count, Is.EqualTo(8));
            Assert.That(g.ActivePots, Is.EqualTo(6));
            Assert.That(g.Pots.All(p => p.State == GardenRushGame.Empty), Is.True);
            var sprouted = new List<int>();
            g.Sprouted += i => sprouted.Add(i);
            Run(g, 0.3);
            Assert.That(sprouted, Is.Empty);
            Run(g, 0.1);
            Assert.That(sprouted.Count, Is.EqualTo(1));
            Assert.That(sprouted[0], Is.LessThan(6), "only the six active pots");
            var pot = g.Pots[sprouted[0]];
            Assert.That(pot.State, Is.EqualTo(GardenRushGame.Sprout), "no weeds before 12 s");
            Assert.That(pot.Window, Is.EqualTo(GardenRush.WiltWindowAt(g.Elapsed, 60.0, g.Tune)).Within(0.01), "the window of the spawn tick");
            Assert.That(pot.Fraction, Is.EqualTo(1.0).Within(0.02));
        }

        [Test]
        public void HoldingFillsTheRingAndLettingGoInTheLastQuarterWatersPerfectlyForThree()
        {
            var g = new GardenRushGame("normal", 3);
            var i = WaitFor(g, GardenRushGame.Sprout);
            Assume.That(i, Is.GreaterThanOrEqualTo(0));
            var watered = new List<(int pot, int points, bool perfect)>();
            g.Watered += (p, pts, perf) => watered.Add((p, pts, perf));
            Assert.That(g.Press(i), Is.True);
            Assert.That(g.Holding, Is.True);
            Assert.That(g.Press((i + 1) % 6), Is.False, "one hold at a time");
            Run(g, 0.4);
            Assert.That(g.HoldFraction, Is.EqualTo(0.5).Within(0.03));
            Assert.That(g.HoldInPerfectZone, Is.False);
            Run(g, 0.3);
            Assert.That(g.HoldInPerfectZone, Is.True, "0.7 of 0.8 s is inside the last quarter");
            g.Release();
            Assert.That(watered, Is.EqualTo(new[] { (i, 3, true) }));
            Assert.That(g.Score, Is.EqualTo(3));
            Assert.That(g.PerfectStreak, Is.EqualTo(1));
            Assert.That(g.Pots[i].State, Is.EqualTo(GardenRushGame.Cooldown));
            Run(g, GardenRushGame.WateredCooldown + 0.05);
            Assert.That(g.Pots[i].State, Is.EqualTo(GardenRushGame.Empty), "free again after the watered beat");
        }

        [Test]
        public void LettingGoEarlyIsOkayForOneAndBreaksThePerfectStreak()
        {
            var g = new GardenRushGame("normal", 3);
            var i = WaitFor(g, GardenRushGame.Sprout);
            Assume.That(i, Is.GreaterThanOrEqualTo(0));
            var watered = new List<(int pot, int points, bool perfect)>();
            g.Watered += (p, pts, perf) => watered.Add((p, pts, perf));
            g.Press(i);
            Run(g, 0.2);
            g.Release();
            Assert.That(watered, Is.EqualTo(new[] { (i, 1, false) }));
            Assert.That(g.Score, Is.EqualTo(1));
            Assert.That(g.PerfectStreak, Is.EqualTo(0));
            Assert.That(g.Holding, Is.False);
            g.Release();
            Assert.That(g.Score, Is.EqualTo(1), "a release without a hold does nothing");
        }

        [Test]
        public void ASproutNobodyWatersWiltsCostsTwoAndCountsTowardsTheEndlessLimit()
        {
            var g = new GardenRushGame("normal", 3);
            var i = WaitFor(g, GardenRushGame.Sprout);
            Assume.That(i, Is.GreaterThanOrEqualTo(0));
            // bank a few points first so the deduction shows
            g.Press(i); Run(g, 0.75); g.Release();
            Assume.That(g.Score, Is.EqualTo(3));
            var wilted = new List<int>();
            g.Wilted += p => wilted.Add(p);
            var j = WaitFor(g, GardenRushGame.Sprout);
            var window = g.Pots[j].Remaining;
            Run(g, window + 0.05);
            Assert.That(wilted, Is.EqualTo(new[] { j }));
            Assert.That(g.Withered, Is.EqualTo(1));
            Assert.That(g.Score, Is.EqualTo(1));
            Assert.That(g.Pots[j].State, Is.EqualTo(GardenRushGame.Cooldown));

            var e = new GardenRushGame("endless", 3);
            Assert.That(e.Tune.Endless, Is.True);
            Run(e, 40.0);
            Assert.That(e.Finished, Is.True, "three wilts end the endless round");
            Assert.That(e.Withered, Is.EqualTo(3));
        }

        [Test]
        public void HoldingAPotWhoseSproutWiltsUnderTheFingerDropsTheHold()
        {
            var g = new GardenRushGame("normal", 3);
            var i = WaitFor(g, GardenRushGame.Sprout);
            Assume.That(i, Is.GreaterThanOrEqualTo(0));
            var remaining = g.Pots[i].Remaining;
            Run(g, remaining - 0.3);
            Assert.That(g.Press(i), Is.True);
            Run(g, 0.4);
            Assert.That(g.Holding, Is.False, "the pot cleared, the hold with it");
            Assert.That(g.Withered, Is.EqualTo(1));
        }

        [Test]
        public void WeedsShowUpAfterTwelveSecondsAndWateringOneCostsOne()
        {
            var g = new GardenRushGame("normal", 7);
            var weedPot = -1;
            for (var t = 0.0; t < 60.0 && weedPot < 0 && !g.Finished; t += Dt)
            {
                g.Tick(Dt);
                // keep the round alive: water every sprout perfectly (weeds are left alone until one is found)
                var s = FirstPot(g, GardenRushGame.Sprout);
                if (s >= 0 && !g.Holding) { g.Press(s); Run(g, 0.7); g.Release(); }
                weedPot = FirstPot(g, GardenRushGame.Weed);
            }
            Assume.That(weedPot, Is.GreaterThanOrEqualTo(0), "a weed within the minute");
            Assert.That(g.Elapsed, Is.GreaterThanOrEqualTo(12.0));
            var weeds = 0;
            g.WeedWatered += _ => weeds++;
            var before = g.Score;
            Assert.That(g.Press(weedPot), Is.True);
            Run(g, 0.3);
            g.Release();
            Assert.That(weeds, Is.EqualTo(1));
            Assert.That(g.Score, Is.EqualTo(Math.Max(0, before - 1)));
            Assert.That(g.Pots[weedPot].Grown, Is.True);
            Assert.That(g.Pots[weedPot].Remaining, Is.LessThanOrEqualTo(1.2), "a watered weed withers away within 1.2 s");
            Assert.That(g.PerfectStreak, Is.EqualTo(0));
        }

        [Test]
        public void MorePotsOpenAtTwentyAndThirtyFiveSecondsAndTheSprinklerComesAtThirty()
        {
            var g = new GardenRushGame("normal", 3);
            var more = 0;
            g.MorePots += () => more++;
            var ready = 0;
            g.SprinklerReady += () => ready++;
            Run(g, 19.9);
            Assert.That(g.ActivePots, Is.EqualTo(6));
            Run(g, 0.2);
            Assert.That(g.ActivePots, Is.EqualTo(7));
            Assert.That(more, Is.EqualTo(1));
            Assert.That(g.SprinklerAvailable, Is.False);
            Assert.That(g.FireSprinkler(), Is.False);
            Run(g, 10.0);
            Assert.That(ready, Is.EqualTo(1));
            Assert.That(g.SprinklerAvailable, Is.True);
            // the sprinkler refills every sprout's window by half of it
            var sprouts = g.Pots.Where(p => p.State == GardenRushGame.Sprout).ToList();
            var before = sprouts.Select(p => p.Remaining).ToList();
            var refilled = -1;
            g.SprinklerFired += n => refilled = n;
            Assert.That(g.FireSprinkler(), Is.True);
            Assert.That(refilled, Is.EqualTo(sprouts.Count));
            for (var k = 0; k < sprouts.Count; k++)
                Assert.That(sprouts[k].Remaining, Is.EqualTo(Math.Min(sprouts[k].Window, before[k] + sprouts[k].Window * 0.5)).Within(1e-9));
            Assert.That(g.SprinklerAvailable, Is.False, "once per round");
            Assert.That(g.FireSprinkler(), Is.False);
            Run(g, 5.0);
            Assert.That(g.ActivePots, Is.EqualTo(8));
            Assert.That(more, Is.EqualTo(2));
        }

        [Test]
        public void TheRoundEndsAtSixtySecondsAndIgnoresTheFingerAfterwards()
        {
            var g = new GardenRushGame("normal", 3);
            var finished = false;
            g.Finished_ += () => finished = true;
            Run(g, 60.1);
            Assert.That(finished, Is.True);
            Assert.That(g.TimeLeft, Is.EqualTo(0.0));
            Assert.That(g.Press(0), Is.False);
            Assert.That(g.FinalScore, Is.EqualTo(g.Score));
        }
    }
}
