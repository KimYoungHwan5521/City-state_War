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
            // Reciprocal same-turn capital captures exchange control and do not end the game.
            if (lost.Count != 1) return default;
            var winner = players.Find(item => item.Id != lost[0].Id);
            if (winner == null) return default;
            state.Victory = VictoryType.Conquest;
            state.WinnerId = winner.Id;
            return winner.Id;
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
