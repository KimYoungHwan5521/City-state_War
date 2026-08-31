using System;
using System.Collections.Generic;

namespace LittleCiv.Core
{
    [Serializable]
    public sealed class CityMapView
    {
        public EntityId CityId;
        public List<CityTilePlacement> Tiles = new List<CityTilePlacement>();
    }

    [Serializable]
    public sealed class CityTilePlacement
    {
        public EntityId TileId;
        public int LocalQ;
        public int LocalR;
        public bool IsBuildable;
    }

    [Serializable]
    public sealed class WorldMapTopology
    {
        public List<CityMapView> CityViews = new List<CityMapView>();

        public CityMapView FindView(EntityId cityId)
        {
            return CityViews.Find(view => view.CityId == cityId);
        }
    }
}
