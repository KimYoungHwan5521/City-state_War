using System;
using System.Collections.Generic;

namespace LittleCiv.Core
{
    public enum PlayerAiThreatLevel
    {
        None = 0,
        Potential = 1,
        Direct = 2,
        Emergency = 3
    }

    public sealed class PlayerAiAssessment
    {
        public PlayerAiThreatLevel ThreatLevel;
        public int OwnCombatPower;
        public int EnemyCombatPower;
        public int EnemyMilitaryDistricts;
        public int OwnMilitaryDistricts;
        public int EnemyTurnsToTerritory = int.MaxValue;
    }

    public static class EasyPlayerAiPlanner
    {
        private static readonly ResearchType[] ScienceCore =
        {
            ResearchType.School, ResearchType.IronWorking, ResearchType.Gunpowder,
            ResearchType.Vehicles, ResearchType.NuclearFission
        };

        private static readonly ResearchType[] CultureOrder =
        {
            ResearchType.School,
            ResearchType.Arts, ResearchType.Irrigation, ResearchType.Currency,
            ResearchType.Fortification, ResearchType.IronWorking,
            ResearchType.Salting,
            ResearchType.Printing, ResearchType.Fertilizer, ResearchType.Finance,
            ResearchType.AdvancedFortification, ResearchType.Gunpowder,
            ResearchType.Canning,
            ResearchType.MassMedia, ResearchType.MechanizedAgriculture,
            ResearchType.EconomicAdministration, ResearchType.ModernDefense, ResearchType.Vehicles,
            ResearchType.NuclearFission
        };

        private static readonly ResearchType[] FoodResearch =
        {
            ResearchType.Irrigation, ResearchType.Fertilizer, ResearchType.MechanizedAgriculture
        };

        private static readonly ResearchType[] GoldResearch =
        {
            ResearchType.Currency, ResearchType.Finance, ResearchType.EconomicAdministration
        };

        public static List<GameCommand> Plan(GameState state)
        {
            var result = PlanResearch(state);
            result.AddRange(PlanOrders(state));
            return result;
        }

        public static List<GameCommand> PlanResearch(GameState state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            var result = new List<GameCommand>();
            var players = AiPlayers(state);
            players.Sort((left, right) => left.Id.CompareTo(right.Id));
            for (var index = 0; index < players.Count; index++)
            {
                var player = players[index];
                var city = state.Cities.Find(item => item.OwnerId == player.Id);
                if (city == null) continue;
                var assessment = Assess(state, player, city);
                AddResearch(state, player, city, assessment, result);
            }
            return result;
        }

        public static List<GameCommand> PlanOrders(GameState state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            var result = new List<GameCommand>();
            var players = AiPlayers(state);
            players.Sort((left, right) => left.Id.CompareTo(right.Id));
            for (var index = 0; index < players.Count; index++)
            {
                var player = players[index];
                var city = state.Cities.Find(item => item.OwnerId == player.Id);
                if (city == null) continue;
                var assessment = Assess(state, player, city);
                UpdatePersistentAssessment(state, player, assessment);
                AddRepairs(state, player, city, result);
                AddDistricts(state, player, city, assessment, result);
                AddPromotions(state, player, city, assessment, result);
                AddTraining(state, player, city, assessment, result);
                AddDefense(state, player, city, assessment, result);
                AddTradeAndLevy(state, player, city, assessment, result);
                AddNuclearProject(state, player, city, result);
                AddSupplyTransfers(state, player, result);
                AddMilitaryOrders(state, player, city, assessment, result);
                RestoreCitizenAutomation(state, player, city, assessment, result);
            }
            return result;
        }

        private static List<PlayerState> AiPlayers(GameState state) =>
            state.Players.FindAll(item => item.Slot != PlayerSlot.Neutral &&
                item.AiStrategy != PlayerAiStrategy.None);

        public static ResearchType ChooseResearch(GameState state, PlayerState player,
            CityState city, PlayerAiAssessment assessment)
        {
            if (player.HasUnlockedSelfLearningAI && !player.HasCompletedSelfLearningAI &&
                IsAvailable(player, ResearchType.SelfLearningAI)) return ResearchType.SelfLearningAI;
            var emergency = EmergencyResearch(player, assessment);
            if (emergency != ResearchType.None) return emergency;
            var cultureLossTurns = EstimatedCultureLossTurns(state, player.Id);
            if (player.AiStrategy != PlayerAiStrategy.Culture && cultureLossTurns >= 0 &&
                cultureLossTurns <= 30)
            {
                var arts = FirstAvailable(player, new[] { ResearchType.Arts });
                if (arts != ResearchType.None) return arts;
            }

            var desired = ForceTarget(state, player, city, assessment);
            var economy = NeutralEconomyPlanner.Evaluate(state, city,
                additionalFoodConsumption: Math.Max(0, desired.Combat - CountCombat(state, player.Id)),
                additionalGoldUpkeep: Math.Max(0, desired.Combat - CountCombat(state, player.Id)));
            var economic = EconomicResearch(state, player, city, economy);
            if (economic != ResearchType.None) return economic;

            if (player.AiStrategy == PlayerAiStrategy.Culture)
                return FirstAvailable(player, CultureOrder);

            if (player.AiStrategy == PlayerAiStrategy.Science)
            {
                var core = FirstAvailable(player, ScienceCore);
                if (core != ResearchType.None) return core;
                return FirstAvailable(player, new[] { ResearchType.Arts });
            }

            var conquestCore = FirstAvailable(player, new[]
            {
                ResearchType.School, ResearchType.IronWorking, ResearchType.Gunpowder,
                ResearchType.Vehicles
            });
            if (conquestCore != ResearchType.None) return conquestCore;
            if (player.AiStalledAttackTurns >= 10 || EnemyHasNuclearProgram(state, player.Id))
                return FirstAvailable(player, new[] { ResearchType.NuclearFission });
            cultureLossTurns = EstimatedCultureLossTurns(state, player.Id);
            if (cultureLossTurns >= 0 && cultureLossTurns <= 15)
                return FirstAvailable(player, new[] { ResearchType.Arts });
            return FirstAvailable(player, new[] { ResearchType.Irrigation, ResearchType.Salting,
                ResearchType.Canning, ResearchType.NuclearFission, ResearchType.Arts });
        }

        public static PlayerAiAssessment Assess(GameState state, PlayerState player, CityState city)
        {
            var result = new PlayerAiAssessment();
            var enemy = state.Players.Find(item => item.Slot != PlayerSlot.Neutral && item.Id != player.Id);
            if (enemy == null) return result;
            result.OwnCombatPower = CombatPower(state, player.Id);
            result.EnemyCombatPower = CombatPower(state, enemy.Id);
            result.OwnMilitaryDistricts = CountDistricts(state, city.Id, DistrictType.Military);
            var enemyCity = state.Cities.Find(item => item.OwnerId == enemy.Id);
            result.EnemyMilitaryDistricts = enemyCity == null ? 0 :
                CountDistricts(state, enemyCity.Id, DistrictType.Military);
            var enemyInside = false;
            for (var index = 0; index < state.Units.Count; index++)
            {
                var unit = state.Units[index];
                if (unit.OwnerId != enemy.Id || UnitRules.IsSupply(unit.Type) || unit.HitPoints <= 0) continue;
                var tile = state.Tiles.Find(item => item.Id == unit.TileId);
                if (tile != null && tile.CityId == city.Id)
                {
                    enemyInside = true;
                    result.EnemyTurnsToTerritory = 0;
                    break;
                }
                // A unit merely stationed in its own city is not an approaching threat even when
                // the compact world layout places that city within two movement turns.
                if (tile == null || tile.CityId == unit.HomeCityId) continue;
                var turns = TurnsToCity(state, unit, city.Id);
                if (turns < result.EnemyTurnsToTerritory) result.EnemyTurnsToTerritory = turns;
            }
            var occupied = state.Districts.Exists(item => item.CityId == city.Id &&
                item.ControllerId != city.OwnerId);
            if (enemyInside || occupied)
                result.ThreatLevel = PlayerAiThreatLevel.Emergency;
            else if (result.EnemyTurnsToTerritory <= 2 ||
                     result.EnemyCombatPower * 2 >= Math.Max(1, result.OwnCombatPower) * 3)
                result.ThreatLevel = PlayerAiThreatLevel.Direct;
            else if (result.EnemyTurnsToTerritory <= 3 ||
                     result.EnemyCombatPower >= result.OwnCombatPower ||
                     result.EnemyMilitaryDistricts >= result.OwnMilitaryDistricts + 2)
                result.ThreatLevel = PlayerAiThreatLevel.Potential;
            return result;
        }

        private static void AddResearch(GameState state, PlayerState player, CityState city,
            PlayerAiAssessment assessment, List<GameCommand> result)
        {
            if (player.CurrentResearch != ResearchType.None) return;
            var research = ChooseResearch(state, player, city, assessment);
            if (research == ResearchType.None) return;
            result.Add(Command(state, player.Id, GameCommandType.SelectResearch,
                primary: (int)research));
        }

        private static void AddRepairs(GameState state, PlayerState player, CityState city,
            List<GameCommand> result)
        {
            var districts = state.Districts.FindAll(item => item.CityId == city.Id &&
                item.ControllerId == city.OwnerId && item.IsPillaged && item.RemainingRepairTurns <= 0);
            districts.Sort((left, right) => left.Id.CompareTo(right.Id));
            for (var index = 0; index < districts.Count; index++)
                result.Add(Command(state, player.Id, GameCommandType.RepairDistrict,
                    districts[index].Id));
        }

