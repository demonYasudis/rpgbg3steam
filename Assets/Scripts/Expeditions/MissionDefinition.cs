using System;

namespace GuildTactics.Expeditions
{
    public enum MissionType { LootAndClear, RecoverRelic, EliminateTarget, ClearArea }

    /// <summary>Immutable objective and reward data. LootAndClear preserves legacy validation fixtures.</summary>
    public sealed class MissionDefinition
    {
        public MissionType Type { get; }
        public string Name { get; }
        public int MinimumGold { get; }
        public int MaximumGold { get; }
        public bool GrantsItems { get; }
        public bool RequiresRelic => Type == MissionType.RecoverRelic || Type == MissionType.LootAndClear;
        public MissionDefinition(MissionType type, string name, int minimumGold, int maximumGold, bool grantsItems = true)
        {
            if (!Enum.IsDefined(typeof(MissionType), type) || string.IsNullOrWhiteSpace(name) || minimumGold < 0 || maximumGold < minimumGold || maximumGold > 100000)
                throw new ArgumentException("Invalid mission definition.");
            Type = type; Name = name; MinimumGold = minimumGold; MaximumGold = maximumGold; GrantsItems = grantsItems;
        }
        public static MissionDefinition Legacy { get; } = new MissionDefinition(MissionType.LootAndClear, "Loot and clear", 20, 60);
        public static MissionDefinition Relic { get; } = new MissionDefinition(MissionType.RecoverRelic, "Recover relic", 20, 40);
        public static MissionDefinition Hunt { get; } = new MissionDefinition(MissionType.EliminateTarget, "Eliminate target", 30, 60);
        public static MissionDefinition Clear { get; } = new MissionDefinition(MissionType.ClearArea, "Clear area", 40, 70);
    }
}
