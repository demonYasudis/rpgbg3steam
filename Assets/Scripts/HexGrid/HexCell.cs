using System;

namespace GuildTactics.HexGrid
{
    public enum TerrainType { Ground, HighGround, Pit, Blocked }

    public sealed class HexCell
    {
        private TerrainType terrain;
        private int movementCost = 1;
        public HexCoordinates Coordinates { get; }
        public string OccupantId { get; internal set; }
        public bool IsOccupied => OccupantId != null;

        /// <summary>Positive cost paid when entering this cell; the starting cell costs zero.</summary>
        public int MovementCost
        {
            get => movementCost;
            set
            {
                if (value <= 0) throw new ArgumentOutOfRangeException(nameof(value));
                movementCost = value;
            }
        }

        // Terrain remains data; HexPathfinder owns normal traversal rules.
        public TerrainType Terrain
        {
            get => terrain;
            set
            {
                if (value < TerrainType.Ground || value > TerrainType.Blocked)
                    throw new ArgumentOutOfRangeException(nameof(value));
                terrain = value;
            }
        }

        internal HexCell(HexCoordinates coordinates) { Coordinates = coordinates; }
    }
}
