using System;
using System.Collections.Generic;

namespace LittleCiv.Core
{
    public static class TacticalPathfinder
    {
        private sealed class Candidate
        {
            public TileState Tile;
            public int Traffic;
            public int RemainingDistance;
            public int Progress;
            public int Cross;
        }

        public static List<EntityId> FindPath(GameState state, UnitState unit, EntityId target,
            ISet<EntityId> allowedCities = null,
            IReadOnlyDictionary<EntityId, int> reservedTraffic = null,
            int formationIndex = 0,
            bool respectCapacity = true)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (unit == null || !unit.TileId.IsValid || !target.IsValid || unit.TileId == target)
                return new List<EntityId>();
            var targetCoordinate = MapTraversal.GlobalCoordinate(state, target);
            if (!targetCoordinate.HasValue) return new List<EntityId>();

            var byCoordinate = new Dictionary<HexCoord, TileState>();
            for (var index = 0; index < state.Tiles.Count; index++)
            {
                var coordinate = MapTraversal.GlobalCoordinate(state, state.Tiles[index].Id);
                if (coordinate.HasValue) byCoordinate[coordinate.Value] = state.Tiles[index];
            }

            var queue = new Queue<EntityId>();
            var previous = new Dictionary<EntityId, EntityId>();
            queue.Enqueue(unit.TileId);
            previous[unit.TileId] = default;
            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                if (current == target) break;
                var currentCoordinate = MapTraversal.GlobalCoordinate(state, current);
                if (!currentCoordinate.HasValue) continue;
                var candidates = new List<Candidate>();
                var delta = targetCoordinate.Value - currentCoordinate.Value;
                for (var directionIndex = 0; directionIndex < 6; directionIndex++)
                {
                    var direction = HexCoord.Direction(directionIndex);
                    if (!byCoordinate.TryGetValue(currentCoordinate.Value + direction, out var tile) ||
                        previous.ContainsKey(tile.Id)) continue;
                    if (allowedCities != null && !allowedCities.Contains(tile.CityId)) continue;
                    if (tile.Id != target && HasEnemyUnit(state, unit.OwnerId, tile.Id)) continue;
                    if (tile.Id != target && respectCapacity && !HasCapacity(state, unit, tile.Id)) continue;
                    candidates.Add(new Candidate
                    {
                        Tile = tile,
                        Traffic = ReservedTraffic(reservedTraffic, tile.Id),
                        RemainingDistance = HexCoord.Distance(currentCoordinate.Value + direction,
                            targetCoordinate.Value),
                        Progress = Dot(delta, direction),
                        Cross = (delta.Q * direction.R) - (delta.R * direction.Q)
                    });
                }
                candidates.Sort((left, right) => CompareCandidates(left, right, formationIndex));
                for (var index = 0; index < candidates.Count; index++)
                {
                    previous[candidates[index].Tile.Id] = current;
                    queue.Enqueue(candidates[index].Tile.Id);
                }
            }

            if (!previous.ContainsKey(target)) return new List<EntityId>();
            var path = new List<EntityId>();
            for (var cursor = target; cursor != unit.TileId; cursor = previous[cursor]) path.Add(cursor);
            path.Reverse();
            return path;
        }

        public static void AddTraffic(IDictionary<EntityId, int> traffic, IReadOnlyList<EntityId> path)
        {
            if (traffic == null || path == null) return;
            for (var index = 0; index < path.Count; index++)
            {
                traffic.TryGetValue(path[index], out var count);
                traffic[path[index]] = count + 1;
            }
        }

        private static int CompareCandidates(Candidate left, Candidate right, int formationIndex)
        {
            var comparison = left.Traffic.CompareTo(right.Traffic);
            if (comparison != 0) return comparison;
            comparison = left.RemainingDistance.CompareTo(right.RemainingDistance);
            if (comparison != 0) return comparison;

            var lane = Math.Abs(formationIndex) % 3;
            if (lane == 0)
            {
                comparison = Math.Abs(left.Cross).CompareTo(Math.Abs(right.Cross));
                if (comparison != 0) return comparison;
            }
            else
            {
                comparison = lane == 1
                    ? right.Cross.CompareTo(left.Cross)
                    : left.Cross.CompareTo(right.Cross);
                if (comparison != 0) return comparison;
            }
            comparison = right.Progress.CompareTo(left.Progress);
            if (comparison != 0) return comparison;
            comparison = left.Cross.CompareTo(right.Cross);
            return comparison;
        }

        private static int Dot(HexCoord left, HexCoord right) =>
            (left.Q * right.Q) + (left.R * right.R) + (left.S * right.S);

        private static int ReservedTraffic(IReadOnlyDictionary<EntityId, int> traffic, EntityId tileId)
        {
            return traffic != null && traffic.TryGetValue(tileId, out var value) ? value : 0;
        }

        private static bool HasEnemyUnit(GameState state, EntityId ownerId, EntityId tileId)
        {
            return state.Units.Exists(item => item.TileId == tileId && item.OwnerId != ownerId &&
                item.HitPoints > 0 &&
                UnitDiplomacyRules.AreHostile(state, ownerId, item.OwnerId, tileId));
        }

        private static bool HasCapacity(GameState state, UnitState moving, EntityId tileId)
        {
            var supply = UnitRules.IsSupply(moving.Type);
            var count = state.Units.FindAll(item => item.Id != moving.Id && item.TileId == tileId &&
                UnitRules.IsSupply(item.Type) == supply && item.HitPoints > 0).Count;
            return count < (supply ? UnitRules.SupplyUnitsPerTile : UnitRules.CombatUnitsPerTile);
        }
    }
}
