using System;
using System.Collections.Generic;
using System.Linq;
using GuildTactics.Combat;
using GuildTactics.Expeditions;
using GuildTactics.Generation;
using GuildTactics.HexGrid;
using GuildTactics.Meta;
using GuildTactics.Units;
using UnityEditor;
using UnityEngine;

namespace GuildTactics.Editor
{
    public static class LoadoutChecks
    {
        private sealed class MaxDice : IDice { public int Roll(int sides) => sides; }
        private static GuildState Stocked()
        {
            var data = GuildSaveData.Capture(new GuildState(), new ExpeditionSelection("1"));
            data.items = new[] { "iron-edge", "crypt-mail", "healing-draught", "healing-draught", "healing-draught" };
            return GuildState.Restore(data);
        }

        private sealed class RunFixture
        {
            public readonly DungeonMap Map = DungeonGenerator.Generate(27);
            public readonly List<UnitRuntimeState> Party = new List<UnitRuntimeState>();
            public readonly TurnManager Turns;
            public readonly ExpeditionRun Run;
            public RunFixture(GuildState guild)
            {
                var party = guild.BeginExpedition();
                for (int i = 0; i < party.Count; i++)
                {
                    Require(UnitRuntimeState.TrySpawn(Map.Grid, party[i].Id, party[i].Definition, Map.PlayerSpawns[i], out var unit), "Spawn");
                    unit.SetLoadout(party[i].Weapon, party[i].Armor, party[i].HealingPotions);
                    unit.ApplyDamage(unit.Definition.MaxHealth - party[i].Health); Party.Add(unit);
                }
                Turns = new TurnManager(Map.Grid, Party); Run = new ExpeditionRun(Map, Turns);
                guild.AttachRun(Run); Turns.TryStartNextTurn();
            }
            public void Extract()
            {
                var actor = Turns.ActiveUnit;
                Require(actor.TryRelocate(Map.Grid, Run.Chest) && Run.TryOpenChest(actor), "Chest");
                var occupant = Party.FirstOrDefault(p => p.IsAlive && p.Position == Run.Extraction);
                if (occupant != null) occupant.TryRelocate(Map.Grid, Map.Grid.Cells.First(c => TerrainRules.CanWalk(c.Terrain) && !c.IsOccupied).Coordinates);
                Require(actor.TryRelocate(Map.Grid, Run.Extraction) && Run.TryExtract(actor, true), "Extract");
            }
        }

