using System;
using System.Collections.Generic;

namespace LittleCiv.Core
{
    [Serializable]
    public sealed class CombatEngagementRequest
    {
        public EntityId AttackingPlayerId;
        public EntityId AttackingUnitId;
        public EntityId TargetTileId;
        public bool BothSidesAreAttackers;
    }

    public sealed class ManeuverApplicationResult
    {
        public ManeuverChoice Choice;
        public MovementResult Movement;
        public CombatEngagementRequest Combat;
    }

    public static class ManeuverResolutionApplier
    {
        public static ManeuverApplicationResult Apply(GameState state, ManeuverResolution resolution)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (resolution == null) throw new ArgumentNullException(nameof(resolution));
            var unit = FindUnit(state, resolution.UnitId);
            if (unit == null) throw new InvalidOperationException("The maneuver unit no longer exists.");
            if (unit.OwnerId != resolution.PlayerId)
            {
                throw new InvalidOperationException("The maneuver player does not own the unit.");
            }

            var result = new ManeuverApplicationResult { Choice = resolution.Choice };
            switch (resolution.Choice)
            {
                case ManeuverChoice.Wait:
                    ApplyWait(state, unit, resolution.LastValidTileId);
                    break;
                case ManeuverChoice.Detour:
                    result.Movement = ApplyDetour(state, unit, resolution);
                    break;
                case ManeuverChoice.Fight:
                    result.Combat = new CombatEngagementRequest
                    {
                        AttackingPlayerId = unit.OwnerId,
                        AttackingUnitId = unit.Id,
                        TargetTileId = resolution.BlockedTileId,
                        BothSidesAreAttackers = IsBothAttackers(state, resolution)
                    };
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(resolution.Choice));
            }

            return result;
        }

        private static MovementResult ApplyDetour(
            GameState state,
            UnitState unit,
            ManeuverResolution resolution)
        {
            unit.TileId = resolution.LastValidTileId;
            var command = new GameCommand
            {
                CommandId = new EntityId(long.MaxValue),
                PlayerId = unit.OwnerId,
                TurnNumber = state.TurnNumber,
                Type = GameCommandType.MoveUnit,
                SubjectId = unit.Id,
                TargetId = resolution.DetourPath[resolution.DetourPath.Count - 1],
                Path = new List<EntityId>(resolution.DetourPath)
            };
            var movement = MovementResolver.Resolve(state, command);
            if (movement.StopReason != MovementStopReason.Completed)
            {
                ApplyWait(state, unit, unit.TileId);
            }
            else
            {
                BuildAutomaticDefenseIfPossible(state, unit);
            }
            return movement;
        }

        private static void ApplyWait(GameState state, UnitState unit, EntityId tileId)
        {
            if (tileId.IsValid) unit.TileId = tileId;
            BuildAutomaticDefenseIfPossible(state, unit);
        }

        private static void BuildAutomaticDefenseIfPossible(GameState state, UnitState unit)
        {
            if (unit.RemainingMovement <= 0) return;
            var tile = FindTile(state, unit.TileId);
            if (tile != null && tile.IsSharedBoundary) return;
            unit.HasAutomaticDefense = true;
            unit.RemainingMovement = 0;
        }

        private static bool IsBothAttackers(GameState state, ManeuverResolution resolution)
        {
            if (resolution.StopReason == MovementStopReason.SwapConflict) return true;
            var tile = FindTile(state, resolution.BlockedTileId);
            if (tile != null && tile.IsSharedBoundary) return true;
            for (var i = 0; i < state.Units.Count; i++)
            {
                var unit = state.Units[i];
                if (unit.TileId == resolution.BlockedTileId && unit.OwnerId != resolution.PlayerId)
                {
                    return !unit.HasAutomaticDefense;
                }
            }
            return false;
        }

        private static UnitState FindUnit(GameState state, EntityId unitId)
        {
            for (var i = 0; i < state.Units.Count; i++)
            {
                if (state.Units[i].Id == unitId) return state.Units[i];
            }
            return null;
        }

        private static TileState FindTile(GameState state, EntityId tileId)
        {
            for (var i = 0; i < state.Tiles.Count; i++)
            {
                if (state.Tiles[i].Id == tileId) return state.Tiles[i];
            }
            return null;
        }
    }
}
