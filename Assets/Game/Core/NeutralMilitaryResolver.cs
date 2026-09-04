using System;
using System.Collections.Generic;

namespace LittleCiv.Core
{
    public sealed class NeutralMilitaryResult
    {
        public readonly List<UnitPromotionResult> Promotions = new List<UnitPromotionResult>();
        public readonly List<UnitTrainingState> Trainings = new List<UnitTrainingState>();
        public readonly List<GameCommand> Movements = new List<GameCommand>();
    }

    public static class NeutralMilitaryResolver
    {
        public static NeutralMilitaryResult IssueOrders(GameState state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            var result = new NeutralMilitaryResult();
            var cities = NeutralCities(state);
            for (var cityIndex = 0; cityIndex < cities.Count; cityIndex++)
            {
                var city = cities[cityIndex];
                PromoteHomeUnits(state, city, result);
                StartNeededTraining(state, city, result);
                IssueDefensiveMovement(state, city, result);
            }
            return result;
        }

        private static void IssueDefensiveMovement(GameState state, CityState city, NeutralMilitaryResult result)
        {
            var government = state.Districts.Find(item => item.CityId == city.Id &&
                item.Type == DistrictType.Government);
            if (government == null) return;
            var governmentThreatened = government.ControllerId != city.OwnerId;
            var hostileTargets = state.Units.FindAll(item => item.OwnerId != city.OwnerId &&
                item.HitPoints > 0 && IsHostileToCity(state, city, item.OwnerId) &&
                state.Tiles.Exists(tile => tile.Id == item.TileId && tile.CityId == city.Id));
            hostileTargets.Sort((left, right) => left.Id.CompareTo(right.Id));
            var approachingHostiles = state.Units.FindAll(item => item.OwnerId != city.OwnerId &&
                item.HitPoints > 0 && IsHostileToCity(state, city, item.OwnerId) &&
                IsInOrAdjacentToCity(state, city, item.TileId));
            approachingHostiles.Sort((left, right) => left.Id.CompareTo(right.Id));
            var occupiedDistricts = state.Districts.FindAll(item => item.CityId == city.Id &&
                item.ControllerId != city.OwnerId);
            occupiedDistricts.Sort((left, right) =>
            {
                var governmentOrder = (left.Type == DistrictType.Government ? 0 : 1)
                    .CompareTo(right.Type == DistrictType.Government ? 0 : 1);
                return governmentOrder != 0 ? governmentOrder : left.Id.CompareTo(right.Id);
            });
            var units = state.Units.FindAll(item => item.OwnerId == city.OwnerId &&
                item.HomeCityId == city.Id && item.HitPoints > 0 && item.RemainingMovement > 0 &&
                item.CreatedTurn != state.TurnNumber);
            units.Sort((left, right) => left.Id.CompareTo(right.Id));
            var emergencyGuard = SelectEmergencyGuard(units, government.TileId);
            var needsEmergencyGuard = !governmentThreatened && emergencyGuard == null;
            for (var index = 0; index < units.Count; index++)
            {
                var unit = units[index];
                if (!governmentThreatened && emergencyGuard != null && unit.Id == emergencyGuard.Id)
                    continue;
                var outsideHome = !state.Tiles.Exists(tile => tile.Id == unit.TileId && tile.CityId == city.Id);
                EntityId target = default;
                if (governmentThreatened || outsideHome || needsEmergencyGuard)
                {
                    target = government.TileId;
                    needsEmergencyGuard = false;
                }
                else if (occupiedDistricts.Count > 0)
                {
                    target = ClosestDistrictTarget(state, unit, occupiedDistricts);
                }
                else if (hostileTargets.Count > 0)
                {
                    var hostile = ClosestHostile(state, unit, hostileTargets);
                    var attackPath = hostile == null ? new List<EntityId>() : FindPath(state, unit, hostile.TileId);
                    target = attackPath.Count > 0 && attackPath.Count <= unit.RemainingMovement
                        ? hostile.TileId
                        : ClosestDefensiveDistrict(state, city, hostile == null ? unit.TileId : hostile.TileId);
                }
                else if (approachingHostiles.Count > 0)
                {
                    target = ClosestDefensiveDistrict(state, city, approachingHostiles[0].TileId);
                }
                if (!target.IsValid || target == unit.TileId) continue;
                var path = FindPath(state, unit, target);
                if (path.Count == 0) continue;
                result.Movements.Add(new GameCommand
                {
                    CommandId = state.AllocateId(), PlayerId = city.OwnerId,
                    TurnNumber = state.TurnNumber, Type = GameCommandType.MoveUnit,
                    SubjectId = unit.Id, TargetId = target, SecondaryValue = 1, Path = path
                });
            }
        }

