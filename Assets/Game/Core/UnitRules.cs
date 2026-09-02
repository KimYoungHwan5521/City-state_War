using System;

namespace LittleCiv.Core
{
    public static class UnitRules
    {
        public const int CombatUnitsPerTile = 3;
        public const int SupplyUnitsPerTile = 1;

        public static int Movement(UnitType type)
        {
            switch (type)
            {
                case UnitType.Militia: return 2;
                case UnitType.IronInfantry: return 2;
                case UnitType.GunpowderInfantry: return 2;
                case UnitType.MechanizedInfantry: return 3;
                case UnitType.Supply: return 4;
                case UnitType.MotorizedSupply: return 6;
                default: throw new ArgumentOutOfRangeException(nameof(type));
            }
        }

        public static bool IsSupply(UnitType type)
        {
            return type == UnitType.Supply || type == UnitType.MotorizedSupply;
        }

        public static int Attack(UnitType type)
        {
            switch (type)
            {
                case UnitType.Militia: return 3;
                case UnitType.IronInfantry: return 5;
                case UnitType.GunpowderInfantry: return 7;
                case UnitType.MechanizedInfantry: return 9;
                case UnitType.Supply: return 1;
                case UnitType.MotorizedSupply: return 4;
                default: throw new ArgumentOutOfRangeException(nameof(type));
            }
        }

        public static int MaximumHitPoints(UnitType type)
        {
            switch (type)
            {
                case UnitType.Militia: return 16;
                case UnitType.IronInfantry: return 16;
                case UnitType.GunpowderInfantry: return 27;
                case UnitType.MechanizedInfantry: return 38;
                case UnitType.Supply: return 9;
                case UnitType.MotorizedSupply: return 16;
                default: throw new ArgumentOutOfRangeException(nameof(type));
            }
        }

        public static int EquipmentTier(UnitType type)
        {
            switch (type)
            {
                case UnitType.Militia:
                case UnitType.Supply: return 0;
                case UnitType.IronInfantry: return 1;
                case UnitType.GunpowderInfantry: return 2;
                case UnitType.MechanizedInfantry:
                case UnitType.MotorizedSupply: return 3;
                default: throw new ArgumentOutOfRangeException(nameof(type));
            }
        }

        public static int TrainingTurns(UnitType type)
        {
            switch (type)
            {
                case UnitType.Militia:
                case UnitType.Supply: return 1;
                case UnitType.IronInfantry: return 2;
                case UnitType.GunpowderInfantry:
                case UnitType.MotorizedSupply: return 3;
                case UnitType.MechanizedInfantry: return 4;
                default: throw new ArgumentOutOfRangeException(nameof(type));
            }
        }

        public static int TrainingGold(UnitType type)
        {
            switch (type)
            {
                case UnitType.Militia: return 3;
                case UnitType.Supply: return 2;
                case UnitType.IronInfantry: return 7;
                case UnitType.GunpowderInfantry: return 12;
                case UnitType.MechanizedInfantry: return 20;
                case UnitType.MotorizedSupply: return 14;
                default: throw new ArgumentOutOfRangeException(nameof(type));
            }
        }

        public static int FoodCapacity(UnitType type)
        {
            switch (type)
            {
                case UnitType.Militia:
                case UnitType.IronInfantry:
                case UnitType.GunpowderInfantry: return 6;
                case UnitType.MechanizedInfantry: return 10;
                case UnitType.Supply: return 20;
                case UnitType.MotorizedSupply: return 40;
                default: throw new ArgumentOutOfRangeException(nameof(type));
            }
        }

        public static int FoodCapacity(GameState state, UnitState unit)
        {
            if (unit == null) throw new ArgumentNullException(nameof(unit));
            if (state != null)
            {
                var owner = state.Players.Find(item => item.Id == unit.OwnerId);
                if (owner != null && owner.Slot == PlayerSlot.Neutral)
                {
                    var city = state.Cities.Find(item => item.Id == unit.HomeCityId);
                    var percent = NeutralResearchResolver.HasResearch(city, ResearchType.Canning) ? 200 :
                        NeutralResearchResolver.HasResearch(city, ResearchType.Salting) ? 150 : 100;
                    return (FoodCapacity(unit.Type) * percent) / 100;
                }
            }
            return FoodCapacity(state, unit.OwnerId, unit.Type);
        }

        public static int FoodCapacity(GameState state, EntityId ownerId, UnitType type)
        {
            var percent = 100;
            if (state != null)
            {
                var player = state.Players.Find(item => item.Id == ownerId);
                if (player != null && player.FoodCapacityPercent > 0)
                    percent = player.FoodCapacityPercent;
            }
            return (FoodCapacity(type) * percent) / 100;
        }

        public static int FoodConsumption(UnitType type)
        {
            if (!Enum.IsDefined(typeof(UnitType), type))
                throw new ArgumentOutOfRangeException(nameof(type));
            return 1;
        }

        public static int RecoveryPerTurn(UnitType type)
        {
            return Math.Max(1, MaximumHitPoints(type) / 8);
        }
    }
}
