using System;
using System.Collections.Generic;

namespace LittleCiv.Core
{
    public static class GameStateHasher
    {
        private const ulong OffsetBasis = 14695981039346656037UL;
        private const ulong Prime = 1099511628211UL;

        public static ulong Compute(GameState state)
        {
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            var hash = OffsetBasis;
            Add(ref hash, state.SchemaVersion);
            Add(ref hash, state.MatchSeed);
            Add(ref hash, state.TurnNumber);
            Add(ref hash, state.NextEntityId);

            HashPlayers(ref hash, state.Players);
            HashCities(ref hash, state.Cities);
            HashTiles(ref hash, state.Tiles);
            HashUnits(ref hash, state.Units);
            HashDistricts(ref hash, state.Districts);
            return hash;
        }

        private static void HashPlayers(ref ulong hash, List<PlayerState> source)
        {
            var items = SortedCopy(source, item => item.Id.Value);
            Add(ref hash, items.Count);
            foreach (var item in items)
            {
                Add(ref hash, item.Id.Value);
                Add(ref hash, (int)item.Slot);
                Add(ref hash, item.Gold);
                Add(ref hash, item.StoredFood);
                Add(ref hash, item.ReserveTimeSeconds);
            }
        }

        private static void HashCities(ref ulong hash, List<CityState> source)
        {
            var items = SortedCopy(source, item => item.Id.Value);
            Add(ref hash, items.Count);
            foreach (var item in items)
            {
                Add(ref hash, item.Id.Value);
                Add(ref hash, item.OwnerId.Value);
                Add(ref hash, item.WorldQ);
                Add(ref hash, item.WorldR);
                Add(ref hash, item.Population);
            }
        }

        private static void HashTiles(ref ulong hash, List<TileState> source)
        {
            var items = SortedCopy(source, item => item.Id.Value);
            Add(ref hash, items.Count);
            foreach (var item in items)
            {
                Add(ref hash, item.Id.Value);
                Add(ref hash, item.CityId.Value);
                Add(ref hash, item.Q);
                Add(ref hash, item.R);
                Add(ref hash, item.ControllerId.Value);
                Add(ref hash, item.GroundFood);
            }
        }

        private static void HashUnits(ref ulong hash, List<UnitState> source)
        {
            var items = SortedCopy(source, item => item.Id.Value);
            Add(ref hash, items.Count);
            foreach (var item in items)
            {
                Add(ref hash, item.Id.Value);
                Add(ref hash, item.OwnerId.Value);
                Add(ref hash, item.TileId.Value);
                Add(ref hash, (int)item.Type);
                Add(ref hash, item.HitPoints);
                Add(ref hash, item.CarriedFood);
                Add(ref hash, item.IsStarving ? 1 : 0);
            }
        }

        private static void HashDistricts(ref ulong hash, List<DistrictState> source)
        {
            var items = SortedCopy(source, item => item.Id.Value);
            Add(ref hash, items.Count);
            foreach (var item in items)
            {
                Add(ref hash, item.Id.Value);
                Add(ref hash, item.CityId.Value);
                Add(ref hash, item.TileId.Value);
                Add(ref hash, (int)item.Type);
                Add(ref hash, item.ControllerId.Value);
                Add(ref hash, item.IsOperational ? 1 : 0);
            }
        }

        private static List<T> SortedCopy<T>(List<T> source, Func<T, long> keySelector)
        {
            var result = source == null ? new List<T>() : new List<T>(source);
            result.Sort((left, right) => keySelector(left).CompareTo(keySelector(right)));
            return result;
        }

        private static void Add(ref ulong hash, int value) => Add(ref hash, (long)value);

        private static void Add(ref ulong hash, long value)
        {
            unchecked
            {
                var data = (ulong)value;
                for (var i = 0; i < sizeof(long); i++)
                {
                    hash ^= (byte)(data & 0xFF);
                    hash *= Prime;
                    data >>= 8;
                }
            }
        }
    }
}
