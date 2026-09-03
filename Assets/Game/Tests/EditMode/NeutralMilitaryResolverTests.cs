using System.Linq;
using LittleCiv.Core;
using NUnit.Framework;

namespace LittleCiv.Tests
{
    public sealed class NeutralMilitaryResolverTests
    {
        [Test]
        public void SpecializationsUseTheirProvisionalMinimumDefenseTargets()
        {
            Assert.That(NeutralMilitaryResolver.CombatTarget(NeutralCitySpecialization.Military), Is.EqualTo(3));
            Assert.That(NeutralMilitaryResolver.SupplyTarget(NeutralCitySpecialization.Military), Is.EqualTo(1));
            Assert.That(NeutralMilitaryResolver.CombatTarget(NeutralCitySpecialization.Commerce), Is.EqualTo(2));
            Assert.That(NeutralMilitaryResolver.SupplyTarget(NeutralCitySpecialization.Commerce), Is.EqualTo(1));
            Assert.That(NeutralMilitaryResolver.CombatTarget(NeutralCitySpecialization.Science), Is.EqualTo(1));
            Assert.That(NeutralMilitaryResolver.SupplyTarget(NeutralCitySpecialization.Science), Is.Zero);
            Assert.That(NeutralMilitaryResolver.CombatTarget(NeutralCitySpecialization.Culture), Is.EqualTo(1));
            Assert.That(NeutralMilitaryResolver.SupplyTarget(NeutralCitySpecialization.Culture), Is.Zero);
        }

        [Test]
        public void MilitaryCityPromotesHomeMilitiaBeforeStartingMissingTraining()
        {
            var state = PrototypeMatchFactory.Create(13300);
            var city = NeutralCity(state, NeutralCitySpecialization.Military);
            var militia = state.Units.Single(item => item.HomeCityId == city.Id);
            militia.RemainingMovement = UnitRules.Movement(militia.Type);
            city.NeutralCompletedResearch.Add(ResearchType.IronWorking);
            city.Gold = 100;
            AddOperationalMilitaryDistrict(state, city);

            var result = NeutralMilitaryResolver.IssueOrders(state);

            Assert.That(militia.Type, Is.EqualTo(UnitType.IronInfantry));
            Assert.That(militia.RemainingMovement, Is.Zero);
            Assert.That(result.Promotions.Any(item => item.UnitId == militia.Id), Is.True);
            Assert.That(result.Trainings.Any(item => item.Type == UnitType.IronInfantry), Is.True);
        }

        [Test]
        public void PendingTrainingCountsTowardTargetAndStrongestCityResearchIsUsed()
        {
            var state = PrototypeMatchFactory.Create(13301);
            var city = NeutralCity(state, NeutralCitySpecialization.Military);
            city.NeutralCompletedResearch.Add(ResearchType.IronWorking);
            city.NeutralCompletedResearch.Add(ResearchType.Gunpowder);
            city.Gold = 100;
            var first = AddOperationalMilitaryDistrict(state, city);
            var second = AddOperationalMilitaryDistrict(state, city);

            var firstResult = NeutralMilitaryResolver.IssueOrders(state);
            var trainingCount = state.UnitTrainings.Count(item =>
                item.DistrictId == first.Id || item.DistrictId == second.Id);
            var secondResult = NeutralMilitaryResolver.IssueOrders(state);

            Assert.That(firstResult.Trainings.All(item => item.Type == UnitType.GunpowderInfantry), Is.True);
            Assert.That(trainingCount, Is.EqualTo(2));
            Assert.That(secondResult.Trainings, Is.Empty);
        }

