using System;
using System.Collections.Generic;
using System.Linq;
using GuildTactics.HexGrid;
using UnityEditor;
using UnityEngine;
using GridModel = GuildTactics.HexGrid.HexGrid;

namespace GuildTactics.Editor
{
    public static class HexPathfindingChecks
    {
        [MenuItem("Tools/Guild Tactics/Validate Hex Pathfinding")]
        public static void Run()
        {
            CheckOpenGrid();
            CheckBoundariesAndCosts();
            CheckWeightedMaps();
            Debug.Log("WP-03 algorithm checks passed: open-grid all pairs, 20 weighted maps vs Floyd-Warshall, budgets, barriers, occupancy, deterministic paths, snapshots and overflow.");
        }

        public static void RunBatch() => HexPresentationChecks.RunBatch();

        private static void CheckOpenGrid()
        {
            var grid = new GridModel();
            foreach (var start in grid.Cells)
            {
                var range = HexPathfinder.FindReachable(grid, start.Coordinates, int.MaxValue);
                Require(range.Costs.Count == 144, "Open grid connectivity");
                foreach (var target in grid.Cells)
                {
                    Require(range.Costs[target.Coordinates] == start.Coordinates.DistanceTo(target.Coordinates), "Open grid distance");
                    CheckPath(grid, range, target.Coordinates);
                }
                var limited = HexPathfinder.FindReachable(grid, start.Coordinates, 4);
                foreach (var target in grid.Cells)
                    Require(limited.Costs.ContainsKey(target.Coordinates) ==
                        (start.Coordinates.DistanceTo(target.Coordinates) <= 4), "Open grid budget at edges");
            }
        }

        private static void CheckBoundariesAndCosts()
        {
            var origin = new HexCoordinates(0, 0);
            var target = new HexCoordinates(2, 0);
            var grid = new GridModel(3, 2);
            var expensive = grid.GetCell(new HexCoordinates(1, 0));
            expensive.MovementCost = 8;
            var path = HexPathfinder.FindPath(grid, origin, target);
            Require(path.Count == 4 && !path.Contains(expensive.Coordinates), "Cheaper three-step detour beats expensive two-step path");
            Require(HexPathfinder.FindPath(grid, origin, target, 2).Count == 0, "Budget forbids destination");
            Require(HexPathfinder.FindPath(grid, origin, target, 3).Count == 4, "Exact budget accepts destination");
            Require(grid.Cells.All(cell => !cell.IsOccupied && cell.Terrain == TerrainType.Ground), "Search never mutates grid");
            Throws<ArgumentNullException>(() => HexPathfinder.FindReachable(null, origin, 1));
            Throws<ArgumentOutOfRangeException>(() => HexPathfinder.FindReachable(grid, origin, -1));
            Throws<ArgumentOutOfRangeException>(() => expensive.MovementCost = 0);
            Throws<ArgumentOutOfRangeException>(() => expensive.MovementCost = -1);
            Require(expensive.MovementCost == 8, "Rejected costs preserve previous value");
            var outside = new HexCoordinates(-1, 0);
            Require(HexPathfinder.FindReachable(grid, outside, 10).Costs.Count == 0, "Invalid origin");
            Require(HexPathfinder.FindPath(grid, origin, outside).Count == 0, "Invalid target");
            var zero = HexPathfinder.FindReachable(grid, origin, 0);
            Require(zero.Costs.Count == 1 && zero.Costs[origin] == 0 && zero.GetPathTo(origin).SequenceEqual(new[] { origin }), "Zero budget and same-cell path");
            grid.TryOccupy(origin, "mover");
            Require(HexPathfinder.FindReachable(grid, origin, 3).Costs.ContainsKey(target), "Occupied origin can leave");
            grid.TryOccupy(target, "other");
            Require(HexPathfinder.FindPath(grid, origin, target).Count == 0, "Occupied destination rejected");
            Require(grid.GetCell(origin).OccupantId == "mover" && grid.GetCell(target).OccupantId == "other", "Occupancy preserved");
            foreach (TerrainType terrain in new[] { TerrainType.Blocked, TerrainType.Pit })
            {
                grid.GetCell(origin).Terrain = terrain;
                Require(HexPathfinder.FindReachable(grid, origin, 10).Costs.Count == 0, "Impassable origin");
                Require(HexPathfinder.FindPath(grid, origin, origin).Count == 0, "Impassable same-cell path");
            }
            var corridor = new GridModel(3, 1);
            var middle = corridor.GetCell(new HexCoordinates(1, 0));
            foreach (TerrainType terrain in new[] { TerrainType.Blocked, TerrainType.Pit })
            {
                middle.Terrain = terrain;
                Require(HexPathfinder.FindReachable(corridor, origin, 100).Costs.Count == 1, "Barrier stops search");
            }
            middle.Terrain = TerrainType.HighGround;
            Require(HexPathfinder.FindPath(corridor, origin, target).Count == 3, "High ground has no extra rule");
            corridor.TryOccupy(middle.Coordinates, "blocker");
            Require(HexPathfinder.FindPath(corridor, origin, target).Count == 0, "Cannot cross occupied cell");
            corridor.TryVacate(middle.Coordinates, "blocker");
            middle.MovementCost = int.MaxValue;
            var huge = HexPathfinder.FindReachable(corridor, origin, int.MaxValue);
            Require(huge.Costs[middle.Coordinates] == int.MaxValue && !huge.Costs.ContainsKey(target), "No cost overflow");
            middle.MovementCost = 1;
            Require(huge.Costs[middle.Coordinates] == int.MaxValue, "Result is a snapshot");
            Require(HexPathfinder.FindReachable(corridor, origin, 2).Costs[target] == 2, "Recalculation sees changed cost");
            Throws<NotSupportedException>(() => ((IDictionary<HexCoordinates, int>)huge.Costs).Add(target, 0));
            Require(HexPathfinder.FindReachable(new GridModel(1, 1), origin, 100).Costs.Count == 1, "Single-cell map");
        }

