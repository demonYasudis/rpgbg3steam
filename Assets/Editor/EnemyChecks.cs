using System;
using System.Linq;
using GuildTactics.Combat;
using GuildTactics.HexGrid;
using GuildTactics.Units;
using UnityEditor;
using UnityEngine;
using GridModel = GuildTactics.HexGrid.HexGrid;

namespace GuildTactics.Editor
{
    public static class EnemyChecks
    {
        private sealed class MaximumDice : IDice { public int Roll(int sides) => sides; }

        [MenuItem("Tools/Guild Tactics/Validate Melee Enemies")]
        public static void Run()
        {
            var grid = new GridModel(7, 5);
            var enemy = Spawn(grid, "enemy", 0, 2, UnitTeam.Enemy);
            var hero = Spawn(grid, "hero", 6, 2, UnitTeam.Player);
            var heroes = new[] { hero };
            for (int r = 0; r < 4; r++) grid.GetCell(new HexCoordinates(3, r)).Terrain = TerrainType.Blocked;
            var destination = MeleeBrain.ChooseDestination(grid, enemy, heroes, 3);
            Require(destination != enemy.Position, "Route around wall");
            Require(destination == MeleeBrain.ChooseDestination(grid, enemy, heroes, 3), "Deterministic destination");
            Require(HexPathfinder.FindPath(grid, enemy.Position, destination, 3).Count > 1, "Legal budgeted route");
            Require(MeleeBrain.ChooseDestination(grid, enemy, heroes, 0) == enemy.Position, "Zero movement");
            grid.GetCell(new HexCoordinates(3, 4)).Terrain = TerrainType.Pit;
            Require(MeleeBrain.ChooseDestination(grid, enemy, heroes, 3) == enemy.Position, "Unreachable target returns no move");
            var accessible = Spawn(grid, "accessible", 1, 0, UnitTeam.Player);
            Require(MeleeBrain.ChooseDestination(grid, enemy, new[] { hero, accessible }, 3) != enemy.Position,
                "Skip unreachable target for reachable hero");
            accessible.ApplyDamage(accessible.CurrentHealth);
            Require(MeleeBrain.ChooseDestination(grid, enemy, new[] { accessible }, 3) == enemy.Position, "Ignore dead hero");
            grid.GetCell(new HexCoordinates(3, 4)).Terrain = TerrainType.Ground;
            foreach (var cell in grid.Cells)
                if (cell.Coordinates != enemy.Position && cell.Terrain == TerrainType.Ground) cell.MovementCost = 2;
            destination = MeleeBrain.ChooseDestination(grid, enemy, heroes, 3);
            Require(enemy.Position.DistanceTo(destination) == 1, "Weighted movement budget");
            foreach (var cell in grid.Cells) cell.MovementCost = 1;
            var turns = new TurnManager(grid, new[] { enemy, hero });
            var combat = new CombatSystem(grid, turns, new MaximumDice());
            int moves = 0, hits = 0;
            for (int i = 0; i < 100 && BattleRules.Evaluate(turns.Order) == BattleOutcome.Ongoing; i++)
            {
                Require(turns.TryStartNextTurn(), "Turn starts");
                var actor = turns.ActiveUnit;
                if (actor.Team == UnitTeam.Enemy)
                {
                    var target = MeleeBrain.FindTarget(combat, actor, heroes);
                    if (target == null)
                    {
                        destination = MeleeBrain.ChooseDestination(grid, actor, heroes, turns.RemainingMovement);
                        if (destination != actor.Position)
                        {
                            Require(turns.TryBeginMovement(actor, destination, out _), "AI route commits");
                            Require(turns.TryCompleteMovement(actor), "Movement completes");
                            moves++;
                        }
                        target = MeleeBrain.FindTarget(combat, actor, heroes);
                    }
                    if (target != null)
                    {
                        Require(combat.TryAttack(actor, target, out _), "Attack after approach");
                        Require(!combat.TryAttack(actor, target, out _), "No duplicate attack");
                        turns.TryCompleteAction(actor);
                        hits++;
                    }
                }
                Require(turns.TryEndTurn(actor), "Every turn ends");
            }
            Require(moves > 0 && hits > 0 && BattleRules.Evaluate(turns.Order) == BattleOutcome.Defeat,
                "Complete deterministic battle navigates wall and ends in defeat");
            var survivor = Spawn(grid, "survivor", 0, 0, UnitTeam.Player);
            enemy.ApplyDamage(enemy.CurrentHealth);
            Require(BattleRules.Evaluate(new[] { survivor, enemy }) == BattleOutcome.Victory, "Victory");
            Require(BattleRules.Evaluate(Array.Empty<UnitRuntimeState>()) == BattleOutcome.Defeat, "Empty battle terminates");
            Debug.Log("WP-07 model checks passed: obstacles, unreachable/dead targets, weighted costs, deterministic AI and complete battle.");
        }