        private static void AddDistricts(GameState state, PlayerState player, CityState city,
            PlayerAiAssessment assessment, List<GameCommand> result)
        {
            if (AssignCultureDefenseCitizen(state, player, city, result)) return;
            var free = DistrictConstructionResolver.CountFreeCitizens(state, city);
            if (free <= 0)
                free += AddEmergencyCitizenReassignment(state, player, city, assessment, result);
            if (free <= 0) return;
            free -= ResumePausedConstruction(state, player, city, free, result);
            if (free <= 0) return;
            free -= AssignIdleSpecializedDistrict(state, player, city, free, result);
            if (free <= 0) return;
            var reserved = new HashSet<EntityId>();
            while (free-- > 0)
            {
                var type = NextDistrict(state, player, city, assessment, result);
                if (type == DistrictType.Government || !IsDistrictUnlocked(player, type)) break;
                if (type == DistrictType.NuclearFacility &&
                    (HasDistrict(state, city.Id, type) ||
                     HasPlanned(result, player.Id, city.Id, type))) break;
                var tile = SelectTile(state, city, type, reserved);
                if (!tile.IsValid) break;
                reserved.Add(tile);
                result.Add(Command(state, player.Id, GameCommandType.StartDistrict,
                    city.Id, tile, (int)type));
            }
        }

        private static bool AssignCultureDefenseCitizen(GameState state, PlayerState player,
            CityState city, List<GameCommand> result)
        {
            var cultureLossTurns = EstimatedCultureLossTurns(state, player.Id);
            if (player.AiStrategy == PlayerAiStrategy.Culture || cultureLossTurns < 0 ||
                cultureLossTurns > 30) return false;
            var receiver = state.Districts.Find(item => item.CityId == city.Id &&
                item.Type == DistrictType.Culture && item.ControllerId == city.OwnerId &&
                item.RemainingConstructionTurns <= 0 && !item.IsPillaged &&
                item.RemainingRepairTurns <= 0 && item.AssignedCitizens <= 0);
            if (receiver == null) return false;
            DistrictState donor = null;
            if (DistrictConstructionResolver.CountFreeCitizens(state, city) <= 0)
            {
                donor = SelectCitizenDonor(state, city, DistrictType.Culture);
                if (donor == null) return false;
            }
            if (city.CitizenAutoAssignment)
                result.Add(Command(state, player.Id, GameCommandType.SetCitizenAutoAssignment,
                    city.Id, primary: 0));
            if (donor != null)
            {
                result.Add(Command(state, player.Id, GameCommandType.AssignCitizen,
                    donor.Id, primary: donor.AssignedCitizens - 1, secondary: -1));
            }
            result.Add(Command(state, player.Id, GameCommandType.AssignCitizen,
                receiver.Id, primary: 1, secondary: 1));
            return true;
        }

        private static int ResumePausedConstruction(GameState state, PlayerState player,
            CityState city, int freeCitizens, List<GameCommand> result)
        {
            var paused = state.Districts.FindAll(item => item.CityId == city.Id &&
                item.ControllerId == city.OwnerId && item.RemainingConstructionTurns > 0 &&
                item.AssignedCitizens <= 0);
            paused.Sort((left, right) => left.Id.CompareTo(right.Id));
            var resumed = Math.Min(freeCitizens, paused.Count);
            if (resumed <= 0) return 0;
            if (city.CitizenAutoAssignment)
                result.Add(Command(state, player.Id, GameCommandType.SetCitizenAutoAssignment,
                    city.Id, primary: 0));
            for (var index = 0; index < resumed; index++)
                result.Add(Command(state, player.Id, GameCommandType.AssignCitizen,
                    paused[index].Id, primary: 1, secondary: 1));
            return resumed;
        }

        private static int AssignIdleSpecializedDistrict(GameState state, PlayerState player,
            CityState city, int freeCitizens, List<GameCommand> result)
        {
            if (freeCitizens <= 0) return 0;
            var specialized = player.AiStrategy == PlayerAiStrategy.Science ? DistrictType.Science :
                player.AiStrategy == PlayerAiStrategy.Culture ? DistrictType.Culture : DistrictType.Military;
            var idle = state.Districts.Find(item => item.CityId == city.Id &&
                item.Type == specialized && item.ControllerId == city.OwnerId &&
                item.RemainingConstructionTurns <= 0 && !item.IsPillaged &&
                item.RemainingRepairTurns <= 0 && item.AssignedCitizens <= 0);
            if (idle == null) return 0;
            if (city.CitizenAutoAssignment)
                result.Add(Command(state, player.Id, GameCommandType.SetCitizenAutoAssignment,
                    city.Id, primary: 0));
            result.Add(Command(state, player.Id, GameCommandType.AssignCitizen,
                idle.Id, primary: 1, secondary: 1));
            return 1;
        }

        private static int AddEmergencyCitizenReassignment(GameState state, PlayerState player,
            CityState city, PlayerAiAssessment assessment, List<GameCommand> result)
        {
            var projection = NeutralEconomyPlanner.Evaluate(state, city);
            var desired = projection.FoodNet < NeutralEconomyPlanner.MinimumNet
                ? DistrictType.Agriculture
                : projection.GoldNet < NeutralEconomyPlanner.MinimumNet
                    ? DistrictType.Commerce
                    : assessment.ThreatLevel >= PlayerAiThreatLevel.Direct
                        ? DistrictType.Military
                        : DistrictType.Government;
            if (desired == DistrictType.Government) return 0;
            var donor = SelectCitizenDonor(state, city, desired);
            if (donor == null) return 0;
            if (city.CitizenAutoAssignment)
                result.Add(Command(state, player.Id, GameCommandType.SetCitizenAutoAssignment,
                    city.Id, primary: 0));
            result.Add(Command(state, player.Id, GameCommandType.AssignCitizen,
                donor.Id, primary: donor.AssignedCitizens - 1, secondary: -1));
            var receiver = state.Districts.Find(item => item.CityId == city.Id &&
                item.Type == desired && item.ControllerId == city.OwnerId &&
                item.AssignedCitizens <= 0 && !item.IsPillaged && item.RemainingRepairTurns <= 0);
            if (receiver == null) return 1;
            result.Add(Command(state, player.Id, GameCommandType.AssignCitizen,
                receiver.Id, primary: 1, secondary: 1));
            return 0;
        }

        private static DistrictState SelectCitizenDonor(GameState state, CityState city,
            DistrictType desired)
        {
            var candidates = state.Districts.FindAll(item => item.CityId == city.Id &&
                item.Type != DistrictType.Government && item.Type != DistrictType.NuclearFacility &&
                item.Type != desired && item.ControllerId == city.OwnerId && item.AssignedCitizens > 0);
            candidates.Sort((left, right) =>
            {
                var construction = (left.RemainingConstructionTurns > 0 ? 0 : 1)
                    .CompareTo(right.RemainingConstructionTurns > 0 ? 0 : 1);
                if (construction != 0) return construction;
                var priority = CitizenRecallPriority(left.Type, desired)
                    .CompareTo(CitizenRecallPriority(right.Type, desired));
                return priority != 0 ? priority : left.Id.CompareTo(right.Id);
            });
            return candidates.Count == 0 ? null : candidates[0];
        }

        private static int CitizenRecallPriority(DistrictType type, DistrictType desired)
        {
            if (desired == DistrictType.Military)
            {
                if (type == DistrictType.Culture) return 0;
                if (type == DistrictType.Science) return 1;
                if (type == DistrictType.Commerce) return 2;
                if (type == DistrictType.Agriculture) return 3;
            }
            else
            {
                if (type == DistrictType.Culture) return 0;
                if (type == DistrictType.Science) return 1;
                if (type == DistrictType.Military) return 2;
                if (type == DistrictType.Commerce) return 3;
                if (type == DistrictType.Agriculture) return 4;
            }
            return 5;
        }

        private static DistrictType NextDistrict(GameState state, PlayerState player, CityState city,
            PlayerAiAssessment assessment, List<GameCommand> planned)
        {
            if (!HasDistrict(state, city.Id, DistrictType.Agriculture) &&
                !HasPlanned(planned, player.Id, city.Id, DistrictType.Agriculture))
                return DistrictType.Agriculture;
            if (!HasDistrict(state, city.Id, DistrictType.Military) &&
                !HasPlanned(planned, player.Id, city.Id, DistrictType.Military))
                return DistrictType.Military;
            if (!HasDistrict(state, city.Id, DistrictType.Commerce) &&
                !HasPlanned(planned, player.Id, city.Id, DistrictType.Commerce))
                return DistrictType.Commerce;
            var economy = NeutralEconomyPlanner.Evaluate(state, city);
            if (economy.FoodNet < NeutralEconomyPlanner.MinimumNet) return DistrictType.Agriculture;
            if (economy.GoldNet < NeutralEconomyPlanner.MinimumNet) return DistrictType.Commerce;
            if (player.CompletedResearch.Contains(ResearchType.NuclearFission) &&
                !HasDistrict(state, city.Id, DistrictType.NuclearFacility) &&
                !HasPlanned(planned, player.Id, city.Id, DistrictType.NuclearFacility))
                return DistrictType.NuclearFacility;
            if (assessment.ThreatLevel >= PlayerAiThreatLevel.Direct &&
                CountDistricts(state, city.Id, DistrictType.Military) +
                CountPlanned(planned, player.Id, city.Id, DistrictType.Military) < 2)
                return DistrictType.Military;
            var cultureLossTurns = EstimatedCultureLossTurns(state, player.Id);
            if (player.AiStrategy != PlayerAiStrategy.Culture && cultureLossTurns >= 0 &&
                cultureLossTurns <= 30 &&
                CountDistricts(state, city.Id, DistrictType.Culture) +
                CountPlanned(planned, player.Id, city.Id, DistrictType.Culture) < 2)
                return DistrictType.Culture;
            var specialized = player.AiStrategy == PlayerAiStrategy.Science ? DistrictType.Science :
                player.AiStrategy == PlayerAiStrategy.Culture ? DistrictType.Culture : DistrictType.Military;
            return specialized;
        }

