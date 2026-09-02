using System.Collections.Generic;
using System.Linq;
using LittleCiv.Core;
using UnityEngine;
using UnityEngine.InputSystem;
using GameEntityId = LittleCiv.Core.EntityId;

namespace LittleCiv.Runtime
{
    public sealed class PrototypeMapPresenter : MonoBehaviour
    {
        private const float HexRadius = 1f;
        private const float UiScale = 1.5f;

        private sealed class UnitRoutePlan
        {
            public GameEntityId DestinationId;
            public readonly List<GameEntityId> Path = new List<GameEntityId>();
        }

        private readonly List<GameObject> spawnedViews = new List<GameObject>();
        private readonly Dictionary<GameEntityId, GameCommand> plannedMoves =
            new Dictionary<GameEntityId, GameCommand>();
        private readonly Dictionary<GameEntityId, UnitRoutePlan> routePlans =
            new Dictionary<GameEntityId, UnitRoutePlan>();
        private readonly Dictionary<GameEntityId, GameCommand> plannedDistricts =
            new Dictionary<GameEntityId, GameCommand>();
        private readonly Dictionary<GameEntityId, GameCommand> plannedTrainings =
            new Dictionary<GameEntityId, GameCommand>();
        private readonly Dictionary<GameEntityId, GameCommand> plannedFoodAdjustments =
            new Dictionary<GameEntityId, GameCommand>();
        private readonly Dictionary<GameEntityId, GameCommand> plannedRepairs =
            new Dictionary<GameEntityId, GameCommand>();
        private readonly Dictionary<GameEntityId, GameCommand> plannedDefenseConstructions =
            new Dictionary<GameEntityId, GameCommand>();
        private readonly Dictionary<GameEntityId, GameCommand> plannedDefenseActions =
            new Dictionary<GameEntityId, GameCommand>();
        private readonly Dictionary<GameEntityId, GameCommand> plannedNuclearProjects =
            new Dictionary<GameEntityId, GameCommand>();
        private readonly Dictionary<GameEntityId, GameCommand> plannedResearch =
            new Dictionary<GameEntityId, GameCommand>();
        private readonly Dictionary<GameEntityId, GameCommand> plannedCitizenAssignments =
            new Dictionary<GameEntityId, GameCommand>();
        private readonly Dictionary<string, GameCommand> plannedFoodTransfers =
            new Dictionary<string, GameCommand>();
        private readonly Dictionary<string, GameCommand> plannedNeutralTrades =
            new Dictionary<string, GameCommand>();
        private readonly Dictionary<GameEntityId, GameCommand> plannedLevyBids =
            new Dictionary<GameEntityId, GameCommand>();
        private readonly Dictionary<GameEntityId, Vector3> visibleTilePositions =
            new Dictionary<GameEntityId, Vector3>();
        private GameState state;
        private SimultaneousTurnSimulator simulator;
        private int focusedCityIndex;
        private GameEntityId selectedTileId;
        private GameEntityId selectedUnitId;
        private GameEntityId activePlayerId;
        private bool hasFramedWorld;
        private string statusMessage = "Select a unit, then click an adjacent tile.";
        private readonly List<string> turnLog = new List<string>();
        private readonly List<string> combatLog = new List<string>();
        private Material buildableMaterial;
        private Material boundaryMaterial;
        private Material governmentMaterial;
        private Material selectedMaterial;
        private Material playerOneUnitMaterial;
        private Material playerTwoUnitMaterial;
        private Material neutralUnitMaterial;
        private Material agricultureMaterial;
        private Material commerceMaterial;
        private Material scienceMaterial;
        private Material cultureMaterial;
        private Material militaryMaterial;
        private Material constructionMaterial;
        private Material routeMaterial;
        private Material tileOutlineMaterial;
        private Material groundFoodMaterial;
        private Material wallDefenseMaterial;
        private Material moatDefenseMaterial;
        private Material modernDefenseMaterial;
        private Texture2D routeTurnMarker;
        private bool showResearchPanel;
        private Vector2 researchScroll;
        private int levyExtraBid;

        private void Start()
        {
            state = PrototypeMatchFactory.Create(20260831);
            simulator = new SimultaneousTurnSimulator(state);
            activePlayerId = FindPlayer(PlayerSlot.PlayerOne).Id;
            CreateMaterials();
            EnsureCamera();
            ShowCities(new[] { state.Cities[0].Id });
        }

        private void Update()
        {
            if (Keyboard.current != null && Keyboard.current.tabKey.wasPressedThisFrame)
            {
                focusedCityIndex = (focusedCityIndex + 1) % state.Cities.Count;
                selectedTileId = default(GameEntityId);
                selectedUnitId = default(GameEntityId);
                ShowCities(new[] { state.Cities[focusedCityIndex].Id });
            }

            HandlePointerSelection();
        }

        private void HandlePointerSelection()
        {
            if (Mouse.current == null) return;
            var issueMove = Mouse.current.rightButton.wasPressedThisFrame;
            var select = Mouse.current.leftButton.wasPressedThisFrame;
            if (!issueMove && !select) return;
            var screenPosition = Mouse.current.position.ReadValue();
            if (IsPointerOverHud(screenPosition)) return;
            var camera = Camera.main;
            if (camera == null) return;

            var ray = camera.ScreenPointToRay(screenPosition);
            if (!Physics.Raycast(ray, out var hit, 100f))
            {
                statusMessage = "No tile was found under the pointer.";
                return;
            }

            var tileView = hit.collider.GetComponentInParent<PrototypeHexTileView>();
            if (tileView == null)
            {
                statusMessage = "The selected object is not attached to a city tile.";
                return;
            }
            if (issueMove)
            {
                if (!selectedUnitId.IsValid)
                {
                    statusMessage = "Select one of your units before issuing a right-click move order.";
                    return;
                }
                TryAppendMove(tileView.TileId);
                return;
            }
            SelectTile(tileView.TileId);
        }

        private bool IsPointerOverHud(Vector2 screenPosition)
        {
            var guiPosition = new Vector2(screenPosition.x, Screen.height - screenPosition.y) / UiScale;
            if (new Rect(16f, 16f, 410f, 425f).Contains(guiPosition)) return true;
            if (showResearchPanel && ResearchPanelRect().Contains(guiPosition)) return true;
            if (!selectedTileId.IsValid) return false;
            var logicalWidth = Screen.width / UiScale;
            var compact = logicalWidth < 900f;
            var panelRect = compact
                ? new Rect(16f, 416f, 414f, 600f)
                : new Rect(logicalWidth - 430f, 16f, 414f, 600f);
            return panelRect.Contains(guiPosition);
        }

        public void SelectTile(GameEntityId tileId)
        {
            selectedUnitId = default(GameEntityId);
            selectedTileId = tileId;
            var selectedDistrict = state.Districts.Find(item => item.TileId == tileId);
            statusMessage = selectedDistrict != null
                ? $"{selectedDistrict.Type} selected. District actions are shown in the tile panel."
                : "Undeveloped tile selected. Choose a district from the tile panel.";
            var tile = state.Tiles.Find(item => item.Id == tileId);
            var fallbackCityId = state.Cities[focusedCityIndex].Id;
            ShowCities(MapVisibilityResolver.ResolveCitiesForTile(state, tile.Id, fallbackCityId));
        }

        public void SelectUnit(GameEntityId unitId, GameEntityId tileId)
        {
            var unit = state.Units.Find(item => item.Id == unitId);
            if (unit == null || unit.OwnerId != activePlayerId)
            {
                statusMessage = "You can only order the active player's units.";
                SelectTile(tileId);
                return;
            }
            if (IsManeuverRecommandPhase() && unit.ManeuverRecommandTurn != state.TurnNumber)
            {
                statusMessage = "Only units awaiting maneuver re-command can receive orders in this phase.";
                return;
            }

            selectedUnitId = unitId;
            selectedTileId = tileId;
            statusMessage = $"Selected {unit.Type} {unit.Id}. Click any destination tile to plan its route.";
            var fallbackCityId = state.Cities[focusedCityIndex].Id;
            ShowCities(MapVisibilityResolver.ResolveCitiesForTile(state, tileId, fallbackCityId));
        }

        private bool TryAppendMove(GameEntityId tileId)
        {
            if (!selectedUnitId.IsValid) return false;
            var unit = state.Units.Find(item => item.Id == selectedUnitId);
            if (unit == null || unit.OwnerId != activePlayerId) return true;
            if (tileId == unit.TileId)
            {
                statusMessage = "The unit is already on that tile.";
                return true;
            }

            var path = FindShortestTilePath(unit, tileId);
            if (path.Count == 0)
            {
                statusMessage = "No route to that destination was found.";
                return true;
            }

            if (plannedMoves.TryGetValue(unit.Id, out var oldCommand))
            {
                simulator.Planning.Cancel(activePlayerId, oldCommand.CommandId);
                plannedMoves.Remove(unit.Id);
            }

            var route = new UnitRoutePlan { DestinationId = tileId };
            route.Path.AddRange(path);
            routePlans[unit.Id] = route;
            var reserved = ReserveNextRouteSegment(unit, route);
            var turns = CalculateRouteTurns(unit, route.Path.Count);
            statusMessage = reserved
                ? $"Route planned: {route.Path.Count} tiles, estimated arrival in {turns} turn(s)."
                : $"Long-term route saved. This unit cannot move during the current turn.";
            var fallbackCityId = state.Cities[focusedCityIndex].Id;
            ShowCities(MapVisibilityResolver.ResolveCitiesForTile(state, unit.TileId, fallbackCityId));
            return true;
        }

        private List<GameEntityId> FindShortestTilePath(UnitState movingUnit, GameEntityId destinationId)
        {
            var startId = movingUnit.TileId;
            var frontier = new Queue<GameEntityId>();
            var previous = new Dictionary<GameEntityId, GameEntityId>();
            frontier.Enqueue(startId);
            previous[startId] = default;

            while (frontier.Count > 0)
            {
                var current = frontier.Dequeue();
                if (current == destinationId) break;
                for (var index = 0; index < state.Tiles.Count; index++)
                {
                    var next = state.Tiles[index].Id;
                    if (previous.ContainsKey(next) || !MapTraversal.AreAdjacent(state, current, next)) continue;
                    if (next != destinationId && HasEnemyUnit(movingUnit, next)) continue;
                    previous[next] = current;
                    frontier.Enqueue(next);
                }
            }

            if (!previous.ContainsKey(destinationId)) return new List<GameEntityId>();
            var result = new List<GameEntityId>();
            var cursor = destinationId;
            while (cursor != startId)
            {
                result.Add(cursor);
                cursor = previous[cursor];
            }
            result.Reverse();
            return result;
        }

        private bool HasEnemyUnit(UnitState movingUnit, GameEntityId tileId)
        {
            return state.Units.Any(item =>
                item.TileId == tileId && item.OwnerId != movingUnit.OwnerId);
        }

        private bool ReserveNextRouteSegment(UnitState unit, UnitRoutePlan route)
        {
            var plannedMovement = PlannedMovementForTurn(unit);
            if (plannedMovement <= 0 || route.Path.Count == 0) return false;
            var command = new GameCommand
            {
                CommandId = state.AllocateId(), PlayerId = unit.OwnerId,
                TurnNumber = state.TurnNumber, Type = GameCommandType.MoveUnit,
                SubjectId = unit.Id
            };
            var count = Mathf.Min(plannedMovement, route.Path.Count);
            for (var index = 0; index < count; index++) command.Path.Add(route.Path[index]);
            command.TargetId = command.Path[command.Path.Count - 1];
            var result = simulator.Planning.Reserve(command);
            if (result != CommandMutationResult.Accepted) return false;
            plannedMoves[unit.Id] = command;
            return true;
        }

        private void PrepareAutomaticRoutes(GameEntityId playerId)
        {
            var unitIds = routePlans.Keys.OrderBy(id => id.Value).ToList();
            for (var index = 0; index < unitIds.Count; index++)
            {
                var unitId = unitIds[index];
                var unit = state.Units.Find(item => item.Id == unitId);
                var route = routePlans[unitId];
                if (unit == null || unit.OwnerId != playerId)
                {
                    if (unit == null) routePlans.Remove(unitId);
                    continue;
                }
                if (IsManeuverRecommandPhase() && unit.ManeuverRecommandTurn != state.TurnNumber)
                    continue;
                if (unit.TileId == route.DestinationId)
                {
                    routePlans.Remove(unitId);
                    continue;
                }
                route.Path.Clear();
                route.Path.AddRange(FindShortestTilePath(unit, route.DestinationId));
                if (route.Path.Count == 0)
                {
                    routePlans.Remove(unitId);
                    continue;
                }
                ReserveNextRouteSegment(unit, route);
            }
        }

