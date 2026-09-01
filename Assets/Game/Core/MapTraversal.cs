using System;

namespace LittleCiv.Core
{
    public static class MapTraversal
    {
        public static bool AreAdjacent(GameState state, EntityId leftTileId, EntityId rightTileId)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (leftTileId == rightTileId) return false;
            var left = GlobalCoordinate(state, leftTileId);
            var right = GlobalCoordinate(state, rightTileId);
            return left.HasValue && right.HasValue && HexCoord.Distance(left.Value, right.Value) == 1;
        }

        public static HexCoord? GlobalCoordinate(GameState state, EntityId tileId)
        {
            var tile = state.Tiles.Find(item => item.Id == tileId);
            if (tile == null) return null;
            var city = state.Cities.Find(item => item.Id == tile.CityId);
            if (city == null)
            {
                for (var viewIndex = 0; viewIndex < state.MapTopology.CityViews.Count; viewIndex++)
                {
                    var placement = state.MapTopology.CityViews[viewIndex].Tiles.Find(item => item.TileId == tileId);
                    if (placement != null) return new HexCoord(placement.LocalQ, placement.LocalR);
                }
                return null;
            }
            var center = WorldMapGenerator.CityCenterCoordinate(city.WorldQ, city.WorldR);
            return new HexCoord(center.Q + tile.Q, center.R + tile.R);
        }
    }
}
