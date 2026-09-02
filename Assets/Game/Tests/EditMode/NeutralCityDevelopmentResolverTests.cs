using System.Linq;
using LittleCiv.Core;
using NUnit.Framework;

namespace LittleCiv.Tests
{
    public sealed class NeutralCityDevelopmentResolverTests
    {
        [Test]
        public void FirstTurnAllNeutralCitiesStartAgricultureMilitaryAndCommerceTogether()
        {
            var state = PrototypeMatchFactory.Create(13100);
            var neutral = state.Players.Single(item => item.Slot == PlayerSlot.Neutral);

            var turn = new TurnProcessor().Resolve(state, new GameCommand[0]);
            var cities = state.Cities.Where(item => item.OwnerId == neutral.Id).ToArray();

            foreach (var city in cities)
            {
                var types = state.Districts.Where(item => item.CityId == city.Id &&
                    item.Type != DistrictType.Government).Select(item => item.Type).ToArray();
                Assert.That(types, Is.EquivalentTo(new[]
                    { DistrictType.Agriculture, DistrictType.Military, DistrictType.Commerce }));
                Assert.That(state.Districts.Where(item => item.CityId == city.Id &&
                    item.Type != DistrictType.Government).All(item =>
                    item.RemainingConstructionTurns == DistrictConstructionResolver.StandardConstructionTurns), Is.True);
                Assert.That(DistrictConstructionResolver.CountFreeCitizens(state, city), Is.Zero);
            }
            Assert.That(turn.Events.Count(item => item.Type == GameEventType.DistrictConstructionStarted &&
                cities.Any(city => city.Id == item.SourceId)), Is.EqualTo(24));
        }

        [Test]
        public void NewCitizensWaitForUnlockThenBuildScienceCultureAndSpecialization()
        {
            var state = PrototypeMatchFactory.Create(13101);
            var neutral = state.Players.Single(item => item.Slot == PlayerSlot.Neutral);
            var city = state.Cities.First(item => item.OwnerId == neutral.Id);
            NeutralCityDevelopmentResolver.StartAvailableConstruction(state);
            state.TurnNumber = 2;
            city.Population = 5;

            Assert.That(NeutralCityDevelopmentResolver.StartAvailableConstruction(state)
                .Any(item => item.CityId == city.Id), Is.False);
            Assert.That(DistrictConstructionResolver.CountFreeCitizens(state, city), Is.EqualTo(1));

            city.NeutralCompletedResearch.Add(ResearchType.School);
            var science = NeutralCityDevelopmentResolver.StartAvailableConstruction(state)
                .Single(item => item.CityId == city.Id);
            Assert.That(science.Type, Is.EqualTo(DistrictType.Science));

            city.Population = 6;
            city.NeutralCompletedResearch.Add(ResearchType.Arts);
            var culture = NeutralCityDevelopmentResolver.StartAvailableConstruction(state)
                .Single(item => item.CityId == city.Id);
            Assert.That(culture.Type, Is.EqualTo(DistrictType.Culture));

            city.Population = 8;
            var specialized = NeutralCityDevelopmentResolver.StartAvailableConstruction(state)
                .Where(item => item.CityId == city.Id).ToArray();
            Assert.That(specialized.Length, Is.EqualTo(2));
            Assert.That(specialized.All(item => item.Type ==
                NeutralCityRules.DistrictTypeFor(city.NeutralSpecialization)), Is.True);
            Assert.That(NeutralCityDevelopmentResolver.NextDistrictType(state, city),
                Is.EqualTo(DistrictType.Government));
        }

        [Test]
        public void AgricultureCommerceScienceAndCulturePreferMatchingResourceTiles()
        {
            var state = PrototypeMatchFactory.Create(13102);
            var neutral = state.Players.Single(item => item.Slot == PlayerSlot.Neutral);
            var city = state.Cities.First(item => item.OwnerId == neutral.Id);
            city.NeutralCompletedResearch.Add(ResearchType.School);
            city.NeutralCompletedResearch.Add(ResearchType.Arts);
            city.Population = 6;

            NeutralCityDevelopmentResolver.StartAvailableConstruction(state);
            state.TurnNumber = 2;
            var records = NeutralCityDevelopmentResolver.StartAvailableConstruction(state)
                .Where(item => item.CityId == city.Id).ToArray();

            foreach (var type in new[]
                     { DistrictType.Agriculture, DistrictType.Commerce, DistrictType.Science, DistrictType.Culture })
            {
                var district = state.Districts.First(item => item.CityId == city.Id && item.Type == type);
                var tile = state.Tiles.Single(item => item.Id == district.TileId);
                Assert.That(tile.ResourceType, Is.EqualTo(ResourceFor(type)));
            }
            Assert.That(records.Length, Is.EqualTo(2));
        }

        private static TileResourceType ResourceFor(DistrictType type)
        {
            switch (type)
            {
                case DistrictType.Agriculture: return TileResourceType.Food;
                case DistrictType.Commerce: return TileResourceType.Commerce;
                case DistrictType.Science: return TileResourceType.Science;
                default: return TileResourceType.Culture;
            }
        }
    }
}
