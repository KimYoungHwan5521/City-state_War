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
    }
}
