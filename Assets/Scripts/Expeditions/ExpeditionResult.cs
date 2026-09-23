using System.Collections.Generic;
using GuildTactics.Units;

namespace GuildTactics.Expeditions
{
    public enum ExpeditionOutcome { Extracted, Defeated }

    public sealed class AdventurerResult
    {
        public string InstanceId { get; }
        public string DefinitionId { get; }
        public string Name { get; }
        public int Health { get; }
        public int MaxHealth { get; }
        public bool Survived => Health > 0;
        public bool BodyRecovered { get; }
        internal AdventurerResult(UnitRuntimeState unit, bool bodyRecovered)
        {
            InstanceId = unit.InstanceId; DefinitionId = unit.Definition.Id;
            Name = unit.Definition.DisplayName; Health = unit.CurrentHealth; MaxHealth = unit.Definition.MaxHealth;
            BodyRecovered = !Survived && bodyRecovered;
        }
    }

    /// <summary>Snapshot for the result screen and future guild; no scene or mutable unit references.</summary>
    public sealed class ExpeditionResult
    {
        public int Seed { get; }
        public ExpeditionOutcome Outcome { get; }
        public int Gold { get; }
        public IReadOnlyList<ItemDefinition> Items { get; }
        public IReadOnlyList<AdventurerResult> Adventurers { get; }

        internal ExpeditionResult(int seed, ExpeditionOutcome outcome, int gold,
            IEnumerable<ItemDefinition> items, IEnumerable<UnitRuntimeState> party,
            ISet<string> recoveredBodies = null)
        {
            Seed = seed; Outcome = outcome; Gold = gold;
            Items = new List<ItemDefinition>(items).AsReadOnly();
            var snapshots = new List<AdventurerResult>();
            foreach (var unit in party) snapshots.Add(new AdventurerResult(unit,
                outcome == ExpeditionOutcome.Extracted && recoveredBodies != null && recoveredBodies.Contains(unit.InstanceId)));
            Adventurers = snapshots.AsReadOnly();
        }
    }
}