        private static void AddTraining(GameState state, PlayerState player, CityState city,
            PlayerAiAssessment assessment, List<GameCommand> result)
        {
            var target = ForceTarget(state, player, city, assessment);
            var combatMissing = Math.Max(0, target.Combat - CountCombatAndTraining(state, player.Id));
            var supplyMissing = Math.Max(0, target.Supply - CountSupplyAndTraining(state, player.Id));
            if (combatMissing <= 0 && supplyMissing <= 0) return;
            var districts = state.Districts.FindAll(item => item.CityId == city.Id &&
                item.Type == DistrictType.Military && item.ControllerId == city.OwnerId &&
                item.IsOperational && item.AssignedCitizens > 0 && item.RemainingConstructionTurns <= 0 &&
                !state.UnitTrainings.Exists(training => training.DistrictId == item.Id) &&
                !result.Exists(command => command.Type == GameCommandType.AssignCitizen &&
                    command.SubjectId == item.Id && command.PrimaryValue <= 0));
            districts.Sort((left, right) => left.Id.CompareTo(right.Id));
            var budget = city.Gold - PlannedImmediateGold(state, result, player.Id);
            for (var index = 0; index < districts.Count; index++)
            {
                UnitType type;
                if (combatMissing > 0)
                {
                    type = StrongestCombat(player);
                    combatMissing--;
                }
                else if (supplyMissing > 0)
                {
                    type = StrongestSupply(player);
                    supplyMissing--;
                }
                else break;
                var cost = UnitRules.TrainingGold(type);
                var projection = NeutralEconomyPlanner.Evaluate(state, city,
                    UnitRules.FoodConsumption(type), MaintenanceResolver.UnitUpkeep(type), cost);
                var emergency = assessment.ThreatLevel == PlayerAiThreatLevel.Emergency;
                if (budget < cost || (!projection.IsSafe && !(emergency && projection.FoodNet >= 0 &&
                    projection.GoldNet >= 0))) break;
                budget -= cost;
                result.Add(Command(state, player.Id, GameCommandType.StartTraining,
                    districts[index].Id, primary: (int)type));
            }
        }

        private static void AddPromotions(GameState state, PlayerState player, CityState city,
            PlayerAiAssessment assessment, List<GameCommand> result)
        {
            var units = state.Units.FindAll(item => item.OwnerId == player.Id &&
                item.HomeCityId == city.Id && item.HitPoints > 0 && item.RemainingMovement > 0);
            units.Sort((left, right) => left.Id.CompareTo(right.Id));
            var budget = city.Gold - PlannedImmediateGold(state, result, player.Id);
            for (var index = 0; index < units.Count; index++)
            {
                var target = UnitRules.IsSupply(units[index].Type)
                    ? StrongestSupply(player) : StrongestCombat(player);
                if (target == units[index].Type || !IsHigherInBranch(units[index].Type, target)) continue;
                var tile = state.Tiles.Find(item => item.Id == units[index].TileId);
                if (tile == null || tile.IsSharedBoundary || tile.ControllerId != player.Id ||
                    tile.CityId != city.Id) continue;
                var cost = UnitRules.TrainingGold(target) - UnitRules.TrainingGold(units[index].Type);
                var foodDelta = UnitRules.FoodConsumption(target) - UnitRules.FoodConsumption(units[index].Type);
                var upkeepDelta = MaintenanceResolver.UnitUpkeep(target) -
                                  MaintenanceResolver.UnitUpkeep(units[index].Type);
                var projection = NeutralEconomyPlanner.Evaluate(state, city, foodDelta, upkeepDelta,
                    PlannedImmediateGold(state, result, player.Id) + cost);
                if (cost < 0 || budget < cost || (!projection.IsSafe &&
                    assessment.ThreatLevel < PlayerAiThreatLevel.Emergency)) continue;
                result.Add(Command(state, player.Id, GameCommandType.PromoteUnit,
                    units[index].Id, primary: (int)target));
                budget -= cost;
            }
        }

        private static void AddTradeAndLevy(GameState state, PlayerState player, CityState city,
            PlayerAiAssessment assessment, List<GameCommand> result)
        {
            if (state.TradeReservations.Exists(item => item.PlayerId == player.Id)) return;
            var reservedGold = PlannedImmediateGold(state, result, player.Id);
            var projected = NeutralEconomyPlanner.Evaluate(state, city, immediateGoldCost: reservedGold);
            var neutrals = state.Cities.FindAll(item => IsNeutralCity(state, item));
            neutrals.Sort((left, right) => left.Id.CompareTo(right.Id));

            var shouldLevy = player.AiStrategy == PlayerAiStrategy.Conquest ||
                             assessment.ThreatLevel >= PlayerAiThreatLevel.Direct;
            if (shouldLevy && !state.Levies.Exists(item => item.PlayerId == player.Id))
            {
                for (var index = 0; index < neutrals.Count; index++)
                {
                    if (neutrals[index].NeutralSpecialization != NeutralCitySpecialization.Military) continue;
                    var quote = NeutralLevyResolver.Quote(state, player.Id, city.Id, neutrals[index].Id);
                    if (!quote.IsAvailable || city.Gold - reservedGold < quote.BasePrice) continue;
                    var after = NeutralEconomyPlanner.Evaluate(state, city,
                        immediateGoldCost: reservedGold + quote.BasePrice);
                    if (!after.IsSafe && assessment.ThreatLevel < PlayerAiThreatLevel.Emergency) continue;
                    result.Add(Command(state, player.Id, GameCommandType.LevyBid,
                        city.Id, neutrals[index].Id));
                    return;
                }
            }

            var wanted = player.AiStrategy == PlayerAiStrategy.Culture
                ? NeutralCitySpecialization.Culture : NeutralCitySpecialization.Science;
            var purchaseCities = neutrals.FindAll(item => item.NeutralSpecialization == wanted);
            purchaseCities.Sort((left, right) => ComparePurchaseTradeCost(
                state, player.Id, city.Id, left, right));
            for (var index = 0; index < purchaseCities.Count; index++)
            {
                var target = purchaseCities[index];
                var quote = NeutralTradeQuoteResolver.Quote(state, player.Id, city.Id, target.Id);
                if (!quote.IsAvailable || city.Gold - reservedGold - quote.TotalGoldCost <
                    projected.GoldReserveRequired) continue;
                result.Add(Command(state, player.Id, GameCommandType.Trade,
                    city.Id, target.Id, (int)quote.ReceivedResource));
                return;
            }

            if (projected.GoldNet >= NeutralEconomyPlanner.MinimumNet &&
                city.Gold >= projected.GoldReserveRequired) return;
            var offered = city.StoredFood > projected.FoodReserveRequired + 3
                ? TileResourceType.Food
                : player.AiStrategy == PlayerAiStrategy.Culture
                    ? TileResourceType.Science : TileResourceType.Culture;
            var commerceCities = neutrals.FindAll(item =>
                item.NeutralSpecialization == NeutralCitySpecialization.Commerce);
            commerceCities.Sort((left, right) => CompareCommerceTradeCost(
                state, player.Id, city.Id, offered, left, right));
            for (var index = 0; index < commerceCities.Count; index++)
            {
                var target = commerceCities[index];
                var quote = CommerceTradeQuoteResolver.Quote(state, player.Id, city.Id,
                    target.Id, offered);
                if (!quote.IsAvailable) continue;
                result.Add(Command(state, player.Id, GameCommandType.Trade,
                    city.Id, target.Id, (int)offered));
                return;
            }
        }

        private static void AddMilitaryOrders(GameState state, PlayerState player, CityState city,
            PlayerAiAssessment assessment, List<GameCommand> result)
        {
            var enemy = state.Players.Find(item => item.Slot != PlayerSlot.Neutral && item.Id != player.Id);
            var enemyCity = enemy == null ? null : state.Cities.Find(item => item.OwnerId == enemy.Id);
            if (enemyCity == null) return;
            var units = state.Units.FindAll(item => item.OwnerId == player.Id && item.HitPoints > 0 &&
                item.RemainingMovement > 0 && item.CreatedTurn != state.TurnNumber);
            units.Sort((left, right) => left.Id.CompareTo(right.Id));
            var hostileInside = state.Units.FindAll(item => item.OwnerId == enemy.Id && item.HitPoints > 0 &&
                state.Tiles.Exists(tile => tile.Id == item.TileId && tile.CityId == city.Id));
            hostileInside.Sort((left, right) => left.Id.CompareTo(right.Id));

            var government = state.Districts.Find(item => item.CityId == city.Id &&
                item.Type == DistrictType.Government);
            var defender = government == null ? null : units.FindAll(item => !UnitRules.IsSupply(item.Type))
                .Find(item => item.TileId == government.TileId);
            if (defender == null && government != null)
                defender = StrongestUnit(units, false);

            var shouldAttack = ShouldAttack(state, player, city, enemyCity, assessment);
            var prioritizeCultureRaid = enemyCity.LastCultureProduction > city.LastCultureProduction;
            var attackTarget = shouldAttack
                ? player.AiStrategy == PlayerAiStrategy.Conquest ||
                  player.AiStrategy == PlayerAiStrategy.Science ||
                  player.AiStrategy == PlayerAiStrategy.Culture
                    ? GovernmentTile(state, enemyCity.Id)
                    : SelectEnemyTarget(state, player, enemyCity, prioritizeCultureRaid)
                : default;
            var limitedRaid = false;
            if (!shouldAttack && player.AiStrategy == PlayerAiStrategy.Conquest)
            {
                attackTarget = SelectLimitedRaidTarget(state, player, city, enemyCity,
                    assessment, defender, prioritizeCultureRaid);
                limitedRaid = attackTarget.IsValid;
                shouldAttack = limitedRaid;
            }
            var expedition = shouldAttack
                ? limitedRaid
                    ? SelectLimitedRaidUnits(state, player.Id, defender, attackTarget)
                    : SelectExpeditionUnits(state, player.Id, defender, enemyCity.Id)
                : new HashSet<EntityId>();
            var supplyDispatched = false;
            for (var index = 0; index < units.Count; index++)
            {
                var unit = units[index];
                EntityId target = default;
                var unitTile = state.Tiles.Find(item => item.Id == unit.TileId);
                var distanceHomeTiles = government == null ? int.MaxValue :
                    CoordinateDistance(state, unit.TileId, government.TileId);
                var turnsHome = distanceHomeTiles == int.MaxValue ? int.MaxValue :
                    (distanceHomeTiles + UnitRules.Movement(unit.Type) - 1) /
                    UnitRules.Movement(unit.Type);
                var canFinishObjective = shouldAttack && attackTarget.IsValid &&
                    CoordinateDistance(state, unit.TileId, attackTarget) <= unit.RemainingMovement &&
                    unit.CarriedFood >= turnsHome;
                var mustRetreat = unitTile != null && unitTile.CityId != city.Id &&
                    (!shouldAttack || unit.CarriedFood < turnsHome + 1 && !canFinishObjective);
                if (hostileInside.Count > 0 && !UnitRules.IsSupply(unit.Type))
                    target = hostileInside[0].TileId;
                else if (mustRetreat && government != null)
                    target = government.TileId;
                else if (defender != null && unit.Id == defender.Id && government != null &&
                         unit.TileId != government.TileId)
                    target = government.TileId;
                else if (shouldAttack && !UnitRules.IsSupply(unit.Type) && expedition.Contains(unit.Id))
                    target = attackTarget;
                else if (shouldAttack && UnitRules.IsSupply(unit.Type) && !supplyDispatched)
                {
                    target = SupplyEscortTarget(state, player.Id, city.Id, units);
                    supplyDispatched = target.IsValid;
                }
                else if (assessment.ThreatLevel >= PlayerAiThreatLevel.Potential &&
                         !UnitRules.IsSupply(unit.Type))
                    target = SelectDefensiveTarget(state, city, unit);
                if (!target.IsValid || target == unit.TileId) continue;
                var path = FindMajorWarPath(state, unit, target, city.Id, enemyCity.Id);
                if (path.Count == 0) continue;
                AddFoodLoadIfUseful(state, player, city, unit, path.Count, result);
                var move = Command(state, player.Id, GameCommandType.MoveUnit,
                    unit.Id, target, secondary: HasEnemyAt(state, player.Id, target) ? 1 : 0);
                move.Path.AddRange(path);
                result.Add(move);
            }
        }

