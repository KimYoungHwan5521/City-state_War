using System.Linq;
using LittleCiv.Core;
using NUnit.Framework;

namespace LittleCiv.Tests
{
    public sealed class UnitStarvationResolverTests
    {
        [Test]
        public void FirstSupplyFailureEntersStarvationWithoutDestroyingUnit()
        {
            var state = PrototypeMatchFactory.Create(9000);
            var unit = state.Units[0];
            unit.CarriedFood = 0;

            var result = UnitStarvationResolver.ResolveFirstFailure(
                state, new EntityId[0], new[] { unit.Id });

            Assert.That(unit.IsStarving, Is.True);
            Assert.That(state.Units, Does.Contain(unit));
            Assert.That(result.EnteredStarvationUnitIds, Does.Contain(unit.Id));
        }

        [Test]
        public void ExistingStarvationEndsWhenUnitIsSupplied()
        {
            var state = PrototypeMatchFactory.Create(9001);
            var unit = state.Units[0];
            unit.IsStarving = true;

            var result = UnitStarvationResolver.ResolveFirstFailure(
                state, new[] { unit.Id }, new EntityId[0]);

            Assert.That(unit.IsStarving, Is.False);
            Assert.That(result.RecoveredFromStarvationUnitIds, Does.Contain(unit.Id));
        }

        [Test]
        public void AlreadyStarvingUnsuppliedUnitDiesOnSecondFailure()
        {
            var state = PrototypeMatchFactory.Create(9002);
            var unit = state.Units[0];
            unit.IsStarving = true;

            var result = UnitStarvationResolver.ResolveFirstFailure(
                state, new EntityId[0], new[] { unit.Id });

            Assert.That(state.Units.Contains(unit), Is.False);
            Assert.That(result.EnteredStarvationUnitIds, Is.Empty);
            Assert.That(result.StarvedToDeathUnitIds, Does.Contain(unit.Id));
        }

        [Test]
        public void TurnProcessorStartsStarvationAndEmitsEventOnFirstFailedFoodPhase()
        {
            var state = PrototypeMatchFactory.Create(9003);
            var unit = state.Units[0];
            MoveOutsideHomeTerritory(state, unit);
            unit.CarriedFood = 0;

            var resolution = new TurnProcessor().Resolve(state, new GameCommand[0]);

            Assert.That(unit.IsStarving, Is.True);
            Assert.That(resolution.Events.Any(item => item.Type == GameEventType.UnitStarvationStarted &&
                item.SourceId == unit.Id), Is.True);
        }

        [Test]
        public void SuppliedStarvingUnitClearsStateButCannotRecoverThatTurn()
        {
            var state = PrototypeMatchFactory.Create(9004);
            var unit = state.Units[0];
            unit.IsStarving = true;
            unit.HitPoints = 10;
            unit.CarriedFood = 1;

            var resolution = new TurnProcessor().Resolve(state, new GameCommand[0]);

            Assert.That(unit.IsStarving, Is.False);
            Assert.That(unit.HitPoints, Is.EqualTo(10));
            Assert.That(resolution.Events.Any(item => item.Type == GameEventType.UnitStarvationEnded &&
                item.SourceId == unit.Id), Is.True);
            Assert.That(resolution.Events.Any(item => item.Type == GameEventType.UnitRecovered &&
                item.SourceId == unit.Id), Is.False);
        }

        [Test]
        public void TwoConsecutiveFailedTurnFoodPhasesDestroyUnitAndEmitCause()
        {
            var state = PrototypeMatchFactory.Create(9005);
            var unit = state.Units[0];
            MoveOutsideHomeTerritory(state, unit);
            unit.CarriedFood = 0;
            var processor = new TurnProcessor();

            processor.Resolve(state, new GameCommand[0]);
            Assert.That(state.Units.Contains(unit), Is.True);
            Assert.That(unit.IsStarving, Is.True);
            var second = processor.Resolve(state, new GameCommand[0]);

            Assert.That(state.Units.Contains(unit), Is.False);
            Assert.That(second.Events.Any(item => item.Type == GameEventType.UnitStarvedToDeath &&
                item.SourceId == unit.Id), Is.True);
            Assert.That(second.Events.Any(item => item.Type == GameEventType.UnitDestroyed &&
                item.SourceId == unit.Id), Is.True);
        }

        private static void MoveOutsideHomeTerritory(GameState state, UnitState unit)
        {
            unit.TileId = state.Tiles.First(item =>
                state.Cities.First(city => city.Id == item.CityId).OwnerId != unit.OwnerId &&
                state.Units.All(other => other.Id == unit.Id || other.TileId != item.Id)).Id;
        }
    }
}
