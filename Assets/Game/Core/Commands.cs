using System;
using System.Collections.Generic;

namespace LittleCiv.Core
{
    public enum GameCommandType
    {
        None = 0,
        AssignCitizen = 1,
        StartDistrict = 2,
        StartTraining = 3,
        SelectResearch = 4,
        LoadFood = 5,
        MoveUnit = 6,
        Trade = 7,
        LevyBid = 8,
        SetPriority = 9,
        ConfirmTurn = 10,
        TransferFood = 11,
        PromoteUnit = 12
    }

    [Serializable]
    public sealed class GameCommand
    {
        public EntityId CommandId;
        public EntityId PlayerId;
        public int TurnNumber;
        public GameCommandType Type;
        public EntityId SubjectId;
        public EntityId TargetId;
        public int PrimaryValue;
        public int SecondaryValue;
        public List<EntityId> Path = new List<EntityId>();
    }

    public enum CommandValidationError
    {
        None = 0,
        MissingCommandId = 1,
        UnknownPlayer = 2,
        WrongTurn = 3,
        MissingCommandType = 4,
        InvalidPayload = 5
    }

    public enum CommandMutationResult
    {
        Accepted = 0,
        SessionClosed = 1,
        PlayerAlreadyConfirmed = 2,
        InvalidCommand = 3,
        CommandNotFound = 4
    }

    public static class CommandValidator
    {
        public static CommandValidationError ValidateEnvelope(GameState state, GameCommand command)
        {
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            if (command == null)
            {
                throw new ArgumentNullException(nameof(command));
            }

            if (!command.CommandId.IsValid)
            {
                return CommandValidationError.MissingCommandId;
            }

            if (!ContainsPlayer(state.Players, command.PlayerId))
            {
                return CommandValidationError.UnknownPlayer;
            }

            if (command.TurnNumber != state.TurnNumber)
            {
                return CommandValidationError.WrongTurn;
            }

            return command.Type == GameCommandType.None
                ? CommandValidationError.MissingCommandType
                : CommandValidationError.None;
        }

        private static bool ContainsPlayer(List<PlayerState> players, EntityId playerId)
        {
            for (var i = 0; i < players.Count; i++)
            {
                if (players[i].Id == playerId)
                {
                    return true;
                }
            }

            return false;
        }
    }

    public static class GameCommandCopy
    {
        public static GameCommand Clone(GameCommand source)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            return new GameCommand
            {
                CommandId = source.CommandId,
                PlayerId = source.PlayerId,
                TurnNumber = source.TurnNumber,
                Type = source.Type,
                SubjectId = source.SubjectId,
                TargetId = source.TargetId,
                PrimaryValue = source.PrimaryValue,
                SecondaryValue = source.SecondaryValue,
                Path = source.Path == null ? new List<EntityId>() : new List<EntityId>(source.Path)
            };
        }
    }
}
