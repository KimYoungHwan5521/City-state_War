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
                TryEmergencyRebalance(state, city, result);
                while (DistrictConstructionResolver.CountFreeCitizens(state, city) > 0)
                {
                    if (TryRestoreUnstaffedDistrict(state, city)) continue;
                    var next = NextDistrictType(state, city);
                    var stage = NeutralCityRules.DevelopmentStage(state, city);
                    var threatened = NeutralMilitaryResolver.ForceTarget(state, city).IsThreatened;
                    if (stage == NeutralDevelopmentStage.Late && !threatened &&
                        DistrictConstructionResolver.CountFreeCitizens(state, city) <= 1 &&
                        next != DistrictType.Agriculture && next != DistrictType.Commerce &&
                        next != DistrictType.NuclearFacility) break;
                    var upkeep = MaintenanceResolver.DistrictUpkeep(next);
                    if (upkeep > 0)
                    {
                        var candidate = NeutralEconomyPlanner.Evaluate(state, city,
                            additionalGoldUpkeep: upkeep);
                        if (!candidate.IsSafe)
                        {
                            if (candidate.FoodNet < NeutralEconomyPlanner.MinimumNet)
                                next = DistrictType.Agriculture;
                            else if (candidate.GoldNet < NeutralEconomyPlanner.MinimumNet)
                                next = DistrictType.Commerce;
                            else break;
                        }
                    }
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
            var projection = NeutralEconomyPlanner.Evaluate(state, city);
            if (projection.FoodNet < NeutralEconomyPlanner.MinimumNet) return DistrictType.Agriculture;
            if (projection.GoldNet < NeutralEconomyPlanner.MinimumNet) return DistrictType.Commerce;
            var militarySupport = NeutralMilitaryResolver.SupportNeededForNextForceAction(state, city);
            if (militarySupport.HasValue) return militarySupport.Value;
            if (!HasDistrict(state, city.Id, DistrictType.Science)) return DistrictType.Science;
            if (!HasDistrict(state, city.Id, DistrictType.Culture)) return DistrictType.Culture;
            if (NeutralResearchResolver.HasResearch(city, ResearchType.NuclearFission) &&
                !HasDistrict(state, city.Id, DistrictType.NuclearFacility))
                return DistrictType.NuclearFacility;
            var specialization = NeutralCityRules.DistrictTypeFor(city.NeutralSpecialization);
            var forceTarget = NeutralMilitaryResolver.ForceTarget(state, city);
            var stage = NeutralCityRules.DevelopmentStage(state, city);
            var developmentMilitaryGoal = stage == NeutralDevelopmentStage.Early ? 2 : 3;
            var militaryGoal = city.NeutralSpecialization == NeutralCitySpecialization.Military
                ? Math.Max(developmentMilitaryGoal,
                    DivideRoundUp(forceTarget.Combat + forceTarget.Supply, 2))
                : city.NeutralSpecialization == NeutralCitySpecialization.Commerce
                    ? DivideRoundUp(forceTarget.Combat, 2)
                    : 1;
            var militaryCount = CountDistricts(state, city.Id, DistrictType.Military);
            if (militaryCount < militaryGoal) return DistrictType.Military;
            if (city.NeutralSpecialization == NeutralCitySpecialization.Military)
                return DistrictType.Government;
            return specialization;
        }

        private static int DivideRoundUp(int value, int divisor) =>
            value <= 0 ? 0 : (value + divisor - 1) / divisor;

        private static bool TryEmergencyRebalance(GameState state, CityState city,
            List<NeutralDistrictStartRecord> result)
        {
            if (DistrictConstructionResolver.CountFreeCitizens(state, city) > 0) return false;
            var projection = NeutralEconomyPlanner.Evaluate(state, city);
            var support = projection.FoodNet < NeutralEconomyPlanner.MinimumNet
                ? DistrictType.Agriculture
                : projection.GoldNet < NeutralEconomyPlanner.MinimumNet
                    ? DistrictType.Commerce
                    : DistrictType.Government;
            if (support == DistrictType.Government || !SelectTile(state, city, support).IsValid) return false;
            var candidates = state.Districts.FindAll(item => item.CityId == city.Id &&
                item.Type != DistrictType.Government && item.Type != support &&
                item.ControllerId == city.OwnerId && item.AssignedCitizens > 0);
            candidates.Sort((left, right) =>
            {
                var construction = (left.RemainingConstructionTurns > 0 ? 0 : 1)
                    .CompareTo(right.RemainingConstructionTurns > 0 ? 0 : 1);
                if (construction != 0) return construction;
                var priority = EmergencyRecallPriority(left.Type).CompareTo(EmergencyRecallPriority(right.Type));
                return priority != 0 ? priority : left.Id.CompareTo(right.Id);
            });
            if (candidates.Count == 0) return false;
            var recalled = candidates[0];
            recalled.AssignedCitizens--;
            if (recalled.RemainingConstructionTurns <= 0 && recalled.AssignedCitizens <= 0)
                recalled.IsOperational = false;
            city.CitizenAutoAssignment = false;
            return TryStartType(state, city, support, result);
        }

        private static bool TryRestoreUnstaffedDistrict(GameState state, CityState city)
        {
            if (city.CitizenAutoAssignment || DistrictConstructionResolver.CountFreeCitizens(state, city) <= 0)
                return false;
            var candidates = state.Districts.FindAll(item => item.CityId == city.Id &&
                item.Type != DistrictType.Government && item.ControllerId == city.OwnerId &&
                item.AssignedCitizens <= 0 && !item.IsPillaged && item.RemainingRepairTurns <= 0);
            candidates.Sort((left, right) =>
            {
                var construction = (left.RemainingConstructionTurns > 0 ? 0 : 1)
                    .CompareTo(right.RemainingConstructionTurns > 0 ? 0 : 1);
                if (construction != 0) return construction;
                var specialization = NeutralCityRules.DistrictTypeFor(city.NeutralSpecialization);
                var specialized = (left.Type == specialization ? 0 : 1)
                    .CompareTo(right.Type == specialization ? 0 : 1);
                return specialized != 0 ? specialized : left.Id.CompareTo(right.Id);
            });
            for (var index = 0; index < candidates.Count; index++)
            {
                var district = candidates[index];
                var previousOperational = district.IsOperational;
                district.AssignedCitizens = 1;
                if (district.RemainingConstructionTurns <= 0) district.IsOperational = true;
                var safe = NeutralEconomyPlanner.Evaluate(state, city).IsSafe;
                if (safe) return true;
                district.AssignedCitizens = 0;
                district.IsOperational = previousOperational;
            }
            return false;
        }

        private static int EmergencyRecallPriority(DistrictType type)
        {
            switch (type)
            {
                case DistrictType.Culture: return 0;
                case DistrictType.Science: return 1;
                case DistrictType.Commerce: return 2;
                case DistrictType.Military: return 3;
                case DistrictType.Agriculture: return 4;
                default: return 5;
            }
        }

        private static bool TryStartType(GameState state, CityState city, DistrictType type,
            List<NeutralDistrictStartRecord> result)
        {
            if (DistrictConstructionResolver.CountFreeCitizens(state, city) <= 0 ||
                type == DistrictType.Government) return false;
            if (!IsUnlocked(city, type)) return false;
            var upkeep = MaintenanceResolver.DistrictUpkeep(type);
            if (state.TurnNumber > 1 && upkeep > 0 &&
                !NeutralEconomyPlanner.Evaluate(state, city,
                    additionalGoldUpkeep: upkeep).IsSafe) return false;
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
                case DistrictType.NuclearFacility:
                    return NeutralResearchResolver.HasResearch(city, ResearchType.NuclearFission);
                default: return false;
            }
        }
    }

    public sealed class NeutralEconomyProjection
    {
        public int FoodProduction;
        public int GoldProduction;
        public int FoodExpenses;
        public int GoldExpenses;
        public int FoodNet;
        public int GoldNet;
        public int FoodReserveRequired;
        public int GoldReserveRequired;
        public int GoldAfterImmediateCosts;
        public bool IsSafe;
    }

    public static class NeutralEconomyPlanner
    {
        public const int MinimumNet = 1;
        public const int ReserveTurns = 2;

        public static NeutralEconomyProjection Evaluate(GameState state, CityState city,
            int additionalFoodConsumption = 0, int additionalGoldUpkeep = 0,
            int immediateGoldCost = 0)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (city == null) throw new ArgumentNullException(nameof(city));
            var economy = CityEconomyResolver.CalculateBreakdown(state, city);
            var pendingFood = 0;
            var pendingGold = 0;
            for (var index = 0; index < state.UnitTrainings.Count; index++)
            {
                var training = state.UnitTrainings[index];
                var district = state.Districts.Find(item => item.Id == training.DistrictId);
                if (district == null || district.CityId != city.Id) continue;
                pendingFood += UnitRules.FoodConsumption(training.Type);
                pendingGold += MaintenanceResolver.UnitUpkeep(training.Type);
            }
            var returningFood = 0;
            var returningGold = 0;
            for (var levyIndex = 0; levyIndex < state.Levies.Count; levyIndex++)
            {
                var levy = state.Levies[levyIndex];
                if (levy.MilitaryCityId != city.Id) continue;
                for (var unitIndex = 0; unitIndex < levy.Units.Count; unitIndex++)
                {
                    var unit = state.Units.Find(item => item.Id == levy.Units[unitIndex].UnitId &&
                        item.HitPoints > 0);
                    if (unit == null) continue;
                    returningFood += UnitRules.FoodConsumption(unit.Type);
                    returningGold += MaintenanceResolver.UnitUpkeep(unit.Type);
                }
            }
            var modernUpkeep = 0;
            var pendingDistrictUpkeep = 0;
            for (var index = 0; index < state.Districts.Count; index++)
            {
                var district = state.Districts[index];
                if (district.CityId == city.Id && district.ControllerId == city.OwnerId &&
                    district.AssignedCitizens > 0 && district.RemainingConstructionTurns > 0)
                    pendingDistrictUpkeep += MaintenanceResolver.DistrictUpkeep(district.Type);
            }
            for (var index = 0; index < state.DefenseFacilities.Count; index++)
            {
                var defense = state.DefenseFacilities[index];
                if (defense.CityId == city.Id &&
                    (defense.Type == DefenseFacilityType.ModernDefense ||
                     defense.BuildingType == DefenseFacilityType.ModernDefense))
                    modernUpkeep += DefenseFacilityResolver.ModernUpkeep;
            }
            var foodProduction = economy.Food.Total + PendingSupportYield(state, city,
                DistrictType.Agriculture);
            var goldProduction = economy.Gold.Total + PendingSupportYield(state, city,
                DistrictType.Commerce);
            var foodExpenses = city.Population + economy.UnitFoodConsumption + pendingFood +
                               returningFood + additionalFoodConsumption;
            var goldExpenses = economy.UnitUpkeep + economy.FacilityUpkeep + pendingDistrictUpkeep + pendingGold +
                               returningGold + modernUpkeep + additionalGoldUpkeep;
            var result = new NeutralEconomyProjection
            {
                FoodProduction = foodProduction,
                GoldProduction = goldProduction,
                FoodExpenses = foodExpenses,
                GoldExpenses = goldExpenses,
                FoodNet = foodProduction - foodExpenses,
                GoldNet = goldProduction - goldExpenses,
                FoodReserveRequired = foodExpenses * ReserveTurns,
                GoldReserveRequired = goldExpenses * ReserveTurns,
                GoldAfterImmediateCosts = city.Gold - immediateGoldCost
            };
            result.IsSafe = result.FoodNet >= MinimumNet && result.GoldNet >= MinimumNet &&
                            city.StoredFood >= result.FoodReserveRequired &&
                            result.GoldAfterImmediateCosts >= result.GoldReserveRequired;
            return result;
        }

        private static int PendingSupportYield(GameState state, CityState city, DistrictType type)
        {
            var total = 0;
            for (var index = 0; index < state.Districts.Count; index++)
            {
                var district = state.Districts[index];
                if (district.CityId != city.Id || district.Type != type ||
                    district.RemainingConstructionTurns <= 0 || district.AssignedCitizens <= 0) continue;
                var tile = state.Tiles.Find(item => item.Id == district.TileId);
                if (type == DistrictType.Agriculture)
                {
                    total += CityEconomyResolver.AgricultureFood;
                    if (tile != null && tile.ResourceType == TileResourceType.Food)
                        total += CityEconomyResolver.AgricultureResourceBonus;
                    if (NeutralResearchResolver.HasResearch(city, ResearchType.Fertilizer)) total++;
                }
                else if (type == DistrictType.Commerce)
                {
                    total += CityEconomyResolver.CommerceGold;
                    if (tile != null && tile.ResourceType == TileResourceType.Commerce)
                        total += CityEconomyResolver.CommerceResourceBonus;
                    if (NeutralResearchResolver.HasResearch(city, ResearchType.Currency)) total++;
                }
            }
            return total;
        }
    }
}
