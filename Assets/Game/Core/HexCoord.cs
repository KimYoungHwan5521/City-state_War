using System;
using System.Collections.Generic;

namespace LittleCiv.Core
{
    [Serializable]
    public struct HexCoord : IEquatable<HexCoord>, IComparable<HexCoord>
    {
        private static readonly HexCoord[] Directions =
        {
            new HexCoord(1, 0),
            new HexCoord(1, -1),
            new HexCoord(0, -1),
            new HexCoord(-1, 0),
            new HexCoord(-1, 1),
            new HexCoord(0, 1)
        };

        public int Q;
        public int R;

        public HexCoord(int q, int r)
        {
            Q = q;
            R = r;
        }

        public int S => -Q - R;
        public int Length => (Math.Abs(Q) + Math.Abs(R) + Math.Abs(S)) / 2;

        public static HexCoord Direction(int index)
        {
            if (index < 0 || index >= Directions.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            return Directions[index];
        }

        public static int Distance(HexCoord left, HexCoord right) => (left - right).Length;

        public static List<HexCoord> WithinRadius(int radius)
        {
            if (radius < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(radius));
            }

            var result = new List<HexCoord>(1 + (3 * radius * (radius + 1)));
            for (var q = -radius; q <= radius; q++)
            {
                var minimumR = Math.Max(-radius, -q - radius);
                var maximumR = Math.Min(radius, -q + radius);
                for (var r = minimumR; r <= maximumR; r++)
                {
                    result.Add(new HexCoord(q, r));
                }
            }

            result.Sort();
            return result;
        }

        public static List<HexCoord> Ring(int radius)
        {
            if (radius <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(radius));
            }

            var result = WithinRadius(radius);
            result.RemoveAll(coord => coord.Length != radius);
            return result;
        }

        public static List<HexCoord> Side(int radius, int directionIndex)
        {
            Direction(directionIndex);
            var result = Ring(radius);
            result.RemoveAll(coord => !IsOnSide(coord, radius, directionIndex));
            result.Sort((left, right) => SideOrder(left, right, directionIndex));
            return result;
        }

        private static bool IsOnSide(HexCoord coord, int radius, int directionIndex)
        {
            switch (directionIndex)
            {
                case 0: return coord.Q == radius;
                case 1: return coord.R == -radius;
                case 2: return coord.S == radius;
                case 3: return coord.Q == -radius;
                case 4: return coord.R == radius;
                case 5: return coord.S == -radius;
                default: throw new ArgumentOutOfRangeException(nameof(directionIndex));
            }
        }

        private static int SideOrder(HexCoord left, HexCoord right, int directionIndex)
        {
            var tangent = Direction((directionIndex + 2) % 6);
            var leftProjection = (left.Q * tangent.Q) + (left.R * tangent.R) + (left.S * tangent.S);
            var rightProjection = (right.Q * tangent.Q) + (right.R * tangent.R) + (right.S * tangent.S);
            var comparison = leftProjection.CompareTo(rightProjection);
            return comparison != 0 ? comparison : left.CompareTo(right);
        }

        public int CompareTo(HexCoord other)
        {
            var qComparison = Q.CompareTo(other.Q);
            return qComparison != 0 ? qComparison : R.CompareTo(other.R);
        }

        public bool Equals(HexCoord other) => Q == other.Q && R == other.R;
        public override bool Equals(object obj) => obj is HexCoord other && Equals(other);
        public override int GetHashCode() => unchecked((Q * 397) ^ R);
        public override string ToString() => $"({Q}, {R})";

        public static HexCoord operator +(HexCoord left, HexCoord right) =>
            new HexCoord(left.Q + right.Q, left.R + right.R);

        public static HexCoord operator -(HexCoord left, HexCoord right) =>
            new HexCoord(left.Q - right.Q, left.R - right.R);

        public static bool operator ==(HexCoord left, HexCoord right) => left.Equals(right);
        public static bool operator !=(HexCoord left, HexCoord right) => !left.Equals(right);
    }
}
