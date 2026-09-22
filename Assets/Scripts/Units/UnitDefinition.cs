using System;
using System.Collections.Generic;
using GuildTactics.Abilities;

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
        public int AttackRange { get; }
        public IReadOnlyList<AbilityDefinition> Abilities { get; }

        public UnitDefinition(string id, string displayName, int movement, int initiative = 0,
            int maxHealth = 20, int attack = 4, int defense = 12, int damageDie = 6, int damageBonus = 2,
            int attackRange = 1, IEnumerable<AbilityDefinition> abilities = null)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException("A definition requires a stable ID.", nameof(id));
            if (string.IsNullOrWhiteSpace(displayName))
                throw new ArgumentException("A definition requires a display name.", nameof(displayName));
            if (movement < 0) throw new ArgumentOutOfRangeException(nameof(movement));
            if (maxHealth <= 0) throw new ArgumentOutOfRangeException(nameof(maxHealth));
            if (damageDie <= 0) throw new ArgumentOutOfRangeException(nameof(damageDie));
            if (attackRange <= 0) throw new ArgumentOutOfRangeException(nameof(attackRange));
            var abilityList = new List<AbilityDefinition>();
            var abilityIds = new HashSet<string>(StringComparer.Ordinal);
            if (abilities != null)
                foreach (var ability in abilities)
                {
                    if (ability == null || !abilityIds.Add(ability.Id))
                        throw new ArgumentException("Abilities must be non-null with unique IDs.", nameof(abilities));
                    abilityList.Add(ability);
                }
            Abilities = abilityList.AsReadOnly();
            AttackRange = attackRange;
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
