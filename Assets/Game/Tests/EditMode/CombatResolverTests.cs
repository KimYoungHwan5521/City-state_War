using System.Collections.Generic;
using LittleCiv.Core;
using NUnit.Framework;

namespace LittleCiv.Tests
{
    public sealed class CombatResolverTests
    {
        [Test]
        public void IronInfantryKillsMilitiaAndTakesSimultaneousRetaliation()
        {
            var fixture = CreateCombat(UnitType.IronInfantry, UnitType.Militia);

            var result = CombatResolver.Resolve(fixture.State, fixture.Request);

            Assert.That(fixture.Attacker.HitPoints, Is.EqualTo(10));
            Assert.That(result.DestroyedUnitIds, Does.Contain(fixture.Defender.Id));
            Assert.That(result.AttackerAdvanced, Is.True);
            Assert.That(fixture.Attacker.TileId, Is.EqualTo(fixture.TargetTile));
        }

        [Test]
        public void WallReducesIronAttackAgainstPreparedMilitiaToFifteen()
        {
            var fixture = CreateCombat(UnitType.IronInfantry, UnitType.Militia);
            fixture.Defender.HasAutomaticDefense = true;
            fixture.State.Tiles[0].DefenseBonusPercent = 20;

            var result = CombatResolver.Resolve(fixture.State, fixture.Request);

            Assert.That(fixture.Defender.HitPoints, Is.EqualTo(1));
            Assert.That(result.DamageRecords[0].Damage, Is.EqualTo(15));
            Assert.That(result.AttackerAdvanced, Is.False);
        }

        [Test]
        public void BothAttackersUseAttackDamageOnBothSides()
        {
            var fixture = CreateCombat(UnitType.Militia, UnitType.Militia);
            fixture.Request.BothSidesAreAttackers = true;

            CombatResolver.Resolve(fixture.State, fixture.Request);

            Assert.That(fixture.Attacker.HitPoints, Is.EqualTo(10));
            Assert.That(fixture.Defender.HitPoints, Is.EqualTo(10));
        }

        [Test]
        public void EquipmentFormulasMatchRecordedExamples()
        {
            Assert.That(Damage(UnitType.IronInfantry, UnitType.Militia, 2), Is.EqualTo(18));
            Assert.That(Damage(UnitType.Militia, UnitType.IronInfantry, 3), Is.EqualTo(6));
            Assert.That(Damage(UnitType.GunpowderInfantry, UnitType.IronInfantry, 2), Is.EqualTo(25));
            Assert.That(Damage(UnitType.GunpowderInfantry, UnitType.MotorizedSupply, 2), Is.EqualTo(9));
            Assert.That(Damage(UnitType.MotorizedSupply, UnitType.GunpowderInfantry, 3), Is.EqualTo(21));
        }

        [Test]
        public void StarvationHalvesFinalDamage()
        {
            var source = Unit(UnitType.IronInfantry, 1, 1);
            var target = Unit(UnitType.Militia, 2, 2);
            source.IsStarving = true;

            Assert.That(CombatResolver.CalculateDamage(source, target, 2, 0), Is.EqualTo(9));
        }

        [Test]
        public void FrontLineOrdersCombatBeforeSupplyThenAttackAndHealth()
        {
            var supply = Unit(UnitType.Supply, 1, 1);
            var militia = Unit(UnitType.Militia, 2, 2);
            var woundedIron = Unit(UnitType.IronInfantry, 3, 3);
            woundedIron.HitPoints = 5;
            var healthyIron = Unit(UnitType.IronInfantry, 4, 4);
            var units = new List<UnitState> { supply, militia, woundedIron, healthyIron };

            units.Sort(CombatResolver.CompareFrontLine);

            Assert.That(units[0], Is.SameAs(healthyIron));
            Assert.That(units[1], Is.SameAs(woundedIron));
            Assert.That(units[2], Is.SameAs(militia));
            Assert.That(units[3], Is.SameAs(supply));
        }

        [Test]
        public void ExcessDamageCarriesToNextFrontLineUnit()
        {
            var fixture = CreateCombat(UnitType.IronInfantry, UnitType.Militia);
            fixture.Defender.HitPoints = 10;
            var second = Unit(UnitType.Militia, fixture.EnemyPlayer.Value, 800);
            second.OwnerId = fixture.EnemyPlayer;
            second.TileId = fixture.TargetTile;
            second.HitPoints = 10;
            fixture.State.Units.Add(second);

            CombatResolver.Resolve(fixture.State, fixture.Request);

            Assert.That(second.HitPoints, Is.EqualTo(2));
        }

        [Test]
        public void EmptyTargetTileIsOccupiedImmediately()
        {
            var fixture = CreateCombat(UnitType.Militia, UnitType.Militia);
            fixture.State.Units.Remove(fixture.Defender);

            var result = CombatResolver.Resolve(fixture.State, fixture.Request);

            Assert.That(result.AttackerAdvanced, Is.True);
            Assert.That(fixture.Attacker.TileId, Is.EqualTo(fixture.TargetTile));
        }

        private static int Damage(UnitType sourceType, UnitType targetType, int roleMultiplier)
        {
            return CombatResolver.CalculateDamage(
                Unit(sourceType, 1, 1),
                Unit(targetType, 2, 2),
                roleMultiplier,
                0);
        }

        private static Fixture CreateCombat(UnitType attackerType, UnitType defenderType)
        {
            var state = GameState.CreateNew(444);
            var attackerPlayer = state.AllocateId();
            var enemyPlayer = state.AllocateId();
            var sourceTile = state.AllocateId();
            var targetTile = state.AllocateId();
            state.Players.Add(new PlayerState { Id = attackerPlayer, Slot = PlayerSlot.PlayerOne });
            state.Players.Add(new PlayerState { Id = enemyPlayer, Slot = PlayerSlot.PlayerTwo });
            state.Tiles.Add(new TileState { Id = targetTile, ControllerId = enemyPlayer });
            var attacker = Unit(attackerType, attackerPlayer.Value, 100);
            attacker.OwnerId = attackerPlayer;
            attacker.TileId = sourceTile;
            var defender = Unit(defenderType, enemyPlayer.Value, 200);
            defender.OwnerId = enemyPlayer;
            defender.TileId = targetTile;
            state.Units.Add(attacker);
            state.Units.Add(defender);
            return new Fixture
            {
                State = state,
                EnemyPlayer = enemyPlayer,
                Attacker = attacker,
                Defender = defender,
                TargetTile = targetTile,
                Request = new CombatEngagementRequest
                {
                    AttackingPlayerId = attackerPlayer,
                    AttackingUnitId = attacker.Id,
                    TargetTileId = targetTile
                }
            };
        }

        private static UnitState Unit(UnitType type, long ownerId, long unitId)
        {
            return new UnitState
            {
                Id = new EntityId(unitId),
                OwnerId = new EntityId(ownerId),
                Type = type,
                HitPoints = UnitRules.MaximumHitPoints(type)
            };
        }

        private sealed class Fixture
        {
            public GameState State;
            public EntityId EnemyPlayer;
            public UnitState Attacker;
            public UnitState Defender;
            public EntityId TargetTile;
            public CombatEngagementRequest Request;
        }
    }
}
