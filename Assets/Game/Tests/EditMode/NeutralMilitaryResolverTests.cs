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
            Assert.That(NeutralMilitaryResolver.CombatTarget(NeutralCitySpecialization.Military,
                NeutralDevelopmentStage.Early), Is.EqualTo(2));
            Assert.That(NeutralMilitaryResolver.CombatTarget(NeutralCitySpecialization.Military,
                NeutralDevelopmentStage.Middle), Is.EqualTo(3));
            Assert.That(NeutralMilitaryResolver.CombatTarget(NeutralCitySpecialization.Military,
                NeutralDevelopmentStage.Late), Is.EqualTo(6));
            Assert.That(NeutralMilitaryResolver.SupplyTarget(NeutralCitySpecialization.Military,
                NeutralDevelopmentStage.Early), Is.Zero);
            Assert.That(NeutralMilitaryResolver.SupplyTarget(NeutralCitySpecialization.Military,
                NeutralDevelopmentStage.Middle), Is.EqualTo(1));
            Assert.That(NeutralMilitaryResolver.SupplyTarget(NeutralCitySpecialization.Military,
                NeutralDevelopmentStage.Late), Is.EqualTo(2));
            Assert.That(NeutralMilitaryResolver.CombatTarget(NeutralCitySpecialization.Commerce,
                NeutralDevelopmentStage.Late), Is.EqualTo(4));
            Assert.That(NeutralMilitaryResolver.CombatTarget(NeutralCitySpecialization.Science,
                NeutralDevelopmentStage.Middle), Is.EqualTo(2));
            Assert.That(NeutralMilitaryResolver.CombatTarget(NeutralCitySpecialization.Culture,
                NeutralDevelopmentStage.Late), Is.EqualTo(3));
            foreach (var specialization in new[] { NeutralCitySpecialization.Commerce,
                         NeutralCitySpecialization.Science, NeutralCitySpecialization.Culture })
                Assert.That(NeutralMilitaryResolver.SupplyTarget(specialization,
                    NeutralDevelopmentStage.Late), Is.Zero);
        }

        [Test]
        public void MilitaryCityPromotesHomeMilitiaBeforeStartingMissingTraining()
        {
            var state = PrototypeMatchFactory.Create(13300);
            var city = NeutralCity(state, NeutralCitySpecialization.Military);
            var militia = state.Units.Single(item => item.HomeCityId == city.Id);
            militia.RemainingMovement = UnitRules.Movement(militia.Type);
            city.NeutralCompletedResearch.Add(ResearchType.IronWorking);
            MakeEconomicallySafe(city);
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
            MakeEconomicallySafe(city);
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
            MakeEconomicallySafe(cities[0]);
            MakeEconomicallySafe(cities[1]);
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
        public void HostileIntruderKeepsEmergencyGuardAndMovesAnotherDefender()
        {
            var state = PrototypeMatchFactory.Create(13304);
            var city = NeutralCity(state, NeutralCitySpecialization.Science);
            var defender = state.Units.Single(item => item.HomeCityId == city.Id);
            defender.RemainingMovement = UnitRules.Movement(defender.Type);
            defender.CreatedTurn = state.TurnNumber - 1;
            var reinforcementTile = state.MapTopology.FindView(city.Id).Tiles
                .First(item => item.IsBuildable).TileId;
            var reinforcement = new UnitState
            {
                Id = state.AllocateId(), OwnerId = city.OwnerId, HomeCityId = city.Id,
                TileId = reinforcementTile, Type = UnitType.Militia, HitPoints = 16,
                RemainingMovement = 2, CreatedTurn = state.TurnNumber - 1
            };
            state.Units.Add(reinforcement);
            var intruderOwner = state.Players.Single(item => item.Slot == PlayerSlot.PlayerOne);
            NeutralCityRules.SetFavor(city, intruderOwner.Id, -10);
            var targetTile = state.MapTopology.FindView(city.Id).Tiles
                .First(item => item.IsBuildable && item.TileId != reinforcementTile).TileId;
            state.Units.Add(new UnitState
            {
                Id = state.AllocateId(), OwnerId = intruderOwner.Id,
                HomeCityId = state.Cities.Single(item => item.OwnerId == intruderOwner.Id).Id,
                TileId = targetTile, Type = UnitType.Militia, HitPoints = 16
            });

            var result = NeutralMilitaryResolver.IssueOrders(state);
            var movement = result.Movements.Single(item => item.SubjectId == reinforcement.Id);

            Assert.That(result.Movements.Any(item => item.SubjectId == defender.Id), Is.False);
            Assert.That(movement.TargetId, Is.EqualTo(targetTile));
            Assert.That(movement.SecondaryValue, Is.EqualTo(1));
        }

        [Test]
        public void OccupiedDistrictIsRecapturedWithoutAbandoningGovernmentGuard()
        {
            var state = PrototypeMatchFactory.Create(13305);
            var city = NeutralCity(state, NeutralCitySpecialization.Science);
            var guard = state.Units.Single(item => item.HomeCityId == city.Id);
            guard.RemainingMovement = UnitRules.Movement(guard.Type);
            guard.CreatedTurn = state.TurnNumber - 1;
            var occupied = AddOperationalDistrict(state, city, DistrictType.Science);
            var attacker = state.Players.Single(item => item.Slot == PlayerSlot.PlayerOne);
            occupied.ControllerId = attacker.Id;
            occupied.IsOperational = false;
            state.Tiles.Single(item => item.Id == occupied.TileId).ControllerId = attacker.Id;
            var reinforcementTile = state.MapTopology.FindView(city.Id).Tiles.First(item =>
                item.IsBuildable && item.TileId != occupied.TileId &&
                state.Districts.All(district => district.TileId != item.TileId));
            var reinforcement = new UnitState
            {
                Id = state.AllocateId(), OwnerId = city.OwnerId, HomeCityId = city.Id,
                TileId = reinforcementTile.TileId, Type = UnitType.Militia, HitPoints = 16,
                RemainingMovement = 2, CreatedTurn = state.TurnNumber - 1
            };
            state.Units.Add(reinforcement);

            var result = NeutralMilitaryResolver.IssueOrders(state);

            Assert.That(result.Movements.Any(item => item.SubjectId == guard.Id), Is.False);
            Assert.That(result.Movements.Single(item => item.SubjectId == reinforcement.Id).TargetId,
                Is.EqualTo(occupied.TileId));
        }

        [Test]
        public void TrainingIsDeferredWhenFoodAndGoldCannotCoverTheNewUnit()
        {
            var state = PrototypeMatchFactory.Create(13306);
            var city = NeutralCity(state, NeutralCitySpecialization.Military);
            city.Gold = 0;
            city.StoredFood = 0;
            city.Population = 12;

            Assert.That(NeutralMilitaryResolver.CanSustainTraining(state, city, UnitType.Militia),
                Is.False);
        }

        [Test]
        public void TrainingRequiresPlusOneNetAndTwoTurnsOfReserves()
        {
            var state = PrototypeMatchFactory.Create(13307);
            var city = NeutralCity(state, NeutralCitySpecialization.Military);
            city.Gold = 100;
            city.StoredFood = 100;

            Assert.That(NeutralMilitaryResolver.CanSustainTraining(state, city, UnitType.Militia),
                Is.False, "기본 생산은 새 민병대 이후 순식량·순금이 0이므로 훈련하면 안 된다.");

            city.TestGovernmentFoodBonus = 1;
            city.TestGovernmentGoldBonus = 1;
            Assert.That(NeutralMilitaryResolver.CanSustainTraining(state, city, UnitType.Militia),
                Is.True);

            city.StoredFood = 0;
            Assert.That(NeutralMilitaryResolver.CanSustainTraining(state, city, UnitType.Militia),
                Is.False, "순생산이 안전해도 2턴 식량 비축이 없으면 훈련하면 안 된다.");
        }

        [Test]
        public void EconomyForecastIncludesLivingUnitsScheduledToReturnFromLevy()
        {
            var state = PrototypeMatchFactory.Create(13308);
            var city = NeutralCity(state, NeutralCitySpecialization.Military);
            city.Gold = 100;
            city.StoredFood = 100;
            var player = state.Players.Single(item => item.Slot == PlayerSlot.PlayerOne);
            var payment = state.Cities.Single(item => item.OwnerId == player.Id);
            var returning = new UnitState
            {
                Id = state.AllocateId(), OwnerId = player.Id, HomeCityId = payment.Id,
                TileId = state.Districts.Single(item => item.CityId == payment.Id &&
                    item.Type == DistrictType.Government).TileId,
                Type = UnitType.Militia, HitPoints = UnitRules.MaximumHitPoints(UnitType.Militia)
            };
            state.Units.Add(returning);
            var levy = new LevyState
            {
                Id = state.AllocateId(), MilitaryCityId = city.Id, PlayerId = player.Id,
                PaymentCityId = payment.Id, EndTurnExclusive = state.TurnNumber + 1
            };
            levy.Units.Add(new LevyUnitState { UnitId = returning.Id, OriginalHomeCityId = city.Id });
            state.Levies.Add(levy);

            var projection = NeutralEconomyPlanner.Evaluate(state, city);

            Assert.That(projection.FoodNet, Is.Zero);
            Assert.That(projection.GoldNet, Is.Zero);
            Assert.That(projection.IsSafe, Is.False);
        }

        [Test]
        public void HostileUnitInsideTerritoryRaisesTemporaryForceTargetWithoutNonMilitarySupply()
        {
            var state = PrototypeMatchFactory.Create(13309);
            var military = NeutralCity(state, NeutralCitySpecialization.Military);
            var science = NeutralCity(state, NeutralCitySpecialization.Science);
            var player = state.Players.Single(item => item.Slot == PlayerSlot.PlayerOne);
            foreach (var city in new[] { military, science })
            {
                NeutralCityRules.SetFavor(city, player.Id, -10);
                var tile = state.MapTopology.FindView(city.Id).Tiles.First(item => item.IsBuildable);
                state.Units.Add(new UnitState
                {
                    Id = state.AllocateId(), OwnerId = player.Id,
                    HomeCityId = state.Cities.Single(item => item.OwnerId == player.Id).Id,
                    TileId = tile.TileId, Type = UnitType.Militia,
                    HitPoints = UnitRules.MaximumHitPoints(UnitType.Militia)
                });
            }

            var militaryTarget = NeutralMilitaryResolver.ForceTarget(state, military);
            var scienceTarget = NeutralMilitaryResolver.ForceTarget(state, science);

            Assert.That(militaryTarget.Combat, Is.EqualTo(4));
            Assert.That(militaryTarget.Supply, Is.EqualTo(1));
            Assert.That(militaryTarget.IsCriticalThreat, Is.True);
            Assert.That(scienceTarget.Combat, Is.EqualTo(3));
            Assert.That(scienceTarget.Supply, Is.Zero);
            Assert.That(scienceTarget.IsCriticalThreat, Is.True);
        }

        [Test]
        public void MilitaryNeutralCitiesRemainEconomicallySafeAfter150IdleTurns()
        {
            var state = PrototypeMatchFactory.Create(20260831);
            var processor = new TurnProcessor();
            for (var turn = 0; turn < 150; turn++)
                processor.Resolve(state, new GameCommand[0]);

            var cities = state.Cities.Where(item => item.NeutralSpecialization ==
                NeutralCitySpecialization.Military).ToArray();
            foreach (var city in cities)
            {
                var projection = NeutralEconomyPlanner.Evaluate(state, city);
                Assert.That(city.Population, Is.GreaterThan(6));
                Assert.That(projection.FoodNet, Is.GreaterThanOrEqualTo(1));
                Assert.That(projection.GoldNet, Is.GreaterThanOrEqualTo(1));
                Assert.That(city.StoredFood, Is.GreaterThanOrEqualTo(projection.FoodReserveRequired));
                Assert.That(city.Gold, Is.GreaterThanOrEqualTo(projection.GoldReserveRequired));
                Assert.That(state.Districts.Where(item => item.CityId == city.Id)
                    .Any(item => item.IsMaintenanceSuspended), Is.False);
            }
        }

        private static CityState NeutralCity(GameState state, NeutralCitySpecialization specialization) =>
            state.Cities.First(item => item.NeutralSpecialization == specialization);

        private static void MakeEconomicallySafe(CityState city)
        {
            city.Gold = 1000;
            city.StoredFood = 1000;
            city.TestGovernmentFoodBonus = 20;
            city.TestGovernmentGoldBonus = 20;
        }

        private static DistrictState AddOperationalMilitaryDistrict(GameState state, CityState city)
        {
            return AddOperationalDistrict(state, city, DistrictType.Military);
        }

        private static DistrictState AddOperationalDistrict(GameState state, CityState city, DistrictType type)
        {
            var tile = state.MapTopology.FindView(city.Id).Tiles.First(item => item.IsBuildable &&
                state.Districts.All(district => district.TileId != item.TileId));
            var district = new DistrictState
            {
                Id = state.AllocateId(), CityId = city.Id, TileId = tile.TileId,
                Type = type, ControllerId = city.OwnerId,
                IsOperational = true, AssignedCitizens = 1
            };
            state.Districts.Add(district);
            return district;
        }
    }
}
