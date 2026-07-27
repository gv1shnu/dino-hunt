using System.Runtime.CompilerServices;

namespace DinoHunt.Core
{
    /// <summary>
    /// Deterministic pseudo-random generator (SplitMix64). Self-contained: identical
    /// output on every platform for a given seed, with no dependency on UnityEngine.Random.
    ///
    /// This is the RNG spine of the whole project. Every system that needs randomness
    /// takes its own DeterministicRandom seeded from the match seed. Never call
    /// UnityEngine.Random in game logic — evolution fitness depends on reproducibility.
    /// </summary>
    public struct DeterministicRandom
    {
        private ulong _state;

        public DeterministicRandom(ulong seed)
        {
            // Mix the raw seed once so that sequential seeds (0, 1, 2, ...) still
            // produce well-separated streams.
            _state = seed;
            _state = Mix(ref _state);
        }

        /// <summary>Current internal state, for snapshotting / resuming a stream.</summary>
        public ulong State
        {
            get => _state;
            set => _state = value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong Mix(ref ulong state)
        {
            unchecked
            {
                state += 0x9E3779B97F4A7C15UL;
                ulong z = state;
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                return z ^ (z >> 31);
            }
        }

        /// <summary>Next raw 64-bit value.</summary>
        public ulong NextULong() => Mix(ref _state);

        /// <summary>Next raw 32-bit value.</summary>
        public uint NextUInt() => (uint)(NextULong() >> 32);

        /// <summary>Float in [0, 1). Uses 24 bits of mantissa for a clean, portable mapping.</summary>
        public float NextFloat()
        {
            // Divide by 2^24 so the result is always in [0, 1) with uniform spacing.
            return (NextULong() >> 40) * (1.0f / 16777216.0f);
        }

        /// <summary>Float in [min, max).</summary>
        public float NextFloat(float min, float max) => min + NextFloat() * (max - min);

        /// <summary>Int in [minInclusive, maxExclusive). Returns minInclusive if the range is empty.</summary>
        public int NextInt(int minInclusive, int maxExclusive)
        {
            if (maxExclusive <= minInclusive) return minInclusive;
            uint range = (uint)(maxExclusive - minInclusive);
            return minInclusive + (int)(NextUInt() % range);
        }

        /// <summary>True with probability 0.5.</summary>
        public bool NextBool() => (NextULong() & 1UL) != 0UL;

        /// <summary>Fresh independent stream derived from this one. Advances this generator.</summary>
        public DeterministicRandom Fork() => new DeterministicRandom(NextULong());
    }
}
