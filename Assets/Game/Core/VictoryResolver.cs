using System;
using System.Collections.Generic;

namespace LittleCiv.Core
{
    public static class VictoryResolver
    {
        public static EntityId ResolveCulture(GameState state)
        {
            return Resolve(state, VictoryType.Culture,
                player => player.HasMetCultureVictoryCondition);
        }

        public static EntityId ResolveScience(GameState state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (state.IsGameOver) return default;
            var players = state.Players.FindAll(item => item.Slot != PlayerSlot.Neutral);
            var aiCandidates = players.FindAll(item => item.HasCompletedSelfLearningAI);
            if (aiCandidates.Count > 1)
            {
                state.Victory = VictoryType.Draw;
                state.WinnerId = default;
                return default;
            }
            if (aiCandidates.Count == 1)
                return Resolve(state, VictoryType.Science, item => item.HasCompletedSelfLearningAI);

            var nuclearCandidates = players.FindAll(item => item.HasCompletedNuclearProject);
            if (nuclearCandidates.Count > 1)
            {
                for (var index = 0; index < nuclearCandidates.Count; index++)
                    nuclearCandidates[index].HasUnlockedSelfLearningAI = true;
                return default;
            }
            return nuclearCandidates.Count == 1
                ? Resolve(state, VictoryType.Science, item => item.HasCompletedNuclearProject)
                : default;
        }

        public static EntityId ResolveConquest(GameState state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (state.IsGameOver) return default;
            var players = state.Players.FindAll(item => item.Slot != PlayerSlot.Neutral);
            if (players.Count != 2) return default;
            var lost = new List<PlayerState>();
            for (var index = 0; index < players.Count; index++)
            {
                var city = state.Cities.Find(item => item.OwnerId == players[index].Id);
                var government = city == null ? null : state.Districts.Find(item =>
                    item.CityId == city.Id && item.Type == DistrictType.Government);
                if (government != null && government.ControllerId != city.OwnerId)
                    lost.Add(players[index]);
            }
            if (lost.Count == 2)
            {
                ExchangeReciprocallyCapturedCities(state, players[0], players[1]);
                return default;
            }
            if (lost.Count != 1) return default;
            var winner = players.Find(item => item.Id != lost[0].Id);
            if (winner == null) return default;
            state.Victory = VictoryType.Conquest;
            state.WinnerId = winner.Id;
            return winner.Id;
        }

        private static void ExchangeReciprocallyCapturedCities(
            GameState state, PlayerState firstPlayer, PlayerState secondPlayer)
        {
            var firstCity = state.Cities.Find(item => item.OwnerId == firstPlayer.Id);
            var secondCity = state.Cities.Find(item => item.OwnerId == secondPlayer.Id);
            if (firstCity == null || secondCity == null) return;
            var firstGovernment = state.Districts.Find(item => item.CityId == firstCity.Id &&
                item.Type == DistrictType.Government);
            var secondGovernment = state.Districts.Find(item => item.CityId == secondCity.Id &&
                item.Type == DistrictType.Government);
            if (firstGovernment == null || secondGovernment == null ||
                firstGovernment.ControllerId != secondPlayer.Id ||
                secondGovernment.ControllerId != firstPlayer.Id) return;

            var firstOldOwner = firstCity.OwnerId;
            var secondOldOwner = secondCity.OwnerId;
            firstCity.OwnerId = secondOldOwner;
            secondCity.OwnerId = firstOldOwner;
            firstCity.OccupyingPlayerId = default;
            secondCity.OccupyingPlayerId = default;
            firstCity.IndependenceProgress = 0;
            secondCity.IndependenceProgress = 0;

            TransferCityAssets(state, firstCity, firstOldOwner);
            TransferCityAssets(state, secondCity, secondOldOwner);
            RetargetPlayerUnits(state, firstOldOwner, firstCity.Id, secondCity.Id);
            RetargetPlayerUnits(state, secondOldOwner, secondCity.Id, firstCity.Id);
            RetargetPendingPlayerState(state, firstOldOwner, firstCity.Id, secondCity.Id);
            RetargetPendingPlayerState(state, secondOldOwner, secondCity.Id, firstCity.Id);
            OccupyDistrictsUnderForeignUnits(state, firstCity);
            OccupyDistrictsUnderForeignUnits(state, secondCity);
            CityCultureRules.Normalize(firstCity);
            CityCultureRules.Normalize(secondCity);
        }

