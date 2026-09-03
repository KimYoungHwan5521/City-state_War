using System.Linq;
using LittleCiv.Core;
using NUnit.Framework;

namespace LittleCiv.Tests
{
    public sealed class NeutralCityRulesTests
    {
        [Test]
        public void PrototypeAssignsTwoOfEachSpecializationSymmetricallyWithZeroFavor()
        {
            var state = PrototypeMatchFactory.Create(13000);
            var majors = state.Players.Where(item => item.Slot != PlayerSlot.Neutral).ToArray();
            var neutralOwner = state.Players.Single(item => item.Slot == PlayerSlot.Neutral);
            var cities = state.Cities.Where(item => item.OwnerId == neutralOwner.Id).ToArray();

            Assert.That(cities.Length, Is.EqualTo(8));
            foreach (var type in new[]
                     {
                         NeutralCitySpecialization.Military, NeutralCitySpecialization.Science,
                         NeutralCitySpecialization.Culture, NeutralCitySpecialization.Commerce
                     })
                Assert.That(cities.Count(item => item.NeutralSpecialization == type), Is.EqualTo(2));
            Assert.That(cities.Single(item => item.Name == "N1").NeutralSpecialization,
                Is.EqualTo(cities.Single(item => item.Name == "N8").NeutralSpecialization));
            Assert.That(cities.Single(item => item.Name == "N2").NeutralSpecialization,
                Is.EqualTo(cities.Single(item => item.Name == "N7").NeutralSpecialization));
            Assert.That(cities.All(city => majors.All(player =>
                NeutralCityRules.Favor(city, player.Id) == 0)), Is.True);
        }

        [Test]
        public void DevelopmentStageCountsOnlyOperationalSpecializationDistricts()
        {
            var state = PrototypeMatchFactory.Create(13001);
            var neutral = state.Players.Single(item => item.Slot == PlayerSlot.Neutral);
            var city = state.Cities.First(item => item.OwnerId == neutral.Id &&
                item.NeutralSpecialization == NeutralCitySpecialization.Military);
            Assert.That(NeutralCityRules.DevelopmentStage(state, city), Is.EqualTo(NeutralDevelopmentStage.Early));
            AddDistrict(state, city, DistrictType.Military, true);
            AddDistrict(state, city, DistrictType.Military, true);
            Assert.That(NeutralCityRules.DevelopmentStage(state, city), Is.EqualTo(NeutralDevelopmentStage.Middle));
            AddDistrict(state, city, DistrictType.Military, true);
            Assert.That(NeutralCityRules.DevelopmentStage(state, city), Is.EqualTo(NeutralDevelopmentStage.Late));
            state.Districts.Last().IsMaintenanceSuspended = true;
            Assert.That(NeutralCityRules.DevelopmentStage(state, city), Is.EqualTo(NeutralDevelopmentStage.Middle));
        }

