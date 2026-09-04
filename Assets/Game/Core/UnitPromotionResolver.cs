using System;

namespace LittleCiv.Core
{
    public sealed class UnitPromotionResult
    {
        public EntityId UnitId;
        public EntityId HomeCityId;
        public UnitType PreviousType;
        public UnitType PromotedType;
        public int GoldCost;
    }

    public static class UnitPromotionResolver
    {
        public static bool TryPromote(
            GameState state, GameCommand command, out UnitPromotionResult result)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (command == null) throw new ArgumentNullException(nameof(command));
            result = null;
            if (!Enum.IsDefined(typeof(UnitType), command.PrimaryValue)) return false;
            var unit = FindUnit(state, command.SubjectId);
            var player = FindPlayer(state, command.PlayerId);
            if (unit == null || player == null || unit.OwnerId != command.PlayerId ||
                unit.RemainingMovement <= 0) return false;
            var promotedType = (UnitType)command.PrimaryValue;
            if (!IsHigherTypeInSameBranch(unit.Type, promotedType)) return false;

            var tile = FindTile(state, unit.TileId);
            var city = tile == null ? null : FindCity(state, tile.CityId);
            if (tile == null || city == null || tile.IsSharedBoundary ||
                tile.ControllerId != command.PlayerId || city.OwnerId != command.PlayerId) return false;
            if (!IsUnlocked(player, city, promotedType)) return false;
            var cost = UnitRules.TrainingGold(promotedType) - UnitRules.TrainingGold(unit.Type);
            if (cost < 0 || city.Gold < cost) return false;

            var previousType = unit.Type;
            var previousFood = unit.CarriedFood;
            var previousMaximum = UnitRules.MaximumHitPoints(previousType);
            var promotedMaximum = UnitRules.MaximumHitPoints(promotedType);
            // Preserve the pre-promotion health percentage instead of healing the unit.
            unit.HitPoints = Math.Min(promotedMaximum, Math.Max(1,
                (int)(((long)unit.HitPoints * promotedMaximum) / previousMaximum)));
            unit.Type = promotedType;
            // Promotion changes equipment, not the food already loaded on the unit.
            unit.CarriedFood = Math.Min(previousFood, UnitRules.FoodCapacity(state, unit));
            unit.RemainingMovement = 0;
            unit.HasAutomaticDefense = false;
            city.Gold -= cost;
            result = new UnitPromotionResult
            {
                UnitId = unit.Id,
                HomeCityId = city.Id,
                PreviousType = previousType,
                PromotedType = promotedType,
                GoldCost = cost
            };
            return true;
        }

        private static bool IsUnlocked(PlayerState player, CityState city, UnitType type)
        {
            if (player.Slot != PlayerSlot.Neutral)
                return player.UnlockedUnitTypes != null && player.UnlockedUnitTypes.Contains(type);
            switch (type)
            {
                case UnitType.IronInfantry:
                    return NeutralResearchResolver.HasResearch(city, ResearchType.IronWorking);
                case UnitType.GunpowderInfantry:
                    return NeutralResearchResolver.HasResearch(city, ResearchType.Gunpowder);
                case UnitType.MechanizedInfantry:
                case UnitType.MotorizedSupply:
                    return NeutralResearchResolver.HasResearch(city, ResearchType.Vehicles);
                default: return false;
            }
        }

        private static bool IsHigherTypeInSameBranch(UnitType current, UnitType target)
        {
            if (current == UnitType.Supply) return target == UnitType.MotorizedSupply;
            if (current == UnitType.MotorizedSupply) return false;
            return current <= UnitType.MechanizedInfantry && target <= UnitType.MechanizedInfantry &&
                   (int)target > (int)current;
        }

        private static UnitState FindUnit(GameState state, EntityId id)
        {
            for (var index = 0; index < state.Units.Count; index++)
                if (state.Units[index].Id == id) return state.Units[index];
            return null;
        }

        private static PlayerState FindPlayer(GameState state, EntityId id)
        {
            for (var index = 0; index < state.Players.Count; index++)
                if (state.Players[index].Id == id) return state.Players[index];
            return null;
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
