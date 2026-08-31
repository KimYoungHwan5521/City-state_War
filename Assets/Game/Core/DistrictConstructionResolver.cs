using System;
using System.Collections.Generic;

namespace LittleCiv.Core
{
    public static class DistrictConstructionResolver
    {
        public const int StandardConstructionTurns = 3;
        public const int NuclearConstructionTurns = 5;

        public static bool TryStart(GameState state, GameCommand command, out DistrictState district)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (command == null) throw new ArgumentNullException(nameof(command));
            district = null;

            var city = FindCity(state, command.SubjectId);
            if (city == null || city.OwnerId != command.PlayerId) return false;
            if (!Enum.IsDefined(typeof(DistrictType), command.PrimaryValue)) return false;
            var type = (DistrictType)command.PrimaryValue;
            if (type == DistrictType.Government) return false;
            if (!IsBuildableTile(state, city.Id, command.TargetId)) return false;
            if (FindDistrictAt(state, command.TargetId) != null) return false;
            if (CountFreeCitizens(state, city) <= 0) return false;
            if (type == DistrictType.NuclearFacility && HasDistrictOfType(state, city.Id, type)) return false;

            district = new DistrictState
            {
                Id = state.AllocateId(),
                CityId = city.Id,
                TileId = command.TargetId,
                Type = type,
                ControllerId = city.OwnerId,
                IsOperational = false,
                AssignedCitizens = 1,
                RemainingConstructionTurns = type == DistrictType.NuclearFacility
                    ? NuclearConstructionTurns
                    : StandardConstructionTurns
            };
            state.Districts.Add(district);
            return true;
        }

        public static List<EntityId> Advance(GameState state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            var districts = new List<DistrictState>(state.Districts);
            districts.Sort((left, right) => left.Id.CompareTo(right.Id));
            var completed = new List<EntityId>();
            for (var i = 0; i < districts.Count; i++)
            {
                var district = districts[i];
                if (district.RemainingConstructionTurns <= 0 || district.AssignedCitizens <= 0) continue;
                var city = FindCity(state, district.CityId);
                if (city == null || district.ControllerId != city.OwnerId) continue;

                district.RemainingConstructionTurns--;
                if (district.RemainingConstructionTurns > 0) continue;
                district.IsOperational = true;
                completed.Add(district.Id);
            }
            return completed;
        }

        public static int CountFreeCitizens(GameState state, CityState city)
        {
            var assigned = city.GovernmentCitizens;
            for (var i = 0; i < state.Districts.Count; i++)
            {
                var district = state.Districts[i];
                if (district.CityId == city.Id && district.Type != DistrictType.Government)
                {
                    assigned += district.AssignedCitizens;
                }
            }
            return Math.Max(0, city.Population - assigned);
        }

        private static bool IsBuildableTile(GameState state, EntityId cityId, EntityId tileId)
        {
            var view = state.MapTopology == null ? null : state.MapTopology.FindView(cityId);
            if (view == null || view.Tiles == null) return false;
            for (var i = 0; i < view.Tiles.Count; i++)
            {
                var tile = view.Tiles[i];
                if (tile.TileId == tileId) return tile.IsBuildable;
            }
            return false;
        }

        private static CityState FindCity(GameState state, EntityId cityId)
        {
            for (var i = 0; i < state.Cities.Count; i++)
            {
                if (state.Cities[i].Id == cityId) return state.Cities[i];
            }
            return null;
        }

        private static DistrictState FindDistrictAt(GameState state, EntityId tileId)
        {
            for (var i = 0; i < state.Districts.Count; i++)
            {
                if (state.Districts[i].TileId == tileId) return state.Districts[i];
            }
            return null;
        }

        private static bool HasDistrictOfType(GameState state, EntityId cityId, DistrictType type)
        {
            for (var i = 0; i < state.Districts.Count; i++)
            {
                if (state.Districts[i].CityId == cityId && state.Districts[i].Type == type) return true;
            }
            return false;
        }
    }
}
