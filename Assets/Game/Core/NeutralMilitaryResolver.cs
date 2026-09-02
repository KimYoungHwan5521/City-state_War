using System;
using System.Collections.Generic;

namespace LittleCiv.Core
{
    public sealed class NeutralMilitaryResult
    {
        public readonly List<UnitPromotionResult> Promotions = new List<UnitPromotionResult>();
        public readonly List<UnitTrainingState> Trainings = new List<UnitTrainingState>();
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
            }
            return result;
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
