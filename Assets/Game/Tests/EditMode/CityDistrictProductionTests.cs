using System.Linq;
using LittleCiv.Core;
using NUnit.Framework;

namespace LittleCiv.Tests
{
    public sealed class CityDistrictProductionTests
    {
        [TestCase(DistrictType.Agriculture, TileResourceType.None, 6, 2, 1, 1)]
        [TestCase(DistrictType.Agriculture, TileResourceType.Food, 8, 2, 1, 1)]
        [TestCase(DistrictType.Commerce, TileResourceType.None, 4, 4, 1, 1)]
        [TestCase(DistrictType.Commerce, TileResourceType.Commerce, 4, 6, 1, 1)]
        [TestCase(DistrictType.Science, TileResourceType.None, 4, 2, 3, 1)]
        [TestCase(DistrictType.Science, TileResourceType.Science, 4, 2, 5, 1)]
        [TestCase(DistrictType.Culture, TileResourceType.None, 4, 2, 1, 3)]
        [TestCase(DistrictType.Culture, TileResourceType.Culture, 4, 2, 1, 4)]
        [TestCase(DistrictType.Military, TileResourceType.Food, 4, 2, 1, 1)]
        public void OperationalDistrict_ProducesConfiguredBaseAndResourceYield(
            DistrictType type,
            TileResourceType resource,
            int expectedFood,
            int expectedGold,
            int expectedScience,
            int expectedCulture)
        {
            var state = PrototypeMatchFactory.Create(4300 + (int)type * 10 + (int)resource);
            var city = state.Cities[0];
            var tile = FirstEmptyTile(state, city);
            tile.ResourceType = resource;
            AddDistrict(state, city, tile, type, assignedCitizens: 1, operational: true);

            CityEconomyResolver.ResolveProduction(state);

            Assert.That(city.LastFoodProduction, Is.EqualTo(expectedFood));
            Assert.That(city.LastGoldProduction, Is.EqualTo(expectedGold));
            Assert.That(city.LastScienceProduction, Is.EqualTo(expectedScience));
            Assert.That(city.LastCultureProduction, Is.EqualTo(expectedCulture));
            Assert.That(city.Gold, Is.EqualTo(10 + expectedGold));
            Assert.That(city.ResearchPoints, Is.EqualTo(expectedScience));
        }

        [Test]
        public void UnmannedOccupiedAndUnderConstructionDistricts_ProduceNothing()
        {
            var state = PrototypeMatchFactory.Create(4399);
            var city = state.Cities[0];
            var tiles = EmptyTiles(state, city).Take(3).ToArray();
            AddDistrict(state, city, tiles[0], DistrictType.Commerce, assignedCitizens: 0, operational: true);
            var occupied = AddDistrict(
                state, city, tiles[1], DistrictType.Science, assignedCitizens: 1, operational: true);
            occupied.ControllerId = state.Players[1].Id;
            var building = AddDistrict(
                state, city, tiles[2], DistrictType.Culture, assignedCitizens: 1, operational: false);
            building.RemainingConstructionTurns = 1;

            CityEconomyResolver.ResolveProduction(state);

            Assert.That(city.LastFoodProduction, Is.EqualTo(4));
            Assert.That(city.LastGoldProduction, Is.EqualTo(2));
            Assert.That(city.LastScienceProduction, Is.EqualTo(1));
            Assert.That(city.LastCultureProduction, Is.EqualTo(1));
        }

        [TestCase(DistrictType.Commerce, 12, 1, 1)]
        [TestCase(DistrictType.Science, 2, 11, 1)]
        [TestCase(DistrictType.Culture, 2, 1, 11)]
        public void ThreeSameSpecialistDistricts_ReceiveHexAdjacencyBonus(
            DistrictType type,
            int expectedGold,
            int expectedScience,
            int expectedCulture)
        {
            var state = PrototypeMatchFactory.Create(4400 + (int)type);
            var city = state.Cities[0];
            AddDistrict(state, city, TileAt(state, city, 1, 0), type, 1, true);
            AddDistrict(state, city, TileAt(state, city, 2, 0), type, 1, true);
            AddDistrict(state, city, TileAt(state, city, 1, -1), type, 1, true);

            CityEconomyResolver.ResolveProduction(state);

            Assert.That(city.LastGoldProduction, Is.EqualTo(expectedGold));
            Assert.That(city.LastScienceProduction, Is.EqualTo(expectedScience));
            Assert.That(city.LastCultureProduction, Is.EqualTo(expectedCulture));
        }

        [Test]
        public void OccupiedSameTypeDistrict_NeitherProducesNorProvidesAdjacency()
        {
            var state = PrototypeMatchFactory.Create(4499);
            var city = state.Cities[0];
            AddDistrict(state, city, TileAt(state, city, 1, 0), DistrictType.Commerce, 1, true);
            AddDistrict(state, city, TileAt(state, city, 2, 0), DistrictType.Commerce, 1, true);
            var occupied = AddDistrict(
                state, city, TileAt(state, city, 1, -1), DistrictType.Commerce, 1, true);
            occupied.ControllerId = state.Players[1].Id;

            CityEconomyResolver.ResolveProduction(state);

            Assert.That(city.LastGoldProduction, Is.EqualTo(8));
        }

        private static DistrictState AddDistrict(
            GameState state,
            CityState city,
            TileState tile,
            DistrictType type,
            int assignedCitizens,
            bool operational)
        {
            var district = new DistrictState
            {
                Id = state.AllocateId(),
                CityId = city.Id,
                TileId = tile.Id,
                Type = type,
                ControllerId = city.OwnerId,
                AssignedCitizens = assignedCitizens,
                IsOperational = operational
            };
            state.Districts.Add(district);
            return district;
        }

        private static TileState FirstEmptyTile(GameState state, CityState city) => EmptyTiles(state, city).First();

        private static TileState TileAt(GameState state, CityState city, int q, int r)
        {
            var placement = state.MapTopology.FindView(city.Id).Tiles.Single(item =>
                item.IsBuildable && item.LocalQ == q && item.LocalR == r);
            var tile = state.Tiles.Single(item => item.Id == placement.TileId);
            tile.ResourceType = TileResourceType.None;
            return tile;
        }

        private static System.Collections.Generic.IEnumerable<TileState> EmptyTiles(GameState state, CityState city)
        {
            var occupiedTileIds = state.Districts.Select(item => item.TileId).ToArray();
            return state.MapTopology.FindView(city.Id).Tiles
                .Where(item => item.IsBuildable && !occupiedTileIds.Contains(item.TileId))
                .OrderBy(item => item.LocalQ)
                .ThenBy(item => item.LocalR)
                .Select(item => state.Tiles.Single(tile => tile.Id == item.TileId));
        }
    }
}
