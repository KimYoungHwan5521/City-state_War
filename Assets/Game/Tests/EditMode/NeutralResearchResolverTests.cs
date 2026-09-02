using System.Linq;
using LittleCiv.Core;
using NUnit.Framework;

namespace LittleCiv.Tests
{
    public sealed class NeutralResearchResolverTests
    {
        [Test]
        public void EveryNeutralCityCompletesSchoolIndependentlyOnThirdScienceTurn()
        {
            var state = PrototypeMatchFactory.Create(13200);
            var neutral = state.Players.Single(item => item.Slot == PlayerSlot.Neutral);
            var cities = state.Cities.Where(item => item.OwnerId == neutral.Id).ToArray();
            var processor = new TurnProcessor();

            processor.Resolve(state, new GameCommand[0]);
            processor.Resolve(state, new GameCommand[0]);
            var third = processor.Resolve(state, new GameCommand[0]);

            Assert.That(cities.All(item => item.NeutralCompletedResearch.Contains(ResearchType.School)), Is.True);
            Assert.That(third.Events.Count(item => item.Type == GameEventType.NeutralResearchCompleted &&
                item.PrimaryValue == (int)ResearchType.School), Is.EqualTo(8));
            Assert.That(neutral.CompletedResearch, Is.Empty);
        }

        [Test]
        public void MilitaryOrderMatchesDesignAndNoSpecializationCanResearchNuclearFission()
        {
            var order = NeutralResearchResolver.OrderFor(NeutralCitySpecialization.Military);
            Assert.That(order, Is.EqualTo(new[]
            {
                ResearchType.School, ResearchType.IronWorking, ResearchType.Arts,
                ResearchType.Fortification, ResearchType.Irrigation, ResearchType.Salting,
                ResearchType.Gunpowder, ResearchType.AdvancedFortification, ResearchType.Canning,
                ResearchType.Vehicles, ResearchType.ModernDefense
            }));
            foreach (var specialization in new[]
                     {
                         NeutralCitySpecialization.Military, NeutralCitySpecialization.Science,
                         NeutralCitySpecialization.Culture, NeutralCitySpecialization.Commerce
                     })
                Assert.That(NeutralResearchResolver.OrderFor(specialization)
                    .Contains(ResearchType.NuclearFission), Is.False);
        }

        [Test]
        public void DifferentSpecializationsChooseDifferentSecondResearch()
        {
            AssertSecond(NeutralCitySpecialization.Military, ResearchType.IronWorking);
            AssertSecond(NeutralCitySpecialization.Science, ResearchType.IronWorking);
            AssertSecond(NeutralCitySpecialization.Culture, ResearchType.Arts);
            AssertSecond(NeutralCitySpecialization.Commerce, ResearchType.Currency);
        }

        [Test]
        public void NeutralResearchStateSurvivesCopyAndChangesHash()
        {
            var state = PrototypeMatchFactory.Create(13201);
            var neutral = state.Players.Single(item => item.Slot == PlayerSlot.Neutral);
            var city = state.Cities.First(item => item.OwnerId == neutral.Id);
            city.NeutralCurrentResearch = ResearchType.School;
            city.NeutralResearchProgress.Add(new ResearchProgressState
                { Type = ResearchType.School, Progress = 2 });
            var copy = GameStateCopy.Clone(state);

            Assert.That(GameStateHasher.Compute(copy), Is.EqualTo(GameStateHasher.Compute(state)));
            copy.Cities.Single(item => item.Id == city.Id).NeutralResearchProgress[0].Progress++;
            Assert.That(GameStateHasher.Compute(copy), Is.Not.EqualTo(GameStateHasher.Compute(state)));
        }

        [Test]
        public void NeutralCityUsesItsOwnResearchForEconomicEffect()
        {
            var state = PrototypeMatchFactory.Create(13202);
            var neutral = state.Players.Single(item => item.Slot == PlayerSlot.Neutral);
            var city = state.Cities.First(item => item.OwnerId == neutral.Id);
            var placement = state.MapTopology.FindView(city.Id).Tiles.First(item => item.IsBuildable &&
                state.Districts.All(district => district.TileId != item.TileId));
            state.Districts.Add(new DistrictState
            {
                Id = state.AllocateId(), CityId = city.Id, TileId = placement.TileId,
                Type = DistrictType.Commerce, ControllerId = city.OwnerId,
                IsOperational = true, AssignedCitizens = 1
            });
            city.NeutralCompletedResearch.Add(ResearchType.Currency);

            var economy = CityEconomyResolver.CalculateBreakdown(state, city);

            Assert.That(economy.Gold.ResearchBonus, Is.EqualTo(1));
            Assert.That(neutral.CompletedResearch.Contains(ResearchType.Currency), Is.False);
        }

        [Test]
        public void NeutralUnitFoodCapacityUsesItsHomeCityPreservationResearch()
        {
            var state = PrototypeMatchFactory.Create(13203);
            var neutral = state.Players.Single(item => item.Slot == PlayerSlot.Neutral);
            var cities = state.Cities.Where(item => item.OwnerId == neutral.Id).Take(2).ToArray();
            var first = state.Units.Single(item => item.HomeCityId == cities[0].Id);
            var second = state.Units.Single(item => item.HomeCityId == cities[1].Id);
            cities[0].NeutralCompletedResearch.Add(ResearchType.Canning);

            Assert.That(UnitRules.FoodCapacity(state, first), Is.EqualTo(12));
            Assert.That(UnitRules.FoodCapacity(state, second), Is.EqualTo(6));
        }

        private static void AssertSecond(NeutralCitySpecialization specialization, ResearchType expected)
        {
            var city = new CityState { NeutralSpecialization = specialization };
            city.NeutralCompletedResearch.Add(ResearchType.School);
            Assert.That(NeutralResearchResolver.NextResearch(city), Is.EqualTo(expected));
        }
    }
}
