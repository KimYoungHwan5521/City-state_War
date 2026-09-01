using System.Linq;
using LittleCiv.Core;
using NUnit.Framework;

namespace LittleCiv.Tests
{
    public sealed class DefenseFacilityResolverTests
    {
        [Test]
        public void FacilitiesRequireSequentialStagesAndChargeRecordedCosts()
        {
            var fixture = CreateFixture(100);

            Assert.That(Start(fixture, DefenseFacilityType.Moat, out _), Is.False);
            Assert.That(Start(fixture, DefenseFacilityType.Wall, out var wall), Is.True);
            Assert.That(fixture.City.Gold, Is.EqualTo(95));
            Assert.That(wall.RemainingConstructionTurns, Is.EqualTo(2));
            Advance(fixture.State, 2);
            Assert.That(wall.Type, Is.EqualTo(DefenseFacilityType.Wall));
            Assert.That(fixture.Tile.DefenseBonusPercent, Is.EqualTo(20));

            Assert.That(Start(fixture, DefenseFacilityType.Moat, out var moat), Is.True);
            Assert.That(fixture.City.Gold, Is.EqualTo(85));
            Advance(fixture.State, 4);
            Assert.That(moat.Type, Is.EqualTo(DefenseFacilityType.Moat));
            Assert.That(fixture.Tile.DefenseBonusPercent, Is.EqualTo(50));

            Assert.That(Start(fixture, DefenseFacilityType.ModernDefense, out var modern), Is.True);
            Assert.That(fixture.City.Gold, Is.EqualTo(65));
            Advance(fixture.State, 8);
            Assert.That(modern.Type, Is.EqualTo(DefenseFacilityType.ModernDefense));
            Assert.That(modern.IsModernDefenseActive, Is.True);
            Assert.That(fixture.Tile.DefenseBonusPercent, Is.EqualTo(100));
        }

        [Test]
        public void ModernDefenseFallsBackToMoatAndNeedsTwoPaidTurnsToReactivate()
        {
            var fixture = CreateFixture(10);
            var facility = AddModern(fixture);
            var command = Command(fixture, GameCommandType.SetModernDefenseActive);

            command.PrimaryValue = 0;
            Assert.That(DefenseFacilityResolver.TrySetModernActive(fixture.State, command), Is.True);
            Assert.That(fixture.Tile.DefenseBonusPercent, Is.EqualTo(50));
            command.PrimaryValue = 1;
            Assert.That(DefenseFacilityResolver.TrySetModernActive(fixture.State, command), Is.True);
            Assert.That(facility.RemainingReactivationTurns, Is.EqualTo(2));

            var deactivated = new System.Collections.Generic.List<EntityId>();
            var reactivated = new System.Collections.Generic.List<EntityId>();
            DefenseFacilityResolver.ResolveModernMaintenance(fixture.State, fixture.City, deactivated, reactivated);
            Assert.That(facility.IsModernDefenseActive, Is.False);
            Assert.That(fixture.City.Gold, Is.EqualTo(8));
            DefenseFacilityResolver.ResolveModernMaintenance(fixture.State, fixture.City, deactivated, reactivated);
            Assert.That(facility.IsModernDefenseActive, Is.True);
            Assert.That(fixture.Tile.DefenseBonusPercent, Is.EqualTo(100));
            Assert.That(fixture.City.Gold, Is.EqualTo(6));
        }

        [Test]
        public void UnpaidModernDefenseDeactivatesAfterOtherMaintenance()
        {
            var fixture = CreateFixture(1);
            var facility = AddModern(fixture);
            var result = MaintenanceResolver.Resolve(fixture.State);

            Assert.That(facility.IsModernDefenseActive, Is.False);
            Assert.That(fixture.Tile.DefenseBonusPercent, Is.EqualTo(50));
            Assert.That(result.DeactivatedModernDefenses, Does.Contain(facility.Id));
        }

        [Test]
        public void FacilityBonusOnlyProtectsAutomaticDefender()
        {
            var fixture = CreateFixture(100);
            AddModern(fixture);
            var attacker = fixture.State.Units.First(item => item.OwnerId != fixture.City.OwnerId);
            var defender = fixture.State.Units.First(item => item.HomeCityId == fixture.City.Id);
            attacker.Type = UnitType.IronInfantry;
            attacker.HitPoints = 16;
            attacker.TileId = fixture.State.Tiles.First(item => item.CityId != fixture.City.Id).Id;
            defender.HasAutomaticDefense = true;
            var request = new CombatEngagementRequest
            {
                AttackingPlayerId = attacker.OwnerId,
                AttackingUnitId = attacker.Id,
                TargetTileId = fixture.Tile.Id
            };

            var result = CombatResolver.Resolve(fixture.State, request);

            Assert.That(result.DamageRecords[0].Damage, Is.EqualTo(9));
            Assert.That(defender.HitPoints, Is.EqualTo(7));
        }

        private static Fixture CreateFixture(int gold)
        {
            var state = PrototypeMatchFactory.Create(915);
            var city = state.Cities[0];
            city.Gold = gold;
            var district = state.Districts.First(item => item.CityId == city.Id && item.Type == DistrictType.Government);
            return new Fixture { State = state, City = city, Tile = state.Tiles.First(item => item.Id == district.TileId) };
        }

        private static bool Start(Fixture fixture, DefenseFacilityType type, out DefenseFacilityState facility)
        {
            var command = Command(fixture, GameCommandType.StartDefenseFacility);
            command.TargetId = fixture.Tile.Id;
            command.PrimaryValue = (int)type;
            return DefenseFacilityResolver.TryStart(fixture.State, command, out facility);
        }

        private static GameCommand Command(Fixture fixture, GameCommandType type) => new GameCommand
        {
            CommandId = fixture.State.AllocateId(), PlayerId = fixture.City.OwnerId,
            TurnNumber = fixture.State.TurnNumber, Type = type,
            SubjectId = type == GameCommandType.SetModernDefenseActive
                ? fixture.State.DefenseFacilities[0].Id : fixture.City.Id
        };

        private static void Advance(GameState state, int turns)
        {
            for (var i = 0; i < turns; i++) DefenseFacilityResolver.AdvanceConstruction(state);
        }

        private static DefenseFacilityState AddModern(Fixture fixture)
        {
            var facility = new DefenseFacilityState
            {
                Id = fixture.State.AllocateId(), CityId = fixture.City.Id, TileId = fixture.Tile.Id,
                Type = DefenseFacilityType.ModernDefense, IsModernDefenseActive = true
            };
            fixture.State.DefenseFacilities.Add(facility);
            fixture.Tile.DefenseBonusPercent = 100;
            return facility;
        }

        private sealed class Fixture
        {
            public GameState State;
            public CityState City;
            public TileState Tile;
        }
    }
}
