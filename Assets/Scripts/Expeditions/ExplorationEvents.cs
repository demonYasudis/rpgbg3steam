using System;
using System.Collections.Generic;
using System.Globalization;
using GuildTactics.Generation;

namespace GuildTactics.Expeditions
{
    // A deliberately small effect vocabulary, without scripting or dependencies on scene objects.
    public sealed class ExplorationEventDefinition
    {
        public string Id { get; }
        public string Name { get; }
        public string Description { get; }
        public int Difficulty { get; }
        public int Gold { get; }
        public int Healing { get; }
        public int SuccessDamage { get; }
        public int FailureDamage { get; }
        internal ExplorationEventDefinition(string id, string name, string description,
            int difficulty, int gold, int healing, int successDamage, int failureDamage)
        {
            Id = id; Name = name; Description = description; Difficulty = difficulty;
            Gold = gold; Healing = healing; SuccessDamage = successDamage; FailureDamage = failureDamage;
        }
    }

    [Serializable]
    public sealed class ExplorationEventResult
    {
        // choice: 0 = leave safely, 1 = investigate with a living hero.
        public int section, choice, roll, gold, healthBefore, healthAfter;
        public string hero;
        internal ExplorationEventResult Copy() => (ExplorationEventResult)MemberwiseClone();
    }

    public static class ExplorationEvents
    {
        public static IReadOnlyList<ExplorationEventDefinition> All { get; } = Array.AsReadOnly(new[]
        {
            new ExplorationEventDefinition("cache", "Sealed cache", "A rusted lock guards a hidden purse.", 10, 25, 0, 0, 6),
            new ExplorationEventDefinition("altar", "Blood altar", "The altar demands blood in exchange for ancient coins.", 13, 35, 0, 4, 10),
            new ExplorationEventDefinition("spring", "Clouded spring", "The water may soothe wounds or carry poison.", 8, 0, 8, 0, 4),
            new ExplorationEventDefinition("snare", "Wire snare", "A trapped offering lies beneath a taut wire.", 12, 20, 0, 0, 8)
        });

        private static GenerationRandom Stream(int seed, int section, string purpose) => new GenerationRandom(
            GenerationRandom.ParseSeed("exploration:" + seed.ToString(CultureInfo.InvariantCulture) + ":" +
                section.ToString(CultureInfo.InvariantCulture) + ":" + purpose));
        public static ExplorationEventDefinition At(int seed, int section) => All[Stream(seed, section, "type").Next(All.Count)];
        public static int Roll(int seed, int section) => 1 + Stream(seed, section, "roll").Next(20);

        internal static void Validate(ExplorationEventResult result, int seed, int completed, JourneyAdventurer[] party)
        {
            if (result == null || result.section < 1 || result.section > completed || result.choice < 0 || result.choice > 1)
                throw new ArgumentException("Invalid exploration choice.");
            var definition = At(seed, result.section);
            if (result.choice == 0)
            {
                if (!string.IsNullOrEmpty(result.hero) || result.roll != 0 || result.gold != 0 ||
                    result.healthBefore != 0 || result.healthAfter != 0)
                    throw new ArgumentException("Skipping an event must have no effects.");
                return;
            }
            if (!Array.Exists(party, h => h.id == result.hero) || result.roll != Roll(seed, result.section) ||
                result.healthBefore <= 0 || result.healthAfter < 0 || result.gold != (result.roll >= definition.Difficulty ? definition.Gold : 0))
                throw new ArgumentException("Invalid exploration result.");
        }
    }
}
