using System.Collections.Generic;
using System.Linq;
using LittleCiv.Core;
using NUnit.Framework;

namespace LittleCiv.Tests
{
    public sealed class VictoryResolverTests
    {
        [Test]
        public void ScienceBeatsCultureAndConquestWhenAllQualifyInSameTurn()
        {
            var fixture = CreateConquestTurn();
            var culturallyDefeatedCity = fixture.State.Cities.Single(item =>
                item.OwnerId == fixture.PlayerTwo.Id);
            CityCultureRules.GetOrCreate(culturallyDefeatedCity, fixture.PlayerOne.Id)
                .PreferredCitizens = 1;
            fixture.PlayerTwo.HasCompletedNuclearProject = true;

            var result = new TurnProcessor().Resolve(fixture.State, new[] { fixture.ConquestCommand });

            Assert.That(fixture.State.Victory, Is.EqualTo(VictoryType.Science));
            Assert.That(fixture.State.WinnerId, Is.EqualTo(fixture.PlayerTwo.Id));
            Assert.That(result.Events.Count(item => item.Type == GameEventType.VictoryTriggered), Is.EqualTo(1));
            Assert.That(result.Events.Single(item => item.Type == GameEventType.VictoryTriggered).PrimaryValue,
                Is.EqualTo((int)VictoryType.Science));
        }

        [Test]
        public void ScienceBeatsLaterConquestInSameTurn()
        {
            var fixture = CreateConquestTurn();
            fixture.PlayerOne.HasCompletedNuclearProject = true;

            var result = new TurnProcessor().Resolve(fixture.State, new[] { fixture.ConquestCommand });

            Assert.That(fixture.State.Victory, Is.EqualTo(VictoryType.Science));
            Assert.That(fixture.State.WinnerId, Is.EqualTo(fixture.PlayerOne.Id));
            Assert.That(result.Events.Single(item => item.Type == GameEventType.VictoryTriggered).PrimaryValue,
                Is.EqualTo((int)VictoryType.Science));
        }

        [Test]
        public void SimultaneousNuclearProjectsUnlockSelfLearningAiAndContinueGame()
        {
            var state = PrototypeMatchFactory.Create(9901);
            var one = state.Players.Single(item => item.Slot == PlayerSlot.PlayerOne);
            var two = state.Players.Single(item => item.Slot == PlayerSlot.PlayerTwo);
            one.HasCompletedNuclearProject = true;
            two.HasCompletedNuclearProject = true;

            var winner = VictoryResolver.ResolveScience(state);

            Assert.That(winner.IsValid, Is.False);
            Assert.That(state.Victory, Is.EqualTo(VictoryType.None));
            Assert.That(one.HasUnlockedSelfLearningAI, Is.True);
            Assert.That(two.HasUnlockedSelfLearningAI, Is.True);
        }

        [Test]
        public void SimultaneousSelfLearningAiCompletionIsDraw()
        {
            var state = PrototypeMatchFactory.Create(9903);
            var players = state.Players.Where(item => item.Slot != PlayerSlot.Neutral).ToList();
            players[0].HasCompletedSelfLearningAI = true;
            players[1].HasCompletedSelfLearningAI = true;

            var winner = VictoryResolver.ResolveScience(state);

            Assert.That(winner.IsValid, Is.False);
            Assert.That(state.Victory, Is.EqualTo(VictoryType.Draw));
            Assert.That(state.WinnerId.IsValid, Is.False);
        }

        [Test]
        public void ColdWarPlayerWithOnlyUnpillagedNuclearFacilityWinsByScience()
        {
            var state = PrototypeMatchFactory.Create(9904);
            var players = state.Players.Where(item => item.Slot != PlayerSlot.Neutral).ToList();
            var first = AddColdWarFacility(state, players[0], true);
            AddColdWarFacility(state, players[1], false);

            var winner = VictoryResolver.ResolveColdWarNuclearStrike(state);

            Assert.That(first.IsPillaged, Is.True);
            Assert.That(winner, Is.EqualTo(players[1].Id));
            Assert.That(state.Victory, Is.EqualTo(VictoryType.Science));
        }

        [Test]
        public void ColdWarContinuesWhenBothFacilitiesArePillagedOrBothAreRepaired()
        {
            var state = PrototypeMatchFactory.Create(9905);
            var players = state.Players.Where(item => item.Slot != PlayerSlot.Neutral).ToList();
            var first = AddColdWarFacility(state, players[0], true);
            var second = AddColdWarFacility(state, players[1], true);

            Assert.That(VictoryResolver.ResolveColdWarNuclearStrike(state).IsValid, Is.False);
            first.IsPillaged = false;
            second.IsPillaged = false;
            Assert.That(VictoryResolver.ResolveColdWarNuclearStrike(state).IsValid, Is.False);
            Assert.That(state.Victory, Is.EqualTo(VictoryType.None));
        }

