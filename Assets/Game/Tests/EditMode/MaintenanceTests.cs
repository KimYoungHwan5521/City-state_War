using System.Linq;
using LittleCiv.Core;
using NUnit.Framework;

namespace LittleCiv.Tests
{
    public sealed class MaintenanceTests
    {
        [Test]
        public void UnitsArePaidBeforeFacilitiesAndSupplyDisbandsFirst()
        {
            var state = PrototypeMatchFactory.Create(4800);
            var city = state.Cities[0];
            city.Gold = 1;
            var supply = AddUnit(state, city, UnitType.Supply, 12);

            var result = MaintenanceResolver.Resolve(state);

            Assert.That(state.Units.Any(item => item.Id == supply.Id), Is.False);
            Assert.That(result.DisbandedUnits, Does.Contain(supply.Id));
            Assert.That(result.DisbandedUnitRecords.Single().HomeCityId, Is.EqualTo(city.Id));
            Assert.That(result.DisbandedUnitRecords.Single().ReturnedFood, Is.EqualTo(12));
            Assert.That(city.StoredFood, Is.EqualTo(12));
            Assert.That(city.Gold, Is.Zero);
        }

        [Test]
        public void FacilitySuspensionUsesCultureScienceNuclearOrder()
        {
            var state = PrototypeMatchFactory.Create(4801);
            var city = state.Cities[0];
            state.Units.RemoveAll(item => item.OwnerId == city.OwnerId);
            city.Gold = 1;
            var culture = AddDistrict(state, city, DistrictType.Culture);
            var science = AddDistrict(state, city, DistrictType.Science);

            var result = MaintenanceResolver.Resolve(state);

            Assert.That(culture.IsMaintenanceSuspended, Is.True);
            Assert.That(culture.IsOperational, Is.False);
            Assert.That(science.IsOperational, Is.True);
            Assert.That(result.SuspendedDistricts, Does.Contain(culture.Id));
            Assert.That(city.Gold, Is.Zero);
        }

        [Test]
        public void SuspendedOrdinaryDistrictReactivatesImmediatelyWhenAffordable()
        {
            var state = PrototypeMatchFactory.Create(4802);
            var city = state.Cities[0];
            state.Units.RemoveAll(item => item.OwnerId == city.OwnerId);
            var science = AddDistrict(state, city, DistrictType.Science);
            science.IsOperational = false;
            science.IsMaintenanceSuspended = true;
            city.Gold = 1;

            MaintenanceResolver.Resolve(state);

            Assert.That(science.IsMaintenanceSuspended, Is.False);
            Assert.That(science.IsOperational, Is.True);
            Assert.That(city.Gold, Is.Zero);
        }

        [Test]
        public void PlayerMaintenancePriorityOverridesDefaultUnitOrder()
        {
            var state = PrototypeMatchFactory.Create(4803);
            var city = state.Cities[0];
            state.Units.RemoveAll(item => item.OwnerId == city.OwnerId);
            city.Gold = 1;
            var supply = AddUnit(state, city, UnitType.Supply, 0);
            var mechanized = AddUnit(state, city, UnitType.MechanizedInfantry, 0);
            mechanized.MaintenancePriority = 1;

            MaintenanceResolver.Resolve(state);

            Assert.That(state.Units.Any(item => item.Id == mechanized.Id), Is.False);
            Assert.That(state.Units.Any(item => item.Id == supply.Id), Is.True);
        }

        [Test]
        public void MaintenancePriorityCommandPersistsForUnit()
        {
            var state = PrototypeMatchFactory.Create(4804);
            var city = state.Cities[0];
            var unit = state.Units.First(item => item.OwnerId == city.OwnerId);
            var command = new GameCommand
            {
                CommandId = state.AllocateId(), PlayerId = city.OwnerId,
                TurnNumber = state.TurnNumber, Type = GameCommandType.SetPriority,
                TargetId = unit.Id, PrimaryValue = 1, SecondaryValue = 1
            };

            new TurnProcessor().Resolve(state, new[] { command });

            Assert.That(unit.MaintenancePriority, Is.EqualTo(1));
        }

        [Test]
        public void RemoteUnitDisbandsToHomeWithoutCreatingGroundFood()
        {
            var state = PrototypeMatchFactory.Create(4805);
            var city = state.Cities[0];
            var enemyCity = state.Cities[1];
            state.Units.RemoveAll(item => item.OwnerId == city.OwnerId);
            city.Gold = 0;
            var remoteTile = state.Tiles.First(item => item.CityId == enemyCity.Id);
            remoteTile.GroundFood = 3;
            var unit = AddUnit(state, city, UnitType.MechanizedInfantry, 8);
            unit.TileId = remoteTile.Id;

            var result = MaintenanceResolver.Resolve(state);

            Assert.That(state.Units.Contains(unit), Is.False);
            Assert.That(city.StoredFood, Is.EqualTo(8));
            Assert.That(remoteTile.GroundFood, Is.EqualTo(3));
            var record = result.DisbandedUnitRecords.Single(item => item.UnitId == unit.Id);
            Assert.That(record.HomeCityId, Is.EqualTo(city.Id));
            Assert.That(record.PreviousTileId, Is.EqualTo(remoteTile.Id));
            Assert.That(record.ReturnedFood, Is.EqualTo(8));
        }

        [Test]
        public void TurnProcessorDisbandEventIdentifiesHomeCityAndReturnedFood()
        {
            var state = PrototypeMatchFactory.Create(4806);
            var city = state.Cities[0];
            var enemyCity = state.Cities[1];
            state.Units.RemoveAll(item => item.OwnerId == city.OwnerId);
            city.Gold = 0;
            var unit = AddUnit(state, city, UnitType.MechanizedInfantry, 8);
            unit.TileId = state.Tiles.First(item => item.CityId == enemyCity.Id).Id;

            var resolution = new TurnProcessor().Resolve(state, new GameCommand[0]);

            Assert.That(state.Units.Contains(unit), Is.False);
            Assert.That(city.StoredFood, Is.EqualTo(8));
            Assert.That(resolution.Events.Any(item => item.Type == GameEventType.UnitDisbanded &&
                item.SourceId == unit.Id && item.TargetId == city.Id && item.PrimaryValue == 8), Is.True);
        }

        private static UnitState AddUnit(GameState state, CityState city, UnitType type, int food)
        {
            var unit = new UnitState
            {
                Id = state.AllocateId(), OwnerId = city.OwnerId,
                TileId = state.Tiles.First(item => item.CityId == city.Id).Id,
                Type = type, HitPoints = 16, CarriedFood = food
            };
            state.Units.Add(unit);
            return unit;
        }

        private static DistrictState AddDistrict(GameState state, CityState city, DistrictType type)
        {
            var occupied = state.Districts.Select(item => item.TileId).ToArray();
            var tile = state.Tiles.First(item => item.CityId == city.Id && !occupied.Contains(item.Id));
            var district = new DistrictState
            {
                Id = state.AllocateId(), CityId = city.Id, TileId = tile.Id,
                Type = type, ControllerId = city.OwnerId,
                IsOperational = true, AssignedCitizens = 1
            };
            state.Districts.Add(district);
            return district;
        }
    }
}
