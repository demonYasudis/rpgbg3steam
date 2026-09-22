using System;
using System.Collections.Generic;
using GuildTactics.HexGrid;
using GridModel = GuildTactics.HexGrid.HexGrid;

namespace GuildTactics.Units
{
    public enum UnitTeam { Player, Enemy }

    /// <summary>Mutable expedition state, kept separate from the shared unit definition.</summary>
    public sealed class UnitRuntimeState
    {
        private readonly GridModel spawnGrid;
        public string InstanceId { get; }
        public UnitDefinition Definition { get; }
        public HexCoordinates Position { get; private set; }
        public UnitTeam Team { get; }
        public int CurrentHealth { get; private set; }
        public bool IsAlive => CurrentHealth > 0;
        public int EvasionBonus { get; private set; }
        internal event Action StateChanged;
        internal bool BelongsTo(GridModel grid) => ReferenceEquals(grid, spawnGrid);
        public int Defense => (int)Math.Min(int.MaxValue, (long)Definition.Defense + EvasionBonus);

        internal void BeginTurn() => EvasionBonus = 0;
        internal void SetEvasion(int bonus) => EvasionBonus = bonus;

        /// <summary>A forced one-hex step; falling into a pit is immediately fatal.</summary>
        internal bool TryPushTo(GridModel grid, HexCoordinates destination)
        {
            if (!IsPlacedOn(grid) || Position.DistanceTo(destination) != 1 ||
                !grid.TryGetCell(destination, out var cell) || !TerrainRules.CanPushInto(cell.Terrain) ||
                cell.IsOccupied || !grid.TryMoveOccupant(Position, destination, InstanceId)) return false;
            Position = destination;
            if (cell.Terrain == TerrainType.Pit) ApplyDamage(CurrentHealth);
            else StateChanged?.Invoke();
            return true;
        }

        /// <summary>Validated forced movement / blink; does not spend walking points.</summary>
        internal bool TryRelocate(GridModel grid, HexCoordinates destination)
        {
            if (!IsPlacedOn(grid) || !grid.TryGetCell(destination, out var cell) ||
                !TerrainRules.CanWalk(cell.Terrain) || cell.IsOccupied ||
                !grid.TryMoveOccupant(Position, destination, InstanceId)) return false;
            Position = destination;
            StateChanged?.Invoke();
            return true;
        }

        private UnitRuntimeState(GridModel grid, string instanceId, UnitDefinition definition,
            HexCoordinates position, UnitTeam team)
        {
            spawnGrid = grid;
            InstanceId = instanceId;
            Definition = definition;
            Position = position;
            Team = team;
            CurrentHealth = definition.MaxHealth;
        }

        public static bool TrySpawn(GridModel grid, string instanceId, UnitDefinition definition,
            HexCoordinates position, out UnitRuntimeState unit, UnitTeam team = UnitTeam.Player)
        {
            if (grid == null) throw new ArgumentNullException(nameof(grid));
            if (string.IsNullOrWhiteSpace(instanceId))
                throw new ArgumentException("A unit requires a stable instance ID.", nameof(instanceId));
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            if (team != UnitTeam.Player && team != UnitTeam.Enemy)
                throw new ArgumentOutOfRangeException(nameof(team));
            unit = null;
            if (!grid.TryGetCell(position, out var cell) || !TerrainRules.CanWalk(cell.Terrain) ||
                !grid.TryOccupy(position, instanceId))
                return false;
            unit = new UnitRuntimeState(grid, instanceId, definition, position, team);
            return true;
        }

        /// <summary>Validates and commits an entire route as one logical occupancy update.</summary>
        public bool TryMoveAlong(GridModel grid, IReadOnlyList<HexCoordinates> path)
        {
            if (grid == null) throw new ArgumentNullException(nameof(grid));
            if (path == null) throw new ArgumentNullException(nameof(path));
            if (!IsAlive || !ReferenceEquals(grid, spawnGrid) || path.Count < 2 || path[0] != Position ||
                !grid.TryGetCell(Position, out var origin) || origin.OccupantId != InstanceId)
                return false;

            long cost = 0;
            var visited = new HashSet<HexCoordinates> { Position };
            for (int index = 1; index < path.Count; index++)
            {
                if (path[index - 1].DistanceTo(path[index]) != 1 ||
                    !visited.Add(path[index]) || !grid.TryGetCell(path[index], out var cell) ||
                    cell.IsOccupied || !TerrainRules.CanWalk(cell.Terrain))
                    return false;
                cost += cell.MovementCost;
                if (cost > Definition.Movement) return false;
            }

            var destination = path[path.Count - 1];
            if (!grid.TryMoveOccupant(Position, destination, InstanceId)) return false;
            Position = destination;
            StateChanged?.Invoke();
            return true;
        }

        internal bool IsPlacedOn(GridModel grid) => IsAlive && ReferenceEquals(grid, spawnGrid) &&
            grid.TryGetCell(Position, out var cell) && cell.OccupantId == InstanceId;

        // Damage is committed by CombatSystem, never by an animation callback.
        internal int ApplyDamage(int amount)
        {
            if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
            if (!IsAlive) return 0;
            int applied = Math.Min(CurrentHealth, amount);
            CurrentHealth -= applied;
            if (!IsAlive) spawnGrid.TryVacate(Position, InstanceId);
            StateChanged?.Invoke();
            return applied;
        }

    }
}
