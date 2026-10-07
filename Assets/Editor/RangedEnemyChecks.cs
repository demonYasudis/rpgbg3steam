using System;
using System.Linq;
using GuildTactics.Combat;
using GuildTactics.Generation;
using GuildTactics.HexGrid;
using GuildTactics.Units;
using GuildTactics.Visibility;
using UnityEditor;
using UnityEngine;
using GridModel = GuildTactics.HexGrid.HexGrid;

namespace GuildTactics.Editor
{
    public static class RangedEnemyChecks
    {
        private static GameObject presentation;
        private static PlayerUnitController controller;
        private static UnitRuntimeState presentingArcher;
        private static HexCoordinates initialPosition;
        private static double deadline;
        internal static void BeginPresentation(PlayerUnitController sceneController)
        {
            var grid = new GridModel(); var layout = new HexLayout();
            var camera = sceneController.GetComponent<HexGridInteraction>().GridCamera;
            presentation = new GameObject("WP26 Ranged AI Checks");
            var view = presentation.AddComponent<HexGridView>(); view.Initialize(grid, layout);
            var interaction = presentation.AddComponent<HexGridInteraction>(); interaction.Initialize(grid, layout, view, camera);
            var feedback = presentation.AddComponent<CombatText>(); feedback.Initialize(camera, 26);
            controller = presentation.AddComponent<PlayerUnitController>();
            var archetype = EnemyDefinitions.Regular.Single(a => a.Unit.Id == "crypt-bowman");
            initialPosition = new HexCoordinates(2, 3);
            controller.Initialize(grid, layout, view, interaction, 0, new MaxDice(), feedback,
                enableFog: true, playerSpawns: new[] { new HexCoordinates(3, 3), new HexCoordinates(4, 3),
                    new HexCoordinates(3, 4), new HexCoordinates(4, 4) },
                encounter: new[] { new EnemyPlacement(archetype, initialPosition) });
            presentingArcher = controller.Enemies.Single();
            deadline = EditorApplication.timeSinceStartup + 15;
        }
        internal static bool PollPresentation()
        {
            Require(EditorApplication.timeSinceStartup < deadline, "Live ranged AI must finish its turn");
            int damage = controller.Units.Sum(u => u.Definition.MaxHealth - u.CurrentHealth);
            if (damage > 0 && controller.Turns.ActiveUnit != presentingArcher)
            {
                Require(damage == 7 && presentingArcher.Position != initialPosition &&
                    controller.Units.All(u => presentingArcher.Position.DistanceTo(u.Position) > 1),
                    "Live controller retreats, attacks once and hands turn back");
                UnityEngine.Object.Destroy(presentation);
                Debug.Log("WP-26 Play Mode passed: ranged controller movement, one attack and completed enemy turn.");
                return true;
            }
            if (controller.CanPlayerAct) Require(controller.TryEndTurn(), "Pass player turn");
            return false;
        }
        private sealed class MaxDice : IDice { public int Roll(int sides) => sides; }
        [MenuItem("Tools/Guild Tactics/Validate Ranged Enemies")]
        public static void Run()
        {
            var definition = EnemyDefinitions.Regular.Single(a => a.Unit.Id == "crypt-bowman").Unit;
            var grid = new GridModel(9, 7);
            UnitRuntimeState.TrySpawn(grid, "archer", definition, new HexCoordinates(3, 3), out var archer, UnitTeam.Enemy);
            UnitRuntimeState.TrySpawn(grid, "hero", new UnitDefinition("hero", "Hero", 3), new HexCoordinates(4, 3), out var hero);
            using var fog = new FogOfWarSystem(grid, new[] { hero });
            var turns = new TurnManager(grid, new[] { archer, hero }, fog); turns.TryStartNextTurn();
            var combat = new CombatSystem(grid, turns, new MaxDice());
            var targets = new[] { hero };
            var destination = RangedBrain.ChooseDestination(grid, archer, targets, turns.RemainingMovement, turns);
            Require(destination.DistanceTo(hero.Position) > 1 && destination.DistanceTo(hero.Position) <= 4, "Retreat to safe firing range");
            Require(destination == RangedBrain.ChooseDestination(grid, archer, targets, turns.RemainingMovement, turns), "Deterministic destination");
            Require(turns.TryBeginMovement(archer, destination, out _) && turns.TryCompleteMovement(archer) &&
                combat.TryAttack(archer, hero, out _) && !combat.TryAttack(archer, hero, out _) &&
                turns.TryCompleteAction(archer) && turns.TryEndTurn(archer), "Move then attack once and finish");
            turns.TryStartNextTurn(); turns.TryEndTurn(hero); turns.TryStartNextTurn();
            Require(RangedBrain.ChooseDestination(grid, archer, targets, 3, turns) == archer.Position, "Keep existing safe shot");
            Require(RangedBrain.ChooseDestination(grid, archer, targets, 0, turns) == archer.Position, "Zero budget");
            Require(RangedBrain.ChooseDestination(grid, archer, Array.Empty<UnitRuntimeState>(), 3, turns) == archer.Position, "No targets");
            grid = new GridModel(10, 5);
            UnitRuntimeState.TrySpawn(grid, "archer", definition, new HexCoordinates(1, 2), out archer, UnitTeam.Enemy);
            UnitRuntimeState.TrySpawn(grid, "hero", new UnitDefinition("hero", "Hero", 3), new HexCoordinates(7, 2), out hero);
            using var distantFog = new FogOfWarSystem(grid, new[] { hero });
            turns = new TurnManager(grid, new[] { archer, hero }, distantFog); turns.TryStartNextTurn();
            destination = RangedBrain.ChooseDestination(grid, archer, new[] { hero }, 3, turns);
            Require(destination != archer.Position && destination.DistanceTo(hero.Position) <= 4 &&
                HexPathfinder.FindReachable(grid, archer.Position, 3).Costs.ContainsKey(destination), "Approach a visible out-of-range target within budget");
            for (int r = 0; r < 5; r++) grid.GetCell(new HexCoordinates(3, r)).Terrain = TerrainType.Blocked;
            Require(!turns.CanSee(archer, hero.Position) &&
                RangedBrain.ChooseDestination(grid, archer, new[] { hero }, 3, turns) == archer.Position, "Hidden targets are not pursued through walls");
            hero.ApplyDamage(hero.CurrentHealth);
            Require(RangedBrain.ChooseDestination(grid, archer, new[] { hero }, 3, turns) == archer.Position, "Dead targets ignored");
            bool seenArcher = false;
            for (int seed = 0; seed < 100; seed++)
            {
                var map = DungeonGenerator.Generate(seed);
                var first = EncounterGenerator.Generate(map);
                var second = EncounterGenerator.Generate(map);
                Require(first.Select(p => p.Archetype.Unit.Id + p.Position).SequenceEqual(second.Select(p => p.Archetype.Unit.Id + p.Position)), "Encounter reproducibility");
                if (first.Any(p => p.Archetype.Unit.Id == definition.Id)) seenArcher = true;
                Require(first.Sum(p => p.Archetype.Cost) <= 10, "Budget preserved");
            }
            Require(seenArcher, "Ranged archetype appears in seeded encounters");
            Debug.Log("WP-26 passed: safe retreat, firing positions, budget, one action, turn completion, hidden/dead targets, deterministic encounters.");
        }
        public static void RunBatch() => HexPresentationChecks.RunBatch();
        private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException("WP-26: " + message); }
    }
}
