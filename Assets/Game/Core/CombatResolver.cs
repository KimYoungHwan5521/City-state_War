using System;
using System.Collections.Generic;

namespace LittleCiv.Core
{
    [Serializable]
    public sealed class UnitDamageRecord
    {
        public EntityId UnitId;
        public int Damage;
        public bool Destroyed;
    }

    public sealed class CombatResult
    {
        public EntityId AttackingUnitId;
        public EntityId TargetTileId;
        public readonly List<EntityId> DefenderFrontLine = new List<EntityId>();
        public readonly List<UnitDamageRecord> DamageRecords = new List<UnitDamageRecord>();
        public readonly List<EntityId> DestroyedUnitIds = new List<EntityId>();
        public int DroppedFood;
        public EntityId GroundFoodOwnerId;
        public bool AttackerAdvanced;
        public OccupationResult Occupation;
    }

    public static class CombatResolver
    {
        public static CombatResult Resolve(GameState state, CombatEngagementRequest request)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (request == null) throw new ArgumentNullException(nameof(request));
            var attacker = FindUnit(state, request.AttackingUnitId);
            if (attacker == null || attacker.OwnerId != request.AttackingPlayerId)
            {
                throw new InvalidOperationException("The attacking unit is missing or has the wrong owner.");
            }
            if (attacker.CreatedTurn == state.TurnNumber)
            {
                throw new InvalidOperationException("A unit cannot attack on the turn its training completes.");
            }

            var result = new CombatResult
            {
                AttackingUnitId = attacker.Id,
                TargetTileId = request.TargetTileId
            };
            var defenders = GetDefenders(state, request.TargetTileId, attacker.OwnerId);
            defenders.Sort(CompareFrontLine);
            for (var i = 0; i < defenders.Count; i++) result.DefenderFrontLine.Add(defenders[i].Id);
            if (defenders.Count == 0)
            {
                attacker.TileId = request.TargetTileId;
                attacker.RemainingMovement = 0;
                attacker.HasAutomaticDefense = false;
                result.AttackerAdvanced = true;
                result.Occupation = OccupationResolver.Resolve(state, attacker.OwnerId, request.TargetTileId);
                return result;
            }

            var retaliationSources = SnapshotCombatants(defenders);
            var preparedDefense = !request.BothSidesAreAttackers && defenders[0].HasAutomaticDefense;
            var defenseBonus = DefenseBonus(state, request, defenders[0]);
            var defenseEquipmentTier = DefenseEquipmentTier(state, request, defenders[0]);
            ApplyDamagePool(
                defenders,
                CalculateDamage(attacker, defenders[0], preparedDefense,
                    defenseBonus, defenseEquipmentTier),
                result);

            var attackerDamage = 0;
            for (var i = 0; i < retaliationSources.Count; i++)
            {
                attackerDamage += CalculateDamage(
                    retaliationSources[i],
                    attacker,
                    false,
                    0);
            }
            ApplyDamage(attacker, attackerDamage, result);

            result.DroppedFood = SumDestroyedFood(state, result.DestroyedUnitIds);
            RemoveDestroyed(state, result.DestroyedUnitIds);
            NeutralLevyResolver.ReconcileDestroyedUnits(state);
            if (attacker.HitPoints > 0 && !HasEnemyOnTile(state, request.TargetTileId, attacker.OwnerId))
            {
                attacker.TileId = request.TargetTileId;
                attacker.RemainingMovement = 0;
                attacker.HasAutomaticDefense = false;
                result.AttackerAdvanced = true;
                result.Occupation = OccupationResolver.Resolve(state, attacker.OwnerId, request.TargetTileId);
            }
            if (result.DroppedFood > 0)
            {
                result.GroundFoodOwnerId = GroundFoodResolver.DepositAfterCombat(
                    state, request.TargetTileId, result.DroppedFood);
            }
            return result;
        }

        public static int CalculateDamage(
            UnitState source,
            UnitState target,
            bool targetHasDefensivePosture,
            int defenseBonusPercent,
            int minimumTargetEquipmentTier = 0)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (target == null) throw new ArgumentNullException(nameof(target));
            long numerator = UnitRules.Attack(source.Type);
            long denominator = 1;
            var targetEquipmentTier = Math.Max(
                UnitRules.EquipmentTier(target.Type), minimumTargetEquipmentTier);
            var difference = UnitRules.EquipmentTier(source.Type) - targetEquipmentTier;
            if (difference > 0)
            {
                numerator *= 5 + (4 * difference);
                denominator *= 5;
            }