        public static void RunBatch() => HexPresentationChecks.RunBatch();
        private static GameObject root;
        private static PlayerUnitController controller;
        private static int stage;

        internal static void BeginPresentation(PlayerUnitController scene)
        {
            var camera = scene.GetComponent<HexGridInteraction>().GridCamera;
            var grid = new GridModel();
            var layout = new HexLayout();
            root = new GameObject("WP07 Enemy Checks");
            var view = root.AddComponent<HexGridView>();
            view.Initialize(grid, layout);
            var input = root.AddComponent<HexGridInteraction>();
            input.Initialize(grid, layout, view, camera);
            controller = root.AddComponent<PlayerUnitController>();
            controller.Initialize(grid, layout, view, input, 0.02f, new MaximumDice());
            root.AddComponent<TurnOrderUI>().Initialize(controller);
            Require(controller.Enemies.Count == 3 && controller.Turns.Order.Count == 7, "Both teams in initiative");
            stage = 0;
        }

        internal static bool PollPresentation()
        {
            if (stage == 0)
            {
                if (controller.SelectedUnit.Team == UnitTeam.Player)
                {
                    controller.TryEndTurn();
                    return false;
                }
                Require(!controller.CanPlayerAct && !controller.TryEndTurn() && !controller.TryWaitAction() &&
                    !controller.TryMoveSelected(new HexCoordinates(0, 0)) &&
                    !controller.TryAttackSelected(controller.Units[0].Position), "Player commands blocked in enemy turn");
                if (controller.LastAttack == null) return false;
                Require(controller.Units.Any(unit => unit.CurrentHealth < unit.Definition.MaxHealth), "Enemy damages hero");
                controller.enabled = false;
                controller.enabled = true;
                Require(controller.Turns.State == TurnState.SelectingAction && !controller.Turns.ActionAvailable,
                    "Interrupted enemy feedback resumes without second attack");
                stage = 1;
                return false;
            }
            if (stage == 1)
            {
                if (controller.SelectedUnit.Team == UnitTeam.Player)
                {
                    controller.TryEndTurn();
                    return false;
                }
                if (!controller.IsMoving) return false;
                var actor = controller.SelectedUnit;
                controller.enabled = false;
                controller.enabled = true;
                var view = controller.GetComponentsInChildren<UnitView>().Single(item => item.State == actor);
                Require(!controller.IsMoving && controller.Turns.State == TurnState.SelectingAction &&
                    Vector3.Distance(view.transform.position,
                        new HexLayout().ToWorld(actor.Position) + new Vector3(0, 0, -0.2f)) < 0.001f,
                    "Interrupted enemy movement snaps to committed cell and resumes");
                stage = 4;
                return false;
            }
            if (stage == 4)
            {
                if (controller.SelectedUnit.Team == UnitTeam.Enemy) return false;
                foreach (var enemy in controller.Enemies) enemy.ApplyDamage(enemy.CurrentHealth);
                Require(!controller.TryEndTurn(), "Final death immediately blocks player input");
                stage = 2;
                return false;
            }
            if (stage == 2)
            {
                Require(controller.Outcome == BattleOutcome.Victory && controller.SelectedUnit == null &&
                    controller.CurrentRange == null && !controller.TryWaitAction(), "Victory stops battle");
                var scene = controller;
                BeginPresentation(scene);
                UnityEngine.Object.Destroy(scene.gameObject);
                foreach (var hero in controller.Units) hero.ApplyDamage(hero.CurrentHealth);
                stage = 3;
                return false;
            }
            Require(controller.Outcome == BattleOutcome.Defeat && !controller.TryEndTurn(), "Defeat stops battle");
            UnityEngine.Object.Destroy(root);
            Debug.Log("WP-07 Play Mode checks passed: enemy turns, input locks, damage, disable recovery, victory and defeat.");
            return true;
        }

        private static UnitRuntimeState Spawn(GridModel grid, string id, int q, int r, UnitTeam team)
        {
            Require(UnitRuntimeState.TrySpawn(grid, id, new UnitDefinition(id, id, 3),
                new HexCoordinates(q, r), out var unit, team), "Spawn");
            return unit;
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("WP-07 failed: " + message);
        }
    }
}