        [Test]
        public void ResearchInOneNeutralCityDoesNotUnlockAnotherCityUnits()
        {
            var state = PrototypeMatchFactory.Create(13302);
            var cities = state.Cities.Where(item => item.NeutralSpecialization ==
                NeutralCitySpecialization.Military).ToArray();
            cities[0].NeutralCompletedResearch.Add(ResearchType.IronWorking);
            cities[0].Gold = cities[1].Gold = 100;
            state.Units.Single(item => item.HomeCityId == cities[0].Id).RemainingMovement = 2;
            state.Units.Single(item => item.HomeCityId == cities[1].Id).RemainingMovement = 2;
            AddOperationalMilitaryDistrict(state, cities[0]);
            AddOperationalMilitaryDistrict(state, cities[1]);

            NeutralMilitaryResolver.IssueOrders(state);

            Assert.That(state.Units.Single(item => item.HomeCityId == cities[0].Id).Type,
                Is.EqualTo(UnitType.IronInfantry));
            Assert.That(state.Units.Single(item => item.HomeCityId == cities[1].Id).Type,
                Is.EqualTo(UnitType.Militia));
            Assert.That(state.UnitTrainings.Single(item =>
                state.Districts.Single(district => district.Id == item.DistrictId).CityId == cities[1].Id).Type,
                Is.EqualTo(UnitType.Militia));
        }

        [Test]
        public void ReturnedLevyOutsideHomeTerritoryReceivesWalkingReturnOrder()
        {
            var state = PrototypeMatchFactory.Create(13303);
            var city = NeutralCity(state, NeutralCitySpecialization.Military);
            var unit = state.Units.Single(item => item.HomeCityId == city.Id);
            var player = state.Players.Single(item => item.Slot == PlayerSlot.PlayerOne);
            var playerCity = state.Cities.Single(item => item.OwnerId == player.Id);
            unit.TileId = state.Districts.Single(item => item.CityId == playerCity.Id &&
                item.Type == DistrictType.Government).TileId;
            unit.RemainingMovement = UnitRules.Movement(unit.Type);
            unit.CreatedTurn = state.TurnNumber - 1;

            var result = NeutralMilitaryResolver.IssueOrders(state);
            var movement = result.Movements.Single(item => item.SubjectId == unit.Id);
            var government = state.Districts.Single(item => item.CityId == city.Id &&
                item.Type == DistrictType.Government);

            Assert.That(movement.TargetId, Is.EqualTo(government.TileId));
            Assert.That(movement.Path, Is.Not.Empty);
        }

        [Test]
        public void HostileIntruderCausesNeutralDefensiveMovement()
        {
            var state = PrototypeMatchFactory.Create(13304);
            var city = NeutralCity(state, NeutralCitySpecialization.Science);
            var defender = state.Units.Single(item => item.HomeCityId == city.Id);
            defender.RemainingMovement = UnitRules.Movement(defender.Type);
            defender.CreatedTurn = state.TurnNumber - 1;
            var intruderOwner = state.Players.Single(item => item.Slot == PlayerSlot.PlayerOne);
            NeutralCityRules.SetFavor(city, intruderOwner.Id, -10);
            var targetTile = state.MapTopology.FindView(city.Id).Tiles.First(item => item.IsBuildable).TileId;
            state.Units.Add(new UnitState
            {
                Id = state.AllocateId(), OwnerId = intruderOwner.Id,
                HomeCityId = state.Cities.Single(item => item.OwnerId == intruderOwner.Id).Id,
                TileId = targetTile, Type = UnitType.Militia, HitPoints = 16
            });

            var movement = NeutralMilitaryResolver.IssueOrders(state).Movements
                .Single(item => item.SubjectId == defender.Id);

            Assert.That(movement.TargetId, Is.EqualTo(targetTile));
            Assert.That(movement.SecondaryValue, Is.EqualTo(1));
        }

        private static CityState NeutralCity(GameState state, NeutralCitySpecialization specialization) =>
            state.Cities.First(item => item.NeutralSpecialization == specialization);

        private static DistrictState AddOperationalMilitaryDistrict(GameState state, CityState city)
        {
            var tile = state.MapTopology.FindView(city.Id).Tiles.First(item => item.IsBuildable &&
                state.Districts.All(district => district.TileId != item.TileId));
            var district = new DistrictState
            {
                Id = state.AllocateId(), CityId = city.Id, TileId = tile.TileId,
                Type = DistrictType.Military, ControllerId = city.OwnerId,
                IsOperational = true, AssignedCitizens = 1
            };
            state.Districts.Add(district);
            return district;
        }
    }
}
