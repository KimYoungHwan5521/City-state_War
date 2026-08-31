using System;

namespace LittleCiv.Core
{
    [Serializable]
    public struct EntityId : IEquatable<EntityId>, IComparable<EntityId>
    {
        public long Value;

        public EntityId(long value)
        {
            if (value <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value), "Entity IDs must be positive.");
            }

            Value = value;
        }

        public bool IsValid => Value > 0;
        public int CompareTo(EntityId other) => Value.CompareTo(other.Value);
        public bool Equals(EntityId other) => Value == other.Value;
        public override bool Equals(object obj) => obj is EntityId other && Equals(other);
        public override int GetHashCode() => Value.GetHashCode();
        public override string ToString() => Value.ToString();
        public static bool operator ==(EntityId left, EntityId right) => left.Equals(right);
        public static bool operator !=(EntityId left, EntityId right) => !left.Equals(right);
    }
}
