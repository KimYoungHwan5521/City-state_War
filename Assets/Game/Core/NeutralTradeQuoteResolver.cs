using System;

namespace LittleCiv.Core
{
    public enum NeutralTradeQuoteFailure
    {
        None = 0,
        InvalidParticipant = 1,
        UnsupportedSpecialization = 2,
        TargetOccupied = 3,
        RouteBlocked = 4,
        InsufficientGold = 5
    }

    public sealed class NeutralTradeQuote
    {
        public bool IsAvailable;
        public NeutralTradeQuoteFailure Failure;
        public EntityId PlayerId;
        public EntityId SourceCityId;
        public EntityId TargetCityId;
        public NeutralCitySpecialization Specialization;
        public NeutralDevelopmentStage DevelopmentStage;
        public int Favor;
        public TileResourceType ReceivedResource;
        public int ResourceAmount;
        public int BaseGoldCost;
        public int GoldPerAdditionalDistance;
        public int DistanceGoldCost;
        public int TotalGoldCost;
        public TradeRouteResult Route;
    }

    public static class NeutralTradeQuoteResolver
    {
        public static NeutralTradeQuote Quote(
            GameState state, EntityId playerId, EntityId sourceCityId, EntityId targetCityId)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            var quote = new NeutralTradeQuote
            {
                PlayerId = playerId, SourceCityId = sourceCityId, TargetCityId = targetCityId,
                Failure = NeutralTradeQuoteFailure.InvalidParticipant
            };
            var player = state.Players.Find(item => item.Id == playerId);
            var source = state.Cities.Find(item => item.Id == sourceCityId);
            var target = state.Cities.Find(item => item.Id == targetCityId);
            if (player == null || player.Slot == PlayerSlot.Neutral || source == null ||
                target == null || source.OwnerId != playerId || !IsNeutral(state, target)) return quote;

            quote.Specialization = target.NeutralSpecialization;
            if (target.NeutralSpecialization != NeutralCitySpecialization.Science &&
                target.NeutralSpecialization != NeutralCitySpecialization.Culture)
            {
                quote.Failure = NeutralTradeQuoteFailure.UnsupportedSpecialization;
                return quote;
            }
            if (GovernmentController(state, target) != target.OwnerId)
            {
                quote.Failure = NeutralTradeQuoteFailure.TargetOccupied;
                return quote;
            }

            quote.Route = TradeRouteResolver.Find(state, playerId, sourceCityId, targetCityId);
            if (!quote.Route.IsReachable)
            {
                quote.Failure = NeutralTradeQuoteFailure.RouteBlocked;
                return quote;
            }

            quote.DevelopmentStage = NeutralCityRules.DevelopmentStage(state, target);
            quote.Favor = NeutralCityRules.Favor(target, playerId);
            quote.ReceivedResource = target.NeutralSpecialization == NeutralCitySpecialization.Science
                ? TileResourceType.Science : TileResourceType.Culture;
            var scale = (int)quote.DevelopmentStage;
            if (quote.Favor <= -3)
            {
                quote.BaseGoldCost = 3 * scale;
                quote.ResourceAmount = scale;
                quote.GoldPerAdditionalDistance = 2 * scale;
            }
            else if (quote.Favor < 3)
            {
                quote.BaseGoldCost = 2 * scale;
                quote.ResourceAmount = scale;
                quote.GoldPerAdditionalDistance = scale;
            }
            else if (quote.Favor == 3)
            {
                quote.BaseGoldCost = scale;
                quote.ResourceAmount = scale;
                quote.GoldPerAdditionalDistance = scale;
            }
            else
            {
                quote.BaseGoldCost = scale;
                quote.ResourceAmount = 2 * scale;
                quote.GoldPerAdditionalDistance = scale;
            }
            quote.DistanceGoldCost = quote.Route.AdditionalDistance * quote.GoldPerAdditionalDistance;
            quote.TotalGoldCost = quote.BaseGoldCost + quote.DistanceGoldCost;
            if (source.Gold < quote.TotalGoldCost)
            {
                quote.Failure = NeutralTradeQuoteFailure.InsufficientGold;
                return quote;
            }
            quote.IsAvailable = true;
            quote.Failure = NeutralTradeQuoteFailure.None;
            return quote;
        }

        private static bool IsNeutral(GameState state, CityState city)
        {
            var owner = state.Players.Find(item => item.Id == city.OwnerId);
            return owner != null && owner.Slot == PlayerSlot.Neutral;
        }

        private static EntityId GovernmentController(GameState state, CityState city)
        {
            var government = state.Districts.Find(item => item.CityId == city.Id &&
                item.Type == DistrictType.Government);
            return government == null ? default(EntityId) : government.ControllerId;
        }
    }
}