        private static void AddSupplyTransfers(GameState state, PlayerState player,
            List<GameCommand> result)
        {
            var plannedReceipts = new Dictionary<EntityId, int>();
            var supplies = state.Units.FindAll(item => item.OwnerId == player.Id &&
                UnitRules.IsSupply(item.Type) && item.HitPoints > 0 && item.CarriedFood > 0);
            supplies.Sort((left, right) => left.Id.CompareTo(right.Id));
            for (var supplyIndex = 0; supplyIndex < supplies.Count; supplyIndex++)
            {
                var available = supplies[supplyIndex].CarriedFood;
                var receivers = state.Units.FindAll(item => item.OwnerId == player.Id &&
                    !UnitRules.IsSupply(item.Type) && item.HitPoints > 0 &&
                    item.TileId == supplies[supplyIndex].TileId &&
                    item.CarriedFood < UnitRules.FoodCapacity(state, item));
                receivers.Sort((left, right) =>
                {
                    var food = left.CarriedFood.CompareTo(right.CarriedFood);
                    return food != 0 ? food : left.Id.CompareTo(right.Id);
                });
                for (var receiverIndex = 0; receiverIndex < receivers.Count && available > 0;
                     receiverIndex++)
                {
                    plannedReceipts.TryGetValue(receivers[receiverIndex].Id, out var alreadyPlanned);
                    var amount = Math.Min(available,
                        UnitRules.FoodCapacity(state, receivers[receiverIndex]) -
                        receivers[receiverIndex].CarriedFood - alreadyPlanned);
                    if (amount <= 0) continue;
                    result.Add(Command(state, player.Id, GameCommandType.TransferFood,
                        supplies[supplyIndex].Id, receivers[receiverIndex].Id, amount));
                    plannedReceipts[receivers[receiverIndex].Id] = alreadyPlanned + amount;
                    available -= amount;
                }
            }
        }

        private static bool ShouldAttack(GameState state, PlayerState player, CityState city,
            CityState enemyCity, PlayerAiAssessment assessment)
        {
            if (assessment.ThreatLevel >= PlayerAiThreatLevel.Emergency) return false;
            var own = ExpeditionPower(state, player.Id, city.Id);
            var enemy = AssaultDefensePower(state, player.Id, enemyCity.Id);
            var economy = NeutralEconomyPlanner.Evaluate(state, city);
            var route = GovernmentRouteExists(state, player.Id, city.Id, enemyCity.Id);
            if (!economy.IsSafe || !route || CountCombat(state, player.Id) < 2 ||
                !CanSupplyAttack(state, player, city, enemyCity, economy)) return false;
            var cultureLossTurns = EstimatedCultureLossTurns(state, player.Id);
            if (player.AiStrategy == PlayerAiStrategy.Conquest && cultureLossTurns >= 0 &&
                cultureLossTurns <= 20)
                return own * 4 >= Math.Max(1, enemy) * 5;
            if (player.AiStrategy == PlayerAiStrategy.Conquest)
                return own * 2 >= Math.Max(1, enemy) * 3;
            if (player.AiStrategy == PlayerAiStrategy.Science ||
                player.AiStrategy == PlayerAiStrategy.Culture)
                return assessment.OwnCombatPower * 2 >=
                       Math.Max(1, assessment.EnemyCombatPower) * 3 &&
                       own * 2 >= Math.Max(1, enemy) * 3;
            return false;
        }

        private static int EstimatedCultureVictoryTurns(GameState state, EntityId playerId)
        {
            var home = state.Cities.Find(item => item.OwnerId == playerId);
            var enemy = state.Players.Find(item => item.Slot != PlayerSlot.Neutral &&
                item.Id != playerId);
            var target = enemy == null ? null : state.Cities.Find(item => item.OwnerId == enemy.Id);
            if (home == null || target == null || home.LastCultureProduction <= target.LastCultureProduction)
                return -1;
            var difference = home.LastCultureProduction - target.LastCultureProduction;
            var influence = target.CultureInfluences.Find(item => item.CultureOwnerId == playerId);
            var preferred = influence == null ? 0 : influence.PreferredCitizens;
            var progress = influence == null ? 0 : influence.ConversionProgress;
            var neededCitizens = target.Population / 2 + 1 - preferred;
            return neededCitizens <= 0 ? 0 : Math.Max(1,
                (neededCitizens * 10 - progress + difference - 1) / difference);
        }

        private static void RestoreCitizenAutomation(GameState state, PlayerState player, CityState city,
            PlayerAiAssessment assessment, List<GameCommand> result)
        {
            if (city.CitizenAutoAssignment || assessment.ThreatLevel >= PlayerAiThreatLevel.Direct) return;
            if (result.Exists(item => item.PlayerId == player.Id &&
                (item.Type == GameCommandType.AssignCitizen ||
                 item.Type == GameCommandType.SetCitizenAutoAssignment && item.PrimaryValue == 0))) return;
            var economy = NeutralEconomyPlanner.Evaluate(state, city);
            if (!economy.IsSafe) return;
            result.Add(Command(state, player.Id, GameCommandType.SetCitizenAutoAssignment,
                city.Id, primary: 1));
        }

        private static void AddDefense(GameState state, PlayerState player, CityState city,
            PlayerAiAssessment assessment, List<GameCommand> result)
        {
            if (assessment.ThreatLevel == PlayerAiThreatLevel.None) return;
            var government = state.Districts.Find(item => item.CityId == city.Id &&
                item.Type == DistrictType.Government && item.ControllerId == city.OwnerId && item.IsOperational);
            if (government == null) return;
            var facility = state.DefenseFacilities.Find(item => item.CityId == city.Id);
            if (facility != null && facility.Type == DefenseFacilityType.ModernDefense &&
                !facility.IsModernDefenseActive && facility.RemainingReactivationTurns <= 0 &&
                NeutralEconomyPlanner.Evaluate(state, city).IsSafe)
            {
                result.Add(Command(state, player.Id, GameCommandType.SetModernDefenseActive,
                    facility.Id, primary: 1));
                return;
            }
            if (facility != null && facility.RemainingConstructionTurns > 0) return;
            var current = facility == null ? DefenseFacilityType.None : facility.Type;
            var desired = assessment.ThreatLevel == PlayerAiThreatLevel.Potential ? DefenseFacilityType.Wall :
                assessment.ThreatLevel == PlayerAiThreatLevel.Direct ? DefenseFacilityType.Moat :
                DefenseFacilityType.ModernDefense;
            var next = (DefenseFacilityType)((int)current + 1);
            if ((int)next > (int)desired || !player.UnlockedDefenseTypes.Contains(next) ||
                !NeutralDefenseResolver.CanAfford(state, city, next)) return;
            var cost = DefenseFacilityResolver.GoldCost(next);
            var reservedGold = PlannedImmediateGold(state, result, player.Id);
            if (city.Gold - reservedGold < cost) return;
            var projection = NeutralEconomyPlanner.Evaluate(state, city,
                immediateGoldCost: reservedGold + cost);
            if (!projection.IsSafe && assessment.ThreatLevel < PlayerAiThreatLevel.Emergency) return;
            result.Add(Command(state, player.Id, GameCommandType.StartDefenseFacility,
                city.Id, government.TileId, (int)next));
        }

