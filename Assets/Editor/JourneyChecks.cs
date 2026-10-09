using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using GuildTactics.Combat;
using GuildTactics.Core;
using GuildTactics.Expeditions;
using GuildTactics.Generation;
using GuildTactics.Meta;
using GuildTactics.Units;
using UnityEditor;
using UnityEngine;

namespace GuildTactics.Editor
{
    public static class JourneyChecks
    {
        private sealed class Section
        {
            public readonly ExpeditionRun Run;
            public readonly TurnManager Turns;
            public readonly List<UnitRuntimeState> Heroes = new List<UnitRuntimeState>();
            public readonly UnitRuntimeState Enemy;
            public Section(ExpeditionJourney journey, GuildState guild, IReadOnlyList<GuildAdventurer> party)
            {
                var map = DungeonGenerator.Generate(journey.NextSectionSeed, journey.DungeonConfig);
                foreach (var hero in party)
                {
                    Require(UnitRuntimeState.TrySpawn(map.Grid, hero.Id, hero.Definition, map.PlayerSpawns[Heroes.Count], out var unit), "Spawn survivor");
                    unit.SetLoadout(hero.Weapon, hero.Armor, hero.HealingPotions);
                    unit.SetTraining(hero.TrainingAttack, hero.TrainingDefense);
                    unit.ApplyDamage(unit.Definition.MaxHealth - hero.Health);
                    Heroes.Add(unit);
                }
                var placement = EncounterGenerator.Generate(map, journey.EncounterConfig)[0];
                Require(UnitRuntimeState.TrySpawn(map.Grid, "enemy", placement.Archetype.Unit, placement.Position, out Enemy, UnitTeam.Enemy), "Spawn enemy");
                Turns = new TurnManager(map.Grid, Heroes.Concat(new[] { Enemy }));
                Run = new ExpeditionRun(map, Turns, ExpeditionSelection.Offers[journey.OfferIndex].Mission, journey.ResultComposer(guild));
                Turns.TryStartNextTurn();
                while (Turns.ActiveUnit.Team != UnitTeam.Player) { Turns.TryEndTurn(Turns.ActiveUnit); Turns.TryStartNextTurn(); }
            }

            public void Complete()
            {
                Enemy.ApplyDamage(Enemy.CurrentHealth);
                if (Run.Mission.RequiresRelic)
                {
                    Require(Turns.ActiveUnit.TryRelocate(Turns.Grid, Run.Chest), "Reach relic");
                    Require(Run.TryOpenChest(Turns.ActiveUnit), "Take relic");
                }
                RetreatChecks.ReachExit(Turns, Run);
                Require(Run.TryExtract(Turns.ActiveUnit, true), "Section extracted");
            }
        }

        [MenuItem("Tools/Guild Tactics/Validate Multi-section Expeditions")]
        public static void Run()
        {
            for (int offer = 0; offer < ExpeditionSelection.Offers.Count; offer++)
            for (int seed = 0; seed < 20; seed++)
                ValidateJourney(offer, seed);
            ValidatePersistence();
            Debug.Log("WP-30 model checks passed: 80 journeys, 2/3 sections, seed replay, wounds, potions, deaths, carried bodies, rewards, retreat, defeat, checkpoint reload and legacy saves.");
        }

