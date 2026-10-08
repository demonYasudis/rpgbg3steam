using System;
using System.Collections.Generic;
using System.IO;
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
    public static class ProgressionChecks
    {
        private sealed class MaxDice : IDice { public int Roll(int sides) => sides; }
        private sealed class DefenseDice : IDice { public int Roll(int sides) => 14; }
        private sealed class Fixture
        {
            public readonly DungeonMap Map;
            public readonly List<UnitRuntimeState> Units = new List<UnitRuntimeState>();
            public readonly TurnManager Turns;
            public readonly ExpeditionRun Run;
            public Fixture(GuildState guild, int seed)
            {
                Map = DungeonGenerator.Generate(seed);
                var party = guild.BeginExpedition();
                for (int i = 0; i < party.Count; i++)
                {
                    var hero = party[i];
                    Require(UnitRuntimeState.TrySpawn(Map.Grid, hero.Id, hero.Definition, Map.PlayerSpawns[i], out var unit), "Spawn");
                    unit.InitializeProgression(hero.Progression);
                    unit.SetLoadout(hero.Weapon, hero.Armor, hero.HealingPotions);
                    unit.ApplyDamage(unit.Definition.MaxHealth - hero.Health);
                    Units.Add(unit);
                }
                Turns = new TurnManager(Map.Grid, Units); Run = new ExpeditionRun(Map, Turns);
                guild.AttachRun(Run); Turns.TryStartNextTurn();
            }
            public void Extract()
            {
                var actor = Turns.ActiveUnit;
                Require(actor.TryRelocate(Map.Grid, Run.Chest) && Run.TryOpenChest(actor), "Chest");
                var occupant = Units.FirstOrDefault(u => u.IsAlive && u.Position == Run.Extraction);
                if (occupant != null)
                    Require(occupant.TryRelocate(Map.Grid, Map.Grid.Cells.First(c => TerrainRules.CanWalk(c.Terrain) && !c.IsOccupied).Coordinates), "Clear extraction");
                Require(actor.TryRelocate(Map.Grid, Run.Extraction) && Run.TryExtract(actor, true), "Extract");
            }
        }

        [MenuItem("Tools/Guild Tactics/Validate Hero Progression")]
        public static void Run()
        {
            int[] thresholds = { 0, 100, 250, 450, 700 };
            for (int level = 1; level <= HeroProgression.MaximumLevel; level++)
            {
                var progression = new HeroProgression();
                progression.AwardExperience(thresholds[level - 1]);
                Require(progression.Level == level && progression.PendingChoices == level - 1, "Exact level threshold");
                if (level > 1) Require(HeroProgression.LevelFor(thresholds[level - 1] - 1) == level - 1, "Immediately below threshold");
            }
            var capped = new HeroProgression(); capped.AwardExperience(int.MaxValue);
            Require(capped.Experience == 700 && capped.Level == 5 && capped.PendingChoices == 4, "Large award caps without overflow");
            Reject(() => capped.AwardExperience(-1));
            Require(!capped.TryChoose(3, HeroUpgrade.Accuracy) && !capped.TryChoose(2, (HeroUpgrade)99), "Choice order and enum");
            for (int level = 2; level <= 5; level++)
            {
                Require(capped.TryChoose(level, level % 2 == 0 ? HeroUpgrade.Accuracy : HeroUpgrade.Guard), "Earned choice");
                Require(!capped.TryChoose(level, HeroUpgrade.Guard), "Duplicate command rejected for same level");
            }
            Require(!capped.TryChoose(6, HeroUpgrade.Accuracy) && capped.AttackBonus == 2 && capped.DefenseBonus == 2, "Maximum development");

            var guild = new GuildState();
            int notifications = 0; guild.Changed += () => notifications++;
            Require(!guild.TryChooseUpgrade("warrior-1", 2, HeroUpgrade.Accuracy) &&
                !guild.TryChooseUpgrade("missing", 2, HeroUpgrade.Accuracy) && notifications == 0, "No unearned mutation");
            for (int seed = 0; seed < 9; seed++)
            {
                int oldExperience = guild.Roster[0].Progression.Experience;
                var fixture = new Fixture(guild, seed);
                Require(!guild.TryChooseUpgrade("warrior-1", 2, HeroUpgrade.Accuracy), "Away development locked");
                fixture.Extract();
                int gain = Math.Min(HeroProgression.ExtractionExperience, HeroProgression.MaximumExperience - oldExperience);
                Require(fixture.Run.Result.Adventurers.All(a => a.ExperienceGained == gain), "Result previews actual capped reward");
                int beforeReturn = notifications;
                Require(guild.TryReturn() && !guild.TryReturn() && notifications == beforeReturn + 1, "Result applied once");
                Require(guild.Roster[0].Progression.Experience == oldExperience + gain &&
                    guild.Roster[4].Progression.Experience == 0, "Only participating survivors gain XP");
                var hero = guild.Roster[0];
                while (hero.Progression.PendingChoices > 0)
                {
                    int level = hero.Progression.NextChoiceLevel;
                    Require(guild.TryChooseUpgrade(hero.Id, level, level % 2 == 0 ? HeroUpgrade.Accuracy : HeroUpgrade.Guard) &&
                        !guild.TryChooseUpgrade(hero.Id, level, HeroUpgrade.Guard), "Choice committed once");
                }
                guild = RoundTrip(guild);
                guild.Changed += () => notifications++;
            }
            Require(guild.Roster[0].Progression.Level == 5 && guild.Roster[0].Progression.Upgrades.Count == 4, "Repeated runs reach limit");
            var partialData = Capture(new GuildState());
            foreach (var saved in partialData.roster.Take(4)) saved.experience = 650;
            var partialGuild = GuildState.Restore(partialData);
            var partialRun = new Fixture(partialGuild, 16); partialRun.Extract();
            Require(partialRun.Run.Result.Adventurers.All(a => a.ExperienceGained == 50) && partialGuild.TryReturn() &&
                partialGuild.Roster.Take(4).All(a => a.Progression.Experience == 700), "Partial reward at XP cap");
            int gold = guild.Gold;
            Require(guild.TryHire(guild.Candidates[0].Id) && guild.Roster.Last().Progression.Level == 1 &&
                guild.Roster.Last().Progression.Experience == 0 && guild.Gold == gold - GuildState.HiringCost, "Recruits begin at level one");
            var retreat = new Fixture(guild, 23);
            Require(retreat.Run.TryRetreat(true) && retreat.Run.Result.Adventurers.All(a => a.ExperienceGained == 0) &&
                guild.TryReturn() && guild.Roster[0].Progression.Experience == 700, "Retreat gives no XP");

            var casualty = new Fixture(guild, 24);
            var victim = casualty.Units.First(u => u.InstanceId == "warrior-1");
            victim.ApplyDamage(victim.CurrentHealth);
            casualty.Extract();
            Require(casualty.Run.Result.Adventurers.Single(a => a.InstanceId == victim.InstanceId).ExperienceGained == 0 &&
                guild.TryReturn(), "Dead participants gain no XP");
            guild = RoundTrip(guild);
            var veteran = guild.Roster.Single(a => a.Id == victim.InstanceId);
            Require(veteran.Status == AdventurerStatus.BodyRecovered && veteran.Progression.Experience == 700 &&
                !guild.TryChooseUpgrade(veteran.Id, 2, HeroUpgrade.Accuracy), "Dead veteran retains development but cannot train");
            Require(guild.TryResurrect(veteran.Id) && !guild.TryResurrect(veteran.Id) &&
                veteran.Progression.Upgrades.Count == 4 && veteran.Progression.AttackBonus == 2 &&
                veteran.Progression.DefenseBonus == 2 && veteran.Progression.Experience == 700, "Resurrection preserves bonuses once");
            CheckUnspentDeath();
            CheckCombatAndEquipment(guild, veteran.Id);
            CheckSaves();
            CheckLanguages();
            Debug.Log("WP-24 passed: XP recipients, once-only rewards, level thresholds/cap, choices, runtime stats, gear, death, resurrection and schemas 1-4.");
        }

        private static GuildSaveData Capture(GuildState guild) => GuildSaveData.Capture(guild, new ExpeditionSelection("progression"));
        private static GuildState RoundTrip(GuildState guild) => GuildState.Restore(JsonUtility.FromJson<GuildSaveData>(JsonUtility.ToJson(Capture(guild))));
        private static void CheckUnspentDeath()
        {
            var data = Capture(new GuildState());
            data.roster[0].experience = 100;
            var guild = GuildState.Restore(data);
            var fixture = new Fixture(guild, 12);
            foreach (var hero in fixture.Units) hero.ApplyDamage(hero.CurrentHealth);
            fixture.Run.RefreshOutcome();
            Require(fixture.Run.Result.Outcome == ExpeditionOutcome.Defeated &&
                fixture.Run.Result.Adventurers.All(a => a.ExperienceGained == 0) && guild.TryReturn(), "Defeat awards no XP");
            guild = RoundTrip(guild);
            Require(guild.Roster[0].Status == AdventurerStatus.Lost && guild.Roster[0].Progression.Experience == 100 &&
                guild.Roster[0].Progression.PendingChoices == 1 && !guild.TryResurrect("warrior-1"), "Permanent loss retains record without revival");
            guild = GuildState.Restore(data);
            var recovered = new Fixture(guild, 42);
            var victim = recovered.Units.Single(u => u.InstanceId == "warrior-1");
            victim.ApplyDamage(victim.CurrentHealth);
            recovered.Extract();
            Require(recovered.Run.Result.Adventurers.Single(a => a.InstanceId == "warrior-1").ExperienceGained == 0 &&
                guild.TryReturn() && guild.Roster[0].Progression.Experience == 100 &&
                guild.Roster[1].Progression.Experience == 100, "Recovered casualty gets no fresh XP while survivors do");
            guild = RoundTrip(guild);
            Require(!guild.TryChooseUpgrade("warrior-1", 2, HeroUpgrade.Accuracy) && guild.TryResurrect("warrior-1") &&
                guild.TryChooseUpgrade("warrior-1", 2, HeroUpgrade.Accuracy) && !guild.TryChooseUpgrade("warrior-1", 2, HeroUpgrade.Guard),
                "Unspent choice survives death and resurrection");
        }
        private static void CheckCombatAndEquipment(GuildState guild, string id)
        {
            var data = Capture(guild); data.items = new[] { "iron-edge", "crypt-mail" };
            guild = GuildState.Restore(data);
            var hero = guild.Roster.Single(a => a.Id == id);
            int baseAttack = hero.Definition.Attack, baseDefense = hero.Definition.Defense;
            for (int i = 0; i < 3; i++)
            {
                Require(guild.TryEquip(id, "iron-edge") && guild.TryEquip(id, "crypt-mail"), "Equip veteran");
                Require(hero.Attack == baseAttack + 3 && hero.Defense == baseDefense + 4, "Training plus gear exactly once");
                Require(guild.TryUnequip(id, ItemCategory.Weapon) && guild.TryUnequip(id, ItemCategory.Armor) &&
                    hero.Attack == baseAttack + 2 && hero.Defense == baseDefense + 2, "Training remains after gear removal");
            }
            var grid = new HexGrid.HexGrid();
            UnitRuntimeState.TrySpawn(grid, id, hero.Definition, new HexCoordinates(1, 1), out var actor);
            actor.InitializeProgression(hero.Progression); actor.SetLoadout(ItemDefinitions.Weapon, ItemDefinitions.Armor, 0);
            UnitRuntimeState.TrySpawn(grid, "enemy", new UnitDefinition("enemy", "Enemy", 0, initiative: 100, maxHealth: 100, attack: 1),
                new HexCoordinates(2, 1), out var target, UnitTeam.Enemy);
            var turns = new TurnManager(grid, new[] { actor }); turns.TryStartNextTurn();
            var combat = new CombatSystem(grid, turns, new MaxDice());
            Require(combat.TryAttack(actor, target, out var result) && result.AttackBonus == baseAttack + 3 &&
                actor.Defense == baseDefense + 4 && actor.Level == 5, "Runtime combat uses veteran bonuses");
            var enemyTurns = new TurnManager(grid, new[] { actor, target }); enemyTurns.TryStartNextTurn();
            var enemyCombat = new CombatSystem(grid, enemyTurns, new DefenseDice());
            Require(enemyCombat.TryAttack(target, actor, out var incoming) && !incoming.Hit && incoming.Defense == baseDefense + 4,
                "Enemy attacks use trained defense");
            Reject(() => actor.InitializeProgression(hero.Progression));
            Reject(() => target.InitializeProgression(hero.Progression));
            Require(hero.Definition.Attack == baseAttack && hero.Definition.Defense == baseDefense &&
                HeroDefinitions.Defaults[0].Attack == 4, "Shared definitions unaffected");
        }
        private static void CheckSaves()
        {
            foreach (int version in new[] { 1, 2, 3 })
            {
                var legacy = Capture(new GuildState()); legacy.version = version;
                foreach (var saved in legacy.roster) { saved.experience = 0; saved.upgrades = null; }
                var migrated = GuildState.Restore(legacy);
                Require(migrated.Roster.All(a => a.Progression.Level == 1 && a.Progression.Upgrades.Count == 0), "Legacy starts with zero development");
                Require(RoundTrip(migrated).Roster.All(a => a.Progression.Experience == 0), "Migration resaves as current schema");
            }
            foreach (int invalid in new[] { -1, 701, int.MaxValue })
            { var data = Capture(new GuildState()); data.roster[0].experience = invalid; Reject(() => GuildState.Restore(data)); }
            var bad = Capture(new GuildState()); bad.roster[0].upgrades = null; Reject(() => GuildState.Restore(bad));
            bad = Capture(new GuildState()); bad.roster[0].upgrades = new[] { 0 }; Reject(() => GuildState.Restore(bad));
            bad = Capture(new GuildState()); bad.roster[0].experience = 700; bad.roster[0].upgrades = new[] { 99 }; Reject(() => GuildState.Restore(bad));
            bad = Capture(new GuildState()); bad.roster[0].experience = 700; bad.roster[0].upgrades = new[] { 0, 0, 0, 0, 0 }; Reject(() => GuildState.Restore(bad));

            string directory = Path.Combine(Path.GetTempPath(), "GuildTacticsProgression-" + Guid.NewGuid().ToString("N"));
            try
            {
                var data = Capture(new GuildState()); data.roster[0].experience = 450;
                var guild = GuildState.Restore(data); var offers = new ExpeditionSelection("progression");
                var store = new GuildSaveStore(Path.Combine(directory, "guild.json"));
                guild.Changed += () => Require(store.TrySave(guild, offers, out _), "Choice autosaves atomically");
                Require(guild.TryChooseUpgrade("warrior-1", 2, HeroUpgrade.Accuracy), "Commit development checkpoint");
                Require(new GuildSaveStore(store.Path).TryLoad(out var loaded, out _, out _) && loaded.Roster[0].Progression.Experience == 450 &&
                    loaded.Roster[0].Progression.AttackBonus == 1 && loaded.Roster[0].Progression.PendingChoices == 2, "Reopen keeps spent and unspent choices");
                Require(!loaded.TryChooseUpgrade("warrior-1", 2, HeroUpgrade.Guard), "Reload cannot repeat spent choice");
                Require(new GuildState().Roster.All(a => a.Progression.Level == 1), "New game resets development");
            }
            finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        }
        private static void CheckLanguages()
        {
            var previous = Localization.Language;
            try
            {
                Localization.SetLanguage(InterfaceLanguage.Russian, false);
                Require(Localization.T("Attack +1") == "Атака +1" && Localization.F(" | +{0} XP", 100) == " | +100 опыта", "Russian development and result");
                Localization.SetLanguage(InterfaceLanguage.English, false);
                Require(Localization.T("Defense +1") == "Defense +1", "English development");
            }
            finally { Localization.SetLanguage(previous, false); }
        }

        public static void ValidatePresentation(GameBootstrap bootstrap)
        {
            Require(!bootstrap.Guild.IsAway && bootstrap.TryLaunchExpedition(), "Progression scene launch");
            var controller = bootstrap.ActiveController;
            foreach (var enemy in controller.Enemies) enemy.ApplyDamage(enemy.CurrentHealth);
            AdvanceToPlayer(controller);
            var actor = controller.Turns.ActiveUnit;
            Require(actor.Team == UnitTeam.Player, "Initial player turn");
            Require(actor.TryRelocate(bootstrap.Grid, controller.Expedition.Chest) && controller.Expedition.TryOpenChest(actor), "Scene chest");
            foreach (var unit in controller.Units)
                if (unit.IsAlive && unit.Position == controller.Expedition.Extraction)
                    Require(unit.TryRelocate(bootstrap.Grid, bootstrap.Grid.Cells.First(c => TerrainRules.CanWalk(c.Terrain) && !c.IsOccupied).Coordinates), "Scene exit cleared");
            Require(actor.TryRelocate(bootstrap.Grid, controller.Expedition.Extraction) && controller.Expedition.TryExtract(actor) &&
                bootstrap.TryReturnToGuild() && !bootstrap.TryReturnToGuild(), "Scene XP awarded once");
            var hero = bootstrap.Guild.Roster[0];
            Require(hero.Progression.Experience == 100 && bootstrap.Guild.TryChooseUpgrade(hero.Id, 2, HeroUpgrade.Accuracy) &&
                !bootstrap.Guild.TryChooseUpgrade(hero.Id, 2, HeroUpgrade.Guard), "Scene level choice");
            Require(bootstrap.TryLaunchExpedition(), "Second scene launch with training");
            var veteran = bootstrap.ActiveController.Units.Single(u => u.InstanceId == hero.Id);
            Require(veteran.Level == 2 && veteran.TrainingAttackBonus == 1 && veteran.Attack == hero.Attack, "Controller copies development into runtime");
            AdvanceToPlayer(bootstrap.ActiveController);
            Require(bootstrap.ActiveController.Expedition.TryRetreat(true) && bootstrap.TryReturnToGuild() && hero.Progression.Experience == 100 &&
                bootstrap.TryStartNewGuild() && bootstrap.Guild.Roster.All(a => a.Progression.Experience == 0), "Scene retreat and new game");
        }

        private static void AdvanceToPlayer(PlayerUnitController controller)
        {
            var turns = controller.Turns;
            for (int i = 0; i < turns.Order.Count && turns.ActiveUnit.Team != UnitTeam.Player; i++)
            { turns.TryEndTurn(turns.ActiveUnit); turns.TryStartNextTurn(); }
            Require(turns.ActiveUnit.Team == UnitTeam.Player, "Scene can reach a player turn");
        }
        public static void RunBatch() => HexPresentationChecks.RunBatch();
        private static void Reject(Action action)
        { try { action(); } catch (ArgumentException) { return; } throw new InvalidOperationException("WP-24: invalid progression accepted."); }
        private static void Require(bool condition, string message)
        { if (!condition) throw new InvalidOperationException("WP-24: " + message); }
    }
}
