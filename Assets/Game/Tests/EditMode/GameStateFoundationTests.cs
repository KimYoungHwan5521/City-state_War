using LittleCiv.Core;
using LittleCiv.Runtime;
using NUnit.Framework;

namespace LittleCiv.Tests
{
    public sealed class GameStateFoundationTests
    {
        [Test]
        public void AllocateId_ReturnsStableMonotonicIds()
        {
            var state = GameState.CreateNew(1234);

            Assert.That(state.AllocateId().Value, Is.EqualTo(1));
            Assert.That(state.AllocateId().Value, Is.EqualTo(2));
            Assert.That(state.NextEntityId, Is.EqualTo(3));
        }

        [Test]
        public void DeterministicRandom_SameSeedProducesSameSequence()
        {
            var first = new DeterministicRandom(777);
            var second = new DeterministicRandom(777);

            for (var i = 0; i < 100; i++)
            {
                Assert.That(first.NextUInt64(), Is.EqualTo(second.NextUInt64()));
            }
        }

        [Test]
        public void SerializeRoundTrip_PreservesStateHash()
        {
            var original = CreateRepresentativeState();

            var json = GameStateJsonSerializer.Serialize(original);
            var restored = GameStateJsonSerializer.Deserialize(json);

            Assert.That(restored.SchemaVersion, Is.EqualTo(GameState.CurrentSchemaVersion));
            Assert.That(restored.Players.Count, Is.EqualTo(2));
            Assert.That(restored.Cities.Count, Is.EqualTo(1));
            Assert.That(restored.Units.Count, Is.EqualTo(1));
            Assert.That(GameStateHasher.Compute(restored), Is.EqualTo(GameStateHasher.Compute(original)));
        }

        [Test]
        public void StateHash_IsIndependentOfEntityListInsertionOrder()
        {
            var first = CreateRepresentativeState();
            var second = CreateRepresentativeState();
            second.Players.Reverse();

            Assert.That(GameStateHasher.Compute(second), Is.EqualTo(GameStateHasher.Compute(first)));
        }

        [Test]
        public void CommandValidator_RejectsWrongTurn()
        {
            var state = CreateRepresentativeState();
            var command = new GameCommand
            {
                CommandId = new EntityId(99),
                PlayerId = state.Players[0].Id,
                TurnNumber = state.TurnNumber + 1,
                Type = GameCommandType.MoveUnit
            };

            Assert.That(
                CommandValidator.ValidateEnvelope(state, command),
                Is.EqualTo(CommandValidationError.WrongTurn));
        }

        private static GameState CreateRepresentativeState()
        {
            var state = GameState.CreateNew(4567);
            var playerOneId = state.AllocateId();
            var playerTwoId = state.AllocateId();
            var cityId = state.AllocateId();
            var tileId = state.AllocateId();
            var districtId = state.AllocateId();
            var unitId = state.AllocateId();

            state.Players.Add(new PlayerState
            {
                Id = playerOneId,
                Slot = PlayerSlot.PlayerOne,
                Gold = 10,
                StoredFood = 0,
                ReserveTimeSeconds = 180
            });
            state.Players.Add(new PlayerState
            {
                Id = playerTwoId,
                Slot = PlayerSlot.PlayerTwo,
                Gold = 10,
                StoredFood = 0,
                ReserveTimeSeconds = 180
            });
            state.Cities.Add(new CityState
            {
                Id = cityId,
                OwnerId = playerOneId,
                WorldQ = 0,
                WorldR = 0,
                Population = 4
            });
            state.Tiles.Add(new TileState
            {
                Id = tileId,
                CityId = cityId,
                Q = 0,
                R = 0,
                ControllerId = playerOneId,
                GroundFood = 2
            });
            state.Districts.Add(new DistrictState
            {
                Id = districtId,
                CityId = cityId,
                TileId = tileId,
                Type = DistrictType.Government,
                ControllerId = playerOneId,
                IsOperational = true
            });
            state.Units.Add(new UnitState
            {
                Id = unitId,
                OwnerId = playerOneId,
                TileId = tileId,
                Type = UnitType.Militia,
                HitPoints = 16,
                CarriedFood = 6,
                IsStarving = false
            });

            return state;
        }
    }
}