        private int CalculateRouteTurns(UnitState unit, int stepCount)
        {
            var firstTurnMovement = PlannedMovementForTurn(unit);
            if (stepCount <= firstTurnMovement) return 1;
            var laterMovement = Mathf.Max(1, UnitRules.Movement(unit.Type));
            return 1 + Mathf.CeilToInt((stepCount - firstTurnMovement) / (float)laterMovement);
        }

        private int PlannedMovementForTurn(UnitState unit)
        {
            if (unit.CreatedTurn == state.TurnNumber) return 0;
            return unit.ManeuverRecommandTurn == state.TurnNumber
                ? Mathf.Max(0, unit.RemainingMovement)
                : UnitRules.Movement(unit.Type);
        }

        private void CancelSelectedMove()
        {
            if (!selectedUnitId.IsValid || !routePlans.ContainsKey(selectedUnitId)) return;
            if (plannedMoves.TryGetValue(selectedUnitId, out var command))
                simulator.Planning.Cancel(activePlayerId, command.CommandId);
            plannedMoves.Remove(selectedUnitId);
            routePlans.Remove(selectedUnitId);
            statusMessage = "The selected unit's route was cancelled.";
            ShowCities(new[] { state.Cities[focusedCityIndex].Id });
        }

        private void ConfirmActivePlayer()
        {
            if (!simulator.Planning.Confirm(activePlayerId)) return;
            if (!simulator.Planning.IsClosed)
            {
                var next = FindNextUnconfirmedPlayer();
                if (next == null) return;
                activePlayerId = next.Id;
                selectedUnitId = default;
                selectedTileId = default;
                statusMessage = PlanningTurnStatus(next);
                PrepareAutomaticRoutes(activePlayerId);
                FocusOwnedCity(next.Id);
                return;
            }

            var resolution = simulator.ResolveConfirmedTurn();
            ResolveManeuversAsCombat(resolution);
            for (var eventIndex = 0; eventIndex < resolution.Events.Count; eventIndex++)
            {
                var gameEvent = resolution.Events[eventIndex];
                if (gameEvent.Type == GameEventType.MovementBlocked)
                    AddCombatLog($"T{resolution.ResolvedTurnNumber} MOVE BLOCKED: unit {gameEvent.SourceId}, " +
                                 $"reason {(MovementStopReason)gameEvent.SecondaryValue}");
                if (gameEvent.Type == GameEventType.DistrictPillaged)
                    AddCombatLog($"T{resolution.ResolvedTurnNumber} PILLAGE: district {gameEvent.TargetId} | " +
                                 $"reward {gameEvent.PrimaryValue}, food {gameEvent.SecondaryValue}");
            }
            turnLog.Insert(0, $"Turn {resolution.ResolvedTurnNumber}: {resolution.Commands.Count} orders, " +
                              $"{resolution.ManeuverRequests.Count} clashes.");
            if (turnLog.Count > 5) turnLog.RemoveAt(turnLog.Count - 1);
            plannedMoves.Clear();
            plannedDistricts.Clear();
            plannedTrainings.Clear();
            plannedFoodAdjustments.Clear();
            plannedRepairs.Clear();
            plannedDefenseConstructions.Clear();
            plannedDefenseActions.Clear();
            plannedNuclearProjects.Clear();
            plannedResearch.Clear();
            plannedCitizenAssignments.Clear();
            plannedFoodTransfers.Clear();
            plannedNeutralTrades.Clear();
            plannedLevyBids.Clear();
            levyExtraBid = 0;
            selectedUnitId = default;
            selectedTileId = default;
            ConfigurePlanningPlayers();
            statusMessage = state.IsGameOver
                ? $"GAME OVER — {FindPlayer(state.WinnerId).Slot} wins by {state.Victory}."
                : PlanningTurnStatus(FindPlayer(activePlayerId));
            if (!state.IsGameOver) PrepareAutomaticRoutes(activePlayerId);
            FocusOwnedCity(activePlayerId);
        }

        private void ConfigurePlanningPlayers()
        {
            var playerOne = FindPlayer(PlayerSlot.PlayerOne);
            var playerTwo = FindPlayer(PlayerSlot.PlayerTwo);
            if (!IsManeuverRecommandPhase())
            {
                activePlayerId = playerOne.Id;
                return;
            }

            var playerOneHasOrders = HasManeuverRecommandUnits(playerOne.Id);
            var playerTwoHasOrders = HasManeuverRecommandUnits(playerTwo.Id);
            if (!playerOneHasOrders) simulator.Planning.Confirm(playerOne.Id);
            if (!playerTwoHasOrders) simulator.Planning.Confirm(playerTwo.Id);
            activePlayerId = playerOneHasOrders ? playerOne.Id : playerTwo.Id;
        }

        private PlayerState FindNextUnconfirmedPlayer()
        {
            var players = state.Players
                .Where(item => item.Slot != PlayerSlot.Neutral)
                .OrderBy(item => item.Slot)
                .ToList();
            return players.Find(item =>
                simulator.Planning.GetConfirmation(item.Id) == TurnConfirmationReason.None);
        }

        private bool IsManeuverRecommandPhase()
        {
            return state.Units.Any(item =>
                item.ManeuverRecommandTurn == state.TurnNumber && item.RemainingMovement > 0);
        }

        private bool HasManeuverRecommandUnits(GameEntityId playerId)
        {
            return state.Units.Any(item =>
                item.OwnerId == playerId && item.ManeuverRecommandTurn == state.TurnNumber &&
                item.RemainingMovement > 0);
        }

        private string PlanningTurnStatus(PlayerState player)
        {
            var units = state.Units.FindAll(item =>
                item.OwnerId == player.Id && item.ManeuverRecommandTurn == state.TurnNumber);
            if (units.Count == 0)
                return $"Turn {state.TurnNumber}: {player.Slot} plans orders.";
            var remaining = string.Join(", ", units
                .OrderBy(item => item.Id.Value)
                .Select(item => $"{item.Type} {item.Id} move {item.RemainingMovement}"));
            return $"MANEUVER RE-COMMAND TURN — {player.Slot}: {remaining}";
        }

        private void ResolveManeuversAsCombat(TurnResolution resolution)
        {
            for (var i = 0; i < resolution.ManeuverRequests.Count; i++)
            {
                var request = resolution.ManeuverRequests[i];
                routePlans.Remove(request.UnitId);
                plannedMoves.Remove(request.UnitId);
                var maneuverUnit = state.Units.Find(item => item.Id == request.UnitId);
                if (maneuverUnit == null) continue;
                if (maneuverUnit.ManeuverRecommandTurn != resolution.ResolvedTurnNumber)
                {
                    AddCombatLog($"T{resolution.ResolvedTurnNumber} RECOMMAND REQUIRED: unit {request.UnitId} " +
                                 $"stopped before tile {request.BlockedTileId}; previous route cancelled");
                    continue;
                }
                var applied = ManeuverResolutionApplier.Apply(state, new ManeuverResolution
                {
                    PlayerId = request.PlayerId,
                    UnitId = request.UnitId,
                    LastValidTileId = request.LastValidTileId,
                    BlockedTileId = request.BlockedTileId,
                    StopReason = request.StopReason,
                    Choice = ManeuverChoice.Fight,
                    Reason = ManeuverResolutionReason.PlayerChoice
                });
                if (applied.Combat != null)
                {
                    if (ShouldForceNeutralDetourAttackers(request, resolution.ResolvedTurnNumber))
                        applied.Combat.BothSidesAreAttackers = true;
                    var combat = CombatResolver.Resolve(state, applied.Combat);
                    var survivingManeuverUnit = state.Units.Find(item => item.Id == request.UnitId);
                    if (survivingManeuverUnit != null) survivingManeuverUnit.ManeuverRecommandTurn = 0;
                    turnLog.Insert(0, $"Clash: unit {combat.AttackingUnitId}, " +
                                      $"{combat.DestroyedUnitIds.Count} destroyed.");
                    AddCombatLog($"T{resolution.ResolvedTurnNumber} COMBAT: unit {combat.AttackingUnitId} → tile " +
                                 $"{combat.TargetTileId} | " +
                                 (applied.Combat.BothSidesAreAttackers ? "attack vs attack" : "attack vs defense"));
                    for (var damageIndex = 0; damageIndex < combat.DamageRecords.Count; damageIndex++)
                    {
                        var damage = combat.DamageRecords[damageIndex];
                        AddCombatLog($"  unit {damage.UnitId}: -{damage.Damage} HP" +
                                     (damage.Destroyed ? " | DESTROYED" : string.Empty));
                    }
                    AddCombatLog(combat.AttackerAdvanced
                        ? "  attacker advanced into the target tile"
                        : "  attacker did not take the target tile");
                    if (combat.Occupation != null && combat.Occupation.PillageRewardGranted)
                    {
                        AddCombatLog($"  PILLAGE {combat.Occupation.DistrictType}: " +
                                     $"reward {combat.Occupation.PillagePrimaryReward}, " +
                                     $"food {combat.Occupation.PillageFoodReward}");
                    }
                }
            }
            for (var unitIndex = 0; unitIndex < state.Units.Count; unitIndex++)
            {
                if (state.Units[unitIndex].ManeuverRecommandTurn == resolution.ResolvedTurnNumber)
                    state.Units[unitIndex].ManeuverRecommandTurn = 0;
            }
            OccupationResolver.ReleaseVacatedDistricts(state);
            GroundFoodResolver.ReconcileVacatedOwnership(state);
        }

        private bool ShouldForceNeutralDetourAttackers(ManeuverRequest request, int resolvedTurnNumber)
        {
            var tile = state.Tiles.Find(item => item.Id == request.BlockedTileId);
            if (tile == null) return false;
            var city = state.Cities.Find(item => item.Id == tile.CityId);
            var owner = city == null ? null : state.Players.Find(item => item.Id == city.OwnerId);
            if (owner == null || owner.Slot != PlayerSlot.Neutral) return false;
            return state.Units.Any(item => item.TileId == request.BlockedTileId &&
                                           item.OwnerId != request.PlayerId &&
                                           item.ManeuverRecommandTurn == resolvedTurnNumber);
        }

        private void AddCombatLog(string message)
        {
            combatLog.Insert(0, message);
            if (combatLog.Count > 8) combatLog.RemoveAt(combatLog.Count - 1);
        }

        private void FocusOwnedCity(GameEntityId playerId)
        {
            focusedCityIndex = state.Cities.FindIndex(city => city.OwnerId == playerId);
            if (focusedCityIndex < 0) focusedCityIndex = 0;
            ShowCities(new[] { state.Cities[focusedCityIndex].Id });
        }

        private PlayerState FindPlayer(PlayerSlot slot)
        {
            return state.Players.Find(player => player.Slot == slot);
        }

        private PlayerState FindPlayer(GameEntityId id)
        {
            return state.Players.Find(player => player.Id == id);
        }

        private void ReserveDistrictConstruction(DistrictType type)
        {
            if (IsManeuverRecommandPhase())
            {
                statusMessage = "City orders are unavailable during maneuver re-command.";
                return;
            }
            if (!selectedTileId.IsValid || plannedDistricts.ContainsKey(selectedTileId)) return;
            var city = FindActiveOwnedCityForTile(selectedTileId);
            if (city == null)
            {
                statusMessage = "Construction is only available on your city's buildable tiles.";
                return;
            }
            var freeCitizens = DistrictConstructionResolver.CountFreeCitizens(state, city) -
                               CountPlannedDistricts(city.Id);
            if (freeCitizens <= 0)
            {
                statusMessage = "No unassigned citizen is available for construction.";
                return;
            }

            var command = new GameCommand
            {
                CommandId = state.AllocateId(),
                PlayerId = activePlayerId,
                TurnNumber = state.TurnNumber,
                Type = GameCommandType.StartDistrict,
                SubjectId = city.Id,
                TargetId = selectedTileId,
                PrimaryValue = (int)type
            };
            var result = simulator.Planning.Reserve(command);
            if (result != CommandMutationResult.Accepted)
            {
                statusMessage = $"Construction order rejected: {result}";
                return;
            }

            plannedDistricts[selectedTileId] = command;
            statusMessage = $"{type} construction reserved. One citizen will build it.";
            ShowCities(new[] { city.Id });
        }

        private void CancelSelectedConstruction()
        {
            if (!plannedDistricts.TryGetValue(selectedTileId, out var command)) return;
            simulator.Planning.Cancel(activePlayerId, command.CommandId);
            plannedDistricts.Remove(selectedTileId);
            statusMessage = "Construction order cancelled; the citizen is free again.";
            ShowCities(new[] { command.SubjectId });
        }

