using System;

namespace LittleCiv.Core
{
    public enum CommerceTradeQuoteFailure
    {
        None = 0,
        InvalidParticipant = 1,
        NotCommerceCity = 2,
        InvalidResource = 3,
        TargetOccupied = 4,
        RouteBlocked = 5,
        ShippingConsumesPayment = 6,
        InsufficientFood = 7,
        NoProjectedProduction = 8
    }

    public sealed class CommerceTradeQuote
    {
        public bool IsAvailable;
        public CommerceTradeQuoteFailure Failure;
        public EntityId PlayerId;
        public EntityId SourceCityId;
        public EntityId TargetCityId;
        public TileResourceType OfferedResource;
        public NeutralDevelopmentStage DevelopmentStage;
        public int Favor;
        public int RequiredResourceAmount;
        public int AvailableResourceAmount;
        public int BaseGoldPayment;
        public int ShippingResourcePerDistance;
        public int ShippingResourceCost;
        public int NetGoldPayment;
        public TradeRouteResult Route;
    }

    public static class CommerceTradeQuoteResolver
    {
        public static CommerceTradeQuote Quote(GameState state, EntityId playerId,
            EntityId sourceCityId, EntityId targetCityId, TileResourceType offeredResource)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            var quote = new CommerceTradeQuote
            {
                PlayerId = playerId, SourceCityId = sourceCityId, TargetCityId = targetCityId,
                OfferedResource = offeredResource,
                Failure = CommerceTradeQuoteFailure.InvalidParticipant
            };
            var player = state.Players.Find(item => item.Id == playerId);
            var source = state.Cities.Find(item => item.Id == sourceCityId);
            var target = state.Cities.Find(item => item.Id == targetCityId);
            if (player == null || player.Slot == PlayerSlot.Neutral || source == null ||
                source.OwnerId != playerId || target == null || !IsNeutral(state, target)) return quote;
            if (target.NeutralSpecialization != NeutralCitySpecialization.Commerce)
            {
                quote.Failure = CommerceTradeQuoteFailure.NotCommerceCity;
                return quote;
            }
            if (offeredResource != TileResourceType.Food &&
                offeredResource != TileResourceType.Science && offeredResource != TileResourceType.Culture)
            {
                quote.Failure = CommerceTradeQuoteFailure.InvalidResource;
                return quote;
            }
            if (GovernmentController(state, target) != target.OwnerId)
            {
                quote.Failure = CommerceTradeQuoteFailure.TargetOccupied;
                return quote;
            }

            quote.Route = TradeRouteResolver.Find(state, playerId, sourceCityId, targetCityId);
            if (!quote.Route.IsReachable)
            {
                quote.Failure = CommerceTradeQuoteFailure.RouteBlocked;
                return quote;
            }
            quote.DevelopmentStage = NeutralCityRules.DevelopmentStage(state, target);
            quote.Favor = NeutralCityRules.Favor(target, playerId);
            var scale = (int)quote.DevelopmentStage;
            if (quote.Favor <= -3)
            {
                quote.RequiredResourceAmount = 3 * scale;
                quote.BaseGoldPayment = scale;
                quote.ShippingResourcePerDistance = 2 * scale;
            }
            else if (quote.Favor < 3)
            {
                quote.RequiredResourceAmount = 2 * scale;
                quote.BaseGoldPayment = scale;
                quote.ShippingResourcePerDistance = scale;
            }
            else if (quote.Favor == 3)
            {
                quote.RequiredResourceAmount = scale;
                quote.BaseGoldPayment = scale;
                quote.ShippingResourcePerDistance = scale;
            }
            else
            {
                quote.RequiredResourceAmount = scale;
                quote.BaseGoldPayment = 2 * scale;
                quote.ShippingResourcePerDistance = scale;
            }
            quote.ShippingResourceCost = quote.Route.AdditionalDistance * quote.ShippingResourcePerDistance;
            quote.RequiredResourceAmount += quote.ShippingResourceCost;
            quote.NetGoldPayment = quote.BaseGoldPayment;

            quote.AvailableResourceAmount = AvailableAmount(state, source, offeredResource);
            if (offeredResource == TileResourceType.Food &&
                quote.AvailableResourceAmount < quote.RequiredResourceAmount)
            {
                quote.Failure = CommerceTradeQuoteFailure.InsufficientFood;
                return quote;
            }
            if (offeredResource != TileResourceType.Food && quote.AvailableResourceAmount <= 0)
            {
                quote.Failure = CommerceTradeQuoteFailure.NoProjectedProduction;
                return quote;
            }
            quote.IsAvailable = true;
            quote.Failure = CommerceTradeQuoteFailure.None;
            return quote;
        }

        private static int AvailableAmount(GameState state, CityState source, TileResourceType resource)
        {
            if (resource == TileResourceType.Food) return source.StoredFood;
            var economy = CityEconomyResolver.CalculateBreakdown(state, source);
            return resource == TileResourceType.Science ? economy.Science.Total : economy.Culture.Total;
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
