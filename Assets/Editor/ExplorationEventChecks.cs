using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using GuildTactics.Combat;
using GuildTactics.Core;
using GuildTactics.Expeditions;
using GuildTactics.Generation;
using GuildTactics.Meta;
using UnityEditor;
using UnityEngine;

namespace GuildTactics.Editor
{
    public static class ExplorationEventChecks
    {
        private static ExpeditionJourney Boundary(GuildState guild, ExpeditionSelection offers, int health = 12, bool lone = false)
        {
            var journey = new ExpeditionJourney(offers.NextSeed, offers.SelectedIndex, 73,
                offers.Selected.CreateDungeonConfig(), offers.Selected.CreateEncounterConfig());
            var heroes = guild.Roster.Where(h => guild.SelectedIds.Contains(h.Id)).Select((h, index) =>
                new AdventurerResult(h.Id, h.Definition, lone && index > 0 ? 0 : health, h.HealingPotions, lone && index > 0));
            journey.Complete(new ExpeditionResult(journey.Seed, ExpeditionOutcome.Extracted, offers.Selected.Mission.MinimumGold,
                new[] { ItemDefinitions.Weapon, ItemDefinitions.HealingDraught }, heroes));
            return journey;
        }

        [MenuItem("Tools/Guild Tactics/Validate Exploration Events")]
        public static void Run()
        {
            var covered = new HashSet<string>();
            for (int seed = 0; seed < 200; seed++)
            {
                var guild = new GuildState();
                var offers = new ExpeditionSelection(seed, seed % 3, 0);
                var journey = Boundary(guild, offers);
                var pending = journey.Capture();
                var replay = ExpeditionJourney.Restore(pending, guild);
                Require(journey.EventPending && !journey.TryResolveEvent(guild, -1) &&
                    !journey.TryResolveEvent(guild, 1, "missing"), "Invalid choices do not consume event");
                var hero = pending.party[0];
                var dice = new SeededDice(73);
                int combatRoll = dice.Roll(20);
                Require(journey.TryResolveEvent(guild, 1, hero.id) && replay.TryResolveEvent(guild, 1, hero.id), "Choose once");
                Require(new SeededDice(73).Roll(20) == combatRoll, "Separate combat stream");
                var result = journey.LastEvent;
                bool success = result.roll >= journey.CurrentEvent.Difficulty;
                covered.Add(journey.CurrentEvent.Id + (success ? ":success" : ":failure"));
                int expectedHealth = Math.Max(0, Math.Min(20, hero.health + (success ?
                    journey.CurrentEvent.Healing - journey.CurrentEvent.SuccessDamage : -journey.CurrentEvent.FailureDamage)));
                Require(result.healthAfter == expectedHealth && result.gold == (success ? journey.CurrentEvent.Gold : 0), "Data effects match roll");
                Require(JsonUtility.ToJson(journey.Capture()) == JsonUtility.ToJson(replay.Capture()), "Seed and choice replay");
                var reloaded = ExpeditionJourney.Restore(journey.Capture(), guild);
                Require(!reloaded.EventPending && !reloaded.TryResolveEvent(guild, 1, hero.id) &&
                    reloaded.CarriedGold == journey.CarriedGold && reloaded.LastEvent.roll == result.roll, "Resolved load cannot apply twice");
                var payoutGuild = new GuildState();
                payoutGuild.BeginExpedition();
                Require(payoutGuild.TryReturn(reloaded.BoundaryResult(payoutGuild)) &&
                    payoutGuild.Gold == 100 + pending.gold + result.gold &&
                    payoutGuild.Roster.First(h => h.Id == result.hero).Health == result.healthAfter,
                    "Event rewards and wounds reach guild together");
                var skipped = ExpeditionJourney.Restore(pending, guild);
                Require(skipped.TryResolveEvent(guild, 0) && skipped.LastEvent.roll == 0 && skipped.CarriedGold == pending.gold &&
                    skipped.Capture().party.Select(h => h.health).SequenceEqual(pending.party.Select(h => h.health)), "Skip has no effects");
                var bodyTest = Boundary(guild, offers, 1);
                bodyTest.TryResolveEvent(guild, 1, bodyTest.Capture().party[0].id);
                if (bodyTest.LastEvent.healthAfter == 0)
                {
                    Require(bodyTest.BoundaryResult(guild).Adventurers.First().BodyRecovered &&
                        bodyTest.ContinuingParty(guild).Count == 3, "Death carries body, never resurrects on transition");
                }
                var fatal = Boundary(guild, offers, 1, true);
                fatal.TryResolveEvent(guild, 1, fatal.Capture().party[0].id);
                fatal.Capture().Validate(guild);
                if (fatal.IsDefeated)
                {
                    var defeat = fatal.BoundaryResult(guild);
                    Require(defeat.Gold == 0 && defeat.Items.Count == 0 && defeat.Adventurers.All(h => !h.BodyRecovered), "Last death loses everything");
                    guild.BeginExpedition();
                    Require(guild.TryReturn(defeat) && guild.Gold == 100 && !guild.TryReturn(defeat), "Fatal return once");
                }
            }
            Require(covered.Count == 8, "All four event types succeed and fail");
            Persistence();
            AccumulatedRewards();
            Debug.Log("WP-31 model checks passed: 200 seeds, four events success/failure, choices, replay, healing, death, defeat, checkpoint reload, rollback, single payout and legacy v5.");
        }

