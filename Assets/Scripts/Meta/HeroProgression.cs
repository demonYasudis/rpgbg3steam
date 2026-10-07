using System;

namespace GuildTactics.Meta
{
    public enum HeroUpgrade { Attack, Defense }

    public static class HeroProgression
    {
        public const int MaximumExperience = 700;
        public const int ExtractionExperience = 100;
        public const int RetreatExperience = 25;
        public static int Level(int experience)
        {
            if (experience < 0 || experience > MaximumExperience) throw new ArgumentOutOfRangeException(nameof(experience));
            return experience >= 700 ? 5 : experience >= 450 ? 4 : experience >= 250 ? 3 : experience >= 100 ? 2 : 1;
        }
        public static int NextThreshold(int experience)
        {
            switch (Level(experience)) { case 1: return 100; case 2: return 250; case 3: return 450; default: return 700; }
        }
    }
}
