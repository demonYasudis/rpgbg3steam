using System;
using System.Linq;
using GuildTactics.Abilities;
using GuildTactics.Combat;
using GuildTactics.HexGrid;
using GuildTactics.Units;
using GuildTactics.Visibility;
using UnityEditor;
using UnityEngine;
using GridModel = GuildTactics.HexGrid.HexGrid;

namespace GuildTactics.Editor
{
    public static class VisibilityChecks
    {
        private sealed class MaximumDice : IDice { public int Roll(int sides) => sides; }

        [MenuItem("Tools/Guild Tactics/Validate Visibility")]
        public static void Run()
        {
            CheckVisionAndMemory();
            CheckTargetingAndMovement();
            CheckEnemyVision();
            CheckForcedMovementAndDeath();
            Debug.Log("WP-10 model checks passed: party union, boundaries, memory, movement/death/spawn refresh, hidden targeting and enemy vision.");
        }

        public static void RunBatch() => HexPresentationChecks.RunBatch();

        private static void CheckVisionAndMemory()
        {
            var grid = new GridModel(10, 8);
            var a = Spawn(grid, "a", new HexCoordinates(0, 0), 2);
            var b = Spawn(grid, "b", new HexCoordinates(5, 3), 1);
            var enemy = Spawn(grid, "enemy", new HexCoordinates(9, 7), 10, UnitTeam.Enemy);
            using var fog = new FogOfWarSystem(grid, new[] { a, b, enemy, a });
            foreach (var cell in grid.Cells)
                Require(fog.IsVisible(cell.Coordinates) ==
                    (a.Position.DistanceTo(cell.Coordinates) <= 2 || b.Position.DistanceTo(cell.Coordinates) <= 1),
                    "Union, inclusive radius and clipped borders");
            Require(fog.GetState(enemy.Position) == CellVisibility.Unknown &&
                !fog.TryGetRememberedTerrain(enemy.Position, out _) &&
                fog.GetState(new HexCoordinates(-1, 0)) == CellVisibility.Unknown, "Enemy reveals nothing; safe invalid lookup");
            var rememberedCell = new HexCoordinates(0, 1);
            grid.GetCell(rememberedCell).Terrain = TerrainType.HighGround;
            fog.Refresh();
            Require(a.TryRelocate(grid, new HexCoordinates(8, 0)), "Move observer");
            Require(fog.GetState(rememberedCell) == CellVisibility.Explored, "Movement automatically preserves exploration");
            grid.GetCell(rememberedCell).Terrain = TerrainType.Pit;
            fog.Refresh();
            Require(fog.TryGetRememberedTerrain(rememberedCell, out var old) && old == TerrainType.HighGround,
                "Explored terrain is remembered, not live terrain");
            Require(a.TryRelocate(grid, new HexCoordinates(0, 0)), "Return observer");
            Require(fog.TryGetRememberedTerrain(rememberedCell, out var updated) && updated == TerrainType.Pit,
                "Revisiting updates remembered terrain");
            b.ApplyDamage(b.CurrentHealth);
            Require(fog.GetState(b.Position) == CellVisibility.Explored, "Dead observer loses vision immediately");
            var newcomer = Spawn(grid, "new", new HexCoordinates(8, 6), 0);
            fog.Register(newcomer);
            fog.Register(newcomer);
            Require(fog.IsVisible(newcomer.Position) && fog.GetState(enemy.Position) == CellVisibility.Unknown,
                "Spawn refresh, idempotent registration and zero range");
            a.ApplyDamage(a.CurrentHealth);
            newcomer.ApplyDamage(newcomer.CurrentHealth);
            Require(grid.Cells.All(cell => !fog.IsVisible(cell.Coordinates)), "No living heroes means no current vision");
            var snapshot = grid.Cells.Select(cell => fog.GetState(cell.Coordinates)).ToArray();
            fog.Refresh();
            Require(snapshot.SequenceEqual(grid.Cells.Select(cell => fog.GetState(cell.Coordinates))), "Deterministic refresh");
            using var empty = new FogOfWarSystem(grid, Array.Empty<UnitRuntimeState>());
            Require(grid.Cells.All(cell => empty.GetState(cell.Coordinates) == CellVisibility.Unknown), "New fog starts unknown");
            var foreign = new GridModel();
            Throws<ArgumentException>(() => new FogOfWarSystem(foreign, new[] { a }));
            Throws<ArgumentException>(() => new TurnManager(foreign, new[] { enemy }, fog));
            Throws<ArgumentOutOfRangeException>(() => new UnitDefinition("bad", "Bad", 1, visionRange: -1));
        }