        private static void AccumulatedRewards()
        {
            var guild = new GuildState();
            var offers = new ExpeditionSelection(26, 1, 0);
            var journey = Boundary(guild, offers, 20);
            int extraGold = 0;
            for (int section = 1; section <= journey.Sections; section++)
            {
                Require(journey.EventPending && journey.TryResolveEvent(guild, 1, journey.Capture().party.First(p => p.health > 0).id), "Each boundary offers one event");
                extraGold += journey.LastEvent.gold;
                journey.Capture().Validate(guild);
                if (section < journey.Sections)
                {
                    var party = journey.ContinuingParty(guild);
                    var heroes = party.Select(h => new AdventurerResult(h.Id, h.Definition, h.Health, h.HealingPotions, false)).ToArray();
                    var compose = journey.ResultComposer(guild);
                    var retreat = compose(new ExpeditionResult(journey.NextSectionSeed, ExpeditionOutcome.Retreated, 0, Array.Empty<ItemDefinition>(), heroes));
                    Require(retreat.Gold == journey.CarriedGold, "Retreat retains prior event gold");
                    var defeat = compose(new ExpeditionResult(journey.NextSectionSeed, ExpeditionOutcome.Defeated, 0, Array.Empty<ItemDefinition>(),
                        heroes.Select(h => new AdventurerResult(h.InstanceId, guild.Roster.First(a => a.Id == h.InstanceId).Definition, 0, h.HealingPotions, false))));
                    Require(defeat.Gold == 0 && defeat.Items.Count == 0, "Later defeat loses event rewards");
                    var next = compose(new ExpeditionResult(journey.NextSectionSeed, ExpeditionOutcome.Extracted, offers.Selected.Mission.MinimumGold,
                        new[] { ItemDefinitions.Weapon, ItemDefinitions.HealingDraught }, heroes));
                    journey.Complete(next);
                }
            }
            Require(journey.Capture().events.Length == journey.Sections && journey.Capture().eventGold == extraGold &&
                journey.CarriedGold == journey.Sections * offers.Selected.Mission.MinimumGold + extraGold &&
                !journey.EventPending, "All section rewards and ledger retained once");
        }

