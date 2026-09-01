using System;
using System.Collections.Generic;

namespace LittleCiv.Core
{
    public sealed class UnitRecoveryRecord
    {
        public EntityId UnitId;
        public int RecoveredHitPoints;
    }

    public static class UnitRecoveryResolver
    {
        public static List<UnitRecoveryRecord> Resolve(
            GameState state, IReadOnlyList<EntityId> suppliedUnitIds)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (suppliedUnitIds == null) throw new ArgumentNullException(nameof(suppliedUnitIds));
            var supplied = new HashSet<EntityId>(suppliedUnitIds);
            var units = new List<UnitState>(state.Units);
            units.Sort((left, right) => left.Id.CompareTo(right.Id));
            var result = new List<UnitRecoveryRecord>();
            for (var index = 0; index < units.Count; index++)
            {
                var unit = units[index];
                var maximum = UnitRules.MaximumHitPoints(unit.Type);
                if (!supplied.Contains(unit.Id) || unit.IsStarving || unit.HitPoints <= 0 ||
                    unit.HitPoints >= maximum) continue;
                var recovered = Math.Min(maximum - unit.HitPoints, UnitRules.RecoveryPerTurn(unit.Type));
                unit.HitPoints += recovered;
                result.Add(new UnitRecoveryRecord
                {
                    UnitId = unit.Id,
                    RecoveredHitPoints = recovered
                });
            }
            return result;
        }
    }
}
