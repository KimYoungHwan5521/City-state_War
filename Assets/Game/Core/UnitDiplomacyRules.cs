namespace LittleCiv.Core
{
    public static class UnitDiplomacyRules
    {
        public static bool AreHostile(GameState state, EntityId firstOwnerId,
            EntityId secondOwnerId, EntityId tileId)
        {
            if (firstOwnerId == secondOwnerId) return false;
            if (state == null) return true;
            var tile = state.Tiles.Find(item => item.Id == tileId);
            var city = tile == null ? null : state.Cities.Find(item => item.Id == tile.CityId);
            if (city == null || !city.CultureSubjectToId.IsValid) return true;
            var cityOwner = state.Players.Find(item => item.Id == city.OwnerId);
            if (cityOwner == null || cityOwner.Slot != PlayerSlot.Neutral) return true;
            return !((firstOwnerId == city.OwnerId && secondOwnerId == city.CultureSubjectToId) ||
                     (secondOwnerId == city.OwnerId && firstOwnerId == city.CultureSubjectToId));
        }

        public static bool IsFriendlyCultureGarrison(GameState state, EntityId playerId,
            EntityId tileId)
        {
            if (state == null) return false;
            var tile = state.Tiles.Find(item => item.Id == tileId);
            var city = tile == null ? null : state.Cities.Find(item => item.Id == tile.CityId);
            if (city == null || city.CultureSubjectToId != playerId) return false;
            var owner = state.Players.Find(item => item.Id == city.OwnerId);
            return owner != null && owner.Slot == PlayerSlot.Neutral;
        }
    }
}
