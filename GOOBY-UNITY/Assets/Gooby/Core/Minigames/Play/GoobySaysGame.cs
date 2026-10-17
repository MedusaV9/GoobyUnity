using System;
using System.Collections.Generic;
using Gooby.Core.Minigames.Games;
using Gooby.Core.Random;

namespace Gooby.Core.Minigames.Play
{
    /// <summary>
    /// Realtime "Gooby sagt" round — port of gooby_says.gd's _next_round/_playback_tick/_input_timeout_tick/
    /// _unhandled_input/_handle_chord_tap/_advance_step/_fail over the certified GoobySays rules, engine-free.
    /// Gooby plays a sequence on four pads (watch), the player taps it back (input); each round adds a step and
    /// plays faster, from round 6 two-pad chords appear, a wrong pad or five idle seconds end the round.
    /// Score = 10 per completed round + a speed bonus for quick taps (GoobySays.RoundScore).
    /// </summary>
    public sealed class GoobySaysGame
    {
        public const double IntroSec = 1.5;
        public const double PlaybackGap = 0.34;      // part of a step the pad is dark again before the next lights
        public const double FirstStepDelaySec = 0.45;
        public const double TapLitSec = 0.16;
        public const double TimeoutWarnFrom = 0.6;
        public const string Watch = "watch", Input = "input";

        public GoobySays.Tune Tune { get; }
        public GoobyRng Rng { get; }
        public string Mode { get; }
        public readonly List<GoobySays.Step> Sequence = new List<GoobySays.Step>();
        public int RoundNo { get; private set; }
        public int RoundsCompleted { get; private set; }
        public int StepIndex { get; private set; }
        public int LitPad { get; private set; } = -1;
        public double LitLeft { get; private set; }
        public string Phase { get; private set; } = Watch;
        public double Elapsed { get; private set; }
        public double IntroLeft { get; private set; } = IntroSec;
        public bool Finished { get; private set; }
        public int FinalScore { get; private set; }
        public int ChordFirst { get; private set; } = -1;

        private double _playTimer;
        private double _chordAt = -1.0;
        private double _reactionTotalMs;
        private int _reactionSteps;
        private double _stepStartedAt = -1.0;

        public event Action<int, int> PadLit;          // playback: pad, chord partner (−1)
        public event Action InputOpen;                  // the sequence is played, the player's turn
        public event Action<int> RoundWon;              // round number
        public event Action Failed;
        public event Action Finished_;

        public GoobySaysGame(string mode, int seed)
        {
            Mode = mode;
            Tune = GoobySays.ApplyDifficulty(GoobySays.Says, mode);
            Rng = new GoobyRng(seed);
            NextRound();
        }

        public int LiveScore => GoobySays.RoundScore(RoundsCompleted, AvgReactionMs, Tune);
        public double AvgReactionMs => _reactionSteps <= 0 ? double.PositiveInfinity : _reactionTotalMs / _reactionSteps;
        /// <summary>The step the player has to tap next (input phase), or null.</summary>
        public GoobySays.Step? CurrentStep => Phase == Input && StepIndex < Sequence.Count ? Sequence[StepIndex] : (GoobySays.Step?)null;

        /// <summary>Warning share [0..1] of the input window: quiet until 60 %, then rising to 1 just before the timeout.</summary>
        public double TimeoutUrgency
        {
            get
            {
                if (Phase != Input || _stepStartedAt < 0.0) return 0.0;
                var frac = (Elapsed - _stepStartedAt) * 1000.0 / Tune.InputTimeoutMs;
                return Math.Min(1.0, Math.Max(0.0, (frac - TimeoutWarnFrom) / (1.0 - TimeoutWarnFrom)));
            }
        }

        private void NextRound()
        {
            RoundNo++;
            var extended = GoobySays.ExtendSequence(Sequence, Rng, RoundNo);
            Sequence.Clear();
            Sequence.AddRange(extended);
            StepIndex = 0;
            _playTimer = FirstStepDelaySec;
            Phase = Watch;
            ChordFirst = -1;
        }

        public void Tick(double dt)
        {
            if (Finished || dt <= 0.0) return;
            if (IntroLeft > 0.0)
            {
                IntroLeft = Math.Max(0.0, IntroLeft - dt);
                return;
            }
            Elapsed += dt;
            if (LitLeft > 0.0)
            {
                LitLeft = Math.Max(0.0, LitLeft - dt);
                if (LitLeft <= 0.0 && Phase == Input) LitPad = -1;
            }
            if (Phase == Watch) PlaybackTick(dt);
            else if (_stepStartedAt >= 0.0 && (Elapsed - _stepStartedAt) * 1000.0 >= Tune.InputTimeoutMs) Fail();   // hesitated too long
        }

        private void PlaybackTick(double dt)
        {
            _playTimer -= dt;
            if (_playTimer > 0.0) return;
            var stepSec = GoobySays.StepMsAt(RoundNo, Tune) / 1000.0;
            if (StepIndex >= Sequence.Count)
            {
                Phase = Input;
                StepIndex = 0;
                LitPad = -1;
                _stepStartedAt = Elapsed;
                InputOpen?.Invoke();
                return;
            }
            var step = Sequence[StepIndex];
            LitPad = step.First;
            LitLeft = stepSec * (1.0 - PlaybackGap);
            PadLit?.Invoke(step.First, step.IsChord ? step.Second : -1);
            StepIndex++;
            _playTimer = stepSec;
        }

        /// <summary>A tap on a pad during the player's turn. Returns "ok", "chord-open", "fail" or null when ignored.</summary>
        public string Tap(int pad)
        {
            if (Finished || Phase != Input || IntroLeft > 0.0 || pad < 0 || pad >= Tune.Pads || StepIndex >= Sequence.Count) return null;
            LitPad = pad;
            LitLeft = TapLitSec;
            var step = Sequence[StepIndex];
            if (step.IsChord) return HandleChordTap(step, pad);
            if (step.First == pad) { AdvanceStep(); return "ok"; }
            Fail();
            return "fail";
        }

        private string HandleChordTap(GoobySays.Step step, int pad)
        {
            if (ChordFirst < 0)
            {
                if (GoobySays.ChordTapResult(step, pad, -1, 0.0, Tune) == "wrong") { Fail(); return "fail"; }
                ChordFirst = pad;
                _chordAt = Elapsed;
                return "chord-open";
            }
            var gapMs = (Elapsed - _chordAt) * 1000.0;
            var result = GoobySays.ChordTapResult(step, ChordFirst, pad, gapMs, Tune);
            ChordFirst = -1;
            if (result == "complete") { AdvanceStep(); return "ok"; }
            Fail();
            return "fail";
        }

        private void AdvanceStep()
        {
            if (_stepStartedAt >= 0.0)
            {
                _reactionTotalMs += (Elapsed - _stepStartedAt) * 1000.0;
                _reactionSteps++;
            }
            StepIndex++;
            _stepStartedAt = Elapsed;
            if (StepIndex < Sequence.Count) return;
            RoundsCompleted = RoundNo;
            RoundWon?.Invoke(RoundNo);
            NextRound();
        }

        private void Fail()
        {
            Failed?.Invoke();
            Finish();
        }

        private void Finish()
        {
            if (Finished) return;
            Finished = true;
            FinalScore = LiveScore;
            Finished_?.Invoke();
        }
    }
}
