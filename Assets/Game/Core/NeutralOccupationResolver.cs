using System;
using System.Collections.Generic;

namespace LittleCiv.Core
{
    public sealed class NeutralOccupationRecord
    {
        public EntityId CityId;
        public EntityId OccupyingPlayerId;
        public int RequiredStrength;
        public int GarrisonStrength;
        public int IndependenceProgress;
        public bool Rebelled;
        public EntityId RebelUnitId;
    }

    public static class NeutralOccupationResolver
    {
        public static int RequiredStrength(CityState city)
        {
            if (city == null) return 0;
            return city.NeutralSpecialization == NeutralCitySpecialization.Military
                ? Math.Max(0, city.Population)
                : Math.Max(0, (city.Population + 1) / 2);
        }

        public static int GarrisonStrength(GameState state, CityState city)
        {
            if (state == null || city == null || !city.OccupyingPlayerId.IsValid) return 0;
            var strength = 0;
            for (var index = 0; index < state.Units.Count; index++)
            {
                var unit = state.Units[index];
                if (unit.OwnerId != city.OccupyingPlayerId || unit.HitPoints <= 0) continue;
                var government = state.Districts.Find(item => item.CityId == city.Id &&
                    item.Type == DistrictType.Government);
                if (government != null && unit.TileId == government.TileId)
                    strength += UnitRules.Attack(unit.Type);
            }
            return strength;
        }

        public static List<NeutralOccupationRecord> Resolve(GameState state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            var result = new List<NeutralOccupationRecord>();
            var cities = state.Cities.FindAll(item => item.OccupyingPlayerId.IsValid);
            cities.Sort((left, right) => left.Id.CompareTo(right.Id));
            for (var index = 0; index < cities.Count; index++)
            {
                var city = cities[index];
                var required = RequiredStrength(city);
                var strength = GarrisonStrength(state, city);
                if (strength >= required)
                {
                    city.IndependenceProgress = 0;
                    result.Add(Record(city, required, strength, false, default));
                    continue;
                }
                city.IndependenceProgress++;
                if (city.IndependenceProgress < 2)
                {
                    result.Add(Record(city, required, strength, false, default));
                    continue;
                }
                var occupier = city.OccupyingPlayerId;
                var rebel = ReleaseAndCreateMilitia(state, city);
                result.Add(new NeutralOccupationRecord
                {
                    CityId = city.Id, OccupyingPlayerId = occupier,
                    RequiredStrength = required, GarrisonStrength = strength,
                    IndependenceProgress = 0, Rebelled = true, RebelUnitId = rebel
                });
            }
            return result;
        }

        private static EntityId ReleaseAndCreateMilitia(GameState state, CityState city)
        {
            var government = state.Districts.Find(item => item.CityId == city.Id &&
                item.Type == DistrictType.Government);
            if (government == null) return default;
            OccupationResolver.RestoreCityControl(state, city);
            var unit = new UnitState
            {
                Id = state.AllocateId(), OwnerId = city.OwnerId, HomeCityId = city.Id,
                TileId = government.TileId, Type = UnitType.Militia,
                HitPoints = UnitRules.MaximumHitPoints(UnitType.Militia),
                RemainingMovement = 0, CreatedTurn = state.TurnNumber
            };
            state.Units.Add(unit);
            return unit.Id;
        }

        private static NeutralOccupationRecord Record(CityState city,
            int required, int strength, bool rebelled, EntityId rebelUnitId) =>
            new NeutralOccupationRecord
            {
                CityId = city.Id, OccupyingPlayerId = city.OccupyingPlayerId,
                RequiredStrength = required, GarrisonStrength = strength,
                IndependenceProgress = city.IndependenceProgress,
                Rebelled = rebelled, RebelUnitId = rebelUnitId
            };
    }
}
