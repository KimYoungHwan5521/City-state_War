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
            if (district.Type == DistrictType.Government && !state.IsGameOver)
            {
                state.Victory = VictoryType.Conquest;
                state.WinnerId = occupyingPlayerId;
                result.ConquestVictoryTriggered = true;
            }
            return result;
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
