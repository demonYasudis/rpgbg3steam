using System;

namespace GuildTactics.Units
{
    /// <summary>Immutable shared data. Per-expedition values belong to UnitRuntimeState.</summary>
    public sealed class UnitDefinition
    {
        public string Id { get; }
        public string DisplayName { get; }
        public int Movement { get; }
        public int Initiative { get; }
        public int MaxHealth { get; }
        public int Attack { get; }
        public int Defense { get; }
        public int DamageDie { get; }
        public int DamageBonus { get; }

        public UnitDefinition(string id, string displayName, int movement, int initiative = 0,
            int maxHealth = 20, int attack = 4, int defense = 12, int damageDie = 6, int damageBonus = 2)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException("A definition requires a stable ID.", nameof(id));
            if (string.IsNullOrWhiteSpace(displayName))
                throw new ArgumentException("A definition requires a display name.", nameof(displayName));
            if (movement < 0) throw new ArgumentOutOfRangeException(nameof(movement));
            if (maxHealth <= 0) throw new ArgumentOutOfRangeException(nameof(maxHealth));
            if (damageDie <= 0) throw new ArgumentOutOfRangeException(nameof(damageDie));
            Id = id;
            DisplayName = displayName;
            Movement = movement;
            Initiative = initiative;
            MaxHealth = maxHealth;
            Attack = attack;
            Defense = defense;
            DamageDie = damageDie;
            DamageBonus = damageBonus;
        }
    }
}
