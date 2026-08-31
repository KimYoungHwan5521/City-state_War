using System;

namespace LittleCiv.Core
{
    public static class CityFoodResolver
    {
        public static void ResolveStorage(GameState state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));

            for (var index = 0; index < state.Cities.Count; index++)
            {
                var city = state.Cities[index];
                var netProduction = city.LastFoodProduction - city.Population;
                city.StoredFood = Math.Max(0, city.StoredFood + netProduction);
            }
        }
    }
}
