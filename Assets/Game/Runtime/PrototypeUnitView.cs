using LittleCiv.Core;
using UnityEngine;
using GameEntityId = LittleCiv.Core.EntityId;

namespace LittleCiv.Runtime
{
    public sealed class PrototypeUnitView : MonoBehaviour
    {
        private PrototypeMapPresenter presenter;
        private GameEntityId tileId;

        public void Initialize(PrototypeMapPresenter owner, UnitState unit)
        {
            presenter = owner;
            tileId = unit.TileId;
        }

        private void OnMouseDown()
        {
            presenter.SelectTile(tileId);
        }
    }
}
