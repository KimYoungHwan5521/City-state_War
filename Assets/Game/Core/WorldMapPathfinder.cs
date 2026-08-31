using System;
using System.Collections.Generic;

namespace LittleCiv.Core
{
    public static class WorldMapPathfinder
    {
        public static List<EntityId> FindCityPath(GameState state, EntityId startCityId, EntityId destinationCityId)
        {
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            var start = state.Cities.Find(city => city.Id == startCityId);
            var destination = state.Cities.Find(city => city.Id == destinationCityId);
            if (start == null || destination == null)
            {
                throw new ArgumentException("Both path endpoints must be cities in the game state.");
            }

            var frontier = new Queue<CityState>();
            var previous = new Dictionary<long, EntityId>();
            frontier.Enqueue(start);
            previous.Add(start.Id.Value, default(EntityId));

            while (frontier.Count > 0)
            {
                var current = frontier.Dequeue();
                if (current.Id == destination.Id)
                {
                    return Reconstruct(previous, destination.Id);
                }

                var currentWorld = new HexCoord(current.WorldQ, current.WorldR);
                var neighbors = state.Cities.FindAll(city =>
                    HexCoord.Distance(currentWorld, new HexCoord(city.WorldQ, city.WorldR)) == 1);
                neighbors.Sort((left, right) => left.Id.CompareTo(right.Id));
                foreach (var neighbor in neighbors)
                {
                    if (previous.ContainsKey(neighbor.Id.Value))
                    {
                        continue;
                    }

                    previous.Add(neighbor.Id.Value, current.Id);
                    frontier.Enqueue(neighbor);
                }
            }

            return new List<EntityId>();
        }

        private static List<EntityId> Reconstruct(Dictionary<long, EntityId> previous, EntityId destination)
        {
            var result = new List<EntityId>();
            var current = destination;
            while (current.IsValid)
            {
                result.Add(current);
                current = previous[current.Value];
            }
            result.Reverse();
            return result;
        }
    }
}
