using LittleCiv.Core;
using UnityEngine;
using GameEntityId = LittleCiv.Core.EntityId;

namespace LittleCiv.Runtime
{
    public sealed class PrototypeHexTileView : MonoBehaviour
    {
        private PrototypeMapPresenter presenter;
        private GameEntityId tileId;

        public void Initialize(PrototypeMapPresenter owner, GameEntityId id)
        {
            presenter = owner;
            tileId = id;
        }

        private void OnMouseDown()
        {
            presenter.SelectTile(tileId);
        }
    }
}
