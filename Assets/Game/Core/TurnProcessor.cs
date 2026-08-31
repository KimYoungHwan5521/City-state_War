using System;
using System.Collections.Generic;

namespace LittleCiv.Core
{
    public enum TurnPhase
    {
        CityProduction = 1,
        Maintenance = 2,
        ConstructionTrainingProjects = 3,
        CultureAndConversion = 4,
        CultureVictory = 5,
        Research = 6,
        ScienceVictory = 7,
        FoodRecoveryStarvation = 8,
        Population = 9,
        TradeAndOrders = 10,
        MovementCombatOccupation = 11,
        ConquestVictory = 12
    }

    public enum DefaultActionType
    {
        None = 0,
        KeepCurrentCityWork = 1,
        KeepCurrentResearch = 2,
        CitizenUnassignedAtGovernment = 3,
        UnitWaits = 4,
        IncompleteTradeCancelled = 5,
        KeepExistingPriority = 6
    }

    public sealed class TurnResolution
    {
        public int ResolvedTurnNumber;
        public readonly List<GameCommand> Commands = new List<GameCommand>();
        public readonly List<GameEvent> Events = new List<GameEvent>();
        public ulong ResultStateHash;
    }

    public sealed class TurnProcessor
    {
        private long nextEventSequence = 1;

        public TurnResolution Resolve(GameState state, IReadOnlyList<GameCommand> commands)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (commands == null) throw new ArgumentNullException(nameof(commands));

            var turnNumber = state.TurnNumber;
            var resolution = new TurnResolution { ResolvedTurnNumber = turnNumber };
            var sortedCommands = CopyAndSort(commands);
            var seenCommandIds = new HashSet<EntityId>();
            resolution.Events.Add(CreateEvent(turnNumber, GameEventType.TurnStarted));

            for (var phaseValue = (int)TurnPhase.CityProduction;
                 phaseValue <= (int)TurnPhase.ConquestVictory;
                 phaseValue++)
            {
                var phase = (TurnPhase)phaseValue;
                resolution.Events.Add(CreateEvent(
                    turnNumber,
                    GameEventType.PhaseStarted,
                    primaryValue: phaseValue));
                ResolveCommandsForPhase(state, sortedCommands, phase, seenCommandIds, resolution);

                if (phase == TurnPhase.TradeAndOrders)
                {
                    AddPlanningDefaults(state, sortedCommands, resolution);
                }

                if (phase == TurnPhase.MovementCombatOccupation)
                {
                    AddUnitWaitDefaults(state, sortedCommands, resolution);
                }
            }

            state.TurnNumber++;
            resolution.Events.Add(CreateEvent(turnNumber, GameEventType.TurnEnded));
            resolution.ResultStateHash = GameStateHasher.Compute(state);
            return resolution;
        }

        private void ResolveCommandsForPhase(
            GameState state,
            List<GameCommand> commands,
            TurnPhase phase,
            HashSet<EntityId> seenCommandIds,
            TurnResolution resolution)
        {
            for (var i = 0; i < commands.Count; i++)
            {
                var command = commands[i];
                if (GetPhase(command.Type) != phase)
                {
                    continue;
                }

                var validation = CommandValidator.ValidateEnvelope(state, command);
                var accepted = validation == CommandValidationError.None &&
                               command.Type != GameCommandType.ConfirmTurn &&
                               seenCommandIds.Add(command.CommandId);
                resolution.Events.Add(CreateEvent(
                    state.TurnNumber,
                    accepted ? GameEventType.CommandAccepted : GameEventType.CommandRejected,
                    command.PlayerId,
                    command.SubjectId,
                    (int)command.Type,
                    accepted ? 0 : (int)validation));
                if (accepted)
                {
                    resolution.Commands.Add(GameCommandCopy.Clone(command));
                }
            }
        }

