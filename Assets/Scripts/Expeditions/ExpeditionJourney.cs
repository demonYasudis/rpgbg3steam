using System;
using System.Collections.Generic;
using System.Linq;
using GuildTactics.Generation;
using GuildTactics.Meta;
using UnityEngine;

namespace GuildTactics.Expeditions
{
    [Serializable]
    public sealed class JourneyAdventurer
    {
        public string id;
        public int health, potions;
        public bool recovered;
    }

    // Saved only after a successful section. The guild itself is still the pre-departure snapshot.
    [Serializable]
    public sealed class JourneyCheckpoint
    {
        public int seed, offer, completed, gold, combatSeed;
        public string[] items;
        public JourneyAdventurer[] party;
        public DungeonGenerationConfig dungeon;
        public EncounterConfig encounter;

        internal void Validate(GuildState guild)
        {
            if (offer < 0 || offer >= ExpeditionSelection.Offers.Count || completed < 1 ||
                completed > ExpeditionSelection.Offers[offer].Sections || gold < 0 ||
                items == null || items.Length > 6 || party == null || party.Length != GuildState.PartySize ||
                dungeon == null || encounter == null || guild.SelectedIds.Count != GuildState.PartySize)
                throw new ArgumentException("Invalid journey checkpoint.");
            dungeon.Validate(); encounter.Validate();
            var seen = new HashSet<string>();
            foreach (var saved in party)
            {
                var hero = saved == null ? null : guild.Roster.FirstOrDefault(h => h.Id == saved.id);
                if (hero == null || !guild.SelectedIds.Contains(saved.id) || !seen.Add(saved.id) ||
                    saved.health < 0 || saved.health > hero.Definition.MaxHealth ||
                    saved.potions < 0 || saved.potions > hero.HealingPotions || (saved.health > 0 && saved.recovered))
                    throw new ArgumentException("Invalid journey adventurer.");
            }
            if (!party.Any(p => p.health > 0)) throw new ArgumentException("Checkpoint needs a survivor.");
            foreach (var id in items) FindItem(id);
            var mission = ExpeditionSelection.Offers[offer].Mission;
            if (gold < completed * mission.MinimumGold || gold > completed * mission.MaximumGold ||
                items.Length != completed * (mission.GrantsItems ? 2 : 0))
                throw new ArgumentException("Invalid accumulated rewards.");
        }

        internal static ItemDefinition FindItem(string id) => ItemDefinitions.All.FirstOrDefault(i => i.Id == id)
            ?? throw new ArgumentException("Unknown journey item.");
        internal JourneyCheckpoint Copy() => JsonUtility.FromJson<JourneyCheckpoint>(JsonUtility.ToJson(this));
    }

    /// <summary>Section seeds, carried casualties and rewards; guild rewards are applied only at final return.</summary>
    public sealed class ExpeditionJourney
    {
        private JourneyCheckpoint checkpoint;
        public int Seed { get; }
        public int OfferIndex { get; }
        public int CombatSeed { get; }
        public int Completed => checkpoint?.completed ?? 0;
        public int Sections => ExpeditionSelection.Offers[OfferIndex].Sections;
        public int CarriedGold => checkpoint?.gold ?? 0;
        public int CarriedItemCount => checkpoint?.items.Length ?? 0;
        public DungeonGenerationConfig DungeonConfig { get; }
        public EncounterConfig EncounterConfig { get; }
        public int NextSectionSeed => SectionSeed(Seed, Completed);
        public static int SectionSeed(int seed, int index) => unchecked(seed + index * (int)0x85EBCA6B);

        public ExpeditionJourney(int seed, int offer, int combatSeed, DungeonGenerationConfig dungeon, EncounterConfig encounter)
        {
            if (offer < 0 || offer >= ExpeditionSelection.Offers.Count) throw new ArgumentOutOfRangeException(nameof(offer));
            Seed = seed; OfferIndex = offer; CombatSeed = combatSeed;
            DungeonConfig = JsonUtility.FromJson<DungeonGenerationConfig>(JsonUtility.ToJson(dungeon));
            EncounterConfig = JsonUtility.FromJson<EncounterConfig>(JsonUtility.ToJson(encounter));
            DungeonConfig.Validate(); EncounterConfig.Validate();
        }

        internal static ExpeditionJourney Restore(JourneyCheckpoint saved, GuildState guild)
        {
            saved.Validate(guild);
            var journey = new ExpeditionJourney(saved.seed, saved.offer, saved.combatSeed, saved.dungeon, saved.encounter);
            journey.checkpoint = saved.Copy();
            return journey;
        }

        public JourneyCheckpoint Capture() => checkpoint?.Copy();

        internal ExpeditionResult BoundaryResult(GuildState guild)
        {
            if (checkpoint == null) return null;
            return new ExpeditionResult(Seed, ExpeditionOutcome.Extracted, checkpoint.gold,
                checkpoint.items.Select(JourneyCheckpoint.FindItem), checkpoint.party.Select(p =>
                    new AdventurerResult(p.id, guild.Roster.First(h => h.Id == p.id).Definition, p.health, p.potions, p.recovered)));
        }

        internal IReadOnlyList<GuildAdventurer> ContinuingParty(GuildState guild)
        {
            if (checkpoint == null || Completed >= Sections) throw new InvalidOperationException("No next section.");
            return checkpoint.party.Where(p => p.health > 0).Select(p =>
            {
                var original = guild.Roster.First(h => h.Id == p.id);
                return new GuildAdventurer(p.id, original.Definition)
                {
                    Health = p.health, Weapon = original.Weapon, Armor = original.Armor,
                    HealingPotions = p.potions, Experience = original.Experience,
                    TrainingAttack = original.TrainingAttack, TrainingDefense = original.TrainingDefense
                };
            }).ToArray();
        }

        // Capture the prior checkpoint in each run: later session changes cannot alter an old run's result.
        internal Func<ExpeditionResult, ExpeditionResult> ResultComposer(GuildState guild)
        {
            var prior = BoundaryResult(guild);
            return local =>
            {
                var heroes = new List<AdventurerResult>(local.Adventurers);
                if (prior != null)
                    foreach (var dead in prior.Adventurers.Where(h => !h.Survived))
                        heroes.Add(local.Outcome == ExpeditionOutcome.Defeated ?
                            new AdventurerResult(dead.InstanceId, guild.Roster.First(h => h.Id == dead.InstanceId).Definition,
                                0, dead.HealingPotions, false) : dead);
                bool lost = local.Outcome == ExpeditionOutcome.Defeated;
                return new ExpeditionResult(Seed, local.Outcome, lost ? 0 : checked(local.Gold + (prior?.Gold ?? 0)),
                    lost ? Array.Empty<ItemDefinition>() : local.Items.Concat(prior?.Items ?? Array.Empty<ItemDefinition>()), heroes);
            };
        }

        internal void Complete(ExpeditionResult result)
        {
            if (result == null || result.Outcome != ExpeditionOutcome.Extracted || Completed >= Sections || result.Seed != Seed)
                throw new InvalidOperationException("Section is not complete.");
            checkpoint = new JourneyCheckpoint
            {
                seed = Seed, offer = OfferIndex, combatSeed = CombatSeed, completed = Completed + 1,
                gold = result.Gold, items = result.Items.Select(i => i.Id).ToArray(),
                party = result.Adventurers.Select(h => new JourneyAdventurer
                    { id = h.InstanceId, health = h.Health, potions = h.HealingPotions, recovered = h.BodyRecovered }).ToArray(),
                dungeon = DungeonConfig, encounter = EncounterConfig
            };
        }
    }
}