        [Test]
        public void ExistingScienceVictoryCannotBeOverwrittenByCulture()
        {
            var state = PrototypeMatchFactory.Create(9902);
            var one = state.Players.Single(item => item.Slot == PlayerSlot.PlayerOne);
            var two = state.Players.Single(item => item.Slot == PlayerSlot.PlayerTwo);
            one.HasMetCultureVictoryCondition = true;
            two.HasCompletedNuclearProject = true;

            VictoryResolver.ResolveScience(state);
            var cultureWinner = VictoryResolver.ResolveCulture(state);

            Assert.That(cultureWinner.IsValid, Is.False);
            Assert.That(state.Victory, Is.EqualTo(VictoryType.Science));
            Assert.That(state.WinnerId, Is.EqualTo(two.Id));
        }

        [Test]
        public void ReciprocalCapitalCaptureSwapsControlAndContinuesGame()
        {
            var state = PrototypeMatchFactory.Create(9906);
            var players = state.Players.Where(item => item.Slot != PlayerSlot.Neutral).ToList();
            var firstCity = state.Cities.Single(item => item.OwnerId == players[0].Id);
            var secondCity = state.Cities.Single(item => item.OwnerId == players[1].Id);
            var firstGovernment = state.Districts.Single(item => item.CityId == firstCity.Id &&
                item.Type == DistrictType.Government);
            var secondGovernment = state.Districts.Single(item => item.CityId == secondCity.Id &&
                item.Type == DistrictType.Government);
            firstCity.Gold = 17;
            firstCity.StoredFood = 23;
            secondCity.Gold = 31;
            secondCity.StoredFood = 37;
            var firstUnit = state.Units.First(item => item.OwnerId == players[0].Id);
            var secondUnit = state.Units.First(item => item.OwnerId == players[1].Id);
            firstUnit.TileId = secondGovernment.TileId;
            secondUnit.TileId = firstGovernment.TileId;
            firstGovernment.ControllerId = players[1].Id;
            secondGovernment.ControllerId = players[0].Id;
            state.Tiles.Single(item => item.Id == firstGovernment.TileId).ControllerId = players[1].Id;
            state.Tiles.Single(item => item.Id == secondGovernment.TileId).ControllerId = players[0].Id;
            state.UnitTrainings.Add(new UnitTrainingState
            {
                Id = state.AllocateId(), DistrictId = firstGovernment.Id,
                OwnerId = players[0].Id, Type = UnitType.Militia, RemainingTurns = 1
            });

            var winner = VictoryResolver.ResolveConquest(state);

            Assert.That(winner.IsValid, Is.False);
            Assert.That(state.IsGameOver, Is.False);
            Assert.That(firstGovernment.ControllerId, Is.EqualTo(players[1].Id));
            Assert.That(secondGovernment.ControllerId, Is.EqualTo(players[0].Id));
            Assert.That(firstCity.OwnerId, Is.EqualTo(players[1].Id));
            Assert.That(secondCity.OwnerId, Is.EqualTo(players[0].Id));
            Assert.That(state.Tiles.Where(item => item.CityId == firstCity.Id)
                .All(item => item.ControllerId == players[1].Id), Is.True);
            Assert.That(state.Tiles.Where(item => item.CityId == secondCity.Id)
                .All(item => item.ControllerId == players[0].Id), Is.True);
            Assert.That(firstCity.Gold, Is.EqualTo(17));
            Assert.That(firstCity.StoredFood, Is.EqualTo(23));
            Assert.That(secondCity.Gold, Is.EqualTo(31));
            Assert.That(secondCity.StoredFood, Is.EqualTo(37));
            Assert.That(firstUnit.OwnerId, Is.EqualTo(players[0].Id));
            Assert.That(firstUnit.HomeCityId, Is.EqualTo(secondCity.Id));
            Assert.That(secondUnit.OwnerId, Is.EqualTo(players[1].Id));
            Assert.That(secondUnit.HomeCityId, Is.EqualTo(firstCity.Id));
            Assert.That(state.UnitTrainings.Single().OwnerId, Is.EqualTo(players[1].Id));
            Assert.That(CityEconomyResolver.CalculateBreakdown(state, firstCity).Food.Total,
                Is.GreaterThan(0));
            Assert.That(CityEconomyResolver.CalculateBreakdown(state, secondCity).Food.Total,
                Is.GreaterThan(0));
        }

