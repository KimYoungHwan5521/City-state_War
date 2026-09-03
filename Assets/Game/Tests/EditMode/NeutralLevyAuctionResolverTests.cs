using System.Collections.Generic;
using System.Linq;
using LittleCiv.Core;
using NUnit.Framework;

namespace LittleCiv.Tests
{
    public sealed class NeutralLevyAuctionResolverTests
    {
        [Test]
        public void HigherFinalPriceWinsIndependentOfCommandOrder()
        {
            AssertHigherBidWinner(false);
            AssertHigherBidWinner(true);
        }

        [Test]
        public void EqualPriceIsATieRegardlessOfTradeDistance()
        {
            var fixture = Create(14202, "N8");
            var result = NeutralLevyAuctionResolver.Resolve(fixture.State, new[]
            {
                Bid(fixture, fixture.One, fixture.CityOne, 0),
                Bid(fixture, fixture.Two, fixture.CityTwo, 0)
            }).Single();

            Assert.That(result.IsTie, Is.True);
            Assert.That(result.Levy, Is.Null);
            Assert.That(result.Bids.Single(item => item.Command.PlayerId == fixture.One.Id)
                .Quote.Route.Distance, Is.EqualTo(2));
            Assert.That(result.Bids.Single(item => item.Command.PlayerId == fixture.Two.Id)
                .Quote.Route.Distance, Is.EqualTo(1));
        }

        [Test]
        public void CompleteTieChangesNoGoldFavorOrUnitControl()
        {
            var fixture = Create(14203, "N1");
            // Put the military city one world step from both player cities so distance cannot break the tie.
            fixture.Military.WorldQ = 0;
            fixture.Military.WorldR = 1;
            var oneGold = fixture.CityOne.Gold;
            var twoGold = fixture.CityTwo.Gold;

            var result = NeutralLevyAuctionResolver.Resolve(fixture.State, new[]
            {
                Bid(fixture, fixture.One, fixture.CityOne, 0),
                Bid(fixture, fixture.Two, fixture.CityTwo, 0)
            }).Single();

            Assert.That(result.IsTie, Is.True);
            Assert.That(result.Levy, Is.Null);
            Assert.That(fixture.State.Levies, Is.Empty);
            Assert.That(fixture.CityOne.Gold, Is.EqualTo(oneGold));
            Assert.That(fixture.CityTwo.Gold, Is.EqualTo(twoGold));
            Assert.That(NeutralCityRules.Favor(fixture.Military, fixture.One.Id), Is.Zero);
            Assert.That(NeutralCityRules.Favor(fixture.Military, fixture.Two.Id), Is.Zero);
            Assert.That(fixture.State.Units.Single(item =>
                item.HomeCityId == fixture.Military.Id).OwnerId, Is.EqualTo(fixture.Military.OwnerId));
        }

        [Test]
        public void EqualExtraBidTiesEvenWhenRelationshipDiscountChangesTotalPrice()
        {
            var fixture = Create(14205, "N1");
            NeutralCityRules.SetFavor(fixture.Military, fixture.One.Id, 3);

            var result = NeutralLevyAuctionResolver.Resolve(fixture.State, new[]
            {
                Bid(fixture, fixture.One, fixture.CityOne, 0),
                Bid(fixture, fixture.Two, fixture.CityTwo, 0)
            }).Single();

            Assert.That(result.Bids[0].FinalPrice, Is.Not.EqualTo(result.Bids[1].FinalPrice));
            Assert.That(result.IsTie, Is.True);
            Assert.That(result.Levy, Is.Null);
        }

        [Test]
        public void WinnerConditionalMoveExecutesAndLoserMoveIsCancelled()
        {
            var fixture = Create(14204, "N1");
            var unit = fixture.State.Units.Single(item => item.HomeCityId == fixture.Military.Id);
            var destination = fixture.State.Tiles.First(item => item.CityId == fixture.Military.Id &&
                item.Id != unit.TileId &&
                MapTraversal.AreAdjacent(fixture.State, unit.TileId, item.Id) &&
                fixture.State.Units.All(other => other.TileId != item.Id));
            var winningMove = Move(fixture, fixture.One, unit.Id, destination.Id);
            var losingMove = Move(fixture, fixture.Two, unit.Id, destination.Id);
            var commands = new List<GameCommand>
            {
                Bid(fixture, fixture.One, fixture.CityOne, 2),
                Bid(fixture, fixture.Two, fixture.CityTwo, 0),
                winningMove, losingMove
            };

            var turn = new TurnProcessor().Resolve(fixture.State, commands);

            Assert.That(unit.OwnerId, Is.EqualTo(fixture.One.Id));
            Assert.That(unit.TileId, Is.EqualTo(destination.Id));
            Assert.That(turn.Events.Any(item => item.Type ==
                GameEventType.NeutralLevyConditionalMoveCancelled &&
                item.SourceId == fixture.Two.Id), Is.True);
            Assert.That(turn.Events.Any(item => item.Type == GameEventType.UnitMoved &&
                item.SourceId == unit.Id), Is.True);
        }

        private static void AssertHigherBidWinner(bool reverse)
        {
            var fixture = Create(reverse ? 14201 : 14200, "N1");
            var high = Bid(fixture, fixture.One, fixture.CityOne, 2);
            var low = Bid(fixture, fixture.Two, fixture.CityTwo, 1);
            var commands = reverse ? new[] { low, high } : new[] { high, low };

            var result = NeutralLevyAuctionResolver.Resolve(fixture.State, commands).Single();

            Assert.That(result.Levy.PlayerId, Is.EqualTo(fixture.One.Id));
            Assert.That(result.Levy.PaidGold, Is.EqualTo(5));
            Assert.That(fixture.CityOne.Gold, Is.EqualTo(95));
            Assert.That(fixture.CityTwo.Gold, Is.EqualTo(100));
        }

        private static Fixture Create(long seed, string militaryName)
        {
            var state = PrototypeMatchFactory.Create(seed);
            var one = state.Players.Single(item => item.Slot == PlayerSlot.PlayerOne);
            var two = state.Players.Single(item => item.Slot == PlayerSlot.PlayerTwo);
            var cityOne = state.Cities.Single(item => item.OwnerId == one.Id);
            var cityTwo = state.Cities.Single(item => item.OwnerId == two.Id);
            cityOne.Gold = cityTwo.Gold = 100;
            return new Fixture
            {
                State = state, One = one, Two = two, CityOne = cityOne, CityTwo = cityTwo,
                Military = state.Cities.Single(item => item.Name == militaryName)
            };
        }

        private static GameCommand Bid(Fixture fixture, PlayerState player,
            CityState payment, int extra) => new GameCommand
        {
            CommandId = fixture.State.AllocateId(), PlayerId = player.Id,
            TurnNumber = fixture.State.TurnNumber, Type = GameCommandType.LevyBid,
            SubjectId = payment.Id, TargetId = fixture.Military.Id, PrimaryValue = extra
        };

        private static GameCommand Move(Fixture fixture, PlayerState player,
            EntityId unitId, EntityId destination) => new GameCommand
        {
            CommandId = fixture.State.AllocateId(), PlayerId = player.Id,
            TurnNumber = fixture.State.TurnNumber, Type = GameCommandType.MoveUnit,
            SubjectId = unitId, TargetId = destination,
            Path = new List<EntityId> { destination }
        };

        private sealed class Fixture
        {
            public GameState State;
            public PlayerState One;
            public PlayerState Two;
            public CityState CityOne;
            public CityState CityTwo;
            public CityState Military;
        }
    }
}