        private static UnitState SelectEmergencyGuard(List<UnitState> units, EntityId governmentTileId)
        {
            UnitState result = null;
            for (var index = 0; index < units.Count; index++)
            {
                var candidate = units[index];
                if (candidate.TileId != governmentTileId || UnitRules.IsSupply(candidate.Type)) continue;
                if (result == null || UnitRules.Attack(candidate.Type) > UnitRules.Attack(result.Type) ||
                    (UnitRules.Attack(candidate.Type) == UnitRules.Attack(result.Type) &&
                     candidate.HitPoints > result.HitPoints)) result = candidate;
            }
            return result;
        }

        private static EntityId ClosestDistrictTarget(GameState state, UnitState unit,
            List<DistrictState> districts)
        {
            DistrictState selected = null;
            var best = int.MaxValue;
            for (var index = 0; index < districts.Count; index++)
            {
                var path = FindPath(state, unit, districts[index].TileId);
                if (path.Count == 0 || path.Count >= best) continue;
                best = path.Count;
                selected = districts[index];
            }
            return selected == null ? default : selected.TileId;
        }

        private static UnitState ClosestHostile(GameState state, UnitState unit, List<UnitState> hostiles)
        {
            UnitState selected = null;
            var best = int.MaxValue;
            for (var index = 0; index < hostiles.Count; index++)
            {
                var path = FindPath(state, unit, hostiles[index].TileId);
                if (path.Count == 0 || path.Count >= best) continue;
                best = path.Count;
                selected = hostiles[index];
            }
            return selected;
        }

        private static EntityId ClosestDefensiveDistrict(GameState state, CityState city,
            EntityId hostileTileId)
        {
            var hostile = state.Tiles.Find(item => item.Id == hostileTileId);
            DistrictState selected = null;
            var best = int.MaxValue;
            for (var index = 0; index < state.Districts.Count; index++)
            {
                var district = state.Districts[index];
                if (district.CityId != city.Id || district.ControllerId != city.OwnerId ||
                    district.Type == DistrictType.Government || district.RemainingConstructionTurns > 0 ||
                    district.IsPillaged) continue;
                var tile = state.Tiles.Find(item => item.Id == district.TileId);
                if (tile == null || hostile == null) continue;
                var distance = HexCoord.Distance(new HexCoord(tile.Q, tile.R),
                    new HexCoord(hostile.Q, hostile.R));
                if (distance >= best) continue;
                best = distance;
                selected = district;
            }
            return selected == null
                ? state.Districts.Find(item => item.CityId == city.Id &&
                    item.Type == DistrictType.Government)?.TileId ?? default
                : selected.TileId;
        }

        private static bool IsHostileToCity(GameState state, CityState city, EntityId playerId)
        {
            var player = state.Players.Find(item => item.Id == playerId);
            return player != null && player.Slot != PlayerSlot.Neutral &&
                   NeutralCityRules.Favor(city, playerId) <= -3;
        }

        private static bool IsInOrAdjacentToCity(GameState state, CityState city, EntityId tileId)
        {
            var tile = state.Tiles.Find(item => item.Id == tileId);
            if (tile != null && tile.CityId == city.Id) return true;
            for (var index = 0; index < state.Tiles.Count; index++)
                if (state.Tiles[index].CityId == city.Id &&
                    MapTraversal.AreAdjacent(state, tileId, state.Tiles[index].Id)) return true;
            return false;
        }

