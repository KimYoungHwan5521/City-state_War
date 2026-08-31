using System.Collections.Generic;
using System.Linq;
using LittleCiv.Core;
using NUnit.Framework;

namespace LittleCiv.Tests
{
    public sealed class MovementPriorityResolverTests
    {
        [Test]
        public void ControlledTile_GivesControllerUnitPriority()
        {
            var fixture = CreateFixture(shared: false, controllerSlot: PlayerSlot.PlayerTwo);

            var plan = MovementPriorityResolver.Build(fixture.State, fixture.Commands);

            Assert.That(plan.OrderedCommands.Single().PlayerId, Is.EqualTo(fixture.PlayerTwo));
            Assert.That(plan.BlockedCommandReasons[fixture.Commands[0].CommandId],
                Is.EqualTo(MovementStopReason.PriorityLost));
        }

        [Test]
        public void SharedTile_GivesCloserUnitPriority()
        {
            var fixture = CreateFixture(shared: true, controllerSlot: PlayerSlot.Neutral);
            fixture.Commands[0].Path.Insert(0, fixture.PlayerOneStart);

            var plan = MovementPriorityResolver.Build(fixture.State, fixture.Commands);

            Assert.That(plan.OrderedCommands.Single().PlayerId, Is.EqualTo(fixture.PlayerTwo));
        }

        [Test]
        public void SharedTile_WhenDistanceTiesGivesFasterUnitPriority()
        {
            var fixture = CreateFixture(shared: true, controllerSlot: PlayerSlot.Neutral);
            fixture.UnitTwo.Type = UnitType.Supply;

            var plan = MovementPriorityResolver.Build(fixture.State, fixture.Commands);

            Assert.That(plan.OrderedCommands.Single().PlayerId, Is.EqualTo(fixture.PlayerTwo));
        }

        [Test]
        public void SharedTile_WhenAllElseTiesGivesPlayerOnePriority()
        {
            var fixture = CreateFixture(shared: true, controllerSlot: PlayerSlot.Neutral);

            var plan = MovementPriorityResolver.Build(fixture.State, fixture.Commands);

            Assert.That(plan.OrderedCommands.Single().PlayerId, Is.EqualTo(fixture.PlayerOne));
        }

        [Test]
        public void SwappingEnemyUnits_BlocksBothAsAttackers()
        {
            var fixture = CreateFixture(shared: false, controllerSlot: PlayerSlot.PlayerOne);
            fixture.Commands[0].Path = new List<EntityId> { fixture.PlayerTwoStart };
            fixture.Commands[1].Path = new List<EntityId> { fixture.PlayerOneStart };

            var plan = MovementPriorityResolver.Build(fixture.State, fixture.Commands);

            Assert.That(plan.OrderedCommands, Is.Empty);
            Assert.That(plan.BlockedCommandReasons.Values,
                Is.All.EqualTo(MovementStopReason.SwapConflict));
        }

        [Test]
        public void TurnProcessor_AppliesPriorityBeforeMovement()
        {
            var fixture = CreateFixture(shared: false, controllerSlot: PlayerSlot.PlayerTwo);

            var result = new TurnProcessor().Resolve(fixture.State, fixture.Commands);

            Assert.That(fixture.UnitTwo.TileId, Is.EqualTo(fixture.Destination));
            Assert.That(fixture.UnitOne.TileId, Is.EqualTo(fixture.PlayerOneStart));
            Assert.That(result.Events.Any(item =>
                item.Type == GameEventType.MovementBlocked &&
                item.SourceId == fixture.UnitOne.Id &&
                item.SecondaryValue == (int)MovementStopReason.PriorityLost), Is.True);
            Assert.That(result.ManeuverRequests.Any(item => item.UnitId == fixture.UnitOne.Id), Is.True);
        }

        private static Fixture CreateFixture(bool shared, PlayerSlot controllerSlot)
        {
            var state = GameState.CreateNew(777);
            var playerOne = state.AllocateId();
            var playerTwo = state.AllocateId();
            var neutral = state.AllocateId();
            state.Players.Add(new PlayerState { Id = playerOne, Slot = PlayerSlot.PlayerOne });
            state.Players.Add(new PlayerState { Id = playerTwo, Slot = PlayerSlot.PlayerTwo });
            state.Players.Add(new PlayerState { Id = neutral, Slot = PlayerSlot.Neutral });
            var left = state.AllocateId();
            var destination = state.AllocateId();
            var right = state.AllocateId();
            var controller = controllerSlot == PlayerSlot.PlayerOne
                ? playerOne
                : controllerSlot == PlayerSlot.PlayerTwo ? playerTwo : neutral;
            state.Tiles.Add(new TileState { Id = left, ControllerId = playerOne });
            state.Tiles.Add(new TileState
            {
                Id = destination,
                ControllerId = controller,
                IsSharedBoundary = shared
            });
            state.Tiles.Add(new TileState { Id = right, ControllerId = playerTwo });
            state.MapTopology.CityViews.Add(new CityMapView
            {
                CityId = state.AllocateId(),
                Tiles = new List<CityTilePlacement>
                {
                    new CityTilePlacement { TileId = left, LocalQ = -1, LocalR = 0 },
                    new CityTilePlacement { TileId = destination, LocalQ = 0, LocalR = 0 },
                    new CityTilePlacement { TileId = right, LocalQ = 1, LocalR = 0 }
                }
            });
            var unitOne = new UnitState
            {
                Id = state.AllocateId(), OwnerId = playerOne, TileId = left,
                Type = UnitType.Militia, HitPoints = 16
            };
            var unitTwo = new UnitState
            {
                Id = state.AllocateId(), OwnerId = playerTwo, TileId = right,
                Type = UnitType.Militia, HitPoints = 16
            };
            state.Units.Add(unitOne);
            state.Units.Add(unitTwo);
            return new Fixture
            {
                State = state,
                PlayerOne = playerOne,
                PlayerTwo = playerTwo,
                PlayerOneStart = left,
                PlayerTwoStart = right,
                Destination = destination,
                UnitOne = unitOne,
                UnitTwo = unitTwo,
                Commands = new List<GameCommand>
                {
                    Move(state, unitOne, 8001, destination),
                    Move(state, unitTwo, 8002, destination)
                }
            };
        }

        private static GameCommand Move(GameState state, UnitState unit, long id, EntityId destination)
        {
            return new GameCommand
            {
                CommandId = new EntityId(id),
                PlayerId = unit.OwnerId,
                TurnNumber = state.TurnNumber,
                Type = GameCommandType.MoveUnit,
                SubjectId = unit.Id,
                TargetId = destination,
                Path = new List<EntityId> { destination }
            };
        }

        private sealed class Fixture
        {
            public GameState State;
            public EntityId PlayerOne;
            public EntityId PlayerTwo;
            public EntityId PlayerOneStart;
            public EntityId PlayerTwoStart;
            public EntityId Destination;
            public UnitState UnitOne;
            public UnitState UnitTwo;
            public List<GameCommand> Commands;
        }
    }
}
