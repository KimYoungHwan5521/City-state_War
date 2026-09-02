using System.Linq;
using LittleCiv.Core;
using NUnit.Framework;

namespace LittleCiv.Tests
{
    public sealed class NeutralOccupationResolverTests
    {
        [Test]
        public void NeutralGovernmentOccupationDoesNotTriggerConquestVictory()
        {
            var fixture = Create(13900, NeutralCitySpecialization.Science);
            fixture.Government.ControllerId = fixture.City.OwnerId;
            fixture.State.Tiles.Single(item => item.Id == fixture.Government.TileId).ControllerId =
                fixture.City.OwnerId;
            fixture.City.OccupyingPlayerId = default;

            var result = OccupationResolver.Resolve(fixture.State, fixture.Player.Id,
                fixture.Government.TileId);

            Assert.That(result.DistrictOccupied, Is.True);
            Assert.That(result.ConquestVictoryTriggered, Is.False);
            Assert.That(fixture.State.IsGameOver, Is.False);
            Assert.That(fixture.City.OccupyingPlayerId, Is.EqualTo(fixture.Player.Id));
        }

        [Test]
        public void RequiredStrengthUsesPopulationAndMilitarySpecialization()
        {
            var state = PrototypeMatchFactory.Create(13901);
            var neutral = state.Players.Single(item => item.Slot == PlayerSlot.Neutral);
            var normal = state.Cities.First(item => item.OwnerId == neutral.Id &&
                item.NeutralSpecialization == NeutralCitySpecialization.Science);
            var military = state.Cities.First(item => item.OwnerId == neutral.Id &&
                item.NeutralSpecialization == NeutralCitySpecialization.Military);
            normal.Population = military.Population = 5;

            Assert.That(NeutralOccupationResolver.RequiredStrength(normal), Is.EqualTo(3));
            Assert.That(NeutralOccupationResolver.RequiredStrength(military), Is.EqualTo(5));
        }

        [Test]
        public void AllOccupierUnitsAcrossCityTerritoryContributeIncludingSupply()
        {
            var fixture = Create(13902, NeutralCitySpecialization.Military);
            AddOccupierUnit(fixture, UnitType.Supply, fixture.Government.TileId);
            var otherTile = fixture.State.Tiles.First(item => item.CityId == fixture.City.Id &&
                item.Id != fixture.Government.TileId);
            AddOccupierUnit(fixture, UnitType.Militia, otherTile.Id);

            Assert.That(NeutralOccupationResolver.GarrisonStrength(fixture.State, fixture.City),
                Is.EqualTo(4));
            Assert.That(NeutralOccupationResolver.Resolve(fixture.State).Single().IndependenceProgress,
                Is.Zero);
        }

        [Test]
        public void TwoConsecutiveWeakTurnsCauseRebellionAndNeutralMilitia()
        {
            var fixture = Create(13903, NeutralCitySpecialization.Military);
            AddOccupierUnit(fixture, UnitType.Militia, fixture.Government.TileId);
            var neutralUnitsBefore = fixture.State.Units.Count(item => item.OwnerId == fixture.City.OwnerId);

            var first = NeutralOccupationResolver.Resolve(fixture.State).Single();
            var second = NeutralOccupationResolver.Resolve(fixture.State).Single();

            Assert.That(first.IndependenceProgress, Is.EqualTo(1));
            Assert.That(second.Rebelled, Is.True);
            Assert.That(second.RebelUnitId.IsValid, Is.True);
            Assert.That(fixture.City.OccupyingPlayerId.IsValid, Is.False);
            Assert.That(fixture.Government.ControllerId, Is.EqualTo(fixture.City.OwnerId));
            Assert.That(fixture.State.Units.Count(item => item.OwnerId == fixture.City.OwnerId),
                Is.EqualTo(neutralUnitsBefore + 1));
        }

        [Test]
        public void ReinforcementBeforeSecondWeakTurnResetsIndependenceProgress()
        {
            var fixture = Create(13904, NeutralCitySpecialization.Military);
            AddOccupierUnit(fixture, UnitType.Supply, fixture.Government.TileId);
            NeutralOccupationResolver.Resolve(fixture.State);
            AddOccupierUnit(fixture, UnitType.IronInfantry, fixture.Government.TileId);

            var record = NeutralOccupationResolver.Resolve(fixture.State).Single();

            Assert.That(record.GarrisonStrength, Is.EqualTo(6));
            Assert.That(record.IndependenceProgress, Is.Zero);
            Assert.That(record.Rebelled, Is.False);
        }

        [Test]
        public void EmptyOccupiedGovernmentKeepsControlForOneTurnGrace()
        {
            var fixture = Create(13905, NeutralCitySpecialization.Science);
            var occupier = AddOccupierUnit(fixture, UnitType.Militia, fixture.Government.TileId);
            var home = fixture.State.Cities.Single(item => item.OwnerId == fixture.Player.Id);
            occupier.TileId = fixture.State.Districts.Single(item => item.CityId == home.Id &&
                item.Type == DistrictType.Government).TileId;

            OccupationResolver.ReleaseVacatedDistricts(fixture.State);
            var record = NeutralOccupationResolver.Resolve(fixture.State).Single();

            Assert.That(fixture.Government.ControllerId, Is.EqualTo(fixture.Player.Id));
            Assert.That(record.IndependenceProgress, Is.EqualTo(1));
        }

        [Test]
        public void OccupationStateSurvivesCopyAndChangesHash()
        {
            var fixture = Create(13906, NeutralCitySpecialization.Science);
            fixture.City.IndependenceProgress = 1;
            var copy = GameStateCopy.Clone(fixture.State);

            Assert.That(GameStateHasher.Compute(copy), Is.EqualTo(GameStateHasher.Compute(fixture.State)));
            copy.Cities.Single(item => item.Id == fixture.City.Id).IndependenceProgress = 0;
            Assert.That(GameStateHasher.Compute(copy), Is.Not.EqualTo(GameStateHasher.Compute(fixture.State)));
        }

        private static Fixture Create(long seed, NeutralCitySpecialization specialization)
        {
            var state = PrototypeMatchFactory.Create(seed);
            var player = state.Players.Single(item => item.Slot == PlayerSlot.PlayerOne);
            var neutral = state.Players.Single(item => item.Slot == PlayerSlot.Neutral);
            var city = state.Cities.First(item => item.OwnerId == neutral.Id &&
                item.NeutralSpecialization == specialization);
            var government = state.Districts.Single(item => item.CityId == city.Id &&
                item.Type == DistrictType.Government);
            state.Units.RemoveAll(item => item.HomeCityId == city.Id);
            government.ControllerId = player.Id;
            state.Tiles.Single(item => item.Id == government.TileId).ControllerId = player.Id;
            city.OccupyingPlayerId = player.Id;
            return new Fixture
            {
                State = state, Player = player, City = city, Government = government
            };
        }

        private static UnitState AddOccupierUnit(Fixture fixture, UnitType type, EntityId tileId)
        {
            var unit = new UnitState
            {
                Id = fixture.State.AllocateId(), OwnerId = fixture.Player.Id,
                HomeCityId = fixture.State.Cities.Single(item => item.OwnerId == fixture.Player.Id).Id,
                TileId = tileId, Type = type, HitPoints = UnitRules.MaximumHitPoints(type)
            };
            fixture.State.Units.Add(unit);
            return unit;
        }

        private sealed class Fixture
        {
            public GameState State;
            public PlayerState Player;
            public CityState City;
            public DistrictState Government;
        }
    }
}