        private void AddPlanningDefaults(
            GameState state,
            List<GameCommand> commands,
            TurnResolution resolution)
        {
            for (var i = 0; i < state.Players.Count; i++)
            {
                var player = state.Players[i];
                if (player.Slot == PlayerSlot.Neutral) continue;

                if (!HasAnyCommand(player.Id, commands, GameCommandType.StartDistrict, GameCommandType.StartTraining))
                {
                    resolution.Events.Add(CreateDefaultEvent(
                        state.TurnNumber, player.Id, DefaultActionType.KeepCurrentCityWork));
                }

                if (!HasAnyCommand(player.Id, commands, GameCommandType.SelectResearch))
                {
                    resolution.Events.Add(CreateDefaultEvent(
                        state.TurnNumber, player.Id, DefaultActionType.KeepCurrentResearch));
                }

                if (!HasAnyCommand(player.Id, commands, GameCommandType.Trade))
                {
                    resolution.Events.Add(CreateDefaultEvent(
                        state.TurnNumber, player.Id, DefaultActionType.IncompleteTradeCancelled));
                }

                if (!HasAnyCommand(player.Id, commands, GameCommandType.SetPriority))
                {
                    resolution.Events.Add(CreateDefaultEvent(
                        state.TurnNumber, player.Id, DefaultActionType.KeepExistingPriority));
                }
            }
        }

        private void AddUnitWaitDefaults(
            GameState state,
            List<GameCommand> commands,
            TurnResolution resolution)
        {
            var units = new List<UnitState>(state.Units);
            units.Sort((left, right) => left.Id.CompareTo(right.Id));
            for (var i = 0; i < units.Count; i++)
            {
                var unit = units[i];
                if (HasSubjectCommand(unit.OwnerId, unit.Id, commands, GameCommandType.MoveUnit)) continue;
                resolution.Events.Add(CreateDefaultEvent(
                    state.TurnNumber,
                    unit.OwnerId,
                    DefaultActionType.UnitWaits,
                    unit.Id));
            }
        }

        private GameEvent CreateDefaultEvent(
            int turnNumber,
            EntityId playerId,
            DefaultActionType action,
            EntityId subjectId = default)
        {
            return CreateEvent(
                turnNumber,
                GameEventType.DefaultActionApplied,
                playerId,
                subjectId,
                (int)action);
        }

        private GameEvent CreateEvent(
            int turnNumber,
            GameEventType type,
            EntityId sourceId = default,
            EntityId targetId = default,
            int primaryValue = 0,
            int secondaryValue = 0)
        {
            return new GameEvent
            {
                Sequence = nextEventSequence++,
                TurnNumber = turnNumber,
                Type = type,
                SourceId = sourceId,
                TargetId = targetId,
                PrimaryValue = primaryValue,
                SecondaryValue = secondaryValue
            };
        }

        private static TurnPhase GetPhase(GameCommandType type)
        {
            return type == GameCommandType.MoveUnit
                ? TurnPhase.MovementCombatOccupation
                : TurnPhase.TradeAndOrders;
        }

        private static bool HasAnyCommand(
            EntityId playerId,
            List<GameCommand> commands,
            params GameCommandType[] types)
        {
            for (var i = 0; i < commands.Count; i++)
            {
                if (commands[i].PlayerId != playerId) continue;
                for (var j = 0; j < types.Length; j++)
                {
                    if (commands[i].Type == types[j]) return true;
                }
            }

            return false;
        }

        private static bool HasSubjectCommand(
            EntityId playerId,
            EntityId subjectId,
            List<GameCommand> commands,
            GameCommandType type)
        {
            for (var i = 0; i < commands.Count; i++)
            {
                var command = commands[i];
                if (command.PlayerId == playerId && command.SubjectId == subjectId && command.Type == type)
                {
                    return true;
                }
            }

            return false;
        }

        private static List<GameCommand> CopyAndSort(IReadOnlyList<GameCommand> source)
        {
            var result = new List<GameCommand>(source.Count);
            for (var i = 0; i < source.Count; i++) result.Add(GameCommandCopy.Clone(source[i]));
            result.Sort((left, right) =>
            {
                var phase = GetPhase(left.Type).CompareTo(GetPhase(right.Type));
                if (phase != 0) return phase;
                var type = left.Type.CompareTo(right.Type);
                if (type != 0) return type;
                var player = left.PlayerId.CompareTo(right.PlayerId);
                if (player != 0) return player;
                return left.CommandId.CompareTo(right.CommandId);
            });
            return result;
        }
    }
}
