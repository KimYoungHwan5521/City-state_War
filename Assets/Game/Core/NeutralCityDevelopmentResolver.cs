using System;
using System.Collections.Generic;

namespace LittleCiv.Core
{
    public sealed class NeutralDistrictStartRecord
    {
        public EntityId CityId;
        public EntityId DistrictId;
        public DistrictType Type;
        public int RemainingTurns;
    }

    public static class NeutralCityDevelopmentResolver
    {
        private static readonly DistrictType[] InitialDistricts =
        {
            DistrictType.Agriculture, DistrictType.Military, DistrictType.Commerce
        };

        public static List<NeutralDistrictStartRecord> StartAvailableConstruction(GameState state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            var result = new List<NeutralDistrictStartRecord>();
            var cities = new List<CityState>();
            for (var index = 0; index < state.Cities.Count; index++)
            {
                var owner = state.Players.Find(item => item.Id == state.Cities[index].OwnerId);
                if (owner != null && owner.Slot == PlayerSlot.Neutral) cities.Add(state.Cities[index]);
            }
            cities.Sort((left, right) => left.Id.CompareTo(right.Id));
            for (var cityIndex = 0; cityIndex < cities.Count; cityIndex++)
            {
                var city = cities[cityIndex];
                if (state.TurnNumber == 1)
                {
                    for (var initialIndex = 0; initialIndex < InitialDistricts.Length; initialIndex++)
                        TryStartType(state, city, InitialDistricts[initialIndex], result);
                    continue;
                }
                while (DistrictConstructionResolver.CountFreeCitizens(state, city) > 0)
                {
                    var next = NextDistrictType(state, city);
                    if (next == DistrictType.Government || !TryStartType(state, city, next, result)) break;
                }
            }
            return result;
        }

        public static List<EntityId> StartAvailableRepairs(GameState state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            var result = new List<EntityId>();
            var districts = new List<DistrictState>(state.Districts);
            districts.Sort((left, right) => left.Id.CompareTo(right.Id));
            for (var index = 0; index < districts.Count; index++)
            {
                var district = districts[index];
                var city = state.Cities.Find(item => item.Id == district.CityId);
                var owner = city == null ? null : state.Players.Find(item => item.Id == city.OwnerId);
                if (owner == null || owner.Slot != PlayerSlot.Neutral || !district.IsPillaged ||
                    district.RemainingRepairTurns > 0 || district.ControllerId != city.OwnerId) continue;
                var command = new GameCommand
                {
                    CommandId = state.AllocateId(), PlayerId = city.OwnerId,
                    TurnNumber = state.TurnNumber, Type = GameCommandType.RepairDistrict,
                    SubjectId = district.Id
                };
                if (DistrictConstructionResolver.TryStartRepair(state, command, out var repairing))
                    result.Add(repairing.Id);
            }
            return result;
        }

        public static DistrictType NextDistrictType(GameState state, CityState city)
        {
            if (!HasDistrict(state, city.Id, DistrictType.Science)) return DistrictType.Science;
            if (!HasDistrict(state, city.Id, DistrictType.Culture)) return DistrictType.Culture;
            var specialization = NeutralCityRules.DistrictTypeFor(city.NeutralSpecialization);
            return CountDistricts(state, city.Id, specialization) < 3 ? specialization : DistrictType.Government;
        }

        private static bool TryStartType(GameState state, CityState city, DistrictType type,
            List<NeutralDistrictStartRecord> result)
        {
            if (DistrictConstructionResolver.CountFreeCitizens(state, city) <= 0 ||
                type == DistrictType.Government) return false;
            if (!IsUnlocked(city, type)) return false;
            var tile = SelectTile(state, city, type);
            if (!tile.IsValid) return false;
            var command = new GameCommand
            {
                CommandId = state.AllocateId(), PlayerId = city.OwnerId, TurnNumber = state.TurnNumber,
                Type = GameCommandType.StartDistrict, SubjectId = city.Id, TargetId = tile,
                PrimaryValue = (int)type
            };
            if (!DistrictConstructionResolver.TryStart(state, command, out var district)) return false;
            result.Add(new NeutralDistrictStartRecord
            {
                CityId = city.Id, DistrictId = district.Id, Type = type,
                RemainingTurns = district.RemainingConstructionTurns
            });
            return true;
        }

