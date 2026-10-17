using System;
using System.Collections.Generic;
using System.Linq;
using Gooby.Core.Minigames.Games;
using Gooby.Core.Minigames.Play;
using NUnit.Framework;

namespace Gooby.Tests.EditMode.Core
{
    public class DancePartyGameTests
    {
        private const double Dt = 1.0 / 120.0;

        private static void RunTo(DancePartyGame g, double songTime)
        {
            while (g.SongTime < songTime - 1e-9 && !g.Finished) g.Tick(Math.Min(Dt, songTime - g.SongTime));
        }

        /// <summary>Plays the chart: taps every note at the given offset before its time.</summary>
        private static void PlayUntil(DancePartyGame g, double untilSongTime, double offsetSec)
        {
            while (g.SongTime < untilSongTime && !g.Finished)
            {
                var next = g.Notes.Skip(g.Head).FirstOrDefault(n => !n.Hit && !n.Missed);
                if (next == null || next.Time - offsetSec > untilSongTime) { RunTo(g, untilSongTime); break; }
                RunTo(g, next.Time - offsetSec);
                g.Tap(next.Lane);
            }
        }

        [Test]
        public void TheChartIsSeededStartsAfterTheLeadInAndScrollsDownToTheLine()
        {
            var g = new DancePartyGame("normal");
            Assert.That(g.SongTime, Is.EqualTo(-2.4));
            Assert.That(g.InLeadIn, Is.True);
            Assert.That(g.IntroProgress, Is.EqualTo(0.0));
            Assert.That(g.Notes.Count, Is.GreaterThan(40));
            Assert.That(g.Notes.All(n => n.Lane >= 0 && n.Lane < 3), Is.True);
            Assert.That(g.Notes.Zip(g.Notes.Skip(1), (a, b) => b.Time - a.Time).All(gap => gap >= g.Tune.MinGapSec - 1e-9), Is.True, "global minimum gap");
            var other = new DancePartyGame("normal");
            Assert.That(other.Notes.Select(n => (n.Time, n.Lane)), Is.EqualTo(g.Notes.Select(n => (n.Time, n.Lane))), "the same chart every time");
            Assert.That(g.Visible().Any(), Is.False, "nothing in sight during the lead-in");
            RunTo(g, 0.0);
            Assert.That(g.IntroProgress, Is.EqualTo(1.0).Within(1e-9));
            var first = g.Notes[0];
            RunTo(g, first.Time - 0.8);
            var (note, progress) = g.Visible().First();
            Assert.That(note, Is.SameAs(first));
            Assert.That(progress, Is.EqualTo(0.5).Within(1e-6), "half way down the 1.6 s travel");
        }

        [Test]
        public void ATapOnTheBeatIsPerfectALateOneGoodAndTheJudgementsScore()
        {
            var g = new DancePartyGame("normal");
            var judged = new List<string>();
            g.Judged += (k, l, t) => judged.Add(k);
            var first = g.Notes[0];
            RunTo(g, first.Time - 0.03);
            Assert.That(g.Tap(first.Lane), Is.EqualTo("perfect"));
            Assert.That(g.Score, Is.EqualTo(4));
            Assert.That(g.Combo, Is.EqualTo(1));
            Assert.That(first.Hit, Is.True);
            var second = g.Notes[1];
            RunTo(g, second.Time + 0.1);
            Assert.That(g.Tap(second.Lane), Is.EqualTo("good"), "100 ms late is still good");
            Assert.That(g.Score, Is.EqualTo(6));
            Assert.That(g.Combo, Is.EqualTo(2));
            Assert.That(judged, Is.EqualTo(new[] { "perfect", "good" }));
            Assert.That(g.Tap(second.Lane), Is.Null, "nothing left in reach on that lane");
        }