        private static void CheckWeightedMaps()
        {
            const long infinity = long.MaxValue / 4;
            for (int seed = 0; seed < 20; seed++)
            {
                var random = new System.Random(seed);
                var grid = new GridModel(6, 6);
                foreach (var cell in grid.Cells)
                {
                    cell.MovementCost = random.Next(1, 6);
                    int kind = random.Next(10);
                    if (kind == 0) cell.Terrain = TerrainType.Blocked;
                    else if (kind == 1) cell.Terrain = TerrainType.Pit;
                    else if (kind == 2) cell.Terrain = TerrainType.HighGround;
                    if (random.Next(8) == 0) grid.TryOccupy(cell.Coordinates, "unit" + cell.Coordinates);
                }
                int count = grid.Cells.Count;
                var distances = new long[count, count];
                // Independent all-pairs oracle; adjacency derives from axial distance, not GetNeighbors.
                for (int i = 0; i < count; i++)
                    for (int j = 0; j < count; j++)
                    {
                        var from = grid.Cells[i];
                        var to = grid.Cells[j];
                        bool fromOpen = from.Terrain != TerrainType.Blocked && from.Terrain != TerrainType.Pit;
                        bool toOpen = to.Terrain != TerrainType.Blocked && to.Terrain != TerrainType.Pit && !to.IsOccupied;
                        distances[i, j] = i == j && fromOpen ? 0 :
                            (fromOpen && toOpen && from.Coordinates.DistanceTo(to.Coordinates) == 1 ? to.MovementCost : infinity);
                    }
                for (int k = 0; k < count; k++)
                    for (int i = 0; i < count; i++)
                        for (int j = 0; j < count; j++)
                            distances[i, j] = Math.Min(distances[i, j], distances[i, k] + distances[k, j]);

                for (int i = 0; i < count; i++)
                    foreach (int budget in new[] { 0, 3, 8, int.MaxValue })
                    {
                        var range = HexPathfinder.FindReachable(grid, grid.Cells[i].Coordinates, budget);
                        var repeated = HexPathfinder.FindReachable(grid, grid.Cells[i].Coordinates, budget);
                        for (int j = 0; j < count; j++)
                        {
                            var target = grid.Cells[j].Coordinates;
                            bool reachable = range.Costs.TryGetValue(target, out int cost);
                            Require(reachable == (distances[i, j] <= budget), "Reachability vs weighted oracle");
                            if (reachable) Require(cost == distances[i, j], "Minimum cost vs weighted oracle");
                            Require(range.GetPathTo(target).SequenceEqual(repeated.GetPathTo(target)), "Deterministic route");
                            CheckPath(grid, range, target);
                        }
                    }
            }
        }

        private static void CheckPath(GridModel grid, HexMovementRange range, HexCoordinates target)
        {
            var path = range.GetPathTo(target);
            if (!range.Costs.TryGetValue(target, out int expected))
            {
                Require(path.Count == 0, "Unreachable has empty path");
                return;
            }
            Require(path[0] == range.Origin && path[path.Count - 1] == target, "Path endpoints");
            Require(path.Distinct().Count() == path.Count, "No path cycles");
            long cost = 0;
            for (int i = 1; i < path.Count; i++)
            {
                var cell = grid.GetCell(path[i]);
                Require(path[i - 1].DistanceTo(path[i]) == 1, "Adjacent steps");
                Require(!cell.IsOccupied && cell.Terrain != TerrainType.Blocked && cell.Terrain != TerrainType.Pit, "Legal step");
                cost += cell.MovementCost;
            }
            Require(cost == expected && cost <= range.Budget, "Path cost matches range and budget");
        }

