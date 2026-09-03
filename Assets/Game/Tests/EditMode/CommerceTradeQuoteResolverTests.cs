using System.Linq;
using LittleCiv.Core;
using NUnit.Framework;

namespace LittleCiv.Tests
{
    public sealed class CommerceTradeQuoteResolverTests
    {
        [TestCase(-3, 3, 1, 2)]
        [TestCase(-2, 2, 1, 1)]
        [TestCase(0, 2, 1, 1)]
        [TestCase(2, 2, 1, 1)]
        [TestCase(3, 1, 1, 1)]
        [TestCase(4, 1, 2, 1)]
        public void EarlyCommerceCityUsesFavorTable(
            int favor, int resource, int gold, int shippingRate)
        {
            var fixture = Create(13700 + favor, "N4");
            fixture.Source.StoredFood = 100;
            if (favor == 4) fixture.Target.CultureSubjectToId = fixture.Player.Id;
            NeutralCityRules.SetFavor(fixture.Target, fixture.Player.Id, favor);

            var quote = CommerceTradeQuoteResolver.Quote(fixture.State, fixture.Player.Id,
                fixture.Source.Id, fixture.Target.Id, TileResourceType.Food);

            Assert.That(quote.IsAvailable, Is.True);
            Assert.That(quote.RequiredResourceAmount,
                Is.EqualTo(resource + quote.Route.AdditionalDistance * shippingRate));
            Assert.That(quote.BaseGoldPayment, Is.EqualTo(gold));
            Assert.That(quote.ShippingResourcePerDistance, Is.EqualTo(shippingRate));
            Assert.That(quote.NetGoldPayment, Is.EqualTo(gold));
        }

        [Test]
        public void DistanceRaisesRequiredResourceInsteadOfReducingGoldPayment()
        {
            var fixture = Create(13710, "N5");
            fixture.Source.StoredFood = 100;

            var quote = CommerceTradeQuoteResolver.Quote(fixture.State, fixture.Player.Id,
                fixture.Source.Id, fixture.Target.Id, TileResourceType.Food);

            Assert.That(quote.Route.AdditionalDistance, Is.GreaterThan(0));
            Assert.That(quote.IsAvailable, Is.True);
            Assert.That(quote.NetGoldPayment, Is.EqualTo(quote.BaseGoldPayment));
            Assert.That(quote.RequiredResourceAmount,
                Is.EqualTo(2 + quote.Route.AdditionalDistance * quote.ShippingResourcePerDistance));
        }

        [Test]
        public void DistanceThreeCommerceTradeExchangesFourFoodForOneGold()
        {
            var fixture = Create(13713, "N5");
            fixture.Source.StoredFood = 100;

            var quote = CommerceTradeQuoteResolver.Quote(fixture.State, fixture.Player.Id,
                fixture.Source.Id, fixture.Target.Id, TileResourceType.Food);

            Assert.That(quote.Route.Distance, Is.EqualTo(3));
            Assert.That(quote.IsAvailable, Is.True);
            Assert.That(quote.RequiredResourceAmount, Is.EqualTo(4));
            Assert.That(quote.NetGoldPayment, Is.EqualTo(1));
        }

        [Test]
        public void FoodRequiresCurrentStockButScienceUsesProjectedProduction()
        {
            var fixture = Create(13711, "N4");
            fixture.Source.StoredFood = 0;

            var food = CommerceTradeQuoteResolver.Quote(fixture.State, fixture.Player.Id,
                fixture.Source.Id, fixture.Target.Id, TileResourceType.Food);
            var science = CommerceTradeQuoteResolver.Quote(fixture.State, fixture.Player.Id,
                fixture.Source.Id, fixture.Target.Id, TileResourceType.Science);

            Assert.That(food.Failure, Is.EqualTo(CommerceTradeQuoteFailure.InsufficientFood));
            Assert.That(science.IsAvailable, Is.True);
            Assert.That(science.AvailableResourceAmount, Is.EqualTo(1));
            Assert.That(science.RequiredResourceAmount,
                Is.EqualTo(2 + science.Route.AdditionalDistance * science.ShippingResourcePerDistance));
        }

        [Test]
        public void MiddleCulturalSubjectOfferScalesRequiredResourceAndGold()
        {
            var fixture = Create(13712, "N4");
            AddCommerceDistricts(fixture, 2);
            fixture.Source.StoredFood = 100;
            fixture.Target.CultureSubjectToId = fixture.Player.Id;
            NeutralCityRules.SetFavor(fixture.Target, fixture.Player.Id, 4);

            var quote = CommerceTradeQuoteResolver.Quote(fixture.State, fixture.Player.Id,
                fixture.Source.Id, fixture.Target.Id, TileResourceType.Food);

            Assert.That(quote.DevelopmentStage, Is.EqualTo(NeutralDevelopmentStage.Middle));
            Assert.That(quote.RequiredResourceAmount,
                Is.EqualTo(2 + quote.Route.AdditionalDistance * quote.ShippingResourcePerDistance));
            Assert.That(quote.BaseGoldPayment, Is.EqualTo(4));
            Assert.That(quote.NetGoldPayment, Is.EqualTo(4));
        }

        private static Fixture Create(long seed, string targetName)
        {
            var state = PrototypeMatchFactory.Create(seed);
            var player = state.Players.Single(item => item.Slot == PlayerSlot.PlayerOne);
            return new Fixture
            {
                State = state, Player = player,
                Source = state.Cities.Single(item => item.OwnerId == player.Id),
                Target = state.Cities.Single(item => item.Name == targetName)
            };
        }

        private static void AddCommerceDistricts(Fixture fixture, int count)
        {
            for (var index = 0; index < count; index++)
            {
                var tile = fixture.State.MapTopology.FindView(fixture.Target.Id).Tiles.First(item =>
                    item.IsBuildable && fixture.State.Districts.All(district => district.TileId != item.TileId));
                fixture.State.Districts.Add(new DistrictState
                {
                    Id = fixture.State.AllocateId(), CityId = fixture.Target.Id, TileId = tile.TileId,
                    Type = DistrictType.Commerce, ControllerId = fixture.Target.OwnerId,
                    IsOperational = true, AssignedCitizens = 1
                });
            }
        }

        private sealed class Fixture
        {
            public GameState State;
            public PlayerState Player;
            public CityState Source;
            public CityState Target;
        }
    }
}
