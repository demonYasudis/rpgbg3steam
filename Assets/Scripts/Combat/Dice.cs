using System;

namespace GuildTactics.Combat
{
    public interface IDice
    {
        int Roll(int sides);
    }

    /// <summary>Private random stream: unrelated Unity random calls cannot change combat rolls.</summary>
    public sealed class SeededDice : IDice
    {
        public const int DefaultSeed = 12345;
        private readonly Random random;
        public int Seed { get; }

        public SeededDice(int seed)
        {
            Seed = seed;
            random = new Random(seed);
        }

        public int Roll(int sides)
        {
            if (sides <= 0) throw new ArgumentOutOfRangeException(nameof(sides));
            return random.Next(sides) + 1;
        }
    }
}
