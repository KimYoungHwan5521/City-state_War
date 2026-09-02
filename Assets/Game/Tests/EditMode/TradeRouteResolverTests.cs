using System.Linq;
using LittleCiv.Core;
using NUnit.Framework;

namespace LittleCiv.Tests
{
    public sealed class TradeRouteResolverTests
    {
        [Test]
        public void AdjacentNeutralCityHasNoAdditionalDistance()
        {
            var state = PrototypeMatchFactory.Create(13500);
            var player = state.Players.Single(item => item.Slot == PlayerSlot.PlayerOne);
            var source = state.Cities.Single(item => item.OwnerId == player.Id);
            var target = state.Cities.Single(item => item.Name == "N1");

            var route = TradeRouteResolver.Find(state, player.Id, source.Id, target.Id);

            Assert.That(route.IsReachable, Is.True);
            Assert.That(route.Distance, Is.EqualTo(1));
            Assert.That(route.AdditionalDistance, Is.Zero);
            Assert.That(route.CityPath, Is.EqualTo(new[] { source.Id, target.Id }));
        }

        [Test]
        public void HostileTargetIsAllowedButHostileIntermediateIsAvoided()
        {
            var state = PrototypeMatchFactory.Create(13501);
            var player = state.Players.Single(item => item.Slot == PlayerSlot.PlayerOne);
            var source = state.Cities.Single(item => item.OwnerId == player.Id);
            var hostileIntermediate = state.Cities.Single(item => item.Name == "N2");
            var target = state.Cities.Single(item => item.Name == "N3");
            NeutralCityRules.SetFavor(hostileIntermediate, player.Id, -1);
            NeutralCityRules.SetFavor(target, player.Id, -2);

            var route = TradeRouteResolver.Find(state, player.Id, source.Id, target.Id);

            Assert.That(route.IsReachable, Is.True);
            Assert.That(route.CityPath.Contains(hostileIntermediate.Id), Is.False);
            Assert.That(route.BlockedCityIds, Does.Contain(hostileIntermediate.Id));
            Assert.That(route.Distance, Is.EqualTo(4));
            Assert.That(route.AdditionalDistance, Is.EqualTo(3));
        }

        [Test]
        public void OpponentCityCannotBeUsedAsIntermediate()
        {
            var state = PrototypeMatchFactory.Create(13502);
            var player = state.Players.Single(item => item.Slot == PlayerSlot.PlayerOne);
            var opponentCity = state.Cities.Single(item => item.Name == "B");
            var source = state.Cities.Single(item => item.OwnerId == player.Id);
            var target = state.Cities.Single(item => item.Name == "N5");

            var route = TradeRouteResolver.Find(state, player.Id, source.Id, target.Id);

            Assert.That(route.IsReachable, Is.True);
            Assert.That(route.CityPath.Contains(opponentCity.Id), Is.False);
            Assert.That(route.BlockedCityIds, Does.Contain(opponentCity.Id));
        }

        [Test]
        public void OpponentOccupiedTargetIsUnavailable()
        {
            var state = PrototypeMatchFactory.Create(13503);
            var player = state.Players.Single(item => item.Slot == PlayerSlot.PlayerOne);
            var opponent = state.Players.Single(item => item.Slot == PlayerSlot.PlayerTwo);
            var source = state.Cities.Single(item => item.OwnerId == player.Id);
            var target = state.Cities.Single(item => item.Name == "N1");
            state.Districts.Single(item => item.CityId == target.Id &&
                item.Type == DistrictType.Government).ControllerId = opponent.Id;

            var route = TradeRouteResolver.Find(state, player.Id, source.Id, target.Id);

            Assert.That(route.IsReachable, Is.False);
            Assert.That(route.BlockedCityIds, Does.Contain(target.Id));
        }
    }
}