        [Test]
        public void ReciprocalCapitalCaptureEmitsCityExchangeEvent()
        {
            var state = PrototypeMatchFactory.Create(9907);
            var players = state.Players.Where(item => item.Slot != PlayerSlot.Neutral).ToList();
            var firstCity = state.Cities.Single(item => item.OwnerId == players[0].Id);
            var secondCity = state.Cities.Single(item => item.OwnerId == players[1].Id);
            var firstGovernment = state.Districts.Single(item => item.CityId == firstCity.Id &&
                item.Type == DistrictType.Government);
            var secondGovernment = state.Districts.Single(item => item.CityId == secondCity.Id &&
                item.Type == DistrictType.Government);
            var firstUnit = state.Units.First(item => item.OwnerId == players[0].Id);
            var secondUnit = state.Units.First(item => item.OwnerId == players[1].Id);
            firstUnit.TileId = secondGovernment.TileId;
            secondUnit.TileId = firstGovernment.TileId;
            firstGovernment.ControllerId = players[1].Id;
            secondGovernment.ControllerId = players[0].Id;

            var result = new TurnProcessor().Resolve(state, new GameCommand[0]);
            var exchange = result.Events.Single(item =>
                item.Type == GameEventType.PlayerCitiesExchanged);

            Assert.That(exchange.SourceId, Is.EqualTo(firstCity.Id));
            Assert.That(exchange.TargetId, Is.EqualTo(secondCity.Id));
            Assert.That(state.IsGameOver, Is.False);
        }

        private static Fixture CreateConquestTurn()
        {
            var state = GameState.CreateNew(9900);
            var one = new PlayerState { Id = state.AllocateId(), Slot = PlayerSlot.PlayerOne };
            var two = new PlayerState { Id = state.AllocateId(), Slot = PlayerSlot.PlayerTwo };
            state.Players.Add(one);
            state.Players.Add(two);
            var cityOne = new CityState
            {
                Id = state.AllocateId(), OwnerId = one.Id, Population = 1,
                GovernmentCitizens = 1, Gold = 10
            };
            var cityTwo = new CityState
            {
                Id = state.AllocateId(), OwnerId = two.Id, Population = 1,
                GovernmentCitizens = 1, Gold = 10
            };
            state.Cities.Add(cityOne);
            state.Cities.Add(cityTwo);
            var target = new TileState
            {
                Id = state.AllocateId(), CityId = cityOne.Id, ControllerId = one.Id, Q = 0, R = 0
            };
            var source = new TileState
            {
                Id = state.AllocateId(), CityId = cityTwo.Id, ControllerId = two.Id, Q = 1, R = 0
            };
            state.Tiles.Add(target);
            state.Tiles.Add(source);
            state.MapTopology.CityViews.Add(new CityMapView
            {
                CityId = cityOne.Id,
                Tiles = new List<CityTilePlacement>
                {
                    new CityTilePlacement { TileId = target.Id, LocalQ = 0, LocalR = 0 },
                    new CityTilePlacement { TileId = source.Id, LocalQ = 1, LocalR = 0 }
                }
            });
            state.Districts.Add(Government(state, cityOne, target));
            state.Districts.Add(Government(state, cityTwo, source));
            var attacker = new UnitState
            {
                Id = state.AllocateId(), OwnerId = two.Id, HomeCityId = cityTwo.Id,
                TileId = source.Id, Type = UnitType.Militia, HitPoints = 16,
                CarriedFood = 6, RemainingMovement = 2
            };
            state.Units.Add(attacker);
            return new Fixture
            {
                State = state, PlayerOne = one, PlayerTwo = two,
                ConquestCommand = new GameCommand
                {
                    CommandId = state.AllocateId(), PlayerId = two.Id, TurnNumber = state.TurnNumber,
                    Type = GameCommandType.MoveUnit, SubjectId = attacker.Id,
                    TargetId = target.Id, Path = new List<EntityId> { target.Id }
                }
            };
        }

        private static DistrictState AddColdWarFacility(GameState state, PlayerState player, bool pillaged)
        {
            player.HasCompletedNuclearProject = true;
            player.HasUnlockedSelfLearningAI = true;
            var city = state.Cities.Single(item => item.OwnerId == player.Id);
            var tile = state.Tiles.First(item => item.CityId == city.Id &&
                state.Districts.All(district => district.TileId != item.Id));
            var district = new DistrictState
            {
                Id = state.AllocateId(), CityId = city.Id, TileId = tile.Id,
                Type = DistrictType.NuclearFacility, ControllerId = player.Id,
                AssignedCitizens = 1, IsOperational = !pillaged, IsPillaged = pillaged
            };
            state.Districts.Add(district);
            return district;
        }

        private static DistrictState Government(GameState state, CityState city, TileState tile) =>
            new DistrictState
            {
                Id = state.AllocateId(), CityId = city.Id, TileId = tile.Id,
                Type = DistrictType.Government, ControllerId = city.OwnerId,
                IsOperational = true, AssignedCitizens = 1
            };

        private sealed class Fixture
        {
            public GameState State;
            public PlayerState PlayerOne;
            public PlayerState PlayerTwo;
            public GameCommand ConquestCommand;
        }
    }
}
