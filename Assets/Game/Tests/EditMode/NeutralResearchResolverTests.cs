using System.Linq;
using LittleCiv.Core;
using NUnit.Framework;

namespace LittleCiv.Tests
{
    public sealed class NeutralResearchResolverTests
    {
        [Test]
        public void EveryNeutralCityCompletesSchoolIndependentlyOnThirdScienceTurn()
        {
            var state = PrototypeMatchFactory.Create(13200);
            var neutral = state.Players.Single(item => item.Slot == PlayerSlot.Neutral);
            var cities = state.Cities.Where(item => item.OwnerId == neutral.Id).ToArray();
            var processor = new TurnProcessor();

            processor.Resolve(state, new GameCommand[0]);
            processor.Resolve(state, new GameCommand[0]);
            var third = processor.Resolve(state, new GameCommand[0]);

            Assert.That(cities.All(item => item.NeutralCompletedResearch.Contains(ResearchType.School)), Is.True);
            Assert.That(third.Events.Count(item => item.Type == GameEventType.NeutralResearchCompleted &&
                item.PrimaryValue == (int)ResearchType.School), Is.EqualTo(8));
            Assert.That(neutral.CompletedResearch, Is.Empty);
        }

        [Test]
        public void EverySpecializationFinishesLowerCostResearchBeforeNuclearFission()
        {
            foreach (var specialization in new[]
                     {
                         NeutralCitySpecialization.Military, NeutralCitySpecialization.Science,
                         NeutralCitySpecialization.Culture, NeutralCitySpecialization.Commerce
                     })
            {
                var order = NeutralResearchResolver.OrderFor(specialization).ToArray();
                Assert.That(order.Length, Is.EqualTo(19));
                Assert.That(order.Distinct().Count(), Is.EqualTo(order.Length));
                Assert.That(order.Last(), Is.EqualTo(ResearchType.NuclearFission));
                for (var index = 1; index < order.Length; index++)
                    Assert.That(ResearchRules.Cost(order[index]),
                        Is.GreaterThanOrEqualTo(ResearchRules.Cost(order[index - 1])));
            }
        }

        [Test]
        public void DifferentSpecializationsChooseDifferentSecondResearch()
        {
            AssertSecond(NeutralCitySpecialization.Military, ResearchType.IronWorking);
            AssertSecond(NeutralCitySpecialization.Science, ResearchType.Irrigation);
            AssertSecond(NeutralCitySpecialization.Culture, ResearchType.Arts);
            AssertSecond(NeutralCitySpecialization.Commerce, ResearchType.Currency);
        }

        [Test]
        public void EqualCostResearchPrioritizesEachCitySpecialization()
        {
            var military = NeutralResearchResolver.OrderFor(NeutralCitySpecialization.Military).ToList();
            var culture = NeutralResearchResolver.OrderFor(NeutralCitySpecialization.Culture).ToList();
            var commerce = NeutralResearchResolver.OrderFor(NeutralCitySpecialization.Commerce).ToList();

            Assert.That(military.IndexOf(ResearchType.IronWorking),
                Is.LessThan(military.IndexOf(ResearchType.Arts)));
            Assert.That(military.IndexOf(ResearchType.Gunpowder),
                Is.LessThan(military.IndexOf(ResearchType.Printing)));
            Assert.That(culture.IndexOf(ResearchType.Arts),
                Is.LessThan(culture.IndexOf(ResearchType.IronWorking)));
            Assert.That(culture.IndexOf(ResearchType.Printing),
                Is.LessThan(culture.IndexOf(ResearchType.Gunpowder)));
            Assert.That(commerce.IndexOf(ResearchType.Currency),
                Is.LessThan(commerce.IndexOf(ResearchType.IronWorking)));
            Assert.That(commerce.IndexOf(ResearchType.Finance),
                Is.LessThan(commerce.IndexOf(ResearchType.Gunpowder)));
        }

