using System;
using System.Collections.Generic;

namespace GuildTactics.HexGrid
{
    /// <summary>Deterministic Dijkstra search, independent of Unity and presentation.</summary>
    public static class HexPathfinder
    {
        public static HexMovementRange FindReachable(HexGrid grid, HexCoordinates origin, int budget)
        {
            if (grid == null) throw new ArgumentNullException(nameof(grid));
            if (budget < 0) throw new ArgumentOutOfRangeException(nameof(budget));
            var costs = new Dictionary<HexCoordinates, int>();
            var previous = new Dictionary<HexCoordinates, HexCoordinates>();
            var result = new HexMovementRange(origin, budget, costs, previous);
            if (!grid.TryGetCell(origin, out var start) || !TerrainRules.CanWalk(start.Terrain)) return result;

            // Occupancy is ignored only for the origin: it is the moving unit's current cell.
            costs.Add(origin, 0);
            var frontier = new List<HexCoordinates> { origin };
            var visited = new HashSet<HexCoordinates>();
            while (frontier.Count > 0)
            {
                // O(V²) is deliberately sufficient for 144 cells. No heap/package needed.
                // Equal costs keep first-discovered order, using the grid's fixed neighbor order.
                int best = 0;
                for (int i = 1; i < frontier.Count; i++)
                    if (costs[frontier[i]] < costs[frontier[best]]) best = i;
                var current = frontier[best];
                frontier.RemoveAt(best);
                visited.Add(current);
                int currentCost = costs[current];

                foreach (var neighbor in grid.GetNeighbors(current))
                {
                    var next = neighbor.Coordinates;
                    if (visited.Contains(next) || neighbor.IsOccupied || !TerrainRules.CanWalk(neighbor.Terrain)) continue;
                    // Subtract before adding so even int.MaxValue budgets/costs cannot overflow.
                    if (neighbor.MovementCost > budget - currentCost) continue;
                    int candidate = currentCost + neighbor.MovementCost;
                    if (costs.TryGetValue(next, out int knownCost))
                    {
                        if (candidate >= knownCost) continue;
                    }
                    else frontier.Add(next);
                    costs[next] = candidate;
                    previous[next] = current;
                }
            }
            return result;
        }

        public static IReadOnlyList<HexCoordinates> FindPath(HexGrid grid, HexCoordinates origin,
            HexCoordinates destination, int budget = int.MaxValue) =>
            FindReachable(grid, origin, budget).GetPathTo(destination);

    }
}
