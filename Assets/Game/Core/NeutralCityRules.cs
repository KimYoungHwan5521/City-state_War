using System;
using System.Collections.Generic;

namespace LittleCiv.Core
{
    public static class NeutralCityRules
    {
        public static NeutralDevelopmentStage DevelopmentStage(GameState state, CityState city)
        {
            var count = OperationalSpecializationDistricts(state, city);
            if (count >= 3) return NeutralDevelopmentStage.Late;
            if (count == 2) return NeutralDevelopmentStage.Middle;
            return NeutralDevelopmentStage.Early;
        }

        public static int OperationalSpecializationDistricts(GameState state, CityState city)
        {
            if (state == null || city == null) return 0;
            var type = DistrictTypeFor(city.NeutralSpecialization);
            if (type == DistrictType.Government) return 0;
            var count = 0;
            for (var index = 0; index < state.Districts.Count; index++)
            {
                var district = state.Districts[index];
                if (district.CityId == city.Id && district.Type == type &&
                    district.ControllerId == city.OwnerId && district.IsOperational &&
                    !district.IsPillaged && !district.IsMaintenanceSuspended &&
                    district.AssignedCitizens > 0 && district.RemainingConstructionTurns <= 0)
                    count++;
            }
            return count;
        }

        public static int Favor(CityState city, EntityId playerId)
        {
            var relation = FindRelation(city, playerId);
            if (relation == null) return 0;
            var maximum = city.CultureSubjectToId == playerId ? 4 : 3;
            return Math.Max(-10, Math.Min(maximum, relation.Favor));
        }

        public static NeutralRelationState GetOrCreateRelation(CityState city, EntityId playerId)
        {
            if (city == null) throw new ArgumentNullException(nameof(city));
            if (!playerId.IsValid) throw new ArgumentException("Player ID must be valid.", nameof(playerId));
            if (city.NeutralRelations == null) city.NeutralRelations = new List<NeutralRelationState>();
            var relation = FindRelation(city, playerId);
            if (relation != null) return relation;
            relation = new NeutralRelationState { PlayerId = playerId };
            city.NeutralRelations.Add(relation);
            city.NeutralRelations.Sort((left, right) => left.PlayerId.CompareTo(right.PlayerId));
            return relation;
        }

        public static void SetFavor(CityState city, EntityId playerId, int favor)
        {
            if (favor <= -3 && city.CultureSubjectToId == playerId)
                city.CultureSubjectToId = default;
            var maximum = city.CultureSubjectToId == playerId ? 4 : 3;
            GetOrCreateRelation(city, playerId).Favor = Math.Max(-10, Math.Min(maximum, favor));
        }

        public static void RecoverHostileRelations(GameState state)
        {
            if (state == null || state.TurnNumber % 4 != 0) return;
            for (var cityIndex = 0; cityIndex < state.Cities.Count; cityIndex++)
            {
                var city = state.Cities[cityIndex];
                var owner = state.Players.Find(item => item.Id == city.OwnerId);
                if (owner == null || owner.Slot != PlayerSlot.Neutral || city.NeutralRelations == null) continue;
                for (var relationIndex = 0; relationIndex < city.NeutralRelations.Count; relationIndex++)
                    if (city.NeutralRelations[relationIndex].Favor <= -3)
                        city.NeutralRelations[relationIndex].Favor++;
            }
        }

        public static DistrictType DistrictTypeFor(NeutralCitySpecialization specialization)
        {
            switch (specialization)
            {
                case NeutralCitySpecialization.Military: return DistrictType.Military;
                case NeutralCitySpecialization.Science: return DistrictType.Science;
                case NeutralCitySpecialization.Culture: return DistrictType.Culture;
                case NeutralCitySpecialization.Commerce: return DistrictType.Commerce;
                default: return DistrictType.Government;
            }
        }

        private static NeutralRelationState FindRelation(CityState city, EntityId playerId)
        {
            return city?.NeutralRelations?.Find(item => item.PlayerId == playerId);
        }
    }
}
