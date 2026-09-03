using System.Collections.Generic;
using System.Linq;
using LittleCiv.Core;
using LittleCiv.Runtime;
using NUnit.Framework;

namespace LittleCiv.Tests
{
    public sealed class TurnProcessorTests
    {
        [Test]
        public void Resolve_VisitsAllTwelvePhasesInRulesOrder()
        {
            var state = CreateState();

            var result = new TurnProcessor().Resolve(state, new List<GameCommand>());

            var phases = result.Events
                .Where(item => item.Type == GameEventType.PhaseStarted)
                .Select(item => item.PrimaryValue)
                .ToArray();
            Assert.That(phases, Is.EqualTo(Enumerable.Range(1, 12).ToArray()));
            Assert.That((int)TurnPhase.Research, Is.EqualTo(4));
            Assert.That((int)TurnPhase.ScienceVictory, Is.EqualTo(5));
            Assert.That((int)TurnPhase.CultureAndConversion, Is.EqualTo(6));
            Assert.That((int)TurnPhase.CultureVictory, Is.EqualTo(7));
            Assert.That(state.TurnNumber, Is.EqualTo(2));
        }

        [Test]
        public void Resolve_OrdersTradeBeforeMovementRegardlessOfInputOrder()
        {
            var state = PrototypeMatchFactory.Create(346);
            var player = state.Players.Single(item => item.Slot == PlayerSlot.PlayerOne);
            var home = state.Cities.Single(item => item.OwnerId == player.Id);
            var neutral = state.Cities.First(item => item.NeutralSpecialization == NeutralCitySpecialization.Science &&
                NeutralTradeQuoteResolver.Quote(state, player.Id, home.Id, item.Id).IsAvailable);
            var move = new GameCommand
            {
                CommandId = new EntityId(20), PlayerId = player.Id, TurnNumber = state.TurnNumber,
                Type = GameCommandType.MoveUnit, SubjectId = state.Units.First(item => item.OwnerId == player.Id).Id
            };
            var trade = new GameCommand
            {
                CommandId = new EntityId(10), PlayerId = player.Id, TurnNumber = state.TurnNumber,
                Type = GameCommandType.Trade, SubjectId = home.Id, TargetId = neutral.Id,
                PrimaryValue = (int)TileResourceType.Science
            };

            var result = new TurnProcessor().Resolve(state, new[] { move, trade });

            Assert.That(result.Commands.Select(item => item.CommandId.Value), Is.EqualTo(new long[] { 10, 20 }));
        }

        [Test]
        public void Resolve_AddsWaitForUnitWithoutMovementOrder()
        {
            var state = CreateState();

            var result = new TurnProcessor().Resolve(state, new List<GameCommand>());

            Assert.That(result.Events.Any(item =>
                item.Type == GameEventType.DefaultActionApplied &&
                item.PrimaryValue == (int)DefaultActionType.UnitWaits &&
                item.TargetId == state.Units[0].Id), Is.True);
        }

        [Test]
        public void Resolve_DoesNotAddWaitWhenUnitHasMovementOrder()
        {
            var state = CreateState();
            var move = Command(state, 20, GameCommandType.MoveUnit, state.Units[0].Id);

            var result = new TurnProcessor().Resolve(state, new[] { move });

            Assert.That(result.Events.Any(item =>
                item.Type == GameEventType.DefaultActionApplied &&
                item.PrimaryValue == (int)DefaultActionType.UnitWaits &&
                item.TargetId == state.Units[0].Id), Is.False);
        }

        [Test]
        public void SameCommandsProduceSameHashAndEventLog()
        {
            var firstState = CreateState();
            var secondState = GameStateCopy.Clone(firstState);
            var commands = new[] { Command(firstState, 20, GameCommandType.MoveUnit, firstState.Units[0].Id) };

            var first = new TurnProcessor().Resolve(firstState, commands);
            var second = new TurnProcessor().Resolve(secondState, commands);

            Assert.That(second.ResultStateHash, Is.EqualTo(first.ResultStateHash));
            Assert.That(second.Events.Count, Is.EqualTo(first.Events.Count));
            for (var i = 0; i < first.Events.Count; i++)
            {
                Assert.That(second.Events[i].Sequence, Is.EqualTo(first.Events[i].Sequence));
                Assert.That(second.Events[i].Type, Is.EqualTo(first.Events[i].Type));
                Assert.That(second.Events[i].PrimaryValue, Is.EqualTo(first.Events[i].PrimaryValue));
            }
        }

