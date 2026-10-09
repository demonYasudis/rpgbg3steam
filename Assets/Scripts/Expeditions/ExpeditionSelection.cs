using System;
using System.Collections.Generic;
using GuildTactics.Generation;

namespace GuildTactics.Expeditions
{
    /// <summary>Immutable offers and a reproducible sequence of fresh expedition seeds.</summary>
    public sealed class ExpeditionOffer
    {
        public string Name { get; }
        public string Difficulty { get; }
        public MissionDefinition Mission { get; }
        public int Sections { get; }
        public DungeonBiome Biome { get; }
        public string Reward => Mission.MinimumGold + "–" + Mission.MaximumGold + " gold, weapon or armor, healing draught";
        private readonly int budget, minimum, maximum, bossChance;
        internal ExpeditionOffer(string name, string difficulty, int budget, int minimum, int maximum, int bossChance, MissionDefinition mission, DungeonBiome biome = DungeonBiome.Crypt, int sections = 0)
        {
            Name = name; Difficulty = difficulty; this.budget = budget;
            this.minimum = minimum; this.maximum = maximum; this.bossChance = bossChance;
            Mission = mission; Biome = biome;
            Sections = sections == 0 ? (mission == MissionDefinition.Relic ? 2 : 3) : sections;
        }
        public EncounterConfig CreateEncounterConfig() => new EncounterConfig
        { Biome = Biome, Budget = budget, MinEnemies = minimum, MaxEnemies = maximum, MiniBossPercent = bossChance };
        public DungeonGenerationConfig CreateDungeonConfig() => Biome == DungeonBiome.FloodedCellar ?
            new DungeonGenerationConfig { Biome = Biome, HighGroundPercent = 18, PitPercent = 0, MinimumWalkableCells = 55 } :
            new DungeonGenerationConfig();
    }

    public sealed class ExpeditionSelection
    {
        public static IReadOnlyList<ExpeditionOffer> Offers { get; } = Array.AsReadOnly(new[]
        {
            new ExpeditionOffer("Outer crypt", "Low risk · 2–3 enemies · no keeper", 4, 2, 3, 0, MissionDefinition.Relic),
            new ExpeditionOffer("Deep crypt", "Dangerous · 3–5 enemies · possible keeper", 10, 3, 5, 20, MissionDefinition.Clear),
            new ExpeditionOffer("Marked quarry", "Dangerous · 3–5 enemies · possible keeper", 10, 3, 5, 20, MissionDefinition.Hunt),
            new ExpeditionOffer("Flooded cellars", "Moderate · 3–4 enemies · deep water", 8, 3, 4, 0,
                new MissionDefinition(MissionType.ClearArea, "Clear area", 30, 50), DungeonBiome.FloodedCellar, 2)
        });
        private readonly int initialSeed;
        public int InitialSeed => initialSeed;
        public int SelectedIndex { get; private set; } = 1;
        public int LaunchedCount { get; private set; }
        public ExpeditionOffer Selected => Offers[SelectedIndex];
        // Odd increment visits distinct int seeds before wrapping. No global Unity random state.
        public int NextSeed => unchecked(initialSeed + LaunchedCount * (int)0x9E3779B9);
        public ExpeditionSelection(string seed) => initialSeed = GenerationRandom.ParseSeed(seed);
        public ExpeditionSelection(int seed, int selectedIndex, int launchedCount)
        {
            if (selectedIndex < 0 || selectedIndex >= Offers.Count || launchedCount < 0)
                throw new ArgumentException("Invalid expedition progression.");
            initialSeed = seed; SelectedIndex = selectedIndex; LaunchedCount = launchedCount;
        }
        public bool TrySelect(int index)
        {
            if (index < 0 || index >= Offers.Count) return false;
            SelectedIndex = index; return true;
        }
        public void RecordLaunch() => LaunchedCount = unchecked(LaunchedCount + 1);
    }
}
