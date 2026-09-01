using System;
using System.Collections.Generic;

namespace LittleCiv.Core
{
    public static class WorldMapGenerator
    {
        public const int BuildableRadius = 4;

        public static HexCoord CityCenterCoordinate(int worldQ, int worldR)
        {
            // Radius-4 hex territories tile the small-hex grid with these two basis vectors.
            // A world neighbor at (+1, 0) is therefore centered at local offset (+5, +4).
            return new HexCoord((5 * worldQ) - (4 * worldR), (4 * worldQ) + (9 * worldR));
        }

        public static WorldMapTopology PopulateTiles(GameState state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (state.Cities == null || state.Cities.Count == 0)
                throw new InvalidOperationException("At least one city is required to generate a map.");

            state.Tiles.Clear();
            var topology = new WorldMapTopology();
            var occupiedWorldCoordinates = new HashSet<HexCoord>();

            foreach (var city in state.Cities)
            {
                var world = new HexCoord(city.WorldQ, city.WorldR);
                if (!occupiedWorldCoordinates.Add(world))
                    throw new InvalidOperationException($"Multiple cities occupy world coordinate {world}.");

                var view = new CityMapView { CityId = city.Id };
                topology.CityViews.Add(view);
                foreach (var local in HexCoord.WithinRadius(BuildableRadius))
                {
                    var tile = new TileState
                    {
                        Id = state.AllocateId(), CityId = city.Id,
                        Q = local.Q, R = local.R,
                        ControllerId = city.OwnerId,
                        IsSharedBoundary = false,
                        VisibleCityIds = new List<EntityId> { city.Id }
                    };
                    state.Tiles.Add(tile);
                    view.Tiles.Add(new CityTilePlacement
                    {
                        TileId = tile.Id, LocalQ = local.Q, LocalR = local.R, IsBuildable = true
                    });
                }
                view.Tiles.Sort((left, right) =>
                {
                    var qComparison = left.LocalQ.CompareTo(right.LocalQ);
                    return qComparison != 0 ? qComparison : left.LocalR.CompareTo(right.LocalR);
                });
            }

            topology.CityViews.Sort((left, right) => left.CityId.CompareTo(right.CityId));
            state.MapTopology = topology;
            CityResourceGenerator.Populate(state);
            return topology;
        }
    }
}
