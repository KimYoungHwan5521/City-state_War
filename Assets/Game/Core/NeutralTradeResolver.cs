using System;
using System.Collections.Generic;

namespace LittleCiv.Core
{
    public sealed class NeutralTradeExecution
    {
        public EntityId PlayerId;
        public EntityId SourceCityId;
        public EntityId TargetCityId;
        public TileResourceType ResourceType;
        public int ResourceAmount;
        public int GoldAmount;
        public bool IsSale;
        public bool IsDeferred;
    }

    public static class NeutralTradeResolver
    {
        public static bool TryExecute(GameState state, GameCommand command,
            out NeutralTradeExecution execution)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (command == null) throw new ArgumentNullException(nameof(command));
            execution = null;
            if (!Enum.IsDefined(typeof(TileResourceType), command.PrimaryValue)) return false;
            var target = state.Cities.Find(item => item.Id == command.TargetId);
            var source = state.Cities.Find(item => item.Id == command.SubjectId);
            if (target == null || source == null) return false;
            var resource = (TileResourceType)command.PrimaryValue;

            if (target.NeutralSpecialization == NeutralCitySpecialization.Science ||
                target.NeutralSpecialization == NeutralCitySpecialization.Culture)
            {
                var quote = NeutralTradeQuoteResolver.Quote(state, command.PlayerId,
                    command.SubjectId, command.TargetId);
                if (!quote.IsAvailable || resource != quote.ReceivedResource) return false;
                source.Gold -= quote.TotalGoldCost;
                AddReservation(state, command, resource, quote.ResourceAmount, 0, false);
                execution = new NeutralTradeExecution
                {
                    PlayerId = command.PlayerId, SourceCityId = source.Id, TargetCityId = target.Id,
                    ResourceType = resource, ResourceAmount = quote.ResourceAmount,
                    GoldAmount = quote.TotalGoldCost, IsDeferred = true
                };
            }
            else if (target.NeutralSpecialization == NeutralCitySpecialization.Commerce)
            {
                var quote = CommerceTradeQuoteResolver.Quote(state, command.PlayerId,
                    command.SubjectId, command.TargetId, resource);
                if (!quote.IsAvailable) return false;
                if (resource == TileResourceType.Food)
                {
                    source.StoredFood -= quote.RequiredResourceAmount;
                    source.Gold += quote.NetGoldPayment;
                }
                else
                {
                    AddReservation(state, command, resource, quote.RequiredResourceAmount,
                        quote.NetGoldPayment, true);
                }
                execution = new NeutralTradeExecution
                {
                    PlayerId = command.PlayerId, SourceCityId = source.Id, TargetCityId = target.Id,
                    ResourceType = resource, ResourceAmount = quote.RequiredResourceAmount,
                    GoldAmount = quote.NetGoldPayment, IsSale = true,
                    IsDeferred = resource != TileResourceType.Food
                };
            }
            else return false;

            var favor = NeutralCityRules.Favor(target, command.PlayerId);
            if (favor < 3) NeutralCityRules.SetFavor(target, command.PlayerId, favor + 1);
            return true;
        }

        public static List<NeutralTradeExecution> ApplyPending(GameState state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            var result = new List<NeutralTradeExecution>();
            var pending = state.TradeReservations.FindAll(item => item.ApplyTurn <= state.TurnNumber);
            pending.Sort((left, right) => left.Id.CompareTo(right.Id));
            for (var index = 0; index < pending.Count; index++)
            {
                var reservation = pending[index];
                var city = state.Cities.Find(item => item.Id == reservation.SourceCityId &&
                    item.OwnerId == reservation.PlayerId);
                if (city == null)
                {
                    state.TradeReservations.Remove(reservation);
                    continue;
                }
                var delivered = reservation.ResourceAmount;
                var gold = 0;
                if (reservation.IsSale)
                {
                    var production = reservation.ResourceType == TileResourceType.Science
                        ? city.LastScienceProduction : city.LastCultureProduction;
                    delivered = Math.Min(reservation.ResourceAmount, Math.Max(0, production));
                    if (reservation.ResourceType == TileResourceType.Science)
                    {
                        city.LastScienceProduction -= delivered;
                        city.ResearchPoints = Math.Max(0, city.ResearchPoints - delivered);
                    }
                    else city.LastCultureProduction -= delivered;
                    if (delivered > 0)
                    {
                        gold = Math.Min(reservation.NetGoldPayment,
                            (reservation.NetGoldPayment * delivered + reservation.ResourceAmount - 1) /
                            reservation.ResourceAmount);
                        city.Gold += gold;
                    }
                }
                else if (reservation.ResourceType == TileResourceType.Science)
                {
                    city.LastScienceProduction += delivered;
                    city.ResearchPoints += delivered;
                }
                else city.LastCultureProduction += delivered;
                result.Add(new NeutralTradeExecution
                {
                    PlayerId = reservation.PlayerId, SourceCityId = city.Id,
                    TargetCityId = reservation.TargetCityId, ResourceType = reservation.ResourceType,
                    ResourceAmount = delivered, GoldAmount = gold,
                    IsSale = reservation.IsSale, IsDeferred = false
                });
                state.TradeReservations.Remove(reservation);
            }
            return result;
        }

        private static void AddReservation(GameState state, GameCommand command,
            TileResourceType resource, int amount, int gold, bool sale)
        {
            state.TradeReservations.Add(new TradeReservationState
            {
                Id = state.AllocateId(), PlayerId = command.PlayerId,
                SourceCityId = command.SubjectId, TargetCityId = command.TargetId,
                ResourceType = resource, ResourceAmount = amount, NetGoldPayment = gold,
                ApplyTurn = state.TurnNumber + 1, IsSale = sale
            });
        }
    }
}