        private static void ValidateJourney(int offer, int seed)
        {
            var guild = new GuildState();
            var data = GuildSaveData.Capture(guild, new ExpeditionSelection(seed, offer, 0));
            data.items = new[] { ItemDefinitions.Weapon.Id, ItemDefinitions.HealingDraught.Id, ItemDefinitions.HealingDraught.Id };
            guild = GuildState.Restore(data);
            guild.TryEquip("warrior-1", ItemDefinitions.Weapon.Id);
            guild.TryTransferPotion("warrior-1", true); guild.TryTransferPotion("warrior-1", true);
            var journey = new ExpeditionJourney(seed, offer, 12345, ExpeditionSelection.Offers[offer].CreateDungeonConfig(), ExpeditionSelection.Offers[offer].CreateEncounterConfig());
            var first = new Section(journey, guild, guild.BeginExpedition());
            guild.AttachRun(first.Run);
            var warrior = first.Heroes.Single(h => h.InstanceId == "warrior-1");
            warrior.ApplyDamage(12); Require(warrior.DrinkHealingPotion() == 8, "Consume one potion");
            var casualty = first.Heroes.Single(h => h.InstanceId == "mage-1");
            casualty.ApplyDamage(casualty.CurrentHealth);
            // Alternate carried and permanently lost bodies.
            bool recoverable = seed % 2 == 0;
            if (!recoverable) first.Turns.Grid.GetCell(casualty.Position).Terrain = GuildTactics.HexGrid.TerrainType.Pit;
            first.Complete(); journey.Complete(first.Run.Result); journey.TryResolveEvent(guild, 0);
            Require(first.Run.Result.Adventurers.Single(h => h.InstanceId == casualty.InstanceId).BodyRecovered == recoverable, "Body decision retained");
            int firstGold = first.Run.Result.Gold;
            Require(guild.Gold == 100 && warrior.HealingPotions == 1, "Rewards not yet paid");
            var saved = journey.Capture(); saved.party[0].health = 1;
            Require(journey.Capture().party[0].health != 1, "Checkpoint copy isolated");
            var next = new Section(journey, guild, journey.ContinuingParty(guild));
            guild.AttachContinuingRun(next.Run);
            var continuing = next.Heroes.Single(h => h.InstanceId == warrior.InstanceId);
            Require(next.Heroes.Count == 3 && continuing.CurrentHealth == warrior.CurrentHealth &&
                continuing.HealingPotions == 1 && continuing.Weapon == warrior.Weapon, "No heal, resupply or resurrection");
            Require(journey.NextSectionSeed != seed, "Distinct section seed");
            var replay = DungeonGenerator.Generate(journey.NextSectionSeed, journey.DungeonConfig);
            Require(replay.Grid.Cells.Select(c => c.Terrain).SequenceEqual(next.Turns.Grid.Cells.Select(c => c.Terrain)), "Section replay");
            if (seed % 3 == 0)
            {
                RetreatChecks.ReachExit(next.Turns, next.Run);
                var preview = next.Run.PreviewRetreat(next.Turns.ActiveUnit);
                Require(!next.Run.TryRetreat(false) && next.Run.Result == null && preview.Gold == firstGold, "Retreat cancellation and retained loot");
                Require(next.Run.TryRetreat(true) && !next.Run.TryRetreat(true), "Retreat once");
            }
            else if (seed % 3 == 1)
            {
                foreach (var hero in next.Heroes) hero.ApplyDamage(hero.CurrentHealth);
                next.Run.RefreshOutcome();
                Require(next.Run.Result.Gold == 0 && next.Run.Result.Items.Count == 0 &&
                    next.Run.Result.Adventurers.All(h => !h.Survived && !h.BodyRecovered), "Defeat loses all carried bodies and loot");
            }
            else
            {
                next.Complete(); journey.Complete(next.Run.Result); journey.TryResolveEvent(guild, 0);
                if (journey.Completed < journey.Sections)
                {
                    next = new Section(journey, guild, journey.ContinuingParty(guild));
                    guild.AttachContinuingRun(next.Run);
                    next.Complete(); journey.Complete(next.Run.Result); journey.TryResolveEvent(guild, 0);
                }
                Require(journey.Completed == journey.Sections && next.Run.Result.Items.Count == 2 * journey.Sections &&
                    next.Run.Result.Gold > firstGold, "All sections accumulate rewards once");
            }
            int reward = next.Run.Result.Gold;
            Require(guild.TryReturn() && !guild.TryReturn() && guild.Gold == 100 + reward, "Guild payout exactly once");
            Require(guild.Roster.Single(h => h.Id == casualty.InstanceId).Status ==
                (recoverable && next.Run.Result.Outcome != ExpeditionOutcome.Defeated ? AdventurerStatus.BodyRecovered : AdventurerStatus.Lost), "Casualty not resurrected");
            Require(guild.Roster.Single(h => h.Id == warrior.InstanceId).Experience ==
                (next.Run.Result.Outcome == ExpeditionOutcome.Extracted ? 100 : next.Run.Result.Outcome == ExpeditionOutcome.Retreated ? 25 : 0), "XP awarded once");
        }

