using System;

namespace LittleCiv.Core
{
    public static class MapTraversal
    {
        public static bool AreAdjacent(GameState state, EntityId leftTileId, EntityId rightTileId)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (leftTileId == rightTileId) return false;
            var topology = state.MapTopology;
            if (topology == null || topology.CityViews == null) return false;

            for (var i = 0; i < topology.CityViews.Count; i++)
            {
                var view = topology.CityViews[i];
                CityTilePlacement left = null;
                CityTilePlacement right = null;
                for (var j = 0; j < view.Tiles.Count; j++)
                {
                    var placement = view.Tiles[j];
                    if (placement.TileId == leftTileId) left = placement;
                    if (placement.TileId == rightTileId) right = placement;
                }

                if (left != null && right != null &&
                    HexCoord.Distance(
                        new HexCoord(left.LocalQ, left.LocalR),
                        new HexCoord(right.LocalQ, right.LocalR)) == 1)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
