using System;
using System.Collections.Generic;

namespace LittleCiv.Core
{
    public static class CitizenAssignmentResolver
    {
        public static EntityId RemoveExcessCitizen(GameState state, CityState city)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (city == null) throw new ArgumentNullException(nameof(city));
            if (CountAssignedCitizens(state, city) <= city.Population) return default(EntityId);

            var candidates = new List<DistrictState>();
            for (var index = 0; index < state.Districts.Count; index++)
            {
                var district = state.Districts[index];
                if (district.CityId == city.Id && district.Type != DistrictType.Government &&
                    district.AssignedCitizens > 0) candidates.Add(district);
            }

            candidates.Sort(CompareRemovalOrder);
            if (candidates.Count == 0) return default(EntityId);
            var selected = candidates[0];
            selected.AssignedCitizens--;
            if (selected.AssignedCitizens == 0) selected.IsOperational = false;
            return selected.Id;
        }

        public static bool TrySetRemovalPriority(GameState state, GameCommand command)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (command == null) throw new ArgumentNullException(nameof(command));
            if (command.PrimaryValue < 0) return false;
            for (var index = 0; index < state.Districts.Count; index++)
            {
                var district = state.Districts[index];
                if (district.Id != command.TargetId || district.Type == DistrictType.Government) continue;
                var city = FindCity(state, district.CityId);
                if (city == null || city.OwnerId != command.PlayerId) return false;
                district.CitizenRemovalPriority = command.PrimaryValue;
                return true;
            }
            return false;
        }

        private static int CountAssignedCitizens(GameState state, CityState city)
        {
            var assigned = city.GovernmentCitizens;
            for (var index = 0; index < state.Districts.Count; index++)
            {
                var district = state.Districts[index];
                if (district.CityId == city.Id && district.Type != DistrictType.Government)
                    assigned += district.AssignedCitizens;
            }
            return assigned;
        }

        private static int CompareRemovalOrder(DistrictState left, DistrictState right)
        {
            var leftConstruction = left.RemainingConstructionTurns > 0;
            var rightConstruction = right.RemainingConstructionTurns > 0;
            if (leftConstruction != rightConstruction) return leftConstruction ? -1 : 1;
            var leftSpecified = left.CitizenRemovalPriority > 0;
            var rightSpecified = right.CitizenRemovalPriority > 0;
            if (leftSpecified != rightSpecified) return leftSpecified ? -1 : 1;
            if (leftSpecified)
            {
                var comparison = left.CitizenRemovalPriority.CompareTo(right.CitizenRemovalPriority);
                if (comparison != 0) return comparison;
            }
            var typeComparison = DefaultRemovalOrder(left.Type).CompareTo(DefaultRemovalOrder(right.Type));
            return typeComparison != 0 ? typeComparison : left.Id.CompareTo(right.Id);
        }

        private static int DefaultRemovalOrder(DistrictType type)
        {
            switch (type)
            {
                case DistrictType.Culture: return 0;
                case DistrictType.Science: return 1;
                case DistrictType.Commerce: return 2;
                case DistrictType.Military: return 3;
                case DistrictType.Agriculture: return 4;
                default: return 5;
            }
        }

        private static CityState FindCity(GameState state, EntityId cityId)
        {
            for (var index = 0; index < state.Cities.Count; index++)
                if (state.Cities[index].Id == cityId) return state.Cities[index];
            return null;
        }
    }
}