        [MenuItem("Tools/Guild Tactics/Validate Equipment and Consumables")]
        public static void Run()
        {
            var guild = Stocked();
            var hero = guild.Roster[0];
            Require(guild.TryEquip(hero.Id, "iron-edge") && guild.TryEquip(hero.Id, "crypt-mail"), "Equip owned items");
            Require(!guild.TryEquip("rogue-1", "iron-edge") && !guild.TryEquip(hero.Id, "iron-edge") &&
                !guild.TryEquip(hero.Id, "healing-draught"), "No duplicate ownership or wrong slot");
            Require(hero.Attack == hero.Definition.Attack + 1 && hero.Defense == hero.Definition.Defense + 2 &&
                hero.DamageDie == 8 && hero.Definition.DamageDie == 6, "Bonuses without definition mutation");
            for (int i = 0; i < 3; i++)
                Require(guild.TryUnequip(hero.Id, ItemCategory.Weapon) && hero.Attack == hero.Definition.Attack &&
                    guild.TryEquip(hero.Id, "iron-edge") && hero.Attack == hero.Definition.Attack + 1, "Repeated swap does not stack");
            Require(guild.TryTransferPotion(hero.Id, true) && guild.TryTransferPotion(hero.Id, true) &&
                !guild.TryTransferPotion(hero.Id, true), "Potion capacity");
            Require(guild.TryTransferPotion(hero.Id, false) && guild.TryTransferPotion(hero.Id, true), "Return portion to storage");
            var saved = GuildSaveData.Capture(guild, new ExpeditionSelection("1"));
            guild = GuildState.Restore(JsonUtility.FromJson<GuildSaveData>(JsonUtility.ToJson(saved)));
            Require(guild.Roster[0].Weapon == ItemDefinitions.Weapon && guild.Roster[0].Armor == ItemDefinitions.Armor &&
                guild.Roster[0].HealingPotions == 2 && guild.Inventory.Count == 1, "Version 2 roundtrip");
            var legacy = GuildSaveData.Capture(new GuildState(), new ExpeditionSelection("1")); legacy.version = 1;
            Require(GuildState.Restore(legacy).Roster.All(a => a.Weapon == null && a.Armor == null && a.HealingPotions == 0), "v1 migration");
            saved.roster[0].armor = "iron-edge";
            Reject(() => GuildState.Restore(saved)); saved.roster[0].armor = "crypt-mail";
            saved.roster[0].potions = 3; Reject(() => GuildState.Restore(saved));

            var grid = new HexGrid.HexGrid();
            UnitRuntimeState.TrySpawn(grid, "actor", HeroDefinitions.Defaults[0], new HexCoordinates(1, 1), out var actor);
            UnitRuntimeState.TrySpawn(grid, "enemy", new UnitDefinition("enemy", "Enemy", 0, maxHealth: 100), new HexCoordinates(2, 1), out var enemy, UnitTeam.Enemy);
            actor.SetLoadout(ItemDefinitions.Weapon, ItemDefinitions.Armor, 2);
            var turns = new TurnManager(grid, new[] { actor }); turns.TryStartNextTurn();
            var combat = new CombatSystem(grid, turns, new MaxDice());
            Require(combat.TryAttack(actor, enemy, out var attack) && attack.AttackBonus == actor.Definition.Attack + 1 &&
                attack.DamageDie == 8 && attack.Damage == 10, "Equipment affects real attack rolls");
            turns.TryCompleteAction(actor); turns.TryEndTurn(actor); turns.TryStartNextTurn();
            Require(!ConsumableSystem.TryHeal(turns, actor, out _) && actor.HealingPotions == 2 && turns.ActionAvailable, "Full HP consumes nothing");
            actor.ApplyDamage(3);
            Require(ConsumableSystem.TryHeal(turns, actor, out int healed) && healed == 3 && actor.CurrentHealth == actor.Definition.MaxHealth &&
                actor.HealingPotions == 1 && turns.RemainingMovement == actor.Definition.Movement, "Clamped self heal preserves movement");
            Require(!ConsumableSystem.TryHeal(turns, actor, out _) && !turns.ActionAvailable, "No double use during resolution");
            turns.TryCompleteAction(actor); actor.ApplyDamage(10);
            Require(!ConsumableSystem.TryHeal(turns, actor, out _), "One action per turn");
            turns.TryEndTurn(actor); turns.TryStartNextTurn();
            Require(ConsumableSystem.TryHeal(turns, actor, out healed) && healed == 8 && actor.HealingPotions == 0, "Defined potion healing");
            turns.TryCompleteAction(actor); turns.TryEndTurn(actor); turns.TryStartNextTurn();
            Require(!ConsumableSystem.TryHeal(turns, actor, out _) && turns.ActionAvailable, "Empty stock consumes no action");
            actor.ApplyDamage(actor.CurrentHealth);
            Require(!ConsumableSystem.TryHeal(turns, actor, out _), "Cannot resurrect by potion");

            foreach (bool recovered in new[] { true, false })
            {
                guild = Stocked(); guild.TryEquip("warrior-1", "iron-edge"); guild.TryEquip("warrior-1", "crypt-mail");
                guild.TryTransferPotion("warrior-1", true); guild.TryTransferPotion("warrior-1", true);
                var run = new RunFixture(guild);
                Require(!guild.TryUnequip("warrior-1", ItemCategory.Weapon) && !guild.TryTransferPotion("warrior-1", false), "Away loadout immutable");
                var victim = run.Party.First(u => u.InstanceId == "warrior-1"); victim.ApplyDamage(victim.CurrentHealth);
                if (recovered) run.Extract(); else Require(run.Run.TryRetreat(true), "Retreat fixture");
                int loot = run.Run.Result.Items.Count;
                Require(guild.TryReturn() && !guild.TryReturn(), "Result applied once");
                Require(guild.Inventory.Count == 1 + loot + (recovered ? 4 : 0) &&
                    guild.Roster[0].Weapon == null && guild.Roster[0].Armor == null && guild.Roster[0].HealingPotions == 0,
                    "Recovered body returns gear/unused potions once; abandoned gear lost");
                if (recovered) Require(guild.TryResurrect("warrior-1") && guild.Roster[0].Weapon == null, "Resurrection cannot duplicate gear");
            }
            guild = Stocked(); guild.TryEquip("rogue-1", "iron-edge");
            guild.TryTransferPotion("rogue-1", true); guild.TryTransferPotion("rogue-1", true);
            var survivingRun = new RunFixture(guild);
            var survivor = survivingRun.Turns.ActiveUnit;
            Require(survivor.InstanceId == "rogue-1", "Fixture initiative");
            survivor.ApplyDamage(10);
            Require(ConsumableSystem.TryHeal(survivingRun.Turns, survivor, out _), "Use potion during expedition");
            survivingRun.Turns.TryCompleteAction(survivor);
            survivingRun.Turns.TryEndTurn(survivor); survivingRun.Turns.TryStartNextTurn();
            survivingRun.Extract(); Require(guild.TryReturn(), "Survivor return");
            var returned = guild.Roster.First(a => a.Id == survivor.InstanceId);
            Require(returned.HealingPotions == 1 && returned.Weapon == ItemDefinitions.Weapon, "Survivor retains remaining loadout");
            var nextRun = new RunFixture(guild);
            Require(nextRun.Party.First(a => a.InstanceId == returned.Id).HealingPotions == 1, "Next run keeps remaining stock");
            foreach (var member in nextRun.Party) member.ApplyDamage(member.CurrentHealth);
            nextRun.Run.RefreshOutcome();
            Require(guild.TryReturn() && returned.Weapon == null && returned.HealingPotions == 0, "Defeat loses carried loadout");
            Debug.Log("WP-21/22 passed: inventory conservation, equipped stats, save migration, potion action locks, recovery and permanent loss.");
        }
        public static void RunBatch() => HexPresentationChecks.RunBatch();
        private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException("WP-21/22: " + message); }
        private static void Reject(Action action)
        { try { action(); } catch (ArgumentException) { return; } throw new InvalidOperationException("Invalid loadout save accepted."); }
    }
}




