using System.Linq;
using LittleCiv.Core;
using NUnit.Framework;

namespace LittleCiv.Tests
{
    public sealed class NuclearProjectResolverTests
    {
        [Test]
        public void FissionAndOperationalFacilityAreRequiredAndStartingCostsTenGold()
        {
            var fixture = CreateFixture();
            var command = Command(fixture);
            fixture.Player.CompletedResearch.Remove(ResearchType.NuclearFission);
            Assert.That(NuclearProjectResolver.TryStart(fixture.State, command, out _), Is.False);
            fixture.Player.CompletedResearch.Add(ResearchType.NuclearFission);

            Assert.That(NuclearProjectResolver.TryStart(fixture.State, command, out var project), Is.True);
            Assert.That(fixture.City.Gold, Is.EqualTo(10));
            Assert.That(project.RemainingTurns, Is.EqualTo(3));
        }

        [Test]
        public void ProjectCompletesAfterThreeOperationalTurnsAndMarksScienceCandidate()
        {
            var fixture = CreateFixture();
            NuclearProjectResolver.TryStart(fixture.State, Command(fixture), out var project);

            Assert.That(NuclearProjectResolver.Advance(fixture.State).Single().RemainingTurns, Is.EqualTo(4));
            Assert.That(NuclearProjectResolver.Advance(fixture.State).Single().RemainingTurns, Is.EqualTo(3));
            Assert.That(NuclearProjectResolver.Advance(fixture.State).Single().RemainingTurns, Is.EqualTo(2));
            Assert.That(NuclearProjectResolver.Advance(fixture.State).Single().RemainingTurns, Is.EqualTo(1));
            var final = NuclearProjectResolver.Advance(fixture.State).Single();

            Assert.That(final.Completed, Is.True);
            Assert.That(project.IsCompleted, Is.True);
            Assert.That(fixture.Player.HasCompletedNuclearProject, Is.True);
            Assert.That(fixture.State.Victory, Is.EqualTo(VictoryType.None));
        }

        [Test]
        public void OccupationPillageAndMaintenanceSuspensionPauseProject()
        {
            var fixture = CreateFixture();
            NuclearProjectResolver.TryStart(fixture.State, Command(fixture), out var project);
            fixture.District.ControllerId = fixture.State.Players.Single(item =>
                item.Slot == PlayerSlot.PlayerTwo).Id;
            fixture.District.IsOperational = false;
            fixture.District.IsPillaged = true;

            Assert.That(NuclearProjectResolver.Advance(fixture.State), Is.Empty);
            Assert.That(project.RemainingTurns, Is.EqualTo(3));

            fixture.District.ControllerId = fixture.City.OwnerId;
            fixture.District.IsPillaged = false;
            fixture.District.IsMaintenanceSuspended = true;
            Assert.That(NuclearProjectResolver.Advance(fixture.State), Is.Empty);
            Assert.That(project.RemainingTurns, Is.EqualTo(3));
        }

        [Test]
        public void ProjectStateSurvivesCopyAndChangesDeterministicHash()
        {
            var fixture = CreateFixture();
            NuclearProjectResolver.TryStart(fixture.State, Command(fixture), out var project);
            var copy = GameStateCopy.Clone(fixture.State);
            Assert.That(GameStateHasher.Compute(copy), Is.EqualTo(GameStateHasher.Compute(fixture.State)));

            copy.NuclearProjects.Single(item => item.Id == project.Id).RemainingTurns--;
            Assert.That(GameStateHasher.Compute(copy), Is.Not.EqualTo(GameStateHasher.Compute(fixture.State)));
        }

        private static Fixture CreateFixture()
        {
            var state = PrototypeMatchFactory.Create(9100);
            var player = state.Players.Single(item => item.Slot == PlayerSlot.PlayerOne);
            player.CompletedResearch.Add(ResearchType.NuclearFission);
            var city = state.Cities.Single(item => item.OwnerId == player.Id);
            city.Gold = 20;
            var tile = state.Tiles.First(item => item.CityId == city.Id &&
                state.Districts.All(district => district.TileId != item.Id));
            var district = new DistrictState
            {
                Id = state.AllocateId(), CityId = city.Id, TileId = tile.Id,
                Type = DistrictType.NuclearFacility, ControllerId = player.Id,
                IsOperational = true, AssignedCitizens = 1
            };
            state.Districts.Add(district);
            return new Fixture { State = state, Player = player, City = city, District = district };
        }

        private static GameCommand Command(Fixture fixture) => new GameCommand
        {
            CommandId = fixture.State.AllocateId(), PlayerId = fixture.Player.Id,
            TurnNumber = fixture.State.TurnNumber, Type = GameCommandType.StartNuclearProject,
            SubjectId = fixture.District.Id
        };

        private sealed class Fixture
        {
            public GameState State;
            public PlayerState Player;
            public CityState City;
            public DistrictState District;
        }
    }
}