        [Test]
        public void ANoteThatPassesTheLineIsAMissThatCostsTwoAndBreaksTheCombo()
        {
            var g = new DancePartyGame("normal");
            var judged = new List<(string kind, int lane)>();
            g.Judged += (k, l, t) => judged.Add((k, l));
            var first = g.Notes[0];
            RunTo(g, first.Time - 0.03);
            g.Tap(first.Lane);
            var second = g.Notes[1];
            RunTo(g, second.Time + 0.15);
            Assert.That(second.Missed, Is.True);
            Assert.That(judged.Last(), Is.EqualTo(("miss", second.Lane)));
            Assert.That(g.Tally.Miss, Is.EqualTo(1));
            Assert.That(g.Combo, Is.EqualTo(0));
            Assert.That(g.Score, Is.EqualTo(2), "4 − 2, never below zero");
            Assert.That(g.Head, Is.EqualTo(2));
        }

        [Test]
        public void ATapOnAnEmptyLaneIsNoTapAtAllAndOneInsideTheWindowOnTheWrongLaneNeither()
        {
            var g = new DancePartyGame("normal");
            var first = g.Notes[0];
            RunTo(g, first.Time - 0.03);
            Assert.That(g.Tap((first.Lane + 1) % 3), Is.Null);
            Assert.That(g.Score, Is.EqualTo(0));
            Assert.That(first.Hit, Is.False);
            RunTo(g, first.Time - 0.5);
        }

        [Test]
        public void FivePerfectsAtTierThreeStartTheEncoreWhichDoublesTheNextFiveSecondsOfHits()
        {
            var g = new DancePartyGame("normal");
            var encores = 0;
            g.EncoreStarted += () => encores++;
            // perfect everything: combo 16 reaches tier 3, five more perfects start the encore
            PlayUntil(g, 30.0, 0.01);
            Assert.That(g.Tally.Miss, Is.EqualTo(0));
            Assert.That(g.Tier, Is.EqualTo(3));
            Assert.That(encores, Is.GreaterThanOrEqualTo(1));
            Assert.That(g.Fever.Encores, Is.EqualTo(encores));
            Assert.That(g.Tally.Bonus, Is.GreaterThan(0), "the doubled hits banked bonus points");
            Assert.That(g.Score, Is.EqualTo(g.Tally.Perfect * 4 + g.Tally.Good * 2 + g.Tally.Bonus));
        }

        [Test]
        public void TheRoundEndsAfterSeventyFiveSecondsWithTheTallyAsScore()
        {
            var g = new DancePartyGame("normal");
            var finished = false;
            g.Finished_ += () => finished = true;
            PlayUntil(g, 80.0, 0.02);
            Assert.That(finished, Is.True);
            Assert.That(g.SongTime, Is.GreaterThanOrEqualTo(75.0));
            Assert.That(g.FinalScore, Is.EqualTo(g.Score));
            Assert.That(g.Score, Is.GreaterThan(100));
            Assert.That(g.Tap(0), Is.Null);
        }

        [Test]
        public void EndlessAppendsChartSegmentsAndEndsAfterThreeSectionsWithAMiss()
        {
            var g = new DancePartyGame("endless");
            Assert.That(g.Tune.Endless, Is.True);
            var initial = g.Notes.Count;
            // nobody taps: every 12-s section has misses, three sections end it
            for (var i = 0; i < 20000 && !g.Finished; i++) g.Tick(Dt);
            Assert.That(g.Finished, Is.True);
            Assert.That(g.Breaks, Is.EqualTo(3));
            Assert.That(g.SongTime, Is.EqualTo(36.0).Within(0.05), "the third broken section closes at 36 s");

            var h = new DancePartyGame("endless");
            PlayUntil(h, 76.0, 0.01);
            Assert.That(h.Finished, Is.False, "clean play goes on past the 75-s chart");
            Assert.That(h.Notes.Count, Is.GreaterThan(initial), "a second segment was appended");
            Assert.That(h.Notes.Last().Time, Is.GreaterThan(75.0));
        }

        [Test]
        public void TheEasyChartHasWiderWindowsAndTheHardOneTighter()
        {
            var easy = new DancePartyGame("easy");
            var hard = new DancePartyGame("hard");
            Assert.That(easy.Tune.PerfectMs, Is.GreaterThan(DanceParty.Tuning.PerfectMs));
            Assert.That(hard.Tune.GoodMs, Is.LessThan(DanceParty.Tuning.GoodMs));
            Assert.That(easy.Tune.Endless, Is.False);
        }
    }
}
