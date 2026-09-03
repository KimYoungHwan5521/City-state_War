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
            Assert.That(NeutralCultureResolver.EffectiveInfluence(source, distant), Is.Zero);
            source.LastCultureProduction = 3;
            Assert.That(NeutralCultureResolver.EffectiveInfluence(source, distant), Is.EqualTo(-3));
        }

        [Test]
        public void RelationshipChangesResistanceAndSubjectCityRelaysCulture()
        {
            var fixture = CreateFixture();
            fixture.CityOne.LastCultureProduction = 1;
            var relay = fixture.State.Cities.First(item => item.OwnerId != fixture.One.Id &&
                item.OwnerId != fixture.Two.Id && item.Id != fixture.NeutralCity.Id);
            relay.CultureSubjectToId = fixture.One.Id;
            relay.LastCultureProduction = 20;
            relay.WorldQ = fixture.NeutralCity.WorldQ + 1;
            relay.WorldR = fixture.NeutralCity.WorldR;

            NeutralCityRules.SetFavor(fixture.NeutralCity, fixture.One.Id, -3);
            Assert.That(NeutralCultureResolver.RelationshipResistance(fixture.NeutralCity, fixture.One.Id), Is.EqualTo(10));
            Assert.That(NeutralCultureResolver.EffectiveInfluence(fixture.State, fixture.One.Id, fixture.NeutralCity), Is.EqualTo(20));
            NeutralCityRules.SetFavor(fixture.NeutralCity, fixture.One.Id, 3);
            Assert.That(NeutralCultureResolver.RelationshipResistance(fixture.NeutralCity, fixture.One.Id), Is.Zero);
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
            Assert.That(fixture.NeutralCity.CultureSubjectToId.IsValid, Is.False);
            Assert.That(NeutralCityRules.Favor(fixture.NeutralCity, fixture.One.Id), Is.EqualTo(1));
            NeutralCultureResolver.Advance(fixture.State);
            NeutralCultureResolver.Advance(fixture.State);
            record = NeutralCultureResolver.Advance(fixture.State).Single(item =>
                item.CityId == fixture.NeutralCity.Id);
            Assert.That(fixture.NeutralCity.CultureSubjectToId, Is.EqualTo(fixture.One.Id));
            Assert.That(record.SubjectToId, Is.EqualTo(fixture.One.Id));
            Assert.That(NeutralCityRules.Favor(fixture.NeutralCity, fixture.One.Id), Is.EqualTo(4));
        }

        [Test]
        public void PillageLevelFavorRecoversGraduallyDespiteForeignMajority()
        {
            var fixture = CreateFixture();
            CityCultureRules.GetOrCreate(fixture.NeutralCity, fixture.One.Id).PreferredCitizens = 3;
            fixture.NeutralCity.CultureSubjectToId = fixture.One.Id;
            NeutralCityRules.SetFavor(fixture.NeutralCity, fixture.One.Id, -10);
            fixture.CityOne.LastCultureProduction = 100;
            fixture.CityTwo.LastCultureProduction = 0;

            NeutralCultureResolver.Advance(fixture.State);

            Assert.That(NeutralCityRules.Favor(fixture.NeutralCity, fixture.One.Id), Is.EqualTo(-9));
            Assert.That(fixture.NeutralCity.CultureSubjectToId.IsValid, Is.False);
        }

        [Test]
        public void HostileResistanceTenGraduallyReclaimsForeignCitizenWhileRelationsRecover()
        {
            var fixture = CreateFixture();
            var influence = CityCultureRules.GetOrCreate(fixture.NeutralCity, fixture.One.Id);
            influence.PreferredCitizens = 3;
            fixture.CityOne.LastCultureProduction = 8;
            fixture.CityTwo.LastCultureProduction = 0;
            fixture.NeutralCity.LastCultureProduction = 1;
            NeutralCityRules.SetFavor(fixture.NeutralCity, fixture.One.Id, -10);

            for (var turn = 0; turn < 5; turn++) NeutralCultureResolver.Advance(fixture.State);

            Assert.That(influence.PreferredCitizens, Is.EqualTo(2));
            Assert.That(NeutralCityRules.Favor(fixture.NeutralCity, fixture.One.Id), Is.EqualTo(-6));
            Assert.That(fixture.NeutralCity.CultureSubjectToId.IsValid, Is.False);
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
                State = state, One = one, Two = two,
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
            public PlayerState Two;
            public CityState CityOne;
            public CityState CityTwo;
            public CityState NeutralCity;
        }
    }
}
