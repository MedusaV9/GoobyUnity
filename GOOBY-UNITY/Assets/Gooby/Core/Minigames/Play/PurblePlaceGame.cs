using System;
using Gooby.Core.Minigames.Games;
using Gooby.Core.Random;

namespace Gooby.Core.Minigames.Play
{
    /// <summary>
    /// Realtime Tortenwerkstatt — the input side of purble_place.gd around the certified <see cref="PurblePlace"/> line:
    /// the ◀/▶ belt buttons are held (−1/0/+1), nozzle buttons, "Neue Form" (one per shape) and "Versand" are one-shot
    /// presses for the next step. The 210-s shift ends the round (endless: three failed orders). A 1.5 s intro lock
    /// lets the frame count down before the belt starts.
    /// </summary>
    public sealed class PurblePlaceGame
    {
        public const double IntroSec = 1.5;

        public PurblePlace.Tune Tune => Line.Tune;
        public PurblePlace.Line Line { get; }
        public double IntroLeft { get; private set; } = IntroSec;
        public bool Finished { get; private set; }
        public int FinalScore { get; private set; }

        public event Action<PurblePlace.Ev> Event;
        public event Action Finished_;

        private int _belt;
        private string _press, _spawn;
        private bool _ship, _botOn;
        private readonly PurblePlace.Bot _bot;

        public PurblePlaceGame(string mode, int seed)
        {
            Line = PurblePlace.CreateLine(new GoobyRng(seed), mode);
            _bot = new PurblePlace.Bot(new GoobyRng(unchecked((uint)seed ^ 0x9E3779B9u)));
        }

        public double TimeLeft => Tune.Endless ? double.PositiveInfinity : Math.Max(0.0, Tune.DurationSec - Line.T);

        public void SetBelt(int dir) => _belt = Math.Sign(dir);
        public void Press(string stationId) { if (!Finished && IntroLeft <= 0.0) _press = stationId; }
        public void Spawn(string shape) { if (!Finished && IntroLeft <= 0.0) _spawn = shape; }
        public void Ship() { if (!Finished && IntroLeft <= 0.0) _ship = true; }

        /// <summary>QA/tests: the certified bot plans this step.</summary>
        public void ApplyBot() => _botOn = true;

        public void Tick(double dt)
        {
            if (Finished || dt <= 0.0) return;
            if (IntroLeft > 0.0)
            {
                IntroLeft = Math.Max(0.0, IntroLeft - dt);
                _press = _spawn = null;
                _ship = false;
                return;
            }
            var input = _botOn
                ? _bot.Plan(Line, dt)
                : new PurblePlace.Input { Belt = _belt, Press = _press, SpawnShape = _spawn, Ship = _ship };
            _botOn = false;
            _press = _spawn = null;
            _ship = false;
            foreach (var ev in PurblePlace.StepLine(Line, Math.Min(dt, 0.1), input)) Event?.Invoke(ev);
            if (Line.Over || (!Tune.Endless && Line.T >= Tune.DurationSec))
            {
                Finished = true;
                FinalScore = Line.Score;
                Finished_?.Invoke();
            }
        }
    }
}
