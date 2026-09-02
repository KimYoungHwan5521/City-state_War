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
            var player = state.Players.Find(item => item.Id == command.PlayerId);
            if (!IsUnlocked(player, city, type)) return false;
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

        private static bool IsUnlocked(PlayerState player, CityState city, DistrictType type)
        {
            if (player == null) return false;
            if (player.Slot != PlayerSlot.Neutral)
                return !player.ResearchUnlocksEnabled || player.UnlockedDistrictTypes.Contains(type);
            switch (type)
            {
                case DistrictType.Agriculture:
                case DistrictType.Commerce:
                case DistrictType.Military:
                    return true;
                case DistrictType.Science:
                    return NeutralResearchResolver.HasResearch(city, ResearchType.School);
                case DistrictType.Culture:
                    return NeutralResearchResolver.HasResearch(city, ResearchType.Arts);
                default:
                    return false;
            }
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

        public static bool TryStartRepair(GameState state, GameCommand command, out DistrictState district)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (command == null) throw new ArgumentNullException(nameof(command));
            district = FindDistrict(state, command.SubjectId);
            if (district == null || district.RemainingRepairTurns > 0 ||
                district.Type == DistrictType.Government) return false;
            var city = FindCity(state, district.CityId);
            if (city == null || city.OwnerId != command.PlayerId ||
                district.ControllerId != city.OwnerId) return false;
            var legacyRecapturedState = !district.IsOperational && district.AssignedCitizens > 0 &&
                                       district.RemainingConstructionTurns <= 0 &&
                                       !district.IsMaintenanceSuspended;
            if (!district.IsPillaged && !legacyRecapturedState) return false;
            district.IsPillaged = true;
            district.RemainingRepairTurns = RepairTurns(district.Type);
            district.IsOperational = false;
            return true;
        }

        public static List<EntityId> AdvanceRepairs(GameState state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            var completed = new List<EntityId>();
            var districts = new List<DistrictState>(state.Districts);
            districts.Sort((left, right) => left.Id.CompareTo(right.Id));
            for (var i = 0; i < districts.Count; i++)
            {
                var district = districts[i];
                if (!district.IsPillaged || district.RemainingRepairTurns <= 0) continue;
                var city = FindCity(state, district.CityId);
                if (city == null || district.ControllerId != city.OwnerId) continue;
                district.RemainingRepairTurns--;
                if (district.RemainingRepairTurns > 0) continue;
                district.IsPillaged = false;
                district.IsOperational = district.AssignedCitizens > 0 && !district.IsMaintenanceSuspended;
                completed.Add(district.Id);
            }
            return completed;
        }

        public static int RepairTurns(DistrictType type)
        {
            switch (type)
            {
                case DistrictType.Agriculture:
                case DistrictType.Commerce: return 2;
                case DistrictType.Science:
                case DistrictType.Culture:
                case DistrictType.Military: return 3;
                case DistrictType.NuclearFacility: return 5;
                default: return 0;
            }
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

        private static DistrictState FindDistrict(GameState state, EntityId districtId)
        {
            for (var i = 0; i < state.Districts.Count; i++)
                if (state.Districts[i].Id == districtId) return state.Districts[i];
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
