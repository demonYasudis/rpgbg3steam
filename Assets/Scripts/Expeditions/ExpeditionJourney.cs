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
        public bool explorationEnabled;
        public int eventGold;
        public ExplorationEventResult[] events;

        internal void Validate(GuildState guild)
        {
            if (offer < 0 || offer >= ExpeditionSelection.Offers.Count || completed < 1 ||
                completed > ExpeditionSelection.Offers[offer].Sections || gold < 0 ||
                items == null || items.Length > 6 || party == null || party.Length != GuildState.PartySize ||
                dungeon == null || encounter == null || guild.SelectedIds.Count != GuildState.PartySize)
                throw new ArgumentException("Invalid journey checkpoint.");
            dungeon.Validate(); encounter.Validate();
            if (dungeon.Biome != ExpeditionSelection.Offers[offer].Biome || encounter.Biome != dungeon.Biome)
                throw new ArgumentException("Invalid checkpoint biome.");
            var seen = new HashSet<string>();
            foreach (var saved in party)
            {
                var hero = saved == null ? null : guild.Roster.FirstOrDefault(h => h.Id == saved.id);
                if (hero == null || !guild.SelectedIds.Contains(saved.id) || !seen.Add(saved.id) ||
                    saved.health < 0 || saved.health > hero.Definition.MaxHealth ||
                    saved.potions < 0 || saved.potions > hero.HealingPotions || (saved.health > 0 && saved.recovered))
                    throw new ArgumentException("Invalid journey adventurer.");
            }
            if (events != null)
            {
                if (events.Length > completed || events.Select(e => e?.section).Distinct().Count() != events.Length)
                    throw new ArgumentException("Duplicate exploration results.");
                foreach (var result in events)
                {
                    ExplorationEvents.Validate(result, seed, completed, party, dungeon.Biome);
                    var hero = guild.Roster.FirstOrDefault(h => h.Id == result.hero);
                    if (result.choice == 1)
                    {
                        var definition = ExplorationEvents.At(seed, result.section, dungeon.Biome);
                        bool success = result.roll >= definition.Difficulty;
                        int expected = Math.Max(0, Math.Min(hero.Definition.MaxHealth, result.healthBefore +
                            (success ? definition.Healing - definition.SuccessDamage : -definition.FailureDamage)));
                        if (result.healthBefore > hero.Definition.MaxHealth || result.healthAfter != expected ||
                            (result.section == completed && party.First(h => h.id == result.hero).health != expected))
                            throw new ArgumentException("Invalid exploration health effect.");
                    }
                }
            }
            if (eventGold < 0 || eventGold != (events?.Sum(e => e.gold) ?? 0) || (!explorationEnabled && (eventGold != 0 || (events?.Length ?? 0) != 0)))
                throw new ArgumentException("Invalid exploration rewards.");
            if (!party.Any(p => p.health > 0) && !(explorationEnabled && events != null &&
                events.Any(e => e.section == completed && e.choice == 1 && e.healthAfter == 0)))
                throw new ArgumentException("Checkpoint needs a survivor or a fatal exploration result.");
            foreach (var id in items) FindItem(id);
            var mission = ExpeditionSelection.Offers[offer].Mission;
            if (gold - eventGold < completed * mission.MinimumGold || gold - eventGold > completed * mission.MaximumGold ||
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
        public bool IsDefeated => checkpoint != null && !checkpoint.party.Any(p => p.health > 0);
        public bool EventPending => checkpoint != null && checkpoint.explorationEnabled && LastEvent == null && !IsDefeated;
        public ExplorationEventDefinition CurrentEvent => checkpoint == null ? null : ExplorationEvents.At(Seed, Completed, DungeonConfig.Biome);
        public ExplorationEventResult LastEvent => checkpoint?.events?.FirstOrDefault(e => e.section == Completed)?.Copy();
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
            if (DungeonConfig.Biome != ExpeditionSelection.Offers[offer].Biome || EncounterConfig.Biome != DungeonConfig.Biome)
                throw new ArgumentException("Journey profiles must match the offer biome.");
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
            return new ExpeditionResult(Seed, IsDefeated ? ExpeditionOutcome.Defeated : ExpeditionOutcome.Extracted, IsDefeated ? 0 : checkpoint.gold,
                IsDefeated ? Array.Empty<ItemDefinition>() : checkpoint.items.Select(JourneyCheckpoint.FindItem), checkpoint.party.Select(p =>
                    new AdventurerResult(p.id, guild.Roster.First(h => h.Id == p.id).Definition, p.health, p.potions, !IsDefeated && p.recovered)));
        }

        internal IReadOnlyList<GuildAdventurer> ContinuingParty(GuildState guild)
        {
            if (checkpoint == null || Completed >= Sections || EventPending || IsDefeated) throw new InvalidOperationException("No next section.");
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

        internal bool TryResolveEvent(GuildState guild, int choice, string heroId = null)
        {
            if (!EventPending || choice < 0 || choice > 1) return false;
            var hero = checkpoint.party.FirstOrDefault(p => p.id == heroId && p.health > 0);
            if (choice == 1 && hero == null) return false;
            var result = new ExplorationEventResult { section = Completed, choice = choice };
            if (choice == 1)
            {
                var definition = CurrentEvent;
                result.hero = heroId;
                result.roll = ExplorationEvents.Roll(Seed, Completed);
                bool success = result.roll >= definition.Difficulty;
                result.gold = success ? definition.Gold : 0;
                result.healthBefore = hero.health;
                int maximum = guild.Roster.First(h => h.Id == heroId).Definition.MaxHealth;
                hero.health = Math.Max(0, Math.Min(maximum, hero.health +
                    (success ? definition.Healing - definition.SuccessDamage : -definition.FailureDamage)));
                result.healthAfter = hero.health;
                hero.recovered = hero.health == 0;
                checkpoint.gold = checked(checkpoint.gold + result.gold);
                checkpoint.eventGold += result.gold;
            }
            checkpoint.events = (checkpoint.events ?? Array.Empty<ExplorationEventResult>()).Concat(new[] { result }).ToArray();
            return true;
        }

        internal void Complete(ExpeditionResult result)
        {
            if (result == null || result.Outcome != ExpeditionOutcome.Extracted || Completed >= Sections || EventPending || IsDefeated || result.Seed != Seed)
                throw new InvalidOperationException("Section is not complete.");
            checkpoint = new JourneyCheckpoint
            {
                seed = Seed, offer = OfferIndex, combatSeed = CombatSeed, completed = Completed + 1,
                gold = result.Gold, items = result.Items.Select(i => i.Id).ToArray(),
                party = result.Adventurers.Select(h => new JourneyAdventurer
                    { id = h.InstanceId, health = h.Health, potions = h.HealingPotions, recovered = h.BodyRecovered }).ToArray(),
                dungeon = DungeonConfig, encounter = EncounterConfig,
                explorationEnabled = true, eventGold = checkpoint?.eventGold ?? 0,
                events = checkpoint?.events ?? Array.Empty<ExplorationEventResult>()
            };
        }
    }
}
