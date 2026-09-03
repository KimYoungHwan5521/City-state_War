using System;
using System.Collections.Generic;

namespace LittleCiv.Core
{
    public sealed class UnitStarvationResult
    {
        public readonly List<EntityId> EnteredStarvationUnitIds = new List<EntityId>();
        public readonly List<EntityId> RecoveredFromStarvationUnitIds = new List<EntityId>();
        public readonly List<EntityId> StarvedToDeathUnitIds = new List<EntityId>();
    }

    public static class UnitStarvationResolver
    {
        public static UnitStarvationResult ResolveFirstFailure(
            GameState state,
            IReadOnlyList<EntityId> suppliedUnitIds,
            IReadOnlyList<EntityId> unsuppliedUnitIds)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (suppliedUnitIds == null) throw new ArgumentNullException(nameof(suppliedUnitIds));
            if (unsuppliedUnitIds == null) throw new ArgumentNullException(nameof(unsuppliedUnitIds));
            var supplied = new HashSet<EntityId>(suppliedUnitIds);
            var unsupplied = new HashSet<EntityId>(unsuppliedUnitIds);
            var units = new List<UnitState>(state.Units);
            units.Sort((left, right) => left.Id.CompareTo(right.Id));
            var result = new UnitStarvationResult();
            for (var index = 0; index < units.Count; index++)
            {
                var unit = units[index];
                if (supplied.Contains(unit.Id) && unit.IsStarving)
                {
                    unit.IsStarving = false;
                    result.RecoveredFromStarvationUnitIds.Add(unit.Id);
                }
                else if (unsupplied.Contains(unit.Id) && unit.IsStarving)
                {
                    result.StarvedToDeathUnitIds.Add(unit.Id);
                }
                else if (unsupplied.Contains(unit.Id))
                {
                    unit.IsStarving = true;
                    result.EnteredStarvationUnitIds.Add(unit.Id);
                }
            }
            state.Units.RemoveAll(item => result.StarvedToDeathUnitIds.Contains(item.Id));
            NeutralLevyResolver.ReconcileDestroyedUnits(state);
            return result;
        }
    }
}
