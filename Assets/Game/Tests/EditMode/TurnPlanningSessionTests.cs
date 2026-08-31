using LittleCiv.Core;
using NUnit.Framework;

namespace LittleCiv.Tests
{
    public sealed class TurnPlanningSessionTests
    {
        [Test]
        public void CommandsRemainPrivateUntilEveryPlayerConfirms()
        {
            var state = CreateState();
            var session = new TurnPlanningSession(state);
            var command = CreateCommand(state, state.Players[0].Id, 100);

            Assert.That(session.Reserve(command), Is.EqualTo(CommandMutationResult.Accepted));
            Assert.That(session.GetOwnCommands(state.Players[0].Id), Has.Count.EqualTo(1));
            Assert.Throws<System.InvalidOperationException>(() => session.BuildResolutionBatch());

            session.Confirm(state.Players[0].Id);
            session.Confirm(state.Players[1].Id);
            Assert.That(session.BuildResolutionBatch(), Has.Count.EqualTo(1));
        }

        [Test]
        public void ReservedCommandIsCopiedAndCanBeUpdatedOrCancelled()
        {
            var state = CreateState();
            var session = new TurnPlanningSession(state);
            var command = CreateCommand(state, state.Players[0].Id, 100);

            session.Reserve(command);
            command.PrimaryValue = 7;
            Assert.That(session.GetOwnCommands(command.PlayerId)[0].PrimaryValue, Is.EqualTo(0));

            Assert.That(session.Reserve(command), Is.EqualTo(CommandMutationResult.Accepted));
            Assert.That(session.GetOwnCommands(command.PlayerId)[0].PrimaryValue, Is.EqualTo(7));
            Assert.That(session.Cancel(command.PlayerId, command.CommandId), Is.EqualTo(CommandMutationResult.Accepted));
            Assert.That(session.GetOwnCommands(command.PlayerId), Is.Empty);
        }

        [Test]
        public void ConfirmationCanBeCancelledOnlyWhileOpponentIsPlanning()
        {
            var state = CreateState();
            var session = new TurnPlanningSession(state);

            Assert.That(session.Confirm(state.Players[0].Id), Is.True);
            Assert.That(session.CancelConfirmation(state.Players[0].Id), Is.True);
            Assert.That(session.Confirm(state.Players[0].Id), Is.True);
            Assert.That(session.Confirm(state.Players[1].Id), Is.True);
            Assert.That(session.CancelConfirmation(state.Players[0].Id), Is.False);
        }

        [Test]
        public void BaseTimeThenReserveTimeAutoConfirms()
        {
            var state = CreateState();
            state.Players[0].ReserveTimeSeconds = 20;
            state.Players[1].ReserveTimeSeconds = 0;
            var session = new TurnPlanningSession(state);

            Assert.That(state.Players[0].ReserveTimeSeconds, Is.EqualTo(30));
            Assert.That(state.Players[1].ReserveTimeSeconds, Is.EqualTo(10));
            session.AdvanceTime(90);
            Assert.That(session.IsClosed, Is.False);
            session.AdvanceTime(10);
            Assert.That(session.GetConfirmation(state.Players[1].Id), Is.EqualTo(TurnConfirmationReason.TimeExpired));
            Assert.That(state.Players[0].ReserveTimeSeconds, Is.EqualTo(20));
            session.AdvanceTime(20);
            Assert.That(session.IsClosed, Is.True);
            Assert.That(state.Players[0].ReserveTimeSeconds, Is.Zero);
        }

        [Test]
        public void ConfirmedPlayerDoesNotSpendReserveTime()
        {
            var state = CreateState();
            state.Players[0].ReserveTimeSeconds = 50;
            var session = new TurnPlanningSession(state);
            session.Confirm(state.Players[0].Id);

            session.AdvanceTime(120);

            Assert.That(state.Players[0].ReserveTimeSeconds, Is.EqualTo(60));
        }

        [Test]
        public void ResolutionOrderIsDeterministicRegardlessOfReservationOrder()
        {
            var firstState = CreateState();
            var secondState = CreateState();
            var first = new TurnPlanningSession(firstState);
            var second = new TurnPlanningSession(secondState);
            var commandA = CreateCommand(firstState, firstState.Players[1].Id, 200);
            commandA.Type = GameCommandType.MoveUnit;
            var commandB = CreateCommand(firstState, firstState.Players[0].Id, 100);
            commandB.Type = GameCommandType.Trade;

            first.Reserve(commandA);
            first.Reserve(commandB);
            second.Reserve(commandB);
            second.Reserve(commandA);
            ConfirmBoth(firstState, first);
            ConfirmBoth(secondState, second);

            Assert.That(first.BuildResolutionBatch()[0].CommandId, Is.EqualTo(second.BuildResolutionBatch()[0].CommandId));
            Assert.That(first.BuildResolutionBatch()[1].CommandId, Is.EqualTo(second.BuildResolutionBatch()[1].CommandId));
        }

        private static GameState CreateState()
        {
            var state = GameState.CreateNew(123);
            state.Players.Add(new PlayerState
            {
                Id = state.AllocateId(),
                Slot = PlayerSlot.PlayerOne,
                ReserveTimeSeconds = 180
            });
            state.Players.Add(new PlayerState
            {
                Id = state.AllocateId(),
                Slot = PlayerSlot.PlayerTwo,
                ReserveTimeSeconds = 180
            });
            return state;
        }

        private static GameCommand CreateCommand(GameState state, EntityId playerId, long id)
        {
            return new GameCommand
            {
                CommandId = new EntityId(id),
                PlayerId = playerId,
                TurnNumber = state.TurnNumber,
                Type = GameCommandType.AssignCitizen
            };
        }

        private static void ConfirmBoth(GameState state, TurnPlanningSession session)
        {
            session.Confirm(state.Players[0].Id);
            session.Confirm(state.Players[1].Id);
        }
    }
}
