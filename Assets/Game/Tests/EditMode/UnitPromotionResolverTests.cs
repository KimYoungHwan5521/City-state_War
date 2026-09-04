using System.Linq;
using LittleCiv.Core;
using NUnit.Framework;

namespace LittleCiv.Tests
{
    public sealed class UnitPromotionResolverTests
    {
        [Test]
        public void PromotionPaysTrainingDifferenceAndPreservesHealthRatioFoodAndStarvation()
        {
            var fixture = CreateFixture(10000);
            fixture.Player.UnlockedUnitTypes.Add(UnitType.GunpowderInfantry);
            fixture.Unit.Type = UnitType.IronInfantry;
            fixture.Unit.HitPoints = 11;
            fixture.Unit.CarriedFood = 5;
            fixture.Unit.IsStarving = true;
            fixture.City.Gold = 10;
            UnitPromotionResult result;

            var accepted = UnitPromotionResolver.TryPromote(
                fixture.State, Command(fixture, UnitType.GunpowderInfantry), out result);

            Assert.That(accepted, Is.True);
            Assert.That(fixture.Unit.Type, Is.EqualTo(UnitType.GunpowderInfantry));
            Assert.That(fixture.Unit.HitPoints, Is.EqualTo(13));
            Assert.That(fixture.Unit.CarriedFood, Is.EqualTo(5));
            Assert.That(fixture.Unit.IsStarving, Is.True);
            Assert.That(fixture.Unit.RemainingMovement, Is.Zero);
            Assert.That(fixture.City.Gold, Is.EqualTo(5));
            Assert.That(result.GoldCost, Is.EqualTo(5));
        }

        [Test]
        public void PromotionPreservesWoundedHealthPercentageWithoutHealingToFull()
        {
            var fixture = CreateFixture(10006);
            fixture.Player.UnlockedUnitTypes.Add(UnitType.MechanizedInfantry);
            fixture.Unit.HitPoints = 4;
            fixture.City.Gold = 100;
            UnitPromotionResult result;

            Assert.That(UnitPromotionResolver.TryPromote(
                fixture.State, Command(fixture, UnitType.MechanizedInfantry), out result), Is.True);
            Assert.That(fixture.Unit.HitPoints, Is.EqualTo(8));
        }

        [Test]
        public void PromotionCanSkipToHighestUnlockedTypeAndStillRequiresEnoughGold()
        {
            var fixture = CreateFixture(10001);
            UnitPromotionResult result;
            Assert.That(UnitPromotionResolver.TryPromote(
                fixture.State, Command(fixture, UnitType.IronInfantry), out result), Is.False);
            fixture.Player.UnlockedUnitTypes.Add(UnitType.GunpowderInfantry);
            Assert.That(UnitPromotionResolver.TryPromote(
                fixture.State, Command(fixture, UnitType.GunpowderInfantry), out result), Is.True);

            fixture = CreateFixture(10004);
            fixture.Player.UnlockedUnitTypes.Add(UnitType.IronInfantry);
            fixture.City.Gold = 2;
            Assert.That(UnitPromotionResolver.TryPromote(
                fixture.State, Command(fixture, UnitType.IronInfantry), out result), Is.False);
        }

        [Test]
        public void PromotionToLargerCapacityDoesNotFillNewFoodSpace()
        {
            var fixture = CreateFixture(10005);
            fixture.Player.UnlockedUnitTypes.Add(UnitType.MechanizedInfantry);
            fixture.Unit.CarriedFood = 2;
            fixture.City.Gold = 100;
            UnitPromotionResult result;

            Assert.That(UnitPromotionResolver.TryPromote(
                fixture.State, Command(fixture, UnitType.MechanizedInfantry), out result), Is.True);
            Assert.That(fixture.Unit.CarriedFood, Is.EqualTo(2));
            Assert.That(UnitRules.FoodCapacity(fixture.State, fixture.Unit), Is.GreaterThan(2));
        }

