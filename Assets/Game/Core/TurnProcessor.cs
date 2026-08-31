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
        public readonly List<ManeuverRequest> ManeuverRequests = new List<ManeuverRequest>();
        public ulong ResultStateHash;
    }

    public sealed class TurnProcessor
    {
        private long nextEventSequence = 1;

        public TurnResolution Resolve(GameState state, IReadOnlyList<GameCommand> commands)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (commands == null) throw new ArgumentNullException(nameof(commands));
            if (state.IsGameOver) throw new InvalidOperationException("A completed match cannot resolve another turn.");

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
                if (phase == TurnPhase.CityProduction)
                {
                    ResetUnitMovement(state);
                    CityEconomyResolver.ResolveProduction(state);
                }
                if (phase == TurnPhase.Maintenance)
                {
                    var maintenance = MaintenanceResolver.Resolve(state);
                    for (var unitIndex = 0; unitIndex < maintenance.DisbandedUnits.Count; unitIndex++)
                    {
                        resolution.Events.Add(CreateEvent(
                            turnNumber,
                            GameEventType.UnitDisbanded,
                            maintenance.DisbandedUnits[unitIndex]));
                    }
                    for (var districtIndex = 0; districtIndex < maintenance.SuspendedDistricts.Count; districtIndex++)
                    {
                        resolution.Events.Add(CreateEvent(
                            turnNumber,
                            GameEventType.DistrictMaintenanceSuspended,
                            maintenance.SuspendedDistricts[districtIndex]));
                    }
                }
                if (phase == TurnPhase.ConstructionTrainingProjects)
                {
                    var completedDistricts = DistrictConstructionResolver.Advance(state);
                    for (var completedIndex = 0; completedIndex < completedDistricts.Count; completedIndex++)
                    {
                        resolution.Events.Add(CreateEvent(
                            turnNumber,
                            GameEventType.DistrictConstructionCompleted,
                            completedDistricts[completedIndex]));
                    }
                }
                if (phase == TurnPhase.FoodRecoveryStarvation)
                {
                    CityFoodResolver.ResolveStorage(state);
                }
                if (phase == TurnPhase.Population)
                {
                    var grownCities = CityPopulationResolver.ResolveGrowth(state);
                    for (var grownIndex = 0; grownIndex < grownCities.Count; grownIndex++)
                    {
                        var grownCity = FindCity(state, grownCities[grownIndex]);
                        resolution.Events.Add(CreateEvent(
                            turnNumber,
                            GameEventType.PopulationIncreased,
                            grownCity.OwnerId,
                            grownCity.Id,
                            grownCity.Population,
                            grownCity.GrowthProgress));
                    }

                    var diminishedCities = CityPopulationResolver.ResolveFamine(state);
                    for (var diminishedIndex = 0; diminishedIndex < diminishedCities.Count; diminishedIndex++)
                    {
                        var diminishedCity = FindCity(state, diminishedCities[diminishedIndex]);
                        var removedDistrictId = CitizenAssignmentResolver.RemoveExcessCitizen(state, diminishedCity);
                        resolution.Events.Add(CreateEvent(
                            turnNumber,
                            GameEventType.PopulationDecreased,
                            diminishedCity.OwnerId,
                            diminishedCity.Id,
                            diminishedCity.Population,
                            diminishedCity.FamineProgress));
                        if (removedDistrictId.IsValid)
                        {
                            resolution.Events.Add(CreateEvent(
                                turnNumber,
                                GameEventType.CitizenAssignmentRemoved,
                                diminishedCity.Id,
                                removedDistrictId));
                        }
                    }
                }
                ResolveCommandsForPhase(state, sortedCommands, phase, seenCommandIds, resolution);

                if (phase == TurnPhase.TradeAndOrders)
                {
                    AddPlanningDefaults(state, sortedCommands, resolution);
                }

                if (phase == TurnPhase.MovementCombatOccupation)
                {
                    var blockedUnits = ResolveMovementCommands(state, resolution);
                    AddUnitWaitDefaults(state, sortedCommands, resolution);
                    FinalizeAutomaticDefense(state, blockedUnits);
                }

                if (phase == TurnPhase.ConquestVictory && state.Victory == VictoryType.Conquest)
                {
                    resolution.Events.Add(CreateEvent(
                        turnNumber,
                        GameEventType.VictoryTriggered,
                        state.WinnerId,
                        primaryValue: (int)VictoryType.Conquest));
                }
            }

            state.TurnNumber++;
            resolution.Events.Add(CreateEvent(turnNumber, GameEventType.TurnEnded));
            resolution.ResultStateHash = GameStateHasher.Compute(state);
            return resolution;
        }

        private static CityState FindCity(GameState state, EntityId cityId)
        {
            for (var index = 0; index < state.Cities.Count; index++)
            {
                if (state.Cities[index].Id == cityId) return state.Cities[index];
            }

            throw new InvalidOperationException("Population resolver returned an unknown city.");
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
                DistrictState startedDistrict = null;
                var accepted = validation == CommandValidationError.None &&
                               command.Type != GameCommandType.ConfirmTurn &&
                               seenCommandIds.Add(command.CommandId);
                if (accepted && command.Type == GameCommandType.StartDistrict &&
                    !DistrictConstructionResolver.TryStart(state, command, out startedDistrict))
                {
                    accepted = false;
                    validation = CommandValidationError.InvalidPayload;
                }
                if (accepted && command.Type == GameCommandType.SetPriority &&
                    !TrySetPriority(state, command))
                {
                    accepted = false;
                    validation = CommandValidationError.InvalidPayload;
                }
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
                    if (startedDistrict != null)
                    {
                        resolution.Events.Add(CreateEvent(
                            state.TurnNumber,
                            GameEventType.DistrictConstructionStarted,
                            command.PlayerId,
                            startedDistrict.Id,
                            (int)startedDistrict.Type,
                            startedDistrict.RemainingConstructionTurns));
                    }
                }
            }
        }

        private static bool TrySetPriority(GameState state, GameCommand command)
        {
            return command.SecondaryValue == 1
                ? MaintenanceResolver.TrySetPriority(state, command)
                : CitizenAssignmentResolver.TrySetRemovalPriority(state, command);
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

        private HashSet<EntityId> ResolveMovementCommands(GameState state, TurnResolution resolution)
        {
            var blockedUnits = new HashSet<EntityId>();
            var priorityPlan = MovementPriorityResolver.Build(state, resolution.Commands);
            var blockedCommandIds = new List<EntityId>(priorityPlan.BlockedCommandReasons.Keys);
            blockedCommandIds.Sort();
            for (var blockedIndex = 0; blockedIndex < blockedCommandIds.Count; blockedIndex++)
            {
                var blockedCommandId = blockedCommandIds[blockedIndex];
                var blockedReason = priorityPlan.BlockedCommandReasons[blockedCommandId];
                var command = FindCommand(resolution.Commands, blockedCommandId);
                if (command == null) continue;
                var unit = FindUnit(state, command.SubjectId);
                blockedUnits.Add(command.SubjectId);
                AddManeuverRequest(resolution, command, unit, 0, blockedReason);
                resolution.Events.Add(CreateEvent(
                    state.TurnNumber,
                    GameEventType.MovementBlocked,
                    command.SubjectId,
                    unit == null ? default : unit.TileId,
                    0,
                    (int)blockedReason));
            }

            for (var i = 0; i < priorityPlan.OrderedCommands.Count; i++)
            {
                var command = priorityPlan.OrderedCommands[i];
                var movement = MovementResolver.Resolve(state, command);
                if (movement.StepsMoved > 0)
                {
                    resolution.Events.Add(CreateEvent(
                        state.TurnNumber,
                        GameEventType.UnitMoved,
                        movement.UnitId,
                        movement.FinalTileId,
                        movement.StepsMoved));
                    var occupation = OccupationResolver.Resolve(
                        state,
                        command.PlayerId,
                        movement.FinalTileId);
                    if (occupation.DistrictOccupied)
                    {
                        resolution.Events.Add(CreateEvent(
                            state.TurnNumber,
                            GameEventType.DistrictOccupied,
                            command.PlayerId,
                            occupation.DistrictId,
                            (int)occupation.DistrictType));
                    }
                }

                if (movement.StopReason != MovementStopReason.Completed)
                {
                    blockedUnits.Add(movement.UnitId);
                    AddManeuverRequest(
                        resolution,
                        command,
                        FindUnit(state, movement.UnitId),
                        movement.StepsMoved,
                        movement.StopReason);
                    resolution.Events.Add(CreateEvent(
                        state.TurnNumber,
                        GameEventType.MovementBlocked,
                        movement.UnitId,
                        movement.FinalTileId,
                        movement.StepsMoved,
                        (int)movement.StopReason));
                }
            }

            return blockedUnits;
        }

        private static void AddManeuverRequest(
            TurnResolution resolution,
            GameCommand command,
            UnitState unit,
            int completedSteps,
            MovementStopReason reason)
        {
            if (unit == null || unit.RemainingMovement <= 0 ||
                (reason != MovementStopReason.EnemyOccupied &&
                 reason != MovementStopReason.PriorityLost &&
                 reason != MovementStopReason.SwapConflict))
            {
                return;
            }

            var blockedTile = command.Path != null && completedSteps < command.Path.Count
                ? command.Path[completedSteps]
                : default;
            resolution.ManeuverRequests.Add(new ManeuverRequest
            {
                PlayerId = unit.OwnerId,
                UnitId = unit.Id,
                LastValidTileId = unit.TileId,
                BlockedTileId = blockedTile,
                RemainingMovement = unit.RemainingMovement,
                StopReason = reason
            });
        }

        private static GameCommand FindCommand(List<GameCommand> commands, EntityId commandId)
        {
            for (var i = 0; i < commands.Count; i++)
            {
                if (commands[i].CommandId == commandId) return commands[i];
            }
            return null;
        }

        private static UnitState FindUnit(GameState state, EntityId unitId)
        {
            for (var i = 0; i < state.Units.Count; i++)
            {
                if (state.Units[i].Id == unitId) return state.Units[i];
            }
            return null;
        }

        private static void ResetUnitMovement(GameState state)
        {
            for (var i = 0; i < state.Units.Count; i++)
            {
                var unit = state.Units[i];
                unit.RemainingMovement = UnitRules.Movement(unit.Type);
                unit.HasAutomaticDefense = false;
            }
        }

        private static void FinalizeAutomaticDefense(GameState state, HashSet<EntityId> blockedUnits)
        {
            for (var i = 0; i < state.Units.Count; i++)
            {
                var unit = state.Units[i];
                if (unit.RemainingMovement <= 0 || blockedUnits.Contains(unit.Id)) continue;
                var tile = FindTile(state, unit.TileId);
                if (tile != null && tile.IsSharedBoundary) continue;
                unit.HasAutomaticDefense = true;
                unit.RemainingMovement = 0;
            }
        }

        private static TileState FindTile(GameState state, EntityId tileId)
        {
            for (var i = 0; i < state.Tiles.Count; i++)
            {
                if (state.Tiles[i].Id == tileId) return state.Tiles[i];
            }
            return null;
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
