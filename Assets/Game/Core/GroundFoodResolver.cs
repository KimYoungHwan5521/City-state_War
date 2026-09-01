using System;
using System.Collections.Generic;

namespace LittleCiv.Core
{
    public sealed class GroundFoodReturn
    {
        public EntityId TileId;
        public EntityId CityId;
        public int Amount;
    }

    public static class GroundFoodResolver
    {
        public static EntityId DepositAfterCombat(GameState state, EntityId tileId, int amount)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (amount <= 0) return default;
            var tile = FindTile(state, tileId);
            if (tile == null) throw new InvalidOperationException("Combat food was dropped on an unknown tile.");

            tile.GroundFood += amount;
            var occupant = FindOccupyingOwner(state, tileId);
            var city = FindCity(state, tile.CityId);
            if (city != null && occupant == city.OwnerId)
            {
                city.StoredFood += tile.GroundFood;
                tile.GroundFood = 0;
                tile.GroundFoodOwnerId = default;
                tile.GroundFoodReturnTurn = 0;
                return city.OwnerId;
            }
            if (occupant.IsValid)
            {
                tile.GroundFoodOwnerId = occupant;
                tile.GroundFoodReturnTurn = 0;
            }
            else if (tile.IsSharedBoundary)
            {
                tile.GroundFoodOwnerId = default;
                tile.GroundFoodReturnTurn = 0;
            }
            else
            {
                tile.GroundFoodOwnerId = tile.ControllerId;
                tile.GroundFoodReturnTurn = tile.ControllerId.IsValid ? state.TurnNumber + 2 : 0;
            }
            return tile.GroundFoodOwnerId;
        }

        public static List<GroundFoodReturn> ReturnEligibleFood(GameState state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            var result = new List<GroundFoodReturn>();
            var tiles = new List<TileState>(state.Tiles);
            tiles.Sort((left, right) => left.Id.CompareTo(right.Id));
            for (var index = 0; index < tiles.Count; index++)
            {
                var tile = tiles[index];
                if (tile.GroundFood <= 0 || tile.GroundFoodReturnTurn <= 0 ||
                    state.TurnNumber < tile.GroundFoodReturnTurn || HasUnit(state, tile.Id)) continue;
                var city = FindCity(state, tile.CityId);
                if (city == null || city.OwnerId != tile.GroundFoodOwnerId) continue;
                var amount = tile.GroundFood;
                city.StoredFood += amount;
                tile.GroundFood = 0;
                tile.GroundFoodOwnerId = default;
                tile.GroundFoodReturnTurn = 0;
                result.Add(new GroundFoodReturn { TileId = tile.Id, CityId = city.Id, Amount = amount });
            }
            return result;
        }

        public static void ReconcileVacatedOwnership(GameState state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            for (var index = 0; index < state.Tiles.Count; index++)
            {
                var tile = state.Tiles[index];
                if (tile.GroundFood <= 0 || tile.IsSharedBoundary) continue;
                var city = FindCity(state, tile.CityId);
                if (city != null && HasUnitOwnedBy(state, tile.Id, city.OwnerId))
                {
                    city.StoredFood += tile.GroundFood;
                    tile.GroundFood = 0;
                    tile.GroundFoodOwnerId = default;
                    tile.GroundFoodReturnTurn = 0;
                    continue;
                }
                if (tile.GroundFoodReturnTurn > 0) continue;
                if (tile.GroundFoodOwnerId.IsValid &&
                    HasUnitOwnedBy(state, tile.Id, tile.GroundFoodOwnerId)) continue;
                if (city == null) continue;
                tile.GroundFoodOwnerId = city.OwnerId;
                tile.GroundFoodReturnTurn = state.TurnNumber + 2;
            }
        }

        private static EntityId FindOccupyingOwner(GameState state, EntityId tileId)
        {
            EntityId owner = default;
            for (var index = 0; index < state.Units.Count; index++)
            {
                var unit = state.Units[index];
                if (unit.TileId != tileId || unit.HitPoints <= 0) continue;
                if (!owner.IsValid || unit.OwnerId.CompareTo(owner) < 0) owner = unit.OwnerId;
            }
            return owner;
        }

        private static bool HasUnit(GameState state, EntityId tileId)
        {
            for (var index = 0; index < state.Units.Count; index++)
                if (state.Units[index].TileId == tileId && state.Units[index].HitPoints > 0) return true;
            return false;
        }

        private static bool HasUnitOwnedBy(GameState state, EntityId tileId, EntityId ownerId)
        {
            for (var index = 0; index < state.Units.Count; index++)
                if (state.Units[index].TileId == tileId && state.Units[index].OwnerId == ownerId &&
                    state.Units[index].HitPoints > 0) return true;
            return false;
        }

        private static TileState FindTile(GameState state, EntityId id)
        {
            for (var index = 0; index < state.Tiles.Count; index++)
                if (state.Tiles[index].Id == id) return state.Tiles[index];
            return null;
        }

        private static CityState FindCity(GameState state, EntityId id)
        {
            for (var index = 0; index < state.Cities.Count; index++)
                if (state.Cities[index].Id == id) return state.Cities[index];
            return null;
        }
    }
}
