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

            result.Food.Government = Math.Max(0, GovernmentFood + city.TestGovernmentFoodBonus);
            result.Gold.Government = Math.Max(0, GovernmentGold + city.TestGovernmentGoldBonus);
            result.Science.Government = Math.Max(0, GovernmentScience + city.TestGovernmentScienceBonus);
            result.Culture.Government = Math.Max(0, GovernmentCulture + city.TestGovernmentCultureBonus);
            AddDistrictProduction(state, city, result);
            ApplyCityResearchMultipliers(state, city, result);
            return result;
        }

        public static CityEconomyBreakdown CalculateOccupiedProduction(GameState state, CityState city)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (city == null) throw new ArgumentNullException(nameof(city));
            var controllers = new EntityId[state.Districts.Count];
            var operational = new bool[state.Districts.Count];
            for (var index = 0; index < state.Districts.Count; index++)
            {
                controllers[index] = state.Districts[index].ControllerId;
                operational[index] = state.Districts[index].IsOperational;
                if (state.Districts[index].CityId != city.Id) continue;
                state.Districts[index].ControllerId = city.OwnerId;
                state.Districts[index].IsOperational = state.Districts[index].RemainingConstructionTurns <= 0 &&
                    !state.Districts[index].IsPillaged && !state.Districts[index].IsMaintenanceSuspended &&
                    (state.Districts[index].Type == DistrictType.Government ||
                     state.Districts[index].AssignedCitizens > 0);
            }
            var result = CalculateBreakdown(state, city);
            for (var index = 0; index < state.Districts.Count; index++)
            {
                state.Districts[index].ControllerId = controllers[index];
                state.Districts[index].IsOperational = operational[index];
            }
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
                var player = state.Players.Find(item => item.Id == city.OwnerId);
                switch (district.Type)
                {
                    case DistrictType.Agriculture:
                        var agricultureBase = AgricultureFood;
                        var agricultureResource = resource == TileResourceType.Food ? AgricultureResourceBonus : 0;
                        var agricultureResearch = HasResearch(state, city, player, ResearchType.Fertilizer) ? 1 : 0;
                        result.Food.DistrictBase += agricultureBase;
                        result.Food.ResourceBonus += agricultureResource;
                        result.Food.ResearchBonus += agricultureResearch;
                        var boosted = HasResearch(state, city, player, ResearchType.MechanizedAgriculture) ||
                                      (HasResearch(state, city, player, ResearchType.Irrigation) && district.AssignedCitizens >= 2);
                        if (boosted)
                        {
                            var subtotal = agricultureBase + agricultureResource + agricultureResearch;
                            result.Food.StaffingBonus += (subtotal * 150 / 100) - subtotal;
                        }
                        break;
                    case DistrictType.Commerce:
                        result.Gold.DistrictBase += CommerceGold;
                        if (resource == TileResourceType.Commerce) result.Gold.ResourceBonus += CommerceResourceBonus;
                        result.Gold.AdjacencyBonus += adjacencyBonus;
                        if (HasResearch(state, city, player, ResearchType.Currency)) result.Gold.ResearchBonus++;
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
                        if (HasResearch(state, city, player, ResearchType.Printing)) result.Culture.ResearchBonus++;
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
            var owner = state.Players.Find(item => item.Id == city.OwnerId);
            var perNeighbor = source.Type == DistrictType.Commerce &&
                              HasResearch(state, city, owner, ResearchType.Finance) ? 2 : 1;
            return count * perNeighbor;
        }

        private static void ApplyCityResearchMultipliers(
            GameState state, CityState city, CityEconomyBreakdown result)
        {
            var player = state.Players.Find(item => item.Id == city.OwnerId);
            if (HasResearch(state, city, player, ResearchType.EconomicAdministration))
                result.Gold.MultiplierBonus = QuarterBonus(result.Gold);
            if (HasResearch(state, city, player, ResearchType.MassMedia))
                result.Culture.MultiplierBonus = QuarterBonus(result.Culture);
        }

        private static int QuarterBonus(YieldBreakdown value)
        {
            var before = value.Government + value.DistrictBase + value.ResourceBonus +
                         value.AdjacencyBonus + value.ResearchBonus + value.StaffingBonus;
            return (before * 125 / 100) - before;
        }

        private static bool HasResearch(PlayerState player, ResearchType type)
        {
            return player != null && player.CompletedResearch != null &&
                   player.CompletedResearch.Contains(type);
        }

        private static bool HasResearch(GameState state, CityState city, PlayerState player, ResearchType type)
        {
            return player != null && player.Slot == PlayerSlot.Neutral
                ? NeutralResearchResolver.HasResearch(city, type)
                : HasResearch(player, type);
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
