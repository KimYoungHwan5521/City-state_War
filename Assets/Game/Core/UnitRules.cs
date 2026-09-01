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
