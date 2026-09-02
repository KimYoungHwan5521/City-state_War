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

    public static class NeutralCultureResolver
    {
        public const int BaseResistance = 2;

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
                    var home = FindHomeCity(state, player.Id);
                    if (home != null) scores.Add(new Score
                    {
                        CultureId = player.Id,
                        Value = EffectiveInfluence(home, city)
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
            var distance = HexCoord.Distance(new HexCoord(playerCity.WorldQ, playerCity.WorldR),
                new HexCoord(neutralCity.WorldQ, neutralCity.WorldR));
            return playerCity.LastCultureProduction - Math.Max(0, distance - 1);
        }

        private static void UpdateSubject(CityState city, List<PlayerState> majorPlayers)
        {
            var previous = city.CultureSubjectToId;
            city.CultureSubjectToId = default;
            for (var index = 0; index < majorPlayers.Count; index++)
            {
                if (!CultureVictoryConditionResolver.HasForeignMajority(city, majorPlayers[index].Id)) continue;
                city.CultureSubjectToId = majorPlayers[index].Id;
                break;
            }
            if (previous.IsValid && previous != city.CultureSubjectToId)
                NeutralCityRules.SetFavor(city, previous, 2);
            if (city.CultureSubjectToId.IsValid)
                NeutralCityRules.SetFavor(city, city.CultureSubjectToId, 3);
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