        private static void CheckTargetingAndMovement()
        {
            var grid = new GridModel(9, 5);
            var abilities = HeroDefinitions.Defaults[3].Abilities.Concat(HeroDefinitions.Defaults[2].Abilities).ToArray();
            UnitRuntimeState.TrySpawn(grid, "caster", new UnitDefinition("caster", "Caster", 4, 10,
                attackRange: 6, abilities: abilities, visionRange: 1), new HexCoordinates(1, 1), out var actor);
            var visible = Spawn(grid, "visible", new HexCoordinates(2, 1), 1, UnitTeam.Enemy);
            var hidden = Spawn(grid, "hidden", new HexCoordinates(3, 1), 1, UnitTeam.Enemy);
            using var fog = new FogOfWarSystem(grid, new[] { actor });
            var turns = new TurnManager(grid, new[] { actor, visible, hidden }, fog);
            turns.TryStartNextTurn();
            var combat = new CombatSystem(grid, turns, new MaximumDice());
            var system = new AbilitySystem(grid, turns, new MaximumDice(), turns.Order);
            Require(combat.CanAttack(actor, visible) && !combat.TryAttack(actor, hidden, out _) && turns.ActionAvailable,
                "Hidden enemy is not a legal basic attack");
            Require(!system.TryUse(actor, abilities[0], hidden.Position, out _, out _) &&
                !system.TryUse(actor, abilities[1], new HexCoordinates(1, 3), out _, out _) &&
                !system.TryUse(actor, abilities[3], new HexCoordinates(1, 3), out _, out _) && turns.ActionAvailable,
                "Unseen burst, blink and trap spend nothing");
            Require(system.TryUse(actor, abilities[0], visible.Position, out var burst, out _) &&
                burst.Attacks.Count == 1 && hidden.CurrentHealth == hidden.Definition.MaxHealth,
                "Burst only affects visible enemies; no hidden result leak");
            turns.TryCompleteAction(actor);
            var unseen = new HexCoordinates(1, 3);
            Require(!turns.GetMovementRange(actor).Costs.ContainsKey(unseen) &&
                !turns.TryBeginMovement(actor, unseen, out _) && turns.RemainingMovement == 4,
                "Range and commit reject unseen paths without resource loss");
            Require(turns.TryBeginMovement(actor, new HexCoordinates(1, 2), out _) && fog.IsVisible(unseen),
                "Legal movement refreshes party vision immediately");
            turns.TryCompleteMovement(actor);
            Require(turns.TryBeginMovement(actor, unseen, out _), "Newly revealed cells allow subsequent movement");
            turns.TryCompleteMovement(actor);
            Require(turns.TryEndTurn(actor), "Vision refresh never strands turn");
        }

        private static void CheckEnemyVision()
        {
            var grid = new GridModel(9, 3);
            var hero = Spawn(grid, "hero", new HexCoordinates(0, 1), 1);
            var enemy = Spawn(grid, "enemy", new HexCoordinates(7, 1), 2, UnitTeam.Enemy);
            using var fog = new FogOfWarSystem(grid, new[] { hero });
            var turns = new TurnManager(grid, new[] { enemy, hero }, fog);
            turns.TryStartNextTurn();
            Require(MeleeBrain.ChooseDestination(grid, enemy, new[] { hero }, 3, turns) == enemy.Position,
                "AI does not pursue unseen hero");
            Require(hero.TryRelocate(grid, new HexCoordinates(5, 1)), "Hero enters enemy vision");
            Require(MeleeBrain.ChooseDestination(grid, enemy, new[] { hero }, 3, turns) != enemy.Position,
                "AI approaches hero in individual vision");
            Require(turns.TryEndTurn(enemy), "Unseen enemy can end turn");
        }

        private static void CheckForcedMovementAndDeath()
        {
            var grid = new GridModel(7, 3);
            var hero = Spawn(grid, "hero", new HexCoordinates(1, 1), 1);
            var enemy = Spawn(grid, "enemy", new HexCoordinates(6, 1), 1, UnitTeam.Enemy);
            using var fog = new FogOfWarSystem(grid, new[] { hero });
            Require(hero.TryPushTo(grid, new HexCoordinates(2, 1)) && fog.IsVisible(new HexCoordinates(3, 1)) &&
                fog.GetState(new HexCoordinates(0, 1)) == CellVisibility.Explored, "Forced movement refreshes vision");
            var turns = new TurnManager(grid, new[] { hero, enemy }, fog);
            turns.TryStartNextTurn();
            turns.Traps.Place(enemy, new HexCoordinates(3, 1), hero.CurrentHealth);
            Require(turns.TryBeginMovement(hero, new HexCoordinates(3, 1), out _) && !hero.IsAlive &&
                !grid.Cells.Any(cell => fog.IsVisible(cell.Coordinates)) &&
                turns.TryCompleteMovement(hero) && turns.TryEndTurn(hero) && turns.TryStartNextTurn(),
                "Lethal movement trap removes vision and still permits turn advance");
            var recruit = Spawn(grid, "recruit", new HexCoordinates(0, 1), 1);
            fog.Register(recruit);
            grid.GetCell(new HexCoordinates(1, 1)).Terrain = TerrainType.Pit;
            Require(recruit.TryPushTo(grid, new HexCoordinates(1, 1)) && !recruit.IsAlive &&
                !grid.Cells.Any(cell => fog.IsVisible(cell.Coordinates)), "Fatal push removes last observer immediately");
        }

