using System;

namespace GuildTactics.Units
{
    /// <summary>Immutable shared data. Per-expedition values belong to UnitRuntimeState.</summary>
    public sealed class UnitDefinition
    {
        public string Id { get; }
        public string DisplayName { get; }
        public int Movement { get; }

        public UnitDefinition(string id, string displayName, int movement)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException("A definition requires a stable ID.", nameof(id));
            if (string.IsNullOrWhiteSpace(displayName))
                throw new ArgumentException("A definition requires a display name.", nameof(displayName));
            if (movement < 0) throw new ArgumentOutOfRangeException(nameof(movement));
            Id = id;
            DisplayName = displayName;
            Movement = movement;
        }
    }
}
