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
                        var disbanded = maintenance.DisbandedUnitRecords[unitIndex];
                        resolution.Events.Add(CreateEvent(
                            turnNumber,
                            GameEventType.UnitDisbanded,
                            disbanded.UnitId,
                            disbanded.HomeCityId,
                            disbanded.ReturnedFood));
                    }
                    for (var districtIndex = 0; districtIndex < maintenance.SuspendedDistricts.Count; districtIndex++)
                    {
                        resolution.Events.Add(CreateEvent(
                            turnNumber,
                            GameEventType.DistrictMaintenanceSuspended,
                            maintenance.SuspendedDistricts[districtIndex]));
                    }
                    for (var defenseIndex = 0; defenseIndex < maintenance.DeactivatedModernDefenses.Count; defenseIndex++)
                    {
                        resolution.Events.Add(CreateEvent(turnNumber, GameEventType.ModernDefenseDeactivated,
                            maintenance.DeactivatedModernDefenses[defenseIndex]));
                    }
                    for (var defenseIndex = 0; defenseIndex < maintenance.ReactivatedModernDefenses.Count; defenseIndex++)
                    {
                        resolution.Events.Add(CreateEvent(turnNumber, GameEventType.ModernDefenseReactivated,
                            maintenance.ReactivatedModernDefenses[defenseIndex]));
                    }
                }
                if (phase == TurnPhase.ConstructionTrainingProjects)
                {
                    var nuclearProjects = NuclearProjectResolver.Advance(state);
                    for (var projectIndex = 0; projectIndex < nuclearProjects.Count; projectIndex++)
                    {
                        var project = nuclearProjects[projectIndex];
                        resolution.Events.Add(CreateEvent(turnNumber,
                            project.Completed ? GameEventType.NuclearProjectCompleted :
                                GameEventType.NuclearProjectProgressed,
                            project.OwnerId, project.ProjectId, project.RemainingTurns));
                    }
                    var completedDefenses = DefenseFacilityResolver.AdvanceConstruction(state);
                    for (var defenseIndex = 0; defenseIndex < completedDefenses.Count; defenseIndex++)
                    {
                        resolution.Events.Add(CreateEvent(turnNumber,
                            GameEventType.DefenseFacilityConstructionCompleted,
                            completedDefenses[defenseIndex]));
                    }
                    var completedDistricts = DistrictConstructionResolver.Advance(state);
                    for (var completedIndex = 0; completedIndex < completedDistricts.Count; completedIndex++)
                    {
                        resolution.Events.Add(CreateEvent(
                            turnNumber,
                            GameEventType.DistrictConstructionCompleted,
                            completedDistricts[completedIndex]));
                    }
                    var completedRepairs = DistrictConstructionResolver.AdvanceRepairs(state);
                    for (var repairIndex = 0; repairIndex < completedRepairs.Count; repairIndex++)
                    {
                        resolution.Events.Add(CreateEvent(
                            turnNumber,
                            GameEventType.DistrictRepairCompleted,
                            completedRepairs[repairIndex]));
                    }
                    var trainingAdvance = UnitTrainingResolver.Advance(state);
                    for (var unitIndex = 0; unitIndex < trainingAdvance.CompletedUnitIds.Count; unitIndex++)
                    {
                        var completedUnit = FindUnit(state, trainingAdvance.CompletedUnitIds[unitIndex]);
                        resolution.Events.Add(CreateEvent(
                            turnNumber,
                            GameEventType.UnitTrainingCompleted,
                            completedUnit.Id,
                            completedUnit.TileId,
                            (int)completedUnit.Type));
                    }
                    for (var waitingIndex = 0; waitingIndex < trainingAdvance.WaitingTrainingIds.Count; waitingIndex++)
                    {
                        resolution.Events.Add(CreateEvent(
                            turnNumber,
                            GameEventType.UnitDeploymentWaiting,
                            trainingAdvance.WaitingTrainingIds[waitingIndex]));
                    }
                }
                if (phase == TurnPhase.FoodRecoveryStarvation)
                {
                    var returnedFood = GroundFoodResolver.ReturnEligibleFood(state);
                    for (var returnIndex = 0; returnIndex < returnedFood.Count; returnIndex++)
                    {
                        resolution.Events.Add(CreateEvent(
                            turnNumber,
                            GameEventType.GroundFoodReturned,
                            returnedFood[returnIndex].TileId,
                            returnedFood[returnIndex].CityId,
                            returnedFood[returnIndex].Amount));
                    }
                    var foodConsumption = UnitFoodResolver.Consume(state);
                    for (var foodIndex = 0; foodIndex < foodConsumption.SuppliedUnitIds.Count; foodIndex++)
                    {
                        resolution.Events.Add(CreateEvent(
                            turnNumber,
                            GameEventType.UnitFoodConsumed,
                            foodConsumption.SuppliedUnitIds[foodIndex],
                            primaryValue: foodConsumption.Records[foodIndex].Amount,
                            secondaryValue: (int)foodConsumption.Records[foodIndex].Source));
                    }
                    var recoveries = UnitRecoveryResolver.Resolve(state, foodConsumption.SuppliedUnitIds);
                    for (var recoveryIndex = 0; recoveryIndex < recoveries.Count; recoveryIndex++)
                    {
                        resolution.Events.Add(CreateEvent(
                            turnNumber,
                            GameEventType.UnitRecovered,
                            recoveries[recoveryIndex].UnitId,
                            primaryValue: recoveries[recoveryIndex].RecoveredHitPoints));
                    }
                    var starvation = UnitStarvationResolver.ResolveFirstFailure(
                        state,
                        foodConsumption.SuppliedUnitIds,
                        foodConsumption.UnsuppliedUnitIds);
                    for (var starvationIndex = 0;
                         starvationIndex < starvation.EnteredStarvationUnitIds.Count;
                         starvationIndex++)
                    {
                        resolution.Events.Add(CreateEvent(
                            turnNumber,
                            GameEventType.UnitStarvationStarted,
                            starvation.EnteredStarvationUnitIds[starvationIndex]));
                    }
                    for (var starvationIndex = 0;
                         starvationIndex < starvation.RecoveredFromStarvationUnitIds.Count;
                         starvationIndex++)
                    {
                        resolution.Events.Add(CreateEvent(
                            turnNumber,
                            GameEventType.UnitStarvationEnded,
                            starvation.RecoveredFromStarvationUnitIds[starvationIndex]));
                    }
                    for (var starvationIndex = 0;
                         starvationIndex < starvation.StarvedToDeathUnitIds.Count;
                         starvationIndex++)
                    {
                        resolution.Events.Add(CreateEvent(
                            turnNumber,
                            GameEventType.UnitStarvedToDeath,
                            starvation.StarvedToDeathUnitIds[starvationIndex]));
                        resolution.Events.Add(CreateEvent(
                            turnNumber,
                            GameEventType.UnitDestroyed,
                            starvation.StarvedToDeathUnitIds[starvationIndex]));
                    }
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
                        CityCultureRules.Normalize(diminishedCity);
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
                if (phase == TurnPhase.CultureAndConversion)
                {
                    var cultureChanges = CultureConversionResolver.AdvancePlayerCities(state);
                    for (var cultureIndex = 0; cultureIndex < cultureChanges.Count; cultureIndex++)
                    {
                        var change = cultureChanges[cultureIndex];
                        resolution.Events.Add(CreateEvent(turnNumber,
                            GameEventType.CultureInfluenceChanged,
                            change.CultureOwnerId, change.CityId,
                            change.PreferredCitizenDelta,
                            change.ConversionProgress - change.ReversionProgress));
                    }
                    var neutralCulture = NeutralCultureResolver.Advance(state);
                    for (var neutralIndex = 0; neutralIndex < neutralCulture.Count; neutralIndex++)
                    {
                        var change = neutralCulture[neutralIndex];
                        resolution.Events.Add(CreateEvent(turnNumber,
                            GameEventType.NeutralCultureResolved,
                            change.WinningCultureId, change.CityId,
                            change.AppliedInfluence,
                            change.SubjectToId.IsValid ? 1 : 0));
                    }
                    CultureVictoryConditionResolver.UpdateCandidates(state);
                }
                ResolveCommandsForPhase(state, sortedCommands, phase, seenCommandIds, resolution);

                if (phase == TurnPhase.Research)
                {
                    var research = ResearchResolver.Advance(state);
                    for (var researchIndex = 0; researchIndex < research.Count; researchIndex++)
                    {
                        var item = research[researchIndex];
                        resolution.Events.Add(CreateEvent(turnNumber,
                            GameEventType.ResearchProgressed, item.PlayerId,
                            primaryValue: (int)item.Type, secondaryValue: item.TotalProgress));
                        if (item.Completed)
                            resolution.Events.Add(CreateEvent(turnNumber,
                                GameEventType.ResearchCompleted, item.PlayerId,
                                primaryValue: (int)item.Type));
                    }
                }

                if (phase == TurnPhase.CultureVictory)
                {
                    var winner = VictoryResolver.ResolveCulture(state);
                    if (winner.IsValid)
                        resolution.Events.Add(CreateEvent(turnNumber, GameEventType.VictoryTriggered,
                            winner, primaryValue: (int)VictoryType.Culture));
                }

                if (phase == TurnPhase.ScienceVictory)
                {
                    var winner = VictoryResolver.ResolveScience(state);
                    if (winner.IsValid)
                        resolution.Events.Add(CreateEvent(turnNumber, GameEventType.VictoryTriggered,
                            winner, primaryValue: (int)VictoryType.Science));
                }

                if (phase == TurnPhase.TradeAndOrders)
                {
                    AddPlanningDefaults(state, sortedCommands, resolution);
                }

                if (phase == TurnPhase.MovementCombatOccupation)
                {
                    var blockedUnits = ResolveMovementCommands(state, resolution);
                    OccupationResolver.ReleaseVacatedDistricts(state);
                    GroundFoodResolver.ReconcileVacatedOwnership(state);
                    var deployedUnits = UnitTrainingResolver.DeployWaiting(state);
                    for (var deploymentIndex = 0; deploymentIndex < deployedUnits.Count; deploymentIndex++)
                    {
                        var deployed = FindUnit(state, deployedUnits[deploymentIndex]);
                        resolution.Events.Add(CreateEvent(
                            turnNumber,
                            GameEventType.UnitTrainingCompleted,
                            deployed.Id,
                            deployed.TileId,
                            (int)deployed.Type));
                    }
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
                UnitTrainingState startedTraining = null;
                var loadedFood = 0;
                var transferredFood = 0;
                UnitPromotionResult promotion = null;
                DistrictState startedRepair = null;
                DefenseFacilityState startedDefense = null;
                var selectedResearch = ResearchType.None;
                NuclearProjectState startedNuclearProject = null;
                var citizenAssignmentChanged = false;
                var accepted = validation == CommandValidationError.None &&
                               command.Type != GameCommandType.ConfirmTurn &&
                               seenCommandIds.Add(command.CommandId);
                if (accepted && command.Type == GameCommandType.StartDistrict &&
                    !DistrictConstructionResolver.TryStart(state, command, out startedDistrict))
                {
                    accepted = false;
                    validation = CommandValidationError.InvalidPayload;
                }
                if (accepted && command.Type == GameCommandType.AssignCitizen)
                {
                    citizenAssignmentChanged = AgricultureCitizenResolver.TryAssign(state, command);
                    if (!citizenAssignmentChanged)
                    {
                        accepted = false;
                        validation = CommandValidationError.InvalidPayload;
                    }
                }
                if (accepted && command.Type == GameCommandType.SetPriority &&
                    !TrySetPriority(state, command))
                {
                    accepted = false;
                    validation = CommandValidationError.InvalidPayload;
                }
                if (accepted && command.Type == GameCommandType.StartTraining &&
                    !UnitTrainingResolver.TryStart(state, command, out startedTraining))
                {
                    accepted = false;
                    validation = CommandValidationError.InvalidPayload;
                }
                if (accepted && command.Type == GameCommandType.LoadFood &&
                    !UnitFoodResolver.TryLoad(state, command, out loadedFood))
                {
                    accepted = false;
                    validation = CommandValidationError.InvalidPayload;
                }
                if (accepted && command.Type == GameCommandType.TransferFood &&
                    !UnitFoodResolver.TryTransfer(state, command, out transferredFood))
                {
                    accepted = false;
                    validation = CommandValidationError.InvalidPayload;
                }
                if (accepted && command.Type == GameCommandType.PromoteUnit &&
                    !UnitPromotionResolver.TryPromote(state, command, out promotion))
                {
                    accepted = false;
                    validation = CommandValidationError.InvalidPayload;
                }
                if (accepted && command.Type == GameCommandType.RepairDistrict &&
                    !DistrictConstructionResolver.TryStartRepair(state, command, out startedRepair))
                {
                    accepted = false;
                    validation = CommandValidationError.InvalidPayload;
                }
                if (accepted && command.Type == GameCommandType.StartDefenseFacility &&
                    !DefenseFacilityResolver.TryStart(state, command, out startedDefense))
                {
                    accepted = false;
                    validation = CommandValidationError.InvalidPayload;
                }
                if (accepted && command.Type == GameCommandType.SetModernDefenseActive &&
                    !DefenseFacilityResolver.TrySetModernActive(state, command))
                {
                    accepted = false;
                    validation = CommandValidationError.InvalidPayload;
                }
                if (accepted && command.Type == GameCommandType.SelectResearch &&
                    !ResearchResolver.TrySelect(state, command, out selectedResearch))
                {
                    accepted = false;
                    validation = CommandValidationError.InvalidPayload;
                }
                if (accepted && command.Type == GameCommandType.StartNuclearProject &&
                    !NuclearProjectResolver.TryStart(state, command, out startedNuclearProject))
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
                    if (selectedResearch != ResearchType.None)
                    {
                        resolution.Events.Add(CreateEvent(state.TurnNumber,
                            GameEventType.ResearchSelected, command.PlayerId,
                            primaryValue: (int)selectedResearch));
                    }
                    if (startedNuclearProject != null)
                    {
                        resolution.Events.Add(CreateEvent(state.TurnNumber,
                            GameEventType.NuclearProjectStarted, command.PlayerId,
                            startedNuclearProject.Id, startedNuclearProject.RemainingTurns));
                    }
                    if (startedDefense != null)
                    {
                        resolution.Events.Add(CreateEvent(state.TurnNumber,
                            GameEventType.DefenseFacilityConstructionStarted,
                            startedDefense.Id, startedDefense.TileId,
                            (int)startedDefense.BuildingType,
                            startedDefense.RemainingConstructionTurns));
                    }
                    if (command.Type == GameCommandType.SetModernDefenseActive)
                    {
                        resolution.Events.Add(CreateEvent(state.TurnNumber,
                            command.PrimaryValue == 0 ? GameEventType.ModernDefenseDeactivated : GameEventType.ModernDefenseReactivationStarted,
                            command.SubjectId));
                    }
                    if (startedTraining != null)
                    {
                        resolution.Events.Add(CreateEvent(
                            state.TurnNumber,
                            GameEventType.UnitTrainingStarted,
                            startedTraining.DistrictId,
                            startedTraining.Id,
                            (int)startedTraining.Type,
                            startedTraining.RemainingTurns));
                    }
                    if (loadedFood != 0)
                    {
                        resolution.Events.Add(CreateEvent(
                            state.TurnNumber,
                            GameEventType.UnitFoodLoaded,
                            command.SubjectId,
                            command.TargetId,
                            loadedFood));
                    }
                    if (transferredFood > 0)
                    {
                        resolution.Events.Add(CreateEvent(
                            state.TurnNumber,
                            GameEventType.UnitFoodTransferred,
                            command.SubjectId,
                            command.TargetId,
                            transferredFood));
                    }
                    if (promotion != null)
                    {
                        resolution.Events.Add(CreateEvent(
                            state.TurnNumber,
                            GameEventType.UnitPromoted,
                            promotion.UnitId,
                            promotion.HomeCityId,
                            (int)promotion.PromotedType,
                            promotion.GoldCost));
                    }
                    if (startedRepair != null)
                    {
                        resolution.Events.Add(CreateEvent(
                            state.TurnNumber,
                            GameEventType.DistrictRepairStarted,
                            startedRepair.Id,
                            startedRepair.TileId,
                            startedRepair.RemainingRepairTurns));
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
                if (unit != null && unit.ManeuverRecommandTurn <= 0)
                    unit.ManeuverRecommandTurn = state.TurnNumber + 1;
                var completedSteps = 0;
                if (blockedReason == MovementStopReason.PriorityLost &&
                    priorityPlan.BlockedPathIndices.TryGetValue(blockedCommandId, out var blockedPathIndex) &&
                    blockedPathIndex > 0 && unit != null)
                {
                    var prefix = GameCommandCopy.Clone(command);
                    prefix.Path = command.Path.GetRange(0, blockedPathIndex);
                    prefix.TargetId = prefix.Path[prefix.Path.Count - 1];
                    var prefixMovement = MovementResolver.Resolve(state, prefix);
                    completedSteps = prefixMovement.StepsMoved;
                    if (completedSteps > 0)
                    {
                        resolution.Events.Add(CreateEvent(
                            state.TurnNumber,
                            GameEventType.UnitMoved,
                            prefixMovement.UnitId,
                            prefixMovement.FinalTileId,
                            completedSteps));
                    }
                }
                blockedUnits.Add(command.SubjectId);
                AddManeuverRequest(resolution, command, unit, completedSteps, blockedReason);
                resolution.Events.Add(CreateEvent(
                    state.TurnNumber,
                    GameEventType.MovementBlocked,
                    command.SubjectId,
                    unit == null ? default : unit.TileId,
                    completedSteps,
                    (int)blockedReason));
            }

            for (var i = 0; i < priorityPlan.OrderedCommands.Count; i++)
            {
                var command = priorityPlan.OrderedCommands[i];
                var movement = MovementResolver.Resolve(state, command);
                var movedUnit = FindUnit(state, movement.UnitId);
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
                        if (occupation.PillageRewardGranted)
                        {
                            resolution.Events.Add(CreateEvent(
                                state.TurnNumber,
                                GameEventType.DistrictPillaged,
                                command.PlayerId,
                                occupation.DistrictId,
                                occupation.PillagePrimaryReward,
                                occupation.PillageFoodReward));
                        }
                    }
                }

                if (movement.StopReason != MovementStopReason.Completed)
                {
                    blockedUnits.Add(movement.UnitId);
                    if (movedUnit != null && movedUnit.ManeuverRecommandTurn <= 0)
                        movedUnit.ManeuverRecommandTurn = state.TurnNumber + 1;
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
                // A maneuver re-command continues the interrupted movement budget instead of
                // granting a fresh turn's movement. The value left on the unit is the exact
                // budget remaining after its successful prefix movement.
                if (unit.ManeuverRecommandTurn != state.TurnNumber)
                    unit.RemainingMovement = UnitRules.Movement(unit.Type);
                unit.HasAutomaticDefense = false;
            }
        }

        private static void FinalizeAutomaticDefense(GameState state, HashSet<EntityId> blockedUnits)
        {
            for (var i = 0; i < state.Units.Count; i++)
            {
                var unit = state.Units[i];
                if (unit.CreatedTurn == state.TurnNumber || unit.RemainingMovement <= 0 ||
                    blockedUnits.Contains(unit.Id)) continue;
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
            if (type == GameCommandType.SelectResearch) return TurnPhase.Research;
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
