using System;
using System.Collections.Generic;
using System.Linq;
using GuildTactics.Combat;
using GuildTactics.Core;
using GuildTactics.Expeditions;
using GuildTactics.Generation;
using GuildTactics.HexGrid;
using GuildTactics.Meta;
using GuildTactics.Units;
using UnityEditor;
using UnityEngine;

namespace GuildTactics.Editor
{
    public static class RetreatChecks
    {
        // Arrange only positions; commands under test must enforce their own permissions.
        internal static void ReachExit(TurnManager turns, ExpeditionRun run)
        {
            var actor = turns.ActiveUnit;
            var occupant = run.Party.FirstOrDefault(u => u != actor && u.IsAlive && u.Position == run.Extraction);
            if (occupant != null)
                Require(occupant.TryRelocate(turns.Grid, turns.Grid.Cells.First(c => !c.IsOccupied &&
                    TerrainRules.CanWalk(c.Terrain) && c.Coordinates != run.Extraction).Coordinates), "Free EXIT");
            Require(actor.Position == run.Extraction || actor.TryRelocate(turns.Grid, run.Extraction), "Reach EXIT");
        }

        [MenuItem("Tools/Guild Tactics/Validate Early Retreat")]
        public static void Run()
        {
            foreach (var mission in new[] { MissionDefinition.Relic, MissionDefinition.Hunt, MissionDefinition.Clear })
            foreach (bool cleared in new[] { false, true })
            {
                var map = DungeonGenerator.Generate(29);
                var party = new List<UnitRuntimeState>();
                for (int i = 0; i < 4; i++)
                {
                    Require(UnitRuntimeState.TrySpawn(map.Grid, "hero" + i, HeroDefinitions.Defaults[i], map.PlayerSpawns[i], out var hero), "Spawn");
                    party.Add(hero);
                }
                var placement = EncounterGenerator.Generate(map)[0];
                Require(UnitRuntimeState.TrySpawn(map.Grid, "enemy", placement.Archetype.Unit, placement.Position, out var enemy, UnitTeam.Enemy), "Enemy");
                var turns = new TurnManager(map.Grid, party.Concat(new[] { enemy }));
                var run = new ExpeditionRun(map, turns, mission);
                turns.TryStartNextTurn();
                while (turns.ActiveUnit.Team != UnitTeam.Player) { turns.TryEndTurn(turns.ActiveUnit); turns.TryStartNextTurn(); }
                var actor = turns.ActiveUnit;
                Require(actor.TryRelocate(map.Grid, run.Chest), "Away from exit");
                Require(!run.CanRetreat(actor) && run.PreviewRetreat(actor) == null && !run.TryRetreat(true), "No remote retreat");
                if (mission.RequiresRelic) Require(run.TryOpenChest(actor), "Collect loot to forfeit");
                actor.ApplyDamage(3);
                var dead = party.First(u => u != actor && u.Position != run.Extraction);
                dead.ApplyDamage(dead.CurrentHealth);
                if (cleared) enemy.ApplyDamage(enemy.CurrentHealth);
                ReachExit(turns, run);
                // Resolving a move/action must block retreat even when the model is already on EXIT.
                if (!turns.ActionAvailable) { turns.TryEndTurn(actor); turns.TryStartNextTurn();
                    while (turns.ActiveUnit != actor) { turns.TryEndTurn(turns.ActiveUnit); turns.TryStartNextTurn(); } }
                Require(turns.TryBeginAction(actor) && !run.TryRetreat(true) && run.PreviewRetreat(actor) == null, "Action lock");
                turns.TryCompleteAction(actor);
                var preview = run.PreviewRetreat(actor);
                Require(preview != null && preview.Gold == 0 && preview.Items.Count == 0, "No reward");
                Require(preview.Adventurers.Single(u => u.InstanceId == dead.InstanceId).BodyRecovered == cleared, "Clearance determines body recovery");
                int movement = turns.RemainingMovement;
                bool action = turns.ActionAvailable;
                Require(!run.TryRetreat(false) && run.Result == null && turns.RemainingMovement == movement && turns.ActionAvailable == action, "Cancel has no side effects");
                Require(!run.CanRetreat(dead) && !run.CanRetreat(enemy), "Wrong/dead actor rejected");
                Require(run.TryRetreat(true) && !run.TryRetreat(true) && !run.TryExtract(actor, true), "Exactly one outcome");
                Require(run.Result.Outcome == ExpeditionOutcome.Retreated && run.Result.Adventurers.Count(u => u.Survived) == 3 &&
                    run.Result.Adventurers.Single(u => u.InstanceId == actor.InstanceId).Health == actor.CurrentHealth, "All survivors keep wounds");
                actor.ApplyDamage(actor.CurrentHealth); run.RefreshOutcome();
                Require(run.Result.Adventurers.Count(u => u.Survived) == 3, "Immutable snapshot");
            }
            ValidateGuildReturn();
            Debug.Log("WP-29 passed: exit permissions, three missions, cancellation, loot forfeiture, cleared/unsafe bodies and single immutable result.");
        }

