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
        public void EverySpecializationFinishesLowerCostResearchBeforeNuclearFission()
        {
            foreach (var specialization in new[]
                     {
                         NeutralCitySpecialization.Military, NeutralCitySpecialization.Science,
                         NeutralCitySpecialization.Culture, NeutralCitySpecialization.Commerce
                     })
            {
                var order = NeutralResearchResolver.OrderFor(specialization).ToArray();
                Assert.That(order.Length, Is.EqualTo(19));
                Assert.That(order.Distinct().Count(), Is.EqualTo(order.Length));
                Assert.That(order.Last(), Is.EqualTo(ResearchType.NuclearFission));
                for (var index = 1; index < order.Length; index++)
                    Assert.That(ResearchRules.Cost(order[index]),
                        Is.GreaterThanOrEqualTo(ResearchRules.Cost(order[index - 1])));
            }
        }

        [Test]
        public void DifferentSpecializationsChooseDifferentSecondResearch()
        {
            AssertSecond(NeutralCitySpecialization.Military, ResearchType.IronWorking);
            AssertSecond(NeutralCitySpecialization.Science, ResearchType.Irrigation);
            AssertSecond(NeutralCitySpecialization.Culture, ResearchType.Arts);
            AssertSecond(NeutralCitySpecialization.Commerce, ResearchType.Currency);
        }

        [Test]
        public void EqualCostResearchPrioritizesEachCitySpecialization()
        {
            var military = NeutralResearchResolver.OrderFor(NeutralCitySpecialization.Military).ToList();
            var culture = NeutralResearchResolver.OrderFor(NeutralCitySpecialization.Culture).ToList();
            var commerce = NeutralResearchResolver.OrderFor(NeutralCitySpecialization.Commerce).ToList();

            Assert.That(military.IndexOf(ResearchType.IronWorking),
                Is.LessThan(military.IndexOf(ResearchType.Arts)));
            Assert.That(military.IndexOf(ResearchType.Gunpowder),
                Is.LessThan(military.IndexOf(ResearchType.Printing)));
            Assert.That(culture.IndexOf(ResearchType.Arts),
                Is.LessThan(culture.IndexOf(ResearchType.IronWorking)));
            Assert.That(culture.IndexOf(ResearchType.Printing),
                Is.LessThan(culture.IndexOf(ResearchType.Gunpowder)));
            Assert.That(commerce.IndexOf(ResearchType.Currency),
                Is.LessThan(commerce.IndexOf(ResearchType.IronWorking)));
            Assert.That(commerce.IndexOf(ResearchType.Finance),
                Is.LessThan(commerce.IndexOf(ResearchType.Gunpowder)));
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

        [Test]
        public void EveryNeutralSpecializationEventuallyCompletesFissionAndBuildsOneFacilityWhenUnopposed()
        {
            var state = PrototypeMatchFactory.Create(20260907);
            var processor = new TurnProcessor();
            for (var turn = 0; turn < 200; turn++)
                processor.Resolve(state, new GameCommand[0]);

            var neutral = state.Players.Single(item => item.Slot == PlayerSlot.Neutral);
            var cities = state.Cities.Where(item => item.OwnerId == neutral.Id).ToArray();
            Assert.That(cities.Length, Is.EqualTo(8));
            Assert.That(cities.All(item => item.NeutralCompletedResearch.Contains(
                ResearchType.NuclearFission)), Is.True);
            foreach (var city in cities)
            {
                var facilities = state.Districts.Where(item => item.CityId == city.Id &&
                    item.Type == DistrictType.NuclearFacility).ToArray();
                Assert.That(facilities.Length, Is.EqualTo(1));
                Assert.That(facilities[0].RemainingConstructionTurns, Is.Zero);
                Assert.That(facilities[0].IsOperational, Is.True);
            }
            Assert.That(state.NuclearProjects, Is.Empty);
        }

        private static void AssertSecond(NeutralCitySpecialization specialization, ResearchType expected)
        {
            var city = new CityState { NeutralSpecialization = specialization };
            city.NeutralCompletedResearch.Add(ResearchType.School);
            Assert.That(NeutralResearchResolver.NextResearch(city), Is.EqualTo(expected));
        }
    }
}
