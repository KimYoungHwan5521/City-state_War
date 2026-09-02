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
                var resource = ResourceFor(city.NeutralSpecialization);
                if (receiver == null || resource == TileResourceType.None) continue;
                var amount = 2 * (int)NeutralCityRules.DevelopmentStage(state, city);
                switch (resource)
                {
                    case TileResourceType.Science:
                        receiver.ResearchPoints += amount;
                        receiver.LastScienceProduction += amount;
                        break;
                    case TileResourceType.Culture:
                        receiver.LastCultureProduction += amount;
                        break;
                    case TileResourceType.Commerce:
                        receiver.Gold += amount;
                        receiver.LastGoldProduction += amount;
                        break;
                }
                result.Add(new NeutralOccupationYieldRecord
                {
                    OccupiedCityId = city.Id, OccupyingPlayerId = city.OccupyingPlayerId,
                    ReceivingCityId = receiver.Id, ResourceType = resource, Amount = amount
                });
            }
            return result;
        }

        private static TileResourceType ResourceFor(NeutralCitySpecialization specialization)
        {
            switch (specialization)
            {
                case NeutralCitySpecialization.Science: return TileResourceType.Science;
                case NeutralCitySpecialization.Culture: return TileResourceType.Culture;
                case NeutralCitySpecialization.Commerce: return TileResourceType.Commerce;
                default: return TileResourceType.None;
            }
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
