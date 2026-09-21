using System.Linq;
using LittleCiv.Core;
using NUnit.Framework;

namespace LittleCiv.Tests
{
    public sealed class NeutralCityDevelopmentResolverTests
    {
        [Test]
        public void NeutralCityStartsRepairOnRecapturedPillagedDistrict()
        {
            var state = PrototypeMatchFactory.Create(13499);
            var neutral = state.Players.Single(item => item.Slot == PlayerSlot.Neutral);
            NeutralCityDevelopmentResolver.StartAvailableConstruction(state);
            var district = state.Districts.First(item =>
                state.Cities.Any(city => city.Id == item.CityId && city.OwnerId == neutral.Id) &&
                item.Type != DistrictType.Government);
            district.ControllerId = neutral.Id;
            district.RemainingConstructionTurns = 0;
            district.IsPillaged = true;
            district.IsOperational = false;

            var repaired = NeutralCityDevelopmentResolver.StartAvailableRepairs(state);

            Assert.That(repaired, Does.Contain(district.Id));
            Assert.That(district.RemainingRepairTurns, Is.EqualTo(DistrictConstructionResolver.RepairTurns(district.Type)));
        }

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
        public void NewCitizensWaitForUnlockThenBuildScienceCultureAndNeededSupport()
        {
            var state = PrototypeMatchFactory.Create(13101);
            var neutral = state.Players.Single(item => item.Slot == PlayerSlot.Neutral);
            var city = state.Cities.First(item => item.OwnerId == neutral.Id);
            NeutralCityDevelopmentResolver.StartAvailableConstruction(state);
            state.TurnNumber = 2;
            city.Population = 5;
            city.StoredFood = 100;
            city.Gold = 100;

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
            var lateConstruction = NeutralCityDevelopmentResolver.StartAvailableConstruction(state)
                .Where(item => item.CityId == city.Id).ToArray();
            Assert.That(lateConstruction.Length, Is.EqualTo(2));
            Assert.That(lateConstruction.All(item => item.Type == DistrictType.Agriculture ||
                item.Type == DistrictType.Commerce || item.Type ==
                NeutralCityRules.DistrictTypeFor(city.NeutralSpecialization)), Is.True);
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
            city.StoredFood = 100;
            city.Gold = 100;

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

        [Test]
        public void MilitaryNeutralBuildsAgricultureBeforeSecondMilitaryWhenFoodMarginIsTooLow()
        {
            var state = CreateMilitaryExpansionEconomy(13110);
            var city = state.Cities.First(item => item.NeutralSpecialization ==
                NeutralCitySpecialization.Military);
            city.Population = 8;

            var support = NeutralCityDevelopmentResolver
                .SupportNeededForAdditionalMilitaryDistrict(state, city);

            Assert.That(support, Is.EqualTo(DistrictType.Agriculture));
        }

        [Test]
        public void MilitaryNeutralCanAddSecondMilitaryAtThreeFoodAndGoldMarginForMilitia()
        {
            var state = CreateMilitaryExpansionEconomy(13111);
            var city = state.Cities.First(item => item.NeutralSpecialization ==
                NeutralCitySpecialization.Military);

            var support = NeutralCityDevelopmentResolver
                .SupportNeededForAdditionalMilitaryDistrict(state, city);

            Assert.That(support, Is.Null);
        }

        [Test]
        public void MilitaryNeutralRequiresSixGoldMarginForSecondMilitaryAfterIronWorking()
        {
            var state = CreateMilitaryExpansionEconomy(13112);
            var city = state.Cities.First(item => item.NeutralSpecialization ==
                NeutralCitySpecialization.Military);
            city.NeutralCompletedResearch.Add(ResearchType.School);
            city.NeutralCompletedResearch.Add(ResearchType.IronWorking);

            var support = NeutralCityDevelopmentResolver
                .SupportNeededForAdditionalMilitaryDistrict(state, city);

            Assert.That(support, Is.EqualTo(DistrictType.Commerce));
        }

        [Test]
        public void ProfessionalDistrictExpansionPrefersSameTypeAdjacency()
        {
            var state = PrototypeMatchFactory.Create(13103);
            var city = state.Cities.First(item => item.NeutralSpecialization ==
                NeutralCitySpecialization.Commerce);
            city.NeutralCompletedResearch.Add(ResearchType.School);
            city.NeutralCompletedResearch.Add(ResearchType.Arts);
            state.TurnNumber = 2;
            var view = state.MapTopology.FindView(city.Id);
            var buildable = view.Tiles.Where(item => item.IsBuildable).ToList();
            foreach (var placement in buildable)
                state.Tiles.Single(item => item.Id == placement.TileId).ResourceType = TileResourceType.None;
            state.Districts.RemoveAll(item => item.CityId == city.Id && item.Type != DistrictType.Government);
            var types = new[] { DistrictType.Commerce, DistrictType.Science, DistrictType.Culture };
            for (var index = 0; index < types.Length; index++)
                state.Districts.Add(new DistrictState
                {
                    Id = state.AllocateId(), CityId = city.Id, TileId = buildable[index].TileId,
                    Type = types[index], ControllerId = city.OwnerId,
                    IsOperational = true, AssignedCitizens = 1
                });
            state.Districts.Add(new DistrictState
            {
                Id = state.AllocateId(), CityId = city.Id, TileId = buildable[3].TileId,
                Type = DistrictType.Agriculture, ControllerId = city.OwnerId,
                IsOperational = true, AssignedCitizens = 1
            });
            state.Districts.Add(new DistrictState
            {
                Id = state.AllocateId(), CityId = city.Id, TileId = buildable[4].TileId,
                Type = DistrictType.Agriculture, ControllerId = city.OwnerId,
                IsOperational = true, AssignedCitizens = 1
            });
            state.Districts.Add(new DistrictState
            {
                Id = state.AllocateId(), CityId = city.Id, TileId = buildable[5].TileId,
                Type = DistrictType.Commerce, ControllerId = city.OwnerId,
                IsOperational = true, AssignedCitizens = 1
            });
            for (var index = 6; index <= 7; index++)
                state.Districts.Add(new DistrictState
                {
                    Id = state.AllocateId(), CityId = city.Id, TileId = buildable[index].TileId,
                    Type = DistrictType.Military, ControllerId = city.OwnerId,
                    IsOperational = true, AssignedCitizens = 1
                });
            city.Population = 10;
            city.StoredFood = 1000;
            city.Gold = 1000;
            city.TestGovernmentFoodBonus = 20;
            city.TestGovernmentGoldBonus = 20;

            var started = NeutralCityDevelopmentResolver.StartAvailableConstruction(state)
                .Single(item => item.CityId == city.Id);
            var addedDistrict = state.Districts.Single(item => item.Id == started.DistrictId);
            var added = view.Tiles.Single(item => item.TileId == addedDistrict.TileId);

            Assert.That(started.Type, Is.EqualTo(DistrictType.Commerce));
            Assert.That(state.Districts.Where(item => item.CityId == city.Id &&
                    item.Type == DistrictType.Commerce && item.Id != addedDistrict.Id)
                .Select(item => view.Tiles.Single(tile => tile.TileId == item.TileId))
                .Any(item => HexCoord.Distance(new HexCoord(item.LocalQ, item.LocalR),
                    new HexCoord(added.LocalQ, added.LocalR)) == 1), Is.True);
        }

        [Test]
        public void FoodEmergencyRecallsSpecialistAndBuildsAgricultureThenRestoresIt()
        {
            var state = PrototypeMatchFactory.Create(13104);
            var city = state.Cities.First(item => item.NeutralSpecialization ==
                NeutralCitySpecialization.Military);
            NeutralCityDevelopmentResolver.StartAvailableConstruction(state);
            foreach (var district in state.Districts.Where(item => item.CityId == city.Id))
            {
                district.RemainingConstructionTurns = 0;
                district.IsOperational = true;
            }
            city.NeutralCompletedResearch.Add(ResearchType.School);
            var scienceTile = state.MapTopology.FindView(city.Id).Tiles.First(item => item.IsBuildable &&
                state.Districts.All(district => district.TileId != item.TileId));
            var science = new DistrictState
            {
                Id = state.AllocateId(), CityId = city.Id, TileId = scienceTile.TileId,
                Type = DistrictType.Science, ControllerId = city.OwnerId,
                IsOperational = true, AssignedCitizens = 1
            };
            state.Districts.Add(science);
            city.Population = 5;
            city.StoredFood = 100;
            city.Gold = 100;
            var originalAgriculture = state.Districts.Single(item => item.CityId == city.Id &&
                item.Type == DistrictType.Agriculture);
            originalAgriculture.IsPillaged = true;
            originalAgriculture.IsOperational = false;
            state.TurnNumber = 2;

            var emergency = NeutralCityDevelopmentResolver.StartAvailableConstruction(state)
                .Single(item => item.CityId == city.Id);

            Assert.That(emergency.Type, Is.EqualTo(DistrictType.Agriculture));
            Assert.That(city.CitizenAutoAssignment, Is.False);
            Assert.That(science.AssignedCitizens, Is.Zero);
            var emergencyFarm = state.Districts.Single(item => item.Id == emergency.DistrictId);
            emergencyFarm.RemainingConstructionTurns = 0;
            emergencyFarm.IsOperational = true;
            city.Population = 6;
            city.StoredFood = 1000;
            city.Gold = 1000;

            var later = NeutralCityDevelopmentResolver.StartAvailableConstruction(state)
                .Where(item => item.CityId == city.Id).ToArray();

            Assert.That(later, Is.Empty);
            Assert.That(science.AssignedCitizens, Is.EqualTo(1));
            Assert.That(science.IsOperational, Is.True);
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

        private static GameState CreateMilitaryExpansionEconomy(int seed)
        {
            var state = PrototypeMatchFactory.Create(seed);
            var city = state.Cities.First(item => item.NeutralSpecialization ==
                NeutralCitySpecialization.Military);
            NeutralCityDevelopmentResolver.StartAvailableConstruction(state);
            city.StoredFood = 100;
            city.Gold = 100;
            return state;
        }
    }
}
