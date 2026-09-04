using System.Collections.Generic;
using System.Linq;
using LittleCiv.Core;
using NUnit.Framework;

namespace LittleCiv.Tests
{
    public sealed class MovementResolverTests
    {
        [Test]
        public void Resolve_MovesAlongAdjacentReservedPathAndSpendsMovement()
        {
            var state = PrototypeMatchFactory.Create(101);
            var unit = state.Units[0];
            var path = PathFromGovernment(state, unit.TileId, 2);
            unit.RemainingMovement = 2;

            var result = MovementResolver.Resolve(state, Move(state, unit, path));

            Assert.That(result.StopReason, Is.EqualTo(MovementStopReason.Completed));
            Assert.That(result.StepsMoved, Is.EqualTo(2));
            Assert.That(unit.TileId, Is.EqualTo(path[1]));
            Assert.That(unit.RemainingMovement, Is.Zero);
        }

        [Test]
        public void Resolve_StopsAtLastValidTileWhenMovementRunsOut()
        {
            var state = PrototypeMatchFactory.Create(102);
            var unit = state.Units[0];
            var path = PathFromGovernment(state, unit.TileId, 2);
            unit.RemainingMovement = 1;

            var result = MovementResolver.Resolve(state, Move(state, unit, path));

            Assert.That(result.StopReason, Is.EqualTo(MovementStopReason.InsufficientMovement));
            Assert.That(result.StepsMoved, Is.EqualTo(1));
            Assert.That(unit.TileId, Is.EqualTo(path[0]));
        }

        [Test]
        public void Resolve_RejectsNonAdjacentPathStep()
        {
            var state = PrototypeMatchFactory.Create(103);
            var unit = state.Units[0];
            var view = ViewContaining(state, unit.TileId);
            var distant = view.Tiles.First(item => item.LocalQ == 3 && item.LocalR == 0).TileId;
            unit.RemainingMovement = 2;

            var result = MovementResolver.Resolve(state, Move(state, unit, new List<EntityId> { distant }));

            Assert.That(result.StopReason, Is.EqualTo(MovementStopReason.NonAdjacentTile));
            Assert.That(result.StepsMoved, Is.Zero);
            Assert.That(unit.TileId, Is.Not.EqualTo(distant));
        }

        [Test]
        public void Resolve_StopsBeforeEnemyOccupiedTile()
        {
            var state = PrototypeMatchFactory.Create(104);
            var unit = state.Units[0];
            var destination = PathFromGovernment(state, unit.TileId, 1)[0];
            state.Units.Add(new UnitState
            {
                Id = state.AllocateId(),
                OwnerId = state.Players[1].Id,
                TileId = destination,
                Type = UnitType.Militia,
                HitPoints = 16
            });
            unit.RemainingMovement = 2;

            var result = MovementResolver.Resolve(state, Move(state, unit, new List<EntityId> { destination }));

            Assert.That(result.StopReason, Is.EqualTo(MovementStopReason.EnemyOccupied));
            Assert.That(result.StepsMoved, Is.Zero);
        }

        [Test]
        public void Resolve_UsesThreeCombatAndOneSupplySlots()
        {
            var state = PrototypeMatchFactory.Create(105);
            var movingUnit = state.Units[0];
            var destination = PathFromGovernment(state, movingUnit.TileId, 1)[0];
            for (var i = 0; i < 3; i++)
            {
                state.Units.Add(new UnitState
                {
                    Id = state.AllocateId(),
                    OwnerId = movingUnit.OwnerId,
                    TileId = destination,
                    Type = UnitType.Militia,
                    HitPoints = 16
                });
            }
            movingUnit.RemainingMovement = 2;

            var result = MovementResolver.Resolve(
                state,
                Move(state, movingUnit, new List<EntityId> { destination }));

            Assert.That(result.StopReason, Is.EqualTo(MovementStopReason.TileCapacityReached));
        }

        [Test]
        public void TurnProcessor_ResetsMovementAndBuildsAutomaticDefenseWhenMovementRemains()
        {
            var state = PrototypeMatchFactory.Create(106);
            var unit = state.Units[0];
            var destination = PathFromGovernment(state, unit.TileId, 1)[0];
            var command = Move(state, unit, new List<EntityId> { destination });

            var result = new TurnProcessor().Resolve(state, new[] { command });

            Assert.That(result.Events.Any(item => item.Type == GameEventType.UnitMoved), Is.True);
            Assert.That(unit.TileId, Is.EqualTo(destination));
            Assert.That(unit.RemainingMovement, Is.Zero);
            Assert.That(unit.HasAutomaticDefense, Is.True);
        }

        [Test]
        public void TurnProcessor_CapacityBlockCreatesVisibleManeuverRecommand()
        {
            var state = PrototypeMatchFactory.Create(107);
            var movingUnit = state.Units[0];
            var destination = PathFromGovernment(state, movingUnit.TileId, 1)[0];
            for (var index = 0; index < 3; index++)
                state.Units.Add(new UnitState
                {
                    Id = state.AllocateId(), OwnerId = movingUnit.OwnerId,
                    HomeCityId = movingUnit.HomeCityId, TileId = destination,
                    Type = UnitType.Militia, HitPoints = 16, CarriedFood = 6
                });

            var result = new TurnProcessor().Resolve(state,
                new[] { Move(state, movingUnit, new List<EntityId> { destination }) });

            Assert.That(result.ManeuverRequests.Any(item => item.UnitId == movingUnit.Id &&
                item.StopReason == MovementStopReason.TileCapacityReached), Is.True);
            Assert.That(movingUnit.ManeuverRecommandTurn, Is.EqualTo(state.TurnNumber));
            Assert.That(movingUnit.RemainingMovement, Is.GreaterThan(0));
        }

        private static GameCommand Move(GameState state, UnitState unit, List<EntityId> path)
        {
            return new GameCommand
            {
                CommandId = new EntityId(9000),
                PlayerId = unit.OwnerId,
                TurnNumber = state.TurnNumber,
                Type = GameCommandType.MoveUnit,
                SubjectId = unit.Id,
                TargetId = path[path.Count - 1],
                Path = path
            };
        }

        private static List<EntityId> PathFromGovernment(GameState state, EntityId tileId, int length)
        {
            var view = ViewContaining(state, tileId);
            var result = new List<EntityId>();
            for (var q = 1; q <= length; q++)
            {
                result.Add(view.Tiles.First(item => item.LocalQ == q && item.LocalR == 0).TileId);
            }
            return result;
        }

        private static CityMapView ViewContaining(GameState state, EntityId tileId)
        {
            return state.MapTopology.CityViews.First(view => view.Tiles.Any(tile => tile.TileId == tileId));
        }
    }
}