        private static PlayerUnitController controller;
        private static HexGridView gridView;
        private static UnitRuntimeState guard;
        private static int stage;

        internal static void ValidateInitial(PlayerUnitController scene)
        {
            Require(scene.Visibility != null && scene.Visibility.Grid.Cells.Any(cell =>
                scene.Visibility.GetState(cell.Coordinates) == CellVisibility.Unknown), "Saved scene starts with fog");
            Require(scene.Enemies.Any(unit => !scene.IsUnitVisible(unit)) && scene.Enemies.Any(scene.IsUnitVisible),
                "Near enemy visible; distant enemies hidden");
        }

        internal static void BeginPresentation(PlayerUnitController scene)
        {
            var grid = new GridModel();
            var layout = new HexLayout();
            var root = new GameObject("WP10 Vision Checks");
            gridView = root.AddComponent<HexGridView>();
            gridView.Initialize(grid, layout);
            var input = root.AddComponent<HexGridInteraction>();
            input.Initialize(grid, layout, gridView, scene.GetComponent<HexGridInteraction>().GridCamera);
            controller = root.AddComponent<PlayerUnitController>();
            controller.Initialize(grid, layout, gridView, input, 0.02f, new MaximumDice(), enableFog: true);
            guard = controller.Enemies[1];
            Require(guard.TryRelocate(grid, new HexCoordinates(7, 2)), "Guard placed beyond initial vision");
            controller.GetComponentsInChildren<UnitView>(true).Single(view => view.State == guard)
                .SnapTo(layout.ToWorld(guard.Position));
            controller.CancelTargeting();
            Require(!controller.IsUnitVisible(guard), "Guard initially hidden");
            Require(controller.TryEndTurn(), "Pass Rogue to Ranger");
            stage = 0;
        }

        internal static bool PollPresentation()
        {
            if (stage == 0)
            {
                if (controller.Turns.State == TurnState.TurnComplete) return false;
                Require(controller.SelectedUnit.Definition.Id == "ranger" &&
                    controller.TryMoveSelected(new HexCoordinates(4, 1)) && !controller.TryEndTurn(),
                    "Animated exploration retains input locks");
                stage = 1;
                return false;
            }
            if (controller.IsMoving) return false;
            var guardView = controller.GetComponentsInChildren<UnitView>(true).Single(view => view.State == guard);
            if (stage == 1)
            {
                Require(controller.IsUnitVisible(guard) && guardView.gameObject.activeSelf &&
                    controller.Visibility.IsVisible(guard.Position), "Exploration reveals enemy sprite and HP object");
                Require(controller.TryMoveSelected(new HexCoordinates(2, 1)), "Retreat with remaining movement");
                stage = 2;
                return false;
            }
            Require(controller.Visibility.GetState(guard.Position) == CellVisibility.Explored &&
                !controller.IsUnitVisible(guard) && !guardView.gameObject.activeSelf && !controller.CanAttack(guard),
                "Leaving hides enemy but preserves exploration");
            var tiles = gridView.GetComponentsInChildren<SpriteRenderer>();
            var unknown = new HexCoordinates(11, 11);
            var unknownTile = tiles.Single(tile => tile.name == "Hex " + unknown);
            var unknownColor = unknownTile.color;
            gridView.SetTargetCells(new[] { unknown, guard.Position });
            gridView.SetTrapCells(new[] { unknown, guard.Position });
            gridView.SetHighlights(unknown, unknown);
            Require(unknownTile.color == unknownColor, "Unknown terrain resists overlays");
            gridView.ToggleVisibilityDebug();
            Require(!guardView.gameObject.activeSelf && !controller.CanAttack(guard), "Debug colors do not reveal/enable enemies");
            gridView.ToggleVisibilityDebug();
            Require(unknownTile.color == unknownColor, "Debug toggle restores fog colors");
            controller.enabled = false;
            controller.enabled = true;
            Require(controller.Turns.State == TurnState.SelectingAction && controller.TryEndTurn(),
                "Fog survives disable/enable without blocking turn");
            UnityEngine.Object.Destroy(controller.gameObject);
            Debug.Log("WP-10 Play Mode checks passed: reveal/retreat, hidden enemy views, fog overlays, debug and movement locks.");
            return true;
        }

        private static UnitRuntimeState Spawn(GridModel grid, string id, HexCoordinates position, int vision,
            UnitTeam team = UnitTeam.Player)
        {
            Require(UnitRuntimeState.TrySpawn(grid, id, new UnitDefinition(id, id, 4, visionRange: vision),
                position, out var unit, team), "Spawn " + id);
            return unit;
        }

        private static void Require(bool value, string message)
        {
            if (!value) throw new InvalidOperationException("WP-10 failed: " + message);
        }

        private static void Throws<T>(Action action) where T : Exception
        {
            try { action(); }
            catch (T) { return; }
            throw new InvalidOperationException("Expected " + typeof(T).Name);
        }
    }
}
