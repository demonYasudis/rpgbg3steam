using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace GuildTactics.HexGrid
{
    /// <summary>Read-only search snapshot. Recalculate after terrain, costs or occupancy change.</summary>
    public sealed class HexMovementRange
    {
        private readonly Dictionary<HexCoordinates, HexCoordinates> previous;
        public HexCoordinates Origin { get; }
        public int Budget { get; }
        // Includes a valid origin at cost zero, even when occupied by the moving unit.
        public IReadOnlyDictionary<HexCoordinates, int> Costs { get; }

        internal HexMovementRange(HexCoordinates origin, int budget,
            Dictionary<HexCoordinates, int> costs, Dictionary<HexCoordinates, HexCoordinates> previous)
        {
            Origin = origin;
            Budget = budget;
            Costs = new ReadOnlyDictionary<HexCoordinates, int>(costs);
            this.previous = previous;
        }

        /// <summary>Returns origin through destination inclusive, or an empty path if unreachable.</summary>
        public IReadOnlyList<HexCoordinates> GetPathTo(HexCoordinates destination)
        {
            if (!Costs.ContainsKey(destination)) return Array.Empty<HexCoordinates>();
            var path = new List<HexCoordinates> { destination };
            var current = destination;
            while (current != Origin)
            {
                current = previous[current];
                path.Add(current);
            }
            path.Reverse();
            return path.AsReadOnly();
        }
    }
}