        [Test]
        public void NeutralResearchStateSurvivesCopyAndChangesHash()
        {
            var state = PrototypeMatchFactory.Create(13201);
            var neutral = state.Players.Single(item => item.Slot == PlayerSlot.Neutral);
            var city = state.Cities.First(item => item.OwnerId == neutral.Id);
            city.NeutralCurrentResearch = ResearchType.School;
            city.NeutralResearchProgress.Add(new ResearchProgressState
                { Type = ResearchType.School, Progress = 2 });
            var copy = GameStateCopy.Clone(state);

            Assert.That(GameStateHasher.Compute(copy), Is.EqualTo(GameStateHasher.Compute(state)));
            copy.Cities.Single(item => item.Id == city.Id).NeutralResearchProgress[0].Progress++;
            Assert.That(GameStateHasher.Compute(copy), Is.Not.EqualTo(GameStateHasher.Compute(state)));
        }

        [Test]
        public void NeutralCityUsesItsOwnResearchForEconomicEffect()
        {
            var state = PrototypeMatchFactory.Create(13202);
            var neutral = state.Players.Single(item => item.Slot == PlayerSlot.Neutral);
            var city = state.Cities.First(item => item.OwnerId == neutral.Id);
            var placement = state.MapTopology.FindView(city.Id).Tiles.First(item => item.IsBuildable &&
                state.Districts.All(district => district.TileId != item.TileId));
            state.Districts.Add(new DistrictState
            {
                Id = state.AllocateId(), CityId = city.Id, TileId = placement.TileId,
                Type = DistrictType.Commerce, ControllerId = city.OwnerId,
                IsOperational = true, AssignedCitizens = 1
            });
            city.NeutralCompletedResearch.Add(ResearchType.Currency);

            var economy = CityEconomyResolver.CalculateBreakdown(state, city);

            Assert.That(economy.Gold.ResearchBonus, Is.EqualTo(1));
            Assert.That(neutral.CompletedResearch.Contains(ResearchType.Currency), Is.False);
        }

        [Test]
        public void NeutralUnitFoodCapacityUsesItsHomeCityPreservationResearch()
        {
            var state = PrototypeMatchFactory.Create(13203);
            var neutral = state.Players.Single(item => item.Slot == PlayerSlot.Neutral);
            var cities = state.Cities.Where(item => item.OwnerId == neutral.Id).Take(2).ToArray();
            var first = state.Units.Single(item => item.HomeCityId == cities[0].Id);
            var second = state.Units.Single(item => item.HomeCityId == cities[1].Id);
            cities[0].NeutralCompletedResearch.Add(ResearchType.Canning);

            Assert.That(UnitRules.FoodCapacity(state, first), Is.EqualTo(12));
            Assert.That(UnitRules.FoodCapacity(state, second), Is.EqualTo(6));
        }

        [Test]
        public void EveryNeutralSpecializationEventuallyCompletesFissionAndBuildsOneFacilityWhenUnopposed()
        {
            var state = PrototypeMatchFactory.Create(20260907);
            var processor = new TurnProcessor();
            for (var turn = 0; turn < 200; turn++)
                processor.Resolve(state, new GameCommand[0]);

            var neutral = state.Players.Single(item => item.Slot == PlayerSlot.Neutral);
            var cities = state.Cities.Where(item => item.OwnerId == neutral.Id).ToArray();
            Assert.That(cities.Length, Is.EqualTo(8));
            Assert.That(cities.All(item => item.NeutralCompletedResearch.Contains(
                ResearchType.NuclearFission)), Is.True);
            foreach (var city in cities)
            {
                var facilities = state.Districts.Where(item => item.CityId == city.Id &&
                    item.Type == DistrictType.NuclearFacility).ToArray();
                Assert.That(facilities.Length, Is.EqualTo(1));
                Assert.That(facilities[0].RemainingConstructionTurns, Is.Zero);
                Assert.That(facilities[0].IsOperational, Is.True);
            }
            Assert.That(state.NuclearProjects, Is.Empty);
        }

