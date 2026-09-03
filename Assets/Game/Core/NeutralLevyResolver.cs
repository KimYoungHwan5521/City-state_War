using System;
using System.Collections.Generic;
using System.Linq;

namespace LittleCiv.Core
{
    public enum LevyQuoteFailure
    {
        None = 0, InvalidParticipant = 1, NotMilitaryCity = 2,
        Hostile = 3, CityOccupied = 4, AlreadyLevied = 5,
        RouteBlocked = 6, NoUnits = 7, InsufficientGold = 8
    }

    public sealed class LevyQuote
    {
        public bool IsAvailable;
        public LevyQuoteFailure Failure;
        public int Favor;
        public int FullUnitValue;
        public int BasePrice;
        public TradeRouteResult Route;
        public readonly List<EntityId> UnitIds = new List<EntityId>();
    }

    public sealed class LevyReturnRecord
    {
        public EntityId LevyId;
        public EntityId MilitaryCityId;
        public EntityId PlayerId;
        public int ReturnedUnits;
        public int DisbandedUnits;
        public bool TerminatedEarly;
    }

    public static class NeutralLevyResolver
    {
        public const int DurationTurns = 30;

        public static LevyQuote Quote(GameState state, EntityId playerId,
            EntityId paymentCityId, EntityId militaryCityId)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            var quote = new LevyQuote { Failure = LevyQuoteFailure.InvalidParticipant };
            var player = state.Players.Find(item => item.Id == playerId);
            var payment = state.Cities.Find(item => item.Id == paymentCityId);
            var military = state.Cities.Find(item => item.Id == militaryCityId);
            if (player == null || player.Slot == PlayerSlot.Neutral || payment == null ||
                payment.OwnerId != playerId || military == null) return quote;
            if (military.NeutralSpecialization != NeutralCitySpecialization.Military)
            {
                quote.Failure = LevyQuoteFailure.NotMilitaryCity;
                return quote;
            }
            quote.Favor = NeutralCityRules.Favor(military, playerId);
            if (quote.Favor <= -3) { quote.Failure = LevyQuoteFailure.Hostile; return quote; }
            if (military.OccupyingPlayerId.IsValid)
            { quote.Failure = LevyQuoteFailure.CityOccupied; return quote; }
            if (state.Levies.Exists(item => item.MilitaryCityId == militaryCityId))
            { quote.Failure = LevyQuoteFailure.AlreadyLevied; return quote; }
            quote.Route = TradeRouteResolver.Find(state, playerId, paymentCityId, militaryCityId);
            if (!quote.Route.IsReachable) { quote.Failure = LevyQuoteFailure.RouteBlocked; return quote; }

            var units = state.Units.FindAll(item => item.OwnerId == military.OwnerId &&
                item.HomeCityId == military.Id && item.HitPoints > 0);
            units.Sort((left, right) => left.Id.CompareTo(right.Id));
            var government = state.Districts.Find(item => item.CityId == military.Id &&
                item.Type == DistrictType.Government);
            UnitState retained = null;
            if (government != null)
            {
                retained = units.FindAll(item => item.TileId == government.TileId)
                    .OrderByDescending(item => UnitRules.Attack(item.Type))
                    .ThenByDescending(item => item.HitPoints)
                    .ThenBy(item => item.Id)
                    .FirstOrDefault();
            }
            for (var index = 0; index < units.Count; index++)
            {
                if (retained != null && units[index].Id == retained.Id) continue;
                quote.UnitIds.Add(units[index].Id);
                quote.FullUnitValue += UnitRules.TrainingGold(units[index].Type);
            }
            if (quote.UnitIds.Count == 0) { quote.Failure = LevyQuoteFailure.NoUnits; return quote; }
            quote.BasePrice = quote.Favor >= 4 ? DivideRoundUp(quote.FullUnitValue, 4) :
                quote.Favor >= 3 ? DivideRoundUp(quote.FullUnitValue, 2) : quote.FullUnitValue;
            if (payment.Gold < quote.BasePrice)
            { quote.Failure = LevyQuoteFailure.InsufficientGold; return quote; }
            quote.IsAvailable = true;
            quote.Failure = LevyQuoteFailure.None;
            return quote;
        }

        public static bool TryStart(GameState state, EntityId playerId,
            EntityId paymentCityId, EntityId militaryCityId, int finalPrice, out LevyState levy)
        {
            levy = null;
            var quote = Quote(state, playerId, paymentCityId, militaryCityId);
            var payment = state.Cities.Find(item => item.Id == paymentCityId);
            if (!quote.IsAvailable || finalPrice < quote.BasePrice || payment.Gold < finalPrice) return false;
            levy = new LevyState
            {
                Id = state.AllocateId(), MilitaryCityId = militaryCityId,
                PlayerId = playerId, PaymentCityId = paymentCityId,
                StartTurn = state.TurnNumber,
                EndTurnExclusive = state.TurnNumber + DurationTurns,
                PaidGold = finalPrice
            };
            payment.Gold -= finalPrice;
            for (var index = 0; index < quote.UnitIds.Count; index++)
            {
                var unit = state.Units.Find(item => item.Id == quote.UnitIds[index]);
                if (unit == null) continue;
                var maximumFood = UnitRules.FoodCapacity(state, unit);
                levy.Units.Add(new LevyUnitState
                    { UnitId = unit.Id, OriginalHomeCityId = unit.HomeCityId });
                unit.OwnerId = playerId;
                unit.HomeCityId = paymentCityId;
                unit.CarriedFood = maximumFood;
            }
            state.Levies.Add(levy);
            var military = state.Cities.Find(item => item.Id == militaryCityId);
            var favor = NeutralCityRules.Favor(military, playerId);
            if (favor < 3) NeutralCityRules.SetFavor(military, playerId, favor + 1);
            return true;
        }

