using System;
using System.Collections.Generic;

namespace LittleCiv.Core
{
    public sealed class LevyBidEvaluation
    {
        public GameCommand Command;
        public LevyQuote Quote;
        public int FinalPrice;
        public bool IsValid;
        public bool Won;
    }

    public sealed class LevyAuctionResult
    {
        public EntityId MilitaryCityId;
        public LevyState Levy;
        public bool IsTie;
        public readonly List<LevyBidEvaluation> Bids = new List<LevyBidEvaluation>();
    }

    public static class NeutralLevyAuctionResolver
    {
        public static List<LevyAuctionResult> Resolve(GameState state, IReadOnlyList<GameCommand> commands)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (commands == null) throw new ArgumentNullException(nameof(commands));
            var targets = new List<EntityId>();
            var seenCommandIds = new HashSet<EntityId>();
            for (var index = 0; index < commands.Count; index++)
                if (commands[index].Type == GameCommandType.LevyBid &&
                    !targets.Contains(commands[index].TargetId)) targets.Add(commands[index].TargetId);
            targets.Sort();
            var results = new List<LevyAuctionResult>();
            for (var targetIndex = 0; targetIndex < targets.Count; targetIndex++)
            {
                var result = new LevyAuctionResult { MilitaryCityId = targets[targetIndex] };
                var usedPlayers = new HashSet<EntityId>();
                for (var commandIndex = 0; commandIndex < commands.Count; commandIndex++)
                {
                    var command = commands[commandIndex];
                    if (command.Type != GameCommandType.LevyBid ||
                        command.TargetId != result.MilitaryCityId || !usedPlayers.Add(command.PlayerId)) continue;
                    var quote = NeutralLevyResolver.Quote(state, command.PlayerId,
                        command.SubjectId, command.TargetId);
                    var finalPrice = quote.BasePrice + Math.Max(0, command.PrimaryValue);
                    var payment = state.Cities.Find(item => item.Id == command.SubjectId);
                    result.Bids.Add(new LevyBidEvaluation
                    {
                        Command = command, Quote = quote, FinalPrice = finalPrice,
                        IsValid = seenCommandIds.Add(command.CommandId) &&
                                  CommandValidator.ValidateEnvelope(state, command) == CommandValidationError.None &&
                                  command.PrimaryValue >= 0 && quote.IsAvailable &&
                                  payment != null && payment.Gold >= finalPrice
                    });
                }
                var valid = result.Bids.FindAll(item => item.IsValid);
                valid.Sort(CompareBids);
                if (valid.Count == 0) { results.Add(result); continue; }
                if (valid.Count > 1 && valid[0].Command.PrimaryValue == valid[1].Command.PrimaryValue)
                {
                    result.IsTie = true;
                    results.Add(result);
                    continue;
                }
                var winner = valid[0];
                if (NeutralLevyResolver.TryStart(state, winner.Command.PlayerId,
                    winner.Command.SubjectId, winner.Command.TargetId,
                    winner.FinalPrice, out var levy))
                {
                    winner.Won = true;
                    result.Levy = levy;
                }
                results.Add(result);
            }
            return results;
        }

        private static int CompareBids(LevyBidEvaluation left, LevyBidEvaluation right)
        {
            var price = right.Command.PrimaryValue.CompareTo(left.Command.PrimaryValue);
            if (price != 0) return price;
            return left.Command.PlayerId.CompareTo(right.Command.PlayerId);
        }
    }
}
