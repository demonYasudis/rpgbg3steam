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
    public static class ProgressionChecks
    {
        private sealed class MaxDice : IDice { public int Roll(int sides) => sides; }
        private sealed class Fixture
        {
            public readonly GuildState Guild;
            public readonly DungeonMap Map = DungeonGenerator.Generate(27);
            public readonly List<UnitRuntimeState> Units = new List<UnitRuntimeState>();
            public readonly TurnManager Turns;
            public readonly ExpeditionRun Run;
            public Fixture(GuildState guild)
            {
                Guild = guild; var party = guild.BeginExpedition();
                for (int i = 0; i < party.Count; i++)
                {
                    Require(UnitRuntimeState.TrySpawn(Map.Grid, party[i].Id, party[i].Definition, Map.PlayerSpawns[i], out var unit), "Spawn");
                    unit.SetLoadout(party[i].Weapon, party[i].Armor, party[i].HealingPotions);
                    unit.SetTraining(party[i].TrainingAttack, party[i].TrainingDefense);
                    unit.ApplyDamage(unit.Definition.MaxHealth - party[i].Health); Units.Add(unit);
                }
                Turns = new TurnManager(Map.Grid, Units); Run = new ExpeditionRun(Map, Turns);
                guild.AttachRun(Run); Turns.TryStartNextTurn();
            }
            public void Extract()
            {
                var actor = Turns.ActiveUnit;
                Require(actor.TryRelocate(Map.Grid, Run.Chest) && Run.TryOpenChest(actor), "Chest");
                var occupant = Units.FirstOrDefault(u => u.IsAlive && u.Position == Run.Extraction);
                if (occupant != null) Require(occupant.TryRelocate(Map.Grid, Map.Grid.Cells.First(c => TerrainRules.CanWalk(c.Terrain) && !c.IsOccupied).Coordinates), "Free exit");
                Require(actor.TryRelocate(Map.Grid, Run.Extraction) && Run.TryExtract(actor, true) && Guild.TryReturn(), "Extract");
            }
        }

        [MenuItem("Tools/Guild Tactics/Validate Hero Progression")]
        public static void Run()
        {
            int[] values = { 0, 99, 100, 249, 250, 449, 450, 699, 700 };
            int[] levels = { 1, 1, 2, 2, 3, 3, 4, 4, 5 };
            for (int i = 0; i < values.Length; i++) Require(HeroProgression.Level(values[i]) == levels[i], "Level thresholds");
            var guild = new GuildState(500);
            for (int i = 0; i < 9; i++)
            {
                new Fixture(guild).Extract();
                Require(!guild.TryReturn(), "Result applied only once");
                Require(guild.Roster.Take(4).All(h => h.Experience == Math.Min(700, (i + 1) * 100)), "XP and cap");
                Require(guild.Roster.Skip(4).All(h => h.Experience == 0), "Reserve receives no XP");
            }
            var hero = guild.Roster[0];
            Require(hero.AvailableUpgrades == 4 && guild.TryUpgrade(hero.Id, HeroUpgrade.Attack) &&
                guild.TryUpgrade(hero.Id, HeroUpgrade.Defense) && guild.TryUpgrade(hero.Id, HeroUpgrade.Attack) &&
                guild.TryUpgrade(hero.Id, HeroUpgrade.Defense) && !guild.TryUpgrade(hero.Id, HeroUpgrade.Attack), "Exactly four choices");
            Require(hero.Attack == hero.Definition.Attack + 2 && hero.Defense == hero.Definition.Defense + 2, "Training bonuses");
            var data = GuildSaveData.Capture(guild, new ExpeditionSelection("24"));
            data.items = new[] { "iron-edge", "crypt-mail" };
            guild = GuildState.Restore(JsonUtility.FromJson<GuildSaveData>(JsonUtility.ToJson(data)));
            hero = guild.Roster[0];
            Require(hero.Level == 5 && hero.AvailableUpgrades == 0 && hero.TrainingAttack == 2, "Progress survives JSON");
            Require(guild.TryEquip(hero.Id, "iron-edge") && guild.TryEquip(hero.Id, "crypt-mail") &&
                hero.Attack == hero.Definition.Attack + 3 && hero.Defense == hero.Definition.Defense + 4, "Training and gear compose");
            Require(guild.TryUnequip(hero.Id, ItemCategory.Weapon) && hero.Attack == hero.Definition.Attack + 2, "Removing gear retains training");
            var fixture = new Fixture(guild);
            var warrior = fixture.Units.First(u => u.InstanceId == hero.Id);
            Require(warrior.Attack == hero.Attack && warrior.Defense == hero.Defense &&
                !guild.TryUpgrade(hero.Id, HeroUpgrade.Attack), "Runtime copies training and locks guild");
            warrior.ApplyDamage(warrior.CurrentHealth); fixture.Extract();
            Require(hero.Experience == 700 && hero.Status == AdventurerStatus.BodyRecovered && guild.TryResurrect(hero.Id) &&
                hero.TrainingAttack == 2 && hero.TrainingDefense == 2 && !guild.TryResurrect(hero.Id), "Death/resurrection retain progression once");
            var recruit = guild.Candidates[0]; Require(guild.TryHire(recruit) && guild.Roster.Last().Level == 1, "Recruit starts at level one");
            guild = new GuildState(500); hero = guild.Roster[0]; hero.Experience = 100;
            Require(guild.TryUpgrade(hero.Id, HeroUpgrade.Attack), "First level choice");
            fixture = new Fixture(guild);
            var victim = fixture.Units.First(u => u.InstanceId == hero.Id);
            // Use the real roll resolver to verify permanent bonuses affect combat, not only the guild label.
            Require(CombatSystem.PrepareAttack(new MaxDice(), victim, fixture.Units[1]).AttackBonus == hero.Definition.Attack + 1, "Trained attack roll");
            victim.ApplyDamage(victim.CurrentHealth); fixture.Extract();
            Require(hero.Experience == 100 && hero.TrainingAttack == 1 && guild.TryResurrect(hero.Id) && hero.Experience == 100,
                "A recovered casualty receives no reward and keeps its earned choice");
            guild = new GuildState(); fixture = new Fixture(guild);
            RetreatChecks.ReachExit(fixture.Turns, fixture.Run);
            Require(fixture.Run.TryRetreat(true) && guild.TryReturn() && guild.Roster.Take(4).All(h => h.Experience == 25), "Retreat rewards survivors");
            fixture = new Fixture(guild);
            foreach (var unit in fixture.Units) unit.ApplyDamage(unit.CurrentHealth);
            fixture.Run.RefreshOutcome(); Require(guild.TryReturn() && guild.Roster.Take(4).All(h => h.Experience == 25), "Defeat grants no XP");
            foreach (int version in new[] { 1, 2, 3 })
            {
                var legacy = GuildSaveData.Capture(new GuildState(), new ExpeditionSelection("24")); legacy.version = version;
                Require(GuildState.Restore(legacy).Roster.All(h => h.Level == 1 && h.AvailableUpgrades == 0), "Legacy migration");
            }
            data = GuildSaveData.Capture(new GuildState(), new ExpeditionSelection("24"));
            data.roster[0].trainingAttack = 1; Reject(() => GuildState.Restore(data));
            data.roster[0].trainingAttack = 0; data.roster[0].experience = 701; Reject(() => GuildState.Restore(data));
            data.roster[0].experience = -1; Reject(() => GuildState.Restore(data));
            Debug.Log("WP-24 passed: thresholds, XP once, cap, choices, equipment, runtime, death/resurrection, recruits, retreat/defeat and save migration.");
        }
        public static void RunBatch() => HexPresentationChecks.RunBatch();
        private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException("WP-24: " + message); }
        private static void Reject(Action action) { try { action(); } catch (ArgumentException) { return; } throw new InvalidOperationException("Invalid progression accepted."); }
    }
}
