namespace Gooby.Core.Random
{
    /// <summary>
    /// mulberry32 — BIT-identical port of the web reference RNG used by every minigame
    /// (GOOBY/src/minigames/framework.js createRng) and of scripts/minigames/gooby_rng.gd.
    /// Golden proof: Tests/Fixtures/expected/rng.json.
    /// </summary>
    public sealed class GoobyRng
    {
        private uint _state;

        public GoobyRng(long seed = 1)
        {
            _state = unchecked((uint)(seed & 0xFFFFFFFFL));
        }

        /// <summary>Next value in [0, 1) — u32 / 2^32 is exact in IEEE-754, hence bit-identical to JS.</summary>
        public double Next() => NextU32() / 4294967296.0;

        /// <summary>Raw 32-bit core: <c>(t ^ (t >>> 14)) >>> 0</c>.</summary>
        public uint NextU32()
        {
            unchecked
            {
                _state += 0x6D2B79F5u;
                var t = Imul(_state ^ (_state >> 15), _state | 1u);
                t = t + Imul(t ^ (t >> 7), t | 61u);
                return t ^ (t >> 14);
            }
        }

        /// <summary>Uniform in [lo, hi) — consumes exactly one Next().</summary>
        public double RangeF(double lo, double hi) => lo + Next() * (hi - lo);

        /// <summary>Integer in [lo, hi] inclusive using one Next().</summary>
        public int RangeI(int lo, int hi) => lo + (int)System.Math.Floor(Next() * (hi - lo + 1));

        /// <summary>JS Math.imul replica: (a * b) mod 2^32.</summary>
        public static uint Imul(uint a, uint b)
        {
            unchecked { return a * b; }
        }

        public uint State => _state;
    }
}
