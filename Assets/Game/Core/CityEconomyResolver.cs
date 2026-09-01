using System;

namespace LittleCiv.Core
{
    public static class CityEconomyResolver
    {
        public const int GovernmentFood = 6;
        public const int GovernmentGold = 2;
        public const int GovernmentScience = 1;
        public const int GovernmentCulture = 1;
        public const int AgricultureFood = 2;
        public const int AgricultureResourceBonus = 2;
        public const int CommerceGold = 2;
        public const int CommerceResourceBonus = 2;
        public const int ScienceResearch = 2;
        public const int ScienceResourceBonus = 2;
        public const int CultureOutput = 2;
        public const int CultureResourceBonus = 1;

        public static void ResolveProduction(GameState state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));

            for (var cityIndex = 0; cityIndex < state.Cities.Count; cityIndex++)
            {
                var city = state.Cities[cityIndex];
                var breakdown = CalculateBreakdown(state, city);
                city.LastFoodProduction = breakdown.Food.Total;
                city.LastGoldProduction = breakdown.Gold.Total;
                city.LastScienceProduction = breakdown.Science.Total;
                city.LastCultureProduction = breakdown.Culture.Total;
                city.Gold += city.LastGoldProduction;
                city.ResearchPoints += city.LastScienceProduction;
            }
        }

        public static CityEconomyBreakdown CalculateBreakdown(GameState state, CityState city)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (city == null) throw new ArgumentNullException(nameof(city));
            var result = new CityEconomyBreakdown
            {
                PopulationConsumption = city.Population,
                GrowthRequired = city.Population * 3,
                FamineRequired = city.Population
            };
            AddMaintenance(state, city, result);
            result.UnitFoodConsumption = CountHomeSuppliedUnits(state, city);
            if (!HasOperationalGovernment(state, city)) return result;

            result.Food.Government = GovernmentFood;
            result.Gold.Government = GovernmentGold;
            result.Science.Government = GovernmentScience;
            result.Culture.Government = GovernmentCulture;
            AddDistrictProduction(state, city, result);
            return result;
        }

        private static int CountHomeSuppliedUnits(GameState state, CityState city)
        {
            var total = 0;
            for (var index = 0; index < state.Units.Count; index++)
            {
                var unit = state.Units[index];
                var tile = state.Tiles.Find(item => item.Id == unit.TileId);
                if (unit.OwnerId == city.OwnerId && tile != null && tile.CityId == city.Id &&
                    tile.ControllerId == city.OwnerId) total += UnitRules.FoodConsumption(unit.Type);
            }
            return total;
        }

        private static void AddDistrictProduction(GameState state, CityState city, CityEconomyBreakdown result)
        {
            for (var index = 0; index < state.Districts.Count; index++)
            {
                var district = state.Districts[index];
                if (district.CityId != city.Id || district.Type == DistrictType.Government ||
                    district.ControllerId != city.OwnerId || !district.IsOperational ||
                    district.RemainingConstructionTurns > 0 || district.AssignedCitizens <= 0)
                {
                    continue;
                }

                var resource = FindTileResource(state, district.TileId);
                var adjacencyBonus = CountAdjacencyBonus(state, city, district);
                switch (district.Type)
                {
                    case DistrictType.Agriculture:
                        result.Food.DistrictBase += AgricultureFood;
                        if (resource == TileResourceType.Food) result.Food.ResourceBonus += AgricultureResourceBonus;
                        break;
                    case DistrictType.Commerce:
                        result.Gold.DistrictBase += CommerceGold;
                        if (resource == TileResourceType.Commerce) result.Gold.ResourceBonus += CommerceResourceBonus;
                        result.Gold.AdjacencyBonus += adjacencyBonus;
                        break;
                    case DistrictType.Science:
                        result.Science.DistrictBase += ScienceResearch;
                        if (resource == TileResourceType.Science) result.Science.ResourceBonus += ScienceResourceBonus;
                        result.Science.AdjacencyBonus += adjacencyBonus;
                        break;
                    case DistrictType.Culture:
                        result.Culture.DistrictBase += CultureOutput;
                        if (resource == TileResourceType.Culture) result.Culture.ResourceBonus += CultureResourceBonus;
                        result.Culture.AdjacencyBonus += adjacencyBonus;
                        break;
                }
            }
        }

        private static void AddMaintenance(GameState state, CityState city, CityEconomyBreakdown result)
        {
            for (var index = 0; index < state.Units.Count; index++)
            {
                var unit = state.Units[index];
                var tile = state.Tiles.Find(item => item.Id == unit.TileId);
                var belongs = unit.HomeCityId.IsValid
                    ? unit.HomeCityId == city.Id
                    : tile != null && tile.CityId == city.Id;
                if (unit.OwnerId == city.OwnerId && belongs)
                    result.UnitUpkeep += MaintenanceResolver.UnitUpkeep(state.Units[index].Type);
            }
            for (var index = 0; index < state.Districts.Count; index++)
            {
                var district = state.Districts[index];
                if (district.CityId == city.Id && district.ControllerId == city.OwnerId &&
                    district.AssignedCitizens > 0 && district.RemainingConstructionTurns <= 0 &&
                    !district.IsMaintenanceSuspended)
                    result.FacilityUpkeep += MaintenanceResolver.DistrictUpkeep(district.Type);
            }
        }

        private static int CountAdjacencyBonus(GameState state, CityState city, DistrictState source)
        {
            if (source.Type != DistrictType.Commerce && source.Type != DistrictType.Science &&
                source.Type != DistrictType.Culture)
            {
                return 0;
            }

            var view = state.MapTopology == null ? null : state.MapTopology.FindView(city.Id);
            var sourcePlacement = FindPlacement(view, source.TileId);
            if (sourcePlacement == null) return 0;
            var sourceCoord = new HexCoord(sourcePlacement.LocalQ, sourcePlacement.LocalR);
            var count = 0;
            for (var index = 0; index < state.Districts.Count && count < 2; index++)
            {
                var candidate = state.Districts[index];
                if (candidate.Id == source.Id || candidate.CityId != city.Id || candidate.Type != source.Type ||
                    candidate.ControllerId != city.OwnerId || !candidate.IsOperational ||
                    candidate.RemainingConstructionTurns > 0 || candidate.AssignedCitizens <= 0)
                {
                    continue;
                }

                var placement = FindPlacement(view, candidate.TileId);
                if (placement != null && HexCoord.Distance(
                        sourceCoord, new HexCoord(placement.LocalQ, placement.LocalR)) == 1)
                {
                    count++;
                }
            }
            return count;
        }

        public static int AdjacencyBonusForDistrict(GameState state, DistrictState district)
        {
            if (state == null || district == null) return 0;
            var city = state.Cities.Find(item => item.Id == district.CityId);
            if (city == null || district.ControllerId != city.OwnerId || !district.IsOperational ||
                district.RemainingConstructionTurns > 0 || district.AssignedCitizens <= 0) return 0;
            return CountAdjacencyBonus(state, city, district);
        }

        public static int ResourceBonusForDistrict(GameState state, DistrictState district)
        {
            if (state == null || district == null || !district.IsOperational ||
                district.RemainingConstructionTurns > 0 || district.AssignedCitizens <= 0) return 0;
            var resource = FindTileResource(state, district.TileId);
            switch (district.Type)
            {
                case DistrictType.Agriculture:
                    return resource == TileResourceType.Food ? AgricultureResourceBonus : 0;
                case DistrictType.Commerce:
                    return resource == TileResourceType.Commerce ? CommerceResourceBonus : 0;
                case DistrictType.Science:
                    return resource == TileResourceType.Science ? ScienceResourceBonus : 0;
                case DistrictType.Culture:
                    return resource == TileResourceType.Culture ? CultureResourceBonus : 0;
                default:
                    return 0;
            }
        }

        private static CityTilePlacement FindPlacement(CityMapView view, EntityId tileId)
        {
            if (view == null || view.Tiles == null) return null;
            for (var index = 0; index < view.Tiles.Count; index++)
            {
                if (view.Tiles[index].TileId == tileId) return view.Tiles[index];
            }
            return null;
        }

        private static TileResourceType FindTileResource(GameState state, EntityId tileId)
        {
            for (var index = 0; index < state.Tiles.Count; index++)
            {
                if (state.Tiles[index].Id == tileId) return state.Tiles[index].ResourceType;
            }
            return TileResourceType.None;
        }

        private static bool HasOperationalGovernment(GameState state, CityState city)
        {
            for (var index = 0; index < state.Districts.Count; index++)
            {
                var district = state.Districts[index];
                if (district.CityId == city.Id &&
                    district.Type == DistrictType.Government &&
                    district.ControllerId == city.OwnerId &&
                    district.IsOperational &&
                    city.GovernmentCitizens > 0)
                {
                    return true;
                }
            }

            return false;
        }

    }
}
