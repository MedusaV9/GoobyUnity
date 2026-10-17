using System.Collections.Generic;
using Gooby.Core.Minigames.Games;
using Gooby.Core.Minigames.Play;
using NUnit.Framework;

namespace Gooby.Tests.Core
{
    /// <summary>The realtime "Gooby sagt" round (port of gooby_says.gd) over the certified GoobySays rules.</summary>
    public class GoobySaysGameTests
    {
        private const double Dt = 1.0 / 60.0;

        private static void Skip(GoobySaysGame g, double seconds) { for (var s = 0.0; s < seconds; s += Dt) g.Tick(Dt); }

        /// <summary>Ticks until the player's turn opens (the playback of the round is over).</summary>
        private static void WatchThrough(GoobySaysGame g)
        {
            var guard = 0;
            while (g.Phase != GoobySaysGame.Input && guard++ < 60 * 60) g.Tick(Dt);
            Assert.That(g.Phase, Is.EqualTo(GoobySaysGame.Input), "the playback ends and the input opens");
        }

        /// <summary>Taps the round's sequence back correctly (chords as two quick taps).</summary>
        private static void TapBack(GoobySaysGame g)
        {
            var steps = new List<GoobySays.Step>(g.Sequence);
            foreach (var step in steps)
            {
                if (step.IsChord) { Assert.That(g.Tap(step.First), Is.EqualTo("chord-open")); Skip(g, 0.05); Assert.That(g.Tap(step.Second), Is.EqualTo("ok")); }
                else Assert.That(g.Tap(step.First), Is.EqualTo("ok"));
                Skip(g, 0.3);
            }
        }

        [Test]
        public void TheFirstRoundPlaysItsPadThenOpensTheInputAndARightAnswerGrowsTheSequence()
        {
            var g = new GoobySaysGame("normal", 7);
            Assert.That(g.RoundNo, Is.EqualTo(1));
            Assert.That(g.Sequence.Count, Is.EqualTo(1), "gooby_says.gd grows the sequence from one step (the autoplay sim's SeqLengthAt is the web's count)");
            Assert.That(g.Phase, Is.EqualTo(GoobySaysGame.Watch));
            Assert.That(g.Tap(0), Is.Null, "no taps while Gooby plays");
            var lit = new List<int>();
            g.PadLit += (pad, partner) => lit.Add(pad);
            var opened = 0;
            g.InputOpen += () => opened++;
            Skip(g, GoobySaysGame.IntroSec + 0.01);
            Assert.That(lit, Is.Empty, "the intro beat waits");
            WatchThrough(g);
            Assert.That(lit.Count, Is.EqualTo(1), "every step of the sequence lit its pad");
            Assert.That(lit[0], Is.EqualTo(g.Sequence[0].First));
            Assert.That(opened, Is.EqualTo(1));
            Assert.That(g.CurrentStep, Is.Not.Null);
            var won = new List<int>();
            g.RoundWon += n => won.Add(n);
            TapBack(g);
            Assert.That(won, Is.EqualTo(new[] { 1 }));
            Assert.That(g.RoundsCompleted, Is.EqualTo(1));
            Assert.That(g.RoundNo, Is.EqualTo(2));
            Assert.That(g.Sequence.Count, Is.EqualTo(2), "one more step per round");
            Assert.That(g.Phase, Is.EqualTo(GoobySaysGame.Watch), "Gooby plays the longer sequence");
            Assert.That(g.LiveScore, Is.EqualTo(GoobySays.RoundScore(1, g.AvgReactionMs, g.Tune)));
            Assert.That(g.LiveScore, Is.GreaterThanOrEqualTo(10));
        }

        [Test]
        public void AWrongPadEndsTheRoundWithTheScoreOfTheCompletedRounds()
        {
            var g = new GoobySaysGame("normal", 3);
            WatchThrough(g);
            TapBack(g);
            WatchThrough(g);
            var failed = 0;
            g.Failed += () => failed++;
            var wrong = (g.Sequence[0].First + 1) % g.Tune.Pads;
            Assert.That(g.Tap(wrong), Is.EqualTo("fail"));
            Assert.That(failed, Is.EqualTo(1));
            Assert.That(g.Finished, Is.True);
            Assert.That(g.FinalScore, Is.EqualTo(GoobySays.RoundScore(1, g.AvgReactionMs, g.Tune)));
            Assert.That(g.FinalScore, Is.GreaterThanOrEqualTo(10).And.LessThanOrEqualTo(18), "10 for the round plus a speed bonus of at most 8");
            Assert.That(g.Tap(0), Is.Null, "over");
        }

