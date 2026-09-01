using System;
using System.Collections.Generic;

namespace LittleCiv.Core
{
    public static class CityPopulationResolver
    {
        public static List<EntityId> ResolveGrowth(GameState state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));

            var grownCities = new List<EntityId>();
            for (var index = 0; index < state.Cities.Count; index++)
            {
                var city = state.Cities[index];
                var surplus = city.LastFoodProduction - city.Population - city.LastUnitFoodConsumption;
                if (surplus <= 0) continue;

                city.GrowthProgress += surplus;
                var requiredGrowth = city.Population * 3;
                if (city.GrowthProgress < requiredGrowth) continue;

                city.GrowthProgress -= requiredGrowth;
                city.Population++;
                grownCities.Add(city.Id);
            }

            return grownCities;
        }

        public static List<EntityId> ResolveFamine(GameState state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));

            var diminishedCities = new List<EntityId>();
            for (var index = 0; index < state.Cities.Count; index++)
            {
                var city = state.Cities[index];
                var deficit = city.Population + city.LastUnitFoodConsumption - city.LastFoodProduction;
                if (deficit <= 0) continue;

                var requiredFamine = city.Population;
                city.FamineProgress += deficit;
                if (city.FamineProgress < requiredFamine) continue;

                if (city.Population <= 1)
                {
                    city.FamineProgress = requiredFamine;
                    continue;
                }

                city.FamineProgress -= requiredFamine;
                city.Population--;
                diminishedCities.Add(city.Id);
            }

            return diminishedCities;
        }
    }
}
