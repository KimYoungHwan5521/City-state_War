using System.Linq;
using LittleCiv.Core;
using NUnit.Framework;

namespace LittleCiv.Tests
{
    public sealed class CityEconomyBreakdownTests
    {
        [Test]
        public void StartingCityBreakdownExplainsGovernmentConsumptionAndMilitiaUpkeep()
        {
            var state = PrototypeMatchFactory.Create(4900);
            var city = state.Cities[0];

            var result = CityEconomyResolver.CalculateBreakdown(state, city);

            Assert.That(result.Food.Government, Is.EqualTo(6));
            Assert.That(result.Food.Total, Is.EqualTo(6));
            Assert.That(result.PopulationConsumption, Is.EqualTo(4));
            Assert.That(result.UnitFoodConsumption, Is.EqualTo(1));
            Assert.That(result.FoodNet, Is.EqualTo(1));
            Assert.That(result.Gold.Total, Is.EqualTo(2));
            Assert.That(result.UnitUpkeep, Is.EqualTo(1));
            Assert.That(result.GrowthRequired, Is.EqualTo(12));
            Assert.That(result.FamineRequired, Is.EqualTo(4));
        }

        [Test]
        public void DistrictBreakdownSeparatesBaseResourceAndAdjacencyBonuses()
        {
            var state = PrototypeMatchFactory.Create(4901);
            var city = state.Cities[0];
            var foodTile = state.Tiles.First(item =>
                item.CityId == city.Id && item.ResourceType == TileResourceType.Food);
            AddDistrict(state, city, foodTile, DistrictType.Agriculture);
            var firstCommerce = TileAt(state, city, 1, 0);
            var secondCommerce = TileAt(state, city, 1, -1);
            firstCommerce.ResourceType = TileResourceType.None;
            secondCommerce.ResourceType = TileResourceType.None;
            AddDistrict(state, city, firstCommerce, DistrictType.Commerce);
            AddDistrict(state, city, secondCommerce, DistrictType.Commerce);

            var result = CityEconomyResolver.CalculateBreakdown(state, city);

            Assert.That(result.Food.DistrictBase, Is.EqualTo(2));
            Assert.That(result.Food.ResourceBonus, Is.EqualTo(2));
            Assert.That(result.Gold.DistrictBase, Is.EqualTo(4));
            Assert.That(result.Gold.ResourceBonus, Is.Zero);
            Assert.That(result.Gold.AdjacencyBonus, Is.EqualTo(2));
            Assert.That(result.Gold.Total, Is.EqualTo(8));
        }

        private static TileState TileAt(GameState state, CityState city, int q, int r)
        {
            var placement = state.MapTopology.FindView(city.Id).Tiles.Single(item =>
                item.LocalQ == q && item.LocalR == r);
            return state.Tiles.Single(item => item.Id == placement.TileId);
        }

        private static void AddDistrict(GameState state, CityState city, TileState tile, DistrictType type)
        {
            state.Districts.Add(new DistrictState
            {
                Id = state.AllocateId(), CityId = city.Id, TileId = tile.Id, Type = type,
                ControllerId = city.OwnerId, IsOperational = true, AssignedCitizens = 1
            });
        }
    }
}
