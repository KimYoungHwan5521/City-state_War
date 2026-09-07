using System;
using System.Collections.Generic;

namespace LittleCiv.Core
{
    public static class CitizenAssignmentResolver
    {
        public static bool TrySetAutoAssignment(GameState state, GameCommand command)
        {
            if (state == null || command == null ||
                command.Type != GameCommandType.SetCitizenAutoAssignment ||
                (command.PrimaryValue != 0 && command.PrimaryValue != 1)) return false;
            var city = FindCity(state, command.SubjectId);
            if (city == null || city.OwnerId != command.PlayerId) return false;
            city.CitizenAutoAssignment = command.PrimaryValue != 0;
            return true;
        }

        public static bool TryAssignManually(GameState state, GameCommand command)
        {
            if (state == null || command == null || command.Type != GameCommandType.AssignCitizen)
                return false;
            var district = state.Districts.Find(item => item.Id == command.SubjectId);
            if (district == null || district.Type == DistrictType.Government) return false;
            var city = FindCity(state, district.CityId);
            if (city == null || city.OwnerId != command.PlayerId || city.CitizenAutoAssignment ||
                district.ControllerId != city.OwnerId) return false;

            var desired = command.PrimaryValue;
            var maximum = MaximumAssignableCitizens(state, city, district);
            if (desired < 0 || desired > maximum || desired == district.AssignedCitizens) return false;
            if (desired > district.AssignedCitizens &&
                DistrictConstructionResolver.CountFreeCitizens(state, city) <
                desired - district.AssignedCitizens) return false;
            if ((district.IsPillaged || district.RemainingRepairTurns > 0) &&
                desired > district.AssignedCitizens) return false;

            district.AssignedCitizens = desired;
            if (district.RemainingConstructionTurns <= 0)
                district.IsOperational = desired > 0 && !district.IsPillaged &&
                                         district.RemainingRepairTurns <= 0 &&
                                         !district.IsMaintenanceSuspended;
            return true;
        }

        public static int MaximumAssignableCitizens(GameState state, CityState city, DistrictState district)
        {
            if (district == null || district.Type == DistrictType.Government) return 0;
            if (district.RemainingConstructionTurns > 0 || district.Type != DistrictType.Agriculture) return 1;
            var player = state?.Players.Find(item => item.Id == city.OwnerId);
            if (player == null || HasResearch(player, ResearchType.MechanizedAgriculture)) return 1;
            return HasResearch(player, ResearchType.Irrigation) ? 2 : 1;
        }

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

        private static bool HasResearch(PlayerState player, ResearchType type)
        {
            return player.CompletedResearch != null && player.CompletedResearch.Contains(type);
        }
    }
}
