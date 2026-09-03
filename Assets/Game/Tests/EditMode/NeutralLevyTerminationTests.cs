using System.Linq;
using LittleCiv.Core;
using NUnit.Framework;

namespace LittleCiv.Tests
{
    public sealed class NeutralLevyTerminationTests
    {
        [Test]
        public void OccupiedOriginDisbandsSurvivingLevyUnitsAndTheirFood()
        {
            var fixture = Create(14300);
            Assert.That(NeutralLevyResolver.TryStart(fixture.State, fixture.Player.Id,
                fixture.Home.Id, fixture.Military.Id, 3, out var levy), Is.True);
            var unit = fixture.State.Units.Single(item => item.Id == levy.Units[0].UnitId);
            Assert.That(unit.CarriedFood, Is.GreaterThan(0));
            var storedBefore = fixture.Military.StoredFood;
            OccupyOrigin(fixture);

            var record = NeutralLevyResolver.DisbandInvalidOrigins(fixture.State).Single();

            Assert.That(record.TerminatedEarly, Is.True);
            Assert.That(record.DisbandedUnits, Is.EqualTo(1));
            Assert.That(fixture.State.Units.Any(item => item.Id == unit.Id), Is.False);
            Assert.That(fixture.Military.StoredFood, Is.EqualTo(storedBefore));
            Assert.That(fixture.State.Levies, Is.Empty);
        }

        [Test]
        public void MissingLevyUnitIsNotCountedOrRemovedTwice()
        {
            var fixture = Create(14301);
            Assert.That(NeutralLevyResolver.TryStart(fixture.State, fixture.Player.Id,
                fixture.Home.Id, fixture.Military.Id, 3, out var levy), Is.True);
            var unit = fixture.State.Units.Single(item => item.Id == levy.Units[0].UnitId);
            fixture.State.Units.Remove(unit);
            OccupyOrigin(fixture);

            var record = NeutralLevyResolver.DisbandInvalidOrigins(fixture.State).Single();

            Assert.That(record.DisbandedUnits, Is.Zero);
            Assert.That(fixture.State.Levies, Is.Empty);
        }

        [Test]
        public void TurnProcessorTerminatesAfterTurnPhasesAndBeforeTurnEndedEvent()
        {
            var fixture = Create(14302);
            Assert.That(NeutralLevyResolver.TryStart(fixture.State, fixture.Player.Id,
                fixture.Home.Id, fixture.Military.Id, 3, out _), Is.True);
            OccupyOrigin(fixture);

            var turn = new TurnProcessor().Resolve(fixture.State, new GameCommand[0]);
            var termination = turn.Events.FindIndex(item =>
                item.Type == GameEventType.NeutralLevyTerminated);
            var ended = turn.Events.FindIndex(item => item.Type == GameEventType.TurnEnded);

            Assert.That(termination, Is.GreaterThanOrEqualTo(0));
            Assert.That(ended, Is.GreaterThan(termination));
            Assert.That(fixture.State.Levies, Is.Empty);
        }

        private static Fixture Create(long seed)
        {
            var state = PrototypeMatchFactory.Create(seed);
            var player = state.Players.Single(item => item.Slot == PlayerSlot.PlayerOne);
            var neutral = state.Players.Single(item => item.Slot == PlayerSlot.Neutral);
            var home = state.Cities.Single(item => item.OwnerId == player.Id);
            home.Gold = 100;
            var military = state.Cities.First(item => item.OwnerId == neutral.Id &&
                item.NeutralSpecialization == NeutralCitySpecialization.Military);
            var government = state.Districts.Single(item =>
                item.CityId == military.Id && item.Type == DistrictType.Government);
            state.Units.Add(new UnitState
            {
                Id = state.AllocateId(), OwnerId = neutral.Id, HomeCityId = military.Id,
                TileId = government.TileId, Type = UnitType.Militia,
                HitPoints = UnitRules.MaximumHitPoints(UnitType.Militia)
            });
            return new Fixture
            {
                State = state, Player = player, Home = home,
                Military = military
            };
        }

        private static void OccupyOrigin(Fixture fixture)
        {
            var opponent = fixture.State.Players.Single(item => item.Slot == PlayerSlot.PlayerTwo);
            var government = fixture.State.Districts.Single(item =>
                item.CityId == fixture.Military.Id && item.Type == DistrictType.Government);
            fixture.Military.OccupyingPlayerId = opponent.Id;
            government.ControllerId = opponent.Id;
            fixture.State.Tiles.Single(item => item.Id == government.TileId).ControllerId = opponent.Id;
        }

        private sealed class Fixture
        {
            public GameState State;
            public PlayerState Player;
            public CityState Home;
            public CityState Military;
        }
    }
}
