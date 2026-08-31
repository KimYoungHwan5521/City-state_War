using System.Linq;
using LittleCiv.Core;
using NUnit.Framework;

namespace LittleCiv.Tests
{
    public sealed class CityStorageTests
    {
        [Test]
        public void GovernmentProductionExactlyFeedsStartingPopulation()
        {
            var state = PrototypeMatchFactory.Create(4500);
            var city = state.Cities[0];

            new TurnProcessor().Resolve(state, new GameCommand[0]);

            Assert.That(city.LastFoodProduction, Is.EqualTo(4));
            Assert.That(city.StoredFood, Is.Zero);
            Assert.That(city.Gold, Is.EqualTo(11));
        }

        [Test]
        public void AgricultureSurplusAndCommerceIncomeAccumulateInCityStorage()
        {
            var state = PrototypeMatchFactory.Create(4501);
            var city = state.Cities[0];
            var foodTile = ResourceTile(state, city, TileResourceType.Food);
            var commerceTile = ResourceTile(state, city, TileResourceType.Commerce);
            AddOperationalDistrict(state, city, foodTile, DistrictType.Agriculture);
            AddOperationalDistrict(state, city, commerceTile, DistrictType.Commerce);

            new TurnProcessor().Resolve(state, new GameCommand[0]);

            Assert.That(city.LastFoodProduction, Is.EqualTo(8));
            Assert.That(city.StoredFood, Is.EqualTo(4));
            Assert.That(city.LastGoldProduction, Is.EqualTo(6));
            Assert.That(city.Gold, Is.EqualTo(15));
        }

        [Test]
        public void FoodDeficitConsumesExistingStorageWithoutGoingNegative()
        {
            var state = PrototypeMatchFactory.Create(4502);
            var city = state.Cities[0];
            city.StoredFood = 3;
            state.Districts.Single(item => item.CityId == city.Id &&
                                           item.Type == DistrictType.Government).IsOperational = false;

            new TurnProcessor().Resolve(state, new GameCommand[0]);

            Assert.That(city.LastFoodProduction, Is.Zero);
            Assert.That(city.StoredFood, Is.Zero);
        }

        private static TileState ResourceTile(GameState state, CityState city, TileResourceType resource)
        {
            return state.MapTopology.FindView(city.Id).Tiles
                .Where(item => item.IsBuildable)
                .Select(item => item.TileId)
                .Distinct()
                .Select(tileId => state.Tiles.Single(tile => tile.Id == tileId))
                .First(item => item.CityId == city.Id && item.ResourceType == resource);
        }

        private static void AddOperationalDistrict(
            GameState state,
            CityState city,
            TileState tile,
            DistrictType type)
        {
            state.Districts.Add(new DistrictState
            {
                Id = state.AllocateId(),
                CityId = city.Id,
                TileId = tile.Id,
                Type = type,
                ControllerId = city.OwnerId,
                IsOperational = true,
                AssignedCitizens = 1
            });
        }
    }
}
