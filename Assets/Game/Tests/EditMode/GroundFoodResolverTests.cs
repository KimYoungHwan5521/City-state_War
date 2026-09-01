using LittleCiv.Core;
using NUnit.Framework;

namespace LittleCiv.Tests
{
    public sealed class GroundFoodResolverTests
    {
        [Test]
        public void VictoriousAttackerTemporarilyOwnsDefendersDroppedFood()
        {
            var fixture = CreateFixture(false);
            fixture.Attacker.Type = UnitType.IronInfantry;
            fixture.Attacker.HitPoints = UnitRules.MaximumHitPoints(UnitType.IronInfantry);
            fixture.Defender.CarriedFood = 5;

            var result = CombatResolver.Resolve(fixture.State, fixture.Request);

            Assert.That(result.DroppedFood, Is.EqualTo(5));
            Assert.That(fixture.Tile.GroundFood, Is.EqualTo(5));
            Assert.That(fixture.Tile.GroundFoodOwnerId, Is.EqualTo(fixture.Attacker.OwnerId));
            Assert.That(fixture.Tile.GroundFoodReturnTurn, Is.Zero);
        }

        [Test]
        public void SurvivingDefenderOwnsDestroyedAttackersFood()
        {
            var fixture = CreateFixture(false);
            fixture.Attacker.HitPoints = 1;
            fixture.Attacker.CarriedFood = 4;

            var result = CombatResolver.Resolve(fixture.State, fixture.Request);

            Assert.That(result.DestroyedUnitIds, Does.Contain(fixture.Attacker.Id));
            Assert.That(fixture.Tile.GroundFood, Is.EqualTo(4));
            Assert.That(fixture.Tile.GroundFoodOwnerId, Is.EqualTo(fixture.Defender.OwnerId));
        }

        [Test]
        public void EmptyOwnedTileReturnsFoodAfterOneFullGraceTurn()
        {
            var fixture = CreateFixture(false);
            fixture.Attacker.HitPoints = 6;
            fixture.Attacker.CarriedFood = 3;
            fixture.Defender.HitPoints = 6;
            fixture.Defender.CarriedFood = 4;
            fixture.Request.BothSidesAreAttackers = true;

            CombatResolver.Resolve(fixture.State, fixture.Request);

            Assert.That(fixture.State.Units, Is.Empty);
            Assert.That(fixture.Tile.GroundFood, Is.EqualTo(7));
            Assert.That(fixture.Tile.GroundFoodOwnerId, Is.EqualTo(fixture.Defender.OwnerId));
            Assert.That(fixture.Tile.GroundFoodReturnTurn, Is.EqualTo(3));
            fixture.State.TurnNumber = 2;
            Assert.That(GroundFoodResolver.ReturnEligibleFood(fixture.State), Is.Empty);
            fixture.State.TurnNumber = 3;
            var returned = GroundFoodResolver.ReturnEligibleFood(fixture.State);
            Assert.That(returned.Count, Is.EqualTo(1));
            Assert.That(returned[0].Amount, Is.EqualTo(7));
            Assert.That(fixture.City.StoredFood, Is.EqualTo(7));
            Assert.That(fixture.Tile.GroundFood, Is.Zero);
        }

        [Test]
        public void SharedBoundaryFoodHasNoOwnerAndNeverReturnsAutomatically()
        {
            var fixture = CreateFixture(true);
            fixture.Attacker.HitPoints = 6;
            fixture.Attacker.CarriedFood = 3;
            fixture.Defender.HitPoints = 6;
            fixture.Defender.CarriedFood = 4;
            fixture.Request.BothSidesAreAttackers = true;

            CombatResolver.Resolve(fixture.State, fixture.Request);
            fixture.State.TurnNumber = 20;

            Assert.That(fixture.Tile.GroundFoodOwnerId.IsValid, Is.False);
            Assert.That(fixture.Tile.GroundFoodReturnTurn, Is.Zero);
            Assert.That(GroundFoodResolver.ReturnEligibleFood(fixture.State), Is.Empty);
            Assert.That(fixture.Tile.GroundFood, Is.EqualTo(7));
        }

        [Test]
        public void GroundFoodOwnershipAndReturnTurnSurviveCopyAndHash()
        {
            var fixture = CreateFixture(false);
            fixture.Tile.GroundFood = 8;
            fixture.Tile.GroundFoodOwnerId = fixture.City.OwnerId;
            fixture.Tile.GroundFoodReturnTurn = 4;
            var copy = GameStateCopy.Clone(fixture.State);

            Assert.That(copy.Tiles[0].GroundFoodOwnerId, Is.EqualTo(fixture.City.OwnerId));
            Assert.That(copy.Tiles[0].GroundFoodReturnTurn, Is.EqualTo(4));
            Assert.That(GameStateHasher.Compute(copy), Is.EqualTo(GameStateHasher.Compute(fixture.State)));
            copy.Tiles[0].GroundFoodReturnTurn++;
            Assert.That(GameStateHasher.Compute(copy), Is.Not.EqualTo(GameStateHasher.Compute(fixture.State)));
        }

        private static Fixture CreateFixture(bool shared)
        {
            var state = GameState.CreateNew(7000);
            var attackerOwner = state.AllocateId();
            var defenderOwner = state.AllocateId();
            var cityId = state.AllocateId();
            var sourceTile = state.AllocateId();
            var targetTile = state.AllocateId();
            state.Players.Add(new PlayerState { Id = attackerOwner, Slot = PlayerSlot.PlayerOne });
            state.Players.Add(new PlayerState { Id = defenderOwner, Slot = PlayerSlot.PlayerTwo });
            var city = new CityState { Id = cityId, OwnerId = defenderOwner };
            state.Cities.Add(city);
            var tile = new TileState
            {
                Id = targetTile, CityId = cityId, ControllerId = defenderOwner,
                IsSharedBoundary = shared
            };
            state.Tiles.Add(tile);
            var attacker = Unit(state, attackerOwner, sourceTile);
            var defender = Unit(state, defenderOwner, targetTile);
            state.Units.Add(attacker);
            state.Units.Add(defender);
            return new Fixture
            {
                State = state, City = city, Tile = tile, Attacker = attacker, Defender = defender,
                Request = new CombatEngagementRequest
                {
                    AttackingPlayerId = attackerOwner,
                    AttackingUnitId = attacker.Id,
                    TargetTileId = targetTile
                }
            };
        }

        private static UnitState Unit(GameState state, EntityId owner, EntityId tile)
        {
            return new UnitState
            {
                Id = state.AllocateId(), OwnerId = owner, TileId = tile,
                Type = UnitType.Militia, HitPoints = UnitRules.MaximumHitPoints(UnitType.Militia)
            };
        }

        private sealed class Fixture
        {
            public GameState State;
            public CityState City;
            public TileState Tile;
            public UnitState Attacker;
            public UnitState Defender;
            public CombatEngagementRequest Request;
        }
    }
}