        [Test]
        public void SinglePlayerFactoryAssignsOnlyPlayerTwoAiAndPreservesItInCopyAndHash()
        {
            var state = PrototypeMatchFactory.CreateSinglePlayer(13204, PlayerAiStrategy.Science);
            var one = state.Players.Single(item => item.Slot == PlayerSlot.PlayerOne);
            var two = state.Players.Single(item => item.Slot == PlayerSlot.PlayerTwo);

            Assert.That(one.AiStrategy, Is.EqualTo(PlayerAiStrategy.None));
            Assert.That(two.AiStrategy, Is.EqualTo(PlayerAiStrategy.Science));
            var copy = GameStateCopy.Clone(state);
            Assert.That(copy.Players.Single(item => item.Id == two.Id).AiStrategy,
                Is.EqualTo(PlayerAiStrategy.Science));
            Assert.That(GameStateHasher.Compute(copy), Is.EqualTo(GameStateHasher.Compute(state)));
            copy.Players.Single(item => item.Id == two.Id).AiLowEnemyMilitaryTurns++;
            Assert.That(GameStateHasher.Compute(copy), Is.Not.EqualTo(GameStateHasher.Compute(state)));
        }

        [Test]
        public void ScienceAiSubmitsResearchAndInitialDistrictCommandsThroughNormalTurnResolution()
        {
            var state = PrototypeMatchFactory.CreateSinglePlayer(13205, PlayerAiStrategy.Science);
            var ai = state.Players.Single(item => item.Slot == PlayerSlot.PlayerTwo);
            var city = state.Cities.Single(item => item.OwnerId == ai.Id);

            var resolution = new TurnProcessor().Resolve(state, new GameCommand[0]);

            Assert.That(ai.CurrentResearch, Is.EqualTo(ResearchType.School));
            Assert.That(resolution.Commands.Any(item => item.PlayerId == ai.Id &&
                item.Type == GameCommandType.SelectResearch), Is.True);
            Assert.That(state.Districts.Count(item => item.CityId == city.Id &&
                item.Type != DistrictType.Government), Is.EqualTo(3));
            Assert.That(state.Districts.Where(item => item.CityId == city.Id)
                .Select(item => item.Type), Does.Contain(DistrictType.Agriculture));
            Assert.That(state.Districts.Where(item => item.CityId == city.Id)
                .Select(item => item.Type), Does.Contain(DistrictType.Commerce));
            Assert.That(state.Districts.Where(item => item.CityId == city.Id)
                .Select(item => item.Type), Does.Contain(DistrictType.Military));
        }

        [Test]
        public void CommandsPretendingToControlAiPlayerAreIgnored()
        {
            var state = PrototypeMatchFactory.CreateSinglePlayer(13206, PlayerAiStrategy.Science);
            var ai = state.Players.Single(item => item.Slot == PlayerSlot.PlayerTwo);
            var injected = new GameCommand
            {
                CommandId = state.AllocateId(), PlayerId = ai.Id, TurnNumber = state.TurnNumber,
                Type = GameCommandType.SelectResearch, PrimaryValue = (int)ResearchType.Arts
            };

            new TurnProcessor().Resolve(state, new[] { injected });

            Assert.That(ai.CurrentResearch, Is.EqualTo(ResearchType.School));
        }

        [Test]
        public void ScienceAiLeavesArtsUntilAfterItsVictoryResearchLine()
        {
            var state = PrototypeMatchFactory.CreateSinglePlayer(13207, PlayerAiStrategy.Science);
            var ai = state.Players.Single(item => item.Slot == PlayerSlot.PlayerTwo);
            var city = state.Cities.Single(item => item.OwnerId == ai.Id);
            ai.CompletedResearch.AddRange(new[]
            {
                ResearchType.School, ResearchType.IronWorking, ResearchType.Gunpowder,
                ResearchType.Vehicles, ResearchType.NuclearFission
            });

            var selected = EasyPlayerAiPlanner.ChooseResearch(state, ai, city,
                new PlayerAiAssessment());

            Assert.That(selected, Is.EqualTo(ResearchType.Arts));
        }

