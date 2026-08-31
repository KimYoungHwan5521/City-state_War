using System;
using System.Collections.Generic;

namespace LittleCiv.Core
{
    public enum TurnConfirmationReason
    {
        None = 0,
        PlayerConfirmed = 1,
        TimeExpired = 2
    }

    public sealed class TurnPlanningSession
    {
        public const int BaseTurnSeconds = 90;
        public const int MaximumReserveSeconds = 180;
        public const int ReserveRecoveryPerTurnSeconds = 10;

        private readonly GameState state;
        private readonly List<EntityId> participatingPlayers = new List<EntityId>();
        private readonly Dictionary<EntityId, List<GameCommand>> commands =
            new Dictionary<EntityId, List<GameCommand>>();
        private readonly Dictionary<EntityId, TurnConfirmationReason> confirmations =
            new Dictionary<EntityId, TurnConfirmationReason>();
        private readonly Dictionary<EntityId, int> startingReserveSeconds =
            new Dictionary<EntityId, int>();
        private double elapsedSeconds;

        public TurnPlanningSession(GameState state)
        {
            this.state = state ?? throw new ArgumentNullException(nameof(state));
            for (var i = 0; i < state.Players.Count; i++)
            {
                var player = state.Players[i];
                if (player.Slot == PlayerSlot.Neutral)
                {
                    continue;
                }

                participatingPlayers.Add(player.Id);
                commands.Add(player.Id, new List<GameCommand>());
                confirmations.Add(player.Id, TurnConfirmationReason.None);
                player.ReserveTimeSeconds = Math.Min(
                    MaximumReserveSeconds,
                    Math.Max(0, player.ReserveTimeSeconds) + ReserveRecoveryPerTurnSeconds);
                startingReserveSeconds.Add(player.Id, player.ReserveTimeSeconds);
            }

            participatingPlayers.Sort();
        }

        public int TurnNumber => state.TurnNumber;
        public double ElapsedSeconds => elapsedSeconds;
        public bool IsClosed => AllPlayersConfirmed();

        public CommandMutationResult Reserve(GameCommand command)
        {
            if (IsClosed)
            {
                return CommandMutationResult.SessionClosed;
            }

            var validation = CommandValidator.ValidateEnvelope(state, command);
            if (validation != CommandValidationError.None || !commands.ContainsKey(command.PlayerId))
            {
                return CommandMutationResult.InvalidCommand;
            }

            if (confirmations[command.PlayerId] != TurnConfirmationReason.None)
            {
                return CommandMutationResult.PlayerAlreadyConfirmed;
            }

            var playerCommands = commands[command.PlayerId];
            for (var i = 0; i < playerCommands.Count; i++)
            {
                if (playerCommands[i].CommandId == command.CommandId)
                {
                    playerCommands[i] = GameCommandCopy.Clone(command);
                    return CommandMutationResult.Accepted;
                }
            }

            playerCommands.Add(GameCommandCopy.Clone(command));
            return CommandMutationResult.Accepted;
        }

        public CommandMutationResult Cancel(EntityId playerId, EntityId commandId)
        {
            if (IsClosed)
            {
                return CommandMutationResult.SessionClosed;
            }

            if (!commands.TryGetValue(playerId, out var playerCommands))
            {
                return CommandMutationResult.InvalidCommand;
            }

            if (confirmations[playerId] != TurnConfirmationReason.None)
            {
                return CommandMutationResult.PlayerAlreadyConfirmed;
            }

            for (var i = 0; i < playerCommands.Count; i++)
            {
                if (playerCommands[i].CommandId == commandId)
                {
                    playerCommands.RemoveAt(i);
                    return CommandMutationResult.Accepted;
                }
            }

            return CommandMutationResult.CommandNotFound;
        }

        public bool Confirm(EntityId playerId)
        {
            if (IsClosed || !confirmations.ContainsKey(playerId))
            {
                return false;
            }

            confirmations[playerId] = TurnConfirmationReason.PlayerConfirmed;
            return true;
        }

        public bool CancelConfirmation(EntityId playerId)
        {
            if (IsClosed || !confirmations.TryGetValue(playerId, out var reason) ||
                reason != TurnConfirmationReason.PlayerConfirmed)
            {
                return false;
            }

            confirmations[playerId] = TurnConfirmationReason.None;
            return true;
        }

        public TurnConfirmationReason GetConfirmation(EntityId playerId)
        {
            return confirmations.TryGetValue(playerId, out var result)
                ? result
                : TurnConfirmationReason.None;
        }

        public IReadOnlyList<GameCommand> GetOwnCommands(EntityId playerId)
        {
            if (!commands.TryGetValue(playerId, out var playerCommands))
            {
                return Array.Empty<GameCommand>();
            }

            var result = new List<GameCommand>(playerCommands.Count);
            for (var i = 0; i < playerCommands.Count; i++)
            {
                result.Add(GameCommandCopy.Clone(playerCommands[i]));
            }

            return result;
        }

        public void AdvanceTime(double deltaSeconds)
        {
            if (deltaSeconds < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(deltaSeconds));
            }

            if (IsClosed || deltaSeconds == 0)
            {
                return;
            }

            elapsedSeconds += deltaSeconds;
            for (var i = 0; i < participatingPlayers.Count; i++)
            {
                var playerId = participatingPlayers[i];
                if (confirmations[playerId] != TurnConfirmationReason.None)
                {
                    continue;
                }

                var player = FindPlayer(playerId);
                var overtime = Math.Max(0, elapsedSeconds - BaseTurnSeconds);
                player.ReserveTimeSeconds = Math.Max(
                    0,
                    startingReserveSeconds[playerId] - (int)Math.Ceiling(overtime));

                if (overtime >= startingReserveSeconds[playerId])
                {
                    confirmations[playerId] = TurnConfirmationReason.TimeExpired;
                }
            }
        }

        public List<GameCommand> BuildResolutionBatch()
        {
            if (!IsClosed)
            {
                throw new InvalidOperationException("Commands cannot be revealed before every player is confirmed.");
            }

            var result = new List<GameCommand>();
            for (var i = 0; i < participatingPlayers.Count; i++)
            {
                var playerCommands = commands[participatingPlayers[i]];
                for (var j = 0; j < playerCommands.Count; j++)
                {
                    result.Add(GameCommandCopy.Clone(playerCommands[j]));
                }
            }

            result.Sort(CompareCommands);
            return result;
        }

        private bool AllPlayersConfirmed()
        {
            if (participatingPlayers.Count == 0)
            {
                return true;
            }

            for (var i = 0; i < participatingPlayers.Count; i++)
            {
                if (confirmations[participatingPlayers[i]] == TurnConfirmationReason.None)
                {
                    return false;
                }
            }

            return true;
        }

        private PlayerState FindPlayer(EntityId playerId)
        {
            for (var i = 0; i < state.Players.Count; i++)
            {
                if (state.Players[i].Id == playerId)
                {
                    return state.Players[i];
                }
            }

            throw new InvalidOperationException("Planning player is missing from game state.");
        }

        private static int CompareCommands(GameCommand left, GameCommand right)
        {
            var type = left.Type.CompareTo(right.Type);
            if (type != 0) return type;
            var player = left.PlayerId.CompareTo(right.PlayerId);
            if (player != 0) return player;
            return left.CommandId.CompareTo(right.CommandId);
        }
    }
}