        private void ReserveAgricultureCitizens(DistrictState district, int desiredCitizens)
        {
            if (IsManeuverRecommandPhase())
            {
                statusMessage = "Citizen orders are unavailable during maneuver re-command.";
                return;
            }
            if (plannedCitizenAssignments.TryGetValue(district.Id, out var previous))
                simulator.Planning.Cancel(activePlayerId, previous.CommandId);
            var command = new GameCommand
            {
                CommandId = state.AllocateId(), PlayerId = activePlayerId, TurnNumber = state.TurnNumber,
                Type = GameCommandType.AssignCitizen, SubjectId = district.Id, PrimaryValue = desiredCitizens
            };
            var result = simulator.Planning.Reserve(command);
            if (result == CommandMutationResult.Accepted) plannedCitizenAssignments[district.Id] = command;
            statusMessage = result == CommandMutationResult.Accepted
                ? $"Agriculture staffing change reserved: {desiredCitizens} citizen(s)."
                : $"Citizen order rejected: {result}";
        }

        private void CancelAgricultureCitizens(DistrictState district)
        {
            if (!plannedCitizenAssignments.TryGetValue(district.Id, out var command)) return;
            simulator.Planning.Cancel(activePlayerId, command.CommandId);
            plannedCitizenAssignments.Remove(district.Id);
            statusMessage = "Agriculture staffing change cancelled.";
        }

        private void ReserveTraining(DistrictState district, UnitType type)
        {
            if (IsManeuverRecommandPhase())
            {
                statusMessage = "Training orders are unavailable during maneuver re-command.";
                return;
            }
            if (plannedTrainings.ContainsKey(district.Id))
            {
                statusMessage = "This military district already has a reserved training order.";
                return;
            }
            var command = new GameCommand
            {
                CommandId = state.AllocateId(),
                PlayerId = activePlayerId,
                TurnNumber = state.TurnNumber,
                Type = GameCommandType.StartTraining,
                SubjectId = district.Id,
                PrimaryValue = (int)type
            };
            var result = simulator.Planning.Reserve(command);
            if (result == CommandMutationResult.Accepted) plannedTrainings[district.Id] = command;
            statusMessage = result == CommandMutationResult.Accepted
                ? $"{type} training reserved at {district.Type}."
                : $"Training order rejected: {result}";
        }

        private void ReserveDistrictRepair(DistrictState district)
        {
            if (IsManeuverRecommandPhase())
            {
                statusMessage = "Repair orders are unavailable during maneuver re-command.";
                return;
            }
            if (plannedRepairs.ContainsKey(district.Id)) return;
            var command = new GameCommand
            {
                CommandId = state.AllocateId(), PlayerId = activePlayerId,
                TurnNumber = state.TurnNumber, Type = GameCommandType.RepairDistrict,
                SubjectId = district.Id, TargetId = district.TileId
            };
            var result = simulator.Planning.Reserve(command);
            if (result == CommandMutationResult.Accepted) plannedRepairs[district.Id] = command;
            statusMessage = result == CommandMutationResult.Accepted
                ? $"{district.Type} repair reserved."
                : $"Repair order rejected: {result}";
        }

        private void CancelDistrictRepair(DistrictState district)
        {
            if (!plannedRepairs.TryGetValue(district.Id, out var command)) return;
            simulator.Planning.Cancel(activePlayerId, command.CommandId);
            plannedRepairs.Remove(district.Id);
            statusMessage = $"{district.Type} repair cancelled.";
        }

        private void ReserveDefenseConstruction(DistrictState district, DefenseFacilityType type)
        {
            if (IsManeuverRecommandPhase())
            {
                statusMessage = "Defense construction is unavailable during maneuver re-command.";
                return;
            }
            if (plannedDefenseConstructions.ContainsKey(district.TileId)) return;
            var command = new GameCommand
            {
                CommandId = state.AllocateId(), PlayerId = activePlayerId,
                TurnNumber = state.TurnNumber, Type = GameCommandType.StartDefenseFacility,
                SubjectId = district.CityId, TargetId = district.TileId, PrimaryValue = (int)type
            };
            var result = simulator.Planning.Reserve(command);
            if (result == CommandMutationResult.Accepted)
                plannedDefenseConstructions[district.TileId] = command;
            statusMessage = result == CommandMutationResult.Accepted
                ? $"{type} construction reserved ({DefenseFacilityResolver.GoldCost(type)} gold)."
                : $"Defense construction rejected: {result}";
            if (result == CommandMutationResult.Accepted) ShowCities(new[] { district.CityId });
        }

        private void CancelDefenseConstruction(GameEntityId tileId)
        {
            if (!plannedDefenseConstructions.TryGetValue(tileId, out var command)) return;
            simulator.Planning.Cancel(activePlayerId, command.CommandId);
            plannedDefenseConstructions.Remove(tileId);
            statusMessage = "Defense construction cancelled.";
            ShowCities(new[] { command.SubjectId });
        }

        private void ReserveModernDefenseAction(DefenseFacilityState facility, bool activate)
        {
            if (IsManeuverRecommandPhase())
            {
                statusMessage = "Defense controls are unavailable during maneuver re-command.";
                return;
            }
            if (plannedDefenseActions.TryGetValue(facility.Id, out var previous))
                simulator.Planning.Cancel(activePlayerId, previous.CommandId);
            var command = new GameCommand
            {
                CommandId = state.AllocateId(), PlayerId = activePlayerId,
                TurnNumber = state.TurnNumber, Type = GameCommandType.SetModernDefenseActive,
                SubjectId = facility.Id, PrimaryValue = activate ? 1 : 0
            };
            var result = simulator.Planning.Reserve(command);
            if (result == CommandMutationResult.Accepted) plannedDefenseActions[facility.Id] = command;
            else plannedDefenseActions.Remove(facility.Id);
            statusMessage = result == CommandMutationResult.Accepted
                ? activate ? "Modern defense reactivation reserved (2 paid turns)." : "Modern defense deactivation reserved."
                : $"Defense control rejected: {result}";
        }

        private void CancelModernDefenseAction(DefenseFacilityState facility)
        {
            if (!plannedDefenseActions.TryGetValue(facility.Id, out var command)) return;
            simulator.Planning.Cancel(activePlayerId, command.CommandId);
            plannedDefenseActions.Remove(facility.Id);
            statusMessage = "Modern defense control change cancelled.";
        }

        private void ReserveNuclearProject(DistrictState district)
        {
            if (IsManeuverRecommandPhase() || plannedNuclearProjects.ContainsKey(district.Id)) return;
            var command = new GameCommand
            {
                CommandId = state.AllocateId(), PlayerId = activePlayerId,
                TurnNumber = state.TurnNumber, Type = GameCommandType.StartNuclearProject,
                SubjectId = district.Id
            };
            var result = simulator.Planning.Reserve(command);
            if (result == CommandMutationResult.Accepted) plannedNuclearProjects[district.Id] = command;
            statusMessage = result == CommandMutationResult.Accepted
                ? "Nuclear weapon project reserved (10 gold / 3 turns)."
                : $"Nuclear project rejected: {result}";
        }

        private void CancelNuclearProject(DistrictState district)
        {
            if (!plannedNuclearProjects.TryGetValue(district.Id, out var command)) return;
            simulator.Planning.Cancel(activePlayerId, command.CommandId);
            plannedNuclearProjects.Remove(district.Id);
            statusMessage = "Nuclear project reservation cancelled.";
        }

        private void ReserveResearch(ResearchType type)
        {
            if (IsManeuverRecommandPhase())
            {
                statusMessage = "Research selection is unavailable during maneuver re-command.";
                return;
            }
            if (plannedResearch.TryGetValue(activePlayerId, out var previous))
                simulator.Planning.Cancel(activePlayerId, previous.CommandId);
            var command = new GameCommand
            {
                CommandId = state.AllocateId(), PlayerId = activePlayerId,
                TurnNumber = state.TurnNumber, Type = GameCommandType.SelectResearch,
                PrimaryValue = (int)type
            };
            var result = simulator.Planning.Reserve(command);
            if (result == CommandMutationResult.Accepted) plannedResearch[activePlayerId] = command;
            else plannedResearch.Remove(activePlayerId);
            statusMessage = result == CommandMutationResult.Accepted
                ? $"Research selection reserved: {type}."
                : $"Research selection rejected: {result}";
        }

        private void CancelResearchReservation()
        {
            if (!plannedResearch.TryGetValue(activePlayerId, out var command)) return;
            simulator.Planning.Cancel(activePlayerId, command.CommandId);
            plannedResearch.Remove(activePlayerId);
            statusMessage = "Research selection change cancelled; current research will continue.";
        }

        private void AdjustSelectedUnitFood(UnitState unit, int change)
        {
            if (IsManeuverRecommandPhase())
            {
                statusMessage = "Food adjustment is unavailable during maneuver re-command.";
                return;
            }
            var city = FindOwnedCityForUnit(unit);
            if (city == null)
            {
                statusMessage = "Food can only be adjusted while the unit is in controlled home territory.";
                return;
            }

            plannedFoodAdjustments.TryGetValue(unit.Id, out var existing);
            var currentAdjustment = existing == null ? 0 : existing.PrimaryValue;
            var minimum = -unit.CarriedFood;
            var maximum = Mathf.Min(UnitRules.FoodCapacity(state, unit) - unit.CarriedFood, city.StoredFood);
            var adjustment = Mathf.Clamp(currentAdjustment + change, minimum, maximum);
            if (adjustment == currentAdjustment) return;

            if (adjustment == 0)
            {
                if (existing != null) simulator.Planning.Cancel(activePlayerId, existing.CommandId);
                plannedFoodAdjustments.Remove(unit.Id);
                statusMessage = "Food adjustment cancelled.";
                return;
            }

            var command = existing ?? new GameCommand
            {
                CommandId = state.AllocateId(), PlayerId = activePlayerId,
                TurnNumber = state.TurnNumber, Type = GameCommandType.LoadFood,
                SubjectId = unit.Id, TargetId = city.Id
            };
            command.PrimaryValue = adjustment;
            var result = simulator.Planning.Reserve(command);
            if (result != CommandMutationResult.Accepted)
            {
                statusMessage = $"Food adjustment rejected: {result}";
                return;
            }
            plannedFoodAdjustments[unit.Id] = command;
            statusMessage = adjustment > 0
                ? $"Reserved loading {adjustment} food."
                : $"Reserved returning {-adjustment} food to {city.Name}.";
        }

        private void AdjustFoodTransfer(UnitState supplier, UnitState receiver, int change)
        {
            if (IsManeuverRecommandPhase())
            {
                statusMessage = "Food transfer is unavailable during maneuver re-command.";
                return;
            }
            var key = FoodTransferKey(supplier.Id, receiver.Id);
            plannedFoodTransfers.TryGetValue(key, out var existing);
            var current = existing == null ? 0 : existing.PrimaryValue;
            var maximum = Mathf.Min(supplier.CarriedFood,
                UnitRules.FoodCapacity(state, receiver) - receiver.CarriedFood);
            var amount = Mathf.Clamp(current + change, 0, maximum);
            if (existing != null)
            {
                simulator.Planning.Cancel(activePlayerId, existing.CommandId);
                plannedFoodTransfers.Remove(key);
            }
            if (amount <= 0)
            {
                statusMessage = "Food transfer cancelled.";
                return;
            }
            var command = new GameCommand
            {
                CommandId = state.AllocateId(), PlayerId = activePlayerId,
                TurnNumber = state.TurnNumber, Type = GameCommandType.TransferFood,
                SubjectId = supplier.Id, TargetId = receiver.Id, PrimaryValue = amount
            };
            var result = simulator.Planning.Reserve(command);
            if (result == CommandMutationResult.Accepted) plannedFoodTransfers[key] = command;
            statusMessage = result == CommandMutationResult.Accepted
                ? $"Reserved {amount} food: {supplier.Type} → {receiver.Type}."
                : $"Food transfer rejected: {result}";
        }

        private static string FoodTransferKey(GameEntityId supplierId, GameEntityId receiverId)
        {
            return supplierId.Value + ":" + receiverId.Value;
        }

        private CityState FindOwnedCityForUnit(UnitState unit)
        {
            var tile = state.Tiles.Find(item => item.Id == unit.TileId);
            if (tile == null || tile.ControllerId != unit.OwnerId) return null;
            var city = state.Cities.Find(item => item.Id == tile.CityId);
            return city != null && city.OwnerId == unit.OwnerId ? city : null;
        }

        private CityState FindActiveOwnedCityForTile(GameEntityId tileId)
        {
            for (var cityIndex = 0; cityIndex < state.Cities.Count; cityIndex++)
            {
                var city = state.Cities[cityIndex];
                if (city.OwnerId != activePlayerId) continue;
                var view = state.MapTopology.FindView(city.Id);
                var placement = view.Tiles.Find(item => item.TileId == tileId);
                if (placement != null && placement.IsBuildable) return city;
            }
            return null;
        }

        private int CountPlannedDistricts(GameEntityId cityId)
        {
            return plannedDistricts.Values.Count(command => command.SubjectId == cityId);
        }

        private CityState FindActiveHomeCity()
        {
            return state.Cities.Find(item => item.OwnerId == activePlayerId);
        }

