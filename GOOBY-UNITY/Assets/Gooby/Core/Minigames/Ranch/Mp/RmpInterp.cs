using System;
using System.Collections.Generic;

namespace Gooby.Core.Minigames.Ranch.Mp
{
    /// <summary>
    /// Interpolation buffer of a peer's poses (rmp_interp.gd, RW-6): poses arrive at 10 Hz, the rider is drawn
    /// <see cref="DelayMs"/> in the past and interpolated linearly in between (heading the short way round). A drained buffer
    /// (lag spike) holds the last pose; after <see cref="StaleMs"/> of silence the rider counts as frozen. Pure and
    /// deterministic: the caller passes its own millisecond clock.
    /// </summary>
    public sealed class RmpInterp
    {
        public const int DelayMs = 150, BufferMax = 24, StaleMs = 1200;

        public struct Pose
        {
            public V2 Pos;
            public double Y, Yaw;
            public int Gait;
            public bool Stale, Jump;
        }

        private readonly List<(long at, Pose pose)> _samples = new List<(long, Pose)>();

        public bool HasData => _samples.Count > 0;
        public long LastAtMs => _samples.Count > 0 ? _samples[_samples.Count - 1].at : -1;

        public void Push(long atMs, V2 pos, double y, double yaw, int gait, bool jump = false)
        {
            _samples.Add((atMs, new Pose { Pos = pos, Y = y, Yaw = yaw, Gait = gait, Jump = jump }));
            while (_samples.Count > BufferMax) _samples.RemoveAt(0);
        }

        /// <summary>The pose at render time <paramref name="nowMs"/> (looks <see cref="DelayMs"/> back); null before any data.</summary>
        public Pose? SampleAt(long nowMs)
        {
            if (_samples.Count == 0) return null;
            var ziel = nowMs - DelayMs;
            var first = _samples[0];
            var last = _samples[_samples.Count - 1];
            if (ziel <= first.at || _samples.Count == 1)
            {
                var p = first.pose;
                p.Stale = nowMs - last.at > StaleMs;
                return p;
            }
            for (var i = 0; i < _samples.Count - 1; i++)
            {
                var a = _samples[i];
                var b = _samples[i + 1];
                if (ziel < a.at || ziel > b.at) continue;
                var span = (double)(b.at - a.at);
                var frac = span <= 0.0 ? 0.0 : (ziel - a.at) / span;
                var diff = PosMod(b.pose.Yaw - a.pose.Yaw + Math.PI, 2.0 * Math.PI) - Math.PI;
                return new Pose
                {
                    Pos = a.pose.Pos.Lerp(b.pose.Pos, (float)frac),
                    Y = a.pose.Y + (b.pose.Y - a.pose.Y) * frac,
                    Yaw = a.pose.Yaw + diff * frac,
                    Gait = b.pose.Gait,
                    Jump = b.pose.Jump,
                };
            }
            var hold = last.pose;
            hold.Stale = nowMs - last.at > StaleMs;
            return hold;
        }

        /// <summary>Forget everything (rejoin/teleport: nothing glides across the meadow).</summary>
        public void Reset() => _samples.Clear();

        private static double PosMod(double a, double m)
        {
            var r = a % m;
            return r < 0.0 ? r + m : r;
        }
    }
}
