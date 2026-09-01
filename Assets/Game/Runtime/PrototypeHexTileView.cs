using LittleCiv.Core;
using UnityEngine;
using GameEntityId = LittleCiv.Core.EntityId;

namespace LittleCiv.Runtime
{
    public sealed class PrototypeHexTileView : MonoBehaviour
    {
        private GameEntityId tileId;

        public GameEntityId TileId => tileId;

        public void Initialize(PrototypeMapPresenter owner, GameEntityId id)
        {
            tileId = id;
        }

    }
}
