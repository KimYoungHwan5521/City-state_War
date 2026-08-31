using System;
using System.Collections.Generic;

namespace LittleCiv.Core
{
    public static class CityResourceGenerator
    {
        public static void Populate(GameState state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));

            for (var tileIndex = 0; tileIndex < state.Tiles.Count; tileIndex++)
            {
                state.Tiles[tileIndex].ResourceType = TileResourceType.None;
            }

            var cities = new List<CityState>(state.Cities);
            cities.Sort((left, right) => left.Id.CompareTo(right.Id));
            for (var cityIndex = 0; cityIndex < cities.Count; cityIndex++)
            {
                PopulateCity(state, cities[cityIndex]);
            }
        }

        private static void PopulateCity(GameState state, CityState city)
        {
            var view = state.MapTopology.FindView(city.Id);
            if (view == null) throw new InvalidOperationException($"City {city.Id} has no map view.");

            var near = CandidateTiles(state, view, 1, 2);
            var all = CandidateTiles(state, view, 1, 3);
            var random = new DeterministicRandom(unchecked(state.MatchSeed ^ (city.Id.Value * 7919L)));
            Shuffle(near, random);
            Shuffle(all, random);

            PlaceFirstAvailable(state, near, TileResourceType.Commerce);
            PlaceFirstAvailable(state, near, TileResourceType.Science);
            PlaceFirstAvailable(state, near, TileResourceType.Culture);
            for (var index = 0; index < 3; index++)
            {
                PlaceFirstAvailable(state, all, TileResourceType.Food);
            }
        }

        private static List<EntityId> CandidateTiles(
            GameState state,
            CityMapView view,
            int minimumDistance,
            int maximumDistance)
        {
            var result = new List<EntityId>();
            for (var index = 0; index < view.Tiles.Count; index++)
            {
                var placement = view.Tiles[index];
                if (!placement.IsBuildable) continue;
                var distance = HexCoord.Distance(new HexCoord(0, 0),
                    new HexCoord(placement.LocalQ, placement.LocalR));
                if (distance >= minimumDistance && distance <= maximumDistance)
                {
                    result.Add(placement.TileId);
                }
            }

            result.Sort();
            return result;
        }

        private static void PlaceFirstAvailable(
            GameState state,
            List<EntityId> candidates,
            TileResourceType resourceType)
        {
            for (var index = 0; index < candidates.Count; index++)
            {
                var tile = state.Tiles.Find(item => item.Id == candidates[index]);
                if (tile == null || tile.ResourceType != TileResourceType.None) continue;
                tile.ResourceType = resourceType;
                return;
            }

            throw new InvalidOperationException($"No tile is available for {resourceType}.");
        }

        private static void Shuffle<T>(List<T> values, DeterministicRandom random)
        {
            for (var index = values.Count - 1; index > 0; index--)
            {
                var swapIndex = random.NextInt(index + 1);
                var value = values[index];
                values[index] = values[swapIndex];
                values[swapIndex] = value;
            }
        }
    }
}
