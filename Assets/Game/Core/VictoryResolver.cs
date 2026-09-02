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
            return Resolve(state, VictoryType.Science,
                player => player.HasCompletedNuclearProject);
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
