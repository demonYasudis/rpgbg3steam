using System;
using System.Globalization;

namespace GuildTactics.Generation
{
    /// <summary>Stable UTF-16 seed hash and PRNG; independent of combat and runtime string hashing.</summary>
    public sealed class GenerationRandom
    {
        private uint state;
        public GenerationRandom(int seed) { state = unchecked((uint)seed); }
        public static int ParseSeed(string seed)
        {
            if (string.IsNullOrWhiteSpace(seed)) throw new ArgumentException("Seed must not be empty.", nameof(seed));
            seed = seed.Trim();
            if (int.TryParse(seed, NumberStyles.Integer, CultureInfo.InvariantCulture, out int numeric)) return numeric;
            unchecked
            {
                uint hash = 2166136261;
                foreach (char value in seed) hash = (hash ^ value) * 16777619;
                return (int)hash;
            }
        }
        public int Next(int exclusiveMax)
        {
            if (exclusiveMax <= 0) throw new ArgumentOutOfRangeException(nameof(exclusiveMax));
            unchecked { state = state * 1664525u + 1013904223u; }
            return (int)(((ulong)state * (uint)exclusiveMax) >> 32);
        }
    }
}