        private static List<EntityId> FindPath(GameState state, UnitState unit, EntityId target)
        {
            var queue = new Queue<EntityId>();
            var previous = new Dictionary<EntityId, EntityId>();
            queue.Enqueue(unit.TileId);
            previous[unit.TileId] = default;
            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                if (current == target) break;
                for (var index = 0; index < state.Tiles.Count; index++)
                {
                    var next = state.Tiles[index].Id;
                    if (previous.ContainsKey(next) || !MapTraversal.AreAdjacent(state, current, next)) continue;
                    var enemy = state.Units.Exists(item => item.TileId == next &&
                        item.OwnerId != unit.OwnerId && item.HitPoints > 0);
                    if (enemy && next != target) continue;
                    previous[next] = current;
                    queue.Enqueue(next);
                }
            }
            if (!previous.ContainsKey(target)) return new List<EntityId>();
            var reversed = new List<EntityId>();
            for (var cursor = target; cursor != unit.TileId; cursor = previous[cursor]) reversed.Add(cursor);
            reversed.Reverse();
            return reversed;
        }

        public static int CombatTarget(NeutralCitySpecialization specialization)
        {
            if (specialization == NeutralCitySpecialization.Military) return 3;
            if (specialization == NeutralCitySpecialization.Commerce) return 2;
            return 1;
        }

        public static int SupplyTarget(NeutralCitySpecialization specialization)
        {
            return specialization == NeutralCitySpecialization.Military ||
                   specialization == NeutralCitySpecialization.Commerce ? 1 : 0;
        }

        private static void PromoteHomeUnits(GameState state, CityState city, NeutralMilitaryResult result)
        {
            var units = state.Units.FindAll(item => item.HomeCityId == city.Id && item.OwnerId == city.OwnerId);
            units.Sort((left, right) => left.Id.CompareTo(right.Id));
            for (var index = 0; index < units.Count; index++)
            {
                var target = NextPromotion(city, units[index].Type);
                if (!target.HasValue) continue;
                var command = new GameCommand
                {
                    CommandId = state.AllocateId(), PlayerId = city.OwnerId, TurnNumber = state.TurnNumber,
                    Type = GameCommandType.PromoteUnit, SubjectId = units[index].Id,
                    PrimaryValue = (int)target.Value
                };
                if (UnitPromotionResolver.TryPromote(state, command, out var promotion))
                    result.Promotions.Add(promotion);
            }
        }

        private static void StartNeededTraining(GameState state, CityState city, NeutralMilitaryResult result)
        {
            var combat = CountUnitsAndTraining(state, city, false);
            var supply = CountUnitsAndTraining(state, city, true);
            var combatNeeded = Math.Max(0, CombatTarget(city.NeutralSpecialization) - combat);
            var supplyNeeded = Math.Max(0, SupplyTarget(city.NeutralSpecialization) - supply);
            var districts = state.Districts.FindAll(item => item.CityId == city.Id &&
                item.Type == DistrictType.Military);
            districts.Sort((left, right) => left.Id.CompareTo(right.Id));
            for (var index = 0; index < districts.Count; index++)
            {
                if (state.UnitTrainings.Exists(item => item.DistrictId == districts[index].Id)) continue;
                UnitType? type = null;
                if (combatNeeded > 0)
                {
                    type = StrongestCombat(city);
                    combatNeeded--;
                }
                else if (supplyNeeded > 0)
                {
                    type = StrongestSupply(city);
                    supplyNeeded--;
                }
                if (!type.HasValue) break;
                if (!CanSustainTraining(state, city, type.Value))
                {
                    if (UnitRules.IsSupply(type.Value)) supplyNeeded++;
                    else combatNeeded++;
                    break;
                }
                var command = new GameCommand
                {
                    CommandId = state.AllocateId(), PlayerId = city.OwnerId, TurnNumber = state.TurnNumber,
                    Type = GameCommandType.StartTraining, SubjectId = districts[index].Id,
                    PrimaryValue = (int)type.Value
                };
                if (UnitTrainingResolver.TryStart(state, command, out var training))
                    result.Trainings.Add(training);
                else if (UnitRules.IsSupply(type.Value)) supplyNeeded++;
                else combatNeeded++;
            }
        }

