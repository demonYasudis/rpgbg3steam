using System.Collections.Generic;

namespace GuildTactics.Units
{
    public static class HeroDefinitions
    {
        private static readonly IReadOnlyList<UnitDefinition> defaults =
            new List<UnitDefinition>
            {
            new UnitDefinition("warrior", "Warrior", 4),
            new UnitDefinition("rogue", "Rogue", 5),
            new UnitDefinition("ranger", "Ranger", 4),
            new UnitDefinition("mage", "Mage", 3)
            }.AsReadOnly();

        public static IReadOnlyList<UnitDefinition> Defaults => defaults;
    }
}
