using System;

namespace LittleCiv.Core
{
    public static class CityEconomyResolver
    {
        public const int GovernmentFood = 4;
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
                ResetProduction(city);
                if (!HasOperationalGovernment(state, city)) continue;

                city.LastFoodProduction = GovernmentFood;
                city.LastGoldProduction = GovernmentGold;
                city.LastScienceProduction = GovernmentScience;
                city.LastCultureProduction = GovernmentCulture;
                AddDistrictProduction(state, city);
                city.Gold += city.LastGoldProduction;
                city.ResearchPoints += city.LastScienceProduction;
            }
        }

        private static void AddDistrictProduction(GameState state, CityState city)
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
                        city.LastFoodProduction += AgricultureFood +
                            (resource == TileResourceType.Food ? AgricultureResourceBonus : 0);
                        break;
                    case DistrictType.Commerce:
                        city.LastGoldProduction += CommerceGold +
                            (resource == TileResourceType.Commerce ? CommerceResourceBonus : 0) +
                            adjacencyBonus;
                        break;
                    case DistrictType.Science:
                        city.LastScienceProduction += ScienceResearch +
                            (resource == TileResourceType.Science ? ScienceResourceBonus : 0) +
                            adjacencyBonus;
                        break;
                    case DistrictType.Culture:
                        city.LastCultureProduction += CultureOutput +
                            (resource == TileResourceType.Culture ? CultureResourceBonus : 0) +
                            adjacencyBonus;
                        break;
                }
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

        private static void ResetProduction(CityState city)
        {
            city.LastFoodProduction = 0;
            city.LastGoldProduction = 0;
            city.LastScienceProduction = 0;
            city.LastCultureProduction = 0;
        }
    }
}