        [Test]
        public void AiPlanningIsDeterministicForEqualStateAndSeed()
        {
            var first = PrototypeMatchFactory.CreateSinglePlayer(13208, PlayerAiStrategy.Conquest);
            var second = GameStateCopy.Clone(first);

            var firstCommands = EasyPlayerAiPlanner.Plan(first);
            var secondCommands = EasyPlayerAiPlanner.Plan(second);

            Assert.That(firstCommands.Select(Signature), Is.EqualTo(secondCommands.Select(Signature)));
            Assert.That(GameStateHasher.Compute(first), Is.EqualTo(GameStateHasher.Compute(second)));
        }

        [Test]
        public void TwoAiPlayersKeepInitialDistrictReservationsScopedToTheirOwnCities()
        {
            var state = PrototypeMatchFactory.Create(13211);
            var competitors = state.Players.Where(item => item.Slot != PlayerSlot.Neutral).ToList();
            foreach (var player in competitors) player.AiStrategy = PlayerAiStrategy.Conquest;

            var commands = EasyPlayerAiPlanner.PlanOrders(state);

            foreach (var player in competitors)
            {
                var city = state.Cities.Single(item => item.OwnerId == player.Id);
                var districtTypes = commands.Where(item => item.PlayerId == player.Id &&
                        item.Type == GameCommandType.StartDistrict && item.SubjectId == city.Id)
                    .Select(item => (DistrictType)item.PrimaryValue).ToList();
                Assert.That(districtTypes, Is.EquivalentTo(new[]
                {
                    DistrictType.Agriculture, DistrictType.Military, DistrictType.Commerce
                }));
            }
        }

        [Test]
        public void FirstPlayersPlannedSpendingDoesNotConsumeSecondPlayersAiBudget()
        {
            var bothActive = CreateTwoPlayerTrainingBudgetState();
            var secondOnly = GameStateCopy.Clone(bothActive);
            secondOnly.Players.Single(item => item.Slot == PlayerSlot.PlayerOne).AiStrategy =
                PlayerAiStrategy.None;
            var secondId = bothActive.Players.Single(item => item.Slot == PlayerSlot.PlayerTwo).Id;

            var withFirst = EasyPlayerAiPlanner.PlanOrders(bothActive)
                .Where(item => item.PlayerId == secondId).Select(SemanticSignature).ToList();
            var withoutFirst = EasyPlayerAiPlanner.PlanOrders(secondOnly)
                .Where(item => item.PlayerId == secondId).Select(SemanticSignature).ToList();

            Assert.That(withFirst, Is.EqualTo(withoutFirst));
            Assert.That(withFirst.Any(item => item.StartsWith("StartTraining:")), Is.True);
        }

        [Test]
        public void CultureAiTradesWithLowestFinalCostCityInsteadOfLowestIdCity()
        {
            var state = PrototypeMatchFactory.Create(13214);
            var ai = state.Players.Single(item => item.Slot == PlayerSlot.PlayerOne);
            ai.AiStrategy = PlayerAiStrategy.Culture;
            var city = state.Cities.Single(item => item.OwnerId == ai.Id);
            city.Gold = 100;
            var cultureCities = state.Cities.Where(item =>
                item.NeutralSpecialization == NeutralCitySpecialization.Culture).ToList();
            var expected = cultureCities.OrderBy(item => NeutralTradeQuoteResolver.Quote(
                    state, ai.Id, city.Id, item.Id).TotalGoldCost)
                .ThenBy(item => NeutralTradeQuoteResolver.Quote(
                    state, ai.Id, city.Id, item.Id).Route.Distance)
                .ThenBy(item => item.Id.Value).First();

            var trade = EasyPlayerAiPlanner.PlanOrders(state).Single(item =>
                item.PlayerId == ai.Id && item.Type == GameCommandType.Trade);

            Assert.That(trade.TargetId, Is.EqualTo(expected.Id));
            Assert.That(expected.Id, Is.Not.EqualTo(cultureCities.OrderBy(item => item.Id.Value)
                .First().Id), "The fixture must distinguish cost ordering from ID ordering.");
        }

