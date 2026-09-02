using System;
using System.Collections.Generic;

namespace LittleCiv.Core
{
    public sealed class NeutralResearchRecord
    {
        public EntityId CityId;
        public ResearchType Type;
        public int AddedProgress;
        public int TotalProgress;
        public bool Completed;
    }

    public static class NeutralResearchResolver
    {
        private static readonly ResearchType[] MilitaryOrder =
        {
            ResearchType.School, ResearchType.IronWorking, ResearchType.Arts,
            ResearchType.Fortification, ResearchType.Irrigation, ResearchType.Salting,
            ResearchType.Gunpowder, ResearchType.AdvancedFortification, ResearchType.Canning,
            ResearchType.Vehicles, ResearchType.ModernDefense
        };

        private static readonly ResearchType[] CultureOrder =
        {
            ResearchType.School, ResearchType.Arts, ResearchType.Printing, ResearchType.MassMedia,
            ResearchType.Fortification, ResearchType.Irrigation, ResearchType.Salting,
            ResearchType.AdvancedFortification, ResearchType.Canning, ResearchType.ModernDefense
        };

        private static readonly ResearchType[] CommerceOrder =
        {
            ResearchType.School, ResearchType.Currency, ResearchType.Finance,
            ResearchType.EconomicAdministration, ResearchType.Arts, ResearchType.Fortification,
            ResearchType.Irrigation, ResearchType.Salting, ResearchType.AdvancedFortification,
            ResearchType.Canning, ResearchType.IronWorking, ResearchType.Gunpowder,
            ResearchType.Vehicles, ResearchType.ModernDefense
        };

        private static readonly ResearchType[] ScienceOrder =
        {
            ResearchType.School, ResearchType.IronWorking, ResearchType.Arts,
            ResearchType.Currency, ResearchType.Irrigation, ResearchType.Fortification,
            ResearchType.Salting, ResearchType.Gunpowder, ResearchType.Printing,
            ResearchType.Finance, ResearchType.Fertilizer, ResearchType.AdvancedFortification,
            ResearchType.Canning, ResearchType.Vehicles, ResearchType.MassMedia,
            ResearchType.EconomicAdministration, ResearchType.MechanizedAgriculture,
            ResearchType.ModernDefense
        };

        public static List<NeutralResearchRecord> Advance(GameState state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            var result = new List<NeutralResearchRecord>();
            var cities = NeutralCities(state);
            for (var index = 0; index < cities.Count; index++)
            {
                var city = cities[index];
                if (city.NeutralCurrentResearch == ResearchType.None)
                    city.NeutralCurrentResearch = NextResearch(city);
                if (city.NeutralCurrentResearch == ResearchType.None) continue;
                var progress = GetProgress(city, city.NeutralCurrentResearch);
                var needed = Math.Max(0, ResearchRules.Cost(progress.Type) - progress.Progress);
                var consumed = Math.Min(needed, Math.Max(0, city.ResearchPoints));
                city.ResearchPoints -= consumed;
                progress.Progress += consumed;
                var completed = progress.Progress >= ResearchRules.Cost(progress.Type);
                result.Add(new NeutralResearchRecord
                {
                    CityId = city.Id, Type = progress.Type, AddedProgress = consumed,
                    TotalProgress = progress.Progress, Completed = completed
                });
                if (!completed) continue;
                if (!city.NeutralCompletedResearch.Contains(progress.Type))
                    city.NeutralCompletedResearch.Add(progress.Type);
                city.NeutralCurrentResearch = ResearchType.None;
            }
            return result;
        }

        public static ResearchType NextResearch(CityState city)
        {
            var order = OrderFor(city.NeutralSpecialization);
            for (var index = 0; index < order.Count; index++)
                if (!city.NeutralCompletedResearch.Contains(order[index])) return order[index];
            return ResearchType.None;
        }

        public static IReadOnlyList<ResearchType> OrderFor(NeutralCitySpecialization specialization)
        {
            switch (specialization)
            {
                case NeutralCitySpecialization.Military: return MilitaryOrder;
                case NeutralCitySpecialization.Culture: return CultureOrder;
                case NeutralCitySpecialization.Commerce: return CommerceOrder;
                default: return ScienceOrder;
            }
        }

        public static bool HasResearch(CityState city, ResearchType type) =>
            city != null && city.NeutralCompletedResearch != null &&
            city.NeutralCompletedResearch.Contains(type);

        private static ResearchProgressState GetProgress(CityState city, ResearchType type)
        {
            if (city.NeutralResearchProgress == null)
                city.NeutralResearchProgress = new List<ResearchProgressState>();
            var progress = city.NeutralResearchProgress.Find(item => item.Type == type);
            if (progress != null) return progress;
            progress = new ResearchProgressState { Type = type };
            city.NeutralResearchProgress.Add(progress);
            return progress;
        }

        private static List<CityState> NeutralCities(GameState state)
        {
            var result = new List<CityState>();
            for (var index = 0; index < state.Cities.Count; index++)
            {
                var owner = state.Players.Find(item => item.Id == state.Cities[index].OwnerId);
                if (owner != null && owner.Slot == PlayerSlot.Neutral) result.Add(state.Cities[index]);
            }
            result.Sort((left, right) => left.Id.CompareTo(right.Id));
            return result;
        }
    }
}
