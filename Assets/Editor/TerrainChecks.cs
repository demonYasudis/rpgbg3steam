using System;
using System.Linq;
using GuildTactics.Abilities;
using GuildTactics.Combat;
using GuildTactics.HexGrid;
using GuildTactics.Units;
using UnityEditor;
using UnityEngine;
using GridModel = GuildTactics.HexGrid.HexGrid;

namespace GuildTactics.Editor
{
    public static class TerrainChecks
    {
        private sealed class NoDice : IDice
        {
            public int Roll(int sides) => throw new InvalidOperationException("Push should not roll dice.");
        }

        [MenuItem("Tools/Guild Tactics/Validate Terrain")]
        public static void Run()
        {
            for (int direction = 0; direction < 6; direction++)
                foreach (TerrainType terrain in Enum.GetValues(typeof(TerrainType)))
                {
                    var grid = new GridModel(7, 7);
                    var origin = new HexCoordinates(3, 3);
                    var target = origin.GetNeighbor(direction);
                    var landing = target.GetNeighbor(direction);
                    UnitRuntimeState.TrySpawn(grid, "warrior", HeroDefinitions.Defaults[0], origin, out var actor);
                    UnitRuntimeState.TrySpawn(grid, "enemy", new UnitDefinition("enemy", "Enemy", 2), target,
                        out var enemy, UnitTeam.Enemy);
                    grid.GetCell(landing).Terrain = terrain;
                    var turns = new TurnManager(grid, new[] { actor, enemy });
                    turns.TryStartNextTurn();
                    var system = new AbilitySystem(grid, turns, new NoDice(), turns.Order);
                    var push = actor.Definition.Abilities[1];
                    bool succeeds = system.TryUse(actor, push, target, out var result, out _);
                    Require(succeeds == (terrain != TerrainType.Blocked), "Push terrain rule in all six directions");
                    if (!succeeds)
                    {
                        Require(enemy.Position == target && turns.ActionAvailable, "Blocked push spends nothing");
                        continue;
                    }
                    Require(enemy.Position == landing && !grid.GetCell(target).IsOccupied, "Exactly one hex push");
                    Require(!system.TryUse(actor, push, target, out _, out _), "Push cannot resolve twice");
                    Require(result.FellIntoPit == (terrain == TerrainType.Pit), "Fall feedback");
                    Require(enemy.IsAlive == (terrain != TerrainType.Pit) &&
                        grid.GetCell(landing).IsOccupied == enemy.IsAlive, "Fall kills and releases occupancy");
                    turns.TryCompleteAction(actor);
                    turns.TryEndTurn(actor);
                    turns.TryStartNextTurn();
                    if (terrain == TerrainType.Pit)
                        Require(turns.ActiveUnit == actor && BattleRules.Evaluate(turns.Order) == BattleOutcome.Victory,
                            "Pit victim skipped; final fall wins battle");
                }

            foreach (var terrain in new[] { TerrainType.Blocked, TerrainType.Pit })
            {
                var grid = new GridModel(3, 1);
                var middle = new HexCoordinates(1, 0);
                grid.GetCell(middle).Terrain = terrain;
                Require(!UnitRuntimeState.TrySpawn(grid, "invalid", HeroDefinitions.Defaults[0], middle, out _),
                    "Cannot spawn on wall or pit");
                UnitRuntimeState.TrySpawn(grid, "walker", HeroDefinitions.Defaults[0], new HexCoordinates(0, 0), out var unit);
                Require(HexPathfinder.FindPath(grid, unit.Position, new HexCoordinates(2, 0)).Count == 0 &&
                    !unit.TryRelocate(grid, middle) && !unit.TryMoveAlong(grid, new[] { unit.Position, middle }),
                    "Walking, pathfinding and blink reject wall/pit");
            }
            var highGrid = new GridModel(3, 1);
            highGrid.GetCell(new HexCoordinates(1, 0)).Terrain = TerrainType.HighGround;
            Require(HexPathfinder.FindPath(highGrid, new HexCoordinates(0, 0), new HexCoordinates(2, 0)).Count == 3,
                "High ground traversable without extra cost");
            Debug.Log("WP-09 model checks passed: all terrain and push directions, fatal pits, occupancy, turn skipping and traversal.");
        }

        public static void RunBatch() => HexPresentationChecks.RunBatch();

        internal static void ValidatePresentation(GridModel sceneGrid)
        {
            foreach (TerrainType terrain in Enum.GetValues(typeof(TerrainType)))
                Require(sceneGrid.Cells.Any(cell => cell.Terrain == terrain), "Prototype demonstrates " + terrain);
            var grid = new GridModel(4, 1);
            for (int i = 0; i < 4; i++) grid.GetCell(new HexCoordinates(i, 0)).Terrain = (TerrainType)i;
            var root = new GameObject("WP09 Terrain Visual Checks");
            try
            {
                var view = root.AddComponent<HexGridView>();
                view.Initialize(grid, new HexLayout());
                var tiles = view.GetComponentsInChildren<SpriteRenderer>();
                Require(tiles.Select(tile => tile.color).Distinct().Count() == 4, "Four distinct terrain colors");
                var before = tiles.Select(tile => tile.color).ToArray();
                view.SetHighlights(new HexCoordinates(1, 0), new HexCoordinates(2, 0));
                view.SetHighlights(null, null);
                Require(tiles.Select(tile => tile.color).SequenceEqual(before), "Clearing highlight restores terrain");
            }
            finally { UnityEngine.Object.Destroy(root); }
        }

        private static void Require(bool value, string message)
        {
            if (!value) throw new InvalidOperationException("WP-09 failed: " + message);
        }
    }
}