        private static void AddNuclearProject(GameState state, PlayerState player, CityState city,
            List<GameCommand> result)
        {
            if (!player.CompletedResearch.Contains(ResearchType.NuclearFission) ||
                player.HasCompletedNuclearProject) return;
            var facility = state.Districts.Find(item => item.CityId == city.Id &&
                item.Type == DistrictType.NuclearFacility && item.ControllerId == city.OwnerId &&
                item.IsOperational && !item.IsPillaged && !item.IsMaintenanceSuspended &&
                item.RemainingConstructionTurns <= 0 && item.AssignedCitizens > 0);
            if (facility == null || city.Gold - PlannedImmediateGold(state, result, player.Id) <
                NuclearProjectResolver.StartGold ||
                state.NuclearProjects.Exists(item => item.DistrictId == facility.Id)) return;
            result.Add(Command(state, player.Id, GameCommandType.StartNuclearProject, facility.Id));
        }

        private static ResearchType EconomicResearch(GameState state, PlayerState player,
            CityState city, NeutralEconomyProjection projection)
        {
            var foodUnsafe = projection.FoodNet < NeutralEconomyPlanner.MinimumNet ||
                             city.StoredFood < projection.FoodReserveRequired;
            var goldUnsafe = projection.GoldNet < NeutralEconomyPlanner.MinimumNet ||
                             city.Gold < projection.GoldReserveRequired;
            if (!foodUnsafe && !goldUnsafe) return ResearchType.None;
            if (foodUnsafe)
            {
                var food = FirstAvailable(player, FoodResearch);
                if (food != ResearchType.None && CountDistricts(state, city.Id, DistrictType.Agriculture) > 0)
                    return food;
            }
            if (goldUnsafe)
            {
                var gold = FirstAvailable(player, GoldResearch);
                if (gold != ResearchType.None && CountDistricts(state, city.Id, DistrictType.Commerce) > 0)
                    return gold;
            }
            return ResearchType.None;
        }

        private static ResearchType EmergencyResearch(PlayerState player, PlayerAiAssessment assessment)
        {
            if (assessment == null || assessment.ThreatLevel < PlayerAiThreatLevel.Direct)
                return ResearchType.None;
            var military = FirstAvailable(player, new[]
            {
                ResearchType.IronWorking, ResearchType.Gunpowder, ResearchType.Vehicles
            });
            if (military != ResearchType.None) return military;
            return FirstAvailable(player, new[]
            {
                ResearchType.Fortification, ResearchType.AdvancedFortification,
                ResearchType.ModernDefense
            });
        }

        private static (int Combat, int Supply) ForceTarget(GameState state, PlayerState player,
            CityState city, PlayerAiAssessment assessment)
        {
            var specialized = player.AiStrategy == PlayerAiStrategy.Science ? DistrictType.Science :
                player.AiStrategy == PlayerAiStrategy.Culture ? DistrictType.Culture : DistrictType.Military;
            var stageCount = CountDistricts(state, city.Id, specialized);
            var stage = stageCount <= 1 ? 0 : stageCount == 2 ? 1 : 2;
            var combat = player.AiStrategy == PlayerAiStrategy.Conquest
                ? (stage == 0 ? 3 : stage == 1 ? 5 : 8)
                : (stage == 0 ? 2 : stage == 1 ? 3 : 4);
            if (player.AiStrategy == PlayerAiStrategy.Conquest)
            {
                var enemy = state.Players.Find(item => item.Slot != PlayerSlot.Neutral &&
                    item.Id != player.Id);
                var enemyCity = enemy == null ? null : state.Cities.Find(item => item.OwnerId == enemy.Id);
                if (enemyCity != null)
                {
                    var unitPower = Math.Max(1, UnitRules.Attack(StrongestCombat(player)));
                    var defensePower = GovernmentCombatPower(state, enemyCity.Id);
                    // One unit remains at the government district; the expedition itself must reach
                    // the documented 1.5x target power.
                    var required = (defensePower * 3 + unitPower * 2 - 1) /
                                   (unitPower * 2) + 1;
                    combat = Math.Max(combat, required);
                }
            }
            var supply = player.AiStrategy == PlayerAiStrategy.Conquest
                ? (stage == 2 ? 2 : 1) : 0;
            combat += assessment == null ? 0 : (int)assessment.ThreatLevel;
            var cultureLossTurns = EstimatedCultureLossTurns(state, player.Id);
            if (player.AiStrategy != PlayerAiStrategy.Culture && cultureLossTurns >= 0 &&
                cultureLossTurns <= 25) combat += cultureLossTurns <= 10 ? 3 : 2;
            return (combat, supply);
        }

        private static void UpdatePersistentAssessment(GameState state, PlayerState player,
            PlayerAiAssessment assessment)
        {
            if (assessment.EnemyCombatPower * 2 <= assessment.OwnCombatPower)
                player.AiLowEnemyMilitaryTurns++;
            else player.AiLowEnemyMilitaryTurns = 0;
            var enemyCity = state.Cities.Find(item => item.OwnerId != player.Id &&
                state.Players.Exists(owner => owner.Id == item.OwnerId &&
                    owner.Slot != PlayerSlot.Neutral));
            var expeditionPresent = enemyCity != null && state.Units.Exists(item =>
                item.OwnerId == player.Id && item.HitPoints > 0 &&
                state.Tiles.Exists(tile => tile.Id == item.TileId && tile.CityId == enemyCity.Id));
            var gainedGround = enemyCity != null && state.Districts.Exists(item =>
                item.CityId == enemyCity.Id && item.ControllerId == player.Id);
            player.AiStalledAttackTurns = expeditionPresent && !gainedGround
                ? player.AiStalledAttackTurns + 1 : 0;
        }

        private static int PlannedImmediateGold(GameState state, List<GameCommand> commands,
            EntityId playerId)
        {
            var total = 0;
            for (var index = 0; index < commands.Count; index++)
            {
                var command = commands[index];
                if (command.PlayerId != playerId) continue;
                if (command.Type == GameCommandType.StartTraining &&
                    Enum.IsDefined(typeof(UnitType), command.PrimaryValue))
                    total += UnitRules.TrainingGold((UnitType)command.PrimaryValue);
                else if (command.Type == GameCommandType.PromoteUnit &&
                         Enum.IsDefined(typeof(UnitType), command.PrimaryValue))
                {
                    var unit = state.Units.Find(item => item.Id == command.SubjectId);
                    if (unit != null)
                        total += Math.Max(0, UnitRules.TrainingGold((UnitType)command.PrimaryValue) -
                                             UnitRules.TrainingGold(unit.Type));
                }
                else if (command.Type == GameCommandType.StartDefenseFacility &&
                         Enum.IsDefined(typeof(DefenseFacilityType), command.PrimaryValue))
                    total += DefenseFacilityResolver.GoldCost((DefenseFacilityType)command.PrimaryValue);
                else if (command.Type == GameCommandType.StartNuclearProject)
                    total += NuclearProjectResolver.StartGold;
                else if (command.Type == GameCommandType.LevyBid)
                {
                    var quote = NeutralLevyResolver.Quote(state, command.PlayerId,
                        command.SubjectId, command.TargetId);
                    if (quote.IsAvailable) total += quote.BasePrice + Math.Max(0, command.PrimaryValue);
                }
                else if (command.Type == GameCommandType.Trade)
                {
                    var target = state.Cities.Find(item => item.Id == command.TargetId);
                    if (target != null && (target.NeutralSpecialization == NeutralCitySpecialization.Science ||
                        target.NeutralSpecialization == NeutralCitySpecialization.Culture))
                    {
                        var quote = NeutralTradeQuoteResolver.Quote(state, command.PlayerId,
                            command.SubjectId, command.TargetId);
                        if (quote.IsAvailable) total += quote.TotalGoldCost;
                    }
                }
            }
            return total;
        }

        private static bool IsHigherInBranch(UnitType current, UnitType target)
        {
            if (current == UnitType.Supply) return target == UnitType.MotorizedSupply;
            if (current == UnitType.MotorizedSupply || UnitRules.IsSupply(target)) return false;
            return (int)target > (int)current;
        }

        private static bool IsNeutralCity(GameState state, CityState city)
        {
            var owner = city == null ? null : state.Players.Find(item => item.Id == city.OwnerId);
            return owner != null && owner.Slot == PlayerSlot.Neutral;
        }

        private static UnitState StrongestUnit(List<UnitState> units, bool supply)
        {
            UnitState best = null;
            for (var index = 0; index < units.Count; index++)
            {
                var candidate = units[index];
                if (UnitRules.IsSupply(candidate.Type) != supply) continue;
                if (best == null || UnitRules.Attack(candidate.Type) > UnitRules.Attack(best.Type) ||
                    (UnitRules.Attack(candidate.Type) == UnitRules.Attack(best.Type) &&
                     candidate.HitPoints > best.HitPoints)) best = candidate;
            }
            return best;
        }

        private static EntityId SelectEnemyTarget(GameState state, PlayerState player,
            CityState enemyCity, bool prioritizeCultureRaid)
        {
            var priorities = prioritizeCultureRaid || player.AiStrategy == PlayerAiStrategy.Culture
                ? new[] { DistrictType.Culture, DistrictType.NuclearFacility, DistrictType.Military,
                    DistrictType.Agriculture, DistrictType.Science, DistrictType.Commerce,
                    DistrictType.Government }
                : new[] { DistrictType.NuclearFacility, DistrictType.Military,
                    DistrictType.Agriculture, DistrictType.Science, DistrictType.Commerce,
                    DistrictType.Culture, DistrictType.Government };
            for (var priority = 0; priority < priorities.Length; priority++)
            {
                var districts = state.Districts.FindAll(item => item.CityId == enemyCity.Id &&
                    item.Type == priorities[priority] && item.RemainingConstructionTurns <= 0 &&
                    item.ControllerId != player.Id);
                districts.Sort((left, right) => CompareAttackTargetDistance(
                    state, player.Id, default, left, right));
                if (districts.Count > 0) return districts[0].TileId;
            }
            return default;
        }

