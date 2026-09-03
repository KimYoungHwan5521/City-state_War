using System;
using System.Collections.Generic;

namespace LittleCiv.Core
{
    public sealed class NeutralCultureRecord
    {
        public EntityId CityId;
        public EntityId WinningCultureId;
        public int WinningScore;
        public int SecondScore;
        public int AppliedInfluence;
        public EntityId SubjectToId;
    }

    public sealed class NeutralCultureInfluenceBreakdown
    {
        public EntityId SourceCityId;
        public int SourceCulture;
        public int Distance;
        public int DistancePenalty;
        public int EffectiveInfluence;
    }

    public static class NeutralCultureResolver
    {
        public const int BaseResistance = 2;
        public const int DistancePenaltyPerTile = 3;

        public static List<NeutralCultureRecord> Advance(GameState state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            var result = new List<NeutralCultureRecord>();
            var majorPlayers = state.Players.FindAll(item => item.Slot != PlayerSlot.Neutral);
            majorPlayers.Sort((left, right) => left.Id.CompareTo(right.Id));
            for (var cityIndex = 0; cityIndex < state.Cities.Count; cityIndex++)
            {
                var city = state.Cities[cityIndex];
                var owner = state.Players.Find(item => item.Id == city.OwnerId);
                if (owner == null || owner.Slot != PlayerSlot.Neutral) continue;
                var scores = new List<Score>
                {
                    new Score { CultureId = default, Value = Resistance(state, city) }
                };
                for (var playerIndex = 0; playerIndex < majorPlayers.Count; playerIndex++)
                {
                    var player = majorPlayers[playerIndex];
                    if (FindHomeCity(state, player.Id) != null) scores.Add(new Score
                    {
                        CultureId = player.Id,
                        Value = AdjustedInfluence(state, player.Id, city)
                    });
                }
                scores.Sort(CompareScores);
                var top = scores[0];
                var second = scores.Count > 1 ? scores[1] : top;
                var tied = top.Value == second.Value;
                var amount = tied ? 0 : Math.Max(0, top.Value - second.Value);
                if (amount > 0)
                {
                    if (top.CultureId.IsValid)
                        CultureConversionResolver.ApplyForeignInfluence(city, top.CultureId, amount);
                    else
                        CultureConversionResolver.ApplyNativeResistance(city, amount);
                }
                UpdateSubject(city, majorPlayers);
                result.Add(new NeutralCultureRecord
                {
                    CityId = city.Id, WinningCultureId = tied ? default : top.CultureId,
                    WinningScore = top.Value, SecondScore = second.Value,
                    AppliedInfluence = amount, SubjectToId = city.CultureSubjectToId
                });
            }
            return result;
        }

        public static int Resistance(GameState state, CityState city)
        {
            if (state == null || city == null) return BaseResistance;
            var government = state.Districts.Find(item => item.CityId == city.Id &&
                item.Type == DistrictType.Government);
            var governmentCulture = government != null && government.ControllerId == city.OwnerId &&
                                    government.IsOperational && government.AssignedCitizens > 0 &&
                                    government.RemainingConstructionTurns <= 0 ?
                CityEconomyResolver.GovernmentCulture : 0;
            return BaseResistance + Math.Max(0, city.LastCultureProduction - governmentCulture);
        }

        public static int EffectiveInfluence(CityState playerCity, CityState neutralCity)
        {
            if (playerCity == null || neutralCity == null) return 0;
            var distance = HexCoord.Distance(new HexCoord(playerCity.WorldQ, playerCity.WorldR),
                new HexCoord(neutralCity.WorldQ, neutralCity.WorldR));
            return playerCity.LastCultureProduction -
                   Math.Max(0, distance - 1) * DistancePenaltyPerTile;
        }

        public static int EffectiveInfluence(GameState state, EntityId playerId, CityState neutralCity)
        {
            return InfluenceBreakdown(state, playerId, neutralCity).EffectiveInfluence;
        }

        public static NeutralCultureInfluenceBreakdown InfluenceBreakdown(
            GameState state, EntityId playerId, CityState neutralCity)
        {
            var result = new NeutralCultureInfluenceBreakdown();
            if (state == null || neutralCity == null || !playerId.IsValid) return result;
            var home = FindHomeCity(state, playerId);
            if (home != null) ConsiderSource(result, home, neutralCity);
            for (var index = 0; index < state.Cities.Count; index++)
            {
                var relay = state.Cities[index];
                if (relay.Id == neutralCity.Id || relay.CultureSubjectToId != playerId) continue;
                ConsiderSource(result, relay, neutralCity);
            }
            return result;
        }

        private static void ConsiderSource(NeutralCultureInfluenceBreakdown result,
            CityState source, CityState target)
        {
            var distance = HexCoord.Distance(new HexCoord(source.WorldQ, source.WorldR),
                new HexCoord(target.WorldQ, target.WorldR));
            var penalty = Math.Max(0, distance - 1) * DistancePenaltyPerTile;
            var effective = source.LastCultureProduction - penalty;
            if (result.SourceCityId.IsValid && effective <= result.EffectiveInfluence) return;
            result.SourceCityId = source.Id;
            result.SourceCulture = source.LastCultureProduction;
            result.Distance = distance;
            result.DistancePenalty = penalty;
            result.EffectiveInfluence = effective;
        }

        public static int RelationshipResistance(CityState city, EntityId playerId)
        {
            var favor = NeutralCityRules.Favor(city, playerId);
            if (favor <= -3) return 10;
            return favor >= 3 ? 0 : 2;
        }

        public static int AdjustedInfluence(GameState state, EntityId playerId, CityState neutralCity)
        {
            return EffectiveInfluence(state, playerId, neutralCity) + BaseResistance -
                   RelationshipResistance(neutralCity, playerId);
        }

        private static void UpdateSubject(CityState city, List<PlayerState> majorPlayers)
        {
            var previous = city.CultureSubjectToId;
            EntityId majority = default;
            for (var index = 0; index < majorPlayers.Count; index++)
            {
                if (!CultureVictoryConditionResolver.HasForeignMajority(city, majorPlayers[index].Id)) continue;
                majority = majorPlayers[index].Id;
                break;
            }

            if (previous.IsValid && previous != majority)
            {
                city.CultureSubjectToId = default;
                NeutralCityRules.SetFavor(city, previous, 3);
            }
            if (!majority.IsValid) return;

            var favor = NeutralCityRules.Favor(city, majority);
            if (favor < 3)
            {
                city.CultureSubjectToId = default;
                NeutralCityRules.SetFavor(city, majority, favor + 1);
                return;
            }
            city.CultureSubjectToId = majority;
            NeutralCityRules.SetFavor(city, majority, 4);
        }

        private static CityState FindHomeCity(GameState state, EntityId ownerId)
        {
            CityState result = null;
            for (var index = 0; index < state.Cities.Count; index++)
            {
                var city = state.Cities[index];
                if (city.OwnerId == ownerId && (result == null || city.Id.CompareTo(result.Id) < 0)) result = city;
            }
            return result;
        }

        private static int CompareScores(Score left, Score right)
        {
            var value = right.Value.CompareTo(left.Value);
            if (value != 0) return value;
            return left.CultureId.CompareTo(right.CultureId);
        }

        private sealed class Score
        {
            public EntityId CultureId;
            public int Value;
        }
    }
}
