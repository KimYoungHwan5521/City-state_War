using System;

namespace LittleCiv.Core
{
    public static class CultureVictoryConditionResolver
    {
        public static void UpdateCandidates(GameState state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            for (var playerIndex = 0; playerIndex < state.Players.Count; playerIndex++)
            {
                var player = state.Players[playerIndex];
                player.HasMetCultureVictoryCondition = false;
                if (player.Slot == PlayerSlot.Neutral) continue;
                for (var cityIndex = 0; cityIndex < state.Cities.Count; cityIndex++)
                {
                    var city = state.Cities[cityIndex];
                    var owner = state.Players.Find(item => item.Id == city.OwnerId);
                    if (owner == null || owner.Slot == PlayerSlot.Neutral || owner.Id == player.Id) continue;
                    if (!HasForeignMajority(city, player.Id)) continue;
                    player.HasMetCultureVictoryCondition = true;
                    break;
                }
            }
        }

        public static bool HasForeignMajority(CityState city, EntityId cultureOwnerId)
        {
            if (city == null) throw new ArgumentNullException(nameof(city));
            return CityCultureRules.PreferredCitizens(city, cultureOwnerId) > city.Population / 2;
        }
    }
}