        private static EntityId SelectDefensiveTarget(GameState state, CityState city, UnitState unit)
        {
            var hostile = state.Units.FindAll(item => item.OwnerId != city.OwnerId &&
                state.Players.Exists(player => player.Id == item.OwnerId &&
                    player.Slot != PlayerSlot.Neutral));
            if (hostile.Count == 0) return default;
            var districts = state.Districts.FindAll(item => item.CityId == city.Id &&
                item.ControllerId == city.OwnerId && item.RemainingConstructionTurns <= 0);
            EntityId best = default;
            var bestDistance = int.MaxValue;
            for (var districtIndex = 0; districtIndex < districts.Count; districtIndex++)
            for (var hostileIndex = 0; hostileIndex < hostile.Count; hostileIndex++)
            {
                var distance = CoordinateDistance(state, districts[districtIndex].TileId,
                    hostile[hostileIndex].TileId);
                if (distance < bestDistance || distance == bestDistance &&
                    districts[districtIndex].TileId.CompareTo(best) < 0)
                {
                    bestDistance = distance;
                    best = districts[districtIndex].TileId;
                }
            }
            return best;
        }

        private static EntityId SupplyEscortTarget(GameState state, EntityId playerId,
            EntityId homeCityId, List<UnitState> units)
        {
            UnitState selected = null;
            var bestDistance = -1;
            for (var index = 0; index < units.Count; index++)
            {
                var candidate = units[index];
                if (UnitRules.IsSupply(candidate.Type)) continue;
                var tile = state.Tiles.Find(item => item.Id == candidate.TileId);
                if (tile == null || tile.CityId == homeCityId) continue;
                var distance = CoordinateDistance(state, candidate.TileId,
                    state.Districts.Find(item => item.CityId == homeCityId &&
                        item.Type == DistrictType.Government)?.TileId ?? default);
                if (distance > bestDistance)
                {
                    bestDistance = distance;
                    selected = candidate;
                }
            }
            return selected == null ? default : selected.TileId;
        }

        private static HashSet<EntityId> SelectExpeditionUnits(GameState state, EntityId playerId,
            UnitState retained, EntityId enemyCityId)
        {
            var result = new HashSet<EntityId>();
            var candidates = state.Units.FindAll(item => item.OwnerId == playerId &&
                item.HitPoints > 0 && !UnitRules.IsSupply(item.Type) &&
                (retained == null || item.Id != retained.Id));
            candidates.Sort((left, right) =>
            {
                var power = UnitRules.Attack(right.Type).CompareTo(UnitRules.Attack(left.Type));
                return power != 0 ? power : left.Id.CompareTo(right.Id);
            });
            var requiredPowerTwice = AssaultDefensePower(state, playerId, enemyCityId) * 3;
            var selectedPowerTwice = 0;
            for (var index = 0; index < candidates.Count &&
                 (selectedPowerTwice < requiredPowerTwice || result.Count < 2); index++)
            {
                result.Add(candidates[index].Id);
                selectedPowerTwice += 2 * UnitRules.Attack(candidates[index].Type) *
                    candidates[index].HitPoints / Math.Max(1, UnitRules.MaximumHitPoints(candidates[index].Type));
            }
            return result;
        }

        private static EntityId SelectLimitedRaidTarget(GameState state, PlayerState player,
            CityState city, CityState enemyCity, PlayerAiAssessment assessment, UnitState retained,
            bool prioritizeCultureRaid)
        {
            if (assessment.ThreatLevel >= PlayerAiThreatLevel.Emergency ||
                !NeutralEconomyPlanner.Evaluate(state, city).IsSafe ||
                CountCombat(state, player.Id) < 3) return default;
            var priorities = prioritizeCultureRaid
                ? new[] { DistrictType.Culture, DistrictType.NuclearFacility, DistrictType.Military,
                    DistrictType.Agriculture, DistrictType.Science, DistrictType.Commerce }
                : new[] { DistrictType.NuclearFacility, DistrictType.Military,
                    DistrictType.Agriculture, DistrictType.Science, DistrictType.Commerce,
                    DistrictType.Culture };
            for (var priority = 0; priority < priorities.Length; priority++)
            {
                var candidates = state.Districts.FindAll(item => item.CityId == enemyCity.Id &&
                    item.Type == priorities[priority] && item.RemainingConstructionTurns <= 0 &&
                    item.ControllerId != player.Id);
                candidates.Sort((left, right) => CompareAttackTargetDistance(
                    state, player.Id, retained == null ? default : retained.Id, left, right));
                for (var index = 0; index < candidates.Count; index++)
                    if (CanSupplyLimitedRaid(state, player, city, enemyCity, retained,
                            candidates[index].TileId))
                        return candidates[index].TileId;
            }
            return default;
        }

        private static bool CanSupplyLimitedRaid(GameState state, PlayerState player,
            CityState city, CityState enemyCity, UnitState retained, EntityId targetTileId)
        {
            var source = state.Districts.Find(item => item.CityId == city.Id &&
                item.Type == DistrictType.Government);
            if (source == null) return false;
            var probe = new UnitState { OwnerId = player.Id, TileId = source.TileId, Type = UnitType.Militia };
            var distance = FindMajorWarPath(state, probe, targetTileId, city.Id, enemyCity.Id).Count;
            if (distance <= 0) return false;
            var units = RaidCandidates(state, player.Id, retained);
            if (units.Count < 2) return false;
            var requiredPowerTimesFour = Math.Max(1, TileCombatPower(state, enemyCity.OwnerId,
                targetTileId)) * 5;
            var selectedPowerTimesFour = 0;
            var selected = 0;
            var foodNeeded = 0;
            for (var index = 0; index < units.Count &&
                 (selected < 2 || selectedPowerTimesFour < requiredPowerTimesFour); index++)
            {
                var travelTurns = (distance + UnitRules.Movement(units[index].Type) - 1) /
                                  UnitRules.Movement(units[index].Type);
                var desiredFood = Math.Min(UnitRules.FoodCapacity(state, units[index]), travelTurns + 2);
                foodNeeded += Math.Max(0, desiredFood - units[index].CarriedFood);
                selectedPowerTimesFour += 4 * UnitRules.Attack(units[index].Type) *
                    units[index].HitPoints / Math.Max(1, UnitRules.MaximumHitPoints(units[index].Type));
                selected++;
            }
            var economy = NeutralEconomyPlanner.Evaluate(state, city);
            return selected >= 2 && selectedPowerTimesFour >= requiredPowerTimesFour &&
                   city.StoredFood - economy.FoodReserveRequired >= foodNeeded;
        }

        private static HashSet<EntityId> SelectLimitedRaidUnits(GameState state, EntityId playerId,
            UnitState retained, EntityId targetTileId)
        {
            var result = new HashSet<EntityId>();
            var units = RaidCandidates(state, playerId, retained);
            var tile = state.Tiles.Find(item => item.Id == targetTileId);
            var targetCity = tile == null ? null : state.Cities.Find(item => item.Id == tile.CityId);
            var requiredPowerTimesFour = Math.Max(1, TileCombatPower(state,
                targetCity == null ? default : targetCity.OwnerId, targetTileId)) * 5;
            var selectedPowerTimesFour = 0;
            for (var index = 0; index < units.Count &&
                 (result.Count < 2 || selectedPowerTimesFour < requiredPowerTimesFour); index++)
            {
                result.Add(units[index].Id);
                selectedPowerTimesFour += 4 * UnitRules.Attack(units[index].Type) *
                    units[index].HitPoints / Math.Max(1, UnitRules.MaximumHitPoints(units[index].Type));
            }
            return result;
        }

        private static List<UnitState> RaidCandidates(GameState state, EntityId playerId,
            UnitState retained)
        {
            var units = state.Units.FindAll(item => item.OwnerId == playerId && item.HitPoints > 0 &&
                !UnitRules.IsSupply(item.Type) && (retained == null || item.Id != retained.Id));
            units.Sort((left, right) =>
            {
                var power = UnitRules.Attack(right.Type).CompareTo(UnitRules.Attack(left.Type));
                return power != 0 ? power : left.Id.CompareTo(right.Id);
            });
            return units;
        }

        private static int TileCombatPower(GameState state, EntityId ownerId, EntityId tileId)
        {
            var total = 0;
            for (var index = 0; index < state.Units.Count; index++)
            {
                var unit = state.Units[index];
                if (unit.OwnerId != ownerId || unit.TileId != tileId || unit.HitPoints <= 0 ||
                    UnitRules.IsSupply(unit.Type)) continue;
                total += UnitRules.Attack(unit.Type) * unit.HitPoints /
                         Math.Max(1, UnitRules.MaximumHitPoints(unit.Type));
            }
            return total;
        }

        private static void AddFoodLoadIfUseful(GameState state, PlayerState player, CityState city,
            UnitState unit, int pathLength, List<GameCommand> result)
        {
            var tile = state.Tiles.Find(item => item.Id == unit.TileId);
            if (tile == null || tile.CityId != city.Id || tile.ControllerId != player.Id) return;
            var capacity = UnitRules.FoodCapacity(state, unit);
            var desired = Math.Min(capacity, Math.Max(pathLength + 2, capacity / 2));
            var incoming = 0;
            for (var index = 0; index < result.Count; index++)
                if (result[index].Type == GameCommandType.TransferFood &&
                    result[index].TargetId == unit.Id)
                    incoming += Math.Max(0, result[index].PrimaryValue);
            var amount = Math.Min(desired - unit.CarriedFood - incoming,
                Math.Max(0, city.StoredFood - NeutralEconomyPlanner.Evaluate(state, city).FoodReserveRequired));
            if (amount > 0)
                result.Add(Command(state, player.Id, GameCommandType.LoadFood,
                    unit.Id, city.Id, amount));
        }

