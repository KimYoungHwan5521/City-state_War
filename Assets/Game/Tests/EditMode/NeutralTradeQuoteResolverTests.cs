using System.Linq;
using LittleCiv.Core;
using NUnit.Framework;

namespace LittleCiv.Tests
{
    public sealed class NeutralTradeQuoteResolverTests
    {
        [TestCase(-2, 3, 1, 2)]
        [TestCase(-1, 3, 1, 2)]
        [TestCase(0, 2, 1, 1)]
        [TestCase(1, 2, 1, 1)]
        [TestCase(2, 1, 1, 1)]
        [TestCase(3, 1, 2, 1)]
        public void EarlyScienceCityUsesFavorTable(
            int favor, int baseGold, int science, int distanceRate)
        {
            var fixture = Create(13600 + favor);
            if (favor == 3) fixture.Target.CultureSubjectToId = fixture.Player.Id;
            NeutralCityRules.SetFavor(fixture.Target, fixture.Player.Id, favor);

            var quote = NeutralTradeQuoteResolver.Quote(fixture.State, fixture.Player.Id,
                fixture.Source.Id, fixture.Target.Id);

            Assert.That(quote.IsAvailable, Is.True);
            Assert.That(quote.DevelopmentStage, Is.EqualTo(NeutralDevelopmentStage.Early));
            Assert.That(quote.BaseGoldCost, Is.EqualTo(baseGold));
            Assert.That(quote.ResourceAmount, Is.EqualTo(science));
            Assert.That(quote.GoldPerAdditionalDistance, Is.EqualTo(distanceRate));
            Assert.That(quote.ReceivedResource, Is.EqualTo(TileResourceType.Science));
        }

        [Test]
        public void DevelopmentStageScalesTheWholeBundle()
        {
            var fixture = Create(13610);
            AddSpecializationDistricts(fixture, 2);
            fixture.Target.CultureSubjectToId = fixture.Player.Id;
            NeutralCityRules.SetFavor(fixture.Target, fixture.Player.Id, 3);

            var quote = NeutralTradeQuoteResolver.Quote(fixture.State, fixture.Player.Id,
                fixture.Source.Id, fixture.Target.Id);

            Assert.That(quote.DevelopmentStage, Is.EqualTo(NeutralDevelopmentStage.Middle));
            Assert.That(quote.BaseGoldCost, Is.EqualTo(2));
            Assert.That(quote.ResourceAmount, Is.EqualTo(4));
            Assert.That(quote.GoldPerAdditionalDistance, Is.EqualTo(2));
        }

        [Test]
        public void AdditionalDistanceIsAddedToTotalCost()
        {
            var state = PrototypeMatchFactory.Create(13611);
            var player = state.Players.Single(item => item.Slot == PlayerSlot.PlayerOne);
            var source = state.Cities.Single(item => item.OwnerId == player.Id);
            var target = state.Cities.Single(item => item.Name == "N3");
            source.Gold = 100;

            var quote = NeutralTradeQuoteResolver.Quote(state, player.Id, source.Id, target.Id);

            Assert.That(quote.IsAvailable, Is.True);
            Assert.That(quote.ReceivedResource, Is.EqualTo(TileResourceType.Culture));
            Assert.That(quote.Route.Distance, Is.EqualTo(2));
            Assert.That(quote.BaseGoldCost, Is.EqualTo(2));
            Assert.That(quote.DistanceGoldCost, Is.EqualTo(1));
            Assert.That(quote.TotalGoldCost, Is.EqualTo(3));
        }

        [Test]
        public void QuoteReportsInsufficientGoldWithoutDiscardingCalculatedAmounts()
        {
            var fixture = Create(13612);
            fixture.Source.Gold = 0;

            var quote = NeutralTradeQuoteResolver.Quote(fixture.State, fixture.Player.Id,
                fixture.Source.Id, fixture.Target.Id);

            Assert.That(quote.IsAvailable, Is.False);
            Assert.That(quote.Failure, Is.EqualTo(NeutralTradeQuoteFailure.InsufficientGold));
            Assert.That(quote.TotalGoldCost, Is.EqualTo(2));
            Assert.That(quote.ResourceAmount, Is.EqualTo(1));
        }

        private static Fixture Create(long seed)
        {
            var state = PrototypeMatchFactory.Create(seed);
            var player = state.Players.Single(item => item.Slot == PlayerSlot.PlayerOne);
            var source = state.Cities.Single(item => item.OwnerId == player.Id);
            source.Gold = 100;
            return new Fixture
            {
                State = state, Player = player, Source = source,
                Target = state.Cities.Single(item => item.Name == "N2")
            };
        }

        private static void AddSpecializationDistricts(Fixture fixture, int count)
        {
            for (var index = 0; index < count; index++)
            {
                var tile = fixture.State.MapTopology.FindView(fixture.Target.Id).Tiles.First(item =>
                    item.IsBuildable && fixture.State.Districts.All(district => district.TileId != item.TileId));
                fixture.State.Districts.Add(new DistrictState
                {
                    Id = fixture.State.AllocateId(), CityId = fixture.Target.Id, TileId = tile.TileId,
                    Type = DistrictType.Science, ControllerId = fixture.Target.OwnerId,
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