        public static List<LevyReturnRecord> ReturnExpired(GameState state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            var records = new List<LevyReturnRecord>();
            var expired = state.Levies.FindAll(item => item.EndTurnExclusive <= state.TurnNumber);
            expired.Sort((left, right) => left.Id.CompareTo(right.Id));
            for (var index = 0; index < expired.Count; index++)
            {
                var levy = expired[index];
                var city = state.Cities.Find(item => item.Id == levy.MilitaryCityId);
                var government = city == null ? null : state.Districts.Find(item =>
                    item.CityId == city.Id && item.Type == DistrictType.Government);
                var canReturn = city != null && !city.OccupyingPlayerId.IsValid &&
                                government != null && government.ControllerId == city.OwnerId;
                var record = new LevyReturnRecord
                    { LevyId = levy.Id, MilitaryCityId = levy.MilitaryCityId, PlayerId = levy.PlayerId };
                for (var unitIndex = 0; unitIndex < levy.Units.Count; unitIndex++)
                {
                    var leased = levy.Units[unitIndex];
                    var unit = state.Units.Find(item => item.Id == leased.UnitId &&
                        item.OwnerId == levy.PlayerId);
                    if (unit == null) continue;
                    if (!canReturn)
                    {
                        state.Units.Remove(unit);
                        record.DisbandedUnits++;
                        continue;
                    }
                    unit.OwnerId = city.OwnerId;
                    unit.HomeCityId = leased.OriginalHomeCityId;
                    unit.RemainingMovement = 0;
                    unit.HasAutomaticDefense = false;
                    // Returning to a home city starts a fresh starvation grace period.
                    unit.IsStarving = false;
                    record.ReturnedUnits++;
                }
                state.Levies.Remove(levy);
                records.Add(record);
            }
            return records;
        }

        public static int ReconcileDestroyedUnits(GameState state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            var removedLevies = 0;
            for (var levyIndex = state.Levies.Count - 1; levyIndex >= 0; levyIndex--)
            {
                var levy = state.Levies[levyIndex];
                levy.Units.RemoveAll(item => !state.Units.Exists(unit =>
                    unit.Id == item.UnitId && unit.OwnerId == levy.PlayerId && unit.HitPoints > 0));
                if (levy.Units.Count > 0) continue;
                state.Levies.RemoveAt(levyIndex);
                removedLevies++;
            }
            return removedLevies;
        }

        public static List<LevyReturnRecord> DisbandInvalidOrigins(GameState state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            var records = new List<LevyReturnRecord>();
            var invalid = state.Levies.FindAll(item => !CanReturnToOrigin(state, item.MilitaryCityId));
            invalid.Sort((left, right) => left.Id.CompareTo(right.Id));
            for (var index = 0; index < invalid.Count; index++)
            {
                var levy = invalid[index];
                var record = new LevyReturnRecord
                {
                    LevyId = levy.Id, MilitaryCityId = levy.MilitaryCityId,
                    PlayerId = levy.PlayerId, TerminatedEarly = true
                };
                for (var unitIndex = 0; unitIndex < levy.Units.Count; unitIndex++)
                {
                    var unit = state.Units.Find(item => item.Id == levy.Units[unitIndex].UnitId &&
                        item.OwnerId == levy.PlayerId);
                    if (unit == null) continue;
                    state.Units.Remove(unit);
                    record.DisbandedUnits++;
                }
                state.Levies.Remove(levy);
                records.Add(record);
            }
            return records;
        }

        public static bool IsProtectedCityTile(
            GameState state, EntityId playerId, EntityId tileId, EntityId movingUnitId = default)
        {
            var tile = state.Tiles.Find(item => item.Id == tileId);
            if (tile == null) return false;
            var levy = state.Levies.Find(item => item.PlayerId == playerId &&
                item.MilitaryCityId == tile.CityId && item.EndTurnExclusive > state.TurnNumber);
            return levy != null;
        }

        private static int DivideRoundUp(int value, int divisor) =>
            (value + divisor - 1) / divisor;

        private static bool CanReturnToOrigin(GameState state, EntityId militaryCityId)
        {
            var city = state.Cities.Find(item => item.Id == militaryCityId);
            if (city == null || city.OccupyingPlayerId.IsValid) return false;
            var government = state.Districts.Find(item => item.CityId == militaryCityId &&
                item.Type == DistrictType.Government);
            return government != null && government.ControllerId == city.OwnerId;
        }
    }
}