        [Test]
        public void ConquestLimitedRaidChoosesNearestDistrictAmongEqualPriorityTargets()
        {
            var state = PrototypeMatchFactory.Create(13215);
            var attacker = state.Players.Single(item => item.Slot == PlayerSlot.PlayerOne);
            var defender = state.Players.Single(item => item.Slot == PlayerSlot.PlayerTwo);
            attacker.AiStrategy = PlayerAiStrategy.Conquest;
            var attackerCity = state.Cities.Single(item => item.OwnerId == attacker.Id);
            var defenderCity = state.Cities.Single(item => item.OwnerId == defender.Id);
            attackerCity.Gold = 200;
            attackerCity.StoredFood = 200;
            attackerCity.TestGovernmentFoodBonus = 50;
            attackerCity.TestGovernmentGoldBonus = 50;
            var source = state.Districts.Single(item => item.CityId == attackerCity.Id &&
                item.Type == DistrictType.Government);
            var defenderGovernment = state.Districts.Single(item => item.CityId == defenderCity.Id &&
                item.Type == DistrictType.Government);
            var targetTiles = state.MapTopology.FindView(defenderCity.Id).Tiles
                .Where(item => !state.Districts.Any(district => district.TileId == item.TileId))
                .OrderBy(item => HexCoord.Distance(
                    MapTraversal.GlobalCoordinate(state, source.TileId).Value,
                    MapTraversal.GlobalCoordinate(state, item.TileId).Value)).ToList();
            var near = targetTiles.First().TileId;
            var far = targetTiles.Last().TileId;
            foreach (var tileId in new[] { far, near })
                state.Districts.Add(new DistrictState
                {
                    Id = state.AllocateId(), CityId = defenderCity.Id, TileId = tileId,
                    Type = DistrictType.Culture, ControllerId = defender.Id,
                    IsOperational = true, AssignedCitizens = 1
                });
            for (var index = 0; index < 3; index++)
            {
                state.Units.Add(new UnitState
                {
                    Id = state.AllocateId(), OwnerId = attacker.Id, HomeCityId = attackerCity.Id,
                    TileId = source.TileId, Type = UnitType.MechanizedInfantry,
                    HitPoints = UnitRules.MaximumHitPoints(UnitType.MechanizedInfantry),
                    CarriedFood = UnitRules.FoodCapacity(UnitType.MechanizedInfantry),
                    RemainingMovement = UnitRules.Movement(UnitType.MechanizedInfantry)
                });
                state.Units.Add(new UnitState
                {
                    Id = state.AllocateId(), OwnerId = defender.Id, HomeCityId = defenderCity.Id,
                    TileId = defenderGovernment.TileId, Type = UnitType.MechanizedInfantry,
                    HitPoints = UnitRules.MaximumHitPoints(UnitType.MechanizedInfantry),
                    CarriedFood = UnitRules.FoodCapacity(UnitType.MechanizedInfantry),
                    RemainingMovement = UnitRules.Movement(UnitType.MechanizedInfantry)
                });
            }

            var moves = EasyPlayerAiPlanner.PlanOrders(state).Where(item =>
                item.PlayerId == attacker.Id && item.Type == GameCommandType.MoveUnit).ToList();

            Assert.That(moves.Any(item => item.TargetId == near), Is.True);
            Assert.That(moves.Any(item => item.TargetId == far), Is.False);
        }