        private static void TransferCityAssets(GameState state, CityState city, EntityId oldOwner)
        {
            for (var index = 0; index < state.Tiles.Count; index++)
            {
                var tile = state.Tiles[index];
                if (tile.CityId != city.Id) continue;
                tile.ControllerId = city.OwnerId;
                if (tile.GroundFoodOwnerId == oldOwner && tile.GroundFoodReturnTurn > 0)
                    tile.GroundFoodOwnerId = city.OwnerId;
            }
            for (var index = 0; index < state.Districts.Count; index++)
            {
                var district = state.Districts[index];
                if (district.CityId != city.Id) continue;
                district.ControllerId = city.OwnerId;
                district.IsOperational = district.RemainingConstructionTurns <= 0 &&
                    !district.IsPillaged && !district.IsMaintenanceSuspended &&
                    (district.Type == DistrictType.Government || district.AssignedCitizens > 0);
            }
            for (var index = 0; index < state.UnitTrainings.Count; index++)
            {
                var district = state.Districts.Find(item =>
                    item.Id == state.UnitTrainings[index].DistrictId);
                if (district != null && district.CityId == city.Id)
                    state.UnitTrainings[index].OwnerId = city.OwnerId;
            }
            for (var index = 0; index < state.NuclearProjects.Count; index++)
            {
                var district = state.Districts.Find(item =>
                    item.Id == state.NuclearProjects[index].DistrictId);
                if (district != null && district.CityId == city.Id)
                    state.NuclearProjects[index].OwnerId = city.OwnerId;
            }
        }

        private static void RetargetPlayerUnits(GameState state, EntityId playerId,
            EntityId lostCityId, EntityId gainedCityId)
        {
            for (var index = 0; index < state.Units.Count; index++)
                if (state.Units[index].OwnerId == playerId &&
                    state.Units[index].HomeCityId == lostCityId)
                    state.Units[index].HomeCityId = gainedCityId;
        }

        private static void RetargetPendingPlayerState(GameState state, EntityId playerId,
            EntityId lostCityId, EntityId gainedCityId)
        {
            for (var index = 0; index < state.Levies.Count; index++)
                if (state.Levies[index].PlayerId == playerId &&
                    state.Levies[index].PaymentCityId == lostCityId)
                    state.Levies[index].PaymentCityId = gainedCityId;
            state.TradeReservations.RemoveAll(item => item.PlayerId == playerId &&
                (item.SourceCityId == lostCityId || item.TargetCityId == lostCityId));
        }

        private static void OccupyDistrictsUnderForeignUnits(GameState state, CityState city)
        {
            var districts = state.Districts.FindAll(item => item.CityId == city.Id);
            districts.Sort((left, right) => left.Id.CompareTo(right.Id));
            for (var districtIndex = 0; districtIndex < districts.Count; districtIndex++)
            {
                var district = districts[districtIndex];
                var foreign = state.Units.FindAll(item => item.TileId == district.TileId &&
                    item.OwnerId != city.OwnerId && item.HitPoints > 0);
                if (foreign.Count == 0 || state.Units.Exists(item => item.TileId == district.TileId &&
                        item.OwnerId == city.OwnerId && item.HitPoints > 0)) continue;
                foreign.Sort((left, right) => left.Id.CompareTo(right.Id));
                OccupationResolver.Resolve(state, foreign[0].OwnerId, district.TileId, false);
            }
        }

        public static EntityId ResolveColdWarNuclearStrike(GameState state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (state.IsGameOver) return default;
            var players = state.Players.FindAll(item => item.Slot != PlayerSlot.Neutral &&
                item.HasUnlockedSelfLearningAI && item.HasCompletedNuclearProject);
            if (players.Count != 2) return default;

            PlayerState ready = null;
            var readyCount = 0;
            var pillagedCount = 0;
            for (var index = 0; index < players.Count; index++)
            {
                var facility = FindNuclearFacility(state, players[index].Id);
                if (facility != null && facility.IsPillaged)
                {
                    pillagedCount++;
                    continue;
                }
                if (facility != null)
                {
                    ready = players[index];
                    readyCount++;
                }
            }
            if (pillagedCount != 1 || readyCount != 1) return default;
            state.Victory = VictoryType.Science;
            state.WinnerId = ready.Id;
            return ready.Id;
        }

        private static DistrictState FindNuclearFacility(GameState state, EntityId ownerId)
        {
            for (var index = 0; index < state.Districts.Count; index++)
            {
                var district = state.Districts[index];
                if (district.Type != DistrictType.NuclearFacility ||
                    district.RemainingConstructionTurns > 0) continue;
                var city = state.Cities.Find(item => item.Id == district.CityId);
                if (city != null && city.OwnerId == ownerId) return district;
            }
            return null;
        }

        private static EntityId Resolve(
            GameState state, VictoryType type, Func<PlayerState, bool> condition)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (state.IsGameOver) return default;
            var candidates = new List<PlayerState>(state.Players.FindAll(item =>
                item.Slot != PlayerSlot.Neutral && condition(item)));
            candidates.Sort((left, right) =>
            {
                var slot = left.Slot.CompareTo(right.Slot);
                return slot != 0 ? slot : left.Id.CompareTo(right.Id);
            });
            if (candidates.Count == 0) return default;
            state.Victory = type;
            state.WinnerId = candidates[0].Id;
            return state.WinnerId;
        }
    }
}
