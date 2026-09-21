using System;
using System.Collections.Generic;
using GuildTactics.HexGrid;
using GridModel = GuildTactics.HexGrid.HexGrid;

namespace GuildTactics.Units
{
    /// <summary>Mutable expedition state, kept separate from the shared unit definition.</summary>
    public sealed class UnitRuntimeState
    {
        public string InstanceId { get; }
        public UnitDefinition Definition { get; }
        public HexCoordinates Position { get; private set; }

        private UnitRuntimeState(string instanceId, UnitDefinition definition, HexCoordinates position)
        {
            InstanceId = instanceId;
            Definition = definition;
            Position = position;
        }

        public static bool TrySpawn(GridModel grid, string instanceId, UnitDefinition definition,
            HexCoordinates position, out UnitRuntimeState unit)
        {
            if (grid == null) throw new ArgumentNullException(nameof(grid));
            if (string.IsNullOrWhiteSpace(instanceId))
                throw new ArgumentException("A unit requires a stable instance ID.", nameof(instanceId));
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            unit = null;
            if (!grid.TryGetCell(position, out var cell) || !IsWalkable(cell.Terrain) ||
                !grid.TryOccupy(position, instanceId))
                return false;
            unit = new UnitRuntimeState(instanceId, definition, position);
            return true;
        }

        /// <summary>Validates and commits an entire route as one logical occupancy update.</summary>
        public bool TryMoveAlong(GridModel grid, IReadOnlyList<HexCoordinates> path)
        {
            if (grid == null) throw new ArgumentNullException(nameof(grid));
            if (path == null) throw new ArgumentNullException(nameof(path));
            if (path.Count < 2 || path[0] != Position ||
                !grid.TryGetCell(Position, out var origin) || origin.OccupantId != InstanceId)
                return false;

            long cost = 0;
            var visited = new HashSet<HexCoordinates> { Position };
            for (int index = 1; index < path.Count; index++)
            {
                if (path[index - 1].DistanceTo(path[index]) != 1 ||
                    !visited.Add(path[index]) || !grid.TryGetCell(path[index], out var cell) ||
                    cell.IsOccupied || !IsWalkable(cell.Terrain))
                    return false;
                cost += cell.MovementCost;
                if (cost > Definition.Movement) return false;
            }

            var destination = path[path.Count - 1];
            if (!grid.TryMoveOccupant(Position, destination, InstanceId)) return false;
            Position = destination;
            return true;
        }

        private static bool IsWalkable(TerrainType terrain) =>
            terrain == TerrainType.Ground || terrain == TerrainType.HighGround;
    }
}