            if (source.IsStarving) denominator *= 2;
            if (targetHasDefensivePosture)
            {
                numerator *= 2;
                denominator *= 3;
            }
            if (defenseBonusPercent > 0)
            {
                numerator *= 100;
                denominator *= 100 + defenseBonusPercent;
            }
            return Math.Max(1, (int)(numerator / denominator));
        }

        public static int CompareFrontLine(UnitState left, UnitState right)
        {
            var supply = UnitRules.IsSupply(left.Type).CompareTo(UnitRules.IsSupply(right.Type));
            if (supply != 0) return supply;
            var attack = UnitRules.Attack(right.Type).CompareTo(UnitRules.Attack(left.Type));
            if (attack != 0) return attack;
            var hitPoints = right.HitPoints.CompareTo(left.HitPoints);
            return hitPoints != 0 ? hitPoints : left.Id.CompareTo(right.Id);
        }

        private static void ApplyDamagePool(List<UnitState> frontLine, int damage, CombatResult result)
        {
            for (var i = 0; i < frontLine.Count && damage > 0; i++)
            {
                var applied = Math.Min(frontLine[i].HitPoints, damage);
                ApplyDamage(frontLine[i], applied, result);
                damage -= applied;
            }
        }

        private static void ApplyDamage(UnitState unit, int damage, CombatResult result)
        {
            var applied = Math.Min(unit.HitPoints, Math.Max(0, damage));
            unit.HitPoints -= applied;
            var destroyed = unit.HitPoints <= 0;
            result.DamageRecords.Add(new UnitDamageRecord
            {
                UnitId = unit.Id,
                Damage = applied,
                Destroyed = destroyed
            });
            if (destroyed && !result.DestroyedUnitIds.Contains(unit.Id)) result.DestroyedUnitIds.Add(unit.Id);
        }

        private static int DefenseBonus(
            GameState state,
            CombatEngagementRequest request,
            UnitState defender)
        {
            if (request.BothSidesAreAttackers || !defender.HasAutomaticDefense) return 0;
            var tile = FindTile(state, request.TargetTileId);
            return tile == null ? 0 : Math.Max(0, tile.DefenseBonusPercent);
        }

        private static int DefenseEquipmentTier(
            GameState state,
            CombatEngagementRequest request,
            UnitState defender)
        {
            if (request.BothSidesAreAttackers || !defender.HasAutomaticDefense) return 0;
            for (var index = 0; index < state.DefenseFacilities.Count; index++)
            {
                var facility = state.DefenseFacilities[index];
                if (facility.TileId == request.TargetTileId)
                    return DefenseFacilityResolver.EffectiveEquipmentTier(facility);
            }
            return 0;
        }

        private static List<UnitState> GetDefenders(GameState state, EntityId tileId, EntityId attackerOwner)
        {
            var result = new List<UnitState>();
            for (var i = 0; i < state.Units.Count; i++)
            {
                var unit = state.Units[i];
                if (unit.TileId == tileId && unit.OwnerId != attackerOwner) result.Add(unit);
            }
            return result;
        }

        private static List<UnitState> SnapshotCombatants(List<UnitState> source)
        {
            var result = new List<UnitState>(source.Count);
            for (var i = 0; i < source.Count; i++)
            {
                var item = source[i];
                result.Add(new UnitState
                {
                    Id = item.Id,
                    OwnerId = item.OwnerId,
                    TileId = item.TileId,
                    Type = item.Type,
                    HitPoints = item.HitPoints,
                    IsStarving = item.IsStarving
                });
            }
            return result;
        }

        private static bool HasEnemyOnTile(GameState state, EntityId tileId, EntityId ownerId)
        {
            for (var i = 0; i < state.Units.Count; i++)
            {
                var unit = state.Units[i];
                if (unit.TileId == tileId && unit.OwnerId != ownerId && unit.HitPoints > 0) return true;
            }
            return false;
        }

        private static void RemoveDestroyed(GameState state, List<EntityId> destroyedIds)
        {
            state.Units.RemoveAll(item => destroyedIds.Contains(item.Id));
        }

        private static int SumDestroyedFood(GameState state, List<EntityId> destroyedIds)
        {
            var total = 0;
            for (var index = 0; index < state.Units.Count; index++)
                if (destroyedIds.Contains(state.Units[index].Id)) total += Math.Max(0, state.Units[index].CarriedFood);
            return total;
        }

        private static UnitState FindUnit(GameState state, EntityId unitId)
        {
            for (var i = 0; i < state.Units.Count; i++) if (state.Units[i].Id == unitId) return state.Units[i];
            return null;
        }

        private static TileState FindTile(GameState state, EntityId tileId)
        {
            for (var i = 0; i < state.Tiles.Count; i++) if (state.Tiles[i].Id == tileId) return state.Tiles[i];
            return null;
        }
    }
}