        private static bool GovernmentRouteExists(GameState state, EntityId playerId,
            EntityId sourceCityId, EntityId targetCityId)
        {
            var source = state.Districts.Find(item => item.CityId == sourceCityId &&
                item.Type == DistrictType.Government);
            var target = state.Districts.Find(item => item.CityId == targetCityId &&
                item.Type == DistrictType.Government);
            if (source == null || target == null) return false;
            var probe = new UnitState { OwnerId = playerId, TileId = source.TileId, Type = UnitType.Militia };
            return FindMajorWarPath(state, probe, target.TileId, sourceCityId, targetCityId).Count > 0;
        }

        private static bool CanSupplyAttack(GameState state, PlayerState player, CityState city,
            CityState enemyCity, NeutralEconomyProjection economy)
        {
            var source = state.Districts.Find(item => item.CityId == city.Id &&
                item.Type == DistrictType.Government);
            var target = state.Districts.Find(item => item.CityId == enemyCity.Id &&
                item.Type == DistrictType.Government);
            if (source == null || target == null) return false;
            var probe = new UnitState { OwnerId = player.Id, TileId = source.TileId, Type = UnitType.Militia };
            var distance = FindMajorWarPath(state, probe, target.TileId, city.Id, enemyCity.Id).Count;
            if (distance <= 0) return false;
            var units = state.Units.FindAll(item => item.OwnerId == player.Id && item.HitPoints > 0 &&
                !UnitRules.IsSupply(item.Type));
            units.Sort((left, right) =>
            {
                var attack = UnitRules.Attack(right.Type).CompareTo(UnitRules.Attack(left.Type));
                return attack != 0 ? attack : left.Id.CompareTo(right.Id);
            });
            if (units.Count <= 1) return false;
            var retained = units.Find(item => item.TileId == source.TileId);
            var requiredPower = AssaultDefensePower(state, player.Id, enemyCity.Id) * 3;
            var selectedPowerTwice = 0;
            var needed = 0;
            for (var index = 0; index < units.Count && selectedPowerTwice < requiredPower; index++)
            {
                if (retained != null && units[index].Id == retained.Id) continue;
                var travelTurns = (distance + UnitRules.Movement(units[index].Type) - 1) /
                                  UnitRules.Movement(units[index].Type);
                var desired = Math.Min(UnitRules.FoodCapacity(state, units[index]), travelTurns + 2);
                needed += Math.Max(0, desired - units[index].CarriedFood);
                selectedPowerTwice += 2 * UnitRules.Attack(units[index].Type) * units[index].HitPoints /
                                      Math.Max(1, UnitRules.MaximumHitPoints(units[index].Type));
            }
            return selectedPowerTwice >= requiredPower &&
                   city.StoredFood - economy.FoodReserveRequired >= needed;
        }

        private static int ExpeditionPower(GameState state, EntityId playerId, EntityId homeCityId)
        {
            var government = state.Districts.Find(item => item.CityId == homeCityId &&
                item.Type == DistrictType.Government);
            var units = state.Units.FindAll(item => item.OwnerId == playerId && item.HitPoints > 0 &&
                !UnitRules.IsSupply(item.Type));
            UnitState retained = null;
            if (government != null)
                retained = units.Find(item => item.TileId == government.TileId);
            var total = 0;
            for (var index = 0; index < units.Count; index++)
                if (retained == null || units[index].Id != retained.Id)
                    total += UnitRules.Attack(units[index].Type) * units[index].HitPoints /
                             Math.Max(1, UnitRules.MaximumHitPoints(units[index].Type));
            return total;
        }

        private static int GovernmentCombatPower(GameState state, EntityId cityId)
        {
            var city = state.Cities.Find(item => item.Id == cityId);
            var tileId = GovernmentTile(state, cityId);
            return city == null || !tileId.IsValid ? 0 :
                TileCombatPower(state, city.OwnerId, tileId);
        }

        private static int AssaultDefensePower(GameState state, EntityId attackerId,
            EntityId enemyCityId)
        {
            var attacker = state.Players.Find(item => item.Id == attackerId);
            return attacker != null && attacker.AiStrategy == PlayerAiStrategy.Conquest
                ? GovernmentCombatPower(state, enemyCityId)
                : CityCombatPower(state, enemyCityId);
        }

        private static int CityCombatPower(GameState state, EntityId cityId)
        {
            var city = state.Cities.Find(item => item.Id == cityId);
            if (city == null) return 0;
            var total = 0;
            for (var index = 0; index < state.Units.Count; index++)
            {
                var unit = state.Units[index];
                var tile = state.Tiles.Find(item => item.Id == unit.TileId);
                if (tile == null || tile.CityId != cityId || unit.OwnerId != city.OwnerId ||
                    UnitRules.IsSupply(unit.Type) || unit.HitPoints <= 0) continue;
                total += UnitRules.Attack(unit.Type) * unit.HitPoints /
                         Math.Max(1, UnitRules.MaximumHitPoints(unit.Type));
            }
            return total;
        }

        private static EntityId GovernmentTile(GameState state, EntityId cityId)
        {
            var government = state.Districts.Find(item => item.CityId == cityId &&
                item.Type == DistrictType.Government);
            return government == null ? default : government.TileId;
        }

        private static bool HasEnemyAt(GameState state, EntityId ownerId, EntityId tileId) =>
            state.Units.Exists(item => item.TileId == tileId && item.OwnerId != ownerId &&
                item.HitPoints > 0);

        private static int CoordinateDistance(GameState state, EntityId left, EntityId right)
        {
            var a = MapTraversal.GlobalCoordinate(state, left);
            var b = MapTraversal.GlobalCoordinate(state, right);
            return a.HasValue && b.HasValue ? HexCoord.Distance(a.Value, b.Value) : int.MaxValue;
        }

        private static int ComparePurchaseTradeCost(GameState state, EntityId playerId,
            EntityId sourceCityId, CityState left, CityState right)
        {
            var leftQuote = NeutralTradeQuoteResolver.Quote(state, playerId, sourceCityId, left.Id);
            var rightQuote = NeutralTradeQuoteResolver.Quote(state, playerId, sourceCityId, right.Id);
            if (leftQuote.IsAvailable != rightQuote.IsAvailable) return leftQuote.IsAvailable ? -1 : 1;
            var cost = leftQuote.TotalGoldCost.CompareTo(rightQuote.TotalGoldCost);
            if (cost != 0) return cost;
            var distance = TradeDistance(leftQuote.Route).CompareTo(TradeDistance(rightQuote.Route));
            return distance != 0 ? distance : left.Id.CompareTo(right.Id);
        }

        private static int CompareCommerceTradeCost(GameState state, EntityId playerId,
            EntityId sourceCityId, TileResourceType offered, CityState left, CityState right)
        {
            var leftQuote = CommerceTradeQuoteResolver.Quote(
                state, playerId, sourceCityId, left.Id, offered);
            var rightQuote = CommerceTradeQuoteResolver.Quote(
                state, playerId, sourceCityId, right.Id, offered);
            if (leftQuote.IsAvailable != rightQuote.IsAvailable) return leftQuote.IsAvailable ? -1 : 1;
            var cost = leftQuote.RequiredResourceAmount.CompareTo(rightQuote.RequiredResourceAmount);
            if (cost != 0) return cost;
            var distance = TradeDistance(leftQuote.Route).CompareTo(TradeDistance(rightQuote.Route));
            return distance != 0 ? distance : left.Id.CompareTo(right.Id);
        }

        private static int TradeDistance(TradeRouteResult route) =>
            route == null || !route.IsReachable ? int.MaxValue : route.Distance;

        private static int CompareAttackTargetDistance(GameState state, EntityId playerId,
            EntityId excludedUnitId, DistrictState left, DistrictState right)
        {
            var distance = ClosestCombatUnitDistance(state, playerId, excludedUnitId, left.TileId)
                .CompareTo(ClosestCombatUnitDistance(state, playerId, excludedUnitId, right.TileId));
            return distance != 0 ? distance : left.Id.CompareTo(right.Id);
        }

        private static int ClosestCombatUnitDistance(GameState state, EntityId playerId,
            EntityId excludedUnitId, EntityId targetTileId)
        {
            var best = int.MaxValue;
            for (var index = 0; index < state.Units.Count; index++)
            {
                var unit = state.Units[index];
                if (unit.OwnerId != playerId || unit.Id == excludedUnitId || unit.HitPoints <= 0 ||
                    UnitRules.IsSupply(unit.Type)) continue;
                best = Math.Min(best, CoordinateDistance(state, unit.TileId, targetTileId));
            }
            return best;
        }

        private static List<EntityId> FindMajorWarPath(GameState state, UnitState unit,
            EntityId target, EntityId homeCityId, EntityId enemyCityId)
        {
            var byCoordinate = new Dictionary<HexCoord, TileState>();
            for (var index = 0; index < state.Tiles.Count; index++)
            {
                var coordinate = MapTraversal.GlobalCoordinate(state, state.Tiles[index].Id);
                if (coordinate.HasValue) byCoordinate[coordinate.Value] = state.Tiles[index];
            }
            var queue = new Queue<EntityId>();
            var previous = new Dictionary<EntityId, EntityId>();
            queue.Enqueue(unit.TileId);
            previous[unit.TileId] = default;
            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                if (current == target) break;
                var coordinate = MapTraversal.GlobalCoordinate(state, current);
                if (!coordinate.HasValue) continue;
                var directionOffset = (int)(Math.Abs(unit.Id.Value) % 6);
                for (var step = 0; step < 6; step++)
                {
                    var direction = (directionOffset + step) % 6;
                    if (!byCoordinate.TryGetValue(coordinate.Value + HexCoord.Direction(direction),
                            out var tile) || previous.ContainsKey(tile.Id)) continue;
                    if (tile.CityId != homeCityId && tile.CityId != enemyCityId) continue;
                    if (tile.Id != target && !HasBranchCapacity(state, unit, tile.Id)) continue;
                    var blocked = state.Units.Exists(item => item.TileId == tile.Id &&
                        item.OwnerId != unit.OwnerId && item.HitPoints > 0);
                    if (blocked && tile.Id != target) continue;
                    previous[tile.Id] = current;
                    queue.Enqueue(tile.Id);
                }
            }
            if (!previous.ContainsKey(target)) return new List<EntityId>();
            var path = new List<EntityId>();
            for (var cursor = target; cursor != unit.TileId; cursor = previous[cursor]) path.Add(cursor);
            path.Reverse();
            return path;
        }

