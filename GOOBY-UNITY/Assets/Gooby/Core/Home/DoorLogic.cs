using System;

namespace Gooby.Core.Home
{
    /// <summary>
    /// The door gag (port of door_logic.gd, Doc A §5): on the way through a door Gooby sometimes gets stuck in the
    /// frame (~12 %, never twice in a row, only with animated doors) and the player mashes him free — 5–8 taps of
    /// pressure that drains away, faster the closer he is to popping through (a resistance curve, so the last taps must
    /// come quick). Pure: the random rolls are injected.
    /// </summary>
    public sealed class DoorLogic
    {
        public const double StuckChance = 0.12, MashDecayPerSec = 0.8, ResistanceGain = 1.6;
        public const int TapsMin = 5, TapsMax = 8;

        public enum State { Idle, Opening, Walking, Stuck, Popping, Travel }

        public State Current { get; private set; } = State.Idle;
        public bool Animated { get; }
        public bool WillStick { get; }
        public int TapsRequired { get; }
        private double _mash;

        public DoorLogic(bool doorsAnimated, bool previousWasStuck, double stuckRoll = 1.0, double tapsRoll = 0.0)
        {
            Animated = doorsAnimated;
            WillStick = Animated && !previousWasStuck && stuckRoll < StuckChance;
            TapsRequired = TapsMin + Math.Max(0, Math.Min(3, (int)(tapsRoll * (TapsMax - TapsMin + 1))));
        }

        public State Begin()
        {
            if (Current == State.Idle) Current = Animated ? State.Opening : State.Travel;
            return Current;
        }

        public State DoorOpened()
        {
            if (Current == State.Opening) Current = State.Walking;
            return Current;
        }

        public State ReachedDoor()
        {
            if (Current == State.Walking) Current = WillStick ? State.Stuck : State.Travel;
            return Current;
        }

        public State TapMash()
        {
            if (Current != State.Stuck) return Current;
            _mash += 1.0;
            if (_mash >= TapsRequired) Current = State.Popping;
            return Current;
        }

        public void MashDecay(double dt)
        {
            if (Current == State.Stuck) _mash = Math.Max(0.0, _mash - DecayRate * dt);
        }

        public double DecayRate => MashDecayPerSec * (1.0 + ResistanceGain * MashRatio);

        public double MashRatio => TapsRequired <= 0 ? 0.0 : Math.Max(0.0, Math.Min(1.0, _mash / TapsRequired));

        /// <summary>The next tap pops him through (the squash telegraphs it).</summary>
        public bool IstLetzterTap => Current == State.Stuck && _mash >= TapsRequired - 1.0;

        public State PopFinished()
        {
            if (Current == State.Popping) Current = State.Travel;
            return Current;
        }

        /// <summary>Skip by tap — not while stuck (mashing is the way out) and not once travelling.</summary>
        public State Skip()
        {
            if (Current == State.Opening || Current == State.Walking || Current == State.Popping) Current = State.Travel;
            return Current;
        }

        public bool IsTraveling => Current == State.Travel;
    }
}