        private static void ValidateGuildReturn()
        {
            foreach (bool recoverable in new[] { true, false })
            {
                var data = GuildSaveData.Capture(new GuildState(), new ExpeditionSelection("29"));
                data.items = new[] { ItemDefinitions.Weapon.Id, ItemDefinitions.HealingDraught.Id };
                var guild = GuildState.Restore(data);
                Require(guild.TryEquip("warrior-1", ItemDefinitions.Weapon.Id) && guild.TryTransferPotion("warrior-1", true), "Equip casualty");
                var map = DungeonGenerator.Generate(29);
                var party = new List<UnitRuntimeState>();
                foreach (var hero in guild.BeginExpedition())
                {
                    Require(UnitRuntimeState.TrySpawn(map.Grid, hero.Id, hero.Definition, map.PlayerSpawns[party.Count], out var unit), "Guild spawn");
                    unit.SetLoadout(hero.Weapon, hero.Armor, hero.HealingPotions);
                    party.Add(unit);
                }
                var turns = new TurnManager(map.Grid, party);
                var run = new ExpeditionRun(map, turns, MissionDefinition.Relic);
                guild.AttachRun(run); turns.TryStartNextTurn();
                var casualty = party.Single(u => u.InstanceId == "warrior-1");
                Require(casualty.TryRelocate(map.Grid, run.Chest), "Casualty away from exit");
                casualty.ApplyDamage(casualty.CurrentHealth);
                if (!recoverable) map.Grid.GetCell(casualty.Position).Terrain = TerrainType.Pit;
                ReachExit(turns, run);
                Require(run.TryRetreat(true) && guild.TryReturn() && !guild.TryReturn(), "Return once");
                var restored = GuildState.Restore(GuildSaveData.Capture(guild, new ExpeditionSelection("29")));
                Require(restored.Gold == data.gold && restored.Inventory.Count == (recoverable ? 2 : 0), "Gear recovered/lost once and saved");
                Require(restored.Roster.Single(h => h.Id == casualty.InstanceId).Status ==
                    (recoverable ? AdventurerStatus.BodyRecovered : AdventurerStatus.Lost), "Saved casualty status");
                Require(restored.Roster.Where(h => party.Any(u => u.InstanceId == h.Id && u.IsAlive)).All(h => h.Experience == 25), "Survivor retreat XP saved");
            }
        }

        public static void ValidatePresentation(GameBootstrap bootstrap)
        {
            var controller = bootstrap.ActiveController;
            var turns = controller.Turns;
            for (int i = 0; i <= turns.Order.Count && turns.ActiveUnit.Team != UnitTeam.Player; i++)
            { turns.TryEndTurn(turns.ActiveUnit); turns.TryStartNextTurn(); }
            ReachExit(turns, controller.Expedition);
            controller.InterfaceBlocked = true;
            Require(!controller.TryRetreat(true), "Modal blocks controller commands");
            controller.InterfaceBlocked = false;
            int gold = bootstrap.Guild.Gold;
            Require(controller.TryRetreat(true), "Retreat through actual controller");
            Require(bootstrap.TryReturnToGuild() && !bootstrap.TryReturnToGuild() && bootstrap.Guild.Gold == gold, "Single return, no reward");
            Require(!controller.TryRetreat(true) && bootstrap.TryLaunchExpedition(), "Old controller disabled; next expedition starts");
            Debug.Log("WP-29 Play Mode passed: blocked input, retreat, single guild transfer, fresh expedition.");
        }

        private static void Require(bool value, string message)
        { if (!value) throw new InvalidOperationException("WP-29: " + message); }
    }
}
