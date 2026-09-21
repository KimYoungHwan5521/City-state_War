using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using LittleCiv.Core;
using UnityEngine;

namespace LittleCiv.Runtime
{
    internal sealed class MatchAuditLogWriter
    {
        private readonly string latestPath;
        private readonly string archivePath;
        private bool enabled = true;

        public string LatestPath => latestPath;

        private MatchAuditLogWriter(string latestPath, string archivePath)
        {
            this.latestPath = latestPath;
            this.archivePath = archivePath;
        }

        public static MatchAuditLogWriter Start(GameState state, PlayerAiStrategy opponentStrategy)
        {
            var directory = Path.Combine(Application.persistentDataPath, "Logs");
            var startedAt = DateTime.Now;
            var suffix = startedAt.ToString("yyyyMMdd_HHmmss_fff");
            var writer = new MatchAuditLogWriter(
                Path.Combine(directory, "LatestMatch.log"),
                Path.Combine(directory, $"Match_{suffix}.log"));
            var header = new StringBuilder();
            header.AppendLine("=== Little Civilization 경기 감사 로그 ===");
            header.AppendLine($"시작시각={startedAt:yyyy-MM-dd HH:mm:ss.fff zzz}");
            header.AppendLine($"시드={state.MatchSeed} 상대AI={opponentStrategy} 초기턴={state.TurnNumber}");
            header.AppendLine($"보관파일={writer.archivePath}");
            header.AppendLine();
            header.Append(CaptureState(state, "경기 시작 상태"));
            writer.WriteInitial(header.ToString());
            return writer;
        }

        public void AppendTurn(string beforeState, TurnResolution resolution, GameState state)
        {
            if (!enabled || resolution == null || state == null) return;
            var text = new StringBuilder();
            text.AppendLine();
            text.AppendLine($"========== {resolution.ResolvedTurnNumber}턴 ==========");
            text.Append(beforeState);
            text.AppendLine("[확정 명령]");
            if (resolution.Commands.Count == 0) text.AppendLine("없음");
            else
                foreach (var command in resolution.Commands.OrderBy(item => item.PlayerId.Value)
                             .ThenBy(item => item.CommandId.Value))
                    text.AppendLine(FormatCommand(command));
            text.AppendLine("[판정 이벤트]");
            if (resolution.Events.Count == 0) text.AppendLine("없음");
            else
                foreach (var gameEvent in resolution.Events.OrderBy(item => item.Sequence))
                    text.AppendLine(FormatEvent(gameEvent));
            text.AppendLine("[기동 재명령 요청]");
            if (resolution.ManeuverRequests.Count == 0) text.AppendLine("없음");
            else
                foreach (var request in resolution.ManeuverRequests.OrderBy(item => item.PlayerId.Value)
                             .ThenBy(item => item.UnitId.Value))
                    text.AppendLine($"MANEUVER 플레이어={request.PlayerId} 병력={request.UnitId} " +
                                    $"마지막타일={request.LastValidTileId} 차단타일={request.BlockedTileId} " +
                                    $"남은이동={request.RemainingMovement} 사유={request.StopReason}");
            text.Append(CaptureState(state, "턴 처리 후 상태"));
            text.AppendLine($"상태해시={resolution.ResultStateHash} 게임종료={state.IsGameOver} " +
                            $"승리={state.Victory} 승자={state.WinnerId}");
            Append(text.ToString());
        }

