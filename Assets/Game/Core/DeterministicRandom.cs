using System;

namespace LittleCiv.Core
{
    public sealed class DeterministicRandom
    {
        private ulong _state;

        public DeterministicRandom(long seed)
        {
            _state = unchecked((ulong)seed);
            if (_state == 0)
            {
                _state = 0x9E3779B97F4A7C15UL;
            }
        }

        public ulong State => _state;

        public ulong NextUInt64()
        {
            var value = _state;
            value ^= value >> 12;
            value ^= value << 25;
            value ^= value >> 27;
            _state = value;
            return value * 2685821657736338717UL;
        }

        public int NextInt(int exclusiveMax)
        {
            if (exclusiveMax <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(exclusiveMax));
            }

            return (int)(NextUInt64() % (uint)exclusiveMax);
        }
    }
}