        [Test]
        public void MatchJournal_ReplaysFromInitialStateToRecordedHash()
        {
            var state = CreateState();
            var journal = new MatchJournal(state);
            var processor = new TurnProcessor();
            journal.ResolveAndRecord(
                state,
                processor,
                new[] { Command(state, 20, GameCommandType.MoveUnit, state.Units[0].Id) });
            journal.ResolveAndRecord(state, processor, new List<GameCommand>());

            var replayed = journal.Replay();

            Assert.That(journal.Turns, Has.Count.EqualTo(2));
            Assert.That(GameStateHasher.Compute(replayed), Is.EqualTo(GameStateHasher.Compute(state)));
            Assert.That(journal.Turns[1].StateHash, Is.EqualTo(GameStateHasher.Compute(state)));
        }

        [Test]
        public void Snapshot_IsIndependentFromLaterLiveStateChanges()
        {
            var state = CreateState();
            var journal = new MatchJournal(state);
            journal.ResolveAndRecord(state, new TurnProcessor(), new List<GameCommand>());

            state.Players[0].Gold = 999;

            Assert.That(journal.Turns[0].StateSnapshot.Players[0].Gold, Is.Zero);
        }

        [Test]
        public void JournalJsonRoundTrip_RemainsReplayable()
        {
            var state = CreateState();
            var journal = new MatchJournal(state);
            journal.ResolveAndRecord(
                state,
                new TurnProcessor(),
                new[] { Command(state, 20, GameCommandType.MoveUnit, state.Units[0].Id) });

            var json = GameStateJsonSerializer.SerializeJournal(journal);
            var restored = GameStateJsonSerializer.DeserializeJournal(json);
            var replayed = restored.Replay();

            Assert.That(restored.Turns, Has.Count.EqualTo(1));
            Assert.That(GameStateHasher.Compute(replayed), Is.EqualTo(restored.Turns[0].StateHash));
        }

        [Test]
        public void Simulator_ResolvesTwoPrivatePlayerBuffersAndStartsNextTurn()
        {
            var state = CreateState();
            var simulator = new SimultaneousTurnSimulator(state);
            var playerOneCommand = Command(state, 20, GameCommandType.MoveUnit, state.Units[0].Id);
            var playerTwoCommand = new GameCommand
            {
                CommandId = new EntityId(21),
                PlayerId = state.Players[1].Id,
                TurnNumber = state.TurnNumber,
                Type = GameCommandType.MoveUnit,
                SubjectId = state.Units[1].Id
            };

            simulator.Planning.Reserve(playerOneCommand);
            simulator.Planning.Reserve(playerTwoCommand);
            simulator.Planning.Confirm(state.Players[0].Id);
            simulator.Planning.Confirm(state.Players[1].Id);
            var result = simulator.ResolveConfirmedTurn();

            Assert.That(result.Commands, Has.Count.EqualTo(2));
            Assert.That(simulator.Journal.Turns, Has.Count.EqualTo(1));
            Assert.That(simulator.State.TurnNumber, Is.EqualTo(2));
            Assert.That(simulator.Planning.TurnNumber, Is.EqualTo(2));
            Assert.That(simulator.Planning.IsClosed, Is.False);
        }

        private static GameState CreateState()
        {
            var state = GameState.CreateNew(345);
            var playerOne = state.AllocateId();
            var playerTwo = state.AllocateId();
            var tile = state.AllocateId();
            state.Players.Add(new PlayerState { Id = playerOne, Slot = PlayerSlot.PlayerOne });
            state.Players.Add(new PlayerState { Id = playerTwo, Slot = PlayerSlot.PlayerTwo });
            state.Units.Add(new UnitState
            {
                Id = state.AllocateId(),
                OwnerId = playerOne,
                TileId = tile,
                Type = UnitType.Militia,
                HitPoints = 16,
                CarriedFood = 6
            });
            state.Units.Add(new UnitState
            {
                Id = state.AllocateId(), OwnerId = playerTwo, TileId = state.AllocateId(),
                Type = UnitType.Militia, HitPoints = 16, CarriedFood = 6
            });
            return state;
        }

        private static GameCommand Command(
            GameState state,
            long commandId,
            GameCommandType type,
            EntityId subjectId = default)
        {
            return new GameCommand
            {
                CommandId = new EntityId(commandId),
                PlayerId = state.Players[0].Id,
                TurnNumber = state.TurnNumber,
                Type = type,
                SubjectId = subjectId
            };
        }
    }
}