        public static string CaptureState(GameState state, string label)
        {
            var text = new StringBuilder();
            text.AppendLine($"[{label}] 턴={state.TurnNumber}");
            foreach (var player in state.Players.OrderBy(item => item.Slot).ThenBy(item => item.Id.Value))
            {
                var completed = player.CompletedResearch == null || player.CompletedResearch.Count == 0
                    ? "없음"
                    : string.Join(",", player.CompletedResearch.OrderBy(item => (int)item));
                var researchProgress = player.CurrentResearch == ResearchType.None
                    ? 0
                    : ResearchResolver.Progress(player, player.CurrentResearch);
                text.AppendLine($"PLAYER 슬롯={player.Slot} ID={player.Id} AI={player.AiStrategy} " +
                                $"연구={player.CurrentResearch}({researchProgress}) 완료=[{completed}] " +
                                $"핵완료={player.HasCompletedNuclearProject} AI완료={player.HasCompletedSelfLearningAI}");
            }

            foreach (var city in state.Cities.OrderBy(item => item.Id.Value))
            {
                var owner = state.Players.Find(item => item.Id == city.OwnerId);
                var economy = CityEconomyResolver.CalculateBreakdown(state, city);
                var districtSummary = state.Districts.Where(item => item.CityId == city.Id)
                    .OrderBy(item => item.Id.Value).Select(item =>
                        $"{item.Type}#{item.Id}(시민{item.AssignedCitizens},건설{item.RemainingConstructionTurns}," +
                        $"수리{item.RemainingRepairTurns},가동{item.IsOperational},약탈{item.IsPillaged}," +
                        $"유지정지{item.IsMaintenanceSuspended},통제{item.ControllerId})");
                var trainingSummary = state.UnitTrainings.Where(item =>
                        state.Districts.Any(district => district.Id == item.DistrictId && district.CityId == city.Id))
                    .OrderBy(item => item.Id.Value).Select(item =>
                        $"{item.Type}#{item.Id}(남은턴{item.RemainingTurns},배치대기{item.IsAwaitingDeployment})");
                text.AppendLine($"CITY {city.Name}#{city.Id} 소유={owner?.Slot}/{city.OwnerId} " +
                                $"인구={city.Population} 금={city.Gold} 식량={city.StoredFood} 연구점={city.ResearchPoints} " +
                                $"산출=식{economy.Food.Total}/금{economy.Gold.Total}/과{economy.Science.Total}/문{economy.Culture.Total} " +
                                $"소비=인구식{economy.PopulationConsumption}/병력식{economy.UnitFoodConsumption}/" +
                                $"병력금{economy.UnitUpkeep}/시설금{economy.FacilityUpkeep} " +
                                $"순식량={economy.FoodNet} 순금={economy.Gold.Total - economy.UnitUpkeep - economy.FacilityUpkeep} " +
                                $"점령자={city.OccupyingPlayerId}");
                text.AppendLine($"  DISTRICTS [{string.Join(" | ", districtSummary)}]");
                text.AppendLine($"  TRAINING [{string.Join(" | ", trainingSummary)}]");
            }

            foreach (var unit in state.Units.OrderBy(item => item.OwnerId.Value).ThenBy(item => item.Id.Value))
            {
                var owner = state.Players.Find(item => item.Id == unit.OwnerId);
                text.AppendLine($"UNIT #{unit.Id} 소유={owner?.Slot}/{unit.OwnerId} 본도시={unit.HomeCityId} " +
                                $"병종={unit.Type} 타일={unit.TileId} HP={unit.HitPoints}/{UnitRules.MaximumHitPoints(unit.Type)} " +
                                $"군량={unit.CarriedFood}/{UnitRules.FoodCapacity(state, unit)} 이동={unit.RemainingMovement} " +
                                $"굶주림={unit.IsStarving} 재명령턴={unit.ManeuverRecommandTurn}");
            }
            return text.ToString();
        }

        private static string FormatCommand(GameCommand command)
        {
            var path = command.Path == null || command.Path.Count == 0
                ? "없음" : string.Join(">", command.Path);
            return $"CMD #{command.CommandId} 플레이어={command.PlayerId} 종류={command.Type} " +
                   $"주체={command.SubjectId} 대상={command.TargetId} 값={command.PrimaryValue}/{command.SecondaryValue} " +
                   $"경로=[{path}]";
        }

        private static string FormatEvent(GameEvent gameEvent)
        {
            return $"EVT #{gameEvent.Sequence} 종류={gameEvent.Type} 출처={gameEvent.SourceId} " +
                   $"대상={gameEvent.TargetId} 값={gameEvent.PrimaryValue}/{gameEvent.SecondaryValue}";
        }

        private void WriteInitial(string contents)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(latestPath));
                File.WriteAllText(latestPath, contents, new UTF8Encoding(true));
                File.WriteAllText(archivePath, contents, new UTF8Encoding(true));
                Debug.Log($"경기 감사 로그 시작: {latestPath}");
            }
            catch (Exception exception)
            {
                enabled = false;
                Debug.LogWarning($"경기 감사 로그를 시작하지 못했습니다: {exception.Message}");
            }
        }

        private void Append(string contents)
        {
            try
            {
                File.AppendAllText(latestPath, contents, Encoding.UTF8);
                File.AppendAllText(archivePath, contents, Encoding.UTF8);
            }
            catch (Exception exception)
            {
                enabled = false;
                Debug.LogWarning($"경기 감사 로그 기록을 중단했습니다: {exception.Message}");
            }
        }
    }
}
