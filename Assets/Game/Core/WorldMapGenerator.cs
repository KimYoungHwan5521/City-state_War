using System;
using System.Collections.Generic;

namespace LittleCiv.Core
{
    public static class WorldMapGenerator
    {
        public const int BuildableRadius = 3;
        public const int BoundaryRadius = 4;

        private struct BoundaryKey : IEquatable<BoundaryKey>
        {
            public EntityId CityId;
            public HexCoord Local;

            public bool Equals(BoundaryKey other) => CityId == other.CityId && Local == other.Local;
            public override bool Equals(object obj) => obj is BoundaryKey other && Equals(other);
            public override int GetHashCode() => unchecked((CityId.GetHashCode() * 397) ^ Local.GetHashCode());
        }

        public static WorldMapTopology PopulateTiles(GameState state)
        {
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            if (state.Cities == null || state.Cities.Count == 0)
            {
                throw new InvalidOperationException("At least one city is required to generate a map.");
            }

            state.Tiles.Clear();
            var topology = new WorldMapTopology();
            var views = new Dictionary<long, CityMapView>();
            var citiesByWorld = new Dictionary<HexCoord, CityState>();
            var boundaryIndices = new Dictionary<BoundaryKey, int>();
            var boundaryKeys = new List<BoundaryKey>();

            foreach (var city in state.Cities)
            {
                var world = new HexCoord(city.WorldQ, city.WorldR);
                if (citiesByWorld.ContainsKey(world))
                {
                    throw new InvalidOperationException($"Multiple cities occupy world coordinate {world}.");
                }

                citiesByWorld.Add(world, city);
                var view = new CityMapView { CityId = city.Id };
                topology.CityViews.Add(view);
                views.Add(city.Id.Value, view);

                foreach (var local in HexCoord.WithinRadius(BuildableRadius))
                {
                    var tile = new TileState
                    {
                        Id = state.AllocateId(),
                        CityId = city.Id,
                        Q = local.Q,
                        R = local.R,
                        ControllerId = city.OwnerId,
                        VisibleCityIds = new List<EntityId> { city.Id }
                    };
                    state.Tiles.Add(tile);
                    view.Tiles.Add(ToPlacement(tile.Id, local, true));
                }

                foreach (var local in HexCoord.Ring(BoundaryRadius))
                {
                    var key = new BoundaryKey { CityId = city.Id, Local = local };
                    boundaryIndices.Add(key, boundaryKeys.Count);
                    boundaryKeys.Add(key);
                }
            }

            var sets = new DisjointSet(boundaryKeys.Count);
            foreach (var city in state.Cities)
            {
                var cityWorld = new HexCoord(city.WorldQ, city.WorldR);
                for (var directionIndex = 0; directionIndex < 6; directionIndex++)
                {
                    CityState neighbor;
                    if (!citiesByWorld.TryGetValue(cityWorld + HexCoord.Direction(directionIndex), out neighbor) ||
                        city.Id.Value >= neighbor.Id.Value)
                    {
                        continue;
                    }

                    var ownSide = HexCoord.Side(BoundaryRadius, directionIndex);
                    var neighborSide = HexCoord.Side(BoundaryRadius, (directionIndex + 3) % 6);
                    for (var index = 0; index < ownSide.Count; index++)
                    {
                        var ownKey = new BoundaryKey { CityId = city.Id, Local = ownSide[index] };
                        var neighborKey = new BoundaryKey
                        {
                            CityId = neighbor.Id,
                            Local = neighborSide[neighborSide.Count - 1 - index]
                        };
                        sets.Union(boundaryIndices[ownKey], boundaryIndices[neighborKey]);
                    }
                }
            }

            var tilesByRoot = new Dictionary<int, TileState>();
            for (var index = 0; index < boundaryKeys.Count; index++)
            {
                var key = boundaryKeys[index];
                var root = sets.Find(index);
                TileState tile;
                if (!tilesByRoot.TryGetValue(root, out tile))
                {
                    tile = new TileState
                    {
                        Id = state.AllocateId(),
                        IsSharedBoundary = true,
                        VisibleCityIds = new List<EntityId>()
                    };
                    tilesByRoot.Add(root, tile);
                    state.Tiles.Add(tile);
                }

                if (!tile.VisibleCityIds.Contains(key.CityId))
                {
                    tile.VisibleCityIds.Add(key.CityId);
                    tile.VisibleCityIds.Sort((left, right) => left.CompareTo(right));
                }

                views[key.CityId.Value].Tiles.Add(ToPlacement(tile.Id, key.Local, false));
            }

            topology.CityViews.Sort((left, right) => left.CityId.CompareTo(right.CityId));
            foreach (var view in topology.CityViews)
            {
                view.Tiles.Sort((left, right) =>
                {
                    var qComparison = left.LocalQ.CompareTo(right.LocalQ);
                    return qComparison != 0 ? qComparison : left.LocalR.CompareTo(right.LocalR);
                });
            }

            state.MapTopology = topology;
            CityResourceGenerator.Populate(state);
            return topology;
        }

        private static CityTilePlacement ToPlacement(EntityId tileId, HexCoord local, bool isBuildable)
        {
            return new CityTilePlacement
            {
                TileId = tileId,
                LocalQ = local.Q,
                LocalR = local.R,
                IsBuildable = isBuildable
            };
        }

        private sealed class DisjointSet
        {
            private readonly int[] parent;
            private readonly byte[] rank;

            public DisjointSet(int count)
            {
                parent = new int[count];
                rank = new byte[count];
                for (var index = 0; index < count; index++)
                {
                    parent[index] = index;
                }
            }

            public int Find(int value)
            {
                if (parent[value] != value)
                {
                    parent[value] = Find(parent[value]);
                }

                return parent[value];
            }

            public void Union(int left, int right)
            {
                var leftRoot = Find(left);
                var rightRoot = Find(right);
                if (leftRoot == rightRoot)
                {
                    return;
                }

                if (rank[leftRoot] < rank[rightRoot])
                {
                    parent[leftRoot] = rightRoot;
                }
                else
                {
                    parent[rightRoot] = leftRoot;
                    if (rank[leftRoot] == rank[rightRoot])
                    {
                        rank[leftRoot]++;
                    }
                }
            }
        }
    }
}
