using System;
using System.Collections.Generic;
using GuildTactics.HexGrid;
using GuildTactics.Units;
using GridModel = GuildTactics.HexGrid.HexGrid;

namespace GuildTactics.Visibility
{
    public enum CellVisibility { Unknown, Explored, Visible }

    /// <summary>Party vision by hex distance. Walls do not occlude this first version.</summary>
    public sealed class FogOfWarSystem : IDisposable
    {
        private readonly List<UnitRuntimeState> observers = new List<UnitRuntimeState>();
        private readonly Dictionary<HexCoordinates, CellVisibility> states = new Dictionary<HexCoordinates, CellVisibility>();
        private readonly Dictionary<HexCoordinates, TerrainType> remembered = new Dictionary<HexCoordinates, TerrainType>();
        private bool disposed;
        public GridModel Grid { get; }
        public int Revision { get; private set; }

        public FogOfWarSystem(GridModel grid, IEnumerable<UnitRuntimeState> units)
        {
            Grid = grid ?? throw new ArgumentNullException(nameof(grid));
            if (units == null) throw new ArgumentNullException(nameof(units));
            // Validate the whole input before subscribing, including dead units on this grid.
            foreach (var unit in units)
            {
                if (unit == null || !unit.BelongsTo(grid)) throw new ArgumentException("Observers must belong to this grid.", nameof(units));
                if (unit.Team == UnitTeam.Player && !observers.Contains(unit)) observers.Add(unit);
            }
            foreach (var unit in observers) unit.StateChanged += Refresh;
            Refresh();
        }

        public CellVisibility GetState(HexCoordinates coordinate) =>
            states.TryGetValue(coordinate, out var state) ? state : CellVisibility.Unknown;

        public bool IsVisible(HexCoordinates coordinate) => GetState(coordinate) == CellVisibility.Visible;

        public bool TryGetRememberedTerrain(HexCoordinates coordinate, out TerrainType terrain) =>
            remembered.TryGetValue(coordinate, out terrain);

        // Called by the spawning system if another hero joins after construction.
        public void Register(UnitRuntimeState unit)
        {
            if (disposed) throw new ObjectDisposedException(nameof(FogOfWarSystem));
            if (unit == null || !unit.BelongsTo(Grid)) throw new ArgumentException("Observer must belong to this grid.", nameof(unit));
            if (unit.Team == UnitTeam.Player && !observers.Contains(unit))
            {
                observers.Add(unit);
                unit.StateChanged += Refresh;
            }
            Refresh();
        }

        public void Refresh()
        {
            if (disposed) return;
            Revision++;
            foreach (var cell in Grid.Cells)
            {
                bool visible = false;
                foreach (var unit in observers)
                    if (unit.IsPlacedOn(Grid) && unit.Position.DistanceTo(cell.Coordinates) <= unit.Definition.VisionRange)
                    { visible = true; break; }
                states[cell.Coordinates] = visible ? CellVisibility.Visible :
                    remembered.ContainsKey(cell.Coordinates) ? CellVisibility.Explored : CellVisibility.Unknown;
                if (visible) remembered[cell.Coordinates] = cell.Terrain;
            }
        }

        public void Dispose()
        {
            if (disposed) return;
            foreach (var unit in observers) unit.StateChanged -= Refresh;
            disposed = true;
        }
    }
}
