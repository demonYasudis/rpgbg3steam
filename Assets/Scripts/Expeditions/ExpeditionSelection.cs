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
        public string Reward => "20–60 gold, weapon or armor, healing draught";
        private readonly int budget, minimum, maximum, bossChance;
        internal ExpeditionOffer(string name, string difficulty, int budget, int minimum, int maximum, int bossChance)
        {
            Name = name; Difficulty = difficulty; this.budget = budget;
            this.minimum = minimum; this.maximum = maximum; this.bossChance = bossChance;
        }
        public EncounterConfig CreateEncounterConfig() => new EncounterConfig
        { Budget = budget, MinEnemies = minimum, MaxEnemies = maximum, MiniBossPercent = bossChance };
    }

    public sealed class ExpeditionSelection
    {
        public static IReadOnlyList<ExpeditionOffer> Offers { get; } = Array.AsReadOnly(new[]
        {
            new ExpeditionOffer("Outer crypt", "Low risk · 2–3 enemies · no keeper", 4, 2, 3, 0),
            new ExpeditionOffer("Deep crypt", "Dangerous · 3–5 enemies · possible keeper", 10, 3, 5, 20)
        });
        private readonly int initialSeed;
        public int SelectedIndex { get; private set; } = 1;
        public int LaunchedCount { get; private set; }
        public ExpeditionOffer Selected => Offers[SelectedIndex];
        // Odd increment visits distinct int seeds before wrapping. No global Unity random state.
        public int NextSeed => unchecked(initialSeed + LaunchedCount * (int)0x9E3779B9);
        public ExpeditionSelection(string seed) => initialSeed = GenerationRandom.ParseSeed(seed);
        public bool TrySelect(int index)
        {
            if (index < 0 || index >= Offers.Count) return false;
            SelectedIndex = index; return true;
        }
        public void RecordLaunch() => LaunchedCount = unchecked(LaunchedCount + 1);
    }
}