        private static void Persistence()
        {
            string directory = Path.Combine(Path.GetTempPath(), "ExplorationChecks-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                var guild = new GuildState();
                var offers = new ExpeditionSelection(19, 0, 0);
                var departure = GuildSaveData.Capture(guild, offers);
                var journey = Boundary(guild, offers);
                offers.RecordLaunch();
                var store = new GuildSaveStore(Path.Combine(directory, "save.json"));
                Require(store.TrySaveCheckpoint(departure, offers, journey.Capture(), out _), "Persist pending event");
                Require(store.TryLoad(out var loaded, out var loadedOffers, out _), "Read pending event");
                var resumed = ExpeditionJourney.Restore(store.LoadedCheckpoint, loaded);
                Require(resumed.EventPending && resumed.CurrentEvent.Id == journey.CurrentEvent.Id, "Pending event stable after reload");
                var heroId = resumed.Capture().party[0].id;
                resumed.TryResolveEvent(loaded, 1, heroId);
                Require(store.TrySaveCheckpoint(departure, loadedOffers, resumed.Capture(), out _), "Persist resolved event");
                Require(store.TryLoad(out loaded, out loadedOffers, out _), "Read resolved event");
                resumed = ExpeditionJourney.Restore(store.LoadedCheckpoint, loaded);
                Require(!resumed.EventPending && !resumed.TryResolveEvent(loaded, 1, heroId), "Resolved event consumed");
                int gold = resumed.CarriedGold;
                var continuing = resumed.ContinuingParty(loaded);
                continuing[0].Health = 1;
                Require(store.TryLoad(out loaded, out _, out _) &&
                    ExpeditionJourney.Restore(store.LoadedCheckpoint, loaded).LastEvent.healthAfter == resumed.LastEvent.healthAfter, "Unfinished section rollback keeps event effects");
                loaded.BeginExpedition();
                Require(loaded.TryReturn(resumed.BoundaryResult(loaded)) && !loaded.TryReturn(resumed.BoundaryResult(loaded)) &&
                    loaded.Gold == 100 + gold, "Event gold paid once");
                Require(store.TrySave(loaded, loadedOffers, out _) && store.TryLoad(out loaded, out _, out _) &&
                    store.LoadedCheckpoint == null && loaded.Gold == 100 + gold, "Paid save clears event ledger");
                var bad = resumed.Capture();
                bad.events = bad.events.Concat(bad.events).ToArray();
                bool rejected = false;
                try { bad.Validate(GuildState.Restore(departure)); } catch (ArgumentException) { rejected = true; }
                Require(rejected, "Duplicate ledger rejected");
                var legacy = JsonUtility.FromJson<GuildSaveData>(JsonUtility.ToJson(departure));
                legacy.version = 5; legacy.hasJourney = true; legacy.launchedCount = 1;
                legacy.journey = journey.Capture(); legacy.journey.explorationEnabled = false;
                File.WriteAllText(store.Path, JsonUtility.ToJson(legacy));
                Require(store.TryLoad(out loaded, out _, out _) && !ExpeditionJourney.Restore(store.LoadedCheckpoint, loaded).EventPending,
                    "Legacy v5 boundary remains unchanged");
                // A fatal event is a loadable terminal checkpoint, including carried casualties.
                for (int seed = 0; seed < 40; seed++)
                {
                    var fatalGuild = new GuildState();
                    var fatalOffers = new ExpeditionSelection(seed, 0, 0);
                    var fatalDeparture = GuildSaveData.Capture(fatalGuild, fatalOffers);
                    var fatal = Boundary(fatalGuild, fatalOffers, 1, true);
                    fatal.TryResolveEvent(fatalGuild, 1, fatal.Capture().party[0].id);
                    if (!fatal.IsDefeated) continue;
                    fatalOffers.RecordLaunch();
                    Require(store.TrySaveCheckpoint(fatalDeparture, fatalOffers, fatal.Capture(), out _) &&
                        store.TryLoad(out fatalGuild, out _, out _), "Fatal event persists");
                    var restoredFatal = ExpeditionJourney.Restore(store.LoadedCheckpoint, fatalGuild);
                    Require(restoredFatal.IsDefeated && !restoredFatal.EventPending &&
                        restoredFatal.BoundaryResult(fatalGuild).Gold == 0, "Fatal event reload cannot revive or pay loot");
                    break;
                }
                var failing = new GuildSaveStore(Path.Combine(store.Path, "child.json"));
                Require(!failing.TrySaveCheckpoint(departure, offers, resumed.Capture(), out var failure) && failure != null &&
                    !resumed.TryResolveEvent(loaded, 1, heroId), "Failed write never applies event twice");
            }
            finally { Directory.Delete(directory, true); }
        }

