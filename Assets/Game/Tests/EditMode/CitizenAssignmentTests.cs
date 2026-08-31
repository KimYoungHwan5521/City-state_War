using System.Linq;
using LittleCiv.Core;
using NUnit.Framework;

namespace LittleCiv.Tests
{
    public sealed class CitizenAssignmentTests
    {
        [Test]
        public void UnassignedCitizenIsLostBeforeAnyDistrictCitizen()
        {
            var state = PrototypeMatchFactory.Create(4700);
            var city = state.Cities[0];
            var culture = AddDistrict(state, city, DistrictType.Culture, 0);
            city.Population = 4;

            var removed = CitizenAssignmentResolver.RemoveExcessCitizen(state, city);

            Assert.That(removed.IsValid, Is.False);
            Assert.That(culture.AssignedCitizens, Is.EqualTo(1));
        }

        [Test]
        public void ConstructionCitizenIsRemovedFirstAndProgressIsPreserved()
        {
            var state = PrototypeMatchFactory.Create(4701);
            var city = state.Cities[0];
            city.Population = 2;
            var culture = AddDistrict(state, city, DistrictType.Culture, 0);
            var construction = AddDistrict(state, city, DistrictType.Agriculture, 2);

            var removed = CitizenAssignmentResolver.RemoveExcessCitizen(state, city);

            Assert.That(removed, Is.EqualTo(construction.Id));
            Assert.That(construction.AssignedCitizens, Is.Zero);
            Assert.That(construction.RemainingConstructionTurns, Is.EqualTo(2));
            Assert.That(culture.AssignedCitizens, Is.EqualTo(1));
        }

        [Test]
        public void PlayerPriorityOverridesDefaultDistrictOrder()
        {
            var state = PrototypeMatchFactory.Create(4702);
            var city = state.Cities[0];
            city.Population = 2;
            var culture = AddDistrict(state, city, DistrictType.Culture, 0);
            var agriculture = AddDistrict(state, city, DistrictType.Agriculture, 0);
            agriculture.CitizenRemovalPriority = 1;

            var removed = CitizenAssignmentResolver.RemoveExcessCitizen(state, city);

            Assert.That(removed, Is.EqualTo(agriculture.Id));
            Assert.That(culture.AssignedCitizens, Is.EqualTo(1));
        }

        [Test]
        public void DefaultRemovalOrderStartsWithCultureThenScience()
        {
            var state = PrototypeMatchFactory.Create(4703);
            var city = state.Cities[0];
            city.Population = 2;
            var science = AddDistrict(state, city, DistrictType.Science, 0);
            var culture = AddDistrict(state, city, DistrictType.Culture, 0);

            var firstRemoved = CitizenAssignmentResolver.RemoveExcessCitizen(state, city);
            city.Population = 1;
            var secondRemoved = CitizenAssignmentResolver.RemoveExcessCitizen(state, city);

            Assert.That(firstRemoved, Is.EqualTo(culture.Id));
            Assert.That(secondRemoved, Is.EqualTo(science.Id));
        }

        [Test]
        public void SetPriorityCommandPersistsPlayerChoice()
        {
            var state = PrototypeMatchFactory.Create(4704);
            var city = state.Cities[0];
            var district = AddDistrict(state, city, DistrictType.Agriculture, 0);
            var command = new GameCommand
            {
                CommandId = state.AllocateId(), PlayerId = city.OwnerId,
                TurnNumber = state.TurnNumber, Type = GameCommandType.SetPriority,
                SubjectId = city.Id, TargetId = district.Id, PrimaryValue = 1
            };

            new TurnProcessor().Resolve(state, new[] { command });

            Assert.That(district.CitizenRemovalPriority, Is.EqualTo(1));
        }

        private static DistrictState AddDistrict(GameState state, CityState city, DistrictType type, int turns)
        {
            var occupiedTiles = state.Districts.Select(item => item.TileId).ToArray();
            var tile = state.Tiles.First(item => item.CityId == city.Id && !occupiedTiles.Contains(item.Id));
            var district = new DistrictState
            {
                Id = state.AllocateId(), CityId = city.Id, TileId = tile.Id, Type = type,
                ControllerId = city.OwnerId, IsOperational = turns == 0,
                AssignedCitizens = 1, RemainingConstructionTurns = turns
            };
            state.Districts.Add(district);
            return district;
        }
    }
}
