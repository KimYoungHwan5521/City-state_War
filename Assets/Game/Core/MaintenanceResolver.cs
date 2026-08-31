using System;
using System.Collections.Generic;

namespace LittleCiv.Core
{
    public sealed class MaintenanceResolution
    {
        public readonly List<EntityId> DisbandedUnits = new List<EntityId>();
        public readonly List<EntityId> SuspendedDistricts = new List<EntityId>();
    }

    public static class MaintenanceResolver
    {
        public static MaintenanceResolution Resolve(GameState state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            var result = new MaintenanceResolution();
            for (var cityIndex = 0; cityIndex < state.Cities.Count; cityIndex++)
                ResolveCity(state, state.Cities[cityIndex], result);
            return result;
        }

        public static bool TrySetPriority(GameState state, GameCommand command)
        {
            if (command.PrimaryValue < 0) return false;
            for (var i = 0; i < state.Units.Count; i++)
            {
                var unit = state.Units[i];
                if (unit.Id != command.TargetId) continue;
                if (unit.OwnerId != command.PlayerId) return false;
                unit.MaintenancePriority = command.PrimaryValue;
                return true;
            }
            for (var i = 0; i < state.Districts.Count; i++)
            {
                var district = state.Districts[i];
                if (district.Id != command.TargetId) continue;
                var city = FindCity(state, district.CityId);
                if (city == null || city.OwnerId != command.PlayerId) return false;
                district.MaintenancePriority = command.PrimaryValue;
                return true;
            }
            return false;
        }

        private static void ResolveCity(GameState state, CityState city, MaintenanceResolution result)
        {
            var units = state.Units.FindAll(item => item.OwnerId == city.OwnerId);
            units.Sort(CompareUnitsForDisband);
            var unitCost = SumUnitCost(units);
            while (unitCost > city.Gold && units.Count > 0)
            {
                var unit = units[0];
                units.RemoveAt(0);
                unitCost -= UnitUpkeep(unit.Type);
                city.StoredFood += unit.CarriedFood;
                state.Units.Remove(unit);
                result.DisbandedUnits.Add(unit.Id);
            }
            city.Gold -= unitCost;

            var facilities = state.Districts.FindAll(item =>
                item.CityId == city.Id && DistrictUpkeep(item.Type) > 0 && IsStaffedAndControlled(item, city));
            facilities.Sort(CompareDistrictsForSuspension);
            var facilityCost = SumDistrictCost(facilities);
            while (facilityCost > city.Gold && facilities.Count > 0)
            {
                var district = facilities[0];
                facilities.RemoveAt(0);
                facilityCost -= DistrictUpkeep(district.Type);
                district.IsMaintenanceSuspended = true;
                district.IsOperational = false;
                result.SuspendedDistricts.Add(district.Id);
            }
            for (var i = 0; i < facilities.Count; i++)
            {
                facilities[i].IsMaintenanceSuspended = false;
                facilities[i].IsOperational = true;
            }
            city.Gold -= facilityCost;
        }

        private static bool IsStaffedAndControlled(DistrictState district, CityState city)
        {
            return district.RemainingConstructionTurns <= 0 && district.AssignedCitizens > 0 &&
                   district.ControllerId == city.OwnerId;
        }

        private static int CompareUnitsForDisband(UnitState left, UnitState right)
        {
            var priority = CompareSpecifiedPriority(left.MaintenancePriority, right.MaintenancePriority);
            if (priority != 0) return priority;
            var type = UnitRemovalOrder(left.Type).CompareTo(UnitRemovalOrder(right.Type));
            if (type != 0) return type;
            if (left.IsStarving != right.IsStarving) return left.IsStarving ? -1 : 1;
            var health = left.HitPoints.CompareTo(right.HitPoints);
            if (health != 0) return health;
            var food = left.CarriedFood.CompareTo(right.CarriedFood);
            if (food != 0) return food;
            var trained = right.CreatedTurn.CompareTo(left.CreatedTurn);
            return trained != 0 ? trained : right.Id.CompareTo(left.Id);
        }

        private static int CompareDistrictsForSuspension(DistrictState left, DistrictState right)
        {
            var priority = CompareSpecifiedPriority(left.MaintenancePriority, right.MaintenancePriority);
            if (priority != 0) return priority;
            var type = DistrictRemovalOrder(left.Type).CompareTo(DistrictRemovalOrder(right.Type));
            return type != 0 ? type : left.Id.CompareTo(right.Id);
        }

        private static int CompareSpecifiedPriority(int left, int right)
        {
            var leftSpecified = left > 0;
            var rightSpecified = right > 0;
            if (leftSpecified != rightSpecified) return leftSpecified ? -1 : 1;
            return leftSpecified ? left.CompareTo(right) : 0;
        }

        private static int UnitRemovalOrder(UnitType type)
        {
            switch (type)
            {
                case UnitType.Supply: return 0;
                case UnitType.Militia: return 1;
                case UnitType.IronInfantry: return 2;
                case UnitType.GunpowderInfantry: return 3;
                case UnitType.MotorizedSupply: return 4;
                case UnitType.MechanizedInfantry: return 5;
                default: return 6;
            }
        }

        private static int DistrictRemovalOrder(DistrictType type)
        {
            switch (type)
            {
                case DistrictType.Culture: return 0;
                case DistrictType.Science: return 1;
                case DistrictType.NuclearFacility: return 2;
                default: return 3;
            }
        }

        public static int UnitUpkeep(UnitType type)
        {
            switch (type)
            {
                case UnitType.Militia:
                case UnitType.Supply: return 1;
                case UnitType.IronInfantry: return 2;
                case UnitType.GunpowderInfantry:
                case UnitType.MotorizedSupply: return 3;
                case UnitType.MechanizedInfantry: return 5;
                default: return 0;
            }
        }

        public static int DistrictUpkeep(DistrictType type)
        {
            switch (type)
            {
                case DistrictType.Science:
                case DistrictType.Culture: return 1;
                case DistrictType.NuclearFacility: return 3;
                default: return 0;
            }
        }

        private static int SumUnitCost(List<UnitState> units)
        {
            var total = 0;
            for (var i = 0; i < units.Count; i++) total += UnitUpkeep(units[i].Type);
            return total;
        }

        private static int SumDistrictCost(List<DistrictState> districts)
        {
            var total = 0;
            for (var i = 0; i < districts.Count; i++) total += DistrictUpkeep(districts[i].Type);
            return total;
        }

        private static CityState FindCity(GameState state, EntityId id)
        {
            for (var i = 0; i < state.Cities.Count; i++) if (state.Cities[i].Id == id) return state.Cities[i];
            return null;
        }
    }
}
