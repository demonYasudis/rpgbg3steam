using System;

namespace GuildTactics.HexGrid
{
    /// <summary>Axial coordinates. Direction order: +q, +q-r, -r, -q, -q+r, +r.</summary>
    public readonly struct HexCoordinates : IEquatable<HexCoordinates>
    {
        public const int DirectionCount = 6;
        private static readonly HexCoordinates[] Directions =
        {
            new HexCoordinates(1, 0), new HexCoordinates(1, -1),
            new HexCoordinates(0, -1), new HexCoordinates(-1, 0),
            new HexCoordinates(-1, 1), new HexCoordinates(0, 1)
        };

        public int Q { get; }
        public int R { get; }

        public HexCoordinates(int q, int r) { Q = q; R = r; }

        public HexCoordinates GetNeighbor(int direction)
        {
            if (direction < 0 || direction >= DirectionCount)
                throw new ArgumentOutOfRangeException(nameof(direction));
            var offset = Directions[direction];
            return new HexCoordinates(checked(Q + offset.Q), checked(R + offset.R));
        }

        // Long intermediates also support distances across the full int coordinate range.
        public long DistanceTo(HexCoordinates other)
        {
            long dq = (long)Q - other.Q;
            long dr = (long)R - other.R;
            return (Math.Abs(dq) + Math.Abs(dr) + Math.Abs(dq + dr)) / 2;
        }

        public bool Equals(HexCoordinates other) => Q == other.Q && R == other.R;
        public override bool Equals(object obj) => obj is HexCoordinates other && Equals(other);
        public override int GetHashCode() => unchecked((Q * 397) ^ R);
        public static bool operator ==(HexCoordinates left, HexCoordinates right) => left.Equals(right);
        public static bool operator !=(HexCoordinates left, HexCoordinates right) => !left.Equals(right);
        public override string ToString() => $"({Q}, {R})";
    }
}
