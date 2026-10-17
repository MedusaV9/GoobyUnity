using System;

namespace Gooby.Core.Random
{
    /// <summary>
    /// Godot 4's RandomNumberGenerator (core/math/random_pcg + thirdparty pcg32), bit-identical: <c>rng.seed = s</c>,
    /// <c>randi()</c>, <c>randf()</c> (a float32 built from two draws) and <c>randi_range()</c> (pcg32 bounded draw), plus
    /// Godot's <c>String.hash()</c> (djb2 over code points). The Goo und Bye market day (goobye_markttag.gd) draws with it,
    /// so the Unity store reproduces Godot's golden receipts.
    /// </summary>
    public sealed class GodotRng
    {
        private const ulong Multiplier = 6364136223846793005UL;
        public const ulong DefaultInc = 1442695040888963407UL;
        private readonly ulong _sequence;
        private ulong _state, _inc;

        public GodotRng(ulong seed, ulong sequence = DefaultInc)
        {
            _sequence = sequence;
            Seed(seed);
        }

        public GodotRng(long seed) : this(unchecked((ulong)seed)) { }

        /// <summary>pcg32_srandom_r(seed, sequence) — what assigning <c>rng.seed</c> does (sequence = DEFAULT_INC).</summary>
        public void Seed(ulong seed)
        {
            unchecked
            {
                _state = 0UL;
                _inc = (_sequence << 1) | 1UL;
                RandI();
                _state += seed;
                RandI();
            }
        }

        /// <summary>pcg32_random_r (XSH RR on the old state).</summary>
        public uint RandI()
        {
            unchecked
            {
                var old = _state;
                _state = old * Multiplier + (_inc | 1UL);
                var xorshifted = (uint)(((old >> 18) ^ old) >> 27);
                var rot = (int)(old >> 59);
                return (xorshifted >> rot) | (xorshifted << ((-rot) & 31));
            }
        }

        /// <summary>pcg32_boundedrand_r: uniform in [0, bound) without modulo bias.</summary>
        public uint Bounded(uint bound)
        {
            if (bound == 0) return 0;
            var threshold = unchecked(0u - bound) % bound;
            while (true)
            {
                var r = RandI();
                if (r >= threshold) return r % bound;
            }
        }

        /// <summary>RandomPCG::randf — the fraction of a random binary number, as Godot's float32.</summary>
        public float RandF()
        {
            var protoExpOffset = RandI();
            if (protoExpOffset == 0) return 0f;
            var significand = (float)(RandI() | 0x80000001u);
            return (float)(significand * Math.Pow(2.0, -32 - LeadingZeros(protoExpOffset)));
        }

        /// <summary>RandomPCG::random(float from, float to): randf() scaled into the range.</summary>
        public float RandFRange(float from, float to) => RandF() * (to - from) + from;

        /// <summary>RandomPCG::random(from, to), both ends inclusive.</summary>
        public int RandIRange(int from, int to)
        {
            if (from == to) return from;
            return (int)Bounded((uint)Math.Abs(from - to) + 1u) + Math.Min(from, to);
        }

        private static int LeadingZeros(uint x)
        {
            if (x == 0) return 32;
            var n = 0;
            while ((x & 0x80000000u) == 0) { x <<= 1; n++; }
            return n;
        }

        /// <summary>Godot's String.hash(): djb2 over the code points, uint32.</summary>
        public static uint StringHash(string text)
        {
            unchecked
            {
                uint h = 5381;
                if (string.IsNullOrEmpty(text)) return h;
                for (var i = 0; i < text.Length; i++)
                {
                    int cp;
                    if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                    {
                        cp = char.ConvertToUtf32(text[i], text[i + 1]);
                        i++;
                    }
                    else cp = text[i];
                    h = (h << 5) + h + (uint)cp;
                }
                return h;
            }
        }
    }
}