        internal static void ValidatePresentation(GridModel grid, HexGridView view, HexGridInteraction interaction, Camera camera)
        {
            var preview = view.GetComponent<HexMovementPreview>();
            Require(preview != null && preview.MovementBudget == HexMovementPreview.DefaultBudget, "Scene preview and budget");
            var layout = new HexLayout();
            var origin = new HexCoordinates(5, 5);
            interaction.ProcessPointer(camera.WorldToScreenPoint(layout.ToWorld(origin)), true);
            interaction.ProcessPointer(new Vector2(-1, -1), false);
            preview.Refresh();
            var renderers = view.GetComponentsInChildren<SpriteRenderer>();
            Color ground = renderers[0].color;
            Color rangeColor = renderers[5 * grid.Width + 6].color;
            Color selectedColor = renderers[5 * grid.Width + 5].color;
            Require(rangeColor != ground && rangeColor != selectedColor, "Reachable, ground and selection colors distinct");
            foreach (var cell in grid.Cells)
            {
                if (cell.Coordinates == origin) continue;
                bool expected = origin.DistanceTo(cell.Coordinates) <= preview.MovementBudget;
                Require((renderers[cell.Coordinates.R * grid.Width + cell.Coordinates.Q].color == rangeColor) == expected,
                    "Exactly reachable destinations highlighted");
            }
            var neighbor = new HexCoordinates(6, 5);
            interaction.ProcessPointer(camera.WorldToScreenPoint(layout.ToWorld(neighbor)), false);
            Require(renderers[5 * grid.Width + 6].color != rangeColor, "Hover overrides range");
            interaction.ProcessPointer(new Vector2(-1, -1), false);
            Require(renderers[5 * grid.Width + 6].color == rangeColor, "Range restored after hover leaves");
            var changed = grid.GetCell(neighbor);
            try
            {
                changed.Terrain = TerrainType.Blocked;
                preview.Refresh();
                Require(!preview.CurrentRange.Costs.ContainsKey(neighbor) && renderers[5 * grid.Width + 6].color == ground, "Blocked excluded from highlight");
                changed.Terrain = TerrainType.Ground;
                grid.TryOccupy(neighbor, "preview-check");
                preview.Refresh();
                Require(!preview.CurrentRange.Costs.ContainsKey(neighbor), "Occupied excluded from highlight");
                grid.TryVacate(neighbor, "preview-check");
                changed.MovementCost = 5;
                preview.Refresh();
                Require(!preview.CurrentRange.Costs.ContainsKey(neighbor), "Cost exceeds preview budget");
                preview.SetMovementBudget(0);
                Require(preview.CurrentRange.Costs.Count == 1, "Zero-budget preview");
                preview.enabled = false;
                Require(preview.CurrentRange == null && renderers[5 * grid.Width + 6].color == ground, "Disable clears range");
            }
            finally
            {
                changed.Terrain = TerrainType.Ground;
                changed.MovementCost = 1;
                grid.TryVacate(neighbor, "preview-check");
                preview.SetMovementBudget(HexMovementPreview.DefaultBudget);
                preview.enabled = true;
            }
            interaction.ProcessPointer(camera.WorldToScreenPoint(layout.ToWorld(new HexCoordinates(0, 0))), true);
            Require(preview.CurrentRange.Origin == new HexCoordinates(0, 0) && renderers[5 * grid.Width + 6].color == ground, "New selection clears old range");
            interaction.ProcessPointer(camera.WorldToScreenPoint(layout.ToWorld(origin)), true);
            interaction.ProcessPointer(camera.WorldToScreenPoint(layout.ToWorld(neighbor)), false);
            Debug.Log("WP-03 Play Mode checks passed: selection-driven range, budgets, obstacles, occupancy, hover priority, refresh and disable/re-enable.");
        }

        private static void Require(bool condition, string description)
        {
            if (!condition) throw new InvalidOperationException("WP-03 failed: " + description);
        }

        private static void Throws<T>(Action action) where T : Exception
        {
            try { action(); }
            catch (T) { return; }
            throw new InvalidOperationException("Expected " + typeof(T).Name);
        }
    }
}