        [Test]
        public void PromotionRejectsSharedOccupiedOrNoMovementTile()
        {
            var fixture = CreateFixture(10002);
            fixture.Player.UnlockedUnitTypes.Add(UnitType.IronInfantry);
            UnitPromotionResult result;
            fixture.Tile.IsSharedBoundary = true;
            Assert.That(UnitPromotionResolver.TryPromote(
                fixture.State, Command(fixture, UnitType.IronInfantry), out result), Is.False);
            fixture.Tile.IsSharedBoundary = false;
            fixture.Tile.ControllerId = fixture.State.Cities[1].OwnerId;
            Assert.That(UnitPromotionResolver.TryPromote(
                fixture.State, Command(fixture, UnitType.IronInfantry), out result), Is.False);
            fixture.Tile.ControllerId = fixture.City.OwnerId;
            fixture.Unit.RemainingMovement = 0;
            Assert.That(UnitPromotionResolver.TryPromote(
                fixture.State, Command(fixture, UnitType.IronInfantry), out result), Is.False);
        }

        [Test]
        public void PromotionBeforeMovementPreventsSameTurnMoveAndEmitsEvent()
        {
            var fixture = CreateFixture(10003);
            fixture.Player.UnlockedUnitTypes.Add(UnitType.IronInfantry);
            fixture.City.Gold = 20;
            var destination = fixture.State.Tiles.First(item => item.CityId == fixture.City.Id &&
                item.Id != fixture.Tile.Id && MapTraversal.AreAdjacent(fixture.State, fixture.Tile.Id, item.Id));
            var promotion = Command(fixture, UnitType.IronInfantry);
            var move = new GameCommand
            {
                CommandId = fixture.State.AllocateId(), PlayerId = fixture.City.OwnerId,
                TurnNumber = fixture.State.TurnNumber, Type = GameCommandType.MoveUnit,
                SubjectId = fixture.Unit.Id, Path = { destination.Id }
            };

            var resolution = new TurnProcessor().Resolve(fixture.State, new[] { move, promotion });

            Assert.That(fixture.Unit.Type, Is.EqualTo(UnitType.IronInfantry));
            Assert.That(fixture.Unit.TileId, Is.EqualTo(fixture.Tile.Id));
            Assert.That(fixture.Unit.RemainingMovement, Is.Zero);
            Assert.That(resolution.Events.Any(item => item.Type == GameEventType.UnitPromoted &&
                item.SourceId == fixture.Unit.Id && item.PrimaryValue == (int)UnitType.IronInfantry), Is.True);
        }

        [Test]
        public void UnlockedUnitTypesSurviveCopyAndAffectHash()
        {
            var fixture = CreateFixture(10004);
            fixture.Player.UnlockedUnitTypes.Add(UnitType.IronInfantry);
            var copy = GameStateCopy.Clone(fixture.State);

            Assert.That(copy.Players[0].UnlockedUnitTypes, Does.Contain(UnitType.IronInfantry));
            Assert.That(GameStateHasher.Compute(copy), Is.EqualTo(GameStateHasher.Compute(fixture.State)));
            copy.Players[0].UnlockedUnitTypes.Add(UnitType.GunpowderInfantry);
            Assert.That(GameStateHasher.Compute(copy), Is.Not.EqualTo(GameStateHasher.Compute(fixture.State)));
        }

        private static Fixture CreateFixture(long seed)
        {
            var state = PrototypeMatchFactory.Create(seed);
            var city = state.Cities[0];
            var player = state.Players.First(item => item.Id == city.OwnerId);
            var unit = state.Units.First(item => item.OwnerId == city.OwnerId);
            var tile = state.Tiles.First(item => item.Id == unit.TileId);
            unit.RemainingMovement = UnitRules.Movement(unit.Type);
            return new Fixture
            {
                State = state, Player = player, City = city, Unit = unit, Tile = tile
            };
        }

        private static GameCommand Command(Fixture fixture, UnitType target)
        {
            return new GameCommand
            {
                CommandId = fixture.State.AllocateId(), PlayerId = fixture.City.OwnerId,
                TurnNumber = fixture.State.TurnNumber, Type = GameCommandType.PromoteUnit,
                SubjectId = fixture.Unit.Id, PrimaryValue = (int)target
            };
        }

        private sealed class Fixture
        {
            public GameState State;
            public PlayerState Player;
            public CityState City;
            public UnitState Unit;
            public TileState Tile;
        }
    }
}
