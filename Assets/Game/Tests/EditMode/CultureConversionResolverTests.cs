using System.Linq;
using LittleCiv.Core;
using NUnit.Framework;

namespace LittleCiv.Tests
{
    public sealed class CultureConversionResolverTests
    {
        [Test]
        public void CultureDifferenceAccumulatesAndConvertsOneCitizenAtTen()
        {
            var fixture = CreateFixture();

            CultureConversionResolver.ApplyAdvantage(fixture.CityOne, fixture.CityTwo,
                fixture.One.Id, fixture.Two.Id, 4);
            var influence = CityCultureRules.GetOrCreate(fixture.CityTwo, fixture.One.Id);
            Assert.That(influence.ConversionProgress, Is.EqualTo(4));
            Assert.That(influence.PreferredCitizens, Is.Zero);

            CultureConversionResolver.ApplyAdvantage(fixture.CityOne, fixture.CityTwo,
                fixture.One.Id, fixture.Two.Id, 6);
            Assert.That(influence.ConversionProgress, Is.Zero);
            Assert.That(influence.PreferredCitizens, Is.EqualTo(1));
            Assert.That(CityCultureRules.NativeCitizens(fixture.CityTwo), Is.EqualTo(3));
        }

        [Test]
        public void ReversedAdvantageCancelsProgressThenReclaimsHomeBeforeAttacking()
        {
            var fixture = CreateFixture();
            var foreignAtHome = CityCultureRules.GetOrCreate(fixture.CityOne, fixture.Two.Id);
            foreignAtHome.PreferredCitizens = 1;
            foreignAtHome.ConversionProgress = 3;

            CultureConversionResolver.ApplyAdvantage(fixture.CityOne, fixture.CityTwo,
                fixture.One.Id, fixture.Two.Id, 8);
            Assert.That(foreignAtHome.ConversionProgress, Is.Zero);
            Assert.That(foreignAtHome.ReversionProgress, Is.EqualTo(5));
            Assert.That(CityCultureRules.PreferredCitizens(fixture.CityTwo, fixture.One.Id), Is.Zero);

            CultureConversionResolver.ApplyAdvantage(fixture.CityOne, fixture.CityTwo,
                fixture.One.Id, fixture.Two.Id, 5);
            Assert.That(foreignAtHome.PreferredCitizens, Is.Zero);
            Assert.That(foreignAtHome.ReversionProgress, Is.Zero);
            Assert.That(CityCultureRules.PreferredCitizens(fixture.CityTwo, fixture.One.Id), Is.Zero);

            CultureConversionResolver.ApplyAdvantage(fixture.CityOne, fixture.CityTwo,
                fixture.One.Id, fixture.Two.Id, 4);
            Assert.That(CityCultureRules.GetOrCreate(fixture.CityTwo, fixture.One.Id)
                .ConversionProgress, Is.EqualTo(4));
        }

        [Test]
        public void EqualCultureProductionDoesNotChangeEitherCity()
        {
            var fixture = CreateFixture();
            fixture.CityOne.LastCultureProduction = 3;
            fixture.CityTwo.LastCultureProduction = 3;

            var result = CultureConversionResolver.AdvancePlayerCities(fixture.State);

            Assert.That(result, Is.Empty);
            Assert.That(fixture.CityOne.CultureInfluences, Is.Empty);
            Assert.That(fixture.CityTwo.CultureInfluences, Is.Empty);
        }

        [Test]
        public void TurnCulturePhaseUsesFinalProductionAndEmitsChangeEvent()
        {
            var fixture = CreateFixture();
            var placement = fixture.State.MapTopology.FindView(fixture.CityOne.Id).Tiles.First(item =>
                item.IsBuildable && fixture.State.Districts.All(district => district.TileId != item.TileId));
            fixture.State.Districts.Add(new DistrictState
            {
                Id = fixture.State.AllocateId(), CityId = fixture.CityOne.Id, TileId = placement.TileId,
                Type = DistrictType.Culture, ControllerId = fixture.One.Id,
                AssignedCitizens = 1, IsOperational = true
            });

            var turn = new TurnProcessor().Resolve(fixture.State, new GameCommand[0]);
            var influence = CityCultureRules.GetOrCreate(fixture.CityTwo, fixture.One.Id);

            Assert.That(influence.ConversionProgress, Is.EqualTo(2));
            var reverse = CityCultureRules.GetOrCreate(fixture.CityOne, fixture.Two.Id);
            Assert.That(reverse.PreferredCitizens, Is.Zero);
            Assert.That(reverse.ConversionProgress, Is.Zero);
            Assert.That(turn.Events.Any(item => item.Type == GameEventType.CultureInfluenceChanged &&
                item.SourceId == fixture.One.Id && item.TargetId == fixture.CityTwo.Id), Is.True);
        }

