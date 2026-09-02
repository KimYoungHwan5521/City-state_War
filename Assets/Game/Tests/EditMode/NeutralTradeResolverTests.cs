using System.Linq;
using LittleCiv.Core;
using NUnit.Framework;

namespace LittleCiv.Tests
{
    public sealed class NeutralTradeResolverTests
    {
        [Test]
        public void SciencePurchasePaysNowAndAppliesAtNextTurnProduction()
        {
            var fixture = Create(13800, "N2");
            var beforeGold = fixture.Source.Gold;
            var command = Command(fixture, TileResourceType.Science);

            Assert.That(NeutralTradeResolver.TryExecute(fixture.State, command, out var execution), Is.True);
            Assert.That(fixture.Source.Gold, Is.EqualTo(beforeGold - 2));
            Assert.That(fixture.Target.NeutralRelations.Single(item =>
                item.PlayerId == fixture.Player.Id).Favor, Is.EqualTo(1));
            Assert.That(execution.IsDeferred, Is.True);
            Assert.That(fixture.State.TradeReservations.Single().ApplyTurn,
                Is.EqualTo(fixture.State.TurnNumber + 1));

            fixture.State.TurnNumber++;
            CityEconomyResolver.ResolveProduction(fixture.State);
            var applied = NeutralTradeResolver.ApplyPending(fixture.State).Single();
            Assert.That(applied.ResourceAmount, Is.EqualTo(1));
            Assert.That(fixture.Source.LastScienceProduction, Is.EqualTo(2));
            Assert.That(fixture.Source.ResearchPoints, Is.EqualTo(2));
            Assert.That(fixture.State.TradeReservations, Is.Empty);
        }

        [Test]
        public void FoodSaleTransfersImmediatelyAndHostileTradeRecoversFavor()
        {
            var fixture = Create(13801, "N4");
            fixture.Source.StoredFood = 10;
            NeutralCityRules.SetFavor(fixture.Target, fixture.Player.Id, -2);
            var beforeGold = fixture.Source.Gold;

            Assert.That(NeutralTradeResolver.TryExecute(fixture.State,
                Command(fixture, TileResourceType.Food), out var execution), Is.True);

            Assert.That(fixture.Source.StoredFood, Is.EqualTo(7));
            Assert.That(fixture.Source.Gold, Is.EqualTo(beforeGold + 1));
            Assert.That(NeutralCityRules.Favor(fixture.Target, fixture.Player.Id), Is.EqualTo(-1));
            Assert.That(execution.IsDeferred, Is.False);
            Assert.That(fixture.State.TradeReservations, Is.Empty);
        }

        [Test]
        public void ScienceSaleUsesAvailableNextTurnProductionAndProportionalGold()
        {
            var fixture = Create(13802, "N4");
            var beforeGold = fixture.Source.Gold;
            Assert.That(NeutralTradeResolver.TryExecute(fixture.State,
                Command(fixture, TileResourceType.Science), out _), Is.True);

            fixture.State.TurnNumber++;
            CityEconomyResolver.ResolveProduction(fixture.State);
            var applied = NeutralTradeResolver.ApplyPending(fixture.State).Single();

            Assert.That(applied.ResourceAmount, Is.EqualTo(1));
            Assert.That(applied.GoldAmount, Is.EqualTo(1));
            Assert.That(fixture.Source.LastScienceProduction, Is.Zero);
            Assert.That(fixture.Source.ResearchPoints, Is.Zero);
            Assert.That(fixture.Source.Gold, Is.GreaterThan(beforeGold));
        }

        [Test]
        public void ReservationSurvivesCopyAndChangesDeterministicHash()
        {
            var fixture = Create(13803, "N2");
            Assert.That(NeutralTradeResolver.TryExecute(fixture.State,
                Command(fixture, TileResourceType.Science), out _), Is.True);
            var copy = GameStateCopy.Clone(fixture.State);

            Assert.That(GameStateHasher.Compute(copy), Is.EqualTo(GameStateHasher.Compute(fixture.State)));
            copy.TradeReservations[0].ResourceAmount++;
            Assert.That(GameStateHasher.Compute(copy), Is.Not.EqualTo(GameStateHasher.Compute(fixture.State)));
        }

        [Test]
        public void TurnProcessorEmitsExecutionAndNextTurnApplicationEvents()
        {
            var fixture = Create(13804, "N2");
            var processor = new TurnProcessor();

            var first = processor.Resolve(fixture.State,
                new[] { Command(fixture, TileResourceType.Science) });
            var second = processor.Resolve(fixture.State, new GameCommand[0]);

            Assert.That(first.Events.Any(item => item.Type == GameEventType.NeutralTradeExecuted), Is.True);
            Assert.That(second.Events.Any(item =>
                item.Type == GameEventType.NeutralTradeReservationApplied), Is.True);
        }

        private static Fixture Create(long seed, string targetName)
        {
            var state = PrototypeMatchFactory.Create(seed);
            var player = state.Players.Single(item => item.Slot == PlayerSlot.PlayerOne);
            var source = state.Cities.Single(item => item.OwnerId == player.Id);
            source.Gold = 100;
            return new Fixture
            {
                State = state, Player = player, Source = source,
                Target = state.Cities.Single(item => item.Name == targetName)
            };
        }

        private static GameCommand Command(Fixture fixture, TileResourceType resource) =>
            new GameCommand
            {
                CommandId = fixture.State.AllocateId(), PlayerId = fixture.Player.Id,
                TurnNumber = fixture.State.TurnNumber, Type = GameCommandType.Trade,
                SubjectId = fixture.Source.Id, TargetId = fixture.Target.Id,
                PrimaryValue = (int)resource
            };

        private sealed class Fixture
        {
            public GameState State;
            public PlayerState Player;
            public CityState Source;
            public CityState Target;
        }
    }
}
