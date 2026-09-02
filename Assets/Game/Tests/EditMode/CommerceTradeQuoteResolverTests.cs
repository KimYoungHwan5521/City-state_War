using System.Linq;
using LittleCiv.Core;
using NUnit.Framework;

namespace LittleCiv.Tests
{
    public sealed class CommerceTradeQuoteResolverTests
    {
        [TestCase(-2, 3, 1, 2)]
        [TestCase(-1, 3, 1, 2)]
        [TestCase(0, 2, 1, 1)]
        [TestCase(1, 2, 1, 1)]
        [TestCase(2, 1, 1, 1)]
        [TestCase(3, 1, 2, 1)]
        public void EarlyCommerceCityUsesFavorTable(
            int favor, int resource, int gold, int shippingRate)
        {
            var fixture = Create(13700 + favor, "N4");
            fixture.Source.StoredFood = 100;
            if (favor == 3) fixture.Target.CultureSubjectToId = fixture.Player.Id;
            NeutralCityRules.SetFavor(fixture.Target, fixture.Player.Id, favor);

            var quote = CommerceTradeQuoteResolver.Quote(fixture.State, fixture.Player.Id,
                fixture.Source.Id, fixture.Target.Id, TileResourceType.Food);

            Assert.That(quote.IsAvailable, Is.True);
            Assert.That(quote.RequiredResourceAmount, Is.EqualTo(resource));
            Assert.That(quote.BaseGoldPayment, Is.EqualTo(gold));
            Assert.That(quote.ShippingGoldPerAdditionalDistance, Is.EqualTo(shippingRate));
        }

        [Test]
        public void DistantCommerceTradeIsRejectedWhenShippingConsumesPayment()
        {
            var fixture = Create(13710, "N5");
            fixture.Source.StoredFood = 100;

            var quote = CommerceTradeQuoteResolver.Quote(fixture.State, fixture.Player.Id,
                fixture.Source.Id, fixture.Target.Id, TileResourceType.Food);

            Assert.That(quote.Route.AdditionalDistance, Is.GreaterThan(0));
            Assert.That(quote.IsAvailable, Is.False);
            Assert.That(quote.Failure, Is.EqualTo(CommerceTradeQuoteFailure.ShippingConsumesPayment));
            Assert.That(quote.ShippingGoldCost, Is.GreaterThanOrEqualTo(quote.BaseGoldPayment));
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
            Assert.That(science.RequiredResourceAmount, Is.EqualTo(2));
        }

        [Test]
        public void MiddleCulturalSubjectOfferScalesRequiredResourceAndGold()
        {
            var fixture = Create(13712, "N4");
            AddCommerceDistricts(fixture, 2);
            fixture.Source.StoredFood = 100;
            fixture.Target.CultureSubjectToId = fixture.Player.Id;
            NeutralCityRules.SetFavor(fixture.Target, fixture.Player.Id, 3);

            var quote = CommerceTradeQuoteResolver.Quote(fixture.State, fixture.Player.Id,
                fixture.Source.Id, fixture.Target.Id, TileResourceType.Food);

            Assert.That(quote.DevelopmentStage, Is.EqualTo(NeutralDevelopmentStage.Middle));
            Assert.That(quote.RequiredResourceAmount, Is.EqualTo(2));
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
