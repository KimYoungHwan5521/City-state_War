using System;
using System.Collections.Generic;

namespace LittleCiv.Core
{
    public enum UnitFoodSource
    {
        None = 0,
        Personal = 1,
        Ground = 2,
        OccupiedAgriculture = 3,
        SupplyUnit = 4
    }

    public sealed class UnitFoodConsumptionRecord
    {
        public EntityId UnitId;
        public UnitFoodSource Source;
        public int Amount;
    }

    public sealed class UnitFoodConsumptionResult
    {
        public readonly List<EntityId> SuppliedUnitIds = new List<EntityId>();
        public readonly List<EntityId> UnsuppliedUnitIds = new List<EntityId>();
        public readonly List<UnitFoodConsumptionRecord> Records = new List<UnitFoodConsumptionRecord>();
    }

    public static class UnitFoodResolver
    {
        public static bool TryLoad(GameState state, GameCommand command, out int loadedFood)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (command == null) throw new ArgumentNullException(nameof(command));
            loadedFood = 0;
            if (command.PrimaryValue <= 0) return false;

            var unit = FindUnit(state, command.SubjectId);
            var city = FindCity(state, command.TargetId);
            if (unit == null || city == null || unit.OwnerId != command.PlayerId ||
                city.OwnerId != command.PlayerId || city.StoredFood <= 0) return false;
            var tile = FindTile(state, unit.TileId);
            if (tile == null || tile.CityId != city.Id || tile.ControllerId != command.PlayerId) return false;

            var freeCapacity = UnitRules.FoodCapacity(unit.Type) - unit.CarriedFood;
            if (freeCapacity <= 0) return false;
            loadedFood = Math.Min(command.PrimaryValue, Math.Min(freeCapacity, city.StoredFood));
            if (loadedFood <= 0) return false;
            city.StoredFood -= loadedFood;
            unit.CarriedFood += loadedFood;
            return true;
        }

        public static UnitFoodConsumptionResult Consume(GameState state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            var result = new UnitFoodConsumptionResult();
            var units = new List<UnitState>(state.Units);
            units.Sort((left, right) => left.Id.CompareTo(right.Id));
            for (var index = 0; index < units.Count; index++)
            {
                var unit = units[index];
                if (unit.CreatedTurn == state.TurnNumber) continue;
                var required = UnitRules.FoodConsumption(unit.Type);
                var source = ConsumeGroundFood(state, unit, required)
                    ? UnitFoodSource.Ground
                    : IsSuppliedByOccupiedAgriculture(state, unit)
                        ? UnitFoodSource.OccupiedAgriculture
                        : ConsumePersonalFood(unit, required)
                            ? UnitFoodSource.Personal
                            : UnitFoodSource.None;
                if (source != UnitFoodSource.None)
                {
                    result.SuppliedUnitIds.Add(unit.Id);
                    result.Records.Add(new UnitFoodConsumptionRecord
                    {
                        UnitId = unit.Id,
                        Source = source,
                        Amount = required
                    });
                }
                else
                {
                    result.UnsuppliedUnitIds.Add(unit.Id);
                }
            }
            return result;
        }

        private static bool ConsumeGroundFood(GameState state, UnitState unit, int amount)
        {
            var tile = FindTile(state, unit.TileId);
            if (tile == null || tile.GroundFood < amount) return false;
            if (tile.GroundFoodOwnerId.IsValid && tile.GroundFoodOwnerId != unit.OwnerId) return false;
            if (!tile.GroundFoodOwnerId.IsValid && !tile.IsSharedBoundary) return false;
            tile.GroundFood -= amount;
            if (tile.GroundFood > 0)
            {
                tile.GroundFoodOwnerId = unit.OwnerId;
                tile.GroundFoodReturnTurn = 0;
            }
            else
            {
                tile.GroundFoodOwnerId = default;
                tile.GroundFoodReturnTurn = 0;
            }
            return true;
        }

        private static bool IsSuppliedByOccupiedAgriculture(GameState state, UnitState unit)
        {
            for (var districtIndex = 0; districtIndex < state.Districts.Count; districtIndex++)
            {
                var district = state.Districts[districtIndex];
                if (district.TileId != unit.TileId || district.Type != DistrictType.Agriculture ||
                    district.ControllerId != unit.OwnerId) continue;
                var city = FindCity(state, district.CityId);
                return city != null && city.OwnerId != unit.OwnerId;
            }
            return false;
        }

        private static bool ConsumePersonalFood(UnitState unit, int amount)
        {
            if (unit.CarriedFood < amount) return false;
            unit.CarriedFood -= amount;
            return true;
        }

        public static bool TryTransfer(GameState state, GameCommand command, out int transferredFood)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (command == null) throw new ArgumentNullException(nameof(command));
            transferredFood = 0;
            if (command.PrimaryValue <= 0 || command.SubjectId == command.TargetId) return false;

            var supplier = FindUnit(state, command.SubjectId);
            var receiver = FindUnit(state, command.TargetId);
            if (supplier == null || receiver == null || supplier.OwnerId != command.PlayerId ||
                receiver.OwnerId != command.PlayerId || supplier.TileId != receiver.TileId ||
                !UnitRules.IsSupply(supplier.Type) || UnitRules.IsSupply(receiver.Type) ||
                supplier.CarriedFood <= 0) return false;

            var freeCapacity = UnitRules.FoodCapacity(receiver.Type) - receiver.CarriedFood;
            if (freeCapacity <= 0) return false;
            transferredFood = Math.Min(command.PrimaryValue, Math.Min(freeCapacity, supplier.CarriedFood));
            if (transferredFood <= 0) return false;
            supplier.CarriedFood -= transferredFood;
            receiver.CarriedFood += transferredFood;
            return true;
        }

        private static UnitState FindUnit(GameState state, EntityId id)
        {
            for (var index = 0; index < state.Units.Count; index++)
                if (state.Units[index].Id == id) return state.Units[index];
            return null;
        }

        private static CityState FindCity(GameState state, EntityId id)
        {
            for (var index = 0; index < state.Cities.Count; index++)
                if (state.Cities[index].Id == id) return state.Cities[index];
            return null;
        }

        private static TileState FindTile(GameState state, EntityId id)
        {
            for (var index = 0; index < state.Tiles.Count; index++)
                if (state.Tiles[index].Id == id) return state.Tiles[index];
            return null;
        }
    }
}
