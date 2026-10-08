using System;
using System.Collections.Generic;

namespace GuildTactics.Units
{
    public enum HeroUpgrade { Accuracy, Guard }

    /// <summary>Permanent hero development. Definitions and equipment never own these bonuses.</summary>
    public sealed class HeroProgression
    {
        public const int MaximumLevel = 5;
        public const int MaximumExperience = 700;
        public const int ExtractionExperience = 100;
        private static readonly int[] thresholds = { 0, 100, 250, 450, MaximumExperience };
        private readonly List<HeroUpgrade> upgrades = new List<HeroUpgrade>();
        public IReadOnlyList<HeroUpgrade> Upgrades { get; }
        public int Experience { get; private set; }
        public int Level => LevelFor(Experience);
        public int PendingChoices => Level - 1 - upgrades.Count;
        public int AttackBonus { get; private set; }
        public int DefenseBonus { get; private set; }
        public int NextChoiceLevel => upgrades.Count + 2;
        public int NextLevelExperience => Level == MaximumLevel ? MaximumExperience : thresholds[Level];

        public HeroProgression() => Upgrades = upgrades.AsReadOnly();

        public static int LevelFor(int experience)
        {
            if (experience < 0 || experience > MaximumExperience) throw new ArgumentOutOfRangeException(nameof(experience));
            int level = 1;
            while (level < MaximumLevel && experience >= thresholds[level]) level++;
            return level;
        }

        internal void AwardExperience(int amount)
        {
            if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
            Experience = (int)Math.Min(MaximumExperience, (long)Experience + amount);
        }

        // The level identifies a particular choice, so repeated commands cannot apply it twice.
        internal bool TryChoose(int level, HeroUpgrade upgrade)
        {
            if (PendingChoices == 0 || level != NextChoiceLevel || !Enum.IsDefined(typeof(HeroUpgrade), upgrade)) return false;
            upgrades.Add(upgrade);
            if (upgrade == HeroUpgrade.Accuracy) AttackBonus++; else DefenseBonus++;
            return true;
        }

        internal static HeroProgression Restore(int experience, int[] choices)
        {
            if (experience < 0 || experience > MaximumExperience || choices == null || choices.Length > LevelFor(experience) - 1)
                throw new ArgumentException("Invalid saved progression.");
            var progression = new HeroProgression();
            progression.AwardExperience(experience);
            foreach (int choice in choices)
                if (!progression.TryChoose(progression.NextChoiceLevel, (HeroUpgrade)choice))
                    throw new ArgumentException("Invalid saved upgrade.");
            return progression;
        }
    }
}