        public static bool CanSustainTraining(GameState state, CityState city, UnitType type)
        {
            if (state == null || city == null) return false;
            var breakdown = CityEconomyResolver.CalculateBreakdown(state, city);
            var pendingFood = 0;
            var pendingUpkeep = 0;
            for (var index = 0; index < state.UnitTrainings.Count; index++)
            {
                var district = state.Districts.Find(item => item.Id == state.UnitTrainings[index].DistrictId);
                if (district == null || district.CityId != city.Id) continue;
                pendingFood += UnitRules.FoodConsumption(state.UnitTrainings[index].Type);
                pendingUpkeep += MaintenanceResolver.UnitUpkeep(state.UnitTrainings[index].Type);
            }
            var projectedFood = breakdown.Food.Total - city.Population -
                                breakdown.UnitFoodConsumption - pendingFood -
                                UnitRules.FoodConsumption(type);
            var projectedGold = breakdown.Gold.Total - breakdown.UnitUpkeep -
                                breakdown.FacilityUpkeep - pendingUpkeep -
                                MaintenanceResolver.UnitUpkeep(type);
            var foodSafe = projectedFood >= 0 || city.StoredFood >= -projectedFood * 4;
            var goldReserveAfterTraining = city.Gold - UnitRules.TrainingGold(type);
            var goldSafe = projectedGold >= 0 || goldReserveAfterTraining >= -projectedGold * 8;
            return foodSafe && goldSafe;
        }

        private static UnitType? NextPromotion(CityState city, UnitType type)
        {
            if (type == UnitType.Militia && NeutralResearchResolver.HasResearch(city, ResearchType.IronWorking))
                return UnitType.IronInfantry;
            if (type == UnitType.IronInfantry && NeutralResearchResolver.HasResearch(city, ResearchType.Gunpowder))
                return UnitType.GunpowderInfantry;
            if (type == UnitType.GunpowderInfantry && NeutralResearchResolver.HasResearch(city, ResearchType.Vehicles))
                return UnitType.MechanizedInfantry;
            if (type == UnitType.Supply && NeutralResearchResolver.HasResearch(city, ResearchType.Vehicles))
                return UnitType.MotorizedSupply;
            return null;
        }

        private static UnitType StrongestCombat(CityState city)
        {
            if (NeutralResearchResolver.HasResearch(city, ResearchType.Vehicles)) return UnitType.MechanizedInfantry;
            if (NeutralResearchResolver.HasResearch(city, ResearchType.Gunpowder)) return UnitType.GunpowderInfantry;
            if (NeutralResearchResolver.HasResearch(city, ResearchType.IronWorking)) return UnitType.IronInfantry;
            return UnitType.Militia;
        }

        private static UnitType StrongestSupply(CityState city) =>
            NeutralResearchResolver.HasResearch(city, ResearchType.Vehicles)
                ? UnitType.MotorizedSupply : UnitType.Supply;

        private static int CountUnitsAndTraining(GameState state, CityState city, bool supply)
        {
            var count = state.Units.FindAll(item => item.HomeCityId == city.Id &&
                UnitRules.IsSupply(item.Type) == supply).Count;
            for (var index = 0; index < state.UnitTrainings.Count; index++)
            {
                var training = state.UnitTrainings[index];
                var district = state.Districts.Find(item => item.Id == training.DistrictId);
                if (district != null && district.CityId == city.Id &&
                    UnitRules.IsSupply(training.Type) == supply) count++;
            }
            return count;
        }

        private static List<CityState> NeutralCities(GameState state)
        {
            var result = state.Cities.FindAll(city =>
            {
                var owner = state.Players.Find(item => item.Id == city.OwnerId);
                return owner != null && owner.Slot == PlayerSlot.Neutral;
            });
            result.Sort((left, right) => left.Id.CompareTo(right.Id));
            return result;
        }
    }
}