        internal static void ValidatePresentation(GameBootstrap bootstrap)
        {
            var controller = bootstrap.ActiveController;
            foreach (var enemy in controller.Enemies) enemy.ApplyDamage(enemy.CurrentHealth);
            var turns = controller.Turns;
            for (int i = 0; i <= turns.Order.Count && turns.ActiveUnit.Team != GuildTactics.Units.UnitTeam.Player; i++)
            { turns.TryEndTurn(turns.ActiveUnit); turns.TryStartNextTurn(); }
            var actor = turns.ActiveUnit;
            actor.ApplyDamage(Math.Max(0, actor.CurrentHealth - 10));
            if (controller.Expedition.Mission.RequiresRelic)
            {
                Require(actor.TryRelocate(bootstrap.Grid, controller.Expedition.Chest), "Scene reach relic");
                Require(controller.Expedition.TryOpenChest(actor), "Scene take relic");
            }
            RetreatChecks.ReachExit(turns, controller.Expedition);
            Require(controller.Expedition.TryExtract(actor, true), "Scene extract before investigation");
            bootstrap.CaptureJourneyBoundary();
            int gold = bootstrap.Guild.Gold;
            Require(bootstrap.TryResolveExplorationEvent(1, actor.InstanceId), "Scene investigate");
            var outcome = bootstrap.Journey.LastEvent;
            var snapshot = bootstrap.JourneyBoundary.Adventurers.First(h => h.InstanceId == actor.InstanceId);
            Require(snapshot.Health == outcome.healthAfter && bootstrap.JourneyBoundary.Gold == controller.Expedition.Result.Gold + outcome.gold &&
                bootstrap.BoundarySaved && !bootstrap.TryResolveExplorationEvent(1, actor.InstanceId), "Scene effects and save applied once");
            int reward = bootstrap.JourneyBoundary.Gold;
            Require(bootstrap.TryReturnToGuild() && !bootstrap.TryReturnToGuild() && bootstrap.Guild.Gold == gold + reward &&
                bootstrap.Guild.Roster.First(h => h.Id == actor.InstanceId).Health == outcome.healthAfter, "Scene investigation returns effects to guild");
            if (!snapshot.Survived)
                Require(bootstrap.Guild.TryResurrect(actor.InstanceId) && bootstrap.Guild.TryToggleSelection(actor.InstanceId), "Event casualty can be resurrected");
            Require(bootstrap.TryLaunchExpedition(), "New expedition after event");
            Debug.Log("WP-31 Play Mode passed: investigation, visible result data, saved effects, single guild payout and fresh expedition.");
        }

        internal static void ValidateBoundary(GameBootstrap bootstrap)
        {
            Require(!bootstrap.TryContinueJourney() && !bootstrap.TryReturnToGuild() &&
                !bootstrap.ActiveController.TryEndTurn() && !bootstrap.ActiveController.TryRetreat(true), "Pending choice blocks conflicting scene commands");
            var before = bootstrap.Journey.Capture();
            Require(!bootstrap.TryResolveExplorationEvent(1, "missing") && bootstrap.Journey.EventPending, "Invalid hero preserves choice");
            Require(bootstrap.TryResolveExplorationEvent(0) && !bootstrap.TryResolveExplorationEvent(0) &&
                bootstrap.CanContinueJourney && bootstrap.JourneyBoundary.Gold == before.gold, "Scene skip resolves and saves exactly once");
            Debug.Log("WP-31 Play Mode passed: pending choice blocks commands, invalid actor rejected, skip and checkpoint resolution once.");
        }

        private static void Require(bool value, string message)
        { if (!value) throw new InvalidOperationException("WP-31: " + message); }
    }
}
