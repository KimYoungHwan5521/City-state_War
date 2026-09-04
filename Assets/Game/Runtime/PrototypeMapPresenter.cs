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
        private readonly Dictionary<GameEntityId, GameCommand> plannedPromotions =
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
        private readonly Dictionary<GameEntityId, GameCommand> plannedGroundFoodPickups =
            new Dictionary<GameEntityId, GameCommand>();
        private readonly Dictionary<string, GameCommand> plannedNeutralTrades =
            new Dictionary<string, GameCommand>();
        private readonly HashSet<string> repeatingNeutralTrades = new HashSet<string>();
        private readonly Dictionary<string, GameCommand> plannedLevyBids =
            new Dictionary<string, GameCommand>();
        private readonly Dictionary<GameEntityId, Vector3> visibleTilePositions =
            new Dictionary<GameEntityId, Vector3>();
        private GameState state;
        private SimultaneousTurnSimulator simulator;
        private int focusedCityIndex;
        private GameEntityId selectedTileId;
        private GameEntityId selectedUnitId;
        private readonly HashSet<GameEntityId> selectedUnitGroup = new HashSet<GameEntityId>();
        private GameEntityId activePlayerId;
        private bool hasFramedWorld;
        private string statusMessage = "병력을 선택한 뒤 목적지를 우클릭하세요.";
        private readonly List<string> turnLog = new List<string>();
        private readonly List<string> combatLog = new List<string>();
        private Material buildableMaterial;
        private Material boundaryMaterial;
        private Material governmentMaterial;
        private Material selectedMaterial;
        private Material playerOneUnitMaterial;
        private Material playerTwoUnitMaterial;
        private Material neutralUnitMaterial;
        private readonly Dictionary<GameEntityId, Material> neutralCityUnitMaterials =
            new Dictionary<GameEntityId, Material>();
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
        private bool showCheatPanel;
        private bool showNeutralTradePanel;
        private Vector2 researchScroll;
        private Vector2 neutralTradeScroll;
        private Vector2 combatLogScroll;
        private int levyExtraBid;

        private void Start()
        {
            CreateMaterials();
            EnsureCamera();
            RestartMatch();
        }

        private void RestartMatch()
        {
            plannedMoves.Clear();
            routePlans.Clear();
            plannedDistricts.Clear();
            plannedTrainings.Clear();
            plannedPromotions.Clear();
            plannedFoodAdjustments.Clear();
            plannedRepairs.Clear();
            plannedDefenseConstructions.Clear();
            plannedDefenseActions.Clear();
            plannedNuclearProjects.Clear();
            plannedResearch.Clear();
            plannedCitizenAssignments.Clear();
            plannedFoodTransfers.Clear();
            plannedGroundFoodPickups.Clear();
            plannedNeutralTrades.Clear();
            repeatingNeutralTrades.Clear();
            plannedLevyBids.Clear();
            selectedUnitGroup.Clear();
            selectedTileId = default;
            selectedUnitId = default;
            focusedCityIndex = 0;
            levyExtraBid = 0;
            researchScroll = Vector2.zero;
            neutralTradeScroll = Vector2.zero;
            combatLogScroll = Vector2.zero;
            showResearchPanel = false;
            showCheatPanel = false;
            showNeutralTradePanel = false;
            turnLog.Clear();
            combatLog.Clear();
            hasFramedWorld = false;

            state = PrototypeMatchFactory.Create(20260831);
            simulator = new SimultaneousTurnSimulator(state);
            activePlayerId = FindPlayer(PlayerSlot.PlayerOne).Id;
            statusMessage = "1턴부터 새 경기를 시작했습니다.";
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
                statusMessage = "포인터 아래에서 타일을 찾지 못했습니다.";
                return;
            }

            var tileView = hit.collider.GetComponentInParent<PrototypeHexTileView>();
            if (tileView == null)
            {
                statusMessage = "선택한 오브젝트는 도시 타일이 아닙니다.";
                return;
            }
            if (issueMove)
            {
                if (!selectedUnitId.IsValid)
                {
                    statusMessage = "먼저 자신의 병력을 선택한 뒤 목적지를 우클릭하세요.";
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
            if ((showResearchPanel || showCheatPanel || showNeutralTradePanel) && ResearchPanelRect().Contains(guiPosition)) return true;
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
            selectedUnitGroup.Clear();
            selectedTileId = tileId;
            var selectedDistrict = state.Districts.Find(item => item.TileId == tileId);
            statusMessage = selectedDistrict != null
                ? $"{DistrictName(selectedDistrict.Type)} 선택. 타일 패널에서 지구 행동을 선택하세요."
                : "미개발 타일 선택. 타일 패널에서 건설할 지구를 선택하세요.";
            var tile = state.Tiles.Find(item => item.Id == tileId);
            var fallbackCityId = state.Cities[focusedCityIndex].Id;
            ShowCities(MapVisibilityResolver.ResolveCitiesForTile(state, tile.Id, fallbackCityId));
        }

        public void SelectUnit(GameEntityId unitId, GameEntityId tileId)
        {
            var unit = state.Units.Find(item => item.Id == unitId);
            if (unit == null || unit.OwnerId != activePlayerId)
            {
                statusMessage = "현재 명령 중인 플레이어의 병력만 선택할 수 있습니다.";
                SelectTile(tileId);
                return;
            }
            if (IsManeuverRecommandPhase() && unit.ManeuverRecommandTurn != state.TurnNumber)
            {
                statusMessage = "기동 재명령 대상 병력만 명령할 수 있습니다.";
                return;
            }

            selectedUnitId = unitId;
            selectedUnitGroup.Clear();
            selectedUnitGroup.Add(unitId);
            selectedTileId = tileId;
            statusMessage = $"{UnitName(unit.Type)} {unit.Id} 선택. 목적지를 우클릭해 경로를 예약하세요.";
            var fallbackCityId = state.Cities[focusedCityIndex].Id;
            ShowCities(MapVisibilityResolver.ResolveCitiesForTile(state, tileId, fallbackCityId));
        }

        private bool TryAppendMove(GameEntityId tileId)
        {
            if (!selectedUnitId.IsValid) return false;
            if (selectedUnitGroup.Count > 1)
            {
                var ordered = selectedUnitGroup.OrderBy(item => item.Value).ToList();
                var planned = 0;
                for (var index = 0; index < ordered.Count; index++)
                {
                    var member = state.Units.Find(item => item.Id == ordered[index]);
                    if (member != null && member.OwnerId == activePlayerId && PlanMoveForUnit(member, tileId)) planned++;
                }
                statusMessage = $"같은 타일 병력 {planned}/{ordered.Count}부대의 공통 목적지 이동을 예약했습니다.";
                ShowCities(MapVisibilityResolver.ResolveCitiesForTile(state, selectedTileId,
                    state.Cities[focusedCityIndex].Id));
                return true;
            }
            var unit = state.Units.Find(item => item.Id == selectedUnitId);
            if (unit == null || unit.OwnerId != activePlayerId) return true;
            return PlanMoveForUnit(unit, tileId);
        }

        private bool PlanMoveForUnit(UnitState unit, GameEntityId tileId)
        {
            if (NeutralLevyResolver.IsProtectedCityTile(state, unit.OwnerId, tileId, unit.Id) &&
                HasEnemyUnit(unit, tileId))
            {
                statusMessage = "징병 기간에는 원 소속 군사도시의 수비군을 공격할 수 없습니다.";
                return false;
            }
            if (tileId == unit.TileId)
            {
                statusMessage = "병력이 이미 해당 타일에 있습니다.";
                return true;
            }

            var path = FindShortestTilePath(unit, tileId);
            if (path.Count == 0)
            {
                statusMessage = "목적지까지 이동 가능한 경로가 없습니다.";
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
                ? $"경로 예약: {route.Path.Count}칸, 약 {turns}턴 후 도착."
                : "장기 경로를 저장했습니다. 이 병력은 이번 턴에 이동할 수 없습니다.";
            if (reserved && HasEnemyUnit(unit, tileId))
                statusMessage += " " + CombatPreview(unit, tileId);
            var fallbackCityId = state.Cities[focusedCityIndex].Id;
            ShowCities(MapVisibilityResolver.ResolveCitiesForTile(state, unit.TileId, fallbackCityId));
            return true;
        }

        private string CombatPreview(UnitState attacker, GameEntityId targetTileId)
        {
            var defenders = state.Units.Where(item => item.TileId == targetTileId &&
                item.OwnerId != attacker.OwnerId).OrderBy(item => item.Id).ToList();
            var preview = GameStateCopy.Clone(state);
            var result = CombatResolver.Resolve(preview, new CombatEngagementRequest
            {
                AttackingPlayerId = attacker.OwnerId,
                AttackingUnitId = attacker.Id,
                TargetTileId = targetTileId
            });
            var survivor = preview.Units.Find(item => item.Id == attacker.Id);
            var defenderForecast = defenders.Count == 0 ? "수비군 없음" : string.Join(", ",
                defenders.Select(defender =>
                {
                    var after = preview.Units.Find(item => item.Id == defender.Id);
                    return $"{UnitName(defender.Type)} {defender.Id} 체력 " +
                           $"{(after == null ? 0 : after.HitPoints)}/{UnitRules.MaximumHitPoints(defender.Type)}";
                }));
            return $"전투 예상: 공격자 체력 {(survivor == null ? 0 : survivor.HitPoints)}/" +
                   $"{UnitRules.MaximumHitPoints(attacker.Type)}; 수비군 {defenderForecast}; " +
                   $"진입 {(result.AttackerAdvanced ? "가능" : "불가")}.";
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
            if (HasEnemyUnit(unit, command.TargetId)) command.SecondaryValue = 1;
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
            var laterType = plannedPromotions.TryGetValue(unit.Id, out var promotion)
                ? (UnitType)promotion.PrimaryValue : unit.Type;
            var laterMovement = Mathf.Max(1, UnitRules.Movement(laterType));
            return 1 + Mathf.CeilToInt((stepCount - firstTurnMovement) / (float)laterMovement);
        }

        private int PlannedMovementForTurn(UnitState unit)
        {
            if (plannedPromotions.ContainsKey(unit.Id)) return 0;
            if (unit.CreatedTurn == state.TurnNumber) return 0;
            return unit.ManeuverRecommandTurn == state.TurnNumber
                ? Mathf.Max(0, unit.RemainingMovement)
                : UnitRules.Movement(unit.Type);
        }

        private void CancelSelectedMove()
        {
            if (!selectedUnitId.IsValid) return;
            var targets = selectedUnitGroup.Count > 0 ? selectedUnitGroup.ToList() : new List<GameEntityId> { selectedUnitId };
            for (var index = 0; index < targets.Count; index++)
            {
                if (plannedMoves.TryGetValue(targets[index], out var command))
                    simulator.Planning.Cancel(activePlayerId, command.CommandId);
                plannedMoves.Remove(targets[index]);
                routePlans.Remove(targets[index]);
            }
            statusMessage = $"선택 병력 {targets.Count}부대의 이동 경로를 취소했습니다.";
            ShowCities(new[] { state.Cities[focusedCityIndex].Id });
        }

        private void ConfirmActivePlayer()
        {
            var active = FindPlayer(activePlayerId);
            if (!IsManeuverRecommandPhase() && NeedsResearchSelection(active))
            {
                statusMessage = "턴을 확정하기 전에 연구를 선택하세요.";
                showResearchPanel = true;
                return;
            }
            if (!simulator.Planning.Confirm(activePlayerId)) return;
            if (!simulator.Planning.IsClosed)
            {
                var next = FindNextUnconfirmedPlayer();
                if (next == null) return;
                activePlayerId = next.Id;
                selectedUnitId = default;
                selectedUnitGroup.Clear();
                selectedTileId = default;
                statusMessage = PlanningTurnStatus(next);
                PrepareAutomaticRoutes(activePlayerId);
                PrepareAutomaticTrades(activePlayerId);
                FocusOwnedCity(next.Id);
                return;
            }

            var resolution = simulator.ResolveConfirmedTurn();
            ResolveManeuversAsCombat(resolution);
            for (var eventIndex = 0; eventIndex < resolution.Events.Count; eventIndex++)
            {
                var gameEvent = resolution.Events[eventIndex];
                if (gameEvent.Type == GameEventType.MovementBlocked)
                    AddCombatLog($"{resolution.ResolvedTurnNumber}턴 이동 차단: 병력 {gameEvent.SourceId}, " +
                                 $"사유 {MovementStopReasonName((MovementStopReason)gameEvent.SecondaryValue)}");
                if (gameEvent.Type == GameEventType.DistrictPillaged)
                    AddCombatLog($"{resolution.ResolvedTurnNumber}턴 약탈: 지구 {gameEvent.TargetId} | " +
                                 $"보상 {gameEvent.PrimaryValue}, 식량 {gameEvent.SecondaryValue}");
                if (gameEvent.Type == GameEventType.ColdWarStarted)
                    AddCombatLog($"{resolution.ResolvedTurnNumber}턴 냉전체제: 양측 핵 프로젝트 동시 완료, " +
                                 "양 플레이어에게 자가학습 AI(과학 300) 해금");
                if (gameEvent.Type == GameEventType.UnitStarvationStarted)
                    AddCombatLog($"{resolution.ResolvedTurnNumber}턴 굶주림: 병력 {gameEvent.SourceId}의 전투력이 감소했습니다.");
                if (gameEvent.Type == GameEventType.UnitStarvedToDeath)
                    AddCombatLog($"{resolution.ResolvedTurnNumber}턴 아사: 병력 {gameEvent.SourceId}이 군량 부족으로 전멸했습니다.");
                if (gameEvent.Type == GameEventType.UnitDisbanded)
                    AddCombatLog($"{resolution.ResolvedTurnNumber}턴 유지비 해산: 병력 {gameEvent.SourceId} | " +
                                 $"반환 군량 {gameEvent.PrimaryValue}");
                if (gameEvent.Type == GameEventType.NeutralUnitsLevied)
                    AddCombatLog($"{resolution.ResolvedTurnNumber}턴 징병 성공: 플레이어 {gameEvent.TargetId} | " +
                                 $"군사도시 {gameEvent.SourceId}, {gameEvent.PrimaryValue}부대, 금 {gameEvent.SecondaryValue}");
                if (gameEvent.Type == GameEventType.NeutralLevyBidLost)
                    AddCombatLog($"{resolution.ResolvedTurnNumber}턴 징병 입찰 실패: 플레이어 {gameEvent.SourceId} | " +
                                 $"군사도시 {gameEvent.TargetId}, 제시금 {gameEvent.PrimaryValue}");
                if (gameEvent.Type == GameEventType.NeutralLevyAuctionTied)
                    AddCombatLog($"{resolution.ResolvedTurnNumber}턴 징병 입찰 동률: 군사도시 {gameEvent.TargetId} | 양측 실패");
                if (gameEvent.Type == GameEventType.NeutralOccupationYieldCollected)
                    AddCombatLog($"{resolution.ResolvedTurnNumber}턴 점령 산출: 중립도시 {gameEvent.SourceId} → " +
                                 $"도시 {gameEvent.TargetId} | {ResourceName((TileResourceType)gameEvent.PrimaryValue)} " +
                                 $"{gameEvent.SecondaryValue}");
                if (gameEvent.Type == GameEventType.PlayerCitiesExchanged)
                {
                    var firstCity = state.Cities.Find(item => item.Id == gameEvent.SourceId);
                    var secondCity = state.Cities.Find(item => item.Id == gameEvent.TargetId);
                    AddCombatLog($"{resolution.ResolvedTurnNumber}턴 동시 정복: " +
                                 $"{(firstCity == null ? gameEvent.SourceId.ToString() : firstCity.Name)} ↔ " +
                                 $"{(secondCity == null ? gameEvent.TargetId.ToString() : secondCity.Name)} " +
                                 "도시 기지 소유권 교환, 기존 병력 제어권 유지");
                }
            }
            turnLog.Insert(0, $"{resolution.ResolvedTurnNumber}턴: 명령 {resolution.Commands.Count}개, " +
                              $"충돌 {resolution.ManeuverRequests.Count}건.");
            if (turnLog.Count > 5) turnLog.RemoveAt(turnLog.Count - 1);
            plannedMoves.Clear();
            plannedDistricts.Clear();
            plannedTrainings.Clear();
            plannedPromotions.Clear();
            plannedFoodAdjustments.Clear();
            plannedRepairs.Clear();
            plannedDefenseConstructions.Clear();
            plannedDefenseActions.Clear();
            plannedNuclearProjects.Clear();
            plannedResearch.Clear();
            plannedCitizenAssignments.Clear();
            plannedFoodTransfers.Clear();
            plannedGroundFoodPickups.Clear();
            plannedNeutralTrades.Clear();
            plannedLevyBids.Clear();
            levyExtraBid = 0;
            selectedUnitId = default;
            selectedUnitGroup.Clear();
            selectedTileId = default;
            ConfigurePlanningPlayers();
            statusMessage = state.IsGameOver
                ? state.Victory == VictoryType.Draw
                    ? "게임 종료 — 무승부. 양 플레이어가 자가학습 AI를 동시에 완료했습니다."
                    : state.Victory == VictoryType.Science &&
                      state.Players.Count(item => item.Slot != PlayerSlot.Neutral && item.HasUnlockedSelfLearningAI) == 2 &&
                      !FindPlayer(state.WinnerId).HasCompletedSelfLearningAI
                        ? $"{PlayerName(FindPlayer(state.WinnerId).Slot)}가 핵을 유일하게 보유하게 되었습니다. " +
                          $"{PlayerName(FindPlayer(state.WinnerId).Slot)} 과학승리"
                        : $"게임 종료 — {PlayerName(FindPlayer(state.WinnerId).Slot)}의 {VictoryName(state.Victory)}."
                : PlanningTurnStatus(FindPlayer(activePlayerId));
            if (!state.IsGameOver)
            {
                PrepareAutomaticRoutes(activePlayerId);
                PrepareAutomaticTrades(activePlayerId);
            }
            FocusOwnedCity(activePlayerId);
            if (IsManeuverRecommandPhase())
            {
                var maneuver = state.Units.Where(item => item.OwnerId == activePlayerId &&
                        item.ManeuverRecommandTurn == state.TurnNumber && item.RemainingMovement > 0)
                    .OrderBy(item => item.Id.Value).FirstOrDefault();
                if (maneuver != null)
                {
                    selectedUnitId = maneuver.Id;
                    selectedUnitGroup.Clear();
                    selectedUnitGroup.Add(maneuver.Id);
                    selectedTileId = maneuver.TileId;
                    statusMessage = PlanningTurnStatus(FindPlayer(activePlayerId)) +
                                    " 우클릭으로 우회·전투·대기 경로를 다시 지정하세요.";
                    ShowCities(MapVisibilityResolver.ResolveCitiesForTile(state, maneuver.TileId,
                        state.Cities[focusedCityIndex].Id));
                }
            }
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
                return $"{state.TurnNumber}턴: {PlayerName(player.Slot)} 명령 예약.";
            var remaining = string.Join(", ", units
                .OrderBy(item => item.Id.Value)
                .Select(item => $"{UnitName(item.Type)} {item.Id} 이동력 {item.RemainingMovement}"));
            return $"기동 재명령 턴 — {PlayerName(player.Slot)}: {remaining}";
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
                    AddCombatLog($"{resolution.ResolvedTurnNumber}턴 기동 재명령 필요: 병력 {request.UnitId}, " +
                                 $"타일 {request.BlockedTileId} 앞에서 정지하고 기존 경로 취소");
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
                    turnLog.Insert(0, $"교전: 병력 {combat.AttackingUnitId}, " +
                                      $"{combat.DestroyedUnitIds.Count}부대 전멸.");
                    AddCombatLog($"{resolution.ResolvedTurnNumber}턴 전투: 병력 {combat.AttackingUnitId} → 타일 " +
                                 $"{combat.TargetTileId} | " +
                                 (applied.Combat.BothSidesAreAttackers ? "공격 대 공격" : "공격 대 수비"));
                    for (var damageIndex = 0; damageIndex < combat.DamageRecords.Count; damageIndex++)
                    {
                        var damage = combat.DamageRecords[damageIndex];
                        AddCombatLog($"  병력 {damage.UnitId}: 체력 -{damage.Damage}" +
                                     (damage.Destroyed ? " | 전멸" : string.Empty));
                    }
                    AddCombatLog(combat.AttackerAdvanced
                        ? "  공격자가 목표 타일에 진입"
                        : "  공격자가 목표 타일을 점령하지 못함");
                    if (combat.Occupation != null && combat.Occupation.PillageRewardGranted)
                    {
                        AddCombatLog($"  {DistrictName(combat.Occupation.DistrictType)} 약탈: " +
                                     $"보상 {combat.Occupation.PillagePrimaryReward}, " +
                                     $"식량 {combat.Occupation.PillageFoodReward}");
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
            if (combatLog.Count > 200) combatLog.RemoveAt(combatLog.Count - 1);
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
                statusMessage = "기동 재명령 중에는 도시 명령을 내릴 수 없습니다.";
                return;
            }
            if (!selectedTileId.IsValid || plannedDistricts.ContainsKey(selectedTileId)) return;
            var city = FindActiveOwnedCityForTile(selectedTileId);
            if (city == null)
            {
                statusMessage = "자신의 도시에서 건설 가능한 타일에만 건설할 수 있습니다.";
                return;
            }
            var freeCitizens = DistrictConstructionResolver.CountFreeCitizens(state, city) -
                               CountPlannedDistricts(city.Id);
            if (freeCitizens <= 0)
            {
                statusMessage = "건설에 투입할 미배정 시민이 없습니다.";
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
                statusMessage = $"건설 명령 거부: {CommandResultName(result)}";
                return;
            }

            plannedDistricts[selectedTileId] = command;
            statusMessage = $"{DistrictName(type)} 건설 예약. 시민 1명이 건설합니다.";
            ShowCities(new[] { city.Id });
        }

        private void CancelSelectedConstruction()
        {
            if (!plannedDistricts.TryGetValue(selectedTileId, out var command)) return;
            if (command.PlayerId != activePlayerId)
            {
                statusMessage = "다른 플레이어의 건설 명령은 취소할 수 없습니다.";
                return;
            }
            simulator.Planning.Cancel(activePlayerId, command.CommandId);
            plannedDistricts.Remove(selectedTileId);
            statusMessage = "건설 예약을 취소해 시민이 미배정 상태로 돌아갔습니다.";
            ShowCities(new[] { command.SubjectId });
        }

        private void ReserveAgricultureCitizens(DistrictState district, int desiredCitizens)
        {
            if (IsManeuverRecommandPhase())
            {
                statusMessage = "기동 재명령 중에는 시민 명령을 내릴 수 없습니다.";
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
                ? $"농업지구 시민 배치 예약: {desiredCitizens}명."
                : $"시민 명령 거부: {CommandResultName(result)}";
        }

        private void CancelAgricultureCitizens(DistrictState district)
        {
            if (!plannedCitizenAssignments.TryGetValue(district.Id, out var command)) return;
            simulator.Planning.Cancel(activePlayerId, command.CommandId);
            plannedCitizenAssignments.Remove(district.Id);
            statusMessage = "농업지구 시민 배치 변경을 취소했습니다.";
        }

        private void ReserveTraining(DistrictState district, UnitType type)
        {
            if (IsManeuverRecommandPhase())
            {
                statusMessage = "기동 재명령 중에는 훈련 명령을 내릴 수 없습니다.";
                return;
            }
            if (plannedTrainings.ContainsKey(district.Id))
            {
                statusMessage = "이 군사지구에는 이미 훈련 예약이 있습니다.";
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
                ? $"{DistrictName(district.Type)}에 {UnitName(type)} 훈련을 예약했습니다."
                : $"훈련 명령 거부: {CommandResultName(result)}";
        }

        private void ReservePromotion(UnitState unit, UnitType target)
        {
            var command = new GameCommand
            {
                CommandId = state.AllocateId(), PlayerId = activePlayerId,
                TurnNumber = state.TurnNumber, Type = GameCommandType.PromoteUnit,
                SubjectId = unit.Id, PrimaryValue = (int)target
            };
            var result = simulator.Planning.Reserve(command);
            if (result == CommandMutationResult.Accepted)
            {
                plannedPromotions[unit.Id] = command;
                if (plannedMoves.TryGetValue(unit.Id, out var move))
                {
                    simulator.Planning.Cancel(activePlayerId, move.CommandId);
                    plannedMoves.Remove(unit.Id);
                }
            }
            statusMessage = result == CommandMutationResult.Accepted
                ? $"{UnitName(unit.Type)} → {UnitName(target)} 승급을 예약했습니다."
                : $"승급 명령 거부: {CommandResultName(result)}";
        }

        private void ReserveGroundFoodPickup(UnitState unit, TileState tile)
        {
            if (plannedGroundFoodPickups.ContainsKey(unit.Id)) return;
            var command = new GameCommand
            {
                CommandId = state.AllocateId(), PlayerId = activePlayerId,
                TurnNumber = state.TurnNumber, Type = GameCommandType.PickupGroundFood,
                SubjectId = unit.Id, TargetId = tile.Id, PrimaryValue = int.MaxValue
            };
            var result = simulator.Planning.Reserve(command);
            if (result == CommandMutationResult.Accepted) plannedGroundFoodPickups[unit.Id] = command;
            statusMessage = result == CommandMutationResult.Accepted
                ? "현장 군량을 가능한 만큼 습득하도록 예약했습니다."
                : $"현장 군량 습득 명령 거부: {CommandResultName(result)}";
        }

        private void CancelTraining(DistrictState district)
        {
            if (!plannedTrainings.TryGetValue(district.Id, out var command)) return;
            simulator.Planning.Cancel(activePlayerId, command.CommandId);
            plannedTrainings.Remove(district.Id);
            statusMessage = "훈련 예약을 취소했습니다.";
        }

        private void ReserveDistrictRepair(DistrictState district)
        {
            if (IsManeuverRecommandPhase())
            {
                statusMessage = "기동 재명령 중에는 수리 명령을 내릴 수 없습니다.";
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
                ? $"{DistrictName(district.Type)} 수리를 예약했습니다."
                : $"수리 명령 거부: {CommandResultName(result)}";
        }

        private void CancelDistrictRepair(DistrictState district)
        {
            if (!plannedRepairs.TryGetValue(district.Id, out var command)) return;
            simulator.Planning.Cancel(activePlayerId, command.CommandId);
            plannedRepairs.Remove(district.Id);
            statusMessage = $"{DistrictName(district.Type)} 수리 예약을 취소했습니다.";
        }

        private void ReserveDefenseConstruction(DistrictState district, DefenseFacilityType type)
        {
            if (IsManeuverRecommandPhase())
            {
                statusMessage = "기동 재명령 중에는 방어시설을 건설할 수 없습니다.";
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
                ? $"{DefenseName(type)} 건설 예약(금 {DefenseFacilityResolver.GoldCost(type)})."
                : $"방어시설 건설 거부: {CommandResultName(result)}";
            if (result == CommandMutationResult.Accepted) ShowCities(new[] { district.CityId });
        }

        private void CancelDefenseConstruction(GameEntityId tileId)
        {
            if (!plannedDefenseConstructions.TryGetValue(tileId, out var command)) return;
            simulator.Planning.Cancel(activePlayerId, command.CommandId);
            plannedDefenseConstructions.Remove(tileId);
            statusMessage = "방어시설 건설 예약을 취소했습니다.";
            ShowCities(new[] { command.SubjectId });
        }

        private void ReserveModernDefenseAction(DefenseFacilityState facility, bool activate)
        {
            if (IsManeuverRecommandPhase())
            {
                statusMessage = "기동 재명령 중에는 방어시설을 제어할 수 없습니다.";
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
                ? activate ? "현대 방어체계 재활성화 예약(2턴간 유지비 지불)." : "현대 방어체계 비활성화를 예약했습니다."
                : $"방어시설 제어 거부: {CommandResultName(result)}";
        }

        private void CancelModernDefenseAction(DefenseFacilityState facility)
        {
            if (!plannedDefenseActions.TryGetValue(facility.Id, out var command)) return;
            simulator.Planning.Cancel(activePlayerId, command.CommandId);
            plannedDefenseActions.Remove(facility.Id);
            statusMessage = "현대 방어체계 제어 변경을 취소했습니다.";
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
                ? "핵무기 프로젝트 예약(금 10/5턴)."
                : $"핵 프로젝트 거부: {CommandResultName(result)}";
        }

        private void CancelNuclearProject(DistrictState district)
        {
            if (!plannedNuclearProjects.TryGetValue(district.Id, out var command)) return;
            simulator.Planning.Cancel(activePlayerId, command.CommandId);
            plannedNuclearProjects.Remove(district.Id);
            statusMessage = "핵 프로젝트 예약을 취소했습니다.";
        }

        private void ReserveResearch(ResearchType type)
        {
            if (IsManeuverRecommandPhase())
            {
                statusMessage = "기동 재명령 중에는 연구를 선택할 수 없습니다.";
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
                ? $"연구 선택 예약: {ResearchName(type)}."
                : $"연구 선택 거부: {CommandResultName(result)}";
        }

        private void CancelResearchReservation()
        {
            if (!plannedResearch.TryGetValue(activePlayerId, out var command)) return;
            simulator.Planning.Cancel(activePlayerId, command.CommandId);
            plannedResearch.Remove(activePlayerId);
            statusMessage = "연구 변경 예약을 취소했습니다. 기존 연구를 계속합니다.";
        }

        private void AdjustFocusedCityPopulationCheat(int change)
        {
            var city = state.Cities[focusedCityIndex];
            if (city.OwnerId != activePlayerId)
            {
                statusMessage = "시민 치트는 현재 플레이어의 도시에만 적용됩니다.";
                return;
            }
            if (change < 0 && city.Population <= 1)
            {
                statusMessage = "인구는 1명 아래로 감소시킬 수 없습니다.";
                return;
            }
            city.Population += change > 0 ? 1 : -1;
            CityCultureRules.Normalize(city);
            CitizenAssignmentResolver.RemoveExcessCitizen(state, city);
            statusMessage = $"테스트 치트: {city.Name} 인구가 {city.Population}명이 되었습니다.";
            ShowCities(new[] { city.Id });
        }

        private void AdjustSelectedUnitFood(UnitState unit, int change)
        {
            if (IsManeuverRecommandPhase())
            {
                statusMessage = "기동 재명령 중에는 군량을 조절할 수 없습니다.";
                return;
            }
            var city = FindOwnedCityForUnit(unit);
            if (city == null)
            {
                statusMessage = "자신이 통제하는 본토에서만 병력 군량을 조절할 수 있습니다.";
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
                statusMessage = "군량 조절 예약을 취소했습니다.";
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
                statusMessage = $"군량 조절 거부: {CommandResultName(result)}";
                return;
            }
            plannedFoodAdjustments[unit.Id] = command;
            statusMessage = adjustment > 0
                ? $"군량 {adjustment} 적재를 예약했습니다."
                : $"{city.Name}에 군량 {-adjustment} 반환을 예약했습니다.";
        }

        private void AdjustFoodTransfer(UnitState supplier, UnitState receiver, int change)
        {
            if (IsManeuverRecommandPhase())
            {
                statusMessage = "기동 재명령 중에는 군량을 교환할 수 없습니다.";
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
                statusMessage = "군량 교환 예약을 취소했습니다.";
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
                ? $"군량 {amount} 교환 예약: {UnitName(supplier.Type)} → {UnitName(receiver.Type)}."
                : $"군량 교환 거부: {CommandResultName(result)}";
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

        private static string RepeatingNeutralTradeKey(GameEntityId playerId, GameEntityId cityId,
            TileResourceType resource)
        {
            return $"{playerId.Value}:{cityId.Value}:{(int)resource}";
        }

        private static string LevyBidKey(GameEntityId playerId, GameEntityId cityId)
        {
            return $"{playerId.Value}:{cityId.Value}";
        }

        private void ToggleNeutralTradeRepeat(CityState city, TileResourceType resource)
        {
            var key = RepeatingNeutralTradeKey(activePlayerId, city.Id, resource);
            if (!repeatingNeutralTrades.Add(key)) repeatingNeutralTrades.Remove(key);
            statusMessage = repeatingNeutralTrades.Contains(key)
                ? $"{city.Name} 교역을 다음 턴에도 자동 예약합니다."
                : $"{city.Name} 교역 자동 예약을 해제했습니다.";
        }

        private void PrepareAutomaticTrades(GameEntityId playerId)
        {
            if (IsManeuverRecommandPhase()) return;
            var previousActive = activePlayerId;
            activePlayerId = playerId;
            var neutral = FindPlayer(PlayerSlot.Neutral);
            if (neutral != null)
            {
                for (var cityIndex = 0; cityIndex < state.Cities.Count; cityIndex++)
                {
                    var city = state.Cities[cityIndex];
                    if (city.OwnerId != neutral.Id) continue;
                    if (city.NeutralSpecialization == NeutralCitySpecialization.Science ||
                        city.NeutralSpecialization == NeutralCitySpecialization.Culture)
                    {
                        var resource = city.NeutralSpecialization == NeutralCitySpecialization.Science
                            ? TileResourceType.Science : TileResourceType.Culture;
                        if (repeatingNeutralTrades.Contains(RepeatingNeutralTradeKey(playerId, city.Id, resource)))
                            ReserveNeutralTrade(city, resource);
                    }
                    else if (city.NeutralSpecialization == NeutralCitySpecialization.Commerce)
                    {
                        var resources = new[] { TileResourceType.Food, TileResourceType.Science, TileResourceType.Culture };
                        for (var resourceIndex = 0; resourceIndex < resources.Length; resourceIndex++)
                            if (repeatingNeutralTrades.Contains(RepeatingNeutralTradeKey(playerId, city.Id, resources[resourceIndex])))
                                ReserveNeutralTrade(city, resources[resourceIndex]);
                    }
                }
            }
            activePlayerId = previousActive;
        }

        private void ReserveNeutralTrade(CityState neutralCity, TileResourceType resource)
        {
            if (IsManeuverRecommandPhase())
            {
                statusMessage = "기동 재명령 중에는 교역할 수 없습니다.";
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
                statusMessage = $"{neutralCity.Name} 교역을 예약했습니다.";
            }
            else statusMessage = $"교역 예약 실패: {CommandResultName(result)}.";
        }

        private void CancelNeutralTrade(CityState neutralCity, TileResourceType resource)
        {
            var key = NeutralTradeKey(neutralCity.Id, resource);
            if (!plannedNeutralTrades.TryGetValue(key, out var command)) return;
            simulator.Planning.Cancel(activePlayerId, command.CommandId);
            plannedNeutralTrades.Remove(key);
            statusMessage = $"{neutralCity.Name} 교역 예약을 취소했습니다.";
        }

        private void ReserveLevyBid(CityState militaryCity, int basePrice)
        {
            if (IsManeuverRecommandPhase())
            {
                statusMessage = "기동 재명령 중에는 징병 입찰을 할 수 없습니다.";
                return;
            }
            var home = FindActiveHomeCity();
            if (home == null) return;
            var key = LevyBidKey(activePlayerId, militaryCity.Id);
            if (plannedLevyBids.TryGetValue(key, out var previous))
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
                plannedLevyBids[key] = command;
                statusMessage = $"징병 입찰 금 {basePrice + levyExtraBid}를 예약했습니다.";
            }
            else statusMessage = $"징병 입찰 예약 실패: {CommandResultName(result)}.";
        }

        private void CancelLevyBid(CityState militaryCity)
        {
            var key = LevyBidKey(activePlayerId, militaryCity.Id);
            if (!plannedLevyBids.TryGetValue(key, out var command)) return;
            simulator.Planning.Cancel(activePlayerId, command.CommandId);
            plannedLevyBids.Remove(key);
            statusMessage = $"{militaryCity.Name} 징병 입찰을 취소했습니다.";
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
                unitObject.GetComponent<MeshRenderer>().sharedMaterial = ResolveUnitMaterial(unit);
                unitObject.AddComponent<PrototypeUnitView>().Initialize(this, unit);
            }
        }

        private Material ResolveUnitMaterial(UnitState unit)
        {
            var owner = state.Players.Find(player => player.Id == unit.OwnerId);
            if (owner == null || owner.Slot == PlayerSlot.Neutral)
            {
                var city = state.Cities.Find(item => item.Id == unit.HomeCityId);
                if (city == null) return neutralUnitMaterial;
                if (neutralCityUnitMaterials.TryGetValue(city.Id, out var cached)) return cached;
                var alternate = ((city.WorldQ + city.WorldR) & 1) == 0 ? 0.08f : -0.08f;
                Color color;
                switch (city.NeutralSpecialization)
                {
                    case NeutralCitySpecialization.Military: color = new Color(0.82f + alternate, 0.42f, 0.12f); break;
                    case NeutralCitySpecialization.Science: color = new Color(0.10f, 0.72f + alternate, 0.66f); break;
                    case NeutralCitySpecialization.Culture: color = new Color(0.72f + alternate, 0.20f, 0.66f); break;
                    case NeutralCitySpecialization.Commerce: color = new Color(0.82f, 0.72f + alternate, 0.10f); break;
                    default: color = new Color(0.72f + alternate, 0.72f + alternate, 0.72f + alternate); break;
                }
                cached = CreateMaterial(color);
                neutralCityUnitMaterials[city.Id] = cached;
                return cached;
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
                    case DistrictType.NuclearFacility: return modernDefenseMaterial;
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
            GUI.Label(new Rect(28f, 25f, 360f, 22f), $"도시 {city.Name}  월드 좌표 ({city.WorldQ}, {city.WorldR})");
            GUI.Label(new Rect(28f, 47f, 360f, 22f),
                $"인구 {city.Population} | 금 {city.Gold} | 비축 식량 {city.StoredFood}");
            DrawYieldRow(28f, 73f, "식량", economy.Food);
            GUI.Label(new Rect(44f, 94f, 340f, 20f),
                $"소비: 인구 -{economy.PopulationConsumption}, 병력 -{economy.UnitFoodConsumption} " +
                $"=> 순생산 {Signed(economy.FoodNet)}");
            DrawYieldRow(28f, 118f, "금", economy.Gold);
            GUI.Label(new Rect(44f, 139f, 340f, 20f),
                $"유지비: 병력 -{economy.UnitUpkeep}, 시설 -{economy.FacilityUpkeep}");
            DrawYieldRow(28f, 163f, "과학", economy.Science);
            DrawYieldRow(28f, 187f, "문화", economy.Culture);
            DrawCultureStatus(city, 28f, 212f);
            GUI.Label(new Rect(28f, 270f, 380f, 20f), CitizenAssignmentSummary(city));
            GUI.Label(new Rect(28f, 290f, 360f, 20f),
                $"성장 {city.GrowthProgress}/{economy.GrowthRequired} | 기근 {city.FamineProgress}/{economy.FamineRequired}");
            var active = FindPlayer(activePlayerId);
            var reCommandCount = state.Units.Count(item =>
                item.OwnerId == activePlayerId && item.ManeuverRecommandTurn == state.TurnNumber);
            GUI.Label(new Rect(28f, 317f, 380f, 20f),
                reCommandCount > 0
                    ? $"기동 재명령 턴 | {PlayerName(active.Slot)} | 대상 {reCommandCount} | 예약 {simulator.Planning.GetOwnCommands(activePlayerId).Count}"
                    : $"일반 턴 {state.TurnNumber} | {PlayerName(active.Slot)} | 예약 {simulator.Planning.GetOwnCommands(activePlayerId).Count}");
            GUI.Label(new Rect(28f, 339f, 380f, 40f), statusMessage);
            GUI.enabled = !state.IsGameOver;
            if (GUI.Button(new Rect(28f, 383f, 170f, 30f), "선택 병력 경로 취소")) CancelSelectedMove();
            GUI.enabled = true;
            if (GUI.Button(new Rect(210f, 383f, 198f, 30f),
                    state.IsGameOver ? "1턴부터 다시 시작" : "플레이어 턴 확정"))
            {
                if (state.IsGameOver) RestartMatch();
                else ConfirmActivePlayer();
            }
            GUI.enabled = true;
            if (GUI.Button(new Rect(28f, 418f, 116f, 30f),
                showResearchPanel ? "연구 닫기" : "연구 열기"))
            {
                showResearchPanel = !showResearchPanel;
                if (showResearchPanel) { showCheatPanel = false; showNeutralTradePanel = false; }
            }
            if (GUI.Button(new Rect(154f, 418f, 116f, 30f),
                showCheatPanel ? "치트 닫기" : "치트 열기"))
            {
                showCheatPanel = !showCheatPanel;
                if (showCheatPanel) { showResearchPanel = false; showNeutralTradePanel = false; }
            }
            if (GUI.Button(new Rect(280f, 418f, 128f, 30f),
                showNeutralTradePanel ? "교역 닫기" : "중립 교역"))
            {
                showNeutralTradePanel = !showNeutralTradePanel;
                if (showNeutralTradePanel) { showResearchPanel = false; showCheatPanel = false; }
            }
            GUI.Label(new Rect(28f, 453f, 380f, 20f), "좌클릭: 선택 | 우클릭: 이동 | WASD: 화면 이동 | 휠: 확대/축소");
            if (turnLog.Count > 0) GUI.Label(new Rect(28f, 477f, 380f, 20f), turnLog[0]);
            DrawSelectedTilePanel();
            if (showResearchPanel) DrawResearchPanel();
            if (showCheatPanel) DrawCheatPanel();
            if (showNeutralTradePanel) DrawNeutralTradePanel();
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
                $"문화 선호 시민: 고유 {native} | 플레이어 1 {oneCitizens} | 플레이어 2 {twoCitizens}");

            var progress = new List<string>();
            AddCultureProgress(progress, city, one);
            AddCultureProgress(progress, city, two);
            GUI.Label(new Rect(x, y + 22f, 380f, 20f),
                progress.Count == 0 ? "문화 전향 진행: 없음" : $"문화 전향 진행: {string.Join(" | ", progress)}");

            var owner = FindPlayer(city.OwnerId);
            if (owner != null && owner.Slot == PlayerSlot.Neutral)
            {
                var subjectPlayer = city.CultureSubjectToId.IsValid ? FindPlayer(city.CultureSubjectToId) : null;
                var subject = subjectPlayer == null ? "없음" : PlayerName(subjectPlayer.Slot);
                GUI.Label(new Rect(x, y + 44f, 380f, 20f),
                    $"중립도시 문화 저항 {NeutralCultureResolver.Resistance(state, city)} | 문화 종속: {subject}");
                return;
            }
            var foreign = owner != null && owner.Slot == PlayerSlot.PlayerOne ? twoCitizens : oneCitizens;
            var defeatAt = (city.Population / 2) + 1;
            var attacker = owner != null && owner.Slot == PlayerSlot.PlayerOne ? two : one;
            var attackerCity = attacker == null ? null : state.Cities.Find(item => item.OwnerId == attacker.Id);
            var cultureGap = attackerCity == null ? 0 : attackerCity.LastCultureProduction - city.LastCultureProduction;
            if (foreign >= defeatAt)
            {
                GUI.Label(new Rect(x, y + 44f, 380f, 20f),
                    $"문화패배: 상대문화 시민 {foreign}/{defeatAt}");
                return;
            }
            if (cultureGap > 0)
            {
                var turns = EstimateCultureResultTurns(attackerCity, city, attacker.Id, owner.Id, cultureGap);
                GUI.Label(new Rect(x, y + 44f, 380f, 20f),
                    $"문화 방어: 상대문화 {foreign}/{defeatAt} | 약 {turns}턴 후 패배");
            }
            else if (cultureGap < 0)
            {
                var turns = EstimateCultureResultTurns(city, attackerCity, owner.Id, attacker.Id, -cultureGap);
                GUI.Label(new Rect(x, y + 44f, 380f, 20f),
                    $"문화 공세: 약 {turns}턴 후 승리");
            }
            else GUI.Label(new Rect(x, y + 44f, 380f, 20f),
                $"문화 방어: 상대문화 {foreign}/{defeatAt} | 현재 변화 없음");
        }

        private static int EstimateCultureResultTurns(CityState winnerHome, CityState loserHome,
            GameEntityId winnerCulture, GameEntityId loserCulture, int amount)
        {
            if (winnerHome == null || loserHome == null || amount <= 0) return 0;
            var winner = CloneCultureForecastCity(winnerHome);
            var loser = CloneCultureForecastCity(loserHome);
            for (var turn = 1; turn <= 999; turn++)
            {
                CultureConversionResolver.ApplyAdvantage(winner, loser, winnerCulture, loserCulture, amount);
                if (CultureVictoryConditionResolver.HasForeignMajority(loser, winnerCulture)) return turn;
            }
            return 999;
        }

        private static CityState CloneCultureForecastCity(CityState source)
        {
            var clone = new CityState { Id = source.Id, OwnerId = source.OwnerId, Population = source.Population };
            if (source.CultureInfluences == null) return clone;
            for (var index = 0; index < source.CultureInfluences.Count; index++)
            {
                var influence = source.CultureInfluences[index];
                clone.CultureInfluences.Add(new CultureInfluenceState
                {
                    CultureOwnerId = influence.CultureOwnerId,
                    PreferredCitizens = influence.PreferredCitizens,
                    ConversionProgress = influence.ConversionProgress,
                    ReversionProgress = influence.ReversionProgress
                });
            }
            return clone;
        }

        private string CitizenAssignmentSummary(CityState city)
        {
            var assigned = state.Districts.Where(item => item.CityId == city.Id)
                .GroupBy(item => item.Type)
                .ToDictionary(group => group.Key, group => group.Sum(item => item.AssignedCitizens));
            int Count(DistrictType type) => assigned.TryGetValue(type, out var value) ? value : 0;
            var used = city.GovernmentCitizens + assigned.Where(item => item.Key != DistrictType.Government)
                .Sum(item => item.Value);
            return $"시민: 정부청사 {city.GovernmentCitizens} | 농업 {Count(DistrictType.Agriculture)} | " +
                   $"상업 {Count(DistrictType.Commerce)} | 과학 {Count(DistrictType.Science)} | " +
                   $"문화 {Count(DistrictType.Culture)} | 군사 {Count(DistrictType.Military)} | 미배정 {Mathf.Max(0, city.Population - used)}";
        }

        private static void AddCultureProgress(List<string> output, CityState city, PlayerState player)
        {
            if (player == null || player.Id == city.OwnerId || city.CultureInfluences == null) return;
            var influence = city.CultureInfluences.Find(item => item.CultureOwnerId == player.Id);
            if (influence == null || (influence.ConversionProgress <= 0 && influence.ReversionProgress <= 0)) return;
            output.Add(influence.ReversionProgress > 0
                ? $"{PlayerName(player.Slot)} 복귀까지 {10 - influence.ReversionProgress}"
                : $"{PlayerName(player.Slot)} 전향까지 {10 - influence.ConversionProgress}");
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
                $"연구 — {PlayerName(player.Slot)}");
            if (GUI.Button(new Rect(rect.x + 310f, rect.y + 8f, 90f, 28f), "닫기"))
            {
                showResearchPanel = false;
                return;
            }
            GUI.Label(new Rect(rect.x + 14f, rect.y + 38f, 380f, 22f),
                shownResearch == ResearchType.None
                    ? "현재 연구: 없음(과학은 최대 1턴분만 보관)"
                    : $"현재 연구: {ResearchName(shownResearch)} {currentProgress}/{ResearchRules.Cost(shownResearch)}" +
                      (planned == null ? string.Empty : " (변경 예약)"));
            var completedText = player.CompletedResearch == null || player.CompletedResearch.Count == 0
                ? "없음"
                : string.Join(", ", player.CompletedResearch.OrderBy(item => (int)item).Select(ResearchName));
            GUI.Label(new Rect(rect.x + 14f, rect.y + 62f, 380f, 42f),
                $"완료 연구: {completedText}");
            GUI.Label(new Rect(rect.x + 14f, rect.y + 86f, 380f, 42f),
                shownResearch == ResearchType.None
                    ? "효과: 연구를 선택하면 완료 효과가 표시됩니다."
                    : $"효과: {ResearchEffectDescription(shownResearch)}");
            if (planned != null && GUI.Button(new Rect(rect.x + 14f, rect.y + 104f, 376f, 28f),
                "연구 변경 예약 취소"))
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
                var available = IsResearchAvailable(player, type);
                var progress = ResearchResolver.Progress(player, type);
                var label = completed
                    ? $"✓ {ResearchName(type)}"
                    : available
                        ? $"{ResearchName(type)} {progress}/{ResearchRules.Cost(type)}"
                        : $"{ResearchName(type)} ← {ResearchName(prerequisite)}";
                GUI.enabled = !state.IsGameOver && !IsManeuverRecommandPhase() && available && !completed;
                if (GUI.Button(new Rect(2f + (column * 188f), 4f + (row * 38f), 182f, 32f), label))
                    ReserveResearch(type);
            }
            GUI.enabled = true;
            GUI.EndScrollView();
        }

        private void DrawCheatPanel()
        {
            var rect = ResearchPanelRect();
            GUI.Box(rect, string.Empty);
            var city = state.Cities[focusedCityIndex];
            GUI.Label(new Rect(rect.x + 14f, rect.y + 10f, 280f, 24f), $"테스트 치트 — {city.Name}");
            if (GUI.Button(new Rect(rect.x + 310f, rect.y + 8f, 90f, 28f), "닫기"))
            { showCheatPanel = false; return; }
            GUI.enabled = city.OwnerId == activePlayerId;
            if (GUI.Button(new Rect(rect.x + 14f, rect.y + 44f, 376f, 30f), "모든 일반 연구 완료"))
            {
                ResearchResolver.CompleteAllStandardResearchForTesting(state, activePlayerId);
                statusMessage = "테스트 치트 적용: 일반 연구 19개를 모두 완료했습니다.";
            }
            if (GUI.Button(new Rect(rect.x + 14f, rect.y + 82f, 182f, 30f), "시민 +1")) AdjustFocusedCityPopulationCheat(1);
            if (GUI.Button(new Rect(rect.x + 208f, rect.y + 82f, 182f, 30f), "시민 -1")) AdjustFocusedCityPopulationCheat(-1);
            DrawYieldCheatRow(rect, 122f, "정부청사 식량", ref city.TestGovernmentFoodBonus, CityEconomyResolver.GovernmentFood);
            DrawYieldCheatRow(rect, 160f, "정부청사 과학", ref city.TestGovernmentScienceBonus, CityEconomyResolver.GovernmentScience);
            DrawYieldCheatRow(rect, 198f, "정부청사 문화", ref city.TestGovernmentCultureBonus, CityEconomyResolver.GovernmentCulture);
            DrawYieldCheatRow(rect, 236f, "정부청사 금", ref city.TestGovernmentGoldBonus, CityEconomyResolver.GovernmentGold);
            GUI.enabled = true;
            if (GUI.Button(new Rect(rect.x + 14f, rect.y + 278f, 376f, 32f), "1턴부터 경기 다시 시작"))
                RestartMatch();
        }

        private static void DrawYieldCheatRow(Rect rect, float y, string label, ref int bonus, int baseValue)
        {
            GUI.Label(new Rect(rect.x + 14f, rect.y + y, 210f, 28f), $"{label}: {Mathf.Max(0, baseValue + bonus)}");
            if (GUI.Button(new Rect(rect.x + 238f, rect.y + y, 70f, 28f), "-1")) bonus = Mathf.Max(-baseValue, bonus - 1);
            if (GUI.Button(new Rect(rect.x + 320f, rect.y + y, 70f, 28f), "+1")) bonus++;
        }

        private bool NeedsResearchSelection(PlayerState player)
        {
            if (player == null || player.CurrentResearch != ResearchType.None ||
                plannedResearch.ContainsKey(player.Id)) return false;
            return System.Enum.GetValues(typeof(ResearchType)).Cast<ResearchType>().Any(type =>
                type != ResearchType.None && !player.CompletedResearch.Contains(type) &&
                IsResearchAvailable(player, type));
        }

        private static bool IsResearchAvailable(PlayerState player, ResearchType type)
        {
            if (type == ResearchType.SelfLearningAI && !player.HasUnlockedSelfLearningAI) return false;
            var prerequisite = ResearchRules.Prerequisite(type);
            return prerequisite == ResearchType.None || player.CompletedResearch.Contains(prerequisite);
        }

        private static string ResearchEffectDescription(ResearchType type)
        {
            switch (type)
            {
                case ResearchType.School: return "과학지구를 해금합니다.";
                case ResearchType.IronWorking: return "철제 보병과 민병대 승급을 해금합니다.";
                case ResearchType.Gunpowder: return "화약 보병과 철제 보병 승급을 해금합니다.";
                case ResearchType.Vehicles: return "기계화보병·차량화 보급대와 승급을 해금합니다.";
                case ResearchType.NuclearFission: return "도시당 핵시설 하나를 해금합니다.";
                case ResearchType.Arts: return "문화지구를 해금합니다.";
                case ResearchType.Fortification: return "정부청사의 성벽을 해금합니다.";
                case ResearchType.AdvancedFortification: return "정부청사의 해자를 해금합니다.";
                case ResearchType.ModernDefense: return "정부청사의 현대 방어체계를 해금합니다.";
                case ResearchType.Salting: return "모든 병력의 최대 군량을 기본값의 150%로 높입니다.";
                case ResearchType.Canning: return "모든 병력의 최대 군량을 기본값의 200%로 높입니다.";
                case ResearchType.SelfLearningAI: return "냉전체제 과학 경쟁에서 승리합니다. 동시 완료는 무승부입니다.";
                default: return "해당 도시 전문화 효과를 향상합니다.";
            }
        }

        private void DrawCombatLog()
        {
            if (combatLog.Count == 0) return;
            var logicalHeight = Screen.height / UiScale;
            const float height = 166f;
            var y = Mathf.Max(526f, logicalHeight - height - 16f);
            GUI.Box(new Rect(16f, y, 700f, height), string.Empty);
            GUI.Label(new Rect(28f, y + 8f, 660f, 22f), "전투/이동 기록");
            var viewport = new Rect(24f, y + 30f, 684f, 128f);
            var contentHeight = Mathf.Max(viewport.height, combatLog.Count * 22f);
            combatLogScroll = GUI.BeginScrollView(viewport, combatLogScroll,
                new Rect(0f, 0f, 654f, contentHeight));
            for (var index = 0; index < combatLog.Count; index++)
                GUI.Label(new Rect(4f, index * 22f, 640f, 22f), combatLog[index]);
            GUI.EndScrollView();
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
                ? $"군량 {selectedTile.GroundFood} | 소유자 {selectedTile.GroundFoodOwnerId}" +
                  (selectedTile.GroundFoodReturnTurn > 0
                       ? $" | {selectedTile.GroundFoodReturnTurn}턴에 귀속"
                       : " | 타일에 보관")
                : "군량 0";
            var resourceInfo = selectedTile == null ? TileResourceType.None : selectedTile.ResourceType;
            GUI.Label(new Rect(x + 14f, yOffset + 10f, 390f, 24f),
                $"타일 {selectedTileId} | 자원 {ResourceName(resourceInfo)} | {groundFoodInfo}");

            var neutralCity = FindNeutralCityForTile(selectedTileId);
            if (neutralCity != null)
            {
                DrawNeutralCityActions(x, yOffset, neutralCity);
                return;
            }

            if (plannedDistricts.TryGetValue(selectedTileId, out var planned))
            {
                if (planned.PlayerId == activePlayerId)
                {
                    GUI.Label(new Rect(x + 14f, yOffset + 40f, 380f, 22f),
                        $"예약: {DistrictName((DistrictType)planned.PrimaryValue)} | 건설 시민 1명");
                    if (GUI.Button(new Rect(x + 14f, yOffset + 72f, 190f, 30f), "건설 예약 취소"))
                        CancelSelectedConstruction();
                }
                else
                {
                    GUI.Label(new Rect(x + 14f, yOffset + 40f, 380f, 42f),
                        "이 타일에는 다른 플레이어의 명령이 예약되어 있습니다.");
                }
                return;
            }

            var district = state.Districts.Find(item => item.TileId == selectedTileId);
            if (district != null)
            {
                DrawDistrictActions(x, yOffset, district);
                var actionOffset = district.Type == DistrictType.Military ? 232f : 190f;
                if (district.Type == DistrictType.Government)
                {
                    DrawDefenseFacilityActions(x, yOffset + actionOffset, district);
                    actionOffset += 136f;
                }
                DrawUnitsOnSelectedTile(x, yOffset + actionOffset);
                return;
            }

            var city = FindActiveOwnedCityForTile(selectedTileId);
            if (city == null)
            {
                GUI.Label(new Rect(x + 14f, yOffset + 40f, 380f, 42f),
                    "현재 플레이어가 개발할 수 없는 타일입니다.");
                DrawUnitsOnSelectedTile(x, yOffset + 90f);
                return;
            }

            var free = DistrictConstructionResolver.CountFreeCitizens(state, city) -
                       CountPlannedDistricts(city.Id);
            GUI.Label(new Rect(x + 14f, yOffset + 40f, 380f, 22f),
                $"미개발 도시 타일 | 미배정 시민: {free}");
            GUI.Label(new Rect(x + 14f, yOffset + 64f, 380f, 22f),
                "건설할 지구를 선택하세요. 미배정 시민 1명이 건설에 투입됩니다.");
            GUI.enabled = !state.IsGameOver && free > 0;
            DrawDistrictBuildButton(x + 14f, yOffset + 96f, DistrictType.Agriculture);
            DrawDistrictBuildButton(x + 210f, yOffset + 96f, DistrictType.Commerce);
            DrawDistrictBuildButton(x + 14f, yOffset + 134f, DistrictType.Science);
            DrawDistrictBuildButton(x + 210f, yOffset + 134f, DistrictType.Culture);
            DrawDistrictBuildButton(x + 14f, yOffset + 172f, DistrictType.Military);
            DrawDistrictBuildButton(x + 210f, yOffset + 172f, DistrictType.NuclearFacility);
            GUI.enabled = true;
            DrawUnitsOnSelectedTile(x, yOffset + 220f);
        }

        private void DrawNeutralCityActions(float x, float y, CityState city)
        {
            var stage = NeutralCityRules.DevelopmentStage(state, city);
            var playerOne = FindPlayer(PlayerSlot.PlayerOne);
            var playerTwo = FindPlayer(PlayerSlot.PlayerTwo);
            var subjectPlayer = city.CultureSubjectToId.IsValid ? FindPlayer(city.CultureSubjectToId) : null;
            var subject = subjectPlayer == null ? "없음" : PlayerName(subjectPlayer.Slot);
            var occupierPlayer = city.OccupyingPlayerId.IsValid ? FindPlayer(city.OccupyingPlayerId) : null;
            var occupier = occupierPlayer == null ? "없음" : PlayerName(occupierPlayer.Slot);
            GUI.Label(new Rect(x + 14f, y + 40f, 380f, 22f),
                $"중립도시 {city.Name} | {SpecializationName(city.NeutralSpecialization)} | {DevelopmentStageName(stage)}");
            GUI.Label(new Rect(x + 14f, y + 64f, 380f, 22f),
                $"우호도: 플레이어 1 {NeutralCityRules.Favor(city, playerOne.Id)} | " +
                $"플레이어 2 {NeutralCityRules.Favor(city, playerTwo.Id)} | 문화 종속: {subject} | 점령: {occupier}");
            var required = NeutralOccupationResolver.RequiredStrength(city);
            var garrison = NeutralOccupationResolver.GarrisonStrength(state, city);
            var p1Breakdown = NeutralCultureResolver.InfluenceBreakdown(state, playerOne.Id, city);
            var p2Breakdown = NeutralCultureResolver.InfluenceBreakdown(state, playerTwo.Id, city);
            var p1Influence = p1Breakdown.EffectiveInfluence;
            var p2Influence = p2Breakdown.EffectiveInfluence;
            var nativeResistance = NeutralCultureResolver.Resistance(state, city);
            var p1Culture = city.CultureInfluences.Find(item => item.CultureOwnerId == playerOne.Id);
            var p2Culture = city.CultureInfluences.Find(item => item.CultureOwnerId == playerTwo.Id);
            GUI.Label(new Rect(x + 14f, y + 88f, 380f, 22f),
                $"시민: 고유문화 {CityCultureRules.NativeCitizens(city)} | 플레이어 1 {CityCultureRules.PreferredCitizens(city, playerOne.Id)} | " +
                $"플레이어 2 {CityCultureRules.PreferredCitizens(city, playerTwo.Id)}");
            GUI.Label(new Rect(x + 14f, y + 110f, 380f, 82f),
                $"진행도: 플레이어 1 {FormatCultureProgress(p1Culture)} | 플레이어 2 {FormatCultureProgress(p2Culture)}\n" +
                $"고유문화 저항 {nativeResistance} (문화지구 +{Mathf.Max(0, nativeResistance - NeutralCultureResolver.BaseResistance)})\n" +
                $"플레이어 1 문화 {p1Breakdown.SourceCulture} - 거리 페널티 {p1Breakdown.DistancePenalty} = {p1Influence}\n" +
                $"플레이어 2 문화 {p2Breakdown.SourceCulture} - 거리 페널티 {p2Breakdown.DistancePenalty} = {p2Influence}");

            var selectedNeutralDistrict = state.Districts.Find(item => item.TileId == selectedTileId);
            GUI.Label(new Rect(x + 14f, y + 194f, 380f, 22f),
                selectedNeutralDistrict == null
                    ? "선택 타일: 미개발"
                    : $"선택 지구: {DistrictName(selectedNeutralDistrict.Type)} | {DistrictStatus(selectedNeutralDistrict, city)}");

            var home = FindActiveHomeCity();
            if (home == null) return;
            GUI.Label(new Rect(x + 14f, y + 218f, 380f, 22f),
                $"{PlayerName(FindPlayer(activePlayerId).Slot)} 외교 명령");
            if (city.NeutralSpecialization == NeutralCitySpecialization.Science ||
                city.NeutralSpecialization == NeutralCitySpecialization.Culture)
            {
                DrawPurchaseTrade(x, y + 244f, city, home);
            }
            else if (city.NeutralSpecialization == NeutralCitySpecialization.Commerce)
            {
                DrawCommerceTrades(x, y + 244f, city, home);
            }
            else if (city.NeutralSpecialization == NeutralCitySpecialization.Military)
            {
                DrawLevyBid(x, y + 244f, city, home);
            }
            else GUI.Label(new Rect(x + 14f, y + 246f, 380f, 24f), "사용 가능한 전문화 행동이 없습니다.");

            DrawUnitsOnSelectedTile(x, y + 420f);
        }

        private static string DistrictStatus(DistrictState district, CityState city)
        {
            if (district.RemainingConstructionTurns > 0)
                return $"건설 중 ({district.RemainingConstructionTurns}턴)";
            if (district.ControllerId != city.OwnerId) return "점령당함";
            if (district.RemainingRepairTurns > 0) return $"수리 중 ({district.RemainingRepairTurns}턴)";
            if (district.IsPillaged) return "약탈당함";
            if (district.IsMaintenanceSuspended) return "유지비 부족으로 정지";
            return district.IsOperational ? "가동 중" : "비활성";
        }

        private static string FormatCultureProgress(CultureInfluenceState influence)
        {
            if (influence == null) return $"0/{CityCultureRules.ProgressPerCitizen}";
            if (influence.ReversionProgress > 0)
                return $"복귀 {influence.ReversionProgress}/{CityCultureRules.ProgressPerCitizen}";
            return $"{influence.ConversionProgress}/{CityCultureRules.ProgressPerCitizen}";
        }

        private void DrawPurchaseTrade(float x, float y, CityState city, CityState home)
        {
            var quote = NeutralTradeQuoteResolver.Quote(state, activePlayerId, home.Id, city.Id);
            var receivedResource = city.NeutralSpecialization == NeutralCitySpecialization.Science
                ? TileResourceType.Science : TileResourceType.Culture;
            var route = FormatTradeRoute(quote.Route);
            GUI.Label(new Rect(x + 14f, y, 380f, 22f),
                $"최종 교환비: 금 {quote.TotalGoldCost} → {ResourceName(quote.ReceivedResource)} {quote.ResourceAmount}" +
                (quote.IsAvailable ? string.Empty : $" | 거래 불가: {NeutralTradeFailureName(quote.Failure)}"));
            GUI.Label(new Rect(x + 14f, y + 23f, 380f, 42f), route);
            var key = NeutralTradeKey(city.Id, receivedResource);
            var reserved = plannedNeutralTrades.ContainsKey(key);
            GUI.enabled = !state.IsGameOver && !IsManeuverRecommandPhase() && (quote.IsAvailable || reserved);
            if (GUI.Button(new Rect(x + 14f, y + 68f, 248f, 30f),
                    reserved ? "교역 예약 취소" : "교역 예약"))
            {
                if (reserved) CancelNeutralTrade(city, receivedResource);
                else ReserveNeutralTrade(city, receivedResource);
            }
            GUI.enabled = !state.IsGameOver;
            var repeatKey = RepeatingNeutralTradeKey(activePlayerId, city.Id, receivedResource);
            if (GUI.Button(new Rect(x + 270f, y + 68f, 120f, 30f),
                    repeatingNeutralTrades.Contains(repeatKey)
                        ? "다음 턴 자동예약: 켜짐" : "다음 턴 자동예약: 꺼짐"))
                ToggleNeutralTradeRepeat(city, receivedResource);
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
                GUI.Label(new Rect(x + 14f, rowY, 250f, 42f),
                    $"최종 교환비: {ResourceName(resource)} {quote.RequiredResourceAmount} → 금 {quote.NetGoldPayment}" +
                    $" (기본 {quote.RequiredResourceAmount - quote.ShippingResourceCost} + 거리 {quote.ShippingResourceCost})" +
                    (quote.IsAvailable ? string.Empty : $" | 불가: {CommerceTradeFailureName(quote.Failure)}") + "\n" +
                    $"보유 {quote.AvailableResourceAmount} | {FormatTradeRoute(quote.Route)}");
                GUI.enabled = !state.IsGameOver && !IsManeuverRecommandPhase() && (quote.IsAvailable || reserved);
                if (GUI.Button(new Rect(x + 270f, rowY + 5f, 58f, 32f), reserved ? "취소" : "예약"))
                {
                    if (reserved) CancelNeutralTrade(city, resource);
                    else ReserveNeutralTrade(city, resource);
                }
                GUI.enabled = !state.IsGameOver;
                var repeatKey = RepeatingNeutralTradeKey(activePlayerId, city.Id, resource);
                if (GUI.Button(new Rect(x + 332f, rowY + 5f, 58f, 32f),
                        repeatingNeutralTrades.Contains(repeatKey) ? "자동:켜짐" : "자동:꺼짐"))
                    ToggleNeutralTradeRepeat(city, resource);
                GUI.enabled = true;
            }
        }

        private void DrawNeutralTradePanel()
        {
            var rect = ResearchPanelRect();
            GUI.Box(rect, string.Empty);
            GUI.Label(new Rect(rect.x + 14f, rect.y + 12f, 300f, 24f), "전체 중립도시 교역 현황");
            if (GUI.Button(new Rect(rect.x + 360f, rect.y + 10f, 38f, 26f), "X"))
            { showNeutralTradePanel = false; return; }
            var home = FindActiveHomeCity();
            if (home == null) return;
            var neutral = FindPlayer(PlayerSlot.Neutral);
            var cities = state.Cities.Where(item => neutral != null && item.OwnerId == neutral.Id)
                .OrderBy(item => item.Id).ToList();
            var view = new Rect(0f, 0f, 374f, Mathf.Max(520f, cities.Count * 126f));
            neutralTradeScroll = GUI.BeginScrollView(new Rect(rect.x + 12f, rect.y + 44f, 390f, 540f),
                neutralTradeScroll, view);
            for (var index = 0; index < cities.Count; index++)
            {
                var city = cities[index];
                var rowY = index * 126f;
                GUI.Label(new Rect(4f, rowY, 366f, 20f),
                    $"{city.Name} | {SpecializationName(city.NeutralSpecialization)} | 우호도 {NeutralCityRules.Favor(city, activePlayerId)}");
                GUI.Label(new Rect(4f, rowY + 21f, 366f, 38f), NeutralTradeOverview(city, home));
                DrawNeutralTradeOverviewActions(city, home, rowY + 62f);
            }
            GUI.EndScrollView();
        }

        private void DrawNeutralTradeOverviewActions(CityState city, CityState home, float y)
        {
            if (city.NeutralSpecialization == NeutralCitySpecialization.Science ||
                city.NeutralSpecialization == NeutralCitySpecialization.Culture)
            {
                var resource = city.NeutralSpecialization == NeutralCitySpecialization.Science
                    ? TileResourceType.Science : TileResourceType.Culture;
                DrawNeutralTradeOverviewButtons(city, resource, y);
                return;
            }
            if (city.NeutralSpecialization == NeutralCitySpecialization.Commerce)
            {
                var resources = new[] { TileResourceType.Food, TileResourceType.Science, TileResourceType.Culture };
                for (var index = 0; index < resources.Length; index++)
                {
                    GUI.Label(new Rect(4f, y + index * 21f, 54f, 20f), ResourceName(resources[index]));
                    DrawNeutralTradeOverviewButtons(city, resources[index], y + index * 21f, 58f);
                }
                return;
            }
            if (city.NeutralSpecialization != NeutralCitySpecialization.Military) return;
            var quote = NeutralLevyResolver.Quote(state, activePlayerId, home.Id, city.Id);
            var reserved = plannedLevyBids.ContainsKey(LevyBidKey(activePlayerId, city.Id));
            GUI.enabled = !state.IsGameOver && !IsManeuverRecommandPhase() && (quote.IsAvailable || reserved);
            if (GUI.Button(new Rect(4f, y, 366f, 25f), reserved ? "징병 예약 취소" : "기본가로 징병 예약"))
            {
                if (reserved) CancelLevyBid(city);
                else ReserveLevyBid(city, quote.BasePrice);
            }
            GUI.enabled = true;
        }

        private void DrawNeutralTradeOverviewButtons(CityState city, TileResourceType resource,
            float y, float xOffset = 4f)
        {
            var key = NeutralTradeKey(city.Id, resource);
            var reserved = plannedNeutralTrades.ContainsKey(key);
            var repeated = repeatingNeutralTrades.Contains(
                RepeatingNeutralTradeKey(activePlayerId, city.Id, resource));
            var home = FindActiveHomeCity();
            var available = false;
            if (home != null)
            {
                available = city.NeutralSpecialization == NeutralCitySpecialization.Commerce
                    ? CommerceTradeQuoteResolver.Quote(state, activePlayerId, home.Id, city.Id, resource).IsAvailable
                    : NeutralTradeQuoteResolver.Quote(state, activePlayerId, home.Id, city.Id).IsAvailable;
            }
            GUI.enabled = !state.IsGameOver && !IsManeuverRecommandPhase() && (available || reserved);
            if (GUI.Button(new Rect(xOffset, y, 142f, 20f), reserved ? "거래 예약 취소" : "거래 예약"))
            {
                if (reserved) CancelNeutralTrade(city, resource);
                else ReserveNeutralTrade(city, resource);
            }
            GUI.enabled = !state.IsGameOver;
            if (GUI.Button(new Rect(xOffset + 148f, y, 164f, 20f),
                    repeated ? "다음 턴 자동예약: 켜짐" : "다음 턴 자동예약: 꺼짐"))
                ToggleNeutralTradeRepeat(city, resource);
            GUI.enabled = true;
        }

        private string NeutralTradeOverview(CityState city, CityState home)
        {
            if (city.NeutralSpecialization == NeutralCitySpecialization.Science ||
                city.NeutralSpecialization == NeutralCitySpecialization.Culture)
            {
                var quote = NeutralTradeQuoteResolver.Quote(state, activePlayerId, home.Id, city.Id);
                var resource = city.NeutralSpecialization == NeutralCitySpecialization.Science
                    ? TileResourceType.Science : TileResourceType.Culture;
                var reserved = plannedNeutralTrades.ContainsKey(NeutralTradeKey(city.Id, resource));
                var repeated = repeatingNeutralTrades.Contains(RepeatingNeutralTradeKey(activePlayerId, city.Id, resource));
                return $"금 {quote.TotalGoldCost} → {ResourceName(resource)} {quote.ResourceAmount}" +
                       (quote.IsAvailable ? " | 가능" : $" | 불가: {NeutralTradeFailureName(quote.Failure)}") +
                       $" | 예약 {(reserved ? "O" : "X")} | 반복 {(repeated ? (quote.IsAvailable ? "O" : "O(보류)") : "X")}";
            }
            if (city.NeutralSpecialization == NeutralCitySpecialization.Commerce)
            {
                var parts = new List<string>();
                var resources = new[] { TileResourceType.Food, TileResourceType.Science, TileResourceType.Culture };
                for (var index = 0; index < resources.Length; index++)
                {
                    var resource = resources[index];
                    var quote = CommerceTradeQuoteResolver.Quote(state, activePlayerId, home.Id, city.Id, resource);
                    var reserved = plannedNeutralTrades.ContainsKey(NeutralTradeKey(city.Id, resource));
                    var repeated = repeatingNeutralTrades.Contains(
                        RepeatingNeutralTradeKey(activePlayerId, city.Id, resource));
                    parts.Add($"{ResourceName(resource)}{quote.RequiredResourceAmount}→금{quote.NetGoldPayment}" +
                              (quote.IsAvailable ? string.Empty : "[불가]") +
                              $"{(reserved ? "[예약]" : "")}{(repeated ? (quote.IsAvailable ? "[반복]" : "[반복보류]") : "")}");
                }
                return string.Join(" | ", parts);
            }
            if (city.NeutralSpecialization == NeutralCitySpecialization.Military)
            {
                var quote = NeutralLevyResolver.Quote(state, activePlayerId, home.Id, city.Id);
                return quote.IsAvailable
                    ? $"징병 가능: {quote.UnitIds.Count}부대, 기본 금 {quote.BasePrice} | 예약 {(plannedLevyBids.ContainsKey(LevyBidKey(activePlayerId, city.Id)) ? "O" : "X")}"
                    : $"징병 불가: {LevyFailureName(quote.Failure)}";
            }
            return "이용 가능한 교역 없음";
        }

        private void DrawLevyBid(float x, float y, CityState city, CityState home)
        {
            var quote = NeutralLevyResolver.Quote(state, activePlayerId, home.Id, city.Id);
            GUI.Label(new Rect(x + 14f, y, 380f, 22f), quote.IsAvailable
                ? $"징병 {quote.UnitIds.Count}부대 | 병력 가치 {quote.FullUnitValue} | 기본가 금 {quote.BasePrice}"
                : $"징병 불가: {LevyFailureName(quote.Failure)}");
            GUI.Label(new Rect(x + 14f, y + 23f, 380f, 22f), FormatTradeRoute(quote.Route));
            var affordableExtra = quote.IsAvailable ? Mathf.Max(0, home.Gold - quote.BasePrice) : 0;
            levyExtraBid = Mathf.Clamp(levyExtraBid, 0, affordableExtra);
            GUI.Label(new Rect(x + 14f, y + 52f, 155f, 28f),
                $"경매 비교 추가금: {levyExtraBid} | 실제 지불 총액 {quote.BasePrice + levyExtraBid}");
            var bidKey = LevyBidKey(activePlayerId, city.Id);
            GUI.enabled = quote.IsAvailable && !plannedLevyBids.ContainsKey(bidKey);
            if (GUI.Button(new Rect(x + 174f, y + 49f, 46f, 30f), "-")) levyExtraBid = Mathf.Max(0, levyExtraBid - 1);
            if (GUI.Button(new Rect(x + 224f, y + 49f, 46f, 30f), "+")) levyExtraBid = Mathf.Min(affordableExtra, levyExtraBid + 1);
            if (GUI.Button(new Rect(x + 274f, y + 49f, 54f, 30f), "+5")) levyExtraBid = Mathf.Min(affordableExtra, levyExtraBid + 5);
            if (GUI.Button(new Rect(x + 332f, y + 49f, 58f, 30f), "최대")) levyExtraBid = affordableExtra;
            var reserved = plannedLevyBids.TryGetValue(bidKey, out var bid);
            GUI.enabled = !state.IsGameOver && !IsManeuverRecommandPhase() && (quote.IsAvailable || reserved);
            if (GUI.Button(new Rect(x + 14f, y + 86f, 376f, 30f), reserved
                    ? $"입찰 취소(금 {quote.BasePrice + bid.PrimaryValue})"
                    : "징병 입찰 예약"))
            {
                if (reserved) CancelLevyBid(city);
                else ReserveLevyBid(city, quote.BasePrice);
            }
            GUI.enabled = true;
            var levy = state.Levies.Find(item => item.MilitaryCityId == city.Id);
            if (levy != null)
                GUI.Label(new Rect(x + 14f, y + 122f, 380f, 42f),
                    $"징병 진행 중: {PlayerName(FindPlayer(levy.PlayerId).Slot)} | {levy.Units.Count}부대 | " +
                    $"{levy.EndTurnExclusive}턴에 반환");
        }

        private string FormatTradeRoute(TradeRouteResult route)
        {
            if (route == null) return "경로: 계산 안 됨";
            if (!route.IsReachable)
                return route.BlockedCityIds.Count == 0
                    ? "경로: 도달 불가"
                    : $"경로: {string.Join(", ", route.BlockedCityIds)}에 의해 차단";
            var names = route.CityPath.Select(id => state.Cities.Find(city => city.Id == id)?.Name ?? id.ToString());
            return $"경로 {string.Join(" → ", names)} | 거리 {route.Distance} (추가 거리 {route.AdditionalDistance})";
        }

        private void DrawDistrictActions(float x, float y, DistrictState district)
        {
            var city = state.Cities.Find(item => item.Id == district.CityId);
            GUI.Label(new Rect(x + 14f, y + 40f, 380f, 22f),
                $"{DistrictName(district.Type)} | 도시 {city?.Name} | 배치 시민 {district.AssignedCitizens}");
            if (district.RemainingConstructionTurns > 0)
            {
                GUI.Label(new Rect(x + 14f, y + 66f, 380f, 22f),
                    $"건설 중: {district.RemainingConstructionTurns}턴 남음");
                GUI.enabled = city != null && city.OwnerId == activePlayerId && district.ControllerId == activePlayerId;
                if (GUI.Button(new Rect(x + 14f, y + 94f, 376f, 28f), "진행 중인 건설 취소"))
                {
                    if (DistrictConstructionResolver.TryCancel(state, activePlayerId, district.Id))
                    {
                        statusMessage = "건설을 취소하고 배치 시민과 건설비를 반환했습니다.";
                        selectedTileId = default;
                        ShowCities(new[] { district.CityId });
                    }
                }
                GUI.enabled = true;
                return;
            }
            var ownedAndControlled = city != null && city.OwnerId == activePlayerId &&
                                     district.ControllerId == city.OwnerId;
            if (city == null || district.ControllerId != city.OwnerId)
            {
                GUI.Label(new Rect(x + 14f, y + 66f, 380f, 42f),
                    "상태: 점령당함 — 원래 도시는 이 지구를 사용할 수 없습니다.");
                return;
            }
            var legacyRecapturedPillage = !district.IsOperational && district.AssignedCitizens > 0 &&
                                           !district.IsMaintenanceSuspended;
            if (district.IsPillaged || legacyRecapturedPillage)
            {
                if (district.RemainingRepairTurns > 0)
                {
                    GUI.Label(new Rect(x + 14f, y + 66f, 380f, 42f),
                        $"수리 중: {district.RemainingRepairTurns}턴 남음");
                    return;
                }
                GUI.Label(new Rect(x + 14f, y + 66f, 380f, 22f), "상태: 약탈당함");
                GUI.enabled = ownedAndControlled && !state.IsGameOver;
                if (plannedRepairs.ContainsKey(district.Id))
                {
                    GUI.Label(new Rect(x + 14f, y + 94f, 180f, 30f), "수리 예약됨");
                    if (GUI.Button(new Rect(x + 210f, y + 94f, 180f, 30f), "수리 예약 취소"))
                        CancelDistrictRepair(district);
                }
                else if (GUI.Button(new Rect(x + 14f, y + 94f, 376f, 30f),
                    $"수리({DistrictConstructionResolver.RepairTurns(district.Type)}턴)"))
                {
                    ReserveDistrictRepair(district);
                }
                GUI.enabled = true;
                return;
            }
            GUI.Label(new Rect(x + 14f, y + 66f, 380f, 22f),
                district.IsOperational ? "상태: 가동 중" : "상태: 비활성");
            if (district.Type != DistrictType.Government && district.IsOperational)
            {
                var resource = state.Tiles.Find(item => item.Id == district.TileId)?.ResourceType ??
                               TileResourceType.None;
                GUI.Label(new Rect(x + 14f, y + 88f, 380f, 22f),
                    $"자원 {ResourceName(resource)}: +{CityEconomyResolver.ResourceBonusForDistrict(state, district)} | " +
                    $"같은 지구 인접 보너스: +{CityEconomyResolver.AdjacencyBonusForDistrict(state, district)}");
            }
            if (district.Type == DistrictType.Government)
            {
                var free = city == null ? 0 : DistrictConstructionResolver.CountFreeCitizens(state, city) -
                                          CountPlannedDistricts(city.Id);
                GUI.Label(new Rect(x + 14f, y + 94f, 380f, 22f),
                    $"정부청사 행동: 도시 관리 | 미배정 시민 {free}");
                GUI.Label(new Rect(x + 14f, y + 120f, 380f, 42f),
                    "미개발 초록색 타일을 선택해 새 지구를 건설하세요.");
                return;
            }
            if (district.Type == DistrictType.NuclearFacility)
            {
                var project = state.NuclearProjects.Find(item => item.DistrictId == district.Id);
                if (project != null)
                {
                    GUI.Label(new Rect(x + 14f, y + 112f, 380f, 42f),
                        project.IsCompleted ? "핵무기 프로젝트: 완료" :
                        $"핵무기 프로젝트: {project.RemainingTurns}턴 남음");
                    return;
                }
                if (plannedNuclearProjects.ContainsKey(district.Id))
                {
                    GUI.Label(new Rect(x + 14f, y + 112f, 180f, 30f), "핵 프로젝트 예약됨");
                    if (GUI.Button(new Rect(x + 210f, y + 112f, 180f, 30f), "프로젝트 예약 취소"))
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
                    "핵무기 프로젝트 시작 — 금 10/5턴"))
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
                        "기계화 농업: 시민 1명으로 생산량 150%. ");
                    return;
                }
                if (!irrigation)
                {
                    GUI.Label(new Rect(x + 14f, y + 112f, 376f, 42f),
                        "관개를 연구하면 두 번째 시민을 배치해 생산량을 150%로 높일 수 있습니다.");
                    return;
                }
                if (plannedCitizenAssignments.TryGetValue(district.Id, out var citizenPlan))
                {
                    GUI.Label(new Rect(x + 14f, y + 112f, 190f, 30f),
                        $"시민 배치 예약: {citizenPlan.PrimaryValue}명");
                    if (GUI.Button(new Rect(x + 210f, y + 112f, 180f, 30f), "시민 배치 취소"))
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
                    desired == 2 ? "두 번째 시민 배치 — 생산량 150%" : "두 번째 시민 회수"))
                    ReserveAgricultureCitizens(district, desired);
                GUI.enabled = true;
                return;
            }
            if (district.Type != DistrictType.Military)
            {
                GUI.Label(new Rect(x + 14f, y + 112f, 380f, 42f),
                    "이 지구는 매 턴 산출을 자동 생산합니다.");
                return;
            }

            var training = state.UnitTrainings.Find(item => item.DistrictId == district.Id);
            if (training != null)
            {
                GUI.Label(new Rect(x + 14f, y + 112f, 380f, 42f),
                    training.IsAwaitingDeployment
                        ? $"{UnitName(training.Type)} - 남은 턴 0턴(공간 없음)"
                        : $"{UnitName(training.Type)} 훈련: {training.RemainingTurns}턴 남음");
                GUI.enabled = training.OwnerId == activePlayerId;
                if (GUI.Button(new Rect(x + 14f, y + 140f, 376f, 28f),
                    $"훈련 취소 — 금 {UnitRules.TrainingGold(training.Type)} 반환"))
                {
                    if (UnitTrainingResolver.TryCancel(state, activePlayerId, training.Id))
                    {
                        statusMessage = $"{UnitName(training.Type)} 훈련을 취소하고 비용을 전액 반환했습니다.";
                        ShowCities(new[] { district.CityId });
                    }
                }
                GUI.enabled = true;
                return;
            }
            if (plannedTrainings.TryGetValue(district.Id, out var plannedTraining))
            {
                GUI.Label(new Rect(x + 14f, y + 112f, 190f, 30f),
                    $"훈련 예약: {UnitName((UnitType)plannedTraining.PrimaryValue)}");
                if (GUI.Button(new Rect(x + 210f, y + 112f, 180f, 30f), "훈련 예약 취소"))
                    CancelTraining(district);
                return;
            }
            GUI.Label(new Rect(x + 14f, y + 112f, 380f, 22f), "군사지구 행동: 병력 훈련");
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
                    $"{UnitName(type)} | 금 {UnitRules.TrainingGold(type)}/{UnitRules.TrainingTurns(type)}턴"))
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
            GUI.Label(new Rect(x + 14f, y, 376f, 22f), "정부청사 방어시설:");
            if (district.RemainingConstructionTurns > 0)
            {
                GUI.Label(new Rect(x + 14f, y + 24f, 376f, 22f),
                    "지구 건설이 완료된 뒤 사용할 수 있습니다.");
                return;
            }
            if (facility != null && facility.RemainingConstructionTurns > 0)
            {
                GUI.Label(new Rect(x + 14f, y + 24f, 376f, 42f),
                    $"{DefenseName(facility.BuildingType)} 건설 중: {facility.RemainingConstructionTurns}턴 | " +
                    $"현재 보너스 {DefenseFacilityResolver.EffectiveBonus(facility)}%");
                return;
            }
            if (plannedDefenseConstructions.TryGetValue(district.TileId, out var planned))
            {
                var plannedType = (DefenseFacilityType)planned.PrimaryValue;
                GUI.Label(new Rect(x + 14f, y + 24f, 190f, 30f),
                    $"{DefenseName(plannedType)} 건설 예약됨");
                if (GUI.Button(new Rect(x + 210f, y + 24f, 180f, 30f), "방어시설 건설 취소"))
                    CancelDefenseConstruction(district.TileId);
                return;
            }

            var type = facility == null ? DefenseFacilityType.None : facility.Type;
            var bonus = facility == null ? 0 : DefenseFacilityResolver.EffectiveBonus(facility);
            var status = type == DefenseFacilityType.ModernDefense
                ? facility.IsModernDefenseActive
                    ? "가동 중"
                    : facility.RemainingReactivationTurns > 0
                        ? $"재활성화 중({facility.RemainingReactivationTurns}턴)"
                        : "비활성"
                : "가동 중";
            GUI.Label(new Rect(x + 14f, y + 24f, 376f, 22f),
                $"{DefenseName(type)} | 수비 +{bonus}%" + (type == DefenseFacilityType.None ? string.Empty : $" | {status}"));

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
                    (unlocked ? $"{DefenseName(next)} 건설" : $"{DefenseName(next)}(연구 필요)") +
                    $" — 금 {DefenseFacilityResolver.GoldCost(next)}/" +
                    $"{DefenseFacilityResolver.ConstructionTurns(next)}턴"))
                    ReserveDefenseConstruction(district, next);
                GUI.enabled = true;
                return;
            }

            if (plannedDefenseActions.ContainsKey(facility.Id))
            {
                GUI.Label(new Rect(x + 14f, y + 52f, 190f, 30f), "제어 변경 예약됨");
                if (GUI.Button(new Rect(x + 210f, y + 52f, 180f, 30f), "변경 예약 취소"))
                    CancelModernDefenseAction(facility);
                return;
            }
            GUI.enabled = ownedAndControlled;
            if (facility.IsModernDefenseActive)
            {
                if (GUI.Button(new Rect(x + 14f, y + 52f, 376f, 30f),
                    "비활성화(해자 +50%로 전환)"))
                    ReserveModernDefenseAction(facility, false);
            }
            else if (facility.RemainingReactivationTurns <= 0)
            {
                if (GUI.Button(new Rect(x + 14f, y + 52f, 376f, 30f),
                    "재활성화 — 2턴/턴당 금 2"))
                    ReserveModernDefenseAction(facility, true);
            }
            GUI.enabled = true;
        }

        private void DrawUnitsOnSelectedTile(float x, float y)
        {
            var units = state.Units.FindAll(item => item.TileId == selectedTileId);
            units.Sort((left, right) => left.Id.CompareTo(right.Id));
            if (units.Count == 0) return;
            GUI.Label(new Rect(x + 14f, y, 380f, 22f), "타일 위 병력(이동 명령을 내릴 병력 선택):");
            var friendly = units.Where(item => item.OwnerId == activePlayerId).ToList();
            GUI.enabled = friendly.Count > 1 && !state.IsGameOver;
            if (GUI.Button(new Rect(x + 14f, y + 28f, 376f, 28f),
                    selectedUnitGroup.Count > 1 ? $"복수 선택됨 ({selectedUnitGroup.Count}부대)" : "이 타일의 내 병력 모두 선택"))
            {
                selectedUnitGroup.Clear();
                for (var memberIndex = 0; memberIndex < friendly.Count; memberIndex++)
                    selectedUnitGroup.Add(friendly[memberIndex].Id);
                selectedUnitId = friendly[0].Id;
                statusMessage = $"같은 타일의 병력 {friendly.Count}부대를 선택했습니다. 목적지를 우클릭하세요.";
            }
            GUI.enabled = true;
            for (var index = 0; index < units.Count && index < 4; index++)
            {
                var unit = units[index];
                GUI.enabled = unit.OwnerId == activePlayerId && !state.IsGameOver;
                if (GUI.Button(new Rect(x + 14f, y + 62f + (index * 34f), 376f, 28f),
                    $"{UnitName(unit.Type)} | 체력 {unit.HitPoints}/{UnitRules.MaximumHitPoints(unit.Type)} | 군량 {unit.CarriedFood}/{UnitRules.FoodCapacity(state, unit)} | " +
                    (unit.ManeuverRecommandTurn == state.TurnNumber
                        ? $"재명령 이동력 {unit.RemainingMovement}"
                        : $"이동력 {unit.RemainingMovement}")))
                {
                    SelectUnit(unit.Id, unit.TileId);
                }
                GUI.enabled = true;
            }

            var selected = units.Find(item => item.Id == selectedUnitId && item.OwnerId == activePlayerId);
            if (selected == null) return;
            var foodY = y + 66f + (Mathf.Min(4, units.Count) * 34f);
            var city = FindOwnedCityForUnit(selected);
            plannedFoodAdjustments.TryGetValue(selected.Id, out var adjustmentCommand);
            var adjustment = adjustmentCommand == null ? 0 : adjustmentCommand.PrimaryValue;
            var projected = selected.CarriedFood + adjustment;
            var capacity = UnitRules.FoodCapacity(state, selected);
            GUI.Label(new Rect(x + 14f, foodY, 376f, 22f),
                city == null
                    ? $"군량 {selected.CarriedFood}/{capacity} | 통제 중인 본토에서 조절 가능"
                    : $"군량 {selected.CarriedFood}/{capacity} → {projected}/{capacity} | 도시 비축 {city.StoredFood}");
            GUI.enabled = city != null && !state.IsGameOver;
            if (GUI.Button(new Rect(x + 14f, foodY + 26f, 86f, 28f), "1 반환"))
                AdjustSelectedUnitFood(selected, -1);
            if (GUI.Button(new Rect(x + 106f, foodY + 26f, 86f, 28f), "전부 반환"))
                AdjustSelectedUnitFood(selected, -capacity);
            if (GUI.Button(new Rect(x + 210f, foodY + 26f, 86f, 28f), "1 적재"))
                AdjustSelectedUnitFood(selected, 1);
            if (GUI.Button(new Rect(x + 302f, foodY + 26f, 88f, 28f), "최대 적재"))
                AdjustSelectedUnitFood(selected, capacity);
            GUI.enabled = true;
            var promotion = HighestUnlockedPromotion(selected.Type, FindPlayer(activePlayerId));
            var promotionOffset = 0f;
            if (promotion.HasValue)
            {
                var player = FindPlayer(activePlayerId);
                var unlocked = player != null && player.UnlockedUnitTypes.Contains(promotion.Value);
                var cost = UnitRules.TrainingGold(promotion.Value) - UnitRules.TrainingGold(selected.Type);
                GUI.enabled = unlocked && PlannedMovementForTurn(selected) > 0 && city != null && city.Gold >= cost;
                if (GUI.Button(new Rect(x + 14f, foodY + 62f, 376f, 28f),
                    unlocked ? $"{UnitName(promotion.Value)} 승급 — 금 {cost}" : $"{UnitName(promotion.Value)}(연구 필요)"))
                    ReservePromotion(selected, promotion.Value);
                GUI.enabled = true;
                promotionOffset = 34f;
            }
            var disbandY = foodY + 62f + promotionOffset;
            var selectedTile = state.Tiles.Find(item => item.Id == selected.TileId);
            var canPickup = selectedTile != null && selectedTile.GroundFood > 0 &&
                            selected.CarriedFood < capacity;
            GUI.enabled = !state.IsGameOver && canPickup && !plannedGroundFoodPickups.ContainsKey(selected.Id);
            if (GUI.Button(new Rect(x + 14f, disbandY, 376f, 28f),
                    canPickup ? $"현장 군량 습득 — 최대 {Mathf.Min(selectedTile.GroundFood, capacity - selected.CarriedFood)}" :
                        "현장 군량 습득 불가"))
                ReserveGroundFoodPickup(selected, selectedTile);
            GUI.enabled = true;
            disbandY += 34f;
            GUI.enabled = !state.IsGameOver;
            if (GUI.Button(new Rect(x + 14f, disbandY, 376f, 28f), "병력 해체 — 적재 군량 반환"))
            {
                var home = state.Cities.Find(item => item.Id == selected.HomeCityId && item.OwnerId == activePlayerId);
                if (home != null) home.StoredFood += selected.CarriedFood;
                state.Units.Remove(selected);
                NeutralLevyResolver.ReconcileDestroyedUnits(state);
                routePlans.Remove(selected.Id);
                plannedMoves.Remove(selected.Id);
                selectedUnitId = default;
                selectedUnitGroup.Clear();
                statusMessage = "병력을 해체하고 적재 군량을 출신 도시에 반환했습니다.";
                ShowCities(new[] { state.Cities[focusedCityIndex].Id });
                return;
            }
            GUI.enabled = true;
            DrawFoodTransferActions(x, disbandY + 34f, selected, units);
        }

        private static UnitType? HighestUnlockedPromotion(UnitType type, PlayerState player)
        {
            if (player == null) return null;
            if (type == UnitType.Supply)
                return player.UnlockedUnitTypes.Contains(UnitType.MotorizedSupply)
                    ? UnitType.MotorizedSupply : (UnitType?)null;
            if (type == UnitType.MotorizedSupply) return null;
            if (player.UnlockedUnitTypes.Contains(UnitType.MechanizedInfantry) && type < UnitType.MechanizedInfantry)
                return UnitType.MechanizedInfantry;
            if (player.UnlockedUnitTypes.Contains(UnitType.GunpowderInfantry) && type < UnitType.GunpowderInfantry)
                return UnitType.GunpowderInfantry;
            if (player.UnlockedUnitTypes.Contains(UnitType.IronInfantry) && type < UnitType.IronInfantry)
                return UnitType.IronInfantry;
            return null;
        }

        private void DrawFoodTransferActions(float x, float y, UnitState selected, List<UnitState> units)
        {
            var partners = units.FindAll(item =>
                item.Id != selected.Id && item.OwnerId == activePlayerId);
            if (partners.Count == 0) return;
            GUI.Label(new Rect(x + 14f, y, 376f, 22f), "같은 타일 병력 간 군량 교환:");
            for (var i = 0; i < partners.Count && i < 2; i++)
            {
                var partner = partners[i];
                var giveKey = FoodTransferKey(selected.Id, partner.Id);
                var takeKey = FoodTransferKey(partner.Id, selected.Id);
                plannedFoodTransfers.TryGetValue(giveKey, out var give);
                plannedFoodTransfers.TryGetValue(takeKey, out var take);
                var rowY = y + 24f + (i * 58f);
                GUI.Label(new Rect(x + 14f, rowY, 376f, 22f),
                    $"{UnitName(partner.Type)} {partner.Id} | 군량 {partner.CarriedFood}/{UnitRules.FoodCapacity(state, partner)}" +
                    (give != null ? $" | 주기 {give.PrimaryValue}" : string.Empty) +
                    (take != null ? $" | 받기 {take.PrimaryValue}" : string.Empty));
                GUI.enabled = !state.IsGameOver && selected.CarriedFood > 0 &&
                              partner.CarriedFood < UnitRules.FoodCapacity(state, partner);
                if (GUI.Button(new Rect(x + 14f, rowY + 24f, 82f, 28f), "1 주기"))
                    AdjustFoodTransfer(selected, partner, 1);
                if (GUI.Button(new Rect(x + 102f, rowY + 24f, 82f, 28f), "최대 주기"))
                    AdjustFoodTransfer(selected, partner, UnitRules.FoodCapacity(state, partner));
                GUI.enabled = !state.IsGameOver && partner.CarriedFood > 0 &&
                              selected.CarriedFood < UnitRules.FoodCapacity(state, selected);
                if (GUI.Button(new Rect(x + 210f, rowY + 24f, 82f, 28f), "1 받기"))
                    AdjustFoodTransfer(partner, selected, 1);
                if (GUI.Button(new Rect(x + 298f, rowY + 24f, 92f, 28f), "최대 받기"))
                    AdjustFoodTransfer(partner, selected, UnitRules.FoodCapacity(state, selected));
                GUI.enabled = true;
            }
        }

        private void DrawDistrictBuildButton(float x, float y, DistrictType type)
        {
            var player = FindPlayer(activePlayerId);
            var unlocked = player != null && (!player.ResearchUnlocksEnabled ||
                player.UnlockedDistrictTypes.Contains(type));
            var city = FindActiveOwnedCityForTile(selectedTileId);
            var nuclearAlreadyOwned = type == DistrictType.NuclearFacility && city != null &&
                (state.Districts.Any(item => item.CityId == city.Id && item.Type == type) ||
                 plannedDistricts.Values.Any(item => item.PlayerId == activePlayerId &&
                     item.SubjectId == city.Id && (DistrictType)item.PrimaryValue == type));
            var previous = GUI.enabled;
            GUI.enabled = previous && unlocked && !nuclearAlreadyOwned;
            if (GUI.Button(new Rect(x, y, 180f, 30f),
                nuclearAlreadyOwned ? "핵시설(하나만 보유 가능)" :
                unlocked ? $"{DistrictName(type)} 건설" : $"{DistrictName(type)} ({RequiredResearch(type)})"))
                ReserveDistrictConstruction(type);
            GUI.enabled = previous;
        }

        private static string RequiredResearch(DistrictType type)
        {
            switch (type)
            {
                case DistrictType.Science: return "학교 연구 필요";
                case DistrictType.Culture: return "예술 연구 필요";
                case DistrictType.NuclearFacility: return "핵분열 연구 필요";
                default: return "잠김";
            }
        }

        private static void DrawYieldRow(float x, float y, string label, YieldBreakdown value)
        {
            GUI.Label(new Rect(x, y, 360f, 20f),
                $"{label} {value.Total} = 정부청사 {value.Government} + 지구 {value.DistrictBase} " +
                $"+ 자원 {value.ResourceBonus} + 인접 {value.AdjacencyBonus} " +
                $"+ 연구 {value.ResearchBonus} + 시민배치 {value.StaffingBonus} " +
                $"+ 배율 {value.MultiplierBonus}");
        }

        private static string PlayerName(PlayerSlot slot) => slot == PlayerSlot.PlayerOne ? "플레이어 1" :
            slot == PlayerSlot.PlayerTwo ? "플레이어 2" : slot == PlayerSlot.Neutral ? "중립" : "없음";

        private static string VictoryName(VictoryType type) => type == VictoryType.Science ? "과학승리" :
            type == VictoryType.Culture ? "문화승리" : type == VictoryType.Conquest ? "정복승리" :
            type == VictoryType.Draw ? "무승부" : "승리 없음";

        private static string DistrictName(DistrictType type) => type == DistrictType.Government ? "정부청사" :
            type == DistrictType.Agriculture ? "농업지구" : type == DistrictType.Commerce ? "상업지구" :
            type == DistrictType.Science ? "과학지구" : type == DistrictType.Culture ? "문화지구" :
            type == DistrictType.Military ? "군사지구" : "핵시설";

        private static string UnitName(UnitType type) => type == UnitType.Militia ? "민병대" :
            type == UnitType.IronInfantry ? "철제 보병" : type == UnitType.GunpowderInfantry ? "화약 보병" :
            type == UnitType.MechanizedInfantry ? "기계화보병" : type == UnitType.Supply ? "보급병" : "차량화 보급대";

        private static string ResourceName(TileResourceType type) => type == TileResourceType.Food ? "식량" :
            type == TileResourceType.Commerce ? "상업" : type == TileResourceType.Science ? "과학" :
            type == TileResourceType.Culture ? "문화" : "없음";

        private static string DefenseName(DefenseFacilityType type) => type == DefenseFacilityType.Wall ? "성벽" :
            type == DefenseFacilityType.Moat ? "해자" : type == DefenseFacilityType.ModernDefense ? "현대 방어체계" : "방어시설 없음";

        private static string SpecializationName(NeutralCitySpecialization type) =>
            type == NeutralCitySpecialization.Military ? "군사 특성화" :
            type == NeutralCitySpecialization.Science ? "과학 특성화" :
            type == NeutralCitySpecialization.Culture ? "문화 특성화" :
            type == NeutralCitySpecialization.Commerce ? "상업 특성화" : "특성화 없음";

        private static string DevelopmentStageName(NeutralDevelopmentStage stage) =>
            stage == NeutralDevelopmentStage.Early ? "초반" :
            stage == NeutralDevelopmentStage.Middle ? "중반" : "후반";

        private static string CommandResultName(CommandMutationResult result) =>
            result == CommandMutationResult.SessionClosed ? "명령 단계 종료" :
            result == CommandMutationResult.PlayerAlreadyConfirmed ? "이미 턴 확정" :
            result == CommandMutationResult.InvalidCommand ? "잘못된 명령" :
            result == CommandMutationResult.CommandNotFound ? "명령을 찾을 수 없음" : "승인";

        private static string MovementStopReasonName(MovementStopReason reason) =>
            reason == MovementStopReason.UnknownUnit ? "병력을 찾을 수 없음" :
            reason == MovementStopReason.NotUnitOwner ? "병력 소유권 없음" :
            reason == MovementStopReason.EmptyPath ? "빈 경로" :
            reason == MovementStopReason.NonAdjacentTile ? "인접하지 않은 타일" :
            reason == MovementStopReason.InsufficientMovement ? "이동력 부족" :
            reason == MovementStopReason.EnemyOccupied ? "적 병력 점유" :
            reason == MovementStopReason.TileCapacityReached ? "타일 병력 한도 초과" :
            reason == MovementStopReason.PriorityLost ? "이동 우선권 상실" :
            reason == MovementStopReason.SwapConflict ? "상호 위치 교환 충돌" :
            reason == MovementStopReason.TrainedThisTurn ? "이번 턴 생산된 병력" :
            reason == MovementStopReason.LevyOriginProtected ? "징병 원 도시 보호" : "이동 완료";

        private static string NeutralTradeFailureName(NeutralTradeQuoteFailure failure) =>
            failure == NeutralTradeQuoteFailure.InvalidParticipant ? "잘못된 거래 주체" :
            failure == NeutralTradeQuoteFailure.UnsupportedSpecialization ? "지원하지 않는 전문화" :
            failure == NeutralTradeQuoteFailure.TargetOccupied ? "목표 도시 점령 중" :
            failure == NeutralTradeQuoteFailure.RouteBlocked ? "교역로 차단" :
            failure == NeutralTradeQuoteFailure.InsufficientGold ? "금 부족" : "없음";

        private static string CommerceTradeFailureName(CommerceTradeQuoteFailure failure) =>
            failure == CommerceTradeQuoteFailure.InvalidParticipant ? "잘못된 거래 주체" :
            failure == CommerceTradeQuoteFailure.NotCommerceCity ? "상업도시가 아님" :
            failure == CommerceTradeQuoteFailure.InvalidResource ? "판매 불가능한 자원" :
            failure == CommerceTradeQuoteFailure.TargetOccupied ? "목표 도시 점령 중" :
            failure == CommerceTradeQuoteFailure.RouteBlocked ? "교역로 차단" :
            failure == CommerceTradeQuoteFailure.InsufficientFood ? "식량 부족" :
            failure == CommerceTradeQuoteFailure.NoProjectedProduction ? "예상 생산량 없음" : "없음";

        private static string LevyFailureName(LevyQuoteFailure failure) =>
            failure == LevyQuoteFailure.InvalidParticipant ? "잘못된 징병 주체" :
            failure == LevyQuoteFailure.NotMilitaryCity ? "군사도시가 아님" :
            failure == LevyQuoteFailure.Hostile ? "적대 관계" :
            failure == LevyQuoteFailure.CityOccupied ? "도시 점령 중" :
            failure == LevyQuoteFailure.AlreadyLevied ? "이미 징병 중" :
            failure == LevyQuoteFailure.RouteBlocked ? "경로 차단" :
            failure == LevyQuoteFailure.NoUnits ? "징병 가능한 병력 없음" :
            failure == LevyQuoteFailure.InsufficientGold ? "금 부족" : "없음";

        private static string ResearchName(ResearchType type)
        {
            switch (type)
            {
                case ResearchType.School: return "학교"; case ResearchType.IronWorking: return "철기";
                case ResearchType.Gunpowder: return "화약"; case ResearchType.Vehicles: return "차량";
                case ResearchType.NuclearFission: return "핵분열"; case ResearchType.Arts: return "예술";
                case ResearchType.Printing: return "인쇄술"; case ResearchType.MassMedia: return "대중매체";
                case ResearchType.Currency: return "화폐"; case ResearchType.Finance: return "금융";
                case ResearchType.EconomicAdministration: return "경제행정"; case ResearchType.Irrigation: return "관개";
                case ResearchType.Fertilizer: return "비료"; case ResearchType.MechanizedAgriculture: return "기계화 농업";
                case ResearchType.Salting: return "염지"; case ResearchType.Canning: return "통조림";
                case ResearchType.Fortification: return "축성"; case ResearchType.AdvancedFortification: return "요새화";
                case ResearchType.ModernDefense: return "현대 방어체계"; case ResearchType.SelfLearningAI: return "자가학습 AI";
                default: return "없음";
            }
        }

        private static string Signed(int value)
        {
            return value > 0 ? $"+{value}" : value.ToString();
        }
    }
}
