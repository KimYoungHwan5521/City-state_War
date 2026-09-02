using System;
using System.Collections.Generic;

namespace LittleCiv.Core
{
    public sealed class UnitTrainingAdvanceResult
    {
        public readonly List<EntityId> CompletedUnitIds = new List<EntityId>();
        public readonly List<EntityId> WaitingTrainingIds = new List<EntityId>();
    }

    public static class UnitTrainingResolver
    {
        public static bool TryStart(GameState state, GameCommand command, out UnitTrainingState training)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (command == null) throw new ArgumentNullException(nameof(command));
            training = null;
            if (!Enum.IsDefined(typeof(UnitType), command.PrimaryValue)) return false;
            var district = FindDistrict(state, command.SubjectId);
            if (district == null || district.Type != DistrictType.Military || !CanOperate(state, district)) return false;
            var city = FindCity(state, district.CityId);
            if (city == null || city.OwnerId != command.PlayerId || HasTraining(state, district.Id)) return false;
            var type = (UnitType)command.PrimaryValue;
            var player = state.Players.Find(item => item.Id == command.PlayerId);
            if (player == null || (player.ResearchUnlocksEnabled &&
                !player.UnlockedUnitTypes.Contains(type))) return false;
            var cost = UnitRules.TrainingGold(type);
            if (city.Gold < cost) return false;

            city.Gold -= cost;
            training = new UnitTrainingState
            {
                Id = state.AllocateId(),
                DistrictId = district.Id,
                OwnerId = command.PlayerId,
                Type = type,
                RemainingTurns = UnitRules.TrainingTurns(type)
            };
            state.UnitTrainings.Add(training);
            return true;
        }

        public static UnitTrainingAdvanceResult Advance(GameState state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            var result = new UnitTrainingAdvanceResult();
            var trainings = new List<UnitTrainingState>(state.UnitTrainings);
            trainings.Sort((left, right) => left.Id.CompareTo(right.Id));
            for (var index = 0; index < trainings.Count; index++)
            {
                var training = trainings[index];
                var district = FindDistrict(state, training.DistrictId);
                if (district == null || !CanOperate(state, district)) continue;
                if (!training.IsAwaitingDeployment)
                {
                    training.RemainingTurns--;
                    if (training.RemainingTurns > 0) continue;
                    training.RemainingTurns = 0;
                    training.IsAwaitingDeployment = true;
                }

                if (!HasDeploymentSpace(state, district.TileId, training.Type))
                {
                    result.WaitingTrainingIds.Add(training.Id);
                    continue;
                }

                var unit = new UnitState
                {
                    Id = state.AllocateId(),
                    OwnerId = training.OwnerId,
                    HomeCityId = district.CityId,
                    TileId = district.TileId,
                    Type = training.Type,
                    HitPoints = UnitRules.MaximumHitPoints(training.Type),
                    CarriedFood = 0,
                    RemainingMovement = 0,
                    CreatedTurn = state.TurnNumber
                };
                state.Units.Add(unit);
                state.UnitTrainings.Remove(training);
                result.CompletedUnitIds.Add(unit.Id);
            }
            return result;
        }

        public static List<EntityId> DeployWaiting(GameState state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            var completed = new List<EntityId>();
            var trainings = new List<UnitTrainingState>(state.UnitTrainings);
            trainings.Sort((left, right) => left.Id.CompareTo(right.Id));
            for (var index = 0; index < trainings.Count; index++)
            {
                var training = trainings[index];
                if (!training.IsAwaitingDeployment) continue;
                var district = FindDistrict(state, training.DistrictId);
                if (district == null || !CanOperate(state, district) ||
                    !HasDeploymentSpace(state, district.TileId, training.Type)) continue;
                var unit = new UnitState
                {
                    Id = state.AllocateId(), OwnerId = training.OwnerId,
                    HomeCityId = district.CityId, TileId = district.TileId,
                    Type = training.Type, HitPoints = UnitRules.MaximumHitPoints(training.Type),
                    CarriedFood = 0, RemainingMovement = 0, CreatedTurn = state.TurnNumber
                };
                state.Units.Add(unit);
                state.UnitTrainings.Remove(training);
                completed.Add(unit.Id);
            }
            return completed;
        }

        private static bool CanOperate(GameState state, DistrictState district)
        {
            var city = FindCity(state, district.CityId);
            return city != null && district.ControllerId == city.OwnerId && district.IsOperational &&
                   district.AssignedCitizens > 0 && district.RemainingConstructionTurns <= 0;
        }

        private static bool HasDeploymentSpace(GameState state, EntityId tileId, UnitType type)
        {
            var count = 0;
            for (var index = 0; index < state.Units.Count; index++)
            {
                var unit = state.Units[index];
                if (unit.TileId == tileId && UnitRules.IsSupply(unit.Type) == UnitRules.IsSupply(type)) count++;
            }
            return count < (UnitRules.IsSupply(type) ? UnitRules.SupplyUnitsPerTile : UnitRules.CombatUnitsPerTile);
        }

        private static bool HasTraining(GameState state, EntityId districtId)
        {
            for (var index = 0; index < state.UnitTrainings.Count; index++)
                if (state.UnitTrainings[index].DistrictId == districtId) return true;
            return false;
        }

        private static DistrictState FindDistrict(GameState state, EntityId id)
        {
            for (var index = 0; index < state.Districts.Count; index++)
                if (state.Districts[index].Id == id) return state.Districts[index];
            return null;
        }

        private static CityState FindCity(GameState state, EntityId id)
        {
            for (var index = 0; index < state.Cities.Count; index++)
                if (state.Cities[index].Id == id) return state.Cities[index];
            return null;
        }
    }
}
