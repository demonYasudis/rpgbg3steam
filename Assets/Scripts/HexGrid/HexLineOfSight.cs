using System;

namespace GuildTactics.HexGrid
{
    /// <summary>Center-to-center sight. Touching any wall edge or corner blocks the segment.</summary>
    public static class HexLineOfSight
    {
        public static bool IsClear(HexGrid grid, HexCoordinates origin, HexCoordinates target, bool allowWallTarget = false)
        {
            if (grid == null) throw new ArgumentNullException(nameof(grid));
            if (!grid.TryGetCell(origin, out var start) || !grid.TryGetCell(target, out var end) ||
                TerrainRules.BlocksSight(start.Terrain) || (!allowWallTarget && TerrainRules.BlocksSight(end.Terrain)))
                return false;
            long dq = (long)target.Q - origin.Q, dr = (long)target.R - origin.R;
            foreach (var cell in grid.Cells)
                if (cell.Coordinates != target && TerrainRules.BlocksSight(cell.Terrain) &&
                    IntersectsHex(origin, dq, dr, cell.Coordinates)) return false;
            return true;
        }

        private static bool IntersectsHex(HexCoordinates origin, long dq, long dr, HexCoordinates cell)
        {
            long q = (long)origin.Q - cell.Q, r = (long)origin.R - cell.R;
            var enter = new Fraction(0, 1);
            var leave = new Fraction(1, 1);
            // A point belongs to this hex exactly when all three axial inequalities hold:
            // |2q+r| <= 1, |q+2r| <= 1, |q-r| <= 1. Clip the segment against each pair.
            return Clip(2 * q + r, 2 * dq + dr, ref enter, ref leave) &&
                Clip(q + 2 * r, dq + 2 * dr, ref enter, ref leave) &&
                Clip(q - r, dq - dr, ref enter, ref leave);
        }

        private static bool Clip(long position, long slope, ref Fraction enter, ref Fraction leave)
        {
            if (slope == 0) return position >= -1 && position <= 1;
            var near = slope > 0 ? new Fraction(-1 - position, slope) : new Fraction(position - 1, -slope);
            var far = slope > 0 ? new Fraction(1 - position, slope) : new Fraction(position + 1, -slope);
            if (near.CompareTo(enter) > 0) enter = near;
            if (far.CompareTo(leave) < 0) leave = far;
            return enter.CompareTo(leave) <= 0;
        }

        private readonly struct Fraction
        {
            private readonly long numerator, denominator;
            public Fraction(long numerator, long denominator)
            { this.numerator = numerator; this.denominator = denominator; }
            // Exact integer products fit decimal even across the entire int coordinate range.
            // No divisions, epsilon, cube-rounding ties or direction-dependent results.
            public int CompareTo(Fraction other) => ((decimal)numerator * other.denominator)
                .CompareTo((decimal)other.numerator * denominator);
        }
    }
}
