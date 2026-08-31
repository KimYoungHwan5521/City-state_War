using System.Linq;
using LittleCiv.Core;
using NUnit.Framework;

namespace LittleCiv.Tests
{
    public sealed class CityPopulationTests
    {
        [Test]
        public void PositiveFoodSurplusAdvancesGrowthAndCreatesUnassignedCitizenAtThreshold()
        {
            var state = PrototypeMatchFactory.Create(4600);
            var city = state.Cities[0];
            city.GrowthProgress = 10;
            AddOperationalAgriculture(state, city);

            var result = new TurnProcessor().Resolve(state, new GameCommand[0]);

            Assert.That(city.LastFoodProduction, Is.EqualTo(6));
            Assert.That(city.Population, Is.EqualTo(5));
            Assert.That(city.GrowthProgress, Is.Zero);
            Assert.That(city.GovernmentCitizens, Is.EqualTo(1));
            Assert.That(result.Events.Any(item =>
                item.Type == GameEventType.PopulationIncreased &&
                item.TargetId == city.Id &&
                item.PrimaryValue == 5), Is.True);
        }

        [Test]
        public void GrowthIsLimitedToOnePopulationPerTurnAndPreservesOverflow()
        {
            var state = PrototypeMatchFactory.Create(4601);
            var city = state.Cities[0];
            city.GrowthProgress = 30;

            CityEconomyResolver.ResolveProduction(state);
            CityPopulationResolver.ResolveGrowth(state);

            Assert.That(city.Population, Is.EqualTo(4));
            Assert.That(city.GrowthProgress, Is.EqualTo(30));

            city.LastFoodProduction = 20;
            CityPopulationResolver.ResolveGrowth(state);

            Assert.That(city.Population, Is.EqualTo(5));
            Assert.That(city.GrowthProgress, Is.EqualTo(34));
        }

        [Test]
        public void StoredFoodAmountDoesNotChangeGrowthRate()
        {
            var first = PrototypeMatchFactory.Create(4602);
            var second = GameStateCopy.Clone(first);
            first.Cities[0].StoredFood = 0;
            second.Cities[0].StoredFood = 100;

            new TurnProcessor().Resolve(first, new GameCommand[0]);
            new TurnProcessor().Resolve(second, new GameCommand[0]);

            Assert.That(second.Cities[0].GrowthProgress, Is.EqualTo(first.Cities[0].GrowthProgress));
            Assert.That(second.Cities[0].Population, Is.EqualTo(first.Cities[0].Population));
        }

        [Test]
        public void FoodDeficitAccumulatesFamineAndReducesAtMostOnePopulationPerTurn()
        {
            var state = PrototypeMatchFactory.Create(4603);
            var city = state.Cities[0];
            city.FamineProgress = 20;
            DisableGovernment(state, city);

            var result = new TurnProcessor().Resolve(state, new GameCommand[0]);

            Assert.That(city.Population, Is.EqualTo(3));
            Assert.That(city.FamineProgress, Is.EqualTo(20));
            Assert.That(result.Events.Any(item =>
                item.Type == GameEventType.PopulationDecreased &&
                item.TargetId == city.Id &&
                item.PrimaryValue == 3), Is.True);
        }

        [Test]
        public void FamineUsesFoodProductionDeficitEvenWhenStoredFoodExists()
        {
            var state = PrototypeMatchFactory.Create(4604);
            var city = state.Cities[0];
            city.StoredFood = 100;
            DisableGovernment(state, city);

            new TurnProcessor().Resolve(state, new GameCommand[0]);

            Assert.That(city.Population, Is.EqualTo(3));
            Assert.That(city.StoredFood, Is.EqualTo(96));
        }

        [Test]
        public void FamineCannotReduceCityBelowOnePopulation()
        {
            var state = PrototypeMatchFactory.Create(4605);
            var city = state.Cities[0];
            city.Population = 1;
            DisableGovernment(state, city);

            new TurnProcessor().Resolve(state, new GameCommand[0]);

            Assert.That(city.Population, Is.EqualTo(1));
            Assert.That(city.FamineProgress, Is.EqualTo(1));
        }

        private static void AddOperationalAgriculture(GameState state, CityState city)
        {
            var tile = state.Tiles.First(item =>
                item.CityId == city.Id &&
                item.ResourceType == TileResourceType.None &&
                item.Q != 0 && item.R != 0);
            state.Districts.Add(new DistrictState
            {
                Id = state.AllocateId(),
                CityId = city.Id,
                TileId = tile.Id,
                Type = DistrictType.Agriculture,
                ControllerId = city.OwnerId,
                IsOperational = true,
                AssignedCitizens = 1
            });
        }

        private static void DisableGovernment(GameState state, CityState city)
        {
            state.Districts.Single(item =>
                item.CityId == city.Id && item.Type == DistrictType.Government).IsOperational = false;
        }
    }
}