        [TestCase(PlayerAiStrategy.Science)]
        [TestCase(PlayerAiStrategy.Culture)]
        public void NonConquestVictoryAiDefendsBelowFullArmyAdvantageAndTargetsGovernmentAboveIt(
            PlayerAiStrategy strategy)
        {
            var defensive = PrototypeMatchFactory.Create(13216);
            var science = defensive.Players.Single(item => item.Slot == PlayerSlot.PlayerOne);
            science.AiStrategy = strategy;
            var enemy = defensive.Players.Single(item => item.Slot == PlayerSlot.PlayerTwo);
            var enemyCity = defensive.Cities.Single(item => item.OwnerId == enemy.Id);

            var defensiveMoves = EasyPlayerAiPlanner.PlanOrders(defensive).Where(item =>
                item.PlayerId == science.Id && item.Type == GameCommandType.MoveUnit).ToList();

            Assert.That(defensiveMoves.Any(item => item.Path.Any(tileId =>
                defensive.Tiles.Single(tile => tile.Id == tileId).CityId == enemyCity.Id)), Is.False);

            var offensive = PrototypeMatchFactory.Create(13217);
            science = offensive.Players.Single(item => item.Slot == PlayerSlot.PlayerOne);
            science.AiStrategy = strategy;
            enemy = offensive.Players.Single(item => item.Slot == PlayerSlot.PlayerTwo);
            var scienceCity = offensive.Cities.Single(item => item.OwnerId == science.Id);
            enemyCity = offensive.Cities.Single(item => item.OwnerId == enemy.Id);
            scienceCity.Gold = 200;
            scienceCity.StoredFood = 200;
            scienceCity.TestGovernmentFoodBonus = 50;
            scienceCity.TestGovernmentGoldBonus = 50;
            var ownGovernment = offensive.Districts.Single(item =>
                item.CityId == scienceCity.Id && item.Type == DistrictType.Government);
            var enemyGovernment = offensive.Districts.Single(item =>
                item.CityId == enemyCity.Id && item.Type == DistrictType.Government);
            for (var index = 0; index < 3; index++)
                offensive.Units.Add(new UnitState
                {
                    Id = offensive.AllocateId(), OwnerId = science.Id, HomeCityId = scienceCity.Id,
                    TileId = ownGovernment.TileId, Type = UnitType.MechanizedInfantry,
                    HitPoints = UnitRules.MaximumHitPoints(UnitType.MechanizedInfantry),
                    CarriedFood = UnitRules.FoodCapacity(UnitType.MechanizedInfantry),
                    RemainingMovement = UnitRules.Movement(UnitType.MechanizedInfantry)
                });

            var offensiveMoves = EasyPlayerAiPlanner.PlanOrders(offensive).Where(item =>
                item.PlayerId == science.Id && item.Type == GameCommandType.MoveUnit).ToList();

            Assert.That(offensiveMoves.Count(item => item.TargetId == enemyGovernment.TileId),
                Is.GreaterThanOrEqualTo(2));
            Assert.That(offensiveMoves.Any(item => item.TargetId != enemyGovernment.TileId &&
                item.Path.Any(tileId => offensive.Tiles.Single(tile => tile.Id == tileId).CityId ==
                                        enemyCity.Id)), Is.False);
        }

