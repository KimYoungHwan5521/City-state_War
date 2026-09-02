namespace LittleCiv.Core
{
    public static class AgricultureCitizenResolver
    {
        public static bool TryAssign(GameState state, GameCommand command)
        {
            if (state == null || command == null || command.Type != GameCommandType.AssignCitizen)
                return false;
            var district = state.Districts.Find(item => item.Id == command.SubjectId);
            if (district == null || district.Type != DistrictType.Agriculture ||
                district.RemainingConstructionTurns > 0 || !district.IsOperational ||
                district.ControllerId != command.PlayerId || command.PrimaryValue < 1 ||
                command.PrimaryValue > 2)
                return false;
            var city = state.Cities.Find(item => item.Id == district.CityId);
            var player = state.Players.Find(item => item.Id == command.PlayerId);
            if (city == null || player == null || city.OwnerId != command.PlayerId) return false;
            if (command.PrimaryValue == 2)
            {
                if (!HasResearch(player, ResearchType.Irrigation) ||
                    HasResearch(player, ResearchType.MechanizedAgriculture)) return false;
                var additional = 2 - district.AssignedCitizens;
                if (additional <= 0 || DistrictConstructionResolver.CountFreeCitizens(state, city) < additional)
                    return false;
            }
            else if (district.AssignedCitizens <= 1) return false;
            district.AssignedCitizens = command.PrimaryValue;
            return true;
        }

        private static bool HasResearch(PlayerState player, ResearchType type)
        {
            return player.CompletedResearch != null && player.CompletedResearch.Contains(type);
        }
    }
}
