using System;
using System.Collections.Generic;

namespace GuildTactics.Expeditions
{
    public enum ItemCategory { Weapon, Armor, HealingConsumable }

    /// <summary>Immutable reward data. Equipment/use is deferred until inventory gameplay exists.</summary>
    public sealed class ItemDefinition
    {
        public string Id { get; }
        public string Name { get; }
        public ItemCategory Category { get; }
        public int AttackBonus { get; }
        public int DamageDie { get; }
        public int DefenseBonus { get; }
        public int Healing { get; }

        public ItemDefinition(string id, string name, ItemCategory category, int attackBonus = 0,
            int damageDie = 0, int defenseBonus = 0, int healing = 0)
        {
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Items require an ID and a name.");
            if (category < ItemCategory.Weapon || category > ItemCategory.HealingConsumable ||
                attackBonus < 0 || damageDie < 0 || defenseBonus < 0 || healing < 0 ||
                (category == ItemCategory.Weapon && (damageDie == 0 || defenseBonus != 0 || healing != 0)) ||
                (category == ItemCategory.Armor && (defenseBonus == 0 || damageDie != 0 || attackBonus != 0 || healing != 0)) ||
                (category == ItemCategory.HealingConsumable && (healing == 0 || damageDie != 0 || attackBonus != 0 || defenseBonus != 0)))
                throw new ArgumentException("Item stats must match its category.");
            Id = id; Name = name; Category = category; AttackBonus = attackBonus;
            DamageDie = damageDie; DefenseBonus = defenseBonus; Healing = healing;
        }
    }

    public static class ItemDefinitions
    {
        public static ItemDefinition Weapon { get; } = new ItemDefinition(
            "iron-edge", "Iron Edge", ItemCategory.Weapon, attackBonus: 1, damageDie: 8);
        public static ItemDefinition Armor { get; } = new ItemDefinition(
            "crypt-mail", "Crypt Mail", ItemCategory.Armor, defenseBonus: 2);
        public static ItemDefinition HealingDraught { get; } = new ItemDefinition(
            "healing-draught", "Healing Draught", ItemCategory.HealingConsumable, healing: 8);
        public static IReadOnlyList<ItemDefinition> All { get; } = Array.AsReadOnly(
            new[] { Weapon, Armor, HealingDraught });
    }
}
