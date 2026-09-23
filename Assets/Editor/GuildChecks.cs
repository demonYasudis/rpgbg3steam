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
    public static class GuildChecks
    {
        private sealed class Fixture
        {
            public readonly GuildState Guild;
            public readonly DungeonMap Map;
            public readonly List<UnitRuntimeState> Party = new List<UnitRuntimeState>();
            public readonly TurnManager Turns;
            public readonly ExpeditionRun Run;
            public Fixture(GuildState guild, int seed)
            {
                Guild = guild; Map = DungeonGenerator.Generate(seed);
                var party = guild.BeginExpedition();
                for (int i = 0; i < party.Count; i++)
                {
                    Require(UnitRuntimeState.TrySpawn(Map.Grid, party[i].Id, party[i].Definition, Map.PlayerSpawns[i], out var unit), "Spawn selected identity");
                    unit.ApplyDamage(unit.Definition.MaxHealth - party[i].Health);
                    Party.Add(unit);
                }
                Turns = new TurnManager(Map.Grid, Party);
                Run = new ExpeditionRun(Map, Turns);
                guild.AttachRun(Run);
                Turns.TryStartNextTurn();
            }
            public void Extract(bool confirm = false)
            {
                var actor = Turns.ActiveUnit;
                Require(actor.TryRelocate(Map.Grid, Run.Chest), "Reach chest fixture");
                Require(Run.TryOpenChest(actor), "Open chest fixture");
                var occupant = Party.FirstOrDefault(u => u.IsAlive && u.Position == Run.Extraction);
                if (occupant != null)
                {
                    var free = Map.Grid.Cells.First(c => TerrainRules.CanWalk(c.Terrain) && !c.IsOccupied && c.Coordinates != Run.Extraction);
                    Require(occupant.TryRelocate(Map.Grid, free.Coordinates), "Clear exit fixture");
                }
                Require(actor.TryRelocate(Map.Grid, Run.Extraction), "Reach exit fixture");
                if (Run.HasUnrecoverableBodies)
                    Require(!Run.TryExtract(actor) && Run.Result == null, "Permanent loss requires confirmation");
                Require(Run.TryExtract(actor, confirm), "Extract fixture");
            }
        }

        [MenuItem("Tools/Guild Tactics/Validate Guild and Recovery")]
        public static void Run()
        {
            for (int seed = 0; seed < 20; seed++)
            {
                var guild = new GuildState();
                Require(guild.CanLaunch && guild.Roster.Count == 8, "Initial roster");
                Require(!guild.TryToggleSelection("warrior-2") && !guild.TryToggleSelection("missing"), "Full/unknown selection rejected");
                Require(guild.TryToggleSelection("warrior-1") && !guild.CanLaunch, "Exactly four required");
                Require(guild.TryToggleSelection("warrior-2"), "Reserve selected");
                var first = new Fixture(guild, seed);
                Require(first.Party.Any(u => u.InstanceId == "warrior-2") && first.Party.All(u => u.InstanceId != "warrior-1"), "Selected identities spawned");
                Require(!guild.CanLaunch && !guild.TryToggleSelection("rogue-1") && !guild.TryHeal("rogue-1") && !guild.TryReturn(), "Away locks and unfinished result");
                var survivor = first.Turns.ActiveUnit;
                survivor.ApplyDamage(3);
                var dead = first.Party.First(u => u != survivor);
                dead.ApplyDamage(dead.CurrentHealth);
                Require(first.Run.Bodies.Count == 1 && first.Run.Bodies[0].Recoverable &&
                    !first.Map.Grid.GetCell(dead.Position).IsOccupied, "Body remains without blocking occupancy");
                first.Extract();
                int reward = first.Run.Result.Gold;
                Require(guild.TryReturn() && !guild.TryReturn() && guild.Gold == 100 + reward && guild.Inventory.Count == 2, "Apply result once");
                var record = guild.Roster.First(a => a.Id == dead.InstanceId);
                Require(record.Status == AdventurerStatus.BodyRecovered && record.Health == 0 && !guild.TryToggleSelection(record.Id), "Death persists outside battle");
                Require(guild.Roster.First(a => a.Id == survivor.InstanceId).Health == survivor.CurrentHealth, "Wounds returned");
                Require(guild.TryResurrect(record.Id) && !guild.TryResurrect(record.Id) && guild.Gold == 100 + reward - 30, "Resurrection charged once");
                Require(guild.TryToggleSelection(record.Id), "Resurrected hero selectable");
                var second = new Fixture(guild, seed);
                Require(second.Party.First(u => u.InstanceId == survivor.InstanceId).CurrentHealth == survivor.CurrentHealth, "Second expedition preserves wounds");
                Require(second.Party.First(u => u.InstanceId == dead.InstanceId).CurrentHealth == record.Definition.MaxHealth, "Resurrected hero respawns");
                Require(!guild.TryResurrect(record.Id), "No resurrection while away");
                second.Extract();
                Require(guild.TryReturn() && guild.Inventory.Count == 4, "Second return stores rewards");
                int gold = guild.Gold;
                Require(guild.TryHeal(survivor.InstanceId) && !guild.TryHeal(survivor.InstanceId) && guild.Gold == gold - 5, "Healing charged once");
            }

            var pitGuild = new GuildState();
            var pit = new Fixture(pitGuild, 31);
            var victim = pit.Party.First(u => u != pit.Turns.ActiveUnit);
            var neighbor = pit.Map.Grid.GetNeighbors(victim.Position).First(c => !c.IsOccupied && c.Coordinates != pit.Run.Chest && c.Coordinates != pit.Run.Extraction);
            neighbor.Terrain = TerrainType.Pit;
            Require(victim.TryPushTo(pit.Map.Grid, neighbor.Coordinates) && !victim.IsAlive, "Fatal pit push");
            Require(pit.Run.HasUnrecoverableBodies, "Pit body cannot be recovered");
            pit.Extract(true);
            Require(pitGuild.TryReturn() && pitGuild.Roster.First(a => a.Id == victim.InstanceId).Status == AdventurerStatus.Lost &&
                !pitGuild.TryResurrect(victim.InstanceId) && !pitGuild.TryToggleSelection(victim.InstanceId), "Lost hero cannot return");

            var poorGuild = new GuildState(0);
            var poor = new Fixture(poorGuild, 5);
            var poorVictims = poor.Party.Where(u => u != poor.Turns.ActiveUnit).ToArray();
            foreach (var unit in poorVictims) unit.ApplyDamage(unit.CurrentHealth);
            poor.Extract(); poorGuild.TryReturn();
            foreach (var unit in poorVictims)
            {
                int before = poorGuild.Gold;
                bool success = poorGuild.TryResurrect(unit.InstanceId);
                Require(success == (before >= GuildState.ResurrectionCost), "Insufficient funds rejected");
                Require(poorGuild.Gold == before - (success ? GuildState.ResurrectionCost : 0), "Failed resurrection spends nothing");
            }
            Require(poorGuild.Roster.Any(a => a.Status == AdventurerStatus.BodyRecovered), "At least one unaffordable corpse remains");

            var defeatGuild = new GuildState();
            var defeat = new Fixture(defeatGuild, 7);
            foreach (var unit in defeat.Party) unit.ApplyDamage(unit.CurrentHealth);
            defeat.Run.RefreshOutcome();
            Require(defeatGuild.TryReturn() && defeatGuild.Gold == 100 && defeatGuild.Inventory.Count == 0 &&
                defeatGuild.Roster.Count(a => a.Status == AdventurerStatus.Lost) == 4, "Wipe loses bodies and rewards");
            foreach (var unit in defeatGuild.Roster.Where(a => a.Status == AdventurerStatus.Alive)) defeatGuild.TryToggleSelection(unit.Id);
            Require(defeatGuild.CanLaunch, "Reserve can continue after wipe");

            var cancel = new GuildState(); cancel.BeginExpedition(); cancel.CancelLaunch();
            Require(cancel.CanLaunch && cancel.Gold == 100, "Failed startup releases reservation");
            Debug.Log("WP-14/15 passed: 20 two-expedition sessions, selection, HP, rewards, recovery, pit loss, defeat, resurrection and insufficient funds.");
        }

        // Real Play Mode wiring check. Domain relocations arrange the fixture; no claim of mouse/visual testing.
        public static void ValidatePresentation(GameBootstrap bootstrap)
        {
            Require(!bootstrap.Guild.IsAway && bootstrap.ActiveController == null, "Scene starts at guild");
            bootstrap.Guild.TryToggleSelection("warrior-1"); bootstrap.Guild.TryToggleSelection("warrior-2");
            Require(bootstrap.TryLaunchExpedition() && !bootstrap.TryLaunchExpedition(), "Launch and duplicate lock");
            var controller = bootstrap.ActiveController;
            Require(controller.Units.Any(u => u.InstanceId == "warrior-2"), "Controller spawns selected reserve");
            foreach (var enemy in controller.Enemies) enemy.ApplyDamage(enemy.CurrentHealth);
            var turns = controller.Turns;
            for (int i = 0; i < turns.Order.Count && turns.ActiveUnit.Team != UnitTeam.Player; i++)
            { turns.TryEndTurn(turns.ActiveUnit); turns.TryStartNextTurn(); }
            Require(turns.ActiveUnit.Team == UnitTeam.Player, "Finite advance to player turn");
            var actor = turns.ActiveUnit;
            actor.ApplyDamage(2);
            var dead = controller.Units.First(u => u != actor);
            dead.ApplyDamage(dead.CurrentHealth);
            actor.TryRelocate(bootstrap.Grid, controller.Expedition.Chest);
            Require(controller.Expedition.TryOpenChest(actor), "Presentation chest");
            foreach (var unit in controller.Units)
                if (unit.IsAlive && unit.Position == controller.Expedition.Extraction)
                    unit.TryRelocate(bootstrap.Grid, bootstrap.Grid.Cells.First(c => TerrainRules.CanWalk(c.Terrain) && !c.IsOccupied).Coordinates);
            actor.TryRelocate(bootstrap.Grid, controller.Expedition.Extraction);
            Require(controller.Expedition.TryExtract(actor) && bootstrap.TryReturnToGuild() && !bootstrap.TryReturnToGuild(), "Return and duplicate lock");
            Require(!controller.gameObject.activeSelf && bootstrap.ActiveController == null, "Old battle disabled immediately");
            bootstrap.Guild.TryResurrect(dead.InstanceId); bootstrap.Guild.TryToggleSelection(dead.InstanceId);
            Require(bootstrap.TryLaunchExpedition(), "Launch second battle without restarting Play");
            Require(bootstrap.ActiveController != controller && bootstrap.ActiveController.Units.First(u => u.InstanceId == actor.InstanceId).CurrentHealth == actor.CurrentHealth,
                "New controller retains guild health");
        }

        public static void RunBatch() => HexPresentationChecks.RunBatch();
        private static void Require(bool condition, string message)
        { if (!condition) throw new InvalidOperationException("WP-14/15: " + message); }
    }
}
