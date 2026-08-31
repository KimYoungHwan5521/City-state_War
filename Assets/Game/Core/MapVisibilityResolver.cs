using System;
using System.Collections.Generic;

namespace LittleCiv.Core
{
    public static class MapVisibilityResolver
    {
        public static List<EntityId> ResolveCitiesForTile(
            GameState state,
            EntityId tileId,
            EntityId fallbackCityId)
        {
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            var tile = state.Tiles.Find(item => item.Id == tileId);
            if (tile == null)
            {
                throw new ArgumentException("The selected tile is not part of the game state.", nameof(tileId));
            }

            var result = tile.VisibleCityIds == null
                ? new List<EntityId>()
                : new List<EntityId>(tile.VisibleCityIds);
            if (result.Count == 0 && fallbackCityId.IsValid)
            {
                result.Add(fallbackCityId);
            }

            result.Sort();
            return result;
        }
    }
}
