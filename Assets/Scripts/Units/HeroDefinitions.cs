using System.Collections.Generic;
using GuildTactics.Abilities;

namespace GuildTactics.Units
{
    public static class HeroDefinitions
    {
        private static readonly IReadOnlyList<UnitDefinition> defaults =
            new List<UnitDefinition>
            {
            new UnitDefinition("warrior", "Warrior", 4, 2, abilities: new[]
            {
                new AbilityDefinition("heavy-strike", "Heavy Strike", "Range 1. Weapon hit: +4 damage, -2 accuracy.",
                    AbilityEffect.HeavyStrike, 1, power: 4, attackBonus: -2),
                new AbilityDefinition("push", "Push", "Range 1. Push one hex away onto free ground or into a fatal pit.",
                    AbilityEffect.Push, 1)
            }),
            new UnitDefinition("rogue", "Rogue", 5, 4, abilities: new[]
            {
                new AbilityDefinition("backstab", "Backstab", "Range 1. +5 damage if another ally is adjacent to the target.",
                    AbilityEffect.Backstab, 1, power: 5),
                new AbilityDefinition("evade", "Evade", "+4 defense until your next turn. Click your own hex.",
                    AbilityEffect.Evade, 0, power: 4)
            }),
            new UnitDefinition("ranger", "Ranger", 4, 3, attackRange: 4, abilities: new[]
            {
                new AbilityDefinition("aimed-shot", "Aimed Shot", "Range 6. Weapon hit: +3 accuracy, +2 damage.",
                    AbilityEffect.AimedShot, 6, power: 2, attackBonus: 3),
                new AbilityDefinition("trap", "Trap", "Range 3. Empty hex: 8 damage on hostile entry. One active trap; allies safe.",
                    AbilityEffect.Trap, 3, power: 8)
            }),
            new UnitDefinition("mage", "Mage", 3, 1, attackRange: 3, abilities: new[]
            {
                new AbilityDefinition("fire-burst", "Fire Burst", "Range 4, radius 1. Roll a weapon hit against each enemy; allies safe.",
                    AbilityEffect.FireBurst, 4, radius: 1),
                new AbilityDefinition("blink", "Blink", "Range 3. Teleport to free ground, crossing obstacles; keep movement points.",
                    AbilityEffect.Blink, 3)
            })
            }.AsReadOnly();

        public static IReadOnlyList<UnitDefinition> Defaults => defaults;
    }
}
