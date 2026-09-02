using System.Linq;
using LittleCiv.Core;
using NUnit.Framework;

namespace LittleCiv.Tests
{
    public sealed class NeutralDefenseResolverTests
    {
        [Test]
        public void NeutralCityBuildsUnlockedGovernmentDefensesSequentially()
        {
            var state = PrototypeMatchFactory.Create(13400);
            var city = NeutralCity(state);
            city.Gold = 100;
            city.NeutralCompletedResearch.Add(ResearchType.Fortification);

            var wall = NeutralDefenseResolver.StartAvailableConstruction(state)
                .Single(item => item.CityId == city.Id);
            Assert.That(wall.BuildingType, Is.EqualTo(DefenseFacilityType.Wall));
            Advance(state, 2);
            city.NeutralCompletedResearch.Add(ResearchType.AdvancedFortification);

            var moat = NeutralDefenseResolver.StartAvailableConstruction(state)
                .Single(item => item.CityId == city.Id);
            Assert.That(moat.BuildingType, Is.EqualTo(DefenseFacilityType.Moat));
            Advance(state, 4);
            Assert.That(moat.Type, Is.EqualTo(DefenseFacilityType.Moat));
        }

        [Test]
        public void ResearchInOneNeutralCityDoesNotUnlockAnotherCityDefense()
        {
            var state = PrototypeMatchFactory.Create(13401);
            var cities = state.Cities.Where(item => item.OwnerId == NeutralOwner(state).Id).Take(2).ToArray();
            cities[0].Gold = cities[1].Gold = 100;
            cities[0].NeutralCompletedResearch.Add(ResearchType.Fortification);

            var result = NeutralDefenseResolver.StartAvailableConstruction(state);

            Assert.That(result.Count(item => item.CityId == cities[0].Id), Is.EqualTo(1));
            Assert.That(result.Any(item => item.CityId == cities[1].Id), Is.False);
        }

        [Test]
        public void ModernDefenseRequiresSustainableUpkeepAndGoldReserve()
        {
            var state = PrototypeMatchFactory.Create(13402);
            var city = NeutralCity(state);
            city.NeutralCompletedResearch.Add(ResearchType.ModernDefense);
            city.Gold = 21;
            var government = state.Districts.Single(item => item.CityId == city.Id &&
                item.Type == DistrictType.Government);
            var commerceTile = state.MapTopology.FindView(city.Id).Tiles.First(item => item.IsBuildable &&
                state.Districts.All(district => district.TileId != item.TileId));
            state.Districts.Add(new DistrictState
            {
                Id = state.AllocateId(), CityId = city.Id, TileId = commerceTile.TileId,
                Type = DistrictType.Commerce, ControllerId = city.OwnerId,
                IsOperational = true, AssignedCitizens = 1
            });
            state.DefenseFacilities.Add(new DefenseFacilityState
            {
                Id = state.AllocateId(), CityId = city.Id, TileId = government.TileId,
                Type = DefenseFacilityType.Moat
            });

            Assert.That(NeutralDefenseResolver.StartAvailableConstruction(state)
                .Any(item => item.CityId == city.Id), Is.False);
            city.Gold = 22;
            Assert.That(NeutralDefenseResolver.StartAvailableConstruction(state)
                .Single(item => item.CityId == city.Id).BuildingType,
                Is.EqualTo(DefenseFacilityType.ModernDefense));
        }

        private static PlayerState NeutralOwner(GameState state) =>
            state.Players.Single(item => item.Slot == PlayerSlot.Neutral);

        private static CityState NeutralCity(GameState state) =>
            state.Cities.First(item => item.OwnerId == NeutralOwner(state).Id);

        private static void Advance(GameState state, int turns)
        {
            for (var index = 0; index < turns; index++)
                DefenseFacilityResolver.AdvanceConstruction(state);
        }
    }
}
