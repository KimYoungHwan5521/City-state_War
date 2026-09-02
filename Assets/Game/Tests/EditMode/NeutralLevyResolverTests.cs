using System.Linq;
using LittleCiv.Core;
using NUnit.Framework;

namespace LittleCiv.Tests
{
    public sealed class NeutralLevyResolverTests
    {
        [TestCase(0, 5)]
        [TestCase(1, 3)]
        [TestCase(2, 3)]
        [TestCase(3, 2)]
        public void LevyPriceUsesRelationshipDiscountAndRoundsUp(int favor, int expected)
        {
            var fixture = Create(14100 + favor);
            AddNeutralUnit(fixture, UnitType.Supply);
            if (favor == 3) fixture.MilitaryCity.CultureSubjectToId = fixture.Player.Id;
            NeutralCityRules.SetFavor(fixture.MilitaryCity, fixture.Player.Id, favor);

            var quote = NeutralLevyResolver.Quote(fixture.State, fixture.Player.Id,
                fixture.Home.Id, fixture.MilitaryCity.Id);

            Assert.That(quote.IsAvailable, Is.True);
            Assert.That(quote.FullUnitValue, Is.EqualTo(5));
            Assert.That(quote.BasePrice, Is.EqualTo(expected));
        }

        [Test]
        public void HostileCityCannotBeLevied()
        {
            var fixture = Create(14110);
            NeutralCityRules.SetFavor(fixture.MilitaryCity, fixture.Player.Id, -1);

            var quote = NeutralLevyResolver.Quote(fixture.State, fixture.Player.Id,
                fixture.Home.Id, fixture.MilitaryCity.Id);

            Assert.That(quote.IsAvailable, Is.False);
            Assert.That(quote.Failure, Is.EqualTo(LevyQuoteFailure.Hostile));
        }

        [Test]
        public void StartingLevyTransfersControlFullFoodAndMaintenanceHomeForThirtyTurns()
        {
            var fixture = Create(14111);
            fixture.State.Units.RemoveAll(item => item.HomeCityId == fixture.Home.Id);
            var unit = fixture.State.Units.Single(item => item.HomeCityId == fixture.MilitaryCity.Id);
            unit.CarriedFood = 0;
            var beforeGold = fixture.Home.Gold;

            Assert.That(NeutralLevyResolver.TryStart(fixture.State, fixture.Player.Id,
                fixture.Home.Id, fixture.MilitaryCity.Id, 3, out var levy), Is.True);

            Assert.That(unit.OwnerId, Is.EqualTo(fixture.Player.Id));
            Assert.That(unit.HomeCityId, Is.EqualTo(fixture.Home.Id));
            Assert.That(unit.CarriedFood, Is.EqualTo(6));
            Assert.That(fixture.Home.Gold, Is.EqualTo(beforeGold - 3));
            Assert.That(levy.EndTurnExclusive - levy.StartTurn, Is.EqualTo(30));
            Assert.That(NeutralCityRules.Favor(fixture.MilitaryCity, fixture.Player.Id), Is.EqualTo(1));

            var goldBeforeMaintenance = fixture.Home.Gold;
            MaintenanceResolver.Resolve(fixture.State);
            Assert.That(fixture.Home.Gold, Is.EqualTo(goldBeforeMaintenance - 1));
        }

        [Test]
        public void LevyProtectsOriginAndReturnsSurvivorsAtExclusiveEndTurn()
        {
            var fixture = Create(14112);
            Assert.That(NeutralLevyResolver.TryStart(fixture.State, fixture.Player.Id,
                fixture.Home.Id, fixture.MilitaryCity.Id, 3, out var levy), Is.True);
            var unit = fixture.State.Units.Single(item => item.Id == levy.Units[0].UnitId);
            var government = fixture.State.Districts.Single(item =>
                item.CityId == fixture.MilitaryCity.Id && item.Type == DistrictType.Government);

            Assert.That(NeutralLevyResolver.IsProtectedCityTile(fixture.State,
                fixture.Player.Id, government.TileId), Is.True);
            Assert.That(OccupationResolver.Resolve(fixture.State,
                fixture.Player.Id, government.TileId).DistrictOccupied, Is.False);
            fixture.State.TurnNumber = levy.EndTurnExclusive - 1;
            Assert.That(NeutralLevyResolver.ReturnExpired(fixture.State), Is.Empty);
            fixture.State.TurnNumber++;
            var returned = NeutralLevyResolver.ReturnExpired(fixture.State).Single();

            Assert.That(returned.ReturnedUnits, Is.EqualTo(1));
            Assert.That(unit.OwnerId, Is.EqualTo(fixture.MilitaryCity.OwnerId));
            Assert.That(unit.HomeCityId, Is.EqualTo(fixture.MilitaryCity.Id));
            Assert.That(unit.TileId, Is.EqualTo(government.TileId));
            Assert.That(unit.CarriedFood, Is.EqualTo(6));
            Assert.That(fixture.State.Levies, Is.Empty);
        }

        [Test]
        public void LevyStateSurvivesCopyAndChangesHash()
        {
            var fixture = Create(14113);
            Assert.That(NeutralLevyResolver.TryStart(fixture.State, fixture.Player.Id,
                fixture.Home.Id, fixture.MilitaryCity.Id, 3, out _), Is.True);
            var copy = GameStateCopy.Clone(fixture.State);

            Assert.That(GameStateHasher.Compute(copy), Is.EqualTo(GameStateHasher.Compute(fixture.State)));
            copy.Levies[0].EndTurnExclusive++;
            Assert.That(GameStateHasher.Compute(copy), Is.Not.EqualTo(GameStateHasher.Compute(fixture.State)));
        }

        private static Fixture Create(long seed)
        {
            var state = PrototypeMatchFactory.Create(seed);
            var player = state.Players.Single(item => item.Slot == PlayerSlot.PlayerOne);
            var neutral = state.Players.Single(item => item.Slot == PlayerSlot.Neutral);
            var home = state.Cities.Single(item => item.OwnerId == player.Id);
            home.Gold = 100;
            return new Fixture
            {
                State = state, Player = player, Home = home,
                MilitaryCity = state.Cities.First(item => item.OwnerId == neutral.Id &&
                    item.NeutralSpecialization == NeutralCitySpecialization.Military)
            };
        }

        private static void AddNeutralUnit(Fixture fixture, UnitType type)
        {
            var government = fixture.State.Districts.Single(item =>
                item.CityId == fixture.MilitaryCity.Id && item.Type == DistrictType.Government);
            fixture.State.Units.Add(new UnitState
            {
                Id = fixture.State.AllocateId(), OwnerId = fixture.MilitaryCity.OwnerId,
                HomeCityId = fixture.MilitaryCity.Id, TileId = government.TileId,
                Type = type, HitPoints = UnitRules.MaximumHitPoints(type)
            });
        }

        private sealed class Fixture
        {
            public GameState State;
            public PlayerState Player;
            public CityState Home;
            public CityState MilitaryCity;
        }
    }
}
