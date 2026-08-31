using System;
using System.Collections.Generic;

namespace LittleCiv.Core
{
    public sealed class MovementPriorityPlan
    {
        public readonly List<GameCommand> OrderedCommands = new List<GameCommand>();
        public readonly Dictionary<EntityId, MovementStopReason> BlockedCommandReasons =
            new Dictionary<EntityId, MovementStopReason>();
    }

    public static class MovementPriorityResolver
    {
        public static MovementPriorityPlan Build(GameState state, IReadOnlyList<GameCommand> commands)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (commands == null) throw new ArgumentNullException(nameof(commands));
            var plan = new MovementPriorityPlan();
            var moves = new List<GameCommand>();
            for (var i = 0; i < commands.Count; i++)
            {
                if (commands[i].Type == GameCommandType.MoveUnit)
                {
                    moves.Add(commands[i]);
                }
            }

            MarkSwapConflicts(state, moves, plan);
            for (var i = 0; i < moves.Count; i++)
            {
                if (!plan.BlockedCommandReasons.ContainsKey(moves[i].CommandId) &&
                    !FirstDestination(moves[i]).IsValid)
                {
                    plan.OrderedCommands.Add(moves[i]);
                }
            }
            var groups = GroupByPriorityDestination(state, moves, plan.BlockedCommandReasons);
            var destinations = new List<EntityId>(groups.Keys);
            destinations.Sort();
            for (var i = 0; i < destinations.Count; i++)
            {
                var group = groups[destinations[i]];
                ResolveDestinationGroup(state, destinations[i], group, plan);
            }

            return plan;
        }

        private static void MarkSwapConflicts(
            GameState state,
            List<GameCommand> moves,
            MovementPriorityPlan plan)
        {
            for (var i = 0; i < moves.Count; i++)
            {
                var left = moves[i];
                var leftUnit = FindUnit(state, left.SubjectId);
                if (leftUnit == null || FirstDestination(left) == default) continue;
                for (var j = i + 1; j < moves.Count; j++)
                {
                    var right = moves[j];
                    var rightUnit = FindUnit(state, right.SubjectId);
                    if (rightUnit == null || leftUnit.OwnerId == rightUnit.OwnerId) continue;
                    if (FirstDestination(left) == rightUnit.TileId &&
                        FirstDestination(right) == leftUnit.TileId)
                    {
                        plan.BlockedCommandReasons[left.CommandId] = MovementStopReason.SwapConflict;
                        plan.BlockedCommandReasons[right.CommandId] = MovementStopReason.SwapConflict;
                    }
                }
            }
        }

        private static Dictionary<EntityId, List<GameCommand>> GroupByPriorityDestination(
            GameState state,
            List<GameCommand> moves,
            Dictionary<EntityId, MovementStopReason> blocked)
        {
            var result = new Dictionary<EntityId, List<GameCommand>>();
            for (var i = 0; i < moves.Count; i++)
            {
                var command = moves[i];
                if (blocked.ContainsKey(command.CommandId)) continue;
                var destination = PriorityDestination(state, command);
                if (!destination.IsValid)
                {
                    continue;
                }

                if (!result.TryGetValue(destination, out var group))
                {
                    group = new List<GameCommand>();
                    result.Add(destination, group);
                }
                group.Add(command);
            }
            return result;
        }

        private static void ResolveDestinationGroup(
            GameState state,
            EntityId destination,
            List<GameCommand> group,
            MovementPriorityPlan plan)
        {
            group.Sort((left, right) => ComparePriority(state, destination, left, right));
            var winningOwner = group[0].PlayerId;
            for (var i = 0; i < group.Count; i++)
            {
                var command = group[i];
                if (command.PlayerId == winningOwner)
                {
                    plan.OrderedCommands.Add(command);
                }
                else
                {
                    plan.BlockedCommandReasons[command.CommandId] = MovementStopReason.PriorityLost;
                }
            }
        }

        private static int ComparePriority(
            GameState state,
            EntityId destination,
            GameCommand left,
            GameCommand right)
        {
            var tile = FindTile(state, destination);
            if (tile != null && tile.IsSharedBoundary)
            {
                var distance = DistanceTo(left, destination).CompareTo(DistanceTo(right, destination));
                if (distance != 0) return distance;
                var leftUnit = FindUnit(state, left.SubjectId);
                var rightUnit = FindUnit(state, right.SubjectId);
                var leftMovement = leftUnit == null ? -1 : UnitRules.Movement(leftUnit.Type);
                var rightMovement = rightUnit == null ? -1 : UnitRules.Movement(rightUnit.Type);
                var movement = rightMovement.CompareTo(leftMovement);
                if (movement != 0) return movement;
                var slot = FindSlot(state, left.PlayerId).CompareTo(FindSlot(state, right.PlayerId));
                if (slot != 0) return slot;
            }
            else if (tile != null && tile.ControllerId.IsValid)
            {
                var leftHome = left.PlayerId == tile.ControllerId;
                var rightHome = right.PlayerId == tile.ControllerId;
                if (leftHome != rightHome) return leftHome ? -1 : 1;
            }

            var player = FindSlot(state, left.PlayerId).CompareTo(FindSlot(state, right.PlayerId));
            if (player != 0) return player;
            return left.CommandId.CompareTo(right.CommandId);
        }

        private static int DistanceTo(GameCommand command, EntityId destination)
        {
            if (command.Path == null) return int.MaxValue;
            for (var i = 0; i < command.Path.Count; i++)
            {
                if (command.Path[i] == destination) return i + 1;
            }
            return int.MaxValue;
        }

        private static EntityId PriorityDestination(GameState state, GameCommand command)
        {
            if (command.Path == null || command.Path.Count == 0) return default;
            for (var i = 0; i < command.Path.Count; i++)
            {
                var tile = FindTile(state, command.Path[i]);
                if (tile != null && tile.IsSharedBoundary) return command.Path[i];
            }
            return command.Path[0];
        }

        private static EntityId FirstDestination(GameCommand command) =>
            command.Path == null || command.Path.Count == 0 ? default : command.Path[0];

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

        private static PlayerSlot FindSlot(GameState state, EntityId playerId)
        {
            for (var i = 0; i < state.Players.Count; i++)
            {
                if (state.Players[i].Id == playerId) return state.Players[i].Slot;
            }
            return PlayerSlot.Neutral;
        }
    }
}
