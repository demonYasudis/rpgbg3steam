using System;
using System.Collections.Generic;
using System.Linq;
using GuildTactics.HexGrid;
using UnityEditor;
using UnityEngine;
using GridModel = GuildTactics.HexGrid.HexGrid;

namespace GuildTactics.Editor
{
    /// <summary>Dependency-free model checks; callable from the editor or batch mode.</summary>
    public static class HexGridChecks
    {
        [MenuItem("Tools/Guild Tactics/Validate Hex Grid")]
        public static void Run()
        {
            var origin = new HexCoordinates(0, 0);
            var expected = new[] { new HexCoordinates(1, 0), new HexCoordinates(1, -1),
                new HexCoordinates(0, -1), new HexCoordinates(-1, 0),
                new HexCoordinates(-1, 1), new HexCoordinates(0, 1) };
            for (int d = 0; d < 6; d++)
            {
                Require(origin.GetNeighbor(d) == expected[d], "Direction order");
                Require(expected[d].GetNeighbor((d + 3) % 6) == origin, "Opposite direction");
                Require(origin.DistanceTo(expected[d]) == 1, "Adjacent distance");
            }
            Require(origin.DistanceTo(origin) == 0, "Self distance");
            Require(new HexCoordinates(-3, 2).DistanceTo(new HexCoordinates(4, -5)) == 7, "Negative coordinates");
            Require(new HexCoordinates(int.MinValue, int.MinValue).DistanceTo(
                new HexCoordinates(int.MaxValue, int.MaxValue)) == 8589934590L, "Distance overflow safety");
            Require(new HashSet<HexCoordinates> { origin, new HexCoordinates(0, 0) }.Count == 1, "Value equality/hash");
            Throws<ArgumentOutOfRangeException>(() => origin.GetNeighbor(-1));
            Throws<ArgumentOutOfRangeException>(() => origin.GetNeighbor(6));
            Throws<OverflowException>(() => new HexCoordinates(int.MaxValue, 0).GetNeighbor(0));
            Throws<ArgumentOutOfRangeException>(() => new GridModel(0, 12));
            Throws<ArgumentOutOfRangeException>(() => new GridModel(12, -1));
            Throws<OverflowException>(() => new GridModel(int.MaxValue, 2));

            var grid = new GridModel();
            Require(grid.Width == 12 && grid.Height == 12 && grid.Cells.Count == 144, "Default dimensions");
            Require(new GridModel(3, 5).Cells.Count == 15, "Custom dimensions");
            Require(!new GridModel(1, 1).GetNeighbors(origin).Any(), "Single cell boundary");
            Require(grid.GetNeighbors(origin).Count() == 2, "Corner boundary");
            Require(grid.GetNeighbors(new HexCoordinates(0, 5)).Count() == 4, "Edge boundary");
            Require(grid.GetNeighbors(new HexCoordinates(5, 5)).Count() == 6, "Interior neighbors");
            var invalid = new[] { new HexCoordinates(-1, 0), new HexCoordinates(0, -1),
                new HexCoordinates(12, 0), new HexCoordinates(0, 12), new HexCoordinates(int.MaxValue, int.MinValue) };
            foreach (var coordinate in invalid)
            {
                Require(!grid.TryGetCell(coordinate, out var missing) && missing == null, "Invalid lookup");
                Require(!grid.GetNeighbors(coordinate).Any(), "Invalid origin");
                Require(!grid.TryOccupy(coordinate, "unit") && !grid.TryVacate(coordinate, "unit"), "Invalid occupancy");
                Throws<ArgumentOutOfRangeException>(() => grid.GetCell(coordinate));
            }

            // Independent breadth-first distances verify every pair on the bounded grid.
            // This is a test oracle, not a runtime pathfinding implementation.
            foreach (var start in grid.Cells)
            {
                Require(ReferenceEquals(start, grid.GetCell(start.Coordinates)), "Stable cell identity");
                Require(start.Terrain == TerrainType.Ground && !start.IsOccupied, "Initial cell state");
                var distances = new Dictionary<HexCoordinates, int> { [start.Coordinates] = 0 };
                var queue = new Queue<HexCoordinates>();
                queue.Enqueue(start.Coordinates);
                while (queue.Count > 0)
                {
                    var current = queue.Dequeue();
                    foreach (var neighbor in grid.GetNeighbors(current))
                        if (!distances.ContainsKey(neighbor.Coordinates))
                        {
                            distances.Add(neighbor.Coordinates, distances[current] + 1);
                            queue.Enqueue(neighbor.Coordinates);
                        }
                }
                Require(distances.Count == 144, "Grid connectivity");
                foreach (var target in grid.Cells)
                    Require(start.Coordinates.DistanceTo(target.Coordinates) == distances[target.Coordinates], "Distance vs BFS");
            }
            for (int index = 0; index < grid.Cells.Count; index++)
                Require(grid.Cells[index].Coordinates == new HexCoordinates(index % grid.Width, index / grid.Width), "Row-major order");

            var next = new HexCoordinates(1, 0);
            Require(grid.TryOccupy(origin, "hero"), "First occupancy");
            Require(!grid.TryOccupy(origin, "enemy"), "Occupied cell cannot be overwritten");
            Require(!grid.TryOccupy(next, "hero"), "Duplicate unit rejected");
            Require(!grid.TryVacate(origin, "enemy") && grid.GetCell(origin).OccupantId == "hero", "Wrong owner cannot vacate");
            Require(grid.TryVacate(origin, "hero") && !grid.GetCell(origin).IsOccupied, "Vacate");
            Require(!grid.TryVacate(origin, "hero"), "Repeated vacate");
            Require(grid.TryOccupy(next, "hero"), "Released ID can be reused");
            var destination = new HexCoordinates(2, 0);
            Require(grid.TryMoveOccupant(next, destination, "hero"), "Atomic occupancy move");
            Require(!grid.GetCell(next).IsOccupied && grid.GetCell(destination).OccupantId == "hero",
                "Atomic move updates both cells");
            Require(!grid.TryMoveOccupant(next, origin, "hero") &&
                grid.GetCell(destination).OccupantId == "hero", "Failed move preserves occupancy");
            Require(!grid.TryMoveOccupant(destination, destination, "hero"), "Same-cell move rejected");
            Require(new GridModel().TryOccupy(origin, "hero"), "Independent grids");
            Throws<ArgumentException>(() => grid.TryOccupy(origin, null));
            Throws<ArgumentException>(() => grid.TryVacate(origin, " "));
            Throws<ArgumentException>(() => grid.TryMoveOccupant(origin, next, null));
            foreach (TerrainType terrain in Enum.GetValues(typeof(TerrainType)))
            {
                grid.GetCell(origin).Terrain = terrain;
                Require(grid.GetCell(origin).Terrain == terrain, "Terrain storage");
            }
            Throws<ArgumentOutOfRangeException>(() => grid.GetCell(origin).Terrain = (TerrainType)99);
            PrototypeSetup.Validate();
            Debug.Log("WP-01 checks passed: 20,736 pair distances, boundaries, occupancy, terrain, and foundation scene.");
        }

        private static void Require(bool condition, string description)
        {
            if (!condition) throw new InvalidOperationException("WP-01 failed: " + description);
        }

        private static void Throws<T>(Action action) where T : Exception
        {
            try { action(); }
            catch (T) { return; }
            throw new InvalidOperationException("Expected " + typeof(T).Name);
        }
    }
}
