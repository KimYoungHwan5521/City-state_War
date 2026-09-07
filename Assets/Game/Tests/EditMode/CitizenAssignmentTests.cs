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

        [Test]
        public void CitiesStartWithCitizenAutoAssignmentEnabledAndManualChangesAreRejected()
        {
            var state = PrototypeMatchFactory.Create(4705);
            var city = state.Cities[0];
            var district = AddDistrict(state, city, DistrictType.Science, 0);
            var command = new GameCommand
            {
                CommandId = state.AllocateId(), PlayerId = city.OwnerId,
                TurnNumber = state.TurnNumber, Type = GameCommandType.AssignCitizen,
                SubjectId = district.Id, PrimaryValue = 0, SecondaryValue = -1
            };

            Assert.That(city.CitizenAutoAssignment, Is.True);
            Assert.That(CitizenAssignmentResolver.TryAssignManually(state, command), Is.False);
            Assert.That(district.AssignedCitizens, Is.EqualTo(1));
        }

        [Test]
        public void ManualModeCanMoveCitizenFromCompletedDistrictToEmptyDistrict()
        {
            var state = PrototypeMatchFactory.Create(4706);
            var city = state.Cities[0];
            var source = AddDistrict(state, city, DistrictType.Science, 0);
            var target = AddDistrict(state, city, DistrictType.Commerce, 0);
            target.AssignedCitizens = 0;
            target.IsOperational = false;
            city.CitizenAutoAssignment = false;

            Assert.That(CitizenAssignmentResolver.TryAssignManually(state, Assignment(state, city, source, 0)),
                Is.True);
            Assert.That(CitizenAssignmentResolver.TryAssignManually(state, Assignment(state, city, target, 1)),
                Is.True);
            Assert.That(source.AssignedCitizens, Is.Zero);
            Assert.That(source.IsOperational, Is.False);
            Assert.That(target.AssignedCitizens, Is.EqualTo(1));
            Assert.That(target.IsOperational, Is.True);
        }

        [Test]
        public void SameTurnManualModeRecallRunsBeforeNewDistrictConstruction()
        {
            var state = PrototypeMatchFactory.Create(4707);
            var city = state.Cities[0];
            var source = AddDistrict(state, city, DistrictType.Science, 0);
            AddDistrict(state, city, DistrictType.Culture, 0);
            AddDistrict(state, city, DistrictType.Commerce, 0);
            Assert.That(DistrictConstructionResolver.CountFreeCitizens(state, city), Is.Zero);
            var occupied = state.Districts.Select(item => item.TileId).ToArray();
            var target = state.MapTopology.FindView(city.Id).Tiles.First(item =>
                item.IsBuildable && !occupied.Contains(item.TileId)).TileId;
            var toggle = new GameCommand
            {
                CommandId = state.AllocateId(), PlayerId = city.OwnerId,
                TurnNumber = state.TurnNumber, Type = GameCommandType.SetCitizenAutoAssignment,
                SubjectId = city.Id, PrimaryValue = 0
            };
            var recall = Assignment(state, city, source, 0);
            var construction = new GameCommand
            {
                CommandId = state.AllocateId(), PlayerId = city.OwnerId,
                TurnNumber = state.TurnNumber, Type = GameCommandType.StartDistrict,
                SubjectId = city.Id, TargetId = target, PrimaryValue = (int)DistrictType.Agriculture
            };

            new TurnProcessor().Resolve(state, new[] { construction, recall, toggle });

            Assert.That(city.CitizenAutoAssignment, Is.False);
            Assert.That(source.AssignedCitizens, Is.Zero);
            Assert.That(state.Districts.Any(item => item.TileId == target &&
                item.Type == DistrictType.Agriculture && item.AssignedCitizens == 1), Is.True);
        }

        [Test]
        public void CitizenAutoAssignmentSurvivesCopyAndAffectsDeterministicHash()
        {
            var state = PrototypeMatchFactory.Create(4708);
            var city = state.Cities[0];
            var automaticHash = GameStateHasher.Compute(state);

            city.CitizenAutoAssignment = false;
            var manualHash = GameStateHasher.Compute(state);
            var copy = GameStateCopy.Clone(state);

            Assert.That(manualHash, Is.Not.EqualTo(automaticHash));
            Assert.That(copy.Cities.Find(item => item.Id == city.Id).CitizenAutoAssignment, Is.False);
            Assert.That(GameStateHasher.Compute(copy), Is.EqualTo(manualHash));
        }

        private static GameCommand Assignment(GameState state, CityState city,
            DistrictState district, int desired)
        {
            return new GameCommand
            {
                CommandId = state.AllocateId(), PlayerId = city.OwnerId,
                TurnNumber = state.TurnNumber, Type = GameCommandType.AssignCitizen,
                SubjectId = district.Id, PrimaryValue = desired,
                SecondaryValue = desired.CompareTo(district.AssignedCitizens)
            };
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
