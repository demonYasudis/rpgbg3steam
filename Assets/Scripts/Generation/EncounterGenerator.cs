using System;
using System.Collections.Generic;
using GuildTactics.HexGrid;
using GuildTactics.Units;

namespace GuildTactics.Generation
{
    [Serializable]
    public sealed class EncounterConfig
    {
        public const int MaximumSupportedEnemies = 8;
        public int Budget = 10;
        public int MinEnemies = 3;
        public int MaxEnemies = 5;
        public int MiniBossPercent = 20;

        public void Validate()
        {
            if (MinEnemies < 1 || MaxEnemies < MinEnemies || MaxEnemies > MaximumSupportedEnemies ||
                Budget < MinEnemies || Budget > 100 || MiniBossPercent < 0 || MiniBossPercent > 100)
                throw new ArgumentException("Invalid encounter configuration.");
        }
    }

    public sealed class EnemyArchetype
    {
        public UnitDefinition Unit { get; }
        public int Cost { get; }
        public bool IsMiniBoss { get; }
        internal EnemyArchetype(UnitDefinition unit, int cost, bool isMiniBoss = false)
        { Unit = unit; Cost = cost; IsMiniBoss = isMiniBoss; }
    }

    public static class EnemyDefinitions
    {
        // Four melee profiles reuse the proven brain; ranged behaviour is a later content choice.
        public static IReadOnlyList<EnemyArchetype> Regular { get; } = Array.AsReadOnly(new[]
        {
            new EnemyArchetype(new UnitDefinition("ash-crawler", "Ash Crawler", 5, 4,
                maxHealth: 8, attack: 2, defense: 10, damageDie: 4, damageBonus: 0), 1),
            new EnemyArchetype(new UnitDefinition("crypt-warden", "Crypt Warden", 3, 2,
                maxHealth: 16, attack: 3, defense: 13, damageDie: 6, damageBonus: 1), 2),
            new EnemyArchetype(new UnitDefinition("veil-stalker", "Veil Stalker", 5, 5,
                maxHealth: 12, attack: 5, defense: 12, damageDie: 6, damageBonus: 2), 3),
            new EnemyArchetype(new UnitDefinition("hollow-brute", "Hollow Brute", 2, 0,
                maxHealth: 28, attack: 4, defense: 11, damageDie: 8, damageBonus: 3), 4)
        });
        public static EnemyArchetype MiniBoss { get; } = new EnemyArchetype(
            new UnitDefinition("cinder-keeper", "Cinder Keeper", 3, 2,
                maxHealth: 40, attack: 5, defense: 14, damageDie: 8, damageBonus: 3), 6, true);
    }

    public sealed class EnemyPlacement
    {
        public EnemyArchetype Archetype { get; }
        public HexCoordinates Position { get; }
        internal EnemyPlacement(EnemyArchetype archetype, HexCoordinates position)
        { Archetype = archetype; Position = position; }
    }

    public static class EncounterGenerator
    {
        public static IReadOnlyList<EnemyPlacement> Generate(DungeonMap map, EncounterConfig config = null)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            config = config ?? new EncounterConfig(); config.Validate();
            var candidates = new List<HexCoordinates>();
            var seen = new HashSet<HexCoordinates>(map.PlayerSpawns);
            seen.Add(map.Objective);
            foreach (var position in map.EnemyCandidates)
                if (map.Grid.TryGetCell(position, out var cell) && !cell.IsOccupied &&
                    TerrainRules.CanWalk(cell.Terrain) && seen.Add(position)) candidates.Add(position);
            if (candidates.Count < config.MinEnemies)
                throw new InvalidOperationException("Not enough legal encounter cells; no units were spawned.");
            var random = new GenerationRandom(unchecked(map.Seed ^ (int)0x9E3779B9));
            for (int i = candidates.Count - 1; i > 0; i--)
            {
                int j = random.Next(i + 1);
                var swap = candidates[i]; candidates[i] = candidates[j]; candidates[j] = swap;
            }
            int maximum = Math.Min(config.MaxEnemies, Math.Min(candidates.Count, config.Budget));
            int count = config.MinEnemies + random.Next(maximum - config.MinEnemies + 1);
            int remaining = config.Budget;
            var placements = new List<EnemyPlacement>();
            bool boss = random.Next(100) < config.MiniBossPercent &&
                remaining >= EnemyDefinitions.MiniBoss.Cost + count - 1;
            for (int i = 0; i < count; i++)
            {
                int allowance = remaining - (count - i - 1); // Reserve the cheapest unit for each remaining slot.
                EnemyArchetype choice;
                if (i == 0 && boss) choice = EnemyDefinitions.MiniBoss;
                else
                {
                    var affordable = new List<EnemyArchetype>();
                    foreach (var archetype in EnemyDefinitions.Regular)
                        if (archetype.Cost <= allowance) affordable.Add(archetype);
                    choice = affordable[random.Next(affordable.Count)];
                }
                remaining -= choice.Cost;
                placements.Add(new EnemyPlacement(choice, candidates[i]));
            }
            return placements.AsReadOnly();
        }
    }
}
