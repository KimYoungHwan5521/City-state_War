using System;
using System.Collections.Generic;

namespace LittleCiv.Core
{
    public sealed class CultureChangeRecord
    {
        public EntityId CultureOwnerId;
        public EntityId CityId;
        public int PreferredCitizenDelta;
        public int ConversionProgress;
        public int ReversionProgress;
    }

    public static class CultureConversionResolver
    {
        public static List<CultureChangeRecord> AdvancePlayerCities(GameState state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            var result = new List<CultureChangeRecord>();
            var one = state.Players.Find(item => item.Slot == PlayerSlot.PlayerOne);
            var two = state.Players.Find(item => item.Slot == PlayerSlot.PlayerTwo);
            if (one == null || two == null) return result;
            var cityOne = FindHomeCity(state, one.Id);
            var cityTwo = FindHomeCity(state, two.Id);
            if (cityOne == null || cityTwo == null) return result;
            var difference = cityOne.LastCultureProduction - cityTwo.LastCultureProduction;
            if (difference > 0) ApplyAdvantage(cityOne, cityTwo, one.Id, two.Id, difference, result);
            else if (difference < 0) ApplyAdvantage(cityTwo, cityOne, two.Id, one.Id, -difference, result);
            return result;
        }

        public static List<CultureChangeRecord> ApplyAdvantage(
            CityState winnerHome, CityState loserHome, EntityId winnerCulture,
            EntityId loserCulture, int amount)
        {
            var result = new List<CultureChangeRecord>();
            ApplyAdvantage(winnerHome, loserHome, winnerCulture, loserCulture,
                Math.Max(0, amount), result);
            return result;
        }

        public static List<CultureChangeRecord> ApplyForeignInfluence(
            CityState city, EntityId cultureOwner, int amount)
        {
            if (city == null) throw new ArgumentNullException(nameof(city));
            var result = new List<CultureChangeRecord>();
            CityCultureRules.Normalize(city);
            if (amount > 0 && cultureOwner.IsValid && cultureOwner != city.OwnerId)
                ConvertForeignCity(city, cultureOwner, amount, result);
            return result;
        }

        public static List<CultureChangeRecord> ApplyNativeResistance(CityState city, int amount)
        {
            if (city == null) throw new ArgumentNullException(nameof(city));
            var result = new List<CultureChangeRecord>();
            CityCultureRules.Normalize(city);
            amount = Math.Max(0, amount);
            while (amount > 0)
            {
                var threat = StrongestForeignInfluence(city);
                if (threat == null) break;
                var remaining = ReclaimHome(city, threat.CultureOwnerId, amount, result);
                if (remaining == amount) break;
                amount = remaining;
            }
            return result;
        }

        private static void ApplyAdvantage(
            CityState winnerHome, CityState loserHome, EntityId winnerCulture,
            EntityId loserCulture, int amount, List<CultureChangeRecord> result)
        {
            if (amount <= 0) return;
            CityCultureRules.Normalize(winnerHome);
            CityCultureRules.Normalize(loserHome);
            amount = ReclaimHome(winnerHome, loserCulture, amount, result);
            if (amount > 0) ConvertForeignCity(loserHome, winnerCulture, amount, result);
        }

        private static int ReclaimHome(CityState city, EntityId foreignCulture, int amount,
            List<CultureChangeRecord> result)
        {
            var influence = CityCultureRules.GetOrCreate(city, foreignCulture);
            var before = influence.PreferredCitizens;
            var cancelled = Math.Min(amount, influence.ConversionProgress);
            influence.ConversionProgress -= cancelled;
            amount -= cancelled;
            if (amount > 0 && influence.PreferredCitizens > 0)
            {
                influence.ReversionProgress += amount;
                amount = 0;
                while (influence.ReversionProgress >= CityCultureRules.ProgressPerCitizen &&
                       influence.PreferredCitizens > 0)
                {
                    influence.ReversionProgress -= CityCultureRules.ProgressPerCitizen;
                    influence.PreferredCitizens--;
                }
                if (influence.PreferredCitizens == 0)
                {
                    amount = influence.ReversionProgress;
                    influence.ReversionProgress = 0;
                }
            }
            if (before != influence.PreferredCitizens || cancelled > 0 || influence.ReversionProgress > 0)
                AddRecord(result, city, influence, influence.PreferredCitizens - before);
            return amount;
        }

        private static void ConvertForeignCity(CityState city, EntityId cultureOwner, int amount,
            List<CultureChangeRecord> result)
        {
            var influence = CityCultureRules.GetOrCreate(city, cultureOwner);
            var before = influence.PreferredCitizens;
            var cancelled = Math.Min(amount, influence.ReversionProgress);
            influence.ReversionProgress -= cancelled;
            amount -= cancelled;
            influence.ConversionProgress += amount;
            while (influence.ConversionProgress >= CityCultureRules.ProgressPerCitizen &&
                   CityCultureRules.NativeCitizens(city) > 0)
            {
                influence.ConversionProgress -= CityCultureRules.ProgressPerCitizen;
                influence.PreferredCitizens++;
            }
            if (CityCultureRules.NativeCitizens(city) == 0) influence.ConversionProgress = 0;
            if (before != influence.PreferredCitizens || cancelled > 0 || amount > 0)
                AddRecord(result, city, influence, influence.PreferredCitizens - before);
        }

        private static void AddRecord(List<CultureChangeRecord> result, CityState city,
            CultureInfluenceState influence, int delta)
        {
            result.Add(new CultureChangeRecord
            {
                CultureOwnerId = influence.CultureOwnerId, CityId = city.Id,
                PreferredCitizenDelta = delta, ConversionProgress = influence.ConversionProgress,
                ReversionProgress = influence.ReversionProgress
            });
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

        private static CultureInfluenceState StrongestForeignInfluence(CityState city)
        {
            CultureInfluenceState result = null;
            for (var index = 0; index < city.CultureInfluences.Count; index++)
            {
                var candidate = city.CultureInfluences[index];
                if (candidate.PreferredCitizens <= 0 && candidate.ConversionProgress <= 0) continue;
                if (result == null || candidate.PreferredCitizens > result.PreferredCitizens ||
                    (candidate.PreferredCitizens == result.PreferredCitizens &&
                     candidate.ConversionProgress > result.ConversionProgress) ||
                    (candidate.PreferredCitizens == result.PreferredCitizens &&
                     candidate.ConversionProgress == result.ConversionProgress &&
                     candidate.CultureOwnerId.CompareTo(result.CultureOwnerId) < 0))
                    result = candidate;
            }
            return result;
        }
    }
}