        private static void ValidatePersistence()
        {
            string directory = Path.Combine(Path.GetTempPath(), "JourneyChecks-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                var guild = new GuildState();
                var offers = new ExpeditionSelection(99, 0, 0);
                var departure = GuildSaveData.Capture(guild, offers);
                var journey = new ExpeditionJourney(offers.NextSeed, 0, 31, new DungeonGenerationConfig(), offers.Selected.CreateEncounterConfig());
                var first = new Section(journey, guild, guild.BeginExpedition());
                first.Complete(); journey.Complete(first.Run.Result); journey.TryResolveEvent(guild, 0); offers.RecordLaunch();
                var store = new GuildSaveStore(Path.Combine(directory, "save.json"));
                Require(store.TrySaveCheckpoint(departure, offers, journey.Capture(), out _), "Write boundary");
                Require(store.TryLoad(out var restored, out var loadedOffers, out _) && store.LoadedCheckpoint != null &&
                    restored.Gold == 100 && loadedOffers.LaunchedCount == 1, "Load unpaid boundary");
                var resumed = ExpeditionJourney.Restore(store.LoadedCheckpoint, restored);
                Require(resumed.NextSectionSeed == journey.NextSectionSeed && resumed.CombatSeed == 31, "Restore generation streams");
                restored.BeginExpedition();
                var second = new Section(resumed, restored, resumed.ContinuingParty(restored));
                restored.AttachContinuingRun(second.Run);
                second.Heroes[0].ApplyDamage(5);
                Require(store.TryLoad(out var rollback, out _, out _) &&
                    ExpeditionJourney.Restore(store.LoadedCheckpoint, rollback).BoundaryResult(rollback).Adventurers.All(h => h.Health == h.MaxHealth), "Mid-section close rolls back only that section");
                second.Complete(); resumed.Complete(second.Run.Result); resumed.TryResolveEvent(restored, 0);
                Require(store.TrySaveCheckpoint(departure, loadedOffers, resumed.Capture(), out _), "Save final boundary");
                Require(store.TryLoad(out restored, out loadedOffers, out _), "Load final boundary");
                resumed = ExpeditionJourney.Restore(store.LoadedCheckpoint, restored);
                restored.BeginExpedition();
                Require(restored.TryReturn(resumed.BoundaryResult(restored)) && !restored.TryReturn(resumed.BoundaryResult(restored)), "Return loaded boundary once");
                Require(store.TrySave(restored, loadedOffers, out _) && store.TryLoad(out var paid, out _, out _) &&
                    store.LoadedCheckpoint == null && paid.Gold == restored.Gold, "Paid save clears journey");
                // A corrupt journey follows the same backup recovery policy as a corrupt guild.
                var bad = JsonUtility.FromJson<GuildSaveData>(File.ReadAllText(store.Path + ".bak"));
                bad.journey.party[0].health = -1;
                File.WriteAllText(store.Path, JsonUtility.ToJson(bad));
                Require(store.TryLoad(out _, out _, out var message) && message != null && store.LoadedCheckpoint != null, "Bad checkpoint recovers backup");
                for (int version = 1; version <= 4; version++)
                {
                    var legacy = GuildSaveData.Capture(new GuildState(), new ExpeditionSelection("old"));
                    legacy.version = version;
                    File.WriteAllText(store.Path, JsonUtility.ToJson(legacy));
                    Require(store.TryLoad(out var old, out _, out _) && old.Gold == 100 && store.LoadedCheckpoint == null, "Load legacy version " + version);
                }
                var failing = new GuildSaveStore(Path.Combine(store.Path, "child.json"));
                Require(!failing.TrySaveCheckpoint(departure, offers, journey.Capture(), out var failure) && failure != null, "Checkpoint IO error reported");
            }
            finally { Directory.Delete(directory, true); }
        }

