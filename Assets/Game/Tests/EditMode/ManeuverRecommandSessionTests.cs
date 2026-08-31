using System.Collections.Generic;
using LittleCiv.Core;
using NUnit.Framework;

namespace LittleCiv.Tests
{
    public sealed class ManeuverRecommandSessionTests
    {
        [Test]
        public void PublicStatusHidesChoiceAndOpponentCannotReadRequest()
        {
            var state = CreateState();
            var session = new ManeuverRecommandSession(state);
            session.Enqueue(Request(state.Players[0].Id, 100));

            var status = session.GetPublicStatus();

            Assert.That(status.IsRecommandPending, Is.True);
            Assert.That(status.ActingPlayerId, Is.EqualTo(state.Players[0].Id));
            Assert.That(session.GetPrivateRequest(state.Players[1].Id), Is.Null);
            Assert.That(session.GetOwnResolutions(state.Players[1].Id), Is.Empty);
        }

        [Test]
        public void PlayerCanChooseFight()
        {
            var state = CreateState();
            var session = new ManeuverRecommandSession(state);
            session.Enqueue(Request(state.Players[0].Id, 100));

            Assert.That(session.Respond(state.Players[0].Id, ManeuverChoice.Fight), Is.True);

            Assert.That(session.HasPendingRequest, Is.False);
            Assert.That(session.GetOwnResolutions(state.Players[0].Id)[0].Choice,
                Is.EqualTo(ManeuverChoice.Fight));
        }

        [Test]
        public void DetourRequiresAndCopiesNewPath()
        {
            var state = CreateState();
            var session = new ManeuverRecommandSession(state);
            session.Enqueue(Request(state.Players[0].Id, 100));
            var path = new List<EntityId> { new EntityId(700), new EntityId(701) };

            Assert.That(session.Respond(state.Players[0].Id, ManeuverChoice.Detour), Is.False);
            Assert.That(session.Respond(state.Players[0].Id, ManeuverChoice.Detour, path), Is.True);
            path.Clear();

            Assert.That(session.GetOwnResolutions(state.Players[0].Id)[0].DetourPath, Has.Count.EqualTo(2));
        }

        [Test]
        public void TwentySecondTimeoutDefaultsToWait()
        {
            var state = CreateState();
            var session = new ManeuverRecommandSession(state);
            session.Enqueue(Request(state.Players[0].Id, 100));

            session.AdvanceTime(20);

            var result = session.GetOwnResolutions(state.Players[0].Id)[0];
            Assert.That(result.Choice, Is.EqualTo(ManeuverChoice.Wait));
            Assert.That(result.Reason, Is.EqualTo(ManeuverResolutionReason.RequestTimedOut));
        }

        [Test]
        public void FortySecondPlayerBudgetMakesLaterRequestsWaitImmediately()
        {
            var state = CreateState();
            var session = new ManeuverRecommandSession(state);
            session.Enqueue(Request(state.Players[0].Id, 100));
            session.Enqueue(Request(state.Players[0].Id, 101));
            session.Enqueue(Request(state.Players[0].Id, 102));

            session.AdvanceTime(40);

            var results = session.GetOwnResolutions(state.Players[0].Id);
            Assert.That(results, Has.Count.EqualTo(3));
            Assert.That(results[2].Choice, Is.EqualTo(ManeuverChoice.Wait));
            Assert.That(results[2].Reason, Is.EqualTo(ManeuverResolutionReason.PlayerBudgetExhausted));
            Assert.That(session.RemainingBudget(state.Players[0].Id), Is.Zero);
        }

        [Test]
        public void UnitCanReceiveOnlyOneRequestPerTurn()
        {
            var state = CreateState();
            var session = new ManeuverRecommandSession(state);
            var request = Request(state.Players[0].Id, 100);

            Assert.That(session.Enqueue(request), Is.True);
            Assert.That(session.Enqueue(request), Is.False);
        }

        [Test]
        public void RecommandTimeDoesNotSpendNormalTurnReserve()
        {
            var state = CreateState();
            state.Players[0].ReserveTimeSeconds = 75;
            var session = new ManeuverRecommandSession(state);
            session.Enqueue(Request(state.Players[0].Id, 100));

            session.AdvanceTime(20);

            Assert.That(state.Players[0].ReserveTimeSeconds, Is.EqualTo(75));
        }

        private static GameState CreateState()
        {
            var state = GameState.CreateNew(999);
            state.Players.Add(new PlayerState
            {
                Id = state.AllocateId(), Slot = PlayerSlot.PlayerOne, ReserveTimeSeconds = 180
            });
            state.Players.Add(new PlayerState
            {
                Id = state.AllocateId(), Slot = PlayerSlot.PlayerTwo, ReserveTimeSeconds = 180
            });
            return state;
        }

        private static ManeuverRequest Request(EntityId playerId, long unitId)
        {
            return new ManeuverRequest
            {
                PlayerId = playerId,
                UnitId = new EntityId(unitId),
                LastValidTileId = new EntityId(500),
                BlockedTileId = new EntityId(501),
                RemainingMovement = 2
            };
        }
    }
}
