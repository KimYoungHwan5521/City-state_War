using System.Linq;
using LittleCiv.Core;
using NUnit.Framework;

namespace LittleCiv.Tests
{
    public sealed class NeutralCultureResolverTests
    {
        [Test]
        public void ResistanceIsTwoPlusOperationalCultureDistrictProduction()
        {
            var fixture = CreateFixture();
            CityEconomyResolver.ResolveProduction(fixture.State);
            Assert.That(NeutralCultureResolver.Resistance(fixture.State, fixture.NeutralCity), Is.EqualTo(2));
            AddCultureDistrict(fixture.State, fixture.NeutralCity, fixture.NeutralCity.OwnerId);
            CityEconomyResolver.ResolveProduction(fixture.State);

            Assert.That(NeutralCultureResolver.Resistance(fixture.State, fixture.NeutralCity), Is.EqualTo(4));
        }

        [Test]
        public void EffectiveInfluenceSubtractsDistanceBeyondAdjacent()
        {
            var source = new CityState { WorldQ = 0, WorldR = 0, LastCultureProduction = 6 };
            var adjacent = new CityState { WorldQ = 1, WorldR = 0 };
            var distant = new CityState { WorldQ = 3, WorldR = 0 };

            Assert.That(NeutralCultureResolver.EffectiveInfluence(source, adjacent), Is.EqualTo(6));
            Assert.That(NeutralCultureResolver.EffectiveInfluence(source, distant), Is.EqualTo(4));
        }

        [Test]
        public void HighestScoreTieProducesNoConversion()
        {
            var fixture = CreateFixture();
            fixture.CityOne.LastCultureProduction = 2;
            fixture.CityTwo.LastCultureProduction = 2;
            fixture.NeutralCity.LastCultureProduction = 1;

            var record = NeutralCultureResolver.Advance(fixture.State).Single(item =>
                item.CityId == fixture.NeutralCity.Id);

            Assert.That(record.AppliedInfluence, Is.Zero);
            Assert.That(fixture.NeutralCity.CultureInfluences, Is.Empty);
        }

        [Test]
        public void PlayerMajorityCreatesCulturalSubordination()
        {
            var fixture = CreateFixture();
            fixture.CityOne.LastCultureProduction = 32;
            fixture.CityTwo.LastCultureProduction = 0;
            fixture.NeutralCity.LastCultureProduction = 1;

            var record = NeutralCultureResolver.Advance(fixture.State).Single(item =>
                item.CityId == fixture.NeutralCity.Id);

            Assert.That(CityCultureRules.PreferredCitizens(fixture.NeutralCity, fixture.One.Id),
                Is.EqualTo(3));
            Assert.That(fixture.NeutralCity.CultureSubjectToId, Is.EqualTo(fixture.One.Id));
            Assert.That(record.SubjectToId, Is.EqualTo(fixture.One.Id));
        }

        [Test]
        public void NeutralResistanceReclaimsCitizenAndReleasesSubordinationAtHalf()
        {
            var fixture = CreateFixture();
            var influence = CityCultureRules.GetOrCreate(fixture.NeutralCity, fixture.One.Id);
            influence.PreferredCitizens = 3;
            influence.ReversionProgress = 8;
            fixture.NeutralCity.CultureSubjectToId = fixture.One.Id;
            fixture.CityOne.LastCultureProduction = 0;
            fixture.CityTwo.LastCultureProduction = 0;
            fixture.NeutralCity.LastCultureProduction = 1;

            NeutralCultureResolver.Advance(fixture.State);

            Assert.That(influence.PreferredCitizens, Is.EqualTo(2));
            Assert.That(fixture.NeutralCity.CultureSubjectToId.IsValid, Is.False);
        }

        [Test]
        public void SubordinationStateSurvivesCopyAndChangesHash()
        {
            var fixture = CreateFixture();
            fixture.NeutralCity.CultureSubjectToId = fixture.One.Id;
            var copy = GameStateCopy.Clone(fixture.State);

            Assert.That(GameStateHasher.Compute(copy), Is.EqualTo(GameStateHasher.Compute(fixture.State)));
            copy.Cities.Single(item => item.Id == fixture.NeutralCity.Id).CultureSubjectToId = default;
            Assert.That(GameStateHasher.Compute(copy), Is.Not.EqualTo(GameStateHasher.Compute(fixture.State)));
        }

        private static Fixture CreateFixture()
        {
            var state = PrototypeMatchFactory.Create(12200);
            var one = state.Players.Single(item => item.Slot == PlayerSlot.PlayerOne);
            var two = state.Players.Single(item => item.Slot == PlayerSlot.PlayerTwo);
            var neutral = state.Players.Single(item => item.Slot == PlayerSlot.Neutral);
            return new Fixture
            {
                State = state, One = one,
                CityOne = state.Cities.Single(item => item.OwnerId == one.Id),
                CityTwo = state.Cities.Single(item => item.OwnerId == two.Id),
                NeutralCity = state.Cities.First(item => item.OwnerId == neutral.Id)
            };
        }

        private static void AddCultureDistrict(GameState state, CityState city, EntityId ownerId)
        {
            var placement = state.MapTopology.FindView(city.Id).Tiles.First(item =>
                item.IsBuildable && state.Districts.All(district => district.TileId != item.TileId));
            state.Districts.Add(new DistrictState
            {
                Id = state.AllocateId(), CityId = city.Id, TileId = placement.TileId,
                Type = DistrictType.Culture, ControllerId = ownerId,
                AssignedCitizens = 1, IsOperational = true
            });
        }

        private sealed class Fixture
        {
            public GameState State;
            public PlayerState One;
            public CityState CityOne;
            public CityState CityTwo;
            public CityState NeutralCity;
        }
    }
}
