using System;
using System.Collections.Generic;

namespace LittleCiv.Core
{
    public static class NeutralDefenseResolver
    {
        public static List<EntityId> StartAvailableReactivations(GameState state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            var result = new List<EntityId>();
            var cities = NeutralCities(state);
            for (var index = 0; index < cities.Count; index++)
            {
                var city = cities[index];
                var facility = state.DefenseFacilities.Find(item => item.CityId == city.Id &&
                    item.Type == DefenseFacilityType.ModernDefense &&
                    !item.IsModernDefenseActive && item.RemainingReactivationTurns <= 0);
                if (facility == null || !NeutralEconomyPlanner.Evaluate(state, city).IsSafe) continue;
                var command = new GameCommand
                {
                    CommandId = state.AllocateId(), PlayerId = city.OwnerId,
                    TurnNumber = state.TurnNumber, Type = GameCommandType.SetModernDefenseActive,
                    SubjectId = facility.Id, PrimaryValue = 1
                };
                if (DefenseFacilityResolver.TrySetModernActive(state, command)) result.Add(facility.Id);
            }
            return result;
        }

        public static List<DefenseFacilityState> StartAvailableConstruction(GameState state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            var result = new List<DefenseFacilityState>();
            var cities = NeutralCities(state);
            for (var index = 0; index < cities.Count; index++)
            {
                var city = cities[index];
                var government = state.Districts.Find(item => item.CityId == city.Id &&
                    item.Type == DistrictType.Government && item.ControllerId == city.OwnerId &&
                    item.IsOperational && item.RemainingConstructionTurns <= 0);
                if (government == null) continue;
                var facility = state.DefenseFacilities.Find(item => item.TileId == government.TileId);
                if (facility != null && facility.RemainingConstructionTurns > 0) continue;
                var target = NextType(city, facility == null ? DefenseFacilityType.None : facility.Type);
                if (!target.HasValue || !CanAfford(state, city, target.Value)) continue;
                var command = new GameCommand
                {
                    CommandId = state.AllocateId(), PlayerId = city.OwnerId,
                    TurnNumber = state.TurnNumber, Type = GameCommandType.StartDefenseFacility,
                    SubjectId = city.Id, TargetId = government.TileId,
                    PrimaryValue = (int)target.Value
                };
                if (DefenseFacilityResolver.TryStart(state, command, out var started))
                    result.Add(started);
            }
            return result;
        }

        public static bool CanAfford(GameState state, CityState city, DefenseFacilityType type)
        {
            var cost = DefenseFacilityResolver.GoldCost(type);
            if (city.Gold < cost) return false;
            var upkeep = type == DefenseFacilityType.ModernDefense
                ? DefenseFacilityResolver.ModernUpkeep
                : 0;
            return NeutralEconomyPlanner.Evaluate(state, city,
                additionalGoldUpkeep: upkeep, immediateGoldCost: cost).IsSafe;
        }

        private static DefenseFacilityType? NextType(CityState city, DefenseFacilityType current)
        {
            if (current == DefenseFacilityType.Moat &&
                NeutralResearchResolver.HasResearch(city, ResearchType.ModernDefense))
                return DefenseFacilityType.ModernDefense;
            if (current == DefenseFacilityType.Wall &&
                NeutralResearchResolver.HasResearch(city, ResearchType.AdvancedFortification))
                return DefenseFacilityType.Moat;
            if (current == DefenseFacilityType.None &&
                NeutralResearchResolver.HasResearch(city, ResearchType.Fortification))
                return DefenseFacilityType.Wall;
            return null;
        }

        private static List<CityState> NeutralCities(GameState state)
        {
            var result = state.Cities.FindAll(city =>
            {
                var owner = state.Players.Find(item => item.Id == city.OwnerId);
                return owner != null && owner.Slot == PlayerSlot.Neutral;
            });
            result.Sort((left, right) => left.Id.CompareTo(right.Id));
            return result;
        }
    }
}