        private CityState FindNeutralCityForTile(GameEntityId tileId)
        {
            var tile = state.Tiles.Find(item => item.Id == tileId);
            var city = tile == null ? null : state.Cities.Find(item => item.Id == tile.CityId);
            var owner = city == null ? null : FindPlayer(city.OwnerId);
            return owner != null && owner.Slot == PlayerSlot.Neutral ? city : null;
        }

        private static string NeutralTradeKey(GameEntityId cityId, TileResourceType resource)
        {
            return $"{cityId.Value}:{(int)resource}";
        }

        private void ReserveNeutralTrade(CityState neutralCity, TileResourceType resource)
        {
            if (IsManeuverRecommandPhase())
            {
                statusMessage = "Trade is unavailable during maneuver re-command.";
                return;
            }
            var home = FindActiveHomeCity();
            if (home == null) return;
            var key = NeutralTradeKey(neutralCity.Id, resource);
            if (plannedNeutralTrades.TryGetValue(key, out var previous))
                simulator.Planning.Cancel(activePlayerId, previous.CommandId);
            var command = new GameCommand
            {
                CommandId = state.AllocateId(), PlayerId = activePlayerId,
                TurnNumber = state.TurnNumber, Type = GameCommandType.Trade,
                SubjectId = home.Id, TargetId = neutralCity.Id, PrimaryValue = (int)resource
            };
            var result = simulator.Planning.Reserve(command);
            if (result == CommandMutationResult.Accepted)
            {
                plannedNeutralTrades[key] = command;
                statusMessage = $"Trade with {neutralCity.Name} reserved.";
            }
            else statusMessage = $"Trade reservation failed: {result}.";
        }

        private void CancelNeutralTrade(CityState neutralCity, TileResourceType resource)
        {
            var key = NeutralTradeKey(neutralCity.Id, resource);
            if (!plannedNeutralTrades.TryGetValue(key, out var command)) return;
            simulator.Planning.Cancel(activePlayerId, command.CommandId);
            plannedNeutralTrades.Remove(key);
            statusMessage = $"Trade with {neutralCity.Name} cancelled.";
        }

        private void ReserveLevyBid(CityState militaryCity, int basePrice)
        {
            if (IsManeuverRecommandPhase())
            {
                statusMessage = "Levy bids are unavailable during maneuver re-command.";
                return;
            }
            var home = FindActiveHomeCity();
            if (home == null) return;
            if (plannedLevyBids.TryGetValue(militaryCity.Id, out var previous))
                simulator.Planning.Cancel(activePlayerId, previous.CommandId);
            var affordableExtra = Mathf.Max(0, home.Gold - basePrice);
            levyExtraBid = Mathf.Clamp(levyExtraBid, 0, affordableExtra);
            var command = new GameCommand
            {
                CommandId = state.AllocateId(), PlayerId = activePlayerId,
                TurnNumber = state.TurnNumber, Type = GameCommandType.LevyBid,
                SubjectId = home.Id, TargetId = militaryCity.Id, PrimaryValue = levyExtraBid
            };
            var result = simulator.Planning.Reserve(command);
            if (result == CommandMutationResult.Accepted)
            {
                plannedLevyBids[militaryCity.Id] = command;
                statusMessage = $"Levy bid of {basePrice + levyExtraBid} gold reserved.";
            }
            else statusMessage = $"Levy bid reservation failed: {result}.";
        }

        private void CancelLevyBid(CityState militaryCity)
        {
            if (!plannedLevyBids.TryGetValue(militaryCity.Id, out var command)) return;
            simulator.Planning.Cancel(activePlayerId, command.CommandId);
            plannedLevyBids.Remove(militaryCity.Id);
            statusMessage = $"Levy bid for {militaryCity.Name} cancelled.";
        }

        private void ShowCities(IEnumerable<GameEntityId> cityIds)
        {
            foreach (var view in spawnedViews)
            {
                Destroy(view);
            }
            spawnedViews.Clear();
            visibleTilePositions.Clear();

            var ids = state.Cities.Select(city => city.Id).OrderBy(id => id.Value).ToList();
            var positions = new List<Vector3>();
            for (var index = 0; index < ids.Count; index++)
            {
                var city = state.Cities.Find(item => item.Id == ids[index]);
                var worldCenter = AxialToWorld(
                    WorldMapGenerator.CityCenterCoordinate(city.WorldQ, city.WorldR));
                positions.Add(worldCenter);
                CreateCityView(ids[index], worldCenter);
            }

            CreatePlannedRouteViews();

            var camera = Camera.main;
            if (camera != null && !hasFramedWorld)
            {
                var minX = positions.Min(item => item.x) - 7f;
                var maxX = positions.Max(item => item.x) + 7f;
                var minZ = positions.Min(item => item.z) - 6f;
                var maxZ = positions.Max(item => item.z) + 6f;
                var center = new Vector3((minX + maxX) * 0.5f, 18f, (minZ + maxZ) * 0.5f);
                camera.transform.position = center;
                var verticalSize = (maxZ - minZ) * 0.5f;
                var horizontalSize = (maxX - minX) * 0.5f / Mathf.Max(0.5f, camera.aspect);
                camera.orthographicSize = Mathf.Max(verticalSize, horizontalSize) + 1f;
                hasFramedWorld = true;
            }
        }

        private void CreateCityView(GameEntityId cityId, Vector3 offset)
        {
            var city = state.Cities.Find(item => item.Id == cityId);
            var view = state.MapTopology.FindView(cityId);
            var root = new GameObject($"City {city.Name}");
            root.transform.position = offset;
            spawnedViews.Add(root);

            foreach (var placement in view.Tiles)
            {
                var tile = state.Tiles.Find(item => item.Id == placement.TileId);
                var local = AxialToWorld(new HexCoord(placement.LocalQ, placement.LocalR));
                var tileObject = new GameObject($"Tile {placement.LocalQ},{placement.LocalR} [{placement.TileId}]");
                tileObject.transform.SetParent(root.transform, false);
                tileObject.transform.localPosition = local;
                if (!visibleTilePositions.ContainsKey(placement.TileId))
                    visibleTilePositions.Add(placement.TileId, root.transform.position + local);
                var filter = tileObject.AddComponent<MeshFilter>();
                filter.sharedMesh = CreateHexMesh();
                var renderer = tileObject.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = ResolveMaterial(placement, tile);
                var collider = tileObject.AddComponent<MeshCollider>();
                collider.sharedMesh = filter.sharedMesh;
                tileObject.AddComponent<PrototypeHexTileView>().Initialize(this, placement.TileId);
                CreateTileOutline(tileObject.transform);
                CreateResourceMarker(tileObject.transform, tile);
                CreateGroundFoodMarker(tileObject.transform, tile);
                CreateDefenseFacilityMarker(tileObject.transform, tile);
                CreateUnitsOnTile(tileObject.transform, placement.TileId);
            }
            CreateCityBorder(root.transform, view);
        }

