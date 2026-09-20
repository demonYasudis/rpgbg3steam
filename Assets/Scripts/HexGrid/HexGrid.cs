using System;
using System.Collections.Generic;

namespace GuildTactics.HexGrid
{
    /// <summary>Bounded axial parallelogram: 0 &lt;= q &lt; Width, 0 &lt;= r &lt; Height.</summary>
    public sealed class HexGrid
    {
        public const int DefaultWidth = 12;
        public const int DefaultHeight = 12;
        private readonly HexCell[] cells;
        private readonly Dictionary<string, HexCell> occupants = new Dictionary<string, HexCell>(StringComparer.Ordinal);
        public int Width { get; }
        public int Height { get; }
        public IReadOnlyList<HexCell> Cells { get; }

        public HexGrid(int width = DefaultWidth, int height = DefaultHeight)
        {
            if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
            if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
            Width = width;
            Height = height;
            cells = new HexCell[checked(width * height)];
            for (int r = 0; r < height; r++)
                for (int q = 0; q < width; q++)
                    cells[r * width + q] = new HexCell(new HexCoordinates(q, r));
            Cells = Array.AsReadOnly(cells);
        }

        public bool Contains(HexCoordinates coordinates) =>
            coordinates.Q >= 0 && coordinates.Q < Width && coordinates.R >= 0 && coordinates.R < Height;

        public bool TryGetCell(HexCoordinates coordinates, out HexCell cell)
        {
            cell = Contains(coordinates) ? cells[coordinates.R * Width + coordinates.Q] : null;
            return cell != null;
        }

        public HexCell GetCell(HexCoordinates coordinates)
        {
            if (!TryGetCell(coordinates, out var cell))
                throw new ArgumentOutOfRangeException(nameof(coordinates), coordinates, "Coordinate is outside this grid.");
            return cell;
        }

        // Invalid origins produce no neighbors; edge cells never wrap around.
        public IEnumerable<HexCell> GetNeighbors(HexCoordinates coordinates)
        {
            if (!Contains(coordinates)) yield break;
            for (int direction = 0; direction < HexCoordinates.DirectionCount; direction++)
                if (TryGetCell(coordinates.GetNeighbor(direction), out var cell)) yield return cell;
        }

        public bool TryOccupy(HexCoordinates coordinates, string occupantId)
        {
            ValidateOccupantId(occupantId);
            if (!TryGetCell(coordinates, out var cell) || cell.IsOccupied || occupants.ContainsKey(occupantId))
                return false;
            occupants.Add(occupantId, cell);
            cell.OccupantId = occupantId;
            return true;
        }

        // An unrelated unit cannot accidentally release another unit's cell.
        public bool TryVacate(HexCoordinates coordinates, string occupantId)
        {
            ValidateOccupantId(occupantId);
            if (!TryGetCell(coordinates, out var cell) || cell.OccupantId != occupantId) return false;
            occupants.Remove(occupantId);
            cell.OccupantId = null;
            return true;
        }

        private static void ValidateOccupantId(string occupantId)
        {
            if (string.IsNullOrWhiteSpace(occupantId))
                throw new ArgumentException("An occupant requires a non-empty stable ID.", nameof(occupantId));
        }
    }
}
