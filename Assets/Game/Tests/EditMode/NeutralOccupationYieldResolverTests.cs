using System.Linq;
using LittleCiv.Core;
using NUnit.Framework;

namespace LittleCiv.Tests
{
    public sealed class NeutralOccupationYieldResolverTests
    {
        [TestCase(NeutralCitySpecialization.Science, TileResourceType.Science)]
        [TestCase(NeutralCitySpecialization.Culture, TileResourceType.Culture)]
        [TestCase(NeutralCitySpecialization.Commerce, TileResourceType.Commerce)]
        public void OccupiedEconomicCityProvidesTwoFourSixByDevelopmentStage(
            NeutralCitySpecialization specialization, TileResourceType resource)
        {
            var fixture = Create(14000 + (int)specialization, specialization);
            AddSpecializationDistricts(fixture, 2);

            var record = NeutralOccupationYieldResolver.Collect(fixture.State).Single();

            Assert.That(record.ResourceType, Is.EqualTo(resource));
            Assert.That(record.Amount, Is.EqualTo(4));
            if (resource == TileResourceType.Science)
                Assert.That(fixture.Home.ResearchPoints, Is.EqualTo(4));
            if (resource == TileResourceType.Culture)
                Assert.That(fixture.Home.LastCultureProduction, Is.EqualTo(4));
            if (resource == TileResourceType.Commerce)
                Assert.That(fixture.Home.Gold, Is.EqualTo(14));
        }

        [Test]
        public void OccupiedMilitaryCityHasNoPeriodicResourceCollection()
        {
            var fixture = Create(14010, NeutralCitySpecialization.Military);

            Assert.That(NeutralOccupationYieldResolver.Collect(fixture.State), Is.Empty);
        }

        [Test]
        public void OccupierUsesNeutralCityFoodBeforePersonalRationsWithoutDoubleSpending()
        {
            var fixture = Create(14011, NeutralCitySpecialization.Science);
            fixture.City.StoredFood = 10;
            fixture.City.LastFoodProduction = 0;
            var government = fixture.State.Districts.Single(item => item.CityId == fixture.City.Id &&
                item.Type == DistrictType.Government);
            var unit = new UnitState
            {
                Id = fixture.State.AllocateId(), OwnerId = fixture.Player.Id,
                HomeCityId = fixture.Home.Id, TileId = government.TileId,
                Type = UnitType.Militia, HitPoints = 16, CarriedFood = 5,
                CreatedTurn = fixture.State.TurnNumber - 1
            };
            fixture.State.Units.Add(unit);

            var consumption = UnitFoodResolver.Consume(fixture.State);
            var record = consumption.Records.Single(item => item.UnitId == unit.Id);

            Assert.That(record.Source, Is.EqualTo(UnitFoodSource.OccupiedNeutralCity));
            Assert.That(unit.CarriedFood, Is.EqualTo(5));
            Assert.That(fixture.City.StoredFood, Is.EqualTo(10));
            Assert.That(fixture.City.LastUnitFoodConsumption, Is.EqualTo(1));
            CityFoodResolver.ResolveStorage(fixture.State);
            Assert.That(fixture.City.StoredFood, Is.EqualTo(5));
        }

        private static Fixture Create(long seed, NeutralCitySpecialization specialization)
        {
            var state = PrototypeMatchFactory.Create(seed);
            var player = state.Players.Single(item => item.Slot == PlayerSlot.PlayerOne);
            var home = state.Cities.Single(item => item.OwnerId == player.Id);
            var neutral = state.Players.Single(item => item.Slot == PlayerSlot.Neutral);
            var city = state.Cities.First(item => item.OwnerId == neutral.Id &&
                item.NeutralSpecialization == specialization);
            city.OccupyingPlayerId = player.Id;
            var government = state.Districts.Single(item => item.CityId == city.Id &&
                item.Type == DistrictType.Government);
            government.ControllerId = player.Id;
            state.Tiles.Single(item => item.Id == government.TileId).ControllerId = player.Id;
            state.Units.RemoveAll(item => item.HomeCityId == city.Id);
            return new Fixture { State = state, Player = player, Home = home, City = city };
        }

        private static void AddSpecializationDistricts(Fixture fixture, int count)
        {
            var type = NeutralCityRules.DistrictTypeFor(fixture.City.NeutralSpecialization);
            for (var index = 0; index < count; index++)
            {
                var tile = fixture.State.MapTopology.FindView(fixture.City.Id).Tiles.First(item =>
                    item.IsBuildable && fixture.State.Districts.All(district => district.TileId != item.TileId));
                fixture.State.Districts.Add(new DistrictState
                {
                    Id = fixture.State.AllocateId(), CityId = fixture.City.Id, TileId = tile.TileId,
                    Type = type, ControllerId = fixture.City.OwnerId,
                    IsOperational = true, AssignedCitizens = 1
                });
            }
        }

        private sealed class Fixture
        {
            public GameState State;
            public PlayerState Player;
            public CityState Home;
            public CityState City;
        }
    }
}