        [Test]
        public void HesitatingFiveSecondsIsAMistakeAndTheUrgencyRisesBeforeIt()
        {
            var g = new GoobySaysGame("normal", 5);
            WatchThrough(g);
            Assert.That(g.TimeoutUrgency, Is.EqualTo(0.0));
            Skip(g, g.Tune.InputTimeoutMs / 1000.0 * 0.8);
            Assert.That(g.TimeoutUrgency, Is.GreaterThan(0.3).And.LessThan(0.7), "the rim starts pulsing after 60 % of the window");
            Assert.That(g.Finished, Is.False);
            Skip(g, g.Tune.InputTimeoutMs / 1000.0 * 0.25);
            Assert.That(g.Finished, Is.True, "the idle player fails the round");
            Assert.That(g.FinalScore, Is.EqualTo(0), "no round completed, no points");
        }

        [Test]
        public void FromRoundSixChordsNeedBothPadsWithinTheWindow()
        {
            var g = new GoobySaysGame("normal", 11);
            for (var round = 1; round <= 5; round++) { WatchThrough(g); TapBack(g); }
            Assert.That(g.RoundNo, Is.EqualTo(6));
            Assert.That(g.Sequence.Count, Is.EqualTo(6));
            var chord = g.Sequence[5];
            Assert.That(chord.IsChord, Is.True, "the new step of round 6 is a chord");
            var partners = new List<int>();
            g.PadLit += (pad, partner) => partners.Add(partner);
            WatchThrough(g);
            Assert.That(partners[5], Is.EqualTo(chord.Second), "the playback lights the chord's partner too");
            for (var i = 0; i < 5; i++) Assert.That(partners[i], Is.EqualTo(-1), "the earlier steps are singles");
            // the five single steps, then the chord: the first tap opens it, a slow second tap is late → fail
            for (var i = 0; i < 5; i++) { Assert.That(g.Tap(g.Sequence[i].First), Is.EqualTo("ok")); Skip(g, 0.2); }
            Assert.That(g.CurrentStep.Value.IsChord, Is.True);
            Assert.That(g.Tap(chord.Second), Is.EqualTo("chord-open"), "either pad may come first");
            Assert.That(g.ChordFirst, Is.EqualTo(chord.Second));
            Skip(g, g.Tune.ChordWindowMs / 1000.0 + 0.2);
            Assert.That(g.Tap(chord.First), Is.EqualTo("fail"), "too late for the chord");
            Assert.That(g.Finished, Is.True);
            Assert.That(g.RoundsCompleted, Is.EqualTo(5));
        }

        [Test]
        public void TheReplaySpeedsUpEachRoundAndTheModesScaleIt()
        {
            var g = new GoobySaysGame("normal", 2);
            Assert.That(GoobySays.StepMsAt(2, g.Tune), Is.LessThan(GoobySays.StepMsAt(1, g.Tune)));
            var easy = new GoobySaysGame("easy", 2);
            var hard = new GoobySaysGame("hard", 2);
            Assert.That(GoobySays.StepMsAt(1, easy.Tune), Is.GreaterThan(GoobySays.StepMsAt(1, g.Tune)));
            Assert.That(GoobySays.StepMsAt(1, hard.Tune), Is.LessThan(GoobySays.StepMsAt(1, g.Tune)));
            Assert.That(hard.Tune.InputTimeoutMs, Is.LessThan(g.Tune.InputTimeoutMs));
            // the same seed deals the same sequence
            var a = new GoobySaysGame("normal", 9); var b = new GoobySaysGame("normal", 9);
            Assert.That(a.Sequence[0].First, Is.EqualTo(b.Sequence[0].First));
            // the playback of round 1 takes its one step of StepBaseMs after the first-step delay
            WatchThrough(a);
            Assert.That(a.Elapsed, Is.EqualTo(GoobySaysGame.FirstStepDelaySec + a.Tune.StepBaseMs / 1000.0).Within(0.1));
        }
    }
}
