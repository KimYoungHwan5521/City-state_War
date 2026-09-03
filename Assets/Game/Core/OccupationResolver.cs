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
        public static List<OccupationResult> ResolveStandingPillages(GameState state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            var results = new List<OccupationResult>();
            for (var index = 0; index < state.Districts.Count; index++)
            {
                var district = state.Districts[index];
                var city = FindCity(state, district.CityId);
                if (city == null || city.OccupyingPlayerId.IsValid ||
                    district.Type == DistrictType.Government || district.IsPillaged ||
                    district.RemainingConstructionTurns > 0 || district.ControllerId == city.OwnerId) continue;
                var occupier = state.Units.Find(item => item.TileId == district.TileId &&
                    item.OwnerId == district.ControllerId && item.HitPoints > 0 && item.RemainingMovement > 0);
                if (occupier == null) continue;
                district.IsPillaged = true;
                var result = new OccupationResult
                {
                    OccupyingPlayerId = occupier.OwnerId, TileId = district.TileId,
                    DistrictId = district.Id, DistrictType = district.Type,
                    DistrictOccupied = true
                };
                ApplyPillageReward(state, district, city, occupier.OwnerId, result);
                var owner = state.Players.Find(item => item.Id == city.OwnerId);
                if (owner != null && owner.Slot == PlayerSlot.Neutral)
                    NeutralCityRules.SetFavor(city, occupier.OwnerId, -10);
                results.Add(result);
            }
            return results;
        }

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
                if (district.Type == DistrictType.Government && city.OccupyingPlayerId.IsValid &&
                    city.OccupyingPlayerId == district.ControllerId) continue;
                district.ControllerId = city.OwnerId;
                district.IsOperational = district.RemainingConstructionTurns <= 0 &&
                    !district.IsPillaged && !district.IsMaintenanceSuspended;
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
            EntityId tileId,
            bool canPillage = true)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            var result = new OccupationResult
            {
                OccupyingPlayerId = occupyingPlayerId,
                TileId = tileId
            };
            if (NeutralLevyResolver.IsProtectedCityTile(state, occupyingPlayerId, tileId)) return result;
            var district = FindDistrict(state, tileId);
            if (district == null || district.ControllerId == occupyingPlayerId) return result;
            if (HasEnemyUnit(state, tileId, occupyingPlayerId)) return result;

            var city = FindCity(state, district.CityId);
            var cityAlreadyOccupied = city != null && city.OccupyingPlayerId.IsValid;
            var grantsPillageReward = city != null && !cityAlreadyOccupied && occupyingPlayerId != city.OwnerId &&
                                      district.Type != DistrictType.Government && !district.IsPillaged &&
                                      district.RemainingConstructionTurns <= 0 && canPillage;
            district.ControllerId = occupyingPlayerId;
            district.IsOperational = false;
            if (city != null && occupyingPlayerId != city.OwnerId)
            {
                district.IsPillaged = grantsPillageReward;
                district.RemainingRepairTurns = 0;
            }
            var tile = FindTile(state, tileId);
            if (tile != null) tile.ControllerId = occupyingPlayerId;
            result.DistrictId = district.Id;
            result.DistrictType = district.Type;
            result.DistrictOccupied = true;
            if (grantsPillageReward)
            {
                ApplyPillageReward(state, district, city, occupyingPlayerId, result);
                var owner = state.Players.Find(item => item.Id == city.OwnerId);
                if (owner != null && owner.Slot == PlayerSlot.Neutral)
                    NeutralCityRules.SetFavor(city, occupyingPlayerId, -10);
            }
            if (district.Type == DistrictType.Government && !state.IsGameOver)
            {
                if (IsNeutralCity(state, city))
                {
                    if (occupyingPlayerId == city.OwnerId)
                        RestoreCityControl(state, city);
                    else
                        city.OccupyingPlayerId = occupyingPlayerId;
                    city.IndependenceProgress = 0;
                }
                else
                {
                    // Player-capital conquest is resolved after all simultaneous movement,
                    // so reciprocal captures exchange control and continue the match.
                    result.ConquestVictoryTriggered = false;
                }
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
            CultureConversionResolver.ApplyForeignInfluence(city, cultureOwnerId, amount);
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

        private static bool IsNeutralCity(GameState state, CityState city)
        {
            if (city == null) return false;
            var owner = state.Players.Find(item => item.Id == city.OwnerId);
            return owner != null && owner.Slot == PlayerSlot.Neutral;
        }

        public static void RestoreCityControl(GameState state, CityState city)
        {
            if (state == null || city == null) return;
            city.OccupyingPlayerId = default;
            city.IndependenceProgress = 0;
            for (var index = 0; index < state.Districts.Count; index++)
            {
                var district = state.Districts[index];
                if (district.CityId != city.Id) continue;
                district.ControllerId = city.OwnerId;
                district.IsOperational = district.RemainingConstructionTurns <= 0 &&
                    !district.IsPillaged && !district.IsMaintenanceSuspended &&
                    (district.Type == DistrictType.Government || district.AssignedCitizens > 0);
                var tile = FindTile(state, district.TileId);
                if (tile != null) tile.ControllerId = city.OwnerId;
            }
        }
    }
}
