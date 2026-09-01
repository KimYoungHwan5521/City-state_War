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
            Add(ref hash, (int)state.Victory);
            Add(ref hash, state.WinnerId.Value);

            HashPlayers(ref hash, state.Players);
            HashCities(ref hash, state.Cities);
            HashTiles(ref hash, state.Tiles);
            HashUnits(ref hash, state.Units);
            HashDistricts(ref hash, state.Districts);
            HashUnitTrainings(ref hash, state.UnitTrainings);
            HashMapTopology(ref hash, state.MapTopology);
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
                var unlockedUnits = item.UnlockedUnitTypes == null
                    ? new List<UnitType>()
                    : new List<UnitType>(item.UnlockedUnitTypes);
                unlockedUnits.Sort();
                Add(ref hash, unlockedUnits.Count);
                for (var unlockedIndex = 0; unlockedIndex < unlockedUnits.Count; unlockedIndex++)
                    Add(ref hash, (int)unlockedUnits[unlockedIndex]);
            }
        }

        private static void HashCities(ref ulong hash, List<CityState> source)
        {
            var items = SortedCopy(source, item => item.Id.Value);
            Add(ref hash, items.Count);
            foreach (var item in items)
            {
                Add(ref hash, item.Id.Value);
                Add(ref hash, item.Name);
                Add(ref hash, item.OwnerId.Value);
                Add(ref hash, item.WorldQ);
                Add(ref hash, item.WorldR);
                Add(ref hash, item.Population);
                Add(ref hash, item.GovernmentCitizens);
                Add(ref hash, item.Gold);
                Add(ref hash, item.StoredFood);
                Add(ref hash, item.GrowthProgress);
                Add(ref hash, item.FamineProgress);
                Add(ref hash, item.ResearchPoints);
                Add(ref hash, item.LastFoodProduction);
                Add(ref hash, item.LastGoldProduction);
                Add(ref hash, item.LastScienceProduction);
                Add(ref hash, item.LastCultureProduction);
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
                Add(ref hash, item.GroundFoodOwnerId.Value);
                Add(ref hash, item.GroundFoodReturnTurn);
                Add(ref hash, item.IsSharedBoundary ? 1 : 0);
                Add(ref hash, item.DefenseBonusPercent);
                Add(ref hash, (int)item.ResourceType);
                var visibleCities = item.VisibleCityIds == null
                    ? new List<EntityId>()
                    : new List<EntityId>(item.VisibleCityIds);
                visibleCities.Sort();
                Add(ref hash, visibleCities.Count);
                foreach (var cityId in visibleCities)
                {
                    Add(ref hash, cityId.Value);
                }
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
                Add(ref hash, item.RemainingMovement);
                Add(ref hash, item.HasAutomaticDefense ? 1 : 0);
                Add(ref hash, item.MaintenancePriority);
                Add(ref hash, item.CreatedTurn);
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
                Add(ref hash, item.AssignedCitizens);
                Add(ref hash, item.RemainingConstructionTurns);
                Add(ref hash, item.CitizenRemovalPriority);
                Add(ref hash, item.MaintenancePriority);
                Add(ref hash, item.IsMaintenanceSuspended ? 1 : 0);
            }
        }

        private static void HashUnitTrainings(ref ulong hash, List<UnitTrainingState> source)
        {
            var items = SortedCopy(source, item => item.Id.Value);
            Add(ref hash, items.Count);
            foreach (var item in items)
            {
                Add(ref hash, item.Id.Value);
                Add(ref hash, item.DistrictId.Value);
                Add(ref hash, item.OwnerId.Value);
                Add(ref hash, (int)item.Type);
                Add(ref hash, item.RemainingTurns);
                Add(ref hash, item.IsAwaitingDeployment ? 1 : 0);
            }
        }

        private static void HashMapTopology(ref ulong hash, WorldMapTopology topology)
        {
            var views = topology == null || topology.CityViews == null
                ? new List<CityMapView>()
                : new List<CityMapView>(topology.CityViews);
            views.Sort((left, right) => left.CityId.CompareTo(right.CityId));
            Add(ref hash, views.Count);
            foreach (var view in views)
            {
                Add(ref hash, view.CityId.Value);
                var placements = view.Tiles == null
                    ? new List<CityTilePlacement>()
                    : new List<CityTilePlacement>(view.Tiles);
                placements.Sort((left, right) =>
                {
                    var qComparison = left.LocalQ.CompareTo(right.LocalQ);
                    return qComparison != 0 ? qComparison : left.LocalR.CompareTo(right.LocalR);
                });
                Add(ref hash, placements.Count);
                foreach (var placement in placements)
                {
                    Add(ref hash, placement.TileId.Value);
                    Add(ref hash, placement.LocalQ);
                    Add(ref hash, placement.LocalR);
                    Add(ref hash, placement.IsBuildable ? 1 : 0);
                }
            }
        }

        private static List<T> SortedCopy<T>(List<T> source, Func<T, long> keySelector)
        {
            var result = source == null ? new List<T>() : new List<T>(source);
            result.Sort((left, right) => keySelector(left).CompareTo(keySelector(right)));
            return result;
        }

        private static void Add(ref ulong hash, int value) => Add(ref hash, (long)value);

        private static void Add(ref ulong hash, string value)
        {
            value = value ?? string.Empty;

            Add(ref hash, value.Length);
            foreach (var character in value)
            {
                Add(ref hash, character);
            }
        }

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
