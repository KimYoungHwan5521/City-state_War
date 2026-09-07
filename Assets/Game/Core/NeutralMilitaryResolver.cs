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

    public sealed class NeutralForceTarget
    {
        public int Combat;
        public int Supply;
        public bool IsThreatened;
        public bool IsCriticalThreat;
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

        public static int CombatTarget(NeutralCitySpecialization specialization,
            NeutralDevelopmentStage stage)
        {
            if (specialization == NeutralCitySpecialization.Military)
                return stage == NeutralDevelopmentStage.Late ? 6 :
                    stage == NeutralDevelopmentStage.Middle ? 3 : 2;
            if (specialization == NeutralCitySpecialization.Commerce)
                return stage == NeutralDevelopmentStage.Late ? 4 :
                    stage == NeutralDevelopmentStage.Middle ? 3 : 2;
            return stage == NeutralDevelopmentStage.Late ? 3 :
                stage == NeutralDevelopmentStage.Middle ? 2 : 1;
        }

        public static int SupplyTarget(NeutralCitySpecialization specialization,
            NeutralDevelopmentStage stage)
        {
            if (specialization != NeutralCitySpecialization.Military) return 0;
            return stage == NeutralDevelopmentStage.Late ? 2 :
                stage == NeutralDevelopmentStage.Middle ? 1 : 0;
        }

        public static NeutralForceTarget ForceTarget(GameState state, CityState city)
        {
            var stage = NeutralCityRules.DevelopmentStage(state, city);
            var result = new NeutralForceTarget
            {
                Combat = CombatTarget(city.NeutralSpecialization, stage),
                Supply = SupplyTarget(city.NeutralSpecialization, stage)
            };
            var occupied = state.Districts.Exists(item => item.CityId == city.Id &&
                item.ControllerId != city.OwnerId);
            var hostileInside = state.Units.Exists(item => item.OwnerId != city.OwnerId &&
                item.HitPoints > 0 && IsHostileToCity(state, city, item.OwnerId) &&
                state.Tiles.Exists(tile => tile.Id == item.TileId && tile.CityId == city.Id));
            var hostileAdjacent = state.Units.Exists(item => item.OwnerId != city.OwnerId &&
                item.HitPoints > 0 && IsHostileToCity(state, city, item.OwnerId) &&
                IsInOrAdjacentToCity(state, city, item.TileId));
            if (occupied || hostileInside)
            {
                result.Combat += 2;
                if (city.NeutralSpecialization == NeutralCitySpecialization.Military)
                    result.Supply++;
                result.IsThreatened = true;
                result.IsCriticalThreat = true;
            }
            else if (hostileAdjacent)
            {
                result.Combat++;
                result.IsThreatened = true;
            }
            return result;
        }

        private static void PromoteHomeUnits(GameState state, CityState city, NeutralMilitaryResult result)
        {
            var units = state.Units.FindAll(item => item.HomeCityId == city.Id && item.OwnerId == city.OwnerId);
            units.Sort((left, right) => left.Id.CompareTo(right.Id));
            for (var index = 0; index < units.Count; index++)
            {
                var target = NextPromotion(city, units[index].Type);
                if (!target.HasValue) continue;
                if (!CanSustainPromotion(state, city, units[index], target.Value)) continue;
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
            var target = ForceTarget(state, city);
            var combatNeeded = Math.Max(0, target.Combat - combat);
            var supplyNeeded = Math.Max(0, target.Supply - supply);
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
                if (!CanSustainTraining(state, city, type.Value, target.IsCriticalThreat))
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

        public static bool CanSustainTraining(GameState state, CityState city, UnitType type,
            bool emergency = false)
        {
            if (state == null || city == null) return false;
            var projection = NeutralEconomyPlanner.Evaluate(state, city,
                UnitRules.FoodConsumption(type), MaintenanceResolver.UnitUpkeep(type),
                UnitRules.TrainingGold(type));
            if (!emergency) return projection.IsSafe;
            return projection.GoldAfterImmediateCosts >= 0 && projection.FoodNet >= 0 &&
                   projection.GoldNet >= 0;
        }

        public static bool CanSustainPromotion(GameState state, CityState city,
            UnitState unit, UnitType promotedType)
        {
            if (state == null || city == null || unit == null) return false;
            var foodDelta = UnitRules.FoodConsumption(promotedType) - UnitRules.FoodConsumption(unit.Type);
            var upkeepDelta = MaintenanceResolver.UnitUpkeep(promotedType) -
                              MaintenanceResolver.UnitUpkeep(unit.Type);
            var cost = UnitRules.TrainingGold(promotedType) - UnitRules.TrainingGold(unit.Type);
            return cost >= 0 && NeutralEconomyPlanner.Evaluate(state, city,
                foodDelta, upkeepDelta, cost).IsSafe;
        }

        public static DistrictType? SupportNeededForNextForceAction(GameState state, CityState city)
        {
            if (state == null || city == null) return null;
            var units = state.Units.FindAll(item => item.HomeCityId == city.Id &&
                item.OwnerId == city.OwnerId && item.HitPoints > 0);
            units.Sort((left, right) => left.Id.CompareTo(right.Id));
            for (var index = 0; index < units.Count; index++)
            {
                var target = NextPromotion(city, units[index].Type);
                if (!target.HasValue) continue;
                var projection = NeutralEconomyPlanner.Evaluate(state, city,
                    UnitRules.FoodConsumption(target.Value) - UnitRules.FoodConsumption(units[index].Type),
                    MaintenanceResolver.UnitUpkeep(target.Value) - MaintenanceResolver.UnitUpkeep(units[index].Type),
                    UnitRules.TrainingGold(target.Value) - UnitRules.TrainingGold(units[index].Type));
                if (projection.FoodNet < NeutralEconomyPlanner.MinimumNet)
                    return DistrictType.Agriculture;
                if (projection.GoldNet < NeutralEconomyPlanner.MinimumNet)
                    return DistrictType.Commerce;
                break;
            }

            var combat = CountUnitsAndTraining(state, city, false);
            var supply = CountUnitsAndTraining(state, city, true);
            var forceTarget = ForceTarget(state, city);
            UnitType? training = combat < forceTarget.Combat
                ? StrongestCombat(city)
                : supply < forceTarget.Supply
                    ? StrongestSupply(city)
                    : (UnitType?)null;
            if (!training.HasValue) return null;
            var trainingProjection = NeutralEconomyPlanner.Evaluate(state, city,
                UnitRules.FoodConsumption(training.Value), MaintenanceResolver.UnitUpkeep(training.Value),
                UnitRules.TrainingGold(training.Value));
            if (trainingProjection.FoodNet < NeutralEconomyPlanner.MinimumNet)
                return DistrictType.Agriculture;
            if (trainingProjection.GoldNet < NeutralEconomyPlanner.MinimumNet)
                return DistrictType.Commerce;
            return null;
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
