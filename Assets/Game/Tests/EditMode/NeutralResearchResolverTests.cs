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
                Assert.That(city.NeutralCompletedResearch.Count,
                    Is.EqualTo(NeutralResearchResolver.OrderFor(city.NeutralSpecialization).Count),
                    city.Name + "은 전문화 연구 목록을 모두 끝내야 한다.");
                var facilities = state.Districts.Where(item => item.CityId == city.Id &&
                    item.Type == DistrictType.NuclearFacility).ToArray();
                Assert.That(facilities.Length, Is.EqualTo(1));
                Assert.That(facilities[0].RemainingConstructionTurns, Is.Zero);
                Assert.That(facilities[0].IsOperational, Is.True);
                var defense = state.DefenseFacilities.Single(item => item.CityId == city.Id);
                Assert.That(defense.Type, Is.EqualTo(DefenseFacilityType.ModernDefense),
                    city.Name + "은 최종 방어시설을 완성해야 한다.");
                Assert.That(defense.RemainingConstructionTurns, Is.Zero);
                Assert.That(defense.IsModernDefenseActive, Is.True);
                var target = NeutralMilitaryResolver.ForceTarget(state, city);
                var combat = state.Units.Count(item => item.HomeCityId == city.Id &&
                    item.HitPoints > 0 && !UnitRules.IsSupply(item.Type));
                var supply = state.Units.Count(item => item.HomeCityId == city.Id &&
                    item.HitPoints > 0 && UnitRules.IsSupply(item.Type));
                Assert.That(combat, Is.GreaterThanOrEqualTo(target.Combat),
                    city.Name + "은 후반 전투병 목표를 유지해야 한다.");
                Assert.That(supply, Is.GreaterThanOrEqualTo(target.Supply),
                    city.Name + "은 후반 보급병 목표를 유지해야 한다.");
                Assert.That(state.Units.Where(item => item.HomeCityId == city.Id &&
                    item.HitPoints > 0 && !UnitRules.IsSupply(item.Type))
                    .All(item => item.Type == UnitType.MechanizedInfantry), Is.True,
                    city.Name + "의 전투병은 최상위 병종으로 승급되어야 한다.");
                Assert.That(NeutralEconomyPlanner.Evaluate(state, city).IsSafe, Is.True,
                    city.Name + "은 최종 목표 후에도 2턴 경제 안전선을 지켜야 한다.");
                Assert.That(city.ResearchPoints, Is.GreaterThan(0),
                    city.Name + "은 연구 완료 뒤 과학을 장기전용으로 비축해야 한다.");
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
        public void AiPromotionDiagnosticsExplainWhyUnlockedIronUpgradeWasDeferred()
        {
            var state = PrototypeMatchFactory.CreateSinglePlayer(13218, PlayerAiStrategy.Conquest);
            var ai = state.Players.Single(item => item.Slot == PlayerSlot.PlayerTwo);
            var city = state.Cities.Single(item => item.OwnerId == ai.Id);
            ai.CompletedResearch.Add(ResearchType.School);
            ai.CompletedResearch.Add(ResearchType.IronWorking);
            if (!ai.UnlockedUnitTypes.Contains(UnitType.IronInfantry))
                ai.UnlockedUnitTypes.Add(UnitType.IronInfantry);
            city.Gold = 0;
            var militia = state.Units.Single(item => item.OwnerId == ai.Id &&
                item.Type == UnitType.Militia);
            militia.RemainingMovement = UnitRules.Movement(militia.Type);
            var diagnostics = new System.Collections.Generic.List<PlayerAiPromotionDiagnostic>();

            var commands = EasyPlayerAiPlanner.PlanOrders(state, diagnostics);

            Assert.That(commands.Any(item => item.Type == GameCommandType.PromoteUnit &&
                item.SubjectId == militia.Id), Is.False);
            Assert.That(diagnostics.Any(item => item.UnitId == militia.Id &&
                item.TargetType == UnitType.IronInfantry &&
                item.Reason == PlayerAiPromotionDeferralReason.InsufficientGold), Is.True);
        }

        [Test]
        public void ConquestAiResearchesIronBeforeUnsafeFoodEfficiencyResearch()
        {
            var state = PrototypeMatchFactory.CreateSinglePlayer(13219, PlayerAiStrategy.Conquest);
            var ai = state.Players.Single(item => item.Slot == PlayerSlot.PlayerTwo);
            var city = state.Cities.Single(item => item.OwnerId == ai.Id);
            ai.CompletedResearch.Add(ResearchType.School);
            city.StoredFood = 0;

            var selected = EasyPlayerAiPlanner.ChooseResearch(state, ai, city,
                EasyPlayerAiPlanner.Assess(state, ai, city));

            Assert.That(selected, Is.EqualTo(ResearchType.IronWorking));
        }

        [Test]
        public void ConquestAiBuildsAgricultureBeforeSecondMilitaryWhenMilitiaFoodMarginIsBelowThree()
        {
            var state = CreateConquestDistrictExpansionState(13220, foodBonus: 0, goldBonus: 0);
            var ai = state.Players.Single(item => item.Slot == PlayerSlot.PlayerTwo);

            var construction = EasyPlayerAiPlanner.PlanOrders(state).First(item =>
                item.PlayerId == ai.Id && item.Type == GameCommandType.StartDistrict);

            Assert.That((DistrictType)construction.PrimaryValue, Is.EqualTo(DistrictType.Agriculture));
        }

        [Test]
        public void ConquestAiAddsSecondMilitaryWhenMilitiaFoodAndGoldMarginsReachThree()
        {
            var state = CreateConquestDistrictExpansionState(13221, foodBonus: 1, goldBonus: 0);
            var ai = state.Players.Single(item => item.Slot == PlayerSlot.PlayerTwo);

            var construction = EasyPlayerAiPlanner.PlanOrders(state).First(item =>
                item.PlayerId == ai.Id && item.Type == GameCommandType.StartDistrict);

            Assert.That((DistrictType)construction.PrimaryValue, Is.EqualTo(DistrictType.Military));
        }

        [Test]
        public void ConquestAiRequiresSixGoldMarginForSecondMilitaryAfterIronWorking()
        {
            var state = CreateConquestDistrictExpansionState(13222, foodBonus: 1, goldBonus: 0);
            var ai = state.Players.Single(item => item.Slot == PlayerSlot.PlayerTwo);
            ai.CompletedResearch.Add(ResearchType.School);
            ai.CompletedResearch.Add(ResearchType.IronWorking);
            ai.UnlockedUnitTypes.Add(UnitType.IronInfantry);

            var construction = EasyPlayerAiPlanner.PlanOrders(state).First(item =>
                item.PlayerId == ai.Id && item.Type == GameCommandType.StartDistrict);

            Assert.That((DistrictType)construction.PrimaryValue, Is.EqualTo(DistrictType.Commerce));
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
        [TestCase(PlayerAiStrategy.Conquest, VictoryType.Conquest, 170)]
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

            if (strategy == PlayerAiStrategy.Conquest)
                Assert.That(state.Victory, Is.EqualTo(expectedVictory).Or.EqualTo(VictoryType.Science),
                    "정복형은 장기 교착 시 핵개발 대체 승리로 전환할 수 있다.");
            else if (strategy == PlayerAiStrategy.Science)
                Assert.That(state.Victory, Is.EqualTo(expectedVictory).Or.EqualTo(VictoryType.Conquest),
                    "과학형은 1.5배 공세 조건이 먼저 성립하면 정복으로 경기를 끝낼 수 있다.");
            else
                Assert.That(state.Victory, Is.EqualTo(expectedVictory));
            Assert.That(state.WinnerId, Is.EqualTo(ai.Id));
        }

        [Test]
        public void ScienceAiWithEquipmentLeadInterceptsApproachingLowerTierUnit()
        {
            var state = PrototypeMatchFactory.Create(14503);
            var science = state.Players.Single(item => item.Slot == PlayerSlot.PlayerOne);
            var enemy = state.Players.Single(item => item.Slot == PlayerSlot.PlayerTwo);
            science.AiStrategy = PlayerAiStrategy.Science;
            enemy.AiStrategy = PlayerAiStrategy.Conquest;
            science.UnlockedUnitTypes.Add(UnitType.IronInfantry);
            var scienceCity = state.Cities.Single(item => item.OwnerId == science.Id);
            var enemyCity = state.Cities.Single(item => item.OwnerId == enemy.Id);
            scienceCity.Gold = 100;
            scienceCity.StoredFood = 100;
            scienceCity.TestGovernmentFoodBonus = 20;
            scienceCity.TestGovernmentGoldBonus = 20;
            var scienceGovernment = state.Districts.Single(item => item.CityId == scienceCity.Id &&
                item.Type == DistrictType.Government);
            var enemyGovernment = state.Districts.Single(item => item.CityId == enemyCity.Id &&
                item.Type == DistrictType.Government);
            state.Units.Add(new UnitState
            {
                Id = state.AllocateId(), OwnerId = science.Id, HomeCityId = scienceCity.Id,
                TileId = scienceGovernment.TileId, Type = UnitType.IronInfantry,
                HitPoints = UnitRules.MaximumHitPoints(UnitType.IronInfantry), CarriedFood = 6,
                RemainingMovement = UnitRules.Movement(UnitType.IronInfantry)
            });
            var approaching = state.Units.Single(item => item.HomeCityId == enemyCity.Id);
            approaching.TileId = state.Tiles.Where(item => item.CityId != scienceCity.Id &&
                    item.CityId != enemyCity.Id)
                .OrderBy(item => HexCoord.Distance(
                    MapTraversal.GlobalCoordinate(state, scienceGovernment.TileId).Value,
                    MapTraversal.GlobalCoordinate(state, item.Id).Value)).First().Id;
            approaching.CreatedTurn = state.TurnNumber - 1;
            approaching.RemainingMovement = UnitRules.Movement(approaching.Type);
            for (var index = 0; index < 2; index++)
                state.Units.Add(new UnitState
                {
                    Id = state.AllocateId(), OwnerId = enemy.Id, HomeCityId = enemyCity.Id,
                    TileId = enemyGovernment.TileId, Type = UnitType.Militia,
                    HitPoints = UnitRules.MaximumHitPoints(UnitType.Militia), CarriedFood = 6
                });

            var moves = EasyPlayerAiPlanner.PlanOrders(state).Where(item =>
                item.PlayerId == science.Id && item.Type == GameCommandType.MoveUnit).ToList();

            Assert.That(moves.Any(item => item.TargetId == approaching.TileId), Is.True);
        }

        [Test]
        public void ScienceAiCounterRaidsWhenCulturePressureStartsBeforeThirtyTurnThreshold()
        {
            var state = PrototypeMatchFactory.Create(14513);
            var science = state.Players.Single(item => item.Slot == PlayerSlot.PlayerOne);
            var culture = state.Players.Single(item => item.Slot == PlayerSlot.PlayerTwo);
            science.AiStrategy = PlayerAiStrategy.Science;
            culture.AiStrategy = PlayerAiStrategy.Culture;
            science.UnlockedUnitTypes.Add(UnitType.IronInfantry);
            var scienceCity = state.Cities.Single(item => item.OwnerId == science.Id);
            var cultureCity = state.Cities.Single(item => item.OwnerId == culture.Id);
            scienceCity.Population = 6;
            scienceCity.Gold = 200;
            scienceCity.StoredFood = 200;
            scienceCity.TestGovernmentFoodBonus = 50;
            scienceCity.TestGovernmentGoldBonus = 50;
            scienceCity.LastCultureProduction = 1;
            cultureCity.LastCultureProduction = 2;
            var scienceGovernment = state.Districts.Single(item => item.CityId == scienceCity.Id &&
                item.Type == DistrictType.Government);
            for (var index = 0; index < 3; index++)
                AddAiUnit(state, science, scienceCity, scienceGovernment.TileId,
                    UnitType.IronInfantry, 6);
            var cultureGovernment = state.Districts.Single(item => item.CityId == cultureCity.Id &&
                item.Type == DistrictType.Government);
            AddAiUnit(state, culture, cultureCity, cultureGovernment.TileId,
                UnitType.IronInfantry, 6);
            var cultureTile = state.MapTopology.FindView(cultureCity.Id).Tiles.First(item =>
                item.IsBuildable && state.Districts.All(district => district.TileId != item.TileId));
            state.Districts.Add(new DistrictState
            {
                Id = state.AllocateId(), CityId = cultureCity.Id, TileId = cultureTile.TileId,
                Type = DistrictType.Culture, ControllerId = culture.Id,
                AssignedCitizens = 1, IsOperational = true
            });

            var moves = EasyPlayerAiPlanner.PlanOrders(state).Where(item =>
                item.PlayerId == science.Id && item.Type == GameCommandType.MoveUnit).ToList();

            Assert.That(moves.Count(item => item.TargetId == cultureTile.TileId),
                Is.GreaterThanOrEqualTo(3),
                "문화패배가 30턴보다 멀고 적과 동급 장비여도 문화 생산 열세가 시작되면 과학형은 역약탈해야 한다.");
        }

        [Test]
        public void CultureAiWithinTenTurnsOfVictoryDefendsCultureDistrictInsteadOfInvading()
        {
            var state = PrototypeMatchFactory.Create(14504);
            var culture = state.Players.Single(item => item.Slot == PlayerSlot.PlayerOne);
            var enemy = state.Players.Single(item => item.Slot == PlayerSlot.PlayerTwo);
            culture.AiStrategy = PlayerAiStrategy.Culture;
            var cultureCity = state.Cities.Single(item => item.OwnerId == culture.Id);
            var enemyCity = state.Cities.Single(item => item.OwnerId == enemy.Id);
            cultureCity.Gold = 200;
            cultureCity.StoredFood = 200;
            cultureCity.TestGovernmentFoodBonus = 50;
            cultureCity.TestGovernmentGoldBonus = 50;
            cultureCity.LastCultureProduction = 2;
            enemyCity.LastCultureProduction = 1;
            enemyCity.CultureInfluences.Add(new CultureInfluenceState
            {
                CultureOwnerId = culture.Id, PreferredCitizens = 2, ConversionProgress = 9
            });
            var ownGovernment = state.Districts.Single(item => item.CityId == cultureCity.Id &&
                item.Type == DistrictType.Government);
            var enemyGovernment = state.Districts.Single(item => item.CityId == enemyCity.Id &&
                item.Type == DistrictType.Government);
            var cultureTile = state.MapTopology.FindView(cultureCity.Id).Tiles.First(item =>
                item.TileId != ownGovernment.TileId &&
                !state.Districts.Any(district => district.TileId == item.TileId));
            var cultureDistrict = new DistrictState
            {
                Id = state.AllocateId(), CityId = cultureCity.Id, TileId = cultureTile.TileId,
                Type = DistrictType.Culture, ControllerId = culture.Id,
                IsOperational = true, AssignedCitizens = 1
            };
            state.Districts.Add(cultureDistrict);
            for (var index = 0; index < 3; index++)
                state.Units.Add(new UnitState
                {
                    Id = state.AllocateId(), OwnerId = culture.Id, HomeCityId = cultureCity.Id,
                    TileId = ownGovernment.TileId, Type = UnitType.MechanizedInfantry,
                    HitPoints = UnitRules.MaximumHitPoints(UnitType.MechanizedInfantry),
                    CarriedFood = 10, RemainingMovement = UnitRules.Movement(UnitType.MechanizedInfantry)
                });

            var moves = EasyPlayerAiPlanner.PlanOrders(state).Where(item =>
                item.PlayerId == culture.Id && item.Type == GameCommandType.MoveUnit).ToList();

            Assert.That(moves.Any(item => item.TargetId == enemyGovernment.TileId), Is.False);
            Assert.That(moves.Any(item => item.TargetId == cultureDistrict.TileId), Is.True);
        }

        [Test]
        public void ConquestAiCountsPersistentExpeditionWithoutProgressAsStalled()
        {
            var state = PrototypeMatchFactory.CreateSinglePlayer(14505, PlayerAiStrategy.Conquest);
            var ai = state.Players.Single(item => item.Slot == PlayerSlot.PlayerTwo);
            var city = state.Cities.Single(item => item.OwnerId == ai.Id);
            var enemy = state.Players.Single(item => item.Slot == PlayerSlot.PlayerOne);
            var enemyCity = state.Cities.Single(item => item.OwnerId == enemy.Id);
            var expedition = state.Units.Single(item => item.HomeCityId == city.Id);
            var expeditionTile = state.MapTopology.FindView(enemyCity.Id).Tiles.First(item =>
                item.IsBuildable).TileId;
            expedition.TileId = expeditionTile;
            expedition.RemainingMovement = UnitRules.Movement(expedition.Type);
            expedition.CreatedTurn = state.TurnNumber - 1;
            var assessment = EasyPlayerAiPlanner.Assess(state, ai, city);
            var enemyGovernment = state.Districts.Single(item => item.CityId == enemyCity.Id &&
                item.Type == DistrictType.Government);
            ai.AiLastEnemyCombatPower = assessment.EnemyCombatPower;
            ai.AiLastOccupiedEnemyDistricts = 0;
            ai.AiLastGovernmentDistance = HexCoord.Distance(
                MapTraversal.GlobalCoordinate(state, expeditionTile).Value,
                MapTraversal.GlobalCoordinate(state, enemyGovernment.TileId).Value);
            ai.AiStalledAttackTurns = 9;

            EasyPlayerAiPlanner.PlanOrders(state);

            Assert.That(ai.AiStalledAttackTurns, Is.EqualTo(10));
            Assert.That(ai.AiNuclearPivot, Is.True);
        }

        [Test]
        public void ConquestAiNuclearPivotSwitchesResearchWithoutDiscardingOldProgress()
        {
            var state = PrototypeMatchFactory.CreateSinglePlayer(14506, PlayerAiStrategy.Conquest);
            var ai = state.Players.Single(item => item.Slot == PlayerSlot.PlayerTwo);
            ai.CompletedResearch.Add(ResearchType.Vehicles);
            ai.CurrentResearch = ResearchType.Arts;
            ai.ResearchProgress.Add(new ResearchProgressState
            {
                Type = ResearchType.Arts, Progress = 17
            });
            ai.AiNuclearPivot = true;

            var command = EasyPlayerAiPlanner.PlanResearch(state).Single(item =>
                item.PlayerId == ai.Id && item.Type == GameCommandType.SelectResearch);

            Assert.That(command.PrimaryValue, Is.EqualTo((int)ResearchType.NuclearFission));
            Assert.That(ai.ResearchProgress.Single(item => item.Type == ResearchType.Arts).Progress,
                Is.EqualTo(17));
        }

        [Test]
        public void ConquestSupplyDepartsWithExpeditionWithoutTargetingEnemyGovernment()
        {
            var state = PrototypeMatchFactory.CreateSinglePlayer(14507, PlayerAiStrategy.Conquest);
            var ai = state.Players.Single(item => item.Slot == PlayerSlot.PlayerTwo);
            var enemy = state.Players.Single(item => item.Slot == PlayerSlot.PlayerOne);
            var city = state.Cities.Single(item => item.OwnerId == ai.Id);
            var enemyCity = state.Cities.Single(item => item.OwnerId == enemy.Id);
            city.Gold = 200;
            city.StoredFood = 200;
            city.TestGovernmentFoodBonus = 50;
            city.TestGovernmentGoldBonus = 50;
            ai.UnlockedUnitTypes.Add(UnitType.MechanizedInfantry);
            ai.UnlockedUnitTypes.Add(UnitType.MotorizedSupply);
            var government = state.Districts.Single(item => item.CityId == city.Id &&
                item.Type == DistrictType.Government);
            var enemyGovernment = state.Districts.Single(item => item.CityId == enemyCity.Id &&
                item.Type == DistrictType.Government);
            for (var index = 0; index < 3; index++)
                state.Units.Add(new UnitState
                {
                    Id = state.AllocateId(), OwnerId = ai.Id, HomeCityId = city.Id,
                    TileId = government.TileId, Type = UnitType.MechanizedInfantry,
                    HitPoints = UnitRules.MaximumHitPoints(UnitType.MechanizedInfantry),
                    CarriedFood = UnitRules.FoodCapacity(UnitType.MechanizedInfantry),
                    RemainingMovement = UnitRules.Movement(UnitType.MechanizedInfantry),
                    CreatedTurn = state.TurnNumber - 1
                });
            var supply = new UnitState
            {
                Id = state.AllocateId(), OwnerId = ai.Id, HomeCityId = city.Id,
                TileId = government.TileId, Type = UnitType.MotorizedSupply,
                HitPoints = UnitRules.MaximumHitPoints(UnitType.MotorizedSupply),
                CarriedFood = UnitRules.FoodCapacity(UnitType.MotorizedSupply),
                RemainingMovement = UnitRules.Movement(UnitType.MotorizedSupply),
                CreatedTurn = state.TurnNumber - 1
            };
            state.Units.Add(supply);

            var movements = EasyPlayerAiPlanner.PlanOrders(state).Where(item =>
                item.PlayerId == ai.Id && item.Type == GameCommandType.MoveUnit).ToList();
            var supplyMove = movements.Single(item => item.SubjectId == supply.Id);

            Assert.That(supplyMove.Path, Is.Not.Empty);
            Assert.That(supplyMove.TargetId, Is.Not.EqualTo(enemyGovernment.TileId));
            Assert.That(supplyMove.SecondaryValue, Is.Zero);
            Assert.That(movements.Any(item => item.SubjectId != supply.Id &&
                item.TargetId == enemyGovernment.TileId), Is.True);
        }

        [Test]
        public void ConquestAiHoldsSupportedOccupiedDistrict()
        {
            var state = PrototypeMatchFactory.CreateSinglePlayer(14508, PlayerAiStrategy.Conquest);
            var ai = state.Players.Single(item => item.Slot == PlayerSlot.PlayerTwo);
            var enemy = state.Players.Single(item => item.Slot == PlayerSlot.PlayerOne);
            var home = state.Cities.Single(item => item.OwnerId == ai.Id);
            var enemyCity = state.Cities.Single(item => item.OwnerId == enemy.Id);
            home.Gold = 200;
            home.StoredFood = 200;
            home.TestGovernmentFoodBonus = 50;
            home.TestGovernmentGoldBonus = 50;
            var enemyTile = state.MapTopology.FindView(enemyCity.Id).Tiles.First(item =>
                item.IsBuildable && state.Districts.All(district => district.TileId != item.TileId));
            var occupied = new DistrictState
            {
                Id = state.AllocateId(), CityId = enemyCity.Id, TileId = enemyTile.TileId,
                Type = DistrictType.Agriculture, ControllerId = ai.Id,
                IsPillaged = true, IsOperational = false, AssignedCitizens = 1
            };
            state.Districts.Add(occupied);
            state.Tiles.Single(item => item.Id == enemyTile.TileId).ControllerId = ai.Id;
            var occupier = AddAiUnit(state, ai, home, enemyTile.TileId, UnitType.Militia, 6);
            AddAiUnit(state, ai, home, enemyTile.TileId, UnitType.Militia, 6);

            var movements = EasyPlayerAiPlanner.PlanOrders(state).Where(item =>
                item.PlayerId == ai.Id && item.Type == GameCommandType.MoveUnit).ToList();

            Assert.That(movements.Any(item => item.SubjectId == occupier.Id), Is.False,
                "현지 농업 보급과 후속 병력이 있는 점령군은 자리를 유지해야 한다. 이동 대상: " +
                string.Join(",", movements.Where(item => item.SubjectId == occupier.Id)
                    .Select(item => item.TargetId.ToString()).ToArray()));
        }

        [Test]
        public void ConquestAiAbandonsUnsupportedOccupiedDistrictAndReturnsHome()
        {
            var state = PrototypeMatchFactory.CreateSinglePlayer(14509, PlayerAiStrategy.Conquest);
            var ai = state.Players.Single(item => item.Slot == PlayerSlot.PlayerTwo);
            var enemy = state.Players.Single(item => item.Slot == PlayerSlot.PlayerOne);
            var home = state.Cities.Single(item => item.OwnerId == ai.Id);
            var enemyCity = state.Cities.Single(item => item.OwnerId == enemy.Id);
            home.Gold = 200;
            home.StoredFood = 200;
            home.TestGovernmentFoodBonus = 50;
            home.TestGovernmentGoldBonus = 50;
            var homeGovernment = state.Districts.Single(item => item.CityId == home.Id &&
                item.Type == DistrictType.Government);
            var enemyTile = state.MapTopology.FindView(enemyCity.Id).Tiles.First(item =>
                item.IsBuildable && state.Districts.All(district => district.TileId != item.TileId));
            var occupied = new DistrictState
            {
                Id = state.AllocateId(), CityId = enemyCity.Id, TileId = enemyTile.TileId,
                Type = DistrictType.Science, ControllerId = ai.Id,
                IsPillaged = true, IsOperational = false, AssignedCitizens = 1
            };
            state.Districts.Add(occupied);
            state.Tiles.Single(item => item.Id == enemyTile.TileId).ControllerId = ai.Id;
            var occupier = AddAiUnit(state, ai, home, enemyTile.TileId, UnitType.Militia, 1);

            var movement = EasyPlayerAiPlanner.PlanOrders(state).Single(item =>
                item.PlayerId == ai.Id && item.Type == GameCommandType.MoveUnit &&
                item.SubjectId == occupier.Id);

            Assert.That(movement.TargetId, Is.EqualTo(homeGovernment.TileId));
        }

        [Test]
        public void ConquestAiRaidsUnguardedDistrictWhenEnemyKeepsOnlyStartingGuard()
        {
            var state = PrototypeMatchFactory.CreateSinglePlayer(14510, PlayerAiStrategy.Conquest);
            var ai = state.Players.Single(item => item.Slot == PlayerSlot.PlayerTwo);
            var enemy = state.Players.Single(item => item.Slot == PlayerSlot.PlayerOne);
            var home = state.Cities.Single(item => item.OwnerId == ai.Id);
            var enemyCity = state.Cities.Single(item => item.OwnerId == enemy.Id);
            home.Gold = 200;
            home.StoredFood = 200;
            home.TestGovernmentFoodBonus = 50;
            home.TestGovernmentGoldBonus = 50;
            var homeGovernment = state.Districts.Single(item => item.CityId == home.Id &&
                item.Type == DistrictType.Government);
            var targetTile = state.MapTopology.FindView(enemyCity.Id).Tiles.First(item =>
                item.IsBuildable && state.Districts.All(district => district.TileId != item.TileId));
            var target = new DistrictState
            {
                Id = state.AllocateId(), CityId = enemyCity.Id, TileId = targetTile.TileId,
                Type = DistrictType.Culture, ControllerId = enemy.Id, IsOperational = true,
                AssignedCitizens = 1
            };
            state.Districts.Add(target);
            var raider = AddAiUnit(state, ai, home, homeGovernment.TileId, UnitType.Militia, 6);

            var movements = EasyPlayerAiPlanner.PlanOrders(state).Where(item =>
                item.PlayerId == ai.Id && item.Type == GameCommandType.MoveUnit).ToList();

            Assert.That(ai.AiLowEnemyMilitaryTurns, Is.GreaterThan(0));
            Assert.That(movements.Any(item => item.SubjectId == raider.Id &&
                item.TargetId == target.TileId), Is.True,
                "상대가 시작 수비군만 유지하면 두 번째 전투병부터 빈 전문지구를 압박해야 한다.");
        }

        [Test]
        public void ConquestAiKeepsHomeGuardDuringRaidAndSwitchesToGovernmentWhenCapturable()
        {
            var state = PrototypeMatchFactory.CreateSinglePlayer(14512, PlayerAiStrategy.Conquest);
            var ai = state.Players.Single(item => item.Slot == PlayerSlot.PlayerTwo);
            var enemy = state.Players.Single(item => item.Slot == PlayerSlot.PlayerOne);
            var home = state.Cities.Single(item => item.OwnerId == ai.Id);
            var enemyCity = state.Cities.Single(item => item.OwnerId == enemy.Id);
            home.Gold = 300;
            home.StoredFood = 300;
            home.TestGovernmentFoodBonus = 60;
            home.TestGovernmentGoldBonus = 60;
            var homeGovernment = state.Districts.Single(item => item.CityId == home.Id &&
                item.Type == DistrictType.Government);
            var enemyGovernment = state.Districts.Single(item => item.CityId == enemyCity.Id &&
                item.Type == DistrictType.Government);
            var homeGuard = state.Units.Single(item => item.OwnerId == ai.Id &&
                item.TileId == homeGovernment.TileId);
            AddAiUnit(state, ai, home, homeGovernment.TileId, UnitType.MechanizedInfantry, 10);
            AddAiUnit(state, ai, home, homeGovernment.TileId, UnitType.MechanizedInfantry, 10);
            var reinforcements = new[]
            {
                AddAiUnit(state, enemy, enemyCity, enemyGovernment.TileId,
                    UnitType.MechanizedInfantry, 10),
                AddAiUnit(state, enemy, enemyCity, enemyGovernment.TileId,
                    UnitType.MechanizedInfantry, 10)
            };
            var raidTile = state.MapTopology.FindView(enemyCity.Id).Tiles.First(item =>
                item.IsBuildable && state.Districts.All(district => district.TileId != item.TileId));
            state.Districts.Add(new DistrictState
            {
                Id = state.AllocateId(), CityId = enemyCity.Id, TileId = raidTile.TileId,
                Type = DistrictType.Culture, ControllerId = enemy.Id,
                AssignedCitizens = 1, IsOperational = true
            });

            var raidOrders = EasyPlayerAiPlanner.PlanOrders(state).Where(item =>
                item.PlayerId == ai.Id && item.Type == GameCommandType.MoveUnit).ToList();

            Assert.That(raidOrders.Any(item => item.SubjectId == homeGuard.Id), Is.False,
                "제한 약탈 중에도 정부청사 수비병 한 기는 남아야 한다.");
            Assert.That(raidOrders.Count(item => item.TargetId == raidTile.TileId),
                Is.GreaterThanOrEqualTo(2));
            Assert.That(raidOrders.Any(item => item.TargetId == enemyGovernment.TileId), Is.False);
            var groupedRaid = raidOrders.Where(item => item.TargetId == raidTile.TileId).ToList();
            Assert.That(groupedRaid.Skip(1).All(item =>
                item.Path.SequenceEqual(groupedRaid[0].Path)), Is.True,
                "같은 타일에서 같은 목표로 출정하는 AI 전투부대는 선두와 전체 경로를 공유해야 한다.");

            state.Units.RemoveAll(item => reinforcements.Any(reinforcement =>
                reinforcement.Id == item.Id));
            var assaultOrders = EasyPlayerAiPlanner.PlanOrders(state).Where(item =>
                item.PlayerId == ai.Id && item.Type == GameCommandType.MoveUnit).ToList();

            Assert.That(assaultOrders.Any(item => item.SubjectId == homeGuard.Id), Is.False);
            Assert.That(assaultOrders.Count(item => item.TargetId == enemyGovernment.TileId),
                Is.GreaterThanOrEqualTo(2),
                "정부청사 돌파전력이 확보되면 제한 약탈보다 정복 공세로 전환해야 한다.");
            var groupedAssault = assaultOrders.Where(item =>
                item.TargetId == enemyGovernment.TileId).ToList();
            Assert.That(groupedAssault.Skip(1).All(item =>
                item.Path.SequenceEqual(groupedAssault[0].Path)), Is.True,
                "같은 출발지의 정부청사 공격대는 서로 다른 우회로로 갈라지지 않아야 한다.");
        }

        [Test]
        public void ConquestAiKeepsGovernmentGuardWhenCounterattackingInvaderInsideHomeCity()
        {
            var state = PrototypeMatchFactory.CreateSinglePlayer(14514, PlayerAiStrategy.Conquest);
            var ai = state.Players.Single(item => item.Slot == PlayerSlot.PlayerTwo);
            var enemy = state.Players.Single(item => item.Slot == PlayerSlot.PlayerOne);
            var home = state.Cities.Single(item => item.OwnerId == ai.Id);
            var enemyCity = state.Cities.Single(item => item.OwnerId == enemy.Id);
            home.Gold = 300;
            home.StoredFood = 300;
            home.TestGovernmentFoodBonus = 60;
            home.TestGovernmentGoldBonus = 60;
            var government = state.Districts.Single(item => item.CityId == home.Id &&
                item.Type == DistrictType.Government);
            var guard = state.Units.Single(item => item.OwnerId == ai.Id &&
                item.TileId == government.TileId);
            AddAiUnit(state, ai, home, government.TileId, UnitType.IronInfantry, 6);
            AddAiUnit(state, ai, home, government.TileId, UnitType.IronInfantry, 6);
            var invadedTile = state.MapTopology.FindView(home.Id).Tiles.First(item =>
                item.IsBuildable && item.TileId != government.TileId);
            AddAiUnit(state, enemy, enemyCity, invadedTile.TileId,
                UnitType.MechanizedInfantry, 10);

            var movements = EasyPlayerAiPlanner.PlanOrders(state).Where(item =>
                item.PlayerId == ai.Id && item.Type == GameCommandType.MoveUnit).ToList();

            Assert.That(movements.Any(item => item.SubjectId == guard.Id), Is.False,
                "영토 내 적을 반격할 때에도 정부청사 최소 수비군 한 기는 이탈하면 안 된다.");
            Assert.That(movements.Any(item => item.SubjectId != guard.Id &&
                item.TargetId == invadedTile.TileId), Is.True,
                "남는 병력은 영토에 진입한 적을 반격해야 한다.");
        }

        [Test]
        public void ConquestAiFullAssaultUsesDistinctEnemyCityEntryTiles()
        {
            var state = PrototypeMatchFactory.CreateSinglePlayer(14511, PlayerAiStrategy.Conquest);
            var ai = state.Players.Single(item => item.Slot == PlayerSlot.PlayerTwo);
            var enemy = state.Players.Single(item => item.Slot == PlayerSlot.PlayerOne);
            var home = state.Cities.Single(item => item.OwnerId == ai.Id);
            var enemyCity = state.Cities.Single(item => item.OwnerId == enemy.Id);
            home.Gold = 300;
            home.StoredFood = 300;
            home.TestGovernmentFoodBonus = 60;
            home.TestGovernmentGoldBonus = 60;
            var homeGovernment = state.Districts.Single(item => item.CityId == home.Id &&
                item.Type == DistrictType.Government);
            var enemyGovernment = state.Districts.Single(item => item.CityId == enemyCity.Id &&
                item.Type == DistrictType.Government);
            for (var index = 0; index < 3; index++)
                AddAiUnit(state, ai, home, homeGovernment.TileId,
                    UnitType.MechanizedInfantry, 10);

            var assaults = EasyPlayerAiPlanner.PlanOrders(state).Where(item =>
                item.PlayerId == ai.Id && item.Type == GameCommandType.MoveUnit &&
                item.TargetId == enemyGovernment.TileId && item.Path.Count > 0).ToList();
            var entryTiles = assaults.Select(command => command.Path.First(tileId =>
                state.Tiles.Single(tile => tile.Id == tileId).CityId == enemyCity.Id)).Distinct().Count();

            Assert.That(assaults.Count, Is.GreaterThanOrEqualTo(2));
            Assert.That(entryTiles, Is.GreaterThanOrEqualTo(2),
                "전면 침공 병력은 예약 경로 혼잡도를 이용해 둘 이상의 진입로로 분산되어야 한다.");
        }

        [TestCase(PlayerAiStrategy.Science)]
        [TestCase(PlayerAiStrategy.Culture)]
        public void VictoryAiMovesSpecialistToMilitaryDistrictDuringDirectThreat(
            PlayerAiStrategy strategy)
        {
            var state = CreateCitizenPriorityState(14520 + (int)strategy, strategy,
                militaryCitizens: 0, specialistCitizens: 1, directThreat: true);
            var ai = state.Players.Single(item => item.Slot == PlayerSlot.PlayerTwo);
            var city = state.Cities.Single(item => item.OwnerId == ai.Id);
            var military = state.Districts.Single(item => item.CityId == city.Id &&
                item.Type == DistrictType.Military);
            var specializedType = strategy == PlayerAiStrategy.Science
                ? DistrictType.Science : DistrictType.Culture;
            var specialized = state.Districts.Single(item => item.CityId == city.Id &&
                item.Type == specializedType);

            var commands = EasyPlayerAiPlanner.PlanOrders(state).Where(item =>
                item.PlayerId == ai.Id).ToList();

            Assert.That(commands.Any(item => item.Type == GameCommandType.SetCitizenAutoAssignment &&
                item.SubjectId == city.Id && item.PrimaryValue == 0), Is.True);
            Assert.That(commands.Any(item => item.Type == GameCommandType.AssignCitizen &&
                item.SubjectId == specialized.Id && item.PrimaryValue == 0), Is.True);
            Assert.That(commands.Any(item => item.Type == GameCommandType.AssignCitizen &&
                item.SubjectId == military.Id && item.PrimaryValue == 1), Is.True);
        }

        [TestCase(PlayerAiStrategy.Science)]
        [TestCase(PlayerAiStrategy.Culture)]
        public void VictoryAiRestoresSpecialistAfterDirectThreatEnds(PlayerAiStrategy strategy)
        {
            var state = CreateCitizenPriorityState(14530 + (int)strategy, strategy,
                militaryCitizens: 1, specialistCitizens: 0, directThreat: false);
            var ai = state.Players.Single(item => item.Slot == PlayerSlot.PlayerTwo);
            var city = state.Cities.Single(item => item.OwnerId == ai.Id);
            var military = state.Districts.Single(item => item.CityId == city.Id &&
                item.Type == DistrictType.Military);
            var specializedType = strategy == PlayerAiStrategy.Science
                ? DistrictType.Science : DistrictType.Culture;
            var specialized = state.Districts.Single(item => item.CityId == city.Id &&
                item.Type == specializedType);

            var commands = EasyPlayerAiPlanner.PlanOrders(state).Where(item =>
                item.PlayerId == ai.Id).ToList();

            Assert.That(commands.Any(item => item.Type == GameCommandType.AssignCitizen &&
                item.SubjectId == military.Id && item.PrimaryValue == 0), Is.True);
            Assert.That(commands.Any(item => item.Type == GameCommandType.AssignCitizen &&
                item.SubjectId == specialized.Id && item.PrimaryValue == 1), Is.True);
        }

        [TestCase(PlayerAiStrategy.Science)]
        [TestCase(PlayerAiStrategy.Culture)]
        public void VictoryAiTrainsCombatUnitFromStaffedMilitaryDistrictDuringDirectThreat(
            PlayerAiStrategy strategy)
        {
            var state = CreateCitizenPriorityState(14540 + (int)strategy, strategy,
                militaryCitizens: 1, specialistCitizens: 1, directThreat: true);
            var ai = state.Players.Single(item => item.Slot == PlayerSlot.PlayerTwo);
            var city = state.Cities.Single(item => item.OwnerId == ai.Id);
            city.Population++;
            var military = state.Districts.Single(item => item.CityId == city.Id &&
                item.Type == DistrictType.Military);

            var commands = EasyPlayerAiPlanner.PlanOrders(state).Where(item =>
                item.PlayerId == ai.Id).ToList();

            Assert.That(commands.Any(item => item.Type == GameCommandType.StartTraining &&
                item.SubjectId == military.Id &&
                !UnitRules.IsSupply((UnitType)item.PrimaryValue)), Is.True);
        }

        [Test]
        public void ConquestAiAggregatesMultipleSameTurnTrainingUpkeepBeforeOrdering()
        {
            var state = PrototypeMatchFactory.CreateSinglePlayer(14549, PlayerAiStrategy.Conquest);
            var ai = state.Players.Single(item => item.Slot == PlayerSlot.PlayerTwo);
            var city = state.Cities.Single(item => item.OwnerId == ai.Id);
            state.Units.RemoveAll(item => item.OwnerId == ai.Id);
            city.StoredFood = 100;
            city.Gold = 100;
            var emptyTiles = state.MapTopology.FindView(city.Id).Tiles.Where(item =>
                item.IsBuildable && state.Districts.All(district => district.TileId != item.TileId))
                .Take(2).ToArray();
            foreach (var tile in emptyTiles)
                state.Districts.Add(new DistrictState
                {
                    Id = state.AllocateId(), CityId = city.Id, TileId = tile.TileId,
                    Type = DistrictType.Military, ControllerId = ai.Id,
                    AssignedCitizens = 1, IsOperational = true
                });

            var trainings = EasyPlayerAiPlanner.PlanOrders(state).Where(item =>
                item.PlayerId == ai.Id && item.Type == GameCommandType.StartTraining).ToList();

            Assert.That(trainings.Count, Is.EqualTo(1),
                "같은 턴의 두 번째 훈련은 첫 훈련의 미래 식량·금 유지비까지 합산해 거절해야 한다.");
        }

        [Test]
        public void ConquestAiReturnsBorrowedUnitFromNeutralOriginBeforeFoodRunsOut()
        {
            var state = PrototypeMatchFactory.CreateSinglePlayer(14551, PlayerAiStrategy.Conquest);
            var ai = state.Players.Single(item => item.Slot == PlayerSlot.PlayerTwo);
            var city = state.Cities.Single(item => item.OwnerId == ai.Id);
            var government = state.Districts.Single(item => item.CityId == city.Id &&
                item.Type == DistrictType.Government);
            var neutralCity = state.Cities.First(item => item.OwnerId != ai.Id &&
                state.Players.Single(player => player.Id == item.OwnerId).Slot == PlayerSlot.Neutral);
            var neutralGovernment = state.Districts.Single(item => item.CityId == neutralCity.Id &&
                item.Type == DistrictType.Government);
            var borrowed = new UnitState
            {
                Id = state.AllocateId(), OwnerId = ai.Id, HomeCityId = city.Id,
                TileId = neutralGovernment.TileId, Type = UnitType.Militia,
                HitPoints = UnitRules.MaximumHitPoints(UnitType.Militia),
                CarriedFood = 2, RemainingMovement = UnitRules.Movement(UnitType.Militia)
            };
            state.Units.Add(borrowed);
            ai.AiNuclearPivot = true;

            var movement = EasyPlayerAiPlanner.PlanOrders(state).Single(item =>
                item.Type == GameCommandType.MoveUnit && item.SubjectId == borrowed.Id);

            Assert.That(movement.TargetId, Is.EqualTo(government.TileId));
            Assert.That(movement.Path, Is.Not.Empty,
                "중립도시에서 출발하는 징병 병력도 출발 도시를 허용 경로에 넣어 귀환해야 한다.");
        }

        [Test]
        public void CultureAiSendsAvailableCombatUnitToThreatenedSubjectRelay()
        {
            var state = PrototypeMatchFactory.CreateSinglePlayer(14550, PlayerAiStrategy.Culture);
            var ai = state.Players.Single(item => item.Slot == PlayerSlot.PlayerTwo);
            var enemy = state.Players.Single(item => item.Slot == PlayerSlot.PlayerOne);
            var neutral = state.Players.Single(item => item.Slot == PlayerSlot.Neutral);
            var home = state.Cities.Single(item => item.OwnerId == ai.Id);
            var enemyCity = state.Cities.Single(item => item.OwnerId == enemy.Id);
            var relay = state.Cities.First(item => item.OwnerId == neutral.Id);
            relay.CultureSubjectToId = ai.Id;
            home.Gold = 300;
            home.StoredFood = 300;
            home.TestGovernmentFoodBonus = 60;
            home.TestGovernmentGoldBonus = 60;
            var homeGovernment = state.Districts.Single(item => item.CityId == home.Id &&
                item.Type == DistrictType.Government);
            var relayTile = state.MapTopology.FindView(relay.Id).Tiles.First(item =>
                item.IsBuildable && state.Districts.All(district => district.TileId != item.TileId));
            state.Districts.Add(new DistrictState
            {
                Id = state.AllocateId(), CityId = relay.Id, TileId = relayTile.TileId,
                Type = DistrictType.Culture, ControllerId = neutral.Id,
                AssignedCitizens = 1, IsOperational = true
            });
            var defender = AddAiUnit(state, ai, home, homeGovernment.TileId,
                UnitType.MechanizedInfantry, 10);
            var hostile = AddAiUnit(state, enemy, enemyCity, relayTile.TileId,
                UnitType.Militia, 6);

            var movements = EasyPlayerAiPlanner.PlanOrders(state).Where(item =>
                item.PlayerId == ai.Id && item.Type == GameCommandType.MoveUnit).ToList();

            Assert.That(movements.Any(item => item.SubjectId == defender.Id &&
                item.TargetId == hostile.TileId), Is.True);
            Assert.That(movements.Any(item => item.SubjectId == defender.Id &&
                item.TargetId == state.Districts.Single(district => district.CityId == enemyCity.Id &&
                    district.Type == DistrictType.Government).TileId), Is.False);
        }

        [Test]
        public void CultureSubjectGarrisonDoesNotOccupyOrPillageNeutralDistrict()
        {
            var state = PrototypeMatchFactory.CreateSinglePlayer(14551, PlayerAiStrategy.Culture);
            var ai = state.Players.Single(item => item.Slot == PlayerSlot.PlayerTwo);
            var neutral = state.Players.Single(item => item.Slot == PlayerSlot.Neutral);
            var relay = state.Cities.First(item => item.OwnerId == neutral.Id);
            relay.CultureSubjectToId = ai.Id;
            var districtTile = state.MapTopology.FindView(relay.Id).Tiles.First(item =>
                item.IsBuildable && state.Districts.All(existing => existing.TileId != item.TileId));
            var district = new DistrictState
            {
                Id = state.AllocateId(), CityId = relay.Id, TileId = districtTile.TileId,
                Type = DistrictType.Culture, ControllerId = neutral.Id,
                AssignedCitizens = 1, IsOperational = true
            };
            state.Districts.Add(district);
            var tile = state.Tiles.Single(item => item.Id == district.TileId);

            var occupation = OccupationResolver.Resolve(state, ai.Id, tile.Id);

            Assert.That(occupation.DistrictOccupied, Is.False);
            Assert.That(occupation.PillageRewardGranted, Is.False);
            Assert.That(district.ControllerId, Is.EqualTo(neutral.Id));
            Assert.That(tile.ControllerId, Is.EqualTo(neutral.Id));
            Assert.That(relay.OccupyingPlayerId.IsValid, Is.False);
        }

        [Test]
        public void CultureSubjectAndNeutralGarrisonAreNotHostileOnSubjectTile()
        {
            var state = PrototypeMatchFactory.CreateSinglePlayer(14552, PlayerAiStrategy.Culture);
            var ai = state.Players.Single(item => item.Slot == PlayerSlot.PlayerTwo);
            var neutral = state.Players.Single(item => item.Slot == PlayerSlot.Neutral);
            var home = state.Cities.Single(item => item.OwnerId == ai.Id);
            var relay = state.Cities.First(item => item.OwnerId == neutral.Id);
            relay.CultureSubjectToId = ai.Id;
            var government = state.Districts.Single(item => item.CityId == relay.Id &&
                item.Type == DistrictType.Government);
            var adjacent = state.MapTopology.FindView(relay.Id).Tiles.First(item =>
                item.TileId != government.TileId &&
                MapTraversal.AreAdjacent(state, item.TileId, government.TileId));
            var garrison = AddAiUnit(state, ai, home, adjacent.TileId, UnitType.Militia, 6);
            var command = new GameCommand
            {
                CommandId = state.AllocateId(), PlayerId = ai.Id,
                TurnNumber = state.TurnNumber, Type = GameCommandType.MoveUnit,
                SubjectId = garrison.Id, TargetId = government.TileId
            };
            command.Path.Add(government.TileId);

            var movement = MovementResolver.Resolve(state, command);

            Assert.That(movement.StopReason, Is.EqualTo(MovementStopReason.Completed));
            Assert.That(garrison.TileId, Is.EqualTo(government.TileId));
            Assert.That(UnitDiplomacyRules.AreHostile(state, ai.Id, neutral.Id,
                government.TileId), Is.False);
        }

        private static string Signature(GameCommand command) =>
            $"{command.Type}:{command.SubjectId.Value}:{command.TargetId.Value}:" +
            $"{command.PrimaryValue}:{command.SecondaryValue}";

        private static string SemanticSignature(GameCommand command) =>
            $"{command.Type}:{command.SubjectId.Value}:{command.TargetId.Value}:" +
            $"{command.PrimaryValue}:{command.SecondaryValue}:" +
            string.Join(",", command.Path.Select(item => item.Value));

        private static UnitState AddAiUnit(GameState state, PlayerState player, CityState home,
            EntityId tileId, UnitType type, int food)
        {
            var unit = new UnitState
            {
                Id = state.AllocateId(), OwnerId = player.Id, HomeCityId = home.Id,
                TileId = tileId, Type = type, HitPoints = UnitRules.MaximumHitPoints(type),
                CarriedFood = food, RemainingMovement = UnitRules.Movement(type),
                CreatedTurn = state.TurnNumber - 1
            };
            state.Units.Add(unit);
            return unit;
        }

        private static GameState CreateCitizenPriorityState(int seed, PlayerAiStrategy strategy,
            int militaryCitizens, int specialistCitizens, bool directThreat)
        {
            var state = PrototypeMatchFactory.CreateSinglePlayer(seed, strategy);
            var ai = state.Players.Single(item => item.Slot == PlayerSlot.PlayerTwo);
            var enemy = state.Players.Single(item => item.Slot == PlayerSlot.PlayerOne);
            var city = state.Cities.Single(item => item.OwnerId == ai.Id);
            var enemyCity = state.Cities.Single(item => item.OwnerId == enemy.Id);
            city.Population = 4;
            city.GovernmentCitizens = 1;
            city.Gold = 200;
            city.StoredFood = 200;
            city.TestGovernmentFoodBonus = 50;
            city.TestGovernmentGoldBonus = 50;
            city.CitizenAutoAssignment = directThreat;
            state.Districts.RemoveAll(item => item.CityId == city.Id &&
                item.Type != DistrictType.Government);
            var tiles = state.MapTopology.FindView(city.Id).Tiles.Where(item => item.IsBuildable &&
                state.Districts.All(district => district.TileId != item.TileId)).Take(4).ToArray();
            var specialized = strategy == PlayerAiStrategy.Science
                ? DistrictType.Science : DistrictType.Culture;
            var types = new[]
            {
                DistrictType.Agriculture, DistrictType.Commerce, DistrictType.Military, specialized
            };
            var citizens = new[] { 1, 1, militaryCitizens, specialistCitizens };
            for (var index = 0; index < types.Length; index++)
                state.Districts.Add(new DistrictState
                {
                    Id = state.AllocateId(), CityId = city.Id, TileId = tiles[index].TileId,
                    Type = types[index], ControllerId = ai.Id, AssignedCitizens = citizens[index],
                    IsOperational = citizens[index] > 0
                });
            if (directThreat)
            {
                var enemyGovernment = state.Districts.Single(item => item.CityId == enemyCity.Id &&
                    item.Type == DistrictType.Government);
                AddAiUnit(state, enemy, enemyCity, enemyGovernment.TileId, UnitType.Militia, 6);
            }
            return state;
        }

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

        private static GameState CreateConquestDistrictExpansionState(int seed,
            int foodBonus, int goldBonus)
        {
            var state = PrototypeMatchFactory.CreateSinglePlayer(seed, PlayerAiStrategy.Conquest);
            var ai = state.Players.Single(item => item.Slot == PlayerSlot.PlayerTwo);
            var city = state.Cities.Single(item => item.OwnerId == ai.Id);
            city.Population = 5;
            city.GovernmentCitizens = 1;
            city.StoredFood = 100;
            city.Gold = 100;
            city.TestGovernmentFoodBonus = foodBonus;
            city.TestGovernmentGoldBonus = goldBonus;
            state.Districts.RemoveAll(item => item.CityId == city.Id &&
                item.Type != DistrictType.Government);
            var tiles = state.MapTopology.FindView(city.Id).Tiles.Where(item => item.IsBuildable &&
                state.Districts.All(district => district.TileId != item.TileId) &&
                state.Tiles.Single(tile => tile.Id == item.TileId).ResourceType == TileResourceType.None)
                .Take(3).ToArray();
            var types = new[]
            {
                DistrictType.Agriculture, DistrictType.Commerce, DistrictType.Military
            };
            for (var index = 0; index < types.Length; index++)
                state.Districts.Add(new DistrictState
                {
                    Id = state.AllocateId(), CityId = city.Id, TileId = tiles[index].TileId,
                    Type = types[index], ControllerId = ai.Id, AssignedCitizens = 1,
                    IsOperational = true
                });
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
