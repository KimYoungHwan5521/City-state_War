using System.Linq;
using LittleCiv.Core;
using NUnit.Framework;

namespace LittleCiv.Tests
{
    public sealed class CityEconomyFoundationTests
    {
        [Test]
        public void PrototypeMatch_EveryCityStartsWithConfiguredPopulationAndResources()
        {
            var state = PrototypeMatchFactory.Create(4100);

            foreach (var city in state.Cities)
            {
                Assert.That(city.Population, Is.EqualTo(4));
                Assert.That(city.GovernmentCitizens, Is.EqualTo(1));
                Assert.That(city.Gold, Is.EqualTo(10));
                Assert.That(city.StoredFood, Is.Zero);
                Assert.That(city.GrowthProgress, Is.Zero);
                Assert.That(city.FamineProgress, Is.Zero);
            }
        }

        [Test]
        public void PrototypeMatch_EveryCityHasSixResourcesAtConfiguredDistances()
        {
            var state = PrototypeMatchFactory.Create(4101);

            foreach (var city in state.Cities)
            {
                var view = state.MapTopology.FindView(city.Id);
                var resourceTiles = view.Tiles
                    .Where(placement => placement.IsBuildable)
                    .Select(placement => new
                    {
                        Placement = placement,
                        Tile = state.Tiles.Single(tile => tile.Id == placement.TileId)
                    })
                    .Where(item => item.Tile.ResourceType != TileResourceType.None)
                    .ToList();

                Assert.That(resourceTiles.Count, Is.EqualTo(6));
                Assert.That(resourceTiles.Count(item => item.Tile.ResourceType == TileResourceType.Food),
                    Is.EqualTo(3));
                Assert.That(resourceTiles.Count(item => item.Tile.ResourceType == TileResourceType.Commerce),
                    Is.EqualTo(1));
                Assert.That(resourceTiles.Count(item => item.Tile.ResourceType == TileResourceType.Science),
                    Is.EqualTo(1));
                Assert.That(resourceTiles.Count(item => item.Tile.ResourceType == TileResourceType.Culture),
                    Is.EqualTo(1));

                foreach (var item in resourceTiles)
                {
                    var distance = HexCoord.Distance(new HexCoord(0, 0),
                        new HexCoord(item.Placement.LocalQ, item.Placement.LocalR));
                    Assert.That(distance, Is.InRange(1, item.Tile.ResourceType == TileResourceType.Food ? 3 : 2));
                }
            }
        }

        [Test]
        public void ResourcePlacement_SameSeedIsDeterministicAndDifferentSeedsVary()
        {
            var first = PrototypeMatchFactory.Create(4102);
            var second = PrototypeMatchFactory.Create(4102);
            var different = PrototypeMatchFactory.Create(4103);

            Assert.That(ResourceSignature(second), Is.EqualTo(ResourceSignature(first)));
            Assert.That(ResourceSignature(different), Is.Not.EqualTo(ResourceSignature(first)));
        }

        [Test]
        public void ResourcePlacement_SurvivesCopyAndJsonRoundTripInStateHash()
        {
            var state = PrototypeMatchFactory.Create(4104);
            var copy = GameStateCopy.Clone(state);
            var json = LittleCiv.Runtime.GameStateJsonSerializer.Serialize(state);
            var restored = LittleCiv.Runtime.GameStateJsonSerializer.Deserialize(json);

            Assert.That(GameStateHasher.Compute(copy), Is.EqualTo(GameStateHasher.Compute(state)));
            Assert.That(GameStateHasher.Compute(restored), Is.EqualTo(GameStateHasher.Compute(state)));
        }

        [Test]
        public void CityProduction_OperationalGovernmentProducesConfiguredYields()
        {
            var state = PrototypeMatchFactory.Create(4105);
            var city = state.Cities[0];

            new TurnProcessor().Resolve(state, new GameCommand[0]);

            Assert.That(city.LastFoodProduction, Is.EqualTo(4));
            Assert.That(city.LastGoldProduction, Is.EqualTo(2));
            Assert.That(city.LastScienceProduction, Is.EqualTo(1));
            Assert.That(city.LastCultureProduction, Is.EqualTo(1));
            Assert.That(city.Gold, Is.EqualTo(11));
            Assert.That(city.ResearchPoints, Is.EqualTo(1));
            Assert.That(city.StoredFood, Is.Zero);
        }

        [Test]
        public void CityProduction_InoperativeGovernmentProducesNothing()
        {
            var state = PrototypeMatchFactory.Create(4106);
            var city = state.Cities[0];
            state.Districts.Single(district => district.CityId == city.Id).IsOperational = false;

            CityEconomyResolver.ResolveProduction(state);

            Assert.That(city.LastFoodProduction, Is.Zero);
            Assert.That(city.LastGoldProduction, Is.Zero);
            Assert.That(city.LastScienceProduction, Is.Zero);
            Assert.That(city.LastCultureProduction, Is.Zero);
            Assert.That(city.Gold, Is.EqualTo(10));
            Assert.That(city.ResearchPoints, Is.Zero);
        }

        private static string ResourceSignature(GameState state)
        {
            return string.Join("|", state.Cities.OrderBy(city => city.Id.Value).SelectMany(city =>
                state.MapTopology.FindView(city.Id).Tiles
                    .Where(placement => placement.IsBuildable)
                    .OrderBy(placement => placement.LocalQ)
                    .ThenBy(placement => placement.LocalR)
                    .Select(placement =>
                    {
                        var tile = state.Tiles.Single(item => item.Id == placement.TileId);
                        return city.Name + ":" + placement.LocalQ + "," + placement.LocalR + "=" +
                               (int)tile.ResourceType;
                    })));
        }
    }
}
