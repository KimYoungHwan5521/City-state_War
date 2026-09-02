using System.Linq;
using LittleCiv.Core;
using NUnit.Framework;

namespace LittleCiv.Tests
{
    public sealed class UnitRecoveryResolverTests
    {
        [TestCase(UnitType.Militia, 2)]
        [TestCase(UnitType.IronInfantry, 2)]
        [TestCase(UnitType.GunpowderInfantry, 3)]
        [TestCase(UnitType.MechanizedInfantry, 4)]
        [TestCase(UnitType.Supply, 1)]
        [TestCase(UnitType.MotorizedSupply, 2)]
        public void RecoveryIsOneEighthOfMaximumHitPoints(UnitType type, int expected)
        {
            Assert.That(UnitRules.RecoveryPerTurn(type), Is.EqualTo(expected));
        }

        [Test]
        public void SuppliedWoundedUnitRecoversWithoutExceedingMaximum()
        {
            var state = PrototypeMatchFactory.Create(8000);
            var unit = state.Units[0];
            unit.HitPoints = 15;

            var result = UnitRecoveryResolver.Resolve(state, new[] { unit.Id });

            Assert.That(unit.HitPoints, Is.EqualTo(16));
            Assert.That(result.Single().RecoveredHitPoints, Is.EqualTo(1));
        }

        [Test]
        public void UnsuppliedOrStarvingUnitDoesNotRecover()
        {
            var state = PrototypeMatchFactory.Create(8001);
            var unsupplied = state.Units[0];
            var starving = state.Units[1];
            unsupplied.HitPoints = 10;
            starving.HitPoints = 10;
            starving.IsStarving = true;

            var result = UnitRecoveryResolver.Resolve(state, new[] { starving.Id });

            Assert.That(result, Is.Empty);
            Assert.That(unsupplied.HitPoints, Is.EqualTo(10));
            Assert.That(starving.HitPoints, Is.EqualTo(10));
        }

        [Test]
        public void TurnProcessorConsumesFoodThenRecoversAndEmitsEvent()
        {
            var state = PrototypeMatchFactory.Create(8002);
            var unit = state.Units[0];
            MoveOutsideHomeTerritory(state, unit);
            unit.HitPoints = 10;
            unit.CarriedFood = 2;

            var resolution = new TurnProcessor().Resolve(state, new GameCommand[0]);

            Assert.That(unit.CarriedFood, Is.EqualTo(1));
            Assert.That(unit.HitPoints, Is.EqualTo(12));
            Assert.That(resolution.Events.Any(item => item.Type == GameEventType.UnitRecovered &&
                item.SourceId == unit.Id && item.PrimaryValue == 2), Is.True);
        }

        private static void MoveOutsideHomeTerritory(GameState state, UnitState unit)
        {
            unit.TileId = state.Tiles.First(item =>
                state.Cities.First(city => city.Id == item.CityId).OwnerId != unit.OwnerId &&
                state.Units.All(other => other.Id == unit.Id || other.TileId != item.Id)).Id;
        }
    }
}
