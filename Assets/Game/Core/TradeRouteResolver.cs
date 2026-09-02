using System;
using System.Collections.Generic;

namespace LittleCiv.Core
{
    public sealed class TradeRouteResult
    {
        public bool IsReachable;
        public int Distance;
        public int AdditionalDistance;
        public readonly List<EntityId> CityPath = new List<EntityId>();
        public readonly List<EntityId> BlockedCityIds = new List<EntityId>();
    }

    public static class TradeRouteResolver
    {
        public static TradeRouteResult Find(
            GameState state, EntityId playerId, EntityId sourceCityId, EntityId targetCityId)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            var result = new TradeRouteResult();
            var source = state.Cities.Find(item => item.Id == sourceCityId);
            var target = state.Cities.Find(item => item.Id == targetCityId);
            if (source == null || target == null || source.OwnerId != playerId ||
                source.Id == target.Id || !IsNeutral(state, target)) return result;

            var cities = new List<CityState>(state.Cities);
            cities.Sort(CompareCity);
            var frontier = new Queue<CityState>();
            var visited = new HashSet<EntityId>();
            var previous = new Dictionary<EntityId, EntityId>();
            frontier.Enqueue(source);
            visited.Add(source.Id);

            while (frontier.Count > 0)
            {
                var current = frontier.Dequeue();
                for (var index = 0; index < cities.Count; index++)
                {
                    var next = cities[index];
                    if (visited.Contains(next.Id) || !AreAdjacent(current, next)) continue;
                    if (IsBlocked(state, next, playerId, targetCityId))
                    {
                        AddUniqueSorted(result.BlockedCityIds, next.Id);
                        continue;
                    }
                    visited.Add(next.Id);
                    previous[next.Id] = current.Id;
                    if (next.Id == target.Id)
                    {
                        BuildPath(result, previous, source.Id, target.Id);
                        return result;
                    }
                    frontier.Enqueue(next);
                }
            }
            return result;
        }

        public static bool AreAdjacent(CityState left, CityState right)
        {
            if (left == null || right == null || left.Id == right.Id) return false;
            return HexCoord.Distance(new HexCoord(left.WorldQ, left.WorldR),
                new HexCoord(right.WorldQ, right.WorldR)) == 1;
        }

        private static bool IsBlocked(
            GameState state, CityState city, EntityId playerId, EntityId targetCityId)
        {
            if (city.Id == targetCityId)
                return IsOccupiedByOpponent(state, city, playerId);
            if (city.OwnerId == playerId) return false;
            if (!IsNeutral(state, city)) return true;
            if (IsOccupiedByOpponent(state, city, playerId)) return true;
            if (IsOccupiedBy(state, city, playerId)) return false;
            return NeutralCityRules.Favor(city, playerId) < 0;
        }

        private static bool IsNeutral(GameState state, CityState city)
        {
            var owner = state.Players.Find(item => item.Id == city.OwnerId);
            return owner != null && owner.Slot == PlayerSlot.Neutral;
        }

        private static bool IsOccupiedByOpponent(GameState state, CityState city, EntityId playerId)
        {
            var controller = GovernmentController(state, city);
            return controller.IsValid && controller != city.OwnerId && controller != playerId;
        }

        private static bool IsOccupiedBy(GameState state, CityState city, EntityId playerId) =>
            GovernmentController(state, city) == playerId && city.OwnerId != playerId;

        private static EntityId GovernmentController(GameState state, CityState city)
        {
            var government = state.Districts.Find(item => item.CityId == city.Id &&
                item.Type == DistrictType.Government);
            return government == null ? default(EntityId) : government.ControllerId;
        }

        private static void BuildPath(TradeRouteResult result,
            Dictionary<EntityId, EntityId> previous, EntityId sourceId, EntityId targetId)
        {
            var cursor = targetId;
            result.CityPath.Add(cursor);
            while (cursor != sourceId)
            {
                if (!previous.TryGetValue(cursor, out cursor)) return;
                result.CityPath.Add(cursor);
            }
            result.CityPath.Reverse();
            result.IsReachable = true;
            result.Distance = result.CityPath.Count - 1;
            result.AdditionalDistance = Math.Max(0, result.Distance - 1);
        }

        private static int CompareCity(CityState left, CityState right)
        {
            var q = left.WorldQ.CompareTo(right.WorldQ);
            if (q != 0) return q;
            var r = left.WorldR.CompareTo(right.WorldR);
            return r != 0 ? r : left.Id.CompareTo(right.Id);
        }

        private static void AddUniqueSorted(List<EntityId> ids, EntityId id)
        {
            if (!ids.Contains(id)) ids.Add(id);
            ids.Sort((left, right) => left.CompareTo(right));
        }
    }
}