        [Test]
        public void ConquestAiAttacksCapturableGovernmentInsteadOfCountingRemoteDefenders()
        {
            var state = PrototypeMatchFactory.Create(13212);
            var attacker = state.Players.Single(item => item.Slot == PlayerSlot.PlayerOne);
            var defender = state.Players.Single(item => item.Slot == PlayerSlot.PlayerTwo);
            attacker.AiStrategy = PlayerAiStrategy.Conquest;
            attacker.UnlockedUnitTypes.Add(UnitType.MechanizedInfantry);
            var attackerCity = state.Cities.Single(item => item.OwnerId == attacker.Id);
            var defenderCity = state.Cities.Single(item => item.OwnerId == defender.Id);
            attackerCity.Gold = 100;
            attackerCity.StoredFood = 100;
            attackerCity.TestGovernmentFoodBonus = 20;
            attackerCity.TestGovernmentGoldBonus = 20;
            var attackerGovernment = state.Districts.Single(item => item.CityId == attackerCity.Id &&
                item.Type == DistrictType.Government);
            var defenderGovernment = state.Districts.Single(item => item.CityId == defenderCity.Id &&
                item.Type == DistrictType.Government);
            for (var index = 0; index < 2; index++)
                state.Units.Add(new UnitState
                {
                    Id = state.AllocateId(), OwnerId = attacker.Id, HomeCityId = attackerCity.Id,
                    TileId = attackerGovernment.TileId, Type = UnitType.MechanizedInfantry,
                    HitPoints = UnitRules.MaximumHitPoints(UnitType.MechanizedInfantry),
                    CarriedFood = UnitRules.FoodCapacity(UnitType.MechanizedInfantry),
                    RemainingMovement = UnitRules.Movement(UnitType.MechanizedInfantry)
                });

            var remoteTiles = state.MapTopology.FindView(defenderCity.Id).Tiles
                .Where(item => item.TileId != defenderGovernment.TileId)
                .OrderByDescending(item => HexCoord.Distance(new HexCoord(0, 0),
                    new HexCoord(item.LocalQ, item.LocalR))).Take(2).ToList();
            foreach (var tile in remoteTiles)
            for (var index = 0; index < 3; index++)
                state.Units.Add(new UnitState
                {
                    Id = state.AllocateId(), OwnerId = defender.Id, HomeCityId = defenderCity.Id,
                    TileId = tile.TileId, Type = UnitType.MechanizedInfantry,
                    HitPoints = UnitRules.MaximumHitPoints(UnitType.MechanizedInfantry)
                });

            var commands = EasyPlayerAiPlanner.PlanOrders(state);

            Assert.That(commands.Any(item => item.PlayerId == attacker.Id &&
                item.Type == GameCommandType.MoveUnit &&
                item.TargetId == defenderGovernment.TileId), Is.True);
        }

        [Test]
        public void PlayerAiResumesPausedConstructionBeforeStartingAnotherDistrict()
        {
            var state = PrototypeMatchFactory.CreateSinglePlayer(13209, PlayerAiStrategy.Science);
            var ai = state.Players.Single(item => item.Slot == PlayerSlot.PlayerTwo);
            var city = state.Cities.Single(item => item.OwnerId == ai.Id);
            new TurnProcessor().Resolve(state, new GameCommand[0]);
            var paused = state.Districts.First(item => item.CityId == city.Id &&
                item.Type != DistrictType.Government);
            paused.AssignedCitizens = 0;
            paused.IsOperational = false;
            paused.RemainingConstructionTurns = 2;

            var commands = EasyPlayerAiPlanner.PlanOrders(state);

            Assert.That(commands.Any(item => item.PlayerId == ai.Id &&
                item.Type == GameCommandType.SetCitizenAutoAssignment && item.PrimaryValue == 0),
                Is.True);
            Assert.That(commands.Any(item => item.PlayerId == ai.Id &&
                item.Type == GameCommandType.AssignCitizen && item.SubjectId == paused.Id &&
                item.PrimaryValue == 1), Is.True);
            Assert.That(commands.Any(item => item.PlayerId == ai.Id &&
                item.Type == GameCommandType.StartDistrict), Is.False);
        }

