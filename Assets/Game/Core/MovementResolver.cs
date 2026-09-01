using System;
using System.Collections.Generic;

namespace LittleCiv.Core
{
    public enum MovementStopReason
    {
        Completed = 0,
        UnknownUnit = 1,
        NotUnitOwner = 2,
        EmptyPath = 3,
        NonAdjacentTile = 4,
        InsufficientMovement = 5,
        EnemyOccupied = 6,
        TileCapacityReached = 7,
        PriorityLost = 8,
        SwapConflict = 9,
        TrainedThisTurn = 10
    }

    public sealed class MovementResult
    {
        public EntityId UnitId;
        public EntityId FinalTileId;
        public int StepsMoved;
        public MovementStopReason StopReason;
    }

    public static class MovementResolver
    {
        public static MovementResult Resolve(GameState state, GameCommand command)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (command == null) throw new ArgumentNullException(nameof(command));
            var unit = FindUnit(state, command.SubjectId);
            if (unit == null)
            {
                return Result(command.SubjectId, default, 0, MovementStopReason.UnknownUnit);
            }

            if (unit.OwnerId != command.PlayerId)
            {
                return Result(unit.Id, unit.TileId, 0, MovementStopReason.NotUnitOwner);
            }

            if (unit.CreatedTurn == state.TurnNumber)
            {
                return Result(unit.Id, unit.TileId, 0, MovementStopReason.TrainedThisTurn);
            }

            if (command.Path == null || command.Path.Count == 0)
            {
                return Result(unit.Id, unit.TileId, 0, MovementStopReason.EmptyPath);
            }

            var steps = 0;
            var stopReason = MovementStopReason.Completed;
            for (var i = 0; i < command.Path.Count; i++)
            {
                var nextTileId = command.Path[i];
                if (!MapTraversal.AreAdjacent(state, unit.TileId, nextTileId))
                {
                    stopReason = MovementStopReason.NonAdjacentTile;
                    break;
                }

                if (unit.RemainingMovement <= 0)
                {
                    stopReason = MovementStopReason.InsufficientMovement;
                    break;
                }

                if (HasEnemy(state, unit, nextTileId))
                {
                    stopReason = MovementStopReason.EnemyOccupied;
                    break;
                }

                if (!HasCapacity(state, unit, nextTileId))
                {
                    stopReason = MovementStopReason.TileCapacityReached;
                    break;
                }

                unit.TileId = nextTileId;
                unit.RemainingMovement--;
                unit.HasAutomaticDefense = false;
                steps++;
            }

            return Result(unit.Id, unit.TileId, steps, stopReason);
        }

        private static bool HasEnemy(GameState state, UnitState movingUnit, EntityId tileId)
        {
            for (var i = 0; i < state.Units.Count; i++)
            {
                var unit = state.Units[i];
                if (unit.TileId == tileId && unit.OwnerId != movingUnit.OwnerId) return true;
            }

            return false;
        }

        private static bool HasCapacity(GameState state, UnitState movingUnit, EntityId tileId)
        {
            var supply = UnitRules.IsSupply(movingUnit.Type);
            var count = 0;
            for (var i = 0; i < state.Units.Count; i++)
            {
                var unit = state.Units[i];
                if (unit.Id == movingUnit.Id || unit.TileId != tileId) continue;
                if (UnitRules.IsSupply(unit.Type) == supply) count++;
            }

            return count < (supply ? UnitRules.SupplyUnitsPerTile : UnitRules.CombatUnitsPerTile);
        }

        private static UnitState FindUnit(GameState state, EntityId unitId)
        {
            for (var i = 0; i < state.Units.Count; i++)
            {
                if (state.Units[i].Id == unitId) return state.Units[i];
            }

            return null;
        }

        private static MovementResult Result(
            EntityId unitId,
            EntityId finalTileId,
            int steps,
            MovementStopReason reason)
        {
            return new MovementResult
            {
                UnitId = unitId,
                FinalTileId = finalTileId,
                StepsMoved = steps,
                StopReason = reason
            };
        }
    }
}