        [Test]
        public void FavorClampsAndMajorityRaisesFavorToFourThenReleasesToThree()
        {
            var state = PrototypeMatchFactory.Create(13002);
            var one = state.Players.Single(item => item.Slot == PlayerSlot.PlayerOne);
            var two = state.Players.Single(item => item.Slot == PlayerSlot.PlayerTwo);
            var neutral = state.Players.Single(item => item.Slot == PlayerSlot.Neutral);
            var city = state.Cities.First(item => item.OwnerId == neutral.Id &&
                item.NeutralSpecialization == NeutralCitySpecialization.Science);
            NeutralCityRules.SetFavor(city, one.Id, -20);
            Assert.That(NeutralCityRules.Favor(city, one.Id), Is.EqualTo(-10));
            NeutralCityRules.SetFavor(city, one.Id, 3);
            CityCultureRules.GetOrCreate(city, one.Id).PreferredCitizens = 3;
            state.Cities.Single(item => item.OwnerId == one.Id).LastCultureProduction = 30;
            state.Cities.Single(item => item.OwnerId == two.Id).LastCultureProduction = 0;
            city.LastCultureProduction = 1;
            NeutralCultureResolver.Advance(state);
            Assert.That(NeutralCityRules.Favor(city, one.Id), Is.EqualTo(4));
            Assert.That(NeutralCityRules.Favor(city, two.Id), Is.EqualTo(0));
            var subjectQuote = NeutralTradeQuoteResolver.Quote(state, one.Id,
                state.Cities.Single(item => item.OwnerId == one.Id).Id, city.Id);
            Assert.That(subjectQuote.ResourceAmount, Is.EqualTo(2));
            Assert.That(subjectQuote.BaseGoldCost, Is.EqualTo(1));

            var influence = CityCultureRules.GetOrCreate(city, one.Id);
            influence.PreferredCitizens = 2;
            influence.ReversionProgress = 8;
            state.Cities.Single(item => item.OwnerId == one.Id).LastCultureProduction = 0;
            NeutralCultureResolver.Advance(state);
            Assert.That(NeutralCityRules.Favor(city, one.Id), Is.EqualTo(3));
            var releasedQuote = NeutralTradeQuoteResolver.Quote(state, one.Id,
                state.Cities.Single(item => item.OwnerId == one.Id).Id, city.Id);
            Assert.That(releasedQuote.ResourceAmount, Is.EqualTo(1));
            Assert.That(releasedQuote.BaseGoldCost, Is.EqualTo(1));
        }

        [Test]
        public void FavorFourRequiresSubordinationAndHostilityClearsIt()
        {
            var state = PrototypeMatchFactory.Create(13004);
            var one = state.Players.Single(item => item.Slot == PlayerSlot.PlayerOne);
            var two = state.Players.Single(item => item.Slot == PlayerSlot.PlayerTwo);
            var neutral = state.Players.Single(item => item.Slot == PlayerSlot.Neutral);
            var city = state.Cities.First(item => item.OwnerId == neutral.Id);

            NeutralCityRules.SetFavor(city, one.Id, 4);
            Assert.That(NeutralCityRules.Favor(city, one.Id), Is.EqualTo(3));
            city.CultureSubjectToId = two.Id;
            NeutralCityRules.SetFavor(city, one.Id, 4);
            NeutralCityRules.SetFavor(city, two.Id, 4);

            Assert.That(NeutralCityRules.Favor(city, one.Id), Is.EqualTo(3));
            Assert.That(NeutralCityRules.Favor(city, two.Id), Is.EqualTo(4));
            NeutralCityRules.SetFavor(city, two.Id, -10);
            Assert.That(city.CultureSubjectToId.IsValid, Is.False);
        }

        [Test]
        public void NeutralMetadataSurvivesCopyAndChangesHash()
        {
            var state = PrototypeMatchFactory.Create(13003);
            var neutral = state.Players.Single(item => item.Slot == PlayerSlot.Neutral);
            var city = state.Cities.First(item => item.OwnerId == neutral.Id);
            var copy = GameStateCopy.Clone(state);
            Assert.That(GameStateHasher.Compute(copy), Is.EqualTo(GameStateHasher.Compute(state)));

            copy.Cities.Single(item => item.Id == city.Id).NeutralRelations[0].Favor = 1;
            Assert.That(GameStateHasher.Compute(copy), Is.Not.EqualTo(GameStateHasher.Compute(state)));
        }

        private static void AddDistrict(GameState state, CityState city, DistrictType type, bool operational)
        {
            var placement = state.MapTopology.FindView(city.Id).Tiles.First(item => item.IsBuildable &&
                state.Districts.All(district => district.TileId != item.TileId));
            state.Districts.Add(new DistrictState
            {
                Id = state.AllocateId(), CityId = city.Id, TileId = placement.TileId,
                Type = type, ControllerId = city.OwnerId, AssignedCitizens = 1,
                IsOperational = operational
            });
        }
    }
}