        [Test]
        public void StationaryEnemyHomeGarrisonDoesNotCreateDirectThreatOnCompactMap()
        {
            var state = PrototypeMatchFactory.CreateSinglePlayer(13210, PlayerAiStrategy.Culture);
            var ai = state.Players.Single(item => item.Slot == PlayerSlot.PlayerTwo);
            var city = state.Cities.Single(item => item.OwnerId == ai.Id);

            var assessment = EasyPlayerAiPlanner.Assess(state, ai, city);

            Assert.That(assessment.ThreatLevel, Is.LessThan(PlayerAiThreatLevel.Direct));
            Assert.That(EasyPlayerAiPlanner.ChooseResearch(state, ai, city, assessment),
                Is.EqualTo(ResearchType.School));
        }

        [TestCase(PlayerAiStrategy.Science, VictoryType.Science, 80)]
        [TestCase(PlayerAiStrategy.Culture, VictoryType.Culture, 40)]
        [TestCase(PlayerAiStrategy.Conquest, VictoryType.Conquest, 50)]
        public void EasyAiCanFinishAnUnopposedMatchWithoutTargetingNeutralCities(
            PlayerAiStrategy strategy, VictoryType expectedVictory, int turnLimit)
        {
            var state = PrototypeMatchFactory.CreateSinglePlayer(14501, strategy);
            var processor = new TurnProcessor();
            var ai = state.Players.Single(item => item.Slot == PlayerSlot.PlayerTwo);
            for (var turn = 0; turn < turnLimit && !state.IsGameOver; turn++)
            {
                var resolution = processor.Resolve(state, new GameCommand[0]);
                Assert.That(resolution.Events.Any(item => item.Type == GameEventType.CommandRejected &&
                    item.SourceId == ai.Id), Is.False);
                foreach (var move in resolution.Commands.Where(item =>
                    item.PlayerId == ai.Id && item.Type == GameCommandType.MoveUnit))
                foreach (var tileId in move.Path)
                {
                    var tile = state.Tiles.Single(item => item.Id == tileId);
                    var city = state.Cities.Single(item => item.Id == tile.CityId);
                    var owner = state.Players.Single(item => item.Id == city.OwnerId);
                    Assert.That(owner.Slot, Is.Not.EqualTo(PlayerSlot.Neutral));
                }
            }

            Assert.That(state.Victory, Is.EqualTo(expectedVictory));
            Assert.That(state.WinnerId, Is.EqualTo(ai.Id));
        }

        private static string Signature(GameCommand command) =>
            $"{command.Type}:{command.SubjectId.Value}:{command.TargetId.Value}:" +
            $"{command.PrimaryValue}:{command.SecondaryValue}";

        private static string SemanticSignature(GameCommand command) =>
            $"{command.Type}:{command.SubjectId.Value}:{command.TargetId.Value}:" +
            $"{command.PrimaryValue}:{command.SecondaryValue}:" +
            string.Join(",", command.Path.Select(item => item.Value));

        private static GameState CreateTwoPlayerTrainingBudgetState()
        {
            var state = PrototypeMatchFactory.Create(13213);
            foreach (var player in state.Players.Where(item => item.Slot != PlayerSlot.Neutral))
            {
                player.AiStrategy = PlayerAiStrategy.Conquest;
                var city = state.Cities.Single(item => item.OwnerId == player.Id);
                city.Population = 2;
                city.Gold = 8;
                city.StoredFood = 100;
                city.TestGovernmentGoldBonus = 2;
                var tile = state.MapTopology.FindView(city.Id).Tiles.First(item =>
                    !state.Districts.Any(district => district.TileId == item.TileId));
                state.Districts.Add(new DistrictState
                {
                    Id = state.AllocateId(), CityId = city.Id, TileId = tile.TileId,
                    Type = DistrictType.Military, ControllerId = player.Id,
                    IsOperational = true, AssignedCitizens = 1
                });
            }
            return state;
        }

        private static void AssertSecond(NeutralCitySpecialization specialization, ResearchType expected)
        {
            var city = new CityState { NeutralSpecialization = specialization };
            city.NeutralCompletedResearch.Add(ResearchType.School);
            Assert.That(NeutralResearchResolver.NextResearch(city), Is.EqualTo(expected));
        }
    }
}
