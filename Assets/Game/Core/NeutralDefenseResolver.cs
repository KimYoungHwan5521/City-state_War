using System;
using System.Collections.Generic;

namespace LittleCiv.Core
{
    public static class NeutralDefenseResolver
    {
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
            if (type != DefenseFacilityType.ModernDefense) return true;
            var economy = CityEconomyResolver.CalculateBreakdown(state, city);
            return city.Gold >= cost + DefenseFacilityResolver.ModernUpkeep &&
                   economy.Gold.Total >= economy.UnitUpkeep + economy.FacilityUpkeep +
                       DefenseFacilityResolver.ModernUpkeep;
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
