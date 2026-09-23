using System.Collections.Generic;
using GuildTactics.HexGrid;
using GridModel = GuildTactics.HexGrid.HexGrid;

namespace GuildTactics.Generation
{
    public static class DungeonValidator
    {
        public static Dictionary<HexCoordinates, int> Distances(GridModel grid, HexCoordinates start)
        {
            var distances = new Dictionary<HexCoordinates, int>();
            if (!grid.TryGetCell(start, out var first) || !TerrainRules.CanWalk(first.Terrain)) return distances;
            var queue = new Queue<HexCoordinates>();
            queue.Enqueue(start); distances.Add(start, 0);
            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                foreach (var cell in grid.GetNeighbors(current))
                    if (TerrainRules.CanWalk(cell.Terrain) && !distances.ContainsKey(cell.Coordinates))
                    { distances.Add(cell.Coordinates, distances[current] + 1); queue.Enqueue(cell.Coordinates); }
            }
            return distances;
        }

        public static bool IsValid(DungeonMap map, DungeonGenerationConfig config)
        {
            if (map == null || map.PlayerSpawns.Count != 4) return false;
            var reachable = Distances(map.Grid, map.PlayerSpawns[0]);
            if (reachable.Count < config.MinimumWalkableCells || !reachable.ContainsKey(map.Objective) ||
                map.EnemyCandidates.Count < DungeonGenerationConfig.MinimumEnemyCandidates) return false;
            var starts = new HashSet<HexCoordinates>(map.PlayerSpawns);
            if (starts.Count != 4 || starts.Contains(map.Objective)) return false;
            foreach (var spawn in starts)
            {
                if (!reachable.ContainsKey(spawn)) return false;
                bool exit = false;
                foreach (var neighbor in map.Grid.GetNeighbors(spawn))
                    if (TerrainRules.CanWalk(neighbor.Terrain) && !starts.Contains(neighbor.Coordinates)) exit = true;
                if (!exit) return false;
            }
            foreach (var cell in map.Grid.Cells)
                if (TerrainRules.CanWalk(cell.Terrain) && !reachable.ContainsKey(cell.Coordinates)) return false;
            var unique = new HashSet<HexCoordinates>();
            foreach (var candidate in map.EnemyCandidates)
            {
                if (!unique.Add(candidate) || !reachable.ContainsKey(candidate) || candidate == map.Objective) return false;
                foreach (var spawn in starts)
                    if (spawn.DistanceTo(candidate) < config.EnemyDistanceFromParty) return false;
            }
            return true;
        }
    }
}
