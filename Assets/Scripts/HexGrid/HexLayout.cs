using System;
using UnityEngine;

namespace GuildTactics.HexGrid
{
    /// <summary>Pointy-top hexes on the XY plane. Radius is center to corner.</summary>
    public sealed class HexLayout
    {
        private static readonly float Sqrt3 = Mathf.Sqrt(3f);
        public float Radius { get; }
        public Vector2 Origin { get; }

        public HexLayout(float radius = 1f, Vector2 origin = default)
        {
            if (float.IsNaN(radius) || float.IsInfinity(radius) || radius <= 0)
                throw new ArgumentOutOfRangeException(nameof(radius));
            Radius = radius;
            Origin = origin;
        }

        public Vector3 ToWorld(HexCoordinates coordinates) => new Vector3(
            Origin.x + Radius * Sqrt3 * (coordinates.Q + coordinates.R * 0.5f),
            Origin.y + Radius * 1.5f * coordinates.R, 0);

        public HexCoordinates ToCoordinates(Vector3 world)
        {
            double x = (world.x - Origin.x) / Radius;
            double y = (world.y - Origin.y) / Radius;
            double q = x / Sqrt3 - y / 3;
            double r = 2 * y / 3;
            double s = -q - r;
            double rq = Math.Round(q), rr = Math.Round(r), rs = Math.Round(s);
            double dq = Math.Abs(rq - q), dr = Math.Abs(rr - r), ds = Math.Abs(rs - s);
            // Round cube coordinates, correcting the largest error to retain q+r+s=0.
            if (dq > dr && dq > ds) rq = -rr - rs;
            else if (dr > ds) rr = -rq - rs;
            return new HexCoordinates(checked((int)rq), checked((int)rr));
        }

        public Vector2 Corner(int index)
        {
            if (index < 0 || index >= HexCoordinates.DirectionCount)
                throw new ArgumentOutOfRangeException(nameof(index));
            float angle = (30 + 60 * index) * Mathf.Deg2Rad;
            return new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * Radius;
        }

        public Bounds GetBounds(HexGrid grid)
        {
            var first = ToWorld(new HexCoordinates(0, 0));
            var last = ToWorld(new HexCoordinates(grid.Width - 1, grid.Height - 1));
            return new Bounds((first + last) * 0.5f,
                last - first + new Vector3(Sqrt3 * Radius, 2 * Radius, 0));
        }
    }
}