        private void CreateTileOutline(Transform tileTransform)
        {
            var outlineObject = new GameObject("Tile outline");
            outlineObject.transform.SetParent(tileTransform, false);
            outlineObject.transform.localPosition = Vector3.up * 0.06f;
            var line = outlineObject.AddComponent<LineRenderer>();
            line.sharedMaterial = tileOutlineMaterial;
            line.useWorldSpace = false;
            line.loop = true;
            line.positionCount = 6;
            line.startWidth = 0.025f;
            line.endWidth = 0.025f;
            line.numCornerVertices = 1;
            for (var index = 0; index < 6; index++)
            {
                var angle = Mathf.Deg2Rad * ((60f * index) + 30f);
                line.SetPosition(index, new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)));
            }
        }

        private void CreateCityBorder(Transform root, CityMapView view)
        {
            var coordinates = new HashSet<HexCoord>(
                view.Tiles.Select(item => new HexCoord(item.LocalQ, item.LocalR)));
            foreach (var placement in view.Tiles)
            {
                var coordinate = new HexCoord(placement.LocalQ, placement.LocalR);
                var center = AxialToWorld(coordinate) + (Vector3.up * 0.12f);
                for (var direction = 0; direction < 6; direction++)
                {
                    if (coordinates.Contains(coordinate + HexCoord.Direction(direction))) continue;
                    var directionAngle = -60f * direction;
                    var firstAngle = Mathf.Deg2Rad * (directionAngle - 30f);
                    var secondAngle = Mathf.Deg2Rad * (directionAngle + 30f);
                    var first = center + new Vector3(Mathf.Cos(firstAngle), 0f, Mathf.Sin(firstAngle));
                    var second = center + new Vector3(Mathf.Cos(secondAngle), 0f, Mathf.Sin(secondAngle));
                    var edge = new GameObject("Border edge");
                    edge.transform.SetParent(root, false);
                    var line = edge.AddComponent<LineRenderer>();
                    line.sharedMaterial = routeMaterial;
                    line.useWorldSpace = false;
                    line.positionCount = 2;
                    line.startWidth = 0.09f;
                    line.endWidth = 0.09f;
                    line.numCapVertices = 2;
                    line.SetPosition(0, first);
                    line.SetPosition(1, second);
                }
            }
        }

        private void CreateGroundFoodMarker(Transform tileTransform, TileState tile)
        {
            if (tile.GroundFood <= 0) return;
            var marker = new GameObject($"Dropped food ({tile.GroundFood})");
            marker.transform.SetParent(tileTransform, false);
            marker.transform.localPosition = new Vector3(-0.42f, 0.38f, -0.30f);
            var filter = marker.AddComponent<MeshFilter>();
            var mesh = new Mesh { name = "Dropped food triangle" };
            mesh.vertices = new[]
            {
                new Vector3(-0.15f, 0f, -0.11f),
                new Vector3(0.15f, 0f, -0.11f),
                new Vector3(0f, 0f, 0.17f)
            };
            mesh.triangles = new[] { 0, 2, 1, 0, 1, 2 };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            filter.sharedMesh = mesh;
            marker.AddComponent<MeshRenderer>().sharedMaterial = groundFoodMaterial;
        }

        private void CreateResourceMarker(Transform tileTransform, TileState tile)
        {
            var material = ResourceMaterial(tile.ResourceType);
            if (material == null) return;
            var marker = new GameObject($"{tile.ResourceType} resource");
            marker.transform.SetParent(tileTransform, false);
            marker.transform.localPosition = new Vector3(0.42f, 0.20f, -0.30f);
            var filter = marker.AddComponent<MeshFilter>();
            var mesh = new Mesh { name = "Resource square" };
            mesh.vertices = new[]
            {
                new Vector3(-0.18f, 0f, -0.18f), new Vector3(0.18f, 0f, -0.18f),
                new Vector3(0.18f, 0f, 0.18f), new Vector3(-0.18f, 0f, 0.18f)
            };
            mesh.triangles = new[] { 0, 2, 1, 0, 3, 2, 0, 1, 2, 0, 2, 3 };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            filter.sharedMesh = mesh;
            marker.AddComponent<MeshRenderer>().sharedMaterial = material;
        }

        private void CreateDefenseFacilityMarker(Transform tileTransform, TileState tile)
        {
            var facility = state.DefenseFacilities.Find(item => item.TileId == tile.Id);
            plannedDefenseConstructions.TryGetValue(tile.Id, out var planned);
            if (facility == null && planned == null) return;
            var shownType = facility != null && facility.RemainingConstructionTurns > 0
                ? facility.BuildingType
                : facility != null ? facility.Type : (DefenseFacilityType)planned.PrimaryValue;
            var underConstruction = planned != null || (facility != null && facility.RemainingConstructionTurns > 0);
            var marker = new GameObject($"Defense {shownType}");
            marker.transform.SetParent(tileTransform, false);
            marker.transform.localPosition = Vector3.up * 0.24f;
            var line = marker.AddComponent<LineRenderer>();
            line.sharedMaterial = underConstruction
                ? constructionMaterial
                : shownType == DefenseFacilityType.Wall
                    ? wallDefenseMaterial
                    : shownType == DefenseFacilityType.Moat ||
                      (shownType == DefenseFacilityType.ModernDefense && !facility.IsModernDefenseActive)
                        ? moatDefenseMaterial
                        : modernDefenseMaterial;
            line.useWorldSpace = false;
            line.loop = true;
            line.positionCount = 6;
            line.startWidth = shownType == DefenseFacilityType.ModernDefense ? 0.12f : 0.08f;
            line.endWidth = line.startWidth;
            var radius = shownType == DefenseFacilityType.Wall ? 0.58f :
                         shownType == DefenseFacilityType.Moat ? 0.70f : 0.82f;
            for (var index = 0; index < 6; index++)
            {
                var angle = Mathf.Deg2Rad * ((60f * index) + 30f);
                line.SetPosition(index, new Vector3(Mathf.Cos(angle) * radius, 0f,
                    Mathf.Sin(angle) * radius));
            }
        }

        private Material ResourceMaterial(TileResourceType type)
        {
            switch (type)
            {
                case TileResourceType.Food: return agricultureMaterial;
                case TileResourceType.Commerce: return commerceMaterial;
                case TileResourceType.Science: return scienceMaterial;
                case TileResourceType.Culture: return cultureMaterial;
                default: return null;
            }
        }

        private void CreatePlannedRouteViews()
        {
            if (!selectedUnitId.IsValid || !routePlans.TryGetValue(selectedUnitId, out var route)) return;
            var unit = state.Units.Find(item => item.Id == selectedUnitId);
            if (unit == null || unit.OwnerId != activePlayerId || route.Path.Count == 0) return;
            if (!visibleTilePositions.TryGetValue(unit.TileId, out var start)) return;

            var points = new List<Vector3> { start + (Vector3.up * 0.35f) };
            for (var index = 0; index < route.Path.Count; index++)
            {
                if (!visibleTilePositions.TryGetValue(route.Path[index], out var position)) break;
                points.Add(position + (Vector3.up * 0.35f));
            }
            if (points.Count < 2) return;

            var routeObject = new GameObject($"Planned route [{unit.Id}]");
            spawnedViews.Add(routeObject);
            var line = routeObject.AddComponent<LineRenderer>();
            line.sharedMaterial = routeMaterial;
            line.useWorldSpace = true;
            line.positionCount = points.Count;
            line.startWidth = 0.12f;
            line.endWidth = 0.12f;
            line.numCapVertices = 4;
            line.numCornerVertices = 4;
            line.SetPositions(points.ToArray());
        }

        private void CreateUnitsOnTile(Transform tileTransform, GameEntityId tileId)
        {
            var units = state.Units.FindAll(unit => unit.TileId == tileId);
            units.Sort((left, right) => left.Id.CompareTo(right.Id));
            for (var index = 0; index < units.Count; index++)
            {
                var unit = units[index];
                var unitObject = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                unitObject.name = $"{unit.Type} [{unit.Id}]";
                unitObject.transform.SetParent(tileTransform, false);
                unitObject.transform.localPosition = new Vector3((index - ((units.Count - 1) * 0.5f)) * 0.32f, 0.14f, 0f);
                unitObject.transform.localScale = new Vector3(0.26f, 0.12f, 0.26f);
                unitObject.GetComponent<MeshRenderer>().sharedMaterial = ResolveUnitMaterial(unit.OwnerId);
                unitObject.AddComponent<PrototypeUnitView>().Initialize(this, unit);
            }
        }

        private Material ResolveUnitMaterial(GameEntityId ownerId)
        {
            var owner = state.Players.Find(player => player.Id == ownerId);
            if (owner == null || owner.Slot == PlayerSlot.Neutral)
            {
                return neutralUnitMaterial;
            }
            return owner.Slot == PlayerSlot.PlayerOne ? playerOneUnitMaterial : playerTwoUnitMaterial;
        }

        private Material ResolveMaterial(CityTilePlacement placement, TileState tile)
        {
            if (tile.Id == selectedTileId)
            {
                return selectedMaterial;
            }
            if (plannedDistricts.TryGetValue(tile.Id, out _))
            {
                return constructionMaterial;
            }
            var district = state.Districts.Find(item => item.TileId == tile.Id);
            if (district != null)
            {
                if (district.RemainingConstructionTurns > 0) return constructionMaterial;
                switch (district.Type)
                {
                    case DistrictType.Government: return governmentMaterial;
                    case DistrictType.Agriculture: return agricultureMaterial;
                    case DistrictType.Commerce: return commerceMaterial;
                    case DistrictType.Science: return scienceMaterial;
                    case DistrictType.Culture: return cultureMaterial;
                    case DistrictType.Military: return militaryMaterial;
                }
            }
            return placement.IsBuildable ? buildableMaterial : boundaryMaterial;
        }

        private static Vector3 AxialToWorld(HexCoord coord)
        {
            var x = Mathf.Sqrt(3f) * (coord.Q + (coord.R * 0.5f));
            var z = 1.5f * coord.R;
            return new Vector3(x, 0f, z);
        }

        private static Mesh CreateHexMesh()
        {
            var vertices = new Vector3[7];
            vertices[0] = Vector3.zero;
            for (var index = 0; index < 6; index++)
            {
                var angle = Mathf.Deg2Rad * ((60f * index) + 30f);
                vertices[index + 1] = new Vector3(Mathf.Cos(angle) * HexRadius, 0f, Mathf.Sin(angle) * HexRadius);
            }

            var triangles = new int[18];
            for (var index = 0; index < 6; index++)
            {
                triangles[index * 3] = 0;
                triangles[(index * 3) + 1] = ((index + 1) % 6) + 1;
                triangles[(index * 3) + 2] = index + 1;
            }

            var mesh = new Mesh { name = "Prototype Hex" };
            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private void CreateMaterials()
        {
            buildableMaterial = CreateMaterial(new Color(0.23f, 0.45f, 0.28f));
            boundaryMaterial = CreateMaterial(new Color(0.35f, 0.38f, 0.42f));
            governmentMaterial = CreateMaterial(new Color(0.80f, 0.60f, 0.18f));
            selectedMaterial = CreateMaterial(new Color(0.20f, 0.72f, 0.92f));
            playerOneUnitMaterial = CreateMaterial(new Color(0.18f, 0.45f, 0.95f));
            playerTwoUnitMaterial = CreateMaterial(new Color(0.92f, 0.23f, 0.20f));
            neutralUnitMaterial = CreateMaterial(new Color(0.78f, 0.78f, 0.78f));
            agricultureMaterial = CreateMaterial(new Color(0.44f, 0.72f, 0.22f));
            commerceMaterial = CreateMaterial(new Color(0.92f, 0.76f, 0.18f));
            scienceMaterial = CreateMaterial(new Color(0.20f, 0.68f, 0.88f));
            cultureMaterial = CreateMaterial(new Color(0.70f, 0.30f, 0.82f));
            militaryMaterial = CreateMaterial(new Color(0.62f, 0.22f, 0.18f));
            constructionMaterial = CreateMaterial(new Color(0.78f, 0.48f, 0.16f));
            routeMaterial = CreateMaterial(Color.black);
            tileOutlineMaterial = CreateMaterial(new Color(1f, 1f, 1f, 0.9f));
            groundFoodMaterial = CreateMaterial(new Color(1f, 0.82f, 0.05f));
            wallDefenseMaterial = CreateMaterial(new Color(0.75f, 0.75f, 0.75f));
            moatDefenseMaterial = CreateMaterial(new Color(0.15f, 0.70f, 0.95f));
            modernDefenseMaterial = CreateMaterial(new Color(1f, 0.20f, 0.20f));
            routeTurnMarker = CreateCircleTexture(48);
        }

        private static Texture2D CreateCircleTexture(int size)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "Route turn marker", filterMode = FilterMode.Bilinear
            };
            var pixels = new Color[size * size];
            var center = (size - 1) * 0.5f;
            var radiusSquared = center * center;
            for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var dx = x - center;
                var dy = y - center;
                pixels[(y * size) + x] = ((dx * dx) + (dy * dy)) <= radiusSquared
                    ? Color.black
                    : Color.clear;
            }
            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }

        private static Material CreateMaterial(Color color)
        {
            var shader = Resources.Load<Shader>("PrototypeUnlit");
            if (shader == null)
            {
                throw new MissingReferenceException("Resources/PrototypeUnlit.shader is missing from the player build.");
            }
            var material = new Material(shader);
            material.color = color;
            return material;
        }

        private static void EnsureCamera()
        {
            var camera = Camera.main;
            if (camera == null)
            {
                var cameraObject = new GameObject("Main Camera");
                cameraObject.tag = "MainCamera";
                camera = cameraObject.AddComponent<Camera>();
            }

            camera.orthographic = true;
            camera.transform.position = new Vector3(0f, 18f, 0f);
            camera.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            if (camera.GetComponent<PrototypeMapCamera>() == null)
            {
                camera.gameObject.AddComponent<PrototypeMapCamera>();
            }
        }

        private void OnGUI()
        {
            if (state == null)
            {
                return;
            }

            var previousGuiMatrix = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(UiScale, UiScale, 1f));

            DrawRouteTurnMarkers();

            var city = state.Cities[focusedCityIndex];
            var economy = CityEconomyResolver.CalculateBreakdown(state, city);
            GUI.Box(new Rect(16f, 16f, 410f, 505f), string.Empty);
            GUI.Label(new Rect(28f, 25f, 360f, 22f), $"City {city.Name}  World ({city.WorldQ}, {city.WorldR})");
            GUI.Label(new Rect(28f, 47f, 360f, 22f),
                $"Population {city.Population} | Gold {city.Gold} | Stored food {city.StoredFood}");
            DrawYieldRow(28f, 73f, "Food", economy.Food);
            GUI.Label(new Rect(44f, 94f, 340f, 20f),
                $"Consumption: population -{economy.PopulationConsumption}, units -{economy.UnitFoodConsumption} " +
                $"=> Net {Signed(economy.FoodNet)}");
            DrawYieldRow(28f, 118f, "Gold", economy.Gold);
            GUI.Label(new Rect(44f, 139f, 340f, 20f),
                $"Upkeep: units -{economy.UnitUpkeep}, facilities -{economy.FacilityUpkeep}");
            DrawYieldRow(28f, 163f, "Science", economy.Science);
            DrawYieldRow(28f, 187f, "Culture", economy.Culture);
            DrawCultureStatus(city, 28f, 212f);
            GUI.Label(new Rect(28f, 290f, 360f, 20f),
                $"Growth {city.GrowthProgress}/{economy.GrowthRequired} | Famine {city.FamineProgress}/{economy.FamineRequired}");
            var active = FindPlayer(activePlayerId);
            var reCommandCount = state.Units.Count(item =>
                item.OwnerId == activePlayerId && item.ManeuverRecommandTurn == state.TurnNumber);
            GUI.Label(new Rect(28f, 317f, 380f, 20f),
                reCommandCount > 0
                    ? $"RE-COMMAND TURN | {active.Slot} | Units {reCommandCount} | Planned {simulator.Planning.GetOwnCommands(activePlayerId).Count}"
                    : $"NORMAL TURN {state.TurnNumber} | {active.Slot} | Planned {simulator.Planning.GetOwnCommands(activePlayerId).Count}");
            GUI.Label(new Rect(28f, 339f, 380f, 40f), statusMessage);
            GUI.enabled = !state.IsGameOver;
            if (GUI.Button(new Rect(28f, 383f, 170f, 30f), "Cancel selected route")) CancelSelectedMove();
            if (GUI.Button(new Rect(210f, 383f, 198f, 30f), "Confirm player turn")) ConfirmActivePlayer();
            GUI.enabled = true;
            if (GUI.Button(new Rect(28f, 418f, 380f, 30f),
                showResearchPanel ? "Close research" : "Open research"))
                showResearchPanel = !showResearchPanel;
            GUI.Label(new Rect(28f, 453f, 380f, 20f), "Left: select | Right: move | WASD: pan | Wheel: zoom");
            if (turnLog.Count > 0) GUI.Label(new Rect(28f, 477f, 380f, 20f), turnLog[0]);
            DrawSelectedTilePanel();
            if (showResearchPanel) DrawResearchPanel();
            DrawCombatLog();
            GUI.matrix = previousGuiMatrix;
        }

        private void DrawCultureStatus(CityState city, float x, float y)
        {
            var native = CityCultureRules.NativeCitizens(city);
            var one = state.Players.Find(item => item.Slot == PlayerSlot.PlayerOne);
            var two = state.Players.Find(item => item.Slot == PlayerSlot.PlayerTwo);
            var oneCitizens = one == null ? 0 : CityCultureRules.PreferredCitizens(city, one.Id);
            var twoCitizens = two == null ? 0 : CityCultureRules.PreferredCitizens(city, two.Id);
            GUI.Label(new Rect(x, y, 380f, 20f),
                $"Culture citizens: native {native} | P1 {oneCitizens} | P2 {twoCitizens}");

            var progress = new List<string>();
            AddCultureProgress(progress, city, one);
            AddCultureProgress(progress, city, two);
            GUI.Label(new Rect(x, y + 22f, 380f, 20f),
                progress.Count == 0 ? "Culture progress: none" : $"Culture progress: {string.Join(" | ", progress)}");

            var owner = FindPlayer(city.OwnerId);
            if (owner != null && owner.Slot == PlayerSlot.Neutral)
            {
                var subject = city.CultureSubjectToId.IsValid ? FindPlayer(city.CultureSubjectToId)?.Slot.ToString() : "none";
                GUI.Label(new Rect(x, y + 44f, 380f, 20f),
                    $"Neutral resistance {NeutralCultureResolver.Resistance(state, city)} | Subject to: {subject}");
                return;
            }
            var foreign = owner != null && owner.Slot == PlayerSlot.PlayerOne ? twoCitizens : oneCitizens;
            var defeatAt = (city.Population / 2) + 1;
            GUI.Label(new Rect(x, y + 44f, 380f, 20f),
                foreign >= defeatAt
                    ? $"CULTURE DEFEAT: foreign citizens {foreign}/{defeatAt}"
                    : $"Culture safety: foreign {foreign}/{defeatAt} | {defeatAt - foreign} more causes defeat");
        }

        private static void AddCultureProgress(List<string> output, CityState city, PlayerState player)
        {
            if (player == null || player.Id == city.OwnerId || city.CultureInfluences == null) return;
            var influence = city.CultureInfluences.Find(item => item.CultureOwnerId == player.Id);
            if (influence == null || (influence.ConversionProgress <= 0 && influence.ReversionProgress <= 0)) return;
            output.Add(influence.ReversionProgress > 0
                ? $"{player.Slot} return in {10 - influence.ReversionProgress}"
                : $"{player.Slot} convert in {10 - influence.ConversionProgress}");
        }

        private Rect ResearchPanelRect()
        {
            var logicalWidth = Screen.width / UiScale;
            return logicalWidth < 900f
                ? new Rect(16f, 16f, 414f, 600f)
                : new Rect(Mathf.Max(440f, logicalWidth - 844f), 16f, 414f, 600f);
        }

        private void DrawResearchPanel()
        {
            var rect = ResearchPanelRect();
            GUI.Box(rect, string.Empty);
            var player = FindPlayer(activePlayerId);
            if (player == null) return;
            plannedResearch.TryGetValue(activePlayerId, out var planned);
            var shownResearch = planned == null ? player.CurrentResearch : (ResearchType)planned.PrimaryValue;
            var currentProgress = shownResearch == ResearchType.None
                ? 0 : ResearchResolver.Progress(player, shownResearch);
            GUI.Label(new Rect(rect.x + 14f, rect.y + 10f, 290f, 24f),
                $"RESEARCH — {player.Slot}");
            if (GUI.Button(new Rect(rect.x + 310f, rect.y + 8f, 90f, 28f), "Close"))
            {
                showResearchPanel = false;
                return;
            }
            GUI.Label(new Rect(rect.x + 14f, rect.y + 38f, 380f, 22f),
                shownResearch == ResearchType.None
                    ? "Current: none (stores at most one turn of science)"
                    : $"Current: {shownResearch} {currentProgress}/{ResearchRules.Cost(shownResearch)}" +
                      (planned == null ? string.Empty : " (CHANGE RESERVED)"));
            var completedText = player.CompletedResearch == null || player.CompletedResearch.Count == 0
                ? "none"
                : string.Join(", ", player.CompletedResearch.OrderBy(item => (int)item));
            GUI.Label(new Rect(rect.x + 14f, rect.y + 62f, 380f, 42f),
                $"Completed: {completedText}");
            if (planned != null && GUI.Button(new Rect(rect.x + 14f, rect.y + 104f, 376f, 28f),
                "Cancel reserved research change"))
                CancelResearchReservation();

            var viewport = new Rect(rect.x + 8f, rect.y + 140f, rect.width - 16f, rect.height - 150f);
            var content = new Rect(0f, 0f, 378f, 410f);
            researchScroll = GUI.BeginScrollView(viewport, researchScroll, content);
            var types = System.Enum.GetValues(typeof(ResearchType)).Cast<ResearchType>()
                .Where(item => item != ResearchType.None).OrderBy(item => (int)item).ToList();
            for (var index = 0; index < types.Count; index++)
            {
                var type = types[index];
                var column = index % 2;
                var row = index / 2;
                var prerequisite = ResearchRules.Prerequisite(type);
                var completed = player.CompletedResearch.Contains(type);
                var available = prerequisite == ResearchType.None || player.CompletedResearch.Contains(prerequisite);
                var progress = ResearchResolver.Progress(player, type);
                var label = completed
                    ? $"✓ {type}"
                    : available
                        ? $"{type} {progress}/{ResearchRules.Cost(type)}"
                        : $"{type} ← {prerequisite}";
                GUI.enabled = !state.IsGameOver && !IsManeuverRecommandPhase() && available && !completed;
                if (GUI.Button(new Rect(2f + (column * 188f), 4f + (row * 38f), 182f, 32f), label))
                    ReserveResearch(type);
            }
            GUI.enabled = true;
            GUI.EndScrollView();
        }

        private void DrawCombatLog()
        {
            if (combatLog.Count == 0) return;
            var logicalHeight = Screen.height / UiScale;
            var height = 34f + (Mathf.Min(6, combatLog.Count) * 22f);
            var y = Mathf.Max(526f, logicalHeight - height - 16f);
            GUI.Box(new Rect(16f, y, 700f, height), string.Empty);
            GUI.Label(new Rect(28f, y + 8f, 660f, 22f), "COMBAT / MOVEMENT LOG");
            for (var index = 0; index < combatLog.Count && index < 6; index++)
                GUI.Label(new Rect(28f, y + 30f + (index * 22f), 660f, 22f), combatLog[index]);
        }

        private void DrawRouteTurnMarkers()
        {
            if (!selectedUnitId.IsValid || !routePlans.TryGetValue(selectedUnitId, out var route)) return;
            var unit = state.Units.Find(item => item.Id == selectedUnitId);
            var camera = Camera.main;
            if (unit == null || unit.OwnerId != activePlayerId || camera == null) return;

            var markerStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                fontSize = 16
            };
            markerStyle.normal.textColor = Color.white;
            var firstMovement = PlannedMovementForTurn(unit);
            var laterMovement = Mathf.Max(1, UnitRules.Movement(unit.Type));
            var previousTurn = 0;
            for (var step = 1; step <= route.Path.Count; step++)
            {
                var turn = step <= firstMovement
                    ? 1
                    : 1 + Mathf.CeilToInt((step - firstMovement) / (float)laterMovement);
                var isTurnEnd = step == route.Path.Count;
                if (step < route.Path.Count)
                {
                    var nextTurn = (step + 1) <= firstMovement
                        ? 1
                        : 1 + Mathf.CeilToInt(((step + 1) - firstMovement) / (float)laterMovement);
                    isTurnEnd = nextTurn != turn;
                }
                if (!isTurnEnd || turn == previousTurn ||
                    !visibleTilePositions.TryGetValue(route.Path[step - 1], out var world)) continue;

                previousTurn = turn;
                var screen = camera.WorldToScreenPoint(world + (Vector3.up * 0.45f));
                if (screen.z <= 0f) continue;
                var center = new Vector2(screen.x, Screen.height - screen.y) / UiScale;
                var rect = new Rect(center.x - 15f, center.y - 15f, 30f, 30f);
                GUI.DrawTexture(rect, routeTurnMarker, ScaleMode.ScaleToFit, true);
                GUI.Label(rect, turn.ToString(), markerStyle);
            }
        }

        private void DrawSelectedTilePanel()
        {
            if (!selectedTileId.IsValid) return;
            var logicalWidth = Screen.width / UiScale;
            var compact = logicalWidth < 900f;
            var x = compact ? 16f : logicalWidth - 430f;
            var yOffset = compact ? 526f : 16f;
            GUI.Box(new Rect(x, yOffset, 414f, 600f), string.Empty);
            var selectedTile = state.Tiles.Find(item => item.Id == selectedTileId);
            var groundFoodInfo = selectedTile != null && selectedTile.GroundFood > 0
                ? $"FOOD {selectedTile.GroundFood} | owner {selectedTile.GroundFoodOwnerId}" +
                  (selectedTile.GroundFoodReturnTurn > 0
                      ? $" | returns T{selectedTile.GroundFoodReturnTurn}"
                      : " | held on tile")
                : "FOOD 0";
            var resourceInfo = selectedTile == null ? TileResourceType.None : selectedTile.ResourceType;
            GUI.Label(new Rect(x + 14f, yOffset + 10f, 390f, 24f),
                $"Tile {selectedTileId} | Resource {resourceInfo} | {groundFoodInfo}");

            var neutralCity = FindNeutralCityForTile(selectedTileId);
            if (neutralCity != null)
            {
                DrawNeutralCityActions(x, yOffset, neutralCity);
                return;
            }

            if (plannedDistricts.TryGetValue(selectedTileId, out var planned))
            {
                GUI.Label(new Rect(x + 14f, yOffset + 40f, 380f, 22f),
                    $"Planned: {(DistrictType)planned.PrimaryValue} | builder: 1 citizen");
                if (GUI.Button(new Rect(x + 14f, yOffset + 72f, 190f, 30f), "Cancel construction"))
                    CancelSelectedConstruction();
                return;
            }

            var district = state.Districts.Find(item => item.TileId == selectedTileId);
            if (district != null)
            {
                DrawDistrictActions(x, yOffset, district);
                var defenseOffset = district.Type == DistrictType.Military ? 232f : 190f;
                DrawDefenseFacilityActions(x, yOffset + defenseOffset, district);
                DrawUnitsOnSelectedTile(x, yOffset + defenseOffset + 136f);
                return;
            }

            var city = FindActiveOwnedCityForTile(selectedTileId);
            if (city == null)
            {
                GUI.Label(new Rect(x + 14f, yOffset + 40f, 380f, 42f),
                    "This tile cannot be developed by the active player.");
                DrawUnitsOnSelectedTile(x, yOffset + 90f);
                return;
            }

            var free = DistrictConstructionResolver.CountFreeCitizens(state, city) -
                       CountPlannedDistricts(city.Id);
            GUI.Label(new Rect(x + 14f, yOffset + 40f, 380f, 22f),
                $"Undeveloped city tile | Free citizens: {free}");
            GUI.Label(new Rect(x + 14f, yOffset + 64f, 380f, 22f),
                "Choose a district. One free citizen becomes its builder.");
            GUI.enabled = !state.IsGameOver && free > 0;
            DrawDistrictBuildButton(x + 14f, yOffset + 96f, DistrictType.Agriculture);
            DrawDistrictBuildButton(x + 210f, yOffset + 96f, DistrictType.Commerce);
            DrawDistrictBuildButton(x + 14f, yOffset + 134f, DistrictType.Science);
            DrawDistrictBuildButton(x + 210f, yOffset + 134f, DistrictType.Culture);
            DrawDistrictBuildButton(x + 14f, yOffset + 172f, DistrictType.Military);
            GUI.enabled = true;
            DrawUnitsOnSelectedTile(x, yOffset + 220f);
        }

        private void DrawNeutralCityActions(float x, float y, CityState city)
        {
            var stage = NeutralCityRules.DevelopmentStage(state, city);
            var playerOne = FindPlayer(PlayerSlot.PlayerOne);
            var playerTwo = FindPlayer(PlayerSlot.PlayerTwo);
            var subject = city.CultureSubjectToId.IsValid
                ? FindPlayer(city.CultureSubjectToId)?.Slot.ToString() ?? city.CultureSubjectToId.ToString()
                : "none";
            var occupier = city.OccupyingPlayerId.IsValid
                ? FindPlayer(city.OccupyingPlayerId)?.Slot.ToString() ?? city.OccupyingPlayerId.ToString()
                : "none";
            GUI.Label(new Rect(x + 14f, y + 40f, 380f, 22f),
                $"NEUTRAL CITY {city.Name} | {city.NeutralSpecialization} | {stage}");
            GUI.Label(new Rect(x + 14f, y + 64f, 380f, 22f),
                $"Favor: P1 {NeutralCityRules.Favor(city, playerOne.Id)} | " +
                $"P2 {NeutralCityRules.Favor(city, playerTwo.Id)} | Subject: {subject}");
            var required = NeutralOccupationResolver.RequiredStrength(city);
            var garrison = NeutralOccupationResolver.GarrisonStrength(state, city);
            GUI.Label(new Rect(x + 14f, y + 88f, 380f, 22f),
                $"Occupier: {occupier} | Garrison {garrison}/{required} | " +
                $"Independence {city.IndependenceProgress}/2");

            var home = FindActiveHomeCity();
            if (home == null) return;
            GUI.Label(new Rect(x + 14f, y + 116f, 380f, 22f),
                $"{FindPlayer(activePlayerId).Slot} diplomacy orders");
            if (city.NeutralSpecialization == NeutralCitySpecialization.Science ||
                city.NeutralSpecialization == NeutralCitySpecialization.Culture)
            {
                DrawPurchaseTrade(x, y + 142f, city, home);
            }
            else if (city.NeutralSpecialization == NeutralCitySpecialization.Commerce)
            {
                DrawCommerceTrades(x, y + 142f, city, home);
            }
            else if (city.NeutralSpecialization == NeutralCitySpecialization.Military)
            {
                DrawLevyBid(x, y + 142f, city, home);
            }
            else GUI.Label(new Rect(x + 14f, y + 144f, 380f, 24f), "No specialization action available.");

            DrawUnitsOnSelectedTile(x, y + 350f);
        }

        private void DrawPurchaseTrade(float x, float y, CityState city, CityState home)
        {
            var quote = NeutralTradeQuoteResolver.Quote(state, activePlayerId, home.Id, city.Id);
            var receivedResource = city.NeutralSpecialization == NeutralCitySpecialization.Science
                ? TileResourceType.Science : TileResourceType.Culture;
            var route = FormatTradeRoute(quote.Route);
            GUI.Label(new Rect(x + 14f, y, 380f, 22f),
                quote.IsAvailable
                    ? $"Buy {quote.ResourceAmount} {quote.ReceivedResource} for {quote.TotalGoldCost} gold"
                    : $"Trade unavailable: {quote.Failure}");
            GUI.Label(new Rect(x + 14f, y + 23f, 380f, 42f), route);
            var key = NeutralTradeKey(city.Id, receivedResource);
            var reserved = plannedNeutralTrades.ContainsKey(key);
            GUI.enabled = !state.IsGameOver && !IsManeuverRecommandPhase() && (quote.IsAvailable || reserved);
            if (GUI.Button(new Rect(x + 14f, y + 68f, 376f, 30f),
                    reserved ? "Cancel reserved trade" : "Reserve trade"))
            {
                if (reserved) CancelNeutralTrade(city, receivedResource);
                else ReserveNeutralTrade(city, receivedResource);
            }
            GUI.enabled = true;
        }

        private void DrawCommerceTrades(float x, float y, CityState city, CityState home)
        {
            var resources = new[] { TileResourceType.Food, TileResourceType.Science, TileResourceType.Culture };
            for (var index = 0; index < resources.Length; index++)
            {
                var resource = resources[index];
                var quote = CommerceTradeQuoteResolver.Quote(state, activePlayerId, home.Id, city.Id, resource);
                var rowY = y + (index * 54f);
                var key = NeutralTradeKey(city.Id, resource);
                var reserved = plannedNeutralTrades.ContainsKey(key);
                GUI.Label(new Rect(x + 14f, rowY, 250f, 42f), quote.IsAvailable
                    ? $"Sell {quote.RequiredResourceAmount} {resource} → {quote.NetGoldPayment} gold\n" +
                      $"Have {quote.AvailableResourceAmount} | {FormatTradeRoute(quote.Route)}"
                    : $"Sell {resource}: {quote.Failure}\n{FormatTradeRoute(quote.Route)}");
                GUI.enabled = !state.IsGameOver && !IsManeuverRecommandPhase() && (quote.IsAvailable || reserved);
                if (GUI.Button(new Rect(x + 270f, rowY + 5f, 120f, 32f), reserved ? "Cancel" : "Reserve"))
                {
                    if (reserved) CancelNeutralTrade(city, resource);
                    else ReserveNeutralTrade(city, resource);
                }
                GUI.enabled = true;
            }
        }

        private void DrawLevyBid(float x, float y, CityState city, CityState home)
        {
            var quote = NeutralLevyResolver.Quote(state, activePlayerId, home.Id, city.Id);
            GUI.Label(new Rect(x + 14f, y, 380f, 22f), quote.IsAvailable
                ? $"Levy {quote.UnitIds.Count} units | value {quote.FullUnitValue} | base {quote.BasePrice} gold"
                : $"Levy unavailable: {quote.Failure}");
            GUI.Label(new Rect(x + 14f, y + 23f, 380f, 22f), FormatTradeRoute(quote.Route));
            var affordableExtra = quote.IsAvailable ? Mathf.Max(0, home.Gold - quote.BasePrice) : 0;
            levyExtraBid = Mathf.Clamp(levyExtraBid, 0, affordableExtra);
            GUI.Label(new Rect(x + 14f, y + 52f, 155f, 28f),
                $"Extra bid: {levyExtraBid} | total {quote.BasePrice + levyExtraBid}");
            GUI.enabled = quote.IsAvailable && !plannedLevyBids.ContainsKey(city.Id);
            if (GUI.Button(new Rect(x + 174f, y + 49f, 46f, 30f), "-")) levyExtraBid = Mathf.Max(0, levyExtraBid - 1);
            if (GUI.Button(new Rect(x + 224f, y + 49f, 46f, 30f), "+")) levyExtraBid = Mathf.Min(affordableExtra, levyExtraBid + 1);
            if (GUI.Button(new Rect(x + 274f, y + 49f, 54f, 30f), "+5")) levyExtraBid = Mathf.Min(affordableExtra, levyExtraBid + 5);
            if (GUI.Button(new Rect(x + 332f, y + 49f, 58f, 30f), "Max")) levyExtraBid = affordableExtra;
            var reserved = plannedLevyBids.TryGetValue(city.Id, out var bid);
            GUI.enabled = !state.IsGameOver && !IsManeuverRecommandPhase() && (quote.IsAvailable || reserved);
            if (GUI.Button(new Rect(x + 14f, y + 86f, 376f, 30f), reserved
                    ? $"Cancel bid ({quote.BasePrice + bid.PrimaryValue} gold)"
                    : "Reserve levy bid"))
            {
                if (reserved) CancelLevyBid(city);
                else ReserveLevyBid(city, quote.BasePrice);
            }
            GUI.enabled = true;
            var levy = state.Levies.Find(item => item.MilitaryCityId == city.Id);
            if (levy != null)
                GUI.Label(new Rect(x + 14f, y + 122f, 380f, 42f),
                    $"ACTIVE LEVY: {FindPlayer(levy.PlayerId)?.Slot} | {levy.Units.Count} units | " +
                    $"returns before T{levy.EndTurnExclusive}");
        }

        private string FormatTradeRoute(TradeRouteResult route)
        {
            if (route == null) return "Route: not evaluated";
            if (!route.IsReachable)
                return route.BlockedCityIds.Count == 0
                    ? "Route: unreachable"
                    : $"Route: blocked by {string.Join(", ", route.BlockedCityIds)}";
            var names = route.CityPath.Select(id => state.Cities.Find(city => city.Id == id)?.Name ?? id.ToString());
            return $"Route {string.Join(" → ", names)} | distance {route.Distance} (+{route.AdditionalDistance})";
        }

        private void DrawDistrictActions(float x, float y, DistrictState district)
        {
            var city = state.Cities.Find(item => item.Id == district.CityId);
            GUI.Label(new Rect(x + 14f, y + 40f, 380f, 22f),
                $"{district.Type} | City {city?.Name} | Citizens {district.AssignedCitizens}");
            if (district.RemainingConstructionTurns > 0)
            {
                GUI.Label(new Rect(x + 14f, y + 66f, 380f, 22f),
                    $"Under construction: {district.RemainingConstructionTurns} turns remaining");
                return;
            }
            var ownedAndControlled = city != null && city.OwnerId == activePlayerId &&
                                     district.ControllerId == city.OwnerId;
            if (city == null || district.ControllerId != city.OwnerId)
            {
                GUI.Label(new Rect(x + 14f, y + 66f, 380f, 42f),
                    "Status: occupied — the original city cannot use this district.");
                return;
            }
            var legacyRecapturedPillage = !district.IsOperational && district.AssignedCitizens > 0 &&
                                           !district.IsMaintenanceSuspended;
            if (district.IsPillaged || legacyRecapturedPillage)
            {
                if (district.RemainingRepairTurns > 0)
                {
                    GUI.Label(new Rect(x + 14f, y + 66f, 380f, 42f),
                        $"Repairing: {district.RemainingRepairTurns} turns remaining");
                    return;
                }
                GUI.Label(new Rect(x + 14f, y + 66f, 380f, 22f), "Status: pillaged");
                GUI.enabled = ownedAndControlled && !state.IsGameOver;
                if (plannedRepairs.ContainsKey(district.Id))
                {
                    GUI.Label(new Rect(x + 14f, y + 94f, 180f, 30f), "Repair reserved");
                    if (GUI.Button(new Rect(x + 210f, y + 94f, 180f, 30f), "Cancel repair"))
                        CancelDistrictRepair(district);
                }
                else if (GUI.Button(new Rect(x + 14f, y + 94f, 376f, 30f),
                    $"Repair ({DistrictConstructionResolver.RepairTurns(district.Type)} turns)"))
                {
                    ReserveDistrictRepair(district);
                }
                GUI.enabled = true;
                return;
            }
            GUI.Label(new Rect(x + 14f, y + 66f, 380f, 22f),
                district.IsOperational ? "Status: operational" : "Status: inactive");
            if (district.Type != DistrictType.Government && district.IsOperational)
            {
                var resource = state.Tiles.Find(item => item.Id == district.TileId)?.ResourceType ??
                               TileResourceType.None;
                GUI.Label(new Rect(x + 14f, y + 88f, 380f, 22f),
                    $"Resource {resource}: +{CityEconomyResolver.ResourceBonusForDistrict(state, district)} | " +
                    $"same-district adjacency: +{CityEconomyResolver.AdjacencyBonusForDistrict(state, district)}");
            }
            if (district.Type == DistrictType.Government)
            {
                var free = city == null ? 0 : DistrictConstructionResolver.CountFreeCitizens(state, city) -
                                          CountPlannedDistricts(city.Id);
                GUI.Label(new Rect(x + 14f, y + 94f, 380f, 22f),
                    $"Government action: manage city | Free citizens {free}");
                GUI.Label(new Rect(x + 14f, y + 120f, 380f, 42f),
                    "Select an undeveloped green tile to order a new district.");
                return;
            }
            if (district.Type == DistrictType.NuclearFacility)
            {
                var project = state.NuclearProjects.Find(item => item.DistrictId == district.Id);
                if (project != null)
                {
                    GUI.Label(new Rect(x + 14f, y + 112f, 380f, 42f),
                        project.IsCompleted ? "Nuclear weapon project: COMPLETED" :
                        $"Nuclear weapon project: {project.RemainingTurns} turns remaining");
                    return;
                }
                if (plannedNuclearProjects.ContainsKey(district.Id))
                {
                    GUI.Label(new Rect(x + 14f, y + 112f, 180f, 30f), "Nuclear project reserved");
                    if (GUI.Button(new Rect(x + 210f, y + 112f, 180f, 30f), "Cancel project"))
                        CancelNuclearProject(district);
                    return;
                }
                var nuclearPlayer = city == null ? null : FindPlayer(city.OwnerId);
                GUI.enabled = !state.IsGameOver && ownedAndControlled && district.IsOperational &&
                              nuclearPlayer != null &&
                              nuclearPlayer.CompletedResearch.Contains(ResearchType.NuclearFission) &&
                              !nuclearPlayer.HasCompletedNuclearProject &&
                              city.Gold >= NuclearProjectResolver.StartGold;
                if (GUI.Button(new Rect(x + 14f, y + 112f, 376f, 30f),
                    "Start nuclear weapon project — 10 gold / 3 turns"))
                    ReserveNuclearProject(district);
                GUI.enabled = true;
                return;
            }
            if (district.Type == DistrictType.Agriculture)
            {
                var agriculturePlayer = city == null ? null : FindPlayer(city.OwnerId);
                var irrigation = agriculturePlayer != null &&
                                 agriculturePlayer.CompletedResearch.Contains(ResearchType.Irrigation);
                var mechanized = agriculturePlayer != null &&
                                 agriculturePlayer.CompletedResearch.Contains(ResearchType.MechanizedAgriculture);
                if (mechanized)
                {
                    GUI.Label(new Rect(x + 14f, y + 112f, 376f, 42f),
                        "Mechanized agriculture: 150% output with one citizen.");
                    return;
                }
                if (!irrigation)
                {
                    GUI.Label(new Rect(x + 14f, y + 112f, 376f, 42f),
                        "Research Irrigation to assign a second citizen for 150% output.");
                    return;
                }
                if (plannedCitizenAssignments.TryGetValue(district.Id, out var citizenPlan))
                {
                    GUI.Label(new Rect(x + 14f, y + 112f, 190f, 30f),
                        $"Staffing reserved: {citizenPlan.PrimaryValue}");
                    if (GUI.Button(new Rect(x + 210f, y + 112f, 180f, 30f), "Cancel staffing"))
                        CancelAgricultureCitizens(district);
                    return;
                }
                var freeCitizens = city == null ? 0 : DistrictConstructionResolver.CountFreeCitizens(state, city) -
                                                   CountPlannedDistricts(city.Id) -
                                                   CountPlannedAdditionalAgricultureCitizens(city.Id);
                GUI.enabled = ownedAndControlled && !state.IsGameOver &&
                              (district.AssignedCitizens > 1 || freeCitizens > 0);
                var desired = district.AssignedCitizens > 1 ? 1 : 2;
                if (GUI.Button(new Rect(x + 14f, y + 112f, 376f, 30f),
                    desired == 2 ? "Assign second citizen — output 150%" : "Return second citizen"))
                    ReserveAgricultureCitizens(district, desired);
                GUI.enabled = true;
                return;
            }
            if (district.Type != DistrictType.Military)
            {
                GUI.Label(new Rect(x + 14f, y + 112f, 380f, 42f),
                    "This district produces its yield automatically each turn.");
                return;
            }

            var training = state.UnitTrainings.Find(item => item.DistrictId == district.Id);
            if (training != null)
            {
                GUI.Label(new Rect(x + 14f, y + 112f, 380f, 42f),
                    training.IsAwaitingDeployment
                        ? $"{training.Type} - 0 turns remaining (NO SPACE)"
                        : $"Training {training.Type}: {training.RemainingTurns} turns remaining");
                return;
            }
            if (plannedTrainings.TryGetValue(district.Id, out var plannedTraining))
            {
                GUI.Label(new Rect(x + 14f, y + 112f, 380f, 42f),
                    $"Training reserved: {(UnitType)plannedTraining.PrimaryValue}");
                return;
            }
            GUI.Label(new Rect(x + 14f, y + 112f, 380f, 22f), "Military district action: train a unit");
            var player = city == null ? null : FindPlayer(city.OwnerId);
            var unlocked = player == null || player.UnlockedUnitTypes == null
                ? new List<UnitType>()
                : player.UnlockedUnitTypes.OrderBy(item => (int)item).ToList();
            for (var index = 0; index < unlocked.Count && index < 6; index++)
            {
                var type = unlocked[index];
                var column = index % 2;
                var row = index / 2;
                GUI.enabled = !state.IsGameOver && ownedAndControlled && district.IsOperational &&
                              city.Gold >= UnitRules.TrainingGold(type);
                if (GUI.Button(new Rect(x + 14f + (column * 196f), y + 140f + (row * 28f), 180f, 26f),
                    $"{type} | {UnitRules.TrainingGold(type)}g/{UnitRules.TrainingTurns(type)}t"))
                    ReserveTraining(district, type);
            }
            GUI.enabled = true;
        }

        private int CountPlannedAdditionalAgricultureCitizens(GameEntityId cityId)
        {
            var count = 0;
            foreach (var pair in plannedCitizenAssignments)
            {
                var district = state.Districts.Find(item => item.Id == pair.Key);
                if (district != null && district.CityId == cityId)
                    count += Mathf.Max(0, pair.Value.PrimaryValue - district.AssignedCitizens);
            }
            return count;
        }

        private void DrawDefenseFacilityActions(float x, float y, DistrictState district)
        {
            var city = state.Cities.Find(item => item.Id == district.CityId);
            var tile = state.Tiles.Find(item => item.Id == district.TileId);
            var facility = state.DefenseFacilities.Find(item => item.TileId == district.TileId);
            GUI.Label(new Rect(x + 14f, y, 376f, 22f), "Tile defense facility:");
            if (district.RemainingConstructionTurns > 0)
            {
                GUI.Label(new Rect(x + 14f, y + 24f, 376f, 22f),
                    "Available after district construction completes.");
                return;
            }
            if (facility != null && facility.RemainingConstructionTurns > 0)
            {
                GUI.Label(new Rect(x + 14f, y + 24f, 376f, 42f),
                    $"Building {facility.BuildingType}: {facility.RemainingConstructionTurns} turns | " +
                    $"current bonus {DefenseFacilityResolver.EffectiveBonus(facility)}%");
                return;
            }
            if (plannedDefenseConstructions.TryGetValue(district.TileId, out var planned))
            {
                var plannedType = (DefenseFacilityType)planned.PrimaryValue;
                GUI.Label(new Rect(x + 14f, y + 24f, 190f, 30f),
                    $"{plannedType} reserved");
                if (GUI.Button(new Rect(x + 210f, y + 24f, 180f, 30f), "Cancel defense build"))
                    CancelDefenseConstruction(district.TileId);
                return;
            }

            var type = facility == null ? DefenseFacilityType.None : facility.Type;
            var bonus = facility == null ? 0 : DefenseFacilityResolver.EffectiveBonus(facility);
            var status = type == DefenseFacilityType.ModernDefense
                ? facility.IsModernDefenseActive
                    ? "active"
                    : facility.RemainingReactivationTurns > 0
                        ? $"reactivating ({facility.RemainingReactivationTurns} turns)"
                        : "inactive"
                : "active";
            GUI.Label(new Rect(x + 14f, y + 24f, 376f, 22f),
                $"{type} | defense +{bonus}%" + (type == DefenseFacilityType.None ? string.Empty : $" | {status}"));

            var ownedAndControlled = city != null && tile != null && city.OwnerId == activePlayerId &&
                                     tile.ControllerId == city.OwnerId && !state.IsGameOver;
            if (type != DefenseFacilityType.ModernDefense)
            {
                var next = (DefenseFacilityType)((int)type + 1);
                var player = city == null ? null : FindPlayer(city.OwnerId);
                var unlocked = player != null && (!player.ResearchUnlocksEnabled ||
                    player.UnlockedDefenseTypes.Contains(next));
                GUI.enabled = ownedAndControlled && unlocked &&
                              city.Gold >= DefenseFacilityResolver.GoldCost(next);
                if (GUI.Button(new Rect(x + 14f, y + 52f, 376f, 30f),
                    (unlocked ? $"Build {next}" : $"{next} (research required)") +
                    $" — {DefenseFacilityResolver.GoldCost(next)} gold / " +
                    $"{DefenseFacilityResolver.ConstructionTurns(next)} turns"))
                    ReserveDefenseConstruction(district, next);
                GUI.enabled = true;
                return;
            }

            if (plannedDefenseActions.ContainsKey(facility.Id))
            {
                GUI.Label(new Rect(x + 14f, y + 52f, 190f, 30f), "Control change reserved");
                if (GUI.Button(new Rect(x + 210f, y + 52f, 180f, 30f), "Cancel change"))
                    CancelModernDefenseAction(facility);
                return;
            }
            GUI.enabled = ownedAndControlled;
            if (facility.IsModernDefenseActive)
            {
                if (GUI.Button(new Rect(x + 14f, y + 52f, 376f, 30f),
                    "Deactivate (falls back to Moat +50%)"))
                    ReserveModernDefenseAction(facility, false);
            }
            else if (facility.RemainingReactivationTurns <= 0)
            {
                if (GUI.Button(new Rect(x + 14f, y + 52f, 376f, 30f),
                    "Reactivate — 2 turns / 2 gold each turn"))
                    ReserveModernDefenseAction(facility, true);
            }
            GUI.enabled = true;
        }

        private void DrawUnitsOnSelectedTile(float x, float y)
        {
            var units = state.Units.FindAll(item => item.TileId == selectedTileId);
            units.Sort((left, right) => left.Id.CompareTo(right.Id));
            if (units.Count == 0) return;
            GUI.Label(new Rect(x + 14f, y, 380f, 22f), "Units on tile (select one to issue movement orders):");
            for (var index = 0; index < units.Count && index < 4; index++)
            {
                var unit = units[index];
                GUI.enabled = unit.OwnerId == activePlayerId && !state.IsGameOver;
                if (GUI.Button(new Rect(x + 14f, y + 28f + (index * 34f), 376f, 28f),
                    $"{unit.Type} | HP {unit.HitPoints} | Food {unit.CarriedFood} | " +
                    (unit.ManeuverRecommandTurn == state.TurnNumber
                        ? $"RECOMMAND Move {unit.RemainingMovement}"
                        : $"Move {unit.RemainingMovement}")))
                {
                    SelectUnit(unit.Id, unit.TileId);
                }
                GUI.enabled = true;
            }

            var selected = units.Find(item => item.Id == selectedUnitId && item.OwnerId == activePlayerId);
            if (selected == null) return;
            var foodY = y + 32f + (Mathf.Min(4, units.Count) * 34f);
            var city = FindOwnedCityForUnit(selected);
            plannedFoodAdjustments.TryGetValue(selected.Id, out var adjustmentCommand);
            var adjustment = adjustmentCommand == null ? 0 : adjustmentCommand.PrimaryValue;
            var projected = selected.CarriedFood + adjustment;
            var capacity = UnitRules.FoodCapacity(state, selected);
            GUI.Label(new Rect(x + 14f, foodY, 376f, 22f),
                city == null
                    ? $"Food {selected.CarriedFood}/{capacity} | adjust in controlled home territory"
                    : $"Food {selected.CarriedFood}/{capacity} → {projected}/{capacity} | City stock {city.StoredFood}");
            GUI.enabled = city != null && !state.IsGameOver;
            if (GUI.Button(new Rect(x + 14f, foodY + 26f, 86f, 28f), "Return 1"))
                AdjustSelectedUnitFood(selected, -1);
            if (GUI.Button(new Rect(x + 106f, foodY + 26f, 86f, 28f), "Return all"))
                AdjustSelectedUnitFood(selected, -capacity);
            if (GUI.Button(new Rect(x + 210f, foodY + 26f, 86f, 28f), "Load 1"))
                AdjustSelectedUnitFood(selected, 1);
            if (GUI.Button(new Rect(x + 302f, foodY + 26f, 88f, 28f), "Load max"))
                AdjustSelectedUnitFood(selected, capacity);
            GUI.enabled = true;
            DrawFoodTransferActions(x, foodY + 62f, selected, units);
        }

        private void DrawFoodTransferActions(float x, float y, UnitState selected, List<UnitState> units)
        {
            var partners = units.FindAll(item =>
                item.Id != selected.Id && item.OwnerId == activePlayerId);
            if (partners.Count == 0) return;
            GUI.Label(new Rect(x + 14f, y, 376f, 22f), "Same-tile food exchange:");
            for (var i = 0; i < partners.Count && i < 2; i++)
            {
                var partner = partners[i];
                var giveKey = FoodTransferKey(selected.Id, partner.Id);
                var takeKey = FoodTransferKey(partner.Id, selected.Id);
                plannedFoodTransfers.TryGetValue(giveKey, out var give);
                plannedFoodTransfers.TryGetValue(takeKey, out var take);
                var rowY = y + 24f + (i * 58f);
                GUI.Label(new Rect(x + 14f, rowY, 376f, 22f),
                    $"{partner.Type} {partner.Id} | Food {partner.CarriedFood}/{UnitRules.FoodCapacity(state, partner)}" +
                    (give != null ? $" | give {give.PrimaryValue}" : string.Empty) +
                    (take != null ? $" | take {take.PrimaryValue}" : string.Empty));
                GUI.enabled = !state.IsGameOver && selected.CarriedFood > 0 &&
                              partner.CarriedFood < UnitRules.FoodCapacity(state, partner);
                if (GUI.Button(new Rect(x + 14f, rowY + 24f, 82f, 28f), "Give 1"))
                    AdjustFoodTransfer(selected, partner, 1);
                if (GUI.Button(new Rect(x + 102f, rowY + 24f, 82f, 28f), "Give max"))
                    AdjustFoodTransfer(selected, partner, UnitRules.FoodCapacity(state, partner));
                GUI.enabled = !state.IsGameOver && partner.CarriedFood > 0 &&
                              selected.CarriedFood < UnitRules.FoodCapacity(state, selected);
                if (GUI.Button(new Rect(x + 210f, rowY + 24f, 82f, 28f), "Take 1"))
                    AdjustFoodTransfer(partner, selected, 1);
                if (GUI.Button(new Rect(x + 298f, rowY + 24f, 92f, 28f), "Take max"))
                    AdjustFoodTransfer(partner, selected, UnitRules.FoodCapacity(state, selected));
                GUI.enabled = true;
            }
        }

        private void DrawDistrictBuildButton(float x, float y, DistrictType type)
        {
            var player = FindPlayer(activePlayerId);
            var unlocked = player != null && (!player.ResearchUnlocksEnabled ||
                player.UnlockedDistrictTypes.Contains(type));
            var previous = GUI.enabled;
            GUI.enabled = previous && unlocked;
            if (GUI.Button(new Rect(x, y, 180f, 30f),
                unlocked ? $"Build {type}" : $"{type} (locked)"))
                ReserveDistrictConstruction(type);
            GUI.enabled = previous;
        }

        private static void DrawYieldRow(float x, float y, string label, YieldBreakdown value)
        {
            GUI.Label(new Rect(x, y, 360f, 20f),
                $"{label} {value.Total} = government {value.Government} + district {value.DistrictBase} " +
                $"+ resource {value.ResourceBonus} + adjacency {value.AdjacencyBonus} " +
                $"+ research {value.ResearchBonus} + staffing {value.StaffingBonus} " +
                $"+ multiplier {value.MultiplierBonus}");
        }

        private static string Signed(int value)
        {
            return value > 0 ? $"+{value}" : value.ToString();
        }
    }
}
