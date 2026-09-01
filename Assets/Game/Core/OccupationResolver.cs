using System;
using System.Collections.Generic;

namespace LittleCiv.Core
{
    public sealed class OccupationResult
    {
        public EntityId OccupyingPlayerId;
        public EntityId TileId;
        public EntityId DistrictId;
        public DistrictType DistrictType;
        public bool DistrictOccupied;
        public bool ConquestVictoryTriggered;
        public bool PillageRewardGranted;
        public int PillagePrimaryReward;
        public int PillageFoodReward;
    }

    public static class OccupationResolver
    {
        public static List<EntityId> ReleaseVacatedDistricts(GameState state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            var released = new List<EntityId>();
            for (var i = 0; i < state.Districts.Count; i++)
            {
                var district = state.Districts[i];
                var city = FindCity(state, district.CityId);
                if (city == null || district.ControllerId == city.OwnerId ||
                    HasUnitOwnedBy(state, district.TileId, district.ControllerId)) continue;
                district.ControllerId = city.OwnerId;
                district.IsOperational = false;
                district.IsPillaged = district.Type != DistrictType.Government;
                district.RemainingRepairTurns = 0;
                var tile = FindTile(state, district.TileId);
                if (tile != null) tile.ControllerId = city.OwnerId;
                released.Add(district.Id);
            }
            return released;
        }

        public static OccupationResult Resolve(
            GameState state,
            EntityId occupyingPlayerId,
            EntityId tileId)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            var result = new OccupationResult
            {
                OccupyingPlayerId = occupyingPlayerId,
                TileId = tileId
            };
            var district = FindDistrict(state, tileId);
            if (district == null || district.ControllerId == occupyingPlayerId) return result;
            if (HasEnemyUnit(state, tileId, occupyingPlayerId)) return result;

            var city = FindCity(state, district.CityId);
            var grantsPillageReward = city != null && occupyingPlayerId != city.OwnerId &&
                                      district.Type != DistrictType.Government && !district.IsPillaged;
            district.ControllerId = occupyingPlayerId;
            district.IsOperational = false;
            if (city != null && occupyingPlayerId != city.OwnerId)
            {
                district.IsPillaged = true;
                district.RemainingRepairTurns = 0;
            }
            else if (city != null && !district.IsOperational &&
                     district.RemainingConstructionTurns <= 0 && district.AssignedCitizens > 0)
            {
                // Also upgrades recaptured states created before explicit pillage data existed.
                district.IsPillaged = true;
            }
            var tile = FindTile(state, tileId);
            if (tile != null) tile.ControllerId = occupyingPlayerId;
            result.DistrictId = district.Id;
            result.DistrictType = district.Type;
            result.DistrictOccupied = true;
            if (grantsPillageReward)
            {
                ApplyPillageReward(state, district, city, occupyingPlayerId, result);
            }
            if (district.Type == DistrictType.Government && !state.IsGameOver)
            {
                state.Victory = VictoryType.Conquest;
                state.WinnerId = occupyingPlayerId;
                result.ConquestVictoryTriggered = true;
            }
            return result;
        }

        private static void ApplyPillageReward(
            GameState state,
            DistrictState district,
            CityState victimCity,
            EntityId occupyingPlayerId,
            OccupationResult result)
        {
            var receivingCity = FindReceivingCity(state, occupyingPlayerId, district.TileId);
            if (receivingCity == null) return;
            result.PillageRewardGranted = true;
            switch (district.Type)
            {
                case DistrictType.Agriculture:
                    result.PillageFoodReward = Math.Min(6, Math.Max(0, victimCity.StoredFood));
                    victimCity.StoredFood -= result.PillageFoodReward;
                    receivingCity.StoredFood += result.PillageFoodReward;
                    break;
                case DistrictType.Commerce:
                    result.PillagePrimaryReward = 6;
                    receivingCity.Gold += 6;
                    break;
                case DistrictType.Science:
                    result.PillagePrimaryReward = 4;
                    receivingCity.ResearchPoints += 4;
                    break;
                case DistrictType.Culture:
                    result.PillagePrimaryReward = 4;
                    AddCultureInfluence(victimCity, occupyingPlayerId, 4);
                    break;
                case DistrictType.Military:
                    result.PillagePrimaryReward = 3;
                    receivingCity.Gold += 3;
                    result.PillageFoodReward = Math.Min(3, Math.Max(0, victimCity.StoredFood));
                    victimCity.StoredFood -= result.PillageFoodReward;
                    if (result.PillageFoodReward > 0)
                        GroundFoodResolver.DepositAfterCombat(state, district.TileId, result.PillageFoodReward);
                    break;
                case DistrictType.NuclearFacility:
                    result.PillagePrimaryReward = 10;
                    receivingCity.Gold += 10;
                    break;
            }
        }

        private static CityState FindReceivingCity(GameState state, EntityId playerId, EntityId occupiedTileId)
        {
            for (var i = 0; i < state.Units.Count; i++)
            {
                var unit = state.Units[i];
                if (unit.TileId != occupiedTileId || unit.OwnerId != playerId || !unit.HomeCityId.IsValid) continue;
                var home = FindCity(state, unit.HomeCityId);
                if (home != null && home.OwnerId == playerId) return home;
            }
            CityState first = null;
            for (var i = 0; i < state.Cities.Count; i++)
            {
                var candidate = state.Cities[i];
                if (candidate.OwnerId != playerId) continue;
                if (first == null || candidate.Id.CompareTo(first.Id) < 0) first = candidate;
            }
            return first;
        }

        private static void AddCultureInfluence(CityState city, EntityId cultureOwnerId, int amount)
        {
            if (city.CultureInfluences == null) city.CultureInfluences = new List<CultureInfluenceState>();
            var influence = city.CultureInfluences.Find(item => item.CultureOwnerId == cultureOwnerId);
            if (influence == null)
            {
                influence = new CultureInfluenceState { CultureOwnerId = cultureOwnerId };
                city.CultureInfluences.Add(influence);
            }
            influence.ConversionProgress += amount;
        }

        private static DistrictState FindDistrict(GameState state, EntityId tileId)
        {
            for (var i = 0; i < state.Districts.Count; i++)
            {
                if (state.Districts[i].TileId == tileId) return state.Districts[i];
            }
            return null;
        }

        private static TileState FindTile(GameState state, EntityId tileId)
        {
            for (var i = 0; i < state.Tiles.Count; i++)
            {
                if (state.Tiles[i].Id == tileId) return state.Tiles[i];
            }
            return null;
        }

        private static CityState FindCity(GameState state, EntityId cityId)
        {
            for (var i = 0; i < state.Cities.Count; i++)
                if (state.Cities[i].Id == cityId) return state.Cities[i];
            return null;
        }

        private static bool HasEnemyUnit(GameState state, EntityId tileId, EntityId playerId)
        {
            for (var i = 0; i < state.Units.Count; i++)
            {
                var unit = state.Units[i];
                if (unit.TileId == tileId && unit.OwnerId != playerId && unit.HitPoints > 0) return true;
            }
            return false;
        }

        private static bool HasUnitOwnedBy(GameState state, EntityId tileId, EntityId playerId)
        {
            for (var i = 0; i < state.Units.Count; i++)
                if (state.Units[i].TileId == tileId && state.Units[i].OwnerId == playerId &&
                    state.Units[i].HitPoints > 0) return true;
            return false;
        }
    }
}
