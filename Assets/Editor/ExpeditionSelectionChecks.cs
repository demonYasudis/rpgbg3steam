using System;
using System.Collections.Generic;
using System.Linq;
using GuildTactics.Expeditions;
using GuildTactics.Generation;
using GuildTactics.Core;
using GuildTactics.Combat;
using GuildTactics.HexGrid;
using GuildTactics.Units;
using UnityEditor;
using UnityEngine;

namespace GuildTactics.Editor
{
    public static class ExpeditionSelectionChecks
    {
        [MenuItem("Tools/Guild Tactics/Validate Expedition Selection")]
        public static void Run()
        {
            var selection = new ExpeditionSelection("crypt-1");
            var replay = new ExpeditionSelection("crypt-1");
            var seeds = new HashSet<int>();
            var maps = new HashSet<string>();
            for (int i = 0; i < 40; i++)
            {
                Require(selection.NextSeed == replay.NextSeed && seeds.Add(selection.NextSeed), "Distinct reproducible sequence");
                var map = DungeonGenerator.Generate(selection.NextSeed);
                maps.Add(string.Join(",", map.Grid.Cells.Select(c => (int)c.Terrain)));
                foreach (var offer in ExpeditionSelection.Offers)
                {
                    var config = offer.CreateEncounterConfig();
                    var encounter = EncounterGenerator.Generate(map, config);
                    Require(encounter.Count >= config.MinEnemies && encounter.Count <= config.MaxEnemies &&
                        encounter.Sum(e => e.Archetype.Cost) <= config.Budget, "Offer respects advertised difficulty");
                    if (config.MiniBossPercent == 0) Require(encounter.All(e => !e.Archetype.IsMiniBoss), "Easy offer excludes boss");
                }
                selection.RecordLaunch(); replay.RecordLaunch();
            }
            Require(maps.Count > 20 && !selection.TrySelect(-1) && !selection.TrySelect(ExpeditionSelection.Offers.Count), "Variety and invalid selection");
            Require(selection.TrySelect(0) && selection.Selected == ExpeditionSelection.Offers[0], "Selection");
            var copy = selection.Selected.CreateEncounterConfig(); copy.Budget = 99;
            Require(selection.Selected.CreateEncounterConfig().Budget == 4, "Config mutation does not alter offer");
            Debug.Log("WP-16 passed: 40 distinct seeds, reproducible maps, three mission offers and isolated configs.");
        }
        private static void Require(bool condition, string message)
        { if (!condition) throw new InvalidOperationException("WP-16: " + message); }

        // Runs after the existing guild fixture. Arrange combat outcomes through the domain,
        // while exercising the actual bootstrap, scene ownership and guild transfer.
        public static void ValidatePresentation(GameBootstrap bootstrap)
        {
            for (int cycle = 0; cycle < 3; cycle++)
            {
                var controller = bootstrap.ActiveController;
                foreach (var enemy in controller.Enemies) enemy.ApplyDamage(enemy.CurrentHealth);
                var turns = controller.Turns;
                for (int i = 0; i <= turns.Order.Count && turns.ActiveUnit.Team != UnitTeam.Player; i++)
                { turns.TryEndTurn(turns.ActiveUnit); turns.TryStartNextTurn(); }
                var actor = turns.ActiveUnit;
                Require(actor.TryRelocate(bootstrap.Grid, controller.Expedition.Chest), "Reach chest fixture");
                if (controller.Expedition.Mission.RequiresRelic) Require(controller.Expedition.TryOpenChest(actor), "Collect relic reward");
                foreach (var hero in controller.Units)
                    if (hero.IsAlive && hero.Position == controller.Expedition.Extraction)
                        hero.TryRelocate(bootstrap.Grid, bootstrap.Grid.Cells.First(c => TerrainRules.CanWalk(c.Terrain) && !c.IsOccupied).Coordinates);
                Require(actor.TryRelocate(bootstrap.Grid, controller.Expedition.Extraction) && controller.Expedition.TryExtract(actor), "Complete another run");
                bootstrap.CaptureJourneyBoundary();
                Require(bootstrap.TryResolveExplorationEvent(0), "Skip boundary event in selection fixture");
                int gold = bootstrap.Guild.Gold, reward = controller.Expedition.Result.Gold;
                Require(bootstrap.TryReturnToGuild() && bootstrap.Guild.Gold == gold + reward && !bootstrap.TryReturnToGuild(), "Award exactly once");
                Require(bootstrap.TrySelectExpedition(cycle == 0 ? 0 : cycle == 1 ? 2 : 1), "Change offer between runs");
                int nextSeed = bootstrap.Expeditions.NextSeed;
                Require(bootstrap.TryLaunchExpedition() && bootstrap.Dungeon.Seed == nextSeed, "Launch advertised seed");
                if (cycle == 0) Require(bootstrap.ActiveController.Enemies.Count >= 2 && bootstrap.ActiveController.Enemies.Count <= 3,
                    "Easy offer reaches real controller");
            }
            Debug.Log("WP-16/17 Play Mode passed: three completed expeditions, offer switching, reward transfer, fresh seeds.");
        }
    }
}
