using System;
using System.Collections.Generic;
using GuildTactics.HexGrid;
using GridModel = GuildTactics.HexGrid.HexGrid;

namespace GuildTactics.Generation
{
    public sealed class DungeonMap
    {
        public GridModel Grid { get; }
        public int Seed { get; }
        public DungeonBiome Biome { get; }
        public bool UsedFallback { get; }
        public IReadOnlyList<HexCoordinates> PlayerSpawns { get; }
        public IReadOnlyList<HexCoordinates> EnemyCandidates { get; }
        public HexCoordinates Objective { get; }

        internal DungeonMap(GridModel grid, int seed, bool fallback, HexCoordinates[] spawns,
            List<HexCoordinates> candidates, HexCoordinates objective, DungeonBiome biome = DungeonBiome.Crypt)
        {
            Grid = grid; Seed = seed; UsedFallback = fallback; Biome = biome;
            PlayerSpawns = Array.AsReadOnly(spawns);
            EnemyCandidates = candidates.AsReadOnly(); Objective = objective;
        }
    }

    public static class DungeonGenerator
    {
        public static DungeonMap Generate(string seed, DungeonGenerationConfig config = null) =>
            Generate(GenerationRandom.ParseSeed(seed), config);

        public static DungeonMap Generate(int seed, DungeonGenerationConfig config = null)
        {
            config = config ?? new DungeonGenerationConfig();
            config.Validate();
            var random = new GenerationRandom(seed);
            for (int attempt = 0; attempt < config.MaxAttempts; attempt++)
            {
                var map = Build(seed, config, random, false);
                if (DungeonValidator.IsValid(map, config)) return map;
            }
            var fallback = Build(seed, config, random, true);
            if (!DungeonValidator.IsValid(fallback, config))
                throw new InvalidOperationException("Fallback dungeon failed validation.");
            return fallback;
        }

        private static DungeonMap Build(int seed, DungeonGenerationConfig config, GenerationRandom random, bool fallback)
        {
            var grid = new GridModel();
            foreach (var cell in grid.Cells) cell.Terrain = TerrainType.Blocked;
            var spawns = new[] { new HexCoordinates(1, 1), new HexCoordinates(1, 2),
                new HexCoordinates(2, 1), new HexCoordinates(2, 2) };
            var previous = new HexCoordinates(2, 2);
            CarveRoom(grid, previous, 2);
            if (config.Biome == DungeonBiome.FloodedCellar && !fallback)
            {
                CarveCellar(grid, random);
                CarveRoom(grid, previous, 2); // Keep the four-person entrance dry and connected.
            }
            else if (fallback)
            {
                foreach (var cell in grid.Cells)
                    if (cell.Coordinates.Q > 0 && cell.Coordinates.Q < grid.Width - 1 &&
                        cell.Coordinates.R > 0 && cell.Coordinates.R < grid.Height - 1)
                        cell.Terrain = TerrainType.Ground;
            }
            else
            {
                for (int room = 1; room < config.RoomCount; room++)
                {
                    // The final room guarantees an expedition extends away from the entrance.
                    var center = room == config.RoomCount - 1 ? new HexCoordinates(9, 9) :
                        new HexCoordinates(2 + random.Next(8), 2 + random.Next(8));
                    CarveRoom(grid, center, config.RoomRadius);
                    while (previous != center)
                    {
                        foreach (var neighbor in grid.GetNeighbors(previous))
                            if (neighbor.Coordinates.DistanceTo(center) < previous.DistanceTo(center))
                            { previous = neighbor.Coordinates; break; }
                        grid.GetCell(previous).Terrain = TerrainType.Ground;
                    }
                }
            }
            foreach (var cell in grid.Cells)
            {
                if (cell.Coordinates.DistanceTo(spawns[0]) <= 3) continue;
                if (config.Biome == DungeonBiome.Crypt && cell.Terrain == TerrainType.Blocked && random.Next(100) < config.PitPercent)
                    cell.Terrain = TerrainType.Pit;
                else if (cell.Terrain == TerrainType.Ground && random.Next(100) < config.HighGroundPercent)
                    cell.Terrain = TerrainType.HighGround;
            }
            var distances = DungeonValidator.Distances(grid, spawns[0]);
            var objective = spawns[0];
            foreach (var cell in grid.Cells)
                if (distances.TryGetValue(cell.Coordinates, out int distance) && distance > distances[objective])
                    objective = cell.Coordinates;
            var candidates = new List<HexCoordinates>();
            foreach (var cell in grid.Cells)
            {
                if (!distances.ContainsKey(cell.Coordinates) || cell.Coordinates == objective) continue;
                bool away = true;
                foreach (var spawn in spawns)
                    if (spawn.DistanceTo(cell.Coordinates) < config.EnemyDistanceFromParty) away = false;
                if (away) candidates.Add(cell.Coordinates);
            }
            return new DungeonMap(grid, seed, fallback, spawns, candidates, objective, config.Biome);
        }

        private static void CarveCellar(GridModel grid, GenerationRandom random)
        {
            // Open basins do not occlude ranged fire. Two separated north/south routes and
            // three narrow crosswalks force different movement choices from crypt rooms.
            int middleRow = 4 + random.Next(2);
            int farRow = 8 + random.Next(2);
            int farColumn = 6 + random.Next(3);
            foreach (var cell in grid.Cells)
            {
                int q = cell.Coordinates.Q, r = cell.Coordinates.R;
                if (q < 1 || q >= grid.Width - 1 || r < 1 || r >= grid.Height - 1) continue;
                bool walkway = r == 2 || r == middleRow || r == farRow || q == 2 || q == farColumn;
                cell.Terrain = walkway ? TerrainType.Ground : TerrainType.Pit;
            }
            CarveRoom(grid, new HexCoordinates(farColumn, middleRow), 2);
            CarveRoom(grid, new HexCoordinates(9, farRow), 1 + random.Next(2));
        }

        private static void CarveRoom(GridModel grid, HexCoordinates center, int radius)
        {
            foreach (var cell in grid.Cells)
                if (cell.Coordinates.DistanceTo(center) <= radius) cell.Terrain = TerrainType.Ground;
        }
    }
}
