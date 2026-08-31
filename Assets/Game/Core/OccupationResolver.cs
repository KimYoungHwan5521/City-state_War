using System;

namespace LittleCiv.Core
{
    public sealed class OccupationResult
    {
        public EntityId OccupyingPlayerId;
        public EntityId TileId;
        public EntityId DistrictId;
        public DistrictType DistrictType;
        public bool DistrictOccupied;
        public bool ConquestVictoryTriggered;
    }

    public static class OccupationResolver
    {
        public static OccupationResult Resolve(
            GameState state,
            EntityId occupyingPlayerId,
            EntityId tileId)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            var result = new OccupationResult
            {
                OccupyingPlayerId = occupyingPlayerId,
                TileId = tileId
            };
            var district = FindDistrict(state, tileId);
            if (district == null || district.ControllerId == occupyingPlayerId) return result;
            if (HasEnemyUnit(state, tileId, occupyingPlayerId)) return result;

            district.ControllerId = occupyingPlayerId;
            district.IsOperational = false;
            var tile = FindTile(state, tileId);
            if (tile != null) tile.ControllerId = occupyingPlayerId;
            result.DistrictId = district.Id;
            result.DistrictType = district.Type;
            result.DistrictOccupied = true;
            if (district.Type == DistrictType.Government && !state.IsGameOver)
            {
                state.Victory = VictoryType.Conquest;
                state.WinnerId = occupyingPlayerId;
                result.ConquestVictoryTriggered = true;
            }
            return result;
        }

        private static DistrictState FindDistrict(GameState state, EntityId tileId)
        {
            for (var i = 0; i < state.Districts.Count; i++)
            {
                if (state.Districts[i].TileId == tileId) return state.Districts[i];
            }
            return null;
        }

        private static TileState FindTile(GameState state, EntityId tileId)
        {
            for (var i = 0; i < state.Tiles.Count; i++)
            {
                if (state.Tiles[i].Id == tileId) return state.Tiles[i];
            }
            return null;
        }

        private static bool HasEnemyUnit(GameState state, EntityId tileId, EntityId playerId)
        {
            for (var i = 0; i < state.Units.Count; i++)
            {
                var unit = state.Units[i];
                if (unit.TileId == tileId && unit.OwnerId != playerId && unit.HitPoints > 0) return true;
            }
            return false;
        }
    }
}
