using System.Collections.Generic;
using System.Linq;
using LittleCiv.Core;
using NUnit.Framework;

namespace LittleCiv.Tests
{
    public sealed class VictoryResolverTests
    {
        [Test]
        public void CultureBeatsScienceAndConquestWhenAllQualifyInSameTurn()
        {
            var fixture = CreateConquestTurn();
            fixture.PlayerOne.HasMetCultureVictoryCondition = true;
            fixture.PlayerTwo.HasCompletedNuclearProject = true;

            var result = new TurnProcessor().Resolve(fixture.State, new[] { fixture.ConquestCommand });

            Assert.That(fixture.State.Victory, Is.EqualTo(VictoryType.Culture));
            Assert.That(fixture.State.WinnerId, Is.EqualTo(fixture.PlayerOne.Id));
            Assert.That(result.Events.Count(item => item.Type == GameEventType.VictoryTriggered), Is.EqualTo(1));
            Assert.That(result.Events.Single(item => item.Type == GameEventType.VictoryTriggered).PrimaryValue,
                Is.EqualTo((int)VictoryType.Culture));
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
        public void SameTypeSimultaneousCandidatesUsePlayerSlotOrderDeterministically()
        {
            var state = PrototypeMatchFactory.Create(9901);
            var one = state.Players.Single(item => item.Slot == PlayerSlot.PlayerOne);
            var two = state.Players.Single(item => item.Slot == PlayerSlot.PlayerTwo);
            one.HasCompletedNuclearProject = true;
            two.HasCompletedNuclearProject = true;

            var winner = VictoryResolver.ResolveScience(state);

            Assert.That(winner, Is.EqualTo(one.Id));
            Assert.That(state.Victory, Is.EqualTo(VictoryType.Science));
        }

        [Test]
        public void ExistingHigherPriorityVictoryCannotBeOverwritten()
        {
            var state = PrototypeMatchFactory.Create(9902);
            var one = state.Players.Single(item => item.Slot == PlayerSlot.PlayerOne);
            var two = state.Players.Single(item => item.Slot == PlayerSlot.PlayerTwo);
            one.HasMetCultureVictoryCondition = true;
            two.HasCompletedNuclearProject = true;

            VictoryResolver.ResolveCulture(state);
            var scienceWinner = VictoryResolver.ResolveScience(state);

            Assert.That(scienceWinner.IsValid, Is.False);
            Assert.That(state.Victory, Is.EqualTo(VictoryType.Culture));
            Assert.That(state.WinnerId, Is.EqualTo(one.Id));
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