        private static bool HasBranchCapacity(GameState state, UnitState moving, EntityId tileId)
        {
            var supply = UnitRules.IsSupply(moving.Type);
            var count = state.Units.FindAll(item => item.Id != moving.Id && item.TileId == tileId &&
                UnitRules.IsSupply(item.Type) == supply && item.HitPoints > 0).Count;
            return count < (supply ? UnitRules.SupplyUnitsPerTile : UnitRules.CombatUnitsPerTile);
        }

        private static int CombatPower(GameState state, EntityId ownerId)
        {
            var total = 0;
            for (var index = 0; index < state.Units.Count; index++)
            {
                var unit = state.Units[index];
                if (unit.OwnerId != ownerId || UnitRules.IsSupply(unit.Type) || unit.HitPoints <= 0) continue;
                total += UnitRules.Attack(unit.Type) * unit.HitPoints /
                         Math.Max(1, UnitRules.MaximumHitPoints(unit.Type));
            }
            return total;
        }

        private static int TurnsToCity(GameState state, UnitState unit, EntityId cityId)
        {
            var view = state.MapTopology?.FindView(cityId);
            if (view == null) return int.MaxValue;
            var origin = MapTraversal.GlobalCoordinate(state, unit.TileId);
            if (!origin.HasValue) return int.MaxValue;
            var distance = int.MaxValue;
            for (var index = 0; index < view.Tiles.Count; index++)
            {
                var target = MapTraversal.GlobalCoordinate(state, view.Tiles[index].TileId);
                if (target.HasValue)
                    distance = Math.Min(distance, HexCoord.Distance(origin.Value, target.Value));
            }
            return distance == int.MaxValue ? distance :
                (distance + UnitRules.Movement(unit.Type) - 1) / UnitRules.Movement(unit.Type);
        }

        private static ResearchType FirstAvailable(PlayerState player, IReadOnlyList<ResearchType> order)
        {
            for (var index = 0; index < order.Count; index++)
                if (IsAvailable(player, order[index])) return order[index];
            return ResearchType.None;
        }

        private static bool IsAvailable(PlayerState player, ResearchType type)
        {
            if (type == ResearchType.None || player.CompletedResearch.Contains(type)) return false;
            if (type == ResearchType.SelfLearningAI && !player.HasUnlockedSelfLearningAI) return false;
            var prerequisite = ResearchRules.Prerequisite(type);
            return prerequisite == ResearchType.None || player.CompletedResearch.Contains(prerequisite);
        }

        private static UnitType StrongestCombat(PlayerState player)
        {
            if (player.UnlockedUnitTypes.Contains(UnitType.MechanizedInfantry)) return UnitType.MechanizedInfantry;
            if (player.UnlockedUnitTypes.Contains(UnitType.GunpowderInfantry)) return UnitType.GunpowderInfantry;
            if (player.UnlockedUnitTypes.Contains(UnitType.IronInfantry)) return UnitType.IronInfantry;
            return UnitType.Militia;
        }

        private static UnitType StrongestSupply(PlayerState player) =>
            player.UnlockedUnitTypes.Contains(UnitType.MotorizedSupply)
                ? UnitType.MotorizedSupply : UnitType.Supply;

        private static int CountCombat(GameState state, EntityId ownerId) =>
            state.Units.FindAll(item => item.OwnerId == ownerId && item.HitPoints > 0 &&
                !UnitRules.IsSupply(item.Type)).Count;

        private static int CountCombatAndTraining(GameState state, EntityId ownerId) =>
            CountCombat(state, ownerId) + state.UnitTrainings.FindAll(item => item.OwnerId == ownerId &&
                !UnitRules.IsSupply(item.Type)).Count;

        private static int CountSupplyAndTraining(GameState state, EntityId ownerId) =>
            state.Units.FindAll(item => item.OwnerId == ownerId && item.HitPoints > 0 &&
                UnitRules.IsSupply(item.Type)).Count + state.UnitTrainings.FindAll(item =>
                item.OwnerId == ownerId && UnitRules.IsSupply(item.Type)).Count;

        private static int CountDistricts(GameState state, EntityId cityId, DistrictType type) =>
            state.Districts.FindAll(item => item.CityId == cityId && item.Type == type).Count;

        private static bool HasDistrict(GameState state, EntityId cityId, DistrictType type) =>
            state.Districts.Exists(item => item.CityId == cityId && item.Type == type);

        private static bool HasPlanned(List<GameCommand> commands, EntityId playerId,
            EntityId cityId, DistrictType type) =>
            CountPlanned(commands, playerId, cityId, type) > 0;

        private static int CountPlanned(List<GameCommand> commands, EntityId playerId,
            EntityId cityId, DistrictType type) =>
            commands.FindAll(item => item.Type == GameCommandType.StartDistrict &&
                item.PlayerId == playerId && item.SubjectId == cityId &&
                item.PrimaryValue == (int)type).Count;

        private static bool IsDistrictUnlocked(PlayerState player, DistrictType type) =>
            player.UnlockedDistrictTypes.Contains(type);

        private static EntityId SelectTile(GameState state, CityState city, DistrictType type,
            HashSet<EntityId> reserved)
        {
            var view = state.MapTopology?.FindView(city.Id);
            if (view == null) return default;
            CityTilePlacement best = null;
            var desired = type == DistrictType.Agriculture ? TileResourceType.Food :
                type == DistrictType.Commerce ? TileResourceType.Commerce :
                type == DistrictType.Science ? TileResourceType.Science :
                type == DistrictType.Culture ? TileResourceType.Culture : TileResourceType.None;
            for (var index = 0; index < view.Tiles.Count; index++)
            {
                var candidate = view.Tiles[index];
                if (!candidate.IsBuildable || reserved.Contains(candidate.TileId) ||
                    state.Districts.Exists(item => item.TileId == candidate.TileId)) continue;
                if (best == null || TileScore(state, city, type, candidate, desired) >
                    TileScore(state, city, type, best, desired) ||
                    (TileScore(state, city, type, candidate, desired) ==
                     TileScore(state, city, type, best, desired) && candidate.TileId.CompareTo(best.TileId) < 0))
                    best = candidate;
            }
            return best == null ? default : best.TileId;
        }

        private static int TileScore(GameState state, CityState city, DistrictType type,
            CityTilePlacement placement, TileResourceType desired)
        {
            var tile = state.Tiles.Find(item => item.Id == placement.TileId);
            var score = tile != null && desired != TileResourceType.None && tile.ResourceType == desired ? 100 : 0;
            if (type != DistrictType.Commerce && type != DistrictType.Science && type != DistrictType.Culture)
                return score - HexCoord.Distance(new HexCoord(0, 0),
                    new HexCoord(placement.LocalQ, placement.LocalR));
            for (var index = 0; index < state.Districts.Count; index++)
            {
                var district = state.Districts[index];
                if (district.CityId != city.Id || district.Type != type) continue;
                var other = state.MapTopology.FindView(city.Id).Tiles.Find(item => item.TileId == district.TileId);
                if (other != null && HexCoord.Distance(new HexCoord(placement.LocalQ, placement.LocalR),
                    new HexCoord(other.LocalQ, other.LocalR)) == 1) score += 20;
            }
            return score;
        }

        private static bool EnemyHasNuclearProgram(GameState state, EntityId playerId)
        {
            var enemy = state.Players.Find(item => item.Slot != PlayerSlot.Neutral && item.Id != playerId);
            return enemy != null && (enemy.CompletedResearch.Contains(ResearchType.NuclearFission) ||
                enemy.HasCompletedNuclearProject || state.NuclearProjects.Exists(item => item.OwnerId == enemy.Id));
        }

        private static int EstimatedCultureLossTurns(GameState state, EntityId playerId)
        {
            var city = state.Cities.Find(item => item.OwnerId == playerId);
            var enemy = state.Players.Find(item => item.Slot != PlayerSlot.Neutral && item.Id != playerId);
            var enemyCity = enemy == null ? null : state.Cities.Find(item => item.OwnerId == enemy.Id);
            if (city == null || enemyCity == null) return -1;
            var influence = city.CultureInfluences.Find(item => item.CultureOwnerId == enemy.Id);
            var preferred = influence == null ? 0 : influence.PreferredCitizens;
            var progress = influence == null ? 0 : influence.ConversionProgress;
            var difference = enemyCity.LastCultureProduction - city.LastCultureProduction;
            // Trade can add culture after the economy snapshot and before conversion.  If conversion
            // is already present, retain a conservative one-point pressure estimate instead of
            // declaring the threat gone during the following command phase.
            if (difference <= 0)
            {
                if (preferred <= 0 && progress <= 0) return -1;
                difference = 1;
            }
            var neededCitizens = city.Population / 2 + 1 - preferred;
            if (neededCitizens <= 0) return 0;
            var neededProgress = neededCitizens * 10 - progress;
            return Math.Max(1, (neededProgress + difference - 1) / difference);
        }

        private static GameCommand Command(GameState state, EntityId playerId, GameCommandType type,
            EntityId subject = default, EntityId target = default, int primary = 0, int secondary = 0)
        {
            return new GameCommand
            {
                CommandId = state.AllocateId(), PlayerId = playerId, TurnNumber = state.TurnNumber,
                Type = type, SubjectId = subject, TargetId = target,
                PrimaryValue = primary, SecondaryValue = secondary
            };
        }
    }
}
