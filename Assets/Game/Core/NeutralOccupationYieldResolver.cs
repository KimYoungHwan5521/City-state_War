using System;
using System.Collections.Generic;

namespace LittleCiv.Core
{
    public sealed class NeutralOccupationYieldRecord
    {
        public EntityId OccupiedCityId;
        public EntityId OccupyingPlayerId;
        public EntityId ReceivingCityId;
        public TileResourceType ResourceType;
        public int Amount;
    }

    public static class NeutralOccupationYieldResolver
    {
        public static List<NeutralOccupationYieldRecord> Collect(GameState state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            var result = new List<NeutralOccupationYieldRecord>();
            var occupied = state.Cities.FindAll(item => item.OccupyingPlayerId.IsValid);
            occupied.Sort((left, right) => left.Id.CompareTo(right.Id));
            for (var index = 0; index < occupied.Count; index++)
            {
                var city = occupied[index];
                var receiver = FindReceivingCity(state, city.OccupyingPlayerId);
                if (receiver == null) continue;
                var production = CityEconomyResolver.CalculateOccupiedProduction(state, city);
                Add(result, city, receiver, TileResourceType.Food, production.Food.Total);
                Add(result, city, receiver, TileResourceType.Commerce, production.Gold.Total);
                Add(result, city, receiver, TileResourceType.Science, production.Science.Total);
                Add(result, city, receiver, TileResourceType.Culture, production.Culture.Total);
            }
            return result;
        }

        private static void Add(List<NeutralOccupationYieldRecord> result, CityState occupied,
            CityState receiver, TileResourceType resource, int amount)
        {
            if (amount <= 0) return;
            switch (resource)
            {
                case TileResourceType.Food: receiver.LastFoodProduction += amount; break;
                case TileResourceType.Commerce:
                    receiver.Gold += amount; receiver.LastGoldProduction += amount; break;
                case TileResourceType.Science:
                    receiver.ResearchPoints += amount; receiver.LastScienceProduction += amount; break;
                case TileResourceType.Culture: receiver.LastCultureProduction += amount; break;
            }
            result.Add(new NeutralOccupationYieldRecord
            {
                OccupiedCityId = occupied.Id, OccupyingPlayerId = occupied.OccupyingPlayerId,
                ReceivingCityId = receiver.Id, ResourceType = resource, Amount = amount
            });
        }

        private static CityState FindReceivingCity(GameState state, EntityId ownerId)
        {
            CityState result = null;
            for (var index = 0; index < state.Cities.Count; index++)
            {
                var city = state.Cities[index];
                if (city.OwnerId == ownerId && (result == null || city.Id.CompareTo(result.Id) < 0))
                    result = city;
            }
            return result;
        }
    }
}
