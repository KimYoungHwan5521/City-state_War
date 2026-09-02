using System.Linq;
using LittleCiv.Core;
using NUnit.Framework;

namespace LittleCiv.Tests
{
    // Covers base carrying capacity, loading restrictions, and per-turn consumption.
    public sealed class UnitFoodResolverTests
    {
        [TestCase(UnitType.Militia, 6)]
        [TestCase(UnitType.IronInfantry, 6)]
        [TestCase(UnitType.GunpowderInfantry, 6)]
        [TestCase(UnitType.MechanizedInfantry, 10)]
        [TestCase(UnitType.Supply, 20)]
        [TestCase(UnitType.MotorizedSupply, 40)]
        public void BaseCapacityMatchesPrototypeRules(UnitType type, int expected)
        {
            Assert.That(UnitRules.FoodCapacity(type), Is.EqualTo(expected));
            Assert.That(UnitRules.FoodConsumption(type), Is.EqualTo(1));
        }

        [Test]
        public void LoadFoodUsesOwnedCityStorageAndStopsAtUnitCapacity()
        {
            var state = PrototypeMatchFactory.Create(6000);
            var city = state.Cities[0];
            var unit = state.Units.First(item => item.OwnerId == city.OwnerId);
            city.StoredFood = 20;
            unit.CarriedFood = 4;
            int loaded;

            var accepted = UnitFoodResolver.TryLoad(state, LoadCommand(state, city, unit, 10), out loaded);

            Assert.That(accepted, Is.True);
            Assert.That(loaded, Is.EqualTo(2));
            Assert.That(unit.CarriedFood, Is.EqualTo(6));
            Assert.That(city.StoredFood, Is.EqualTo(18));
        }

        [Test]
        public void NegativeFoodAdjustmentReturnsCarriedFoodToOwnedCityStorage()
        {
            var state = PrototypeMatchFactory.Create(6012);
            var city = state.Cities[0];
            var unit = state.Units.First(item => item.OwnerId == city.OwnerId);
            city.StoredFood = 2;
            unit.CarriedFood = 5;
            int adjustment;

            var accepted = UnitFoodResolver.TryLoad(state, LoadCommand(state, city, unit, -3), out adjustment);

            Assert.That(accepted, Is.True);
            Assert.That(adjustment, Is.EqualTo(-3));
            Assert.That(unit.CarriedFood, Is.EqualTo(2));
            Assert.That(city.StoredFood, Is.EqualTo(5));
        }

        [Test]
        public void LoadFoodRejectsEnemyCityAndOccupiedHomeTile()
        {
            var state = PrototypeMatchFactory.Create(6001);
            var city = state.Cities[0];
            var enemyCity = state.Cities[1];
            var unit = state.Units.First(item => item.OwnerId == city.OwnerId);
            city.StoredFood = 10;
            enemyCity.StoredFood = 10;
            unit.CarriedFood = 0;
            int loaded;

            Assert.That(UnitFoodResolver.TryLoad(state, LoadCommand(state, enemyCity, unit, 3), out loaded), Is.False);
            state.Tiles.First(item => item.Id == unit.TileId).ControllerId = enemyCity.OwnerId;
            Assert.That(UnitFoodResolver.TryLoad(state, LoadCommand(state, city, unit, 3), out loaded), Is.False);
        }

        [Test]
        public void HomeTerritorySuppliesExistingUnitWithoutUsingCarriedFoodAndSkipsNewRecruit()
        {
            var state = PrototypeMatchFactory.Create(6002);
            var existing = state.Units[0];
            existing.CarriedFood = 2;
            var recruit = new UnitState
            {
                Id = state.AllocateId(), OwnerId = existing.OwnerId, TileId = existing.TileId,
                Type = UnitType.Militia, HitPoints = 16, CarriedFood = 2,
                CreatedTurn = state.TurnNumber
            };
            state.Units.Add(recruit);
            CityEconomyResolver.ResolveProduction(state);

            var result = UnitFoodResolver.Consume(state);

            Assert.That(existing.CarriedFood, Is.EqualTo(2));
            Assert.That(result.SuppliedUnitIds, Does.Contain(existing.Id));
            Assert.That(result.Records.Single(item => item.UnitId == existing.Id).Source,
                Is.EqualTo(UnitFoodSource.HomeTerritory));
            Assert.That(recruit.CarriedFood, Is.EqualTo(2));
            Assert.That(result.SuppliedUnitIds.Contains(recruit.Id), Is.False);
        }

        [Test]
        public void UnitOutsideHomeTerritoryConsumesPersonalFood()
        {
            var state = PrototypeMatchFactory.Create(6011);
            var unit = state.Units[0];
            var enemyCity = state.Cities.First(item => item.OwnerId != unit.OwnerId);
            unit.TileId = state.Tiles.First(item => item.CityId == enemyCity.Id).Id;
            unit.CarriedFood = 2;

            var result = UnitFoodResolver.Consume(state);

            Assert.That(unit.CarriedFood, Is.EqualTo(1));
            Assert.That(result.Records.Single(item => item.UnitId == unit.Id).Source,
                Is.EqualTo(UnitFoodSource.Personal));
        }

