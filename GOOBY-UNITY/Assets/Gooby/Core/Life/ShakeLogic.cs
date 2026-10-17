using System;
using System.Collections.Generic;

namespace Gooby.Core.Life
{
    /// <summary>
    /// The shake secret's detector (port of shake_logic.gd): accelerometer samples in m/s² in, an escalation stage out.
    /// Gravity is taken off with a low-pass (α 0.1); the shake measure is the moving RMS of what is left over 0.5 s, fed
    /// into an energy store (+RMS·dt, draining 2.5/s). Stage 1 above 4 (about a second of gentle shaking), 2 above 9
    /// (two and a half seconds of hearty shaking), 3 above 15 (four seconds of wild shaking); rest lets it fall back to 0.
    /// The 10-minute pause after stage 3 is pure and time-injected too.
    /// </summary>
    public sealed class ShakeLogic
    {
        public const double GravityAlpha = 0.1, RmsWindowS = 0.5, DecayPerS = 2.5;
        public const double Stage1Energy = 4.0, Stage2Energy = 9.0, Stage3Energy = 15.0;
        public const long CooldownMs = 10L * 60L * 1000L;

        public double Energy { get; private set; }

        private (double x, double y, double z) _gravity;
        private bool _primed;
        private readonly List<(double dt, double sq)> _window = new List<(double, double)>();
        private double _windowDur;

        public static int StageForEnergy(double value) => value > Stage3Energy ? 3 : value > Stage2Energy ? 2 : value > Stage1Energy ? 1 : 0;

        /// <summary>Has the pause after stage 3 run out? (0 = never reached.)</summary>
        public static bool CooldownReady(long lastStage3Ms, long nowMs) => lastStage3Ms <= 0 || nowMs - lastStage3Ms >= CooldownMs;

        /// <summary>One sample (m/s²); returns the current stage.</summary>
        public int Feed(double ax, double ay, double az, double dt)
        {
            if (dt <= 0.0) return Stage;
            if (!_primed)
            {
                // the first sample is gravity: a phone lying still starts at 0
                _gravity = (ax, ay, az);
                _primed = true;
            }
            else
            {
                _gravity = (_gravity.x + (ax - _gravity.x) * GravityAlpha, _gravity.y + (ay - _gravity.y) * GravityAlpha, _gravity.z + (az - _gravity.z) * GravityAlpha);
            }
            var rx = ax - _gravity.x; var ry = ay - _gravity.y; var rz = az - _gravity.z;
            _window.Add((dt, rx * rx + ry * ry + rz * rz));
            _windowDur += dt;
            while (_window.Count > 1 && _windowDur - _window[0].dt > RmsWindowS)
            {
                _windowDur -= _window[0].dt;
                _window.RemoveAt(0);
            }
            Energy = Math.Max(0.0, Energy + (Rms() - DecayPerS) * dt);
            return Stage;
        }

        /// <summary>Moving RMS of the leftover acceleration over the 0.5 s window.</summary>
        public double Rms()
        {
            if (_windowDur <= 0.0) return 0.0;
            var sum = 0.0;
            foreach (var (dt, sq) in _window) sum += sq * dt;
            return Math.Sqrt(sum / _windowDur);
        }

        public int Stage => StageForEnergy(Energy);

        /// <summary>Empties energy and window (after stage 3, in the pause); the gravity filter stays primed.</summary>
        public void Reset()
        {
            Energy = 0.0;
            _window.Clear();
            _windowDur = 0.0;
        }
    }
}
