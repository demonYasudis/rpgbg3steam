using System;

namespace GuildTactics.Abilities
{
    public enum AbilityEffect { HeavyStrike, Push, Backstab, Evade, AimedShot, Trap, FireBurst, Blink }

    /// <summary>Immutable content data; effects are a deliberately small, fixed set.</summary>
    public sealed class AbilityDefinition
    {
        public string Id { get; }
        public string Name { get; }
        public string Description { get; }
        public AbilityEffect Effect { get; }
        public int Range { get; }
        public int Power { get; }
        public int AttackBonus { get; }
        public int Radius { get; }

        public AbilityDefinition(string id, string name, string description, AbilityEffect effect,
            int range, int power = 0, int attackBonus = 0, int radius = 0)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Ability ID is required.", nameof(id));
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Ability name is required.", nameof(name));
            if (!Enum.IsDefined(typeof(AbilityEffect), effect)) throw new ArgumentOutOfRangeException(nameof(effect));
            if (range < 0) throw new ArgumentOutOfRangeException(nameof(range));
            if (power < 0) throw new ArgumentOutOfRangeException(nameof(power));
            if (radius < 0) throw new ArgumentOutOfRangeException(nameof(radius));
            Id = id;
            Name = name;
            Description = description ?? "";
            Effect = effect;
            Range = range;
            Power = power;
            AttackBonus = attackBonus;
            Radius = radius;
        }
    }
}