        [Test]
        public void TurnProcessorLoadsAfterConsumptionAndEmitsBothEvents()
        {
            var state = PrototypeMatchFactory.Create(6003);
            var city = state.Cities[0];
            var unit = state.Units.First(item => item.OwnerId == city.OwnerId);
            city.StoredFood = 10;
            unit.CarriedFood = 2;

            var resolution = new TurnProcessor().Resolve(state, new[] { LoadCommand(state, city, unit, 3) });

            Assert.That(unit.CarriedFood, Is.EqualTo(5));
            Assert.That(resolution.Events.Any(item => item.Type == GameEventType.UnitFoodConsumed &&
                item.SourceId == unit.Id), Is.True);
            Assert.That(resolution.Events.Any(item => item.Type == GameEventType.UnitFoodLoaded &&
                item.SourceId == unit.Id && item.PrimaryValue == 3), Is.True);
        }

        [Test]
        public void SupplyTransfersFoodOnSameTileAndStopsAtReceiverCapacity()
        {
            var state = PrototypeMatchFactory.Create(6004);
            var city = state.Cities[0];
            var receiver = state.Units.First(item => item.OwnerId == city.OwnerId);
            receiver.CarriedFood = 5;
            var supplier = AddUnit(state, city, receiver.TileId, UnitType.Supply, 10);
            int transferred;

            var accepted = UnitFoodResolver.TryTransfer(
                state, TransferCommand(state, supplier, receiver, 8), out transferred);

            Assert.That(accepted, Is.True);
            Assert.That(transferred, Is.EqualTo(1));
            Assert.That(receiver.CarriedFood, Is.EqualTo(6));
            Assert.That(supplier.CarriedFood, Is.EqualTo(9));
        }

        [Test]
        public void AnyFriendlySameTileUnitsCanExchangeFoodButEnemyAndDifferentTileAreRejected()
        {
            var state = PrototypeMatchFactory.Create(6005);
            var city = state.Cities[0];
            var enemyCity = state.Cities[1];
            var receiver = state.Units.First(item => item.OwnerId == city.OwnerId);
            receiver.CarriedFood = 0;
            var ordinary = AddUnit(state, city, receiver.TileId, UnitType.Militia, 6);
            var supplier = AddUnit(state, city, receiver.TileId, UnitType.Supply, 10);
            var enemy = state.Units.First(item => item.OwnerId == enemyCity.OwnerId);
            int transferred;

            Assert.That(UnitFoodResolver.TryTransfer(
                state, TransferCommand(state, ordinary, receiver, 1), out transferred), Is.True);
            Assert.That(transferred, Is.EqualTo(1));
            Assert.That(UnitFoodResolver.TryTransfer(
                state, TransferCommand(state, ordinary, supplier, 2), out transferred), Is.True);
            Assert.That(transferred, Is.EqualTo(2));
            Assert.That(UnitFoodResolver.TryTransfer(
                state, TransferCommand(state, supplier, enemy, 1), out transferred), Is.False);
            supplier.TileId = state.Tiles.First(item => item.CityId == city.Id && item.Id != receiver.TileId).Id;
            Assert.That(UnitFoodResolver.TryTransfer(
                state, TransferCommand(state, supplier, receiver, 1), out transferred), Is.False);
        }

        [Test]
        public void LoadThenTransferCommandsResolveInThatOrderAndEmitTransferEvent()
        {
            var state = PrototypeMatchFactory.Create(6006);
            var city = state.Cities[0];
            var receiver = state.Units.First(item => item.OwnerId == city.OwnerId);
            receiver.CarriedFood = 0;
            var supplier = AddUnit(state, city, receiver.TileId, UnitType.Supply, 0);
            city.StoredFood = 10;
            var transfer = TransferCommand(state, supplier, receiver, 4);
            var load = LoadCommand(state, city, supplier, 5);

            var resolution = new TurnProcessor().Resolve(state, new[] { transfer, load });

            Assert.That(supplier.CarriedFood, Is.EqualTo(1));
            Assert.That(receiver.CarriedFood, Is.EqualTo(4));
            Assert.That(resolution.Events.Any(item => item.Type == GameEventType.UnitFoodTransferred &&
                item.SourceId == supplier.Id && item.TargetId == receiver.Id && item.PrimaryValue == 4), Is.True);
        }

        [Test]
        public void GroundFoodIsConsumedBeforeOccupiedAgricultureAndPersonalFood()
        {
            var fixture = OccupiedAgricultureFixture(6007);
            fixture.Unit.CarriedFood = 3;
            fixture.Tile.GroundFood = 1;
            fixture.Tile.GroundFoodOwnerId = fixture.Unit.OwnerId;

            var result = UnitFoodResolver.Consume(fixture.State);

            Assert.That(result.Records.Single(item => item.UnitId == fixture.Unit.Id).Source,
                Is.EqualTo(UnitFoodSource.Ground));
            Assert.That(fixture.Tile.GroundFood, Is.Zero);
            Assert.That(fixture.Unit.CarriedFood, Is.EqualTo(3));
        }

