using System;
using Gooby.Core.Minigames.Games;

namespace Gooby.Core.Minigames.Play
{
    /// <summary>
    /// Realtime Spielzeug-Rennen — the input side of toy_racer.gd around the certified <see cref="ToyRacer"/> race:
    /// pressing holds the drift and steers towards the finger (×1.2 of the screen position, clamped to the hard lane
    /// limit), lifting releases the boost; a press shorter than 0.22 s is a tap that fires the held item instead of a
    /// micro-drift. A 1.5 s intro lock keeps the grid still while the frame counts down.
    /// </summary>
    public sealed class ToyRacerGame
    {
        public const double IntroSec = 1.5;
        public const double TapSec = 0.22;

        public ToyRacer.Tune Tune { get; }
        public ToyRacer.Race Race { get; }
        public double IntroLeft { get; private set; } = IntroSec;
        public bool Finished { get; private set; }
        public int FinalScore { get; private set; }

        public event Action<ToyRacer.Ev> Event;
        public event Action Finished_;

        private double? _steer;
        private bool _driftHeld, _wantItem, _botOn;
        private double _pressAt = -1.0, _elapsed, _releaseT;

        public ToyRacerGame(string mode, int seed)
        {
            Tune = ToyRacer.ApplyDifficulty(ToyRacer.Racer, mode);
            Race = ToyRacer.CreateRace(seed, Tune);
        }

        public ToyRacer.Kart Player => Race.Karts[0];
        public int Score => ToyRacer.RunScore(Race);
        public int Rank => ToyRacer.PlayerRank(Race);
        public int Lap => ToyRacer.PlayerLap(Race);

        /// <summary>Finger at <paramref name="normalized"/> (−1 left … +1 right) steers towards that lane.</summary>
        public void SetSteer(double normalized)
        {
            var n = Math.Max(-1.0, Math.Min(1.0, normalized));
            _steer = Math.Max(-Tune.LatHardMax, Math.Min(Tune.LatHardMax, n * 1.2));
        }

        public void Press(double normalized)
        {
            if (Finished || IntroLeft > 0.0) return;
            _driftHeld = true;
            _pressAt = _elapsed;
            SetSteer(normalized);
        }

        public void Release()
        {
            if (_pressAt >= 0.0 && _elapsed - _pressAt < TapSec && IntroLeft <= 0.0)
            {
                Player.DriftCharge = 0.0;
                _wantItem = true;
            }
            _driftHeld = false;
            _pressAt = -1.0;
        }

        /// <summary>QA/tests: the certified bot drives this frame (constant drift, releases at full charge).</summary>
        public void ApplyBot() => _botOn = true;

        public void Tick(double dt)
        {
            if (Finished || dt <= 0.0) return;
            _elapsed += dt;
            if (IntroLeft > 0.0)
            {
                IntroLeft = Math.Max(0.0, IntroLeft - dt);
                _wantItem = false;
                return;
            }
            ToyRacer.Input input;
            if (_botOn)
            {
                var b = ToyRacer.BotInput(Race);
                _releaseT = Math.Max(0.0, _releaseT - dt);
                if (Player.DriftCharge >= 0.95 && _releaseT <= 0.0) _releaseT = 2.0 * dt;
                input = new ToyRacer.Input { Steer = b.Steer, Drifting = _releaseT <= 0.0, UseItem = b.UseItem };
            }
            else input = new ToyRacer.Input { Steer = _steer, Drifting = _driftHeld, UseItem = _wantItem };
            _botOn = false;
            _wantItem = false;
            Race.Events.Clear();
            ToyRacer.StepRace(Race, dt, input);
            foreach (var ev in Race.Events) Event?.Invoke(ev);
            Race.Events.Clear();
            if (Race.Ended && !Finished)
            {
                Finished = true;
                FinalScore = ToyRacer.RunScore(Race);
                Finished_?.Invoke();
            }
        }
    }
}