        public static void ValidatePresentation(GameBootstrap bootstrap)
        {
            // This follows RetreatChecks, which leaves an active expedition.
            var previous = bootstrap.ActiveController;
            foreach (var enemy in previous.Enemies) enemy.ApplyDamage(enemy.CurrentHealth);
            for (int i = 0; i <= previous.Turns.Order.Count &&
                (previous.Turns.ActiveUnit.Team != UnitTeam.Player || !previous.Turns.ActiveUnit.IsAlive); i++)
            { previous.Turns.TryEndTurn(previous.Turns.ActiveUnit); previous.Turns.TryStartNextTurn(); }
            if (previous.Expedition.Mission.RequiresRelic)
            {
                Require(previous.Turns.ActiveUnit.TryRelocate(bootstrap.Grid, previous.Expedition.Chest), "Scene reach relic");
                Require(previous.Expedition.TryOpenChest(previous.Turns.ActiveUnit), "Scene take relic");
            }
            RetreatChecks.ReachExit(previous.Turns, previous.Expedition);
            previous.Turns.ActiveUnit.ApplyDamage(3);
            var wounded = previous.Turns.ActiveUnit;
            var casualty = previous.Units.First(h => h != previous.Turns.ActiveUnit);
            casualty.ApplyDamage(casualty.CurrentHealth);
            Require(previous.Expedition.TryExtract(previous.Turns.ActiveUnit, true), "Scene first section");
            int launched = bootstrap.Expeditions.LaunchedCount;
            bootstrap.CaptureJourneyBoundary();
            bootstrap.CaptureJourneyBoundary();
            Require(bootstrap.Journey.Completed == 1 && bootstrap.Journey.EventPending && !bootstrap.CanContinueJourney, "Boundary capture idempotent");
            ExplorationEventChecks.ValidateBoundary(bootstrap);
            Require(bootstrap.TryContinueJourney() && !bootstrap.TryContinueJourney(), "Continue once");
            Require(!previous.gameObject.activeSelf && !previous.TryRetreat(true) && !previous.TryEndTurn(), "Old input disabled immediately");
            Require(bootstrap.ActiveController.Units.Count == 3 && bootstrap.Expeditions.LaunchedCount == launched &&
                bootstrap.ActiveController.Units.Any(h => h.InstanceId == wounded.InstanceId && h.CurrentHealth == wounded.CurrentHealth), "Scene state crosses sections");
            var current = bootstrap.ActiveController;
            for (int i = 0; i <= current.Turns.Order.Count && current.Turns.ActiveUnit.Team != UnitTeam.Player; i++)
            { current.Turns.TryEndTurn(current.Turns.ActiveUnit); current.SendMessage("StartNextTurn"); }
            RetreatChecks.ReachExit(current.Turns, current.Expedition);
            Require(current.TryRetreat(true) && bootstrap.TryReturnToGuild() && !bootstrap.TryReturnToGuild(), "Return continued expedition once");
            Require(bootstrap.Guild.TryResurrect(casualty.InstanceId) && bootstrap.Guild.TryToggleSelection(casualty.InstanceId) &&
                bootstrap.TryLaunchExpedition(), "Next expedition remains available");
            Debug.Log("WP-30 Play Mode passed: boundary, continue, casualties/wounds, stale controller rejection, return and fresh expedition.");
        }

        private static void Require(bool value, string message)
        { if (!value) throw new InvalidOperationException("WP-30: " + message); }
    }
}