        [Test]
        public void OccupiedAgricultureSuppliesEveryOccupyingUnitWithoutUsingPersonalFood()
        {
            var fixture = OccupiedAgricultureFixture(6008);
            fixture.Unit.CarriedFood = 2;
            var second = AddUnit(fixture.State, fixture.OccupierCity, fixture.Tile.Id, UnitType.Militia, 2);

            var result = UnitFoodResolver.Consume(fixture.State);

            Assert.That(result.Records.Count(item => item.Source == UnitFoodSource.OccupiedAgriculture),
                Is.EqualTo(2));
            Assert.That(fixture.Unit.CarriedFood, Is.EqualTo(2));
            Assert.That(second.CarriedFood, Is.EqualTo(2));
        }

        [Test]
        public void AgricultureDoesNotSupplyOccupierFromAnotherTileAndOriginalOwnerUsesHomeSupply()
        {
            var fixture = OccupiedAgricultureFixture(6009);
            fixture.Unit.TileId = fixture.State.Tiles.First(item =>
                item.CityId == fixture.VictimCity.Id && item.Id != fixture.Tile.Id).Id;
            fixture.Unit.CarriedFood = 1;
            var originalOwner = AddUnit(
                fixture.State, fixture.VictimCity, fixture.Unit.TileId, UnitType.Militia, 1);
            CityEconomyResolver.ResolveProduction(fixture.State);

            var result = UnitFoodResolver.Consume(fixture.State);

            Assert.That(result.Records.Single(item => item.UnitId == fixture.Unit.Id).Source,
                Is.EqualTo(UnitFoodSource.Personal));
            Assert.That(result.Records.Single(item => item.UnitId == originalOwner.Id).Source,
                Is.EqualTo(UnitFoodSource.HomeTerritory));
        }

        [Test]
        public void UnownedSharedGroundFoodIsClaimedByFirstUnitInDeterministicIdOrder()
        {
            var state = PrototypeMatchFactory.Create(6010);
            var first = state.Units[0];
            var second = state.Units[1];
            second.OwnerId = state.Players.First(item => item.Id != first.OwnerId).Id;
            second.TileId = first.TileId;
            var tile = state.Tiles.First(item => item.Id == first.TileId);
            tile.IsSharedBoundary = true;
            tile.GroundFood = 2;
            tile.GroundFoodOwnerId = default;
            first.CarriedFood = 0;
            second.CarriedFood = 0;

            var result = UnitFoodResolver.Consume(state);

            var winner = first.Id.CompareTo(second.Id) < 0 ? first : second;
            Assert.That(tile.GroundFood, Is.EqualTo(1));
            Assert.That(tile.GroundFoodOwnerId, Is.EqualTo(winner.OwnerId));
            Assert.That(result.Records.Count(item => item.Source == UnitFoodSource.Ground), Is.EqualTo(1));
        }

        private static GameCommand LoadCommand(GameState state, CityState city, UnitState unit, int amount)
        {
            return new GameCommand
            {
                CommandId = state.AllocateId(), PlayerId = unit.OwnerId,
                TurnNumber = state.TurnNumber, Type = GameCommandType.LoadFood,
                SubjectId = unit.Id, TargetId = city.Id, PrimaryValue = amount
            };
        }

        private static GameCommand TransferCommand(
            GameState state, UnitState supplier, UnitState receiver, int amount)
        {
            return new GameCommand
            {
                CommandId = state.AllocateId(), PlayerId = supplier.OwnerId,
                TurnNumber = state.TurnNumber, Type = GameCommandType.TransferFood,
                SubjectId = supplier.Id, TargetId = receiver.Id, PrimaryValue = amount
            };
        }

        private static UnitState AddUnit(
            GameState state, CityState city, EntityId tileId, UnitType type, int food)
        {
            var unit = new UnitState
            {
                Id = state.AllocateId(), OwnerId = city.OwnerId, TileId = tileId,
                Type = type, HitPoints = UnitRules.MaximumHitPoints(type), CarriedFood = food
            };
            state.Units.Add(unit);
            return unit;
        }

        private static AgricultureFixture OccupiedAgricultureFixture(long seed)
        {
            var state = PrototypeMatchFactory.Create(seed);
            var victim = state.Cities[0];
            var occupier = state.Cities[1];
            var tile = state.Tiles.First(item => item.CityId == victim.Id &&
                !state.Districts.Any(district => district.TileId == item.Id));
            var district = new DistrictState
            {
                Id = state.AllocateId(), CityId = victim.Id, TileId = tile.Id,
                Type = DistrictType.Agriculture, ControllerId = occupier.OwnerId,
                IsOperational = false, AssignedCitizens = 1
            };
            state.Districts.Add(district);
            tile.ControllerId = occupier.OwnerId;
            var unit = AddUnit(state, occupier, tile.Id, UnitType.Militia, 1);
            return new AgricultureFixture
            {
                State = state, VictimCity = victim, OccupierCity = occupier,
                Tile = tile, Unit = unit
            };
        }

        private sealed class AgricultureFixture
        {
            public GameState State;
            public CityState VictimCity;
            public CityState OccupierCity;
            public TileState Tile;
            public UnitState Unit;
        }
    }
}