        [Test]
        public void OccupiedCultureDistrictCannotCreateConversionPressure()
        {
            var fixture = CreateFixture();
            AddCultureDistrict(fixture.State, fixture.CityOne, fixture.One.Id);
            var culture = fixture.State.Districts.Single(item => item.CityId == fixture.CityOne.Id &&
                item.Type == DistrictType.Culture);
            culture.ControllerId = fixture.Two.Id;
            culture.IsOperational = false;

            new TurnProcessor().Resolve(fixture.State, new GameCommand[0]);

            Assert.That(CityCultureRules.PreferredCitizens(fixture.CityTwo, fixture.One.Id), Is.Zero);
            Assert.That(CityCultureRules.GetOrCreate(fixture.CityTwo, fixture.One.Id)
                .ConversionProgress, Is.Zero);
        }

        [Test]
        public void ExactHalfIsSafeButNextConversionTriggersCultureVictoryImmediately()
        {
            var fixture = CreateFixture();
            var influence = CityCultureRules.GetOrCreate(fixture.CityTwo, fixture.One.Id);
            influence.PreferredCitizens = 2;
            influence.ConversionProgress = 8;
            Assert.That(CultureVictoryConditionResolver.HasForeignMajority(
                fixture.CityTwo, fixture.One.Id), Is.False);
            AddCultureDistrict(fixture.State, fixture.CityOne, fixture.One.Id);

            var turn = new TurnProcessor().Resolve(fixture.State, new GameCommand[0]);

            Assert.That(influence.PreferredCitizens, Is.EqualTo(3));
            Assert.That(fixture.State.Victory, Is.EqualTo(VictoryType.Culture));
            Assert.That(fixture.State.WinnerId, Is.EqualTo(fixture.One.Id));
            Assert.That(turn.Events.Any(item => item.Type == GameEventType.VictoryTriggered &&
                item.PrimaryValue == (int)VictoryType.Culture), Is.True);
        }

        [Test]
        public void GrowthAddsNativeCitizenAndDoesNotIncreaseForeignPreference()
        {
            var fixture = CreateFixture();
            CityCultureRules.GetOrCreate(fixture.CityOne, fixture.Two.Id).PreferredCitizens = 2;
            fixture.CityOne.LastFoodProduction = 10;
            fixture.CityOne.LastUnitFoodConsumption = 0;
            fixture.CityOne.GrowthProgress = 11;

            CityPopulationResolver.ResolveGrowth(fixture.State);

            Assert.That(fixture.CityOne.Population, Is.EqualTo(5));
            Assert.That(CityCultureRules.PreferredCitizens(fixture.CityOne, fixture.Two.Id), Is.EqualTo(2));
            Assert.That(CityCultureRules.NativeCitizens(fixture.CityOne), Is.EqualTo(3));
        }

        [Test]
        public void FamineRemovesNativeCitizenAndMajorityWinsOnFollowingTurn()
        {
            var fixture = CreateFixture();
            CityCultureRules.GetOrCreate(fixture.CityOne, fixture.Two.Id).PreferredCitizens = 2;
            var government = fixture.State.Districts.Single(item => item.CityId == fixture.CityOne.Id &&
                item.Type == DistrictType.Government);
            government.IsOperational = false;
            fixture.CityOne.FamineProgress = 4;
            var processor = new TurnProcessor();

            processor.Resolve(fixture.State, new GameCommand[0]);

            Assert.That(fixture.State.Victory, Is.EqualTo(VictoryType.None));
            Assert.That(fixture.CityOne.Population, Is.EqualTo(3));
            Assert.That(CityCultureRules.PreferredCitizens(fixture.CityOne, fixture.Two.Id), Is.EqualTo(2));
            Assert.That(CityCultureRules.NativeCitizens(fixture.CityOne), Is.EqualTo(1));

            processor.Resolve(fixture.State, new GameCommand[0]);
            Assert.That(fixture.State.Victory, Is.EqualTo(VictoryType.Culture));
            Assert.That(fixture.State.WinnerId, Is.EqualTo(fixture.Two.Id));
        }

        private static Fixture CreateFixture()
        {
            var state = PrototypeMatchFactory.Create(12100);
            var one = state.Players.Single(item => item.Slot == PlayerSlot.PlayerOne);
            var two = state.Players.Single(item => item.Slot == PlayerSlot.PlayerTwo);
            return new Fixture
            {
                State = state, One = one, Two = two,
                CityOne = state.Cities.Single(item => item.OwnerId == one.Id),
                CityTwo = state.Cities.Single(item => item.OwnerId == two.Id)
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
        }
    }
}
