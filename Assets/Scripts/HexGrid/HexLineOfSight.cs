using System;
using GridModel = GuildTactics.HexGrid.HexGrid;

namespace GuildTactics.HexGrid
{
    /// <summary>Closed hex supercover: grazing a wall edge/corner also blocks the line.
    /// Cube-space Voronoi boundaries are |dx-dy|, |dy-dz|, |dz-dx| <= 1.
    /// Clipping a segment against these three slabs is symmetric and includes both tied cells.</summary>
    public static class HexLineOfSight
    {
        public static bool CanSee(GridModel grid, HexCoordinates origin, HexCoordinates target)
        {
            if (grid == null) throw new ArgumentNullException(nameof(grid));
            if (!grid.Contains(origin) || !grid.Contains(target)) return false;
            foreach (var cell in grid.Cells)
                if (cell.Terrain == TerrainType.Blocked && cell.Coordinates != origin && cell.Coordinates != target &&
                    Intersects(origin, target, cell.Coordinates)) return false;
            return true;
        }

        public static bool CanShoot(GridModel grid, HexCoordinates origin, HexCoordinates target) =>
            CanSee(grid, origin, target) && grid.GetCell(target).Terrain != TerrainType.Blocked;

        private static bool Intersects(HexCoordinates origin, HexCoordinates target, HexCoordinates center)
        {
            double x = (long)origin.Q - center.Q, z = (long)origin.R - center.R, y = -x - z;
            double vx = (long)target.Q - origin.Q, vz = (long)target.R - origin.R, vy = -vx - vz;
            double enter = 0, leave = 1;
            return Clip(x - y, vx - vy, ref enter, ref leave) &&
                Clip(y - z, vy - vz, ref enter, ref leave) && Clip(z - x, vz - vx, ref enter, ref leave);
        }

        private static bool Clip(double offset, double direction, ref double enter, ref double leave)
        {
            const double tolerance = 1e-9;
            if (direction == 0) return Math.Abs(offset) <= 1 + tolerance;
            double a = (-1 - offset) / direction, b = (1 - offset) / direction;
            enter = Math.Max(enter, Math.Min(a, b)); leave = Math.Min(leave, Math.Max(a, b));
            return enter <= leave + tolerance;
        }
    }
}