        private static EntityId SelectTile(GameState state, CityState city, DistrictType type)
        {
            var view = state.MapTopology?.FindView(city.Id);
            if (view?.Tiles == null) return default;
            CityTilePlacement selected = null;
            var desiredResource = ResourceFor(type);
            for (var index = 0; index < view.Tiles.Count; index++)
            {
                var placement = view.Tiles[index];
                if (!placement.IsBuildable || state.Districts.Exists(item => item.TileId == placement.TileId))
                    continue;
                var tile = state.Tiles.Find(item => item.Id == placement.TileId);
                if (tile == null) continue;
                if (selected == null || BetterTile(state, city, type, placement, selected, desiredResource))
                    selected = placement;
            }
            return selected == null ? default : selected.TileId;
        }

        private static bool BetterTile(GameState state, CityState city, DistrictType type,
            CityTilePlacement candidate, CityTilePlacement current, TileResourceType desired)
        {
            var candidateTile = state.Tiles.Find(item => item.Id == candidate.TileId);
            var currentTile = state.Tiles.Find(item => item.Id == current.TileId);
            var candidateMatches = desired != TileResourceType.None && candidateTile.ResourceType == desired;
            var currentMatches = desired != TileResourceType.None && currentTile.ResourceType == desired;
            if (type == DistrictType.Commerce || type == DistrictType.Science || type == DistrictType.Culture)
            {
                var candidateAdjacent = SameTypeNeighbors(state, city, type, candidate);
                var currentAdjacent = SameTypeNeighbors(state, city, type, current);
                if (candidateAdjacent != currentAdjacent) return candidateAdjacent > currentAdjacent;
            }
            if (candidateMatches != currentMatches) return candidateMatches;
            if (desired == TileResourceType.None)
            {
                var candidateEmpty = candidateTile.ResourceType == TileResourceType.None;
                var currentEmpty = currentTile.ResourceType == TileResourceType.None;
                if (candidateEmpty != currentEmpty) return candidateEmpty;
            }
            var candidateDistance = HexCoord.Distance(new HexCoord(0, 0),
                new HexCoord(candidate.LocalQ, candidate.LocalR));
            var currentDistance = HexCoord.Distance(new HexCoord(0, 0),
                new HexCoord(current.LocalQ, current.LocalR));
            return candidateDistance != currentDistance ? candidateDistance < currentDistance :
                candidate.TileId.CompareTo(current.TileId) < 0;
        }

        private static int SameTypeNeighbors(GameState state, CityState city, DistrictType type,
            CityTilePlacement placement)
        {
            var view = state.MapTopology?.FindView(city.Id);
            if (view?.Tiles == null) return 0;
            var origin = new HexCoord(placement.LocalQ, placement.LocalR);
            var count = 0;
            for (var index = 0; index < state.Districts.Count; index++)
            {
                var district = state.Districts[index];
                if (district.CityId != city.Id || district.Type != type) continue;
                var other = view.Tiles.Find(item => item.TileId == district.TileId);
                if (other != null && HexCoord.Distance(origin,
                        new HexCoord(other.LocalQ, other.LocalR)) == 1) count++;
            }
            return count;
        }

        private static TileResourceType ResourceFor(DistrictType type)
        {
            switch (type)
            {
                case DistrictType.Agriculture: return TileResourceType.Food;
                case DistrictType.Commerce: return TileResourceType.Commerce;
                case DistrictType.Science: return TileResourceType.Science;
                case DistrictType.Culture: return TileResourceType.Culture;
                default: return TileResourceType.None;
            }
        }

        private static bool HasDistrict(GameState state, EntityId cityId, DistrictType type) =>
            state.Districts.Exists(item => item.CityId == cityId && item.Type == type);

        private static int CountDistricts(GameState state, EntityId cityId, DistrictType type) =>
            state.Districts.FindAll(item => item.CityId == cityId && item.Type == type).Count;

        private static bool IsUnlocked(CityState city, DistrictType type)
        {
            switch (type)
            {
                case DistrictType.Agriculture:
                case DistrictType.Commerce:
                case DistrictType.Military: return true;
                case DistrictType.Science: return NeutralResearchResolver.HasResearch(city, ResearchType.School);
                case DistrictType.Culture: return NeutralResearchResolver.HasResearch(city, ResearchType.Arts);
                default: return false;
            }
        }
    }
}
