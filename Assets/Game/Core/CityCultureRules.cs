using System;
using System.Collections.Generic;

namespace LittleCiv.Core
{
    public static class CityCultureRules
    {
        public const int ProgressPerCitizen = 10;

        public static int PreferredCitizens(CityState city, EntityId cultureOwnerId)
        {
            if (city == null) throw new ArgumentNullException(nameof(city));
            if (cultureOwnerId == city.OwnerId) return NativeCitizens(city);
            var influence = Find(city, cultureOwnerId);
            return influence == null ? 0 : Math.Max(0, influence.PreferredCitizens);
        }

        public static int NativeCitizens(CityState city)
        {
            if (city == null) throw new ArgumentNullException(nameof(city));
            var foreign = 0;
            var influences = city.CultureInfluences ?? new List<CultureInfluenceState>();
            for (var index = 0; index < influences.Count; index++)
            {
                if (influences[index].CultureOwnerId != city.OwnerId)
                    foreign += Math.Max(0, influences[index].PreferredCitizens);
            }
            return Math.Max(0, city.Population - foreign);
        }

        public static CultureInfluenceState GetOrCreate(CityState city, EntityId cultureOwnerId)
        {
            if (city == null) throw new ArgumentNullException(nameof(city));
            if (!cultureOwnerId.IsValid || cultureOwnerId == city.OwnerId)
                throw new ArgumentException("Influence must belong to a valid foreign culture.", nameof(cultureOwnerId));
            if (city.CultureInfluences == null) city.CultureInfluences = new List<CultureInfluenceState>();
            var influence = Find(city, cultureOwnerId);
            if (influence != null) return influence;
            influence = new CultureInfluenceState { CultureOwnerId = cultureOwnerId };
            city.CultureInfluences.Add(influence);
            city.CultureInfluences.Sort((left, right) => left.CultureOwnerId.CompareTo(right.CultureOwnerId));
            return influence;
        }

        public static void Normalize(CityState city)
        {
            if (city == null) throw new ArgumentNullException(nameof(city));
            if (city.CultureInfluences == null) city.CultureInfluences = new List<CultureInfluenceState>();
            city.CultureInfluences.RemoveAll(item => item == null || !item.CultureOwnerId.IsValid ||
                item.CultureOwnerId == city.OwnerId);
            city.CultureInfluences.Sort((left, right) => left.CultureOwnerId.CompareTo(right.CultureOwnerId));
            for (var index = city.CultureInfluences.Count - 1; index > 0; index--)
            {
                var current = city.CultureInfluences[index];
                var previous = city.CultureInfluences[index - 1];
                if (current.CultureOwnerId != previous.CultureOwnerId) continue;
                previous.PreferredCitizens += Math.Max(0, current.PreferredCitizens);
                previous.ConversionProgress += Math.Max(0, current.ConversionProgress);
                previous.ReversionProgress += Math.Max(0, current.ReversionProgress);
                city.CultureInfluences.RemoveAt(index);
            }
            var remaining = Math.Max(0, city.Population);
            for (var index = 0; index < city.CultureInfluences.Count; index++)
            {
                var influence = city.CultureInfluences[index];
                influence.PreferredCitizens = Math.Min(remaining, Math.Max(0, influence.PreferredCitizens));
                influence.ConversionProgress = Math.Max(0, influence.ConversionProgress);
                influence.ReversionProgress = Math.Max(0, influence.ReversionProgress);
                remaining -= influence.PreferredCitizens;
            }
        }

        private static CultureInfluenceState Find(CityState city, EntityId cultureOwnerId)
        {
            if (city.CultureInfluences == null) return null;
            return city.CultureInfluences.Find(item => item.CultureOwnerId == cultureOwnerId);
        }
    }
}
