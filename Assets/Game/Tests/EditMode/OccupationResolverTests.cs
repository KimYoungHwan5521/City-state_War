using System.Collections.Generic;
using System.Linq;
using LittleCiv.Core;
using NUnit.Framework;

namespace LittleCiv.Tests
{
    public sealed class OccupationResolverTests
    {
        [Test]
        public void EmptyDistrictChangesControllerAndStopsOperation()
        {
            var fixture = CreateFixture(DistrictType.Science, includeDefender: false);

            var result = OccupationResolver.Resolve(
                fixture.State,
                fixture.AttackerPlayer,
                fixture.TargetTile);

            Assert.That(result.DistrictOccupied, Is.True);
            Assert.That(fixture.District.ControllerId, Is.EqualTo(fixture.AttackerPlayer));
            Assert.That(fixture.District.IsOperational, Is.False);
            Assert.That(fixture.District.IsPillaged, Is.True);
            Assert.That(fixture.State.Tiles[1].ControllerId, Is.EqualTo(fixture.AttackerPlayer));
            Assert.That(fixture.State.IsGameOver, Is.False);
        }

        [Test]
        public void LastOccupyingUnitLeavingImmediatelyReleasesDistrictControl()
        {
            var fixture = CreateFixture(DistrictType.Commerce, includeDefender: false);
            fixture.Attacker.TileId = fixture.TargetTile;
            OccupationResolver.Resolve(fixture.State, fixture.AttackerPlayer, fixture.TargetTile);
            fixture.Attacker.TileId = fixture.State.Tiles.First(item => item.Id != fixture.TargetTile).Id;

            var released = OccupationResolver.ReleaseVacatedDistricts(fixture.State);

            Assert.That(released, Does.Contain(fixture.District.Id));
            Assert.That(fixture.District.ControllerId, Is.EqualTo(fixture.DefenderPlayer));
            Assert.That(fixture.State.Tiles.Single(item => item.Id == fixture.TargetTile).ControllerId,
                Is.EqualTo(fixture.DefenderPlayer));
            Assert.That(fixture.District.IsPillaged, Is.True);
        }

        [Test]
        public void RecapturedPillagedDistrictCanBeRepairedToOperation()
        {
            var fixture = CreateFixture(DistrictType.Science, includeDefender: false);
            OccupationResolver.Resolve(fixture.State, fixture.AttackerPlayer, fixture.TargetTile);
            OccupationResolver.Resolve(fixture.State, fixture.DefenderPlayer, fixture.TargetTile);
            MaintenanceResolver.Resolve(fixture.State);
            Assert.That(fixture.District.IsOperational, Is.False);
            var command = new GameCommand
            {
                CommandId = fixture.State.AllocateId(), PlayerId = fixture.DefenderPlayer,
                TurnNumber = fixture.State.TurnNumber, Type = GameCommandType.RepairDistrict,
                SubjectId = fixture.District.Id
            };

            Assert.That(DistrictConstructionResolver.TryStartRepair(
                fixture.State, command, out var repairing), Is.True);
            Assert.That(repairing.RemainingRepairTurns, Is.EqualTo(3));
            DistrictConstructionResolver.AdvanceRepairs(fixture.State);
            DistrictConstructionResolver.AdvanceRepairs(fixture.State);
            Assert.That(fixture.District.IsPillaged, Is.True);
            DistrictConstructionResolver.AdvanceRepairs(fixture.State);
            Assert.That(fixture.District.IsPillaged, Is.False);
            Assert.That(fixture.District.IsOperational, Is.True);
        }

        [Test]
        public void LegacyRecapturedInactiveDistrictIsAcceptedAsPillagedForRepair()
        {
            var fixture = CreateFixture(DistrictType.Military, includeDefender: false);
            fixture.District.IsOperational = false;
            fixture.District.IsPillaged = false;
            var command = new GameCommand
            {
                CommandId = fixture.State.AllocateId(), PlayerId = fixture.DefenderPlayer,
                TurnNumber = fixture.State.TurnNumber, Type = GameCommandType.RepairDistrict,
                SubjectId = fixture.District.Id
            };

            Assert.That(DistrictConstructionResolver.TryStartRepair(
                fixture.State, command, out var repairing), Is.True);
            Assert.That(repairing.IsPillaged, Is.True);
            Assert.That(repairing.RemainingRepairTurns, Is.EqualTo(3));
        }

        [Test]
        public void DefendedDistrictCannotBeOccupied()
        {
            var fixture = CreateFixture(DistrictType.Science, includeDefender: true);

            var result = OccupationResolver.Resolve(
                fixture.State,
                fixture.AttackerPlayer,
                fixture.TargetTile);

            Assert.That(result.DistrictOccupied, Is.False);
            Assert.That(fixture.District.ControllerId, Is.EqualTo(fixture.DefenderPlayer));
        }

        [Test]
        public void EmptyGovernmentImmediatelyTriggersConquestVictory()
        {
            var fixture = CreateFixture(DistrictType.Government, includeDefender: false);

            var result = OccupationResolver.Resolve(
                fixture.State,
                fixture.AttackerPlayer,
                fixture.TargetTile);

            Assert.That(result.ConquestVictoryTriggered, Is.True);
            Assert.That(fixture.State.Victory, Is.EqualTo(VictoryType.Conquest));
            Assert.That(fixture.State.WinnerId, Is.EqualTo(fixture.AttackerPlayer));
        }

        [Test]
        public void CombatVictoryOccupiesGovernmentAfterLastDefenderDies()
        {
            var fixture = CreateFixture(DistrictType.Government, includeDefender: true);
            fixture.Attacker.Type = UnitType.IronInfantry;
            fixture.Attacker.HitPoints = 16;
            var defender = fixture.State.Units.Single(item => item.OwnerId == fixture.DefenderPlayer);
            defender.Type = UnitType.Militia;
            defender.HitPoints = 16;

            var result = CombatResolver.Resolve(fixture.State, new CombatEngagementRequest
            {
                AttackingPlayerId = fixture.AttackerPlayer,
                AttackingUnitId = fixture.Attacker.Id,
                TargetTileId = fixture.TargetTile
            });

            Assert.That(result.AttackerAdvanced, Is.True);
            Assert.That(result.Occupation.ConquestVictoryTriggered, Is.True);
            Assert.That(fixture.State.WinnerId, Is.EqualTo(fixture.AttackerPlayer));
        }

        [Test]
        public void TurnProcessorEmitsOccupationThenConquestAtFinalPhase()
        {
            var fixture = CreateFixture(DistrictType.Government, includeDefender: false);
            var command = new GameCommand
            {
                CommandId = new EntityId(9001),
                PlayerId = fixture.AttackerPlayer,
                TurnNumber = fixture.State.TurnNumber,
                Type = GameCommandType.MoveUnit,
                SubjectId = fixture.Attacker.Id,
                TargetId = fixture.TargetTile,
                Path = new List<EntityId> { fixture.TargetTile }
            };

            var result = new TurnProcessor().Resolve(fixture.State, new[] { command });

            var occupiedIndex = result.Events.FindIndex(item => item.Type == GameEventType.DistrictOccupied);
            var victoryIndex = result.Events.FindIndex(item => item.Type == GameEventType.VictoryTriggered);
            var conquestPhaseIndex = result.Events.FindIndex(item =>
                item.Type == GameEventType.PhaseStarted &&
                item.PrimaryValue == (int)TurnPhase.ConquestVictory);
            Assert.That(occupiedIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(victoryIndex, Is.GreaterThan(conquestPhaseIndex));
            Assert.That(victoryIndex, Is.GreaterThan(occupiedIndex));
            Assert.That(fixture.State.IsGameOver, Is.True);
        }

        [Test]
        public void CompletedMatchRejectsFurtherTurns()
        {
            var fixture = CreateFixture(DistrictType.Government, includeDefender: false);
            OccupationResolver.Resolve(fixture.State, fixture.AttackerPlayer, fixture.TargetTile);

            Assert.Throws<System.InvalidOperationException>(() =>
                new TurnProcessor().Resolve(fixture.State, new List<GameCommand>()));
        }

        private static Fixture CreateFixture(DistrictType type, bool includeDefender)
        {
            var state = GameState.CreateNew(555);
            var attackerPlayer = state.AllocateId();
            var defenderPlayer = state.AllocateId();
            state.Players.Add(new PlayerState { Id = attackerPlayer, Slot = PlayerSlot.PlayerOne });
            state.Players.Add(new PlayerState { Id = defenderPlayer, Slot = PlayerSlot.PlayerTwo });
            var defenderCity = state.AllocateId();
            state.Cities.Add(new CityState { Id = defenderCity, OwnerId = defenderPlayer });
            var sourceTile = state.AllocateId();
            var targetTile = state.AllocateId();
            state.Tiles.Add(new TileState { Id = sourceTile, ControllerId = attackerPlayer });
            state.Tiles.Add(new TileState
            {
                Id = targetTile, CityId = defenderCity, ControllerId = defenderPlayer
            });
            state.MapTopology.CityViews.Add(new CityMapView
            {
                CityId = defenderCity,
                Tiles = new List<CityTilePlacement>
                {
                    new CityTilePlacement { TileId = sourceTile, LocalQ = 0, LocalR = 0 },
                    new CityTilePlacement { TileId = targetTile, LocalQ = 1, LocalR = 0 }
                }
            });
            var district = new DistrictState
            {
                Id = state.AllocateId(),
                CityId = defenderCity,
                TileId = targetTile,
                Type = type,
                ControllerId = defenderPlayer,
                IsOperational = true,
                AssignedCitizens = 1
            };
            state.Districts.Add(district);
            var attacker = new UnitState
            {
                Id = state.AllocateId(), OwnerId = attackerPlayer, TileId = sourceTile,
                Type = UnitType.Militia, HitPoints = 16, RemainingMovement = 2
            };
            state.Units.Add(attacker);
            if (includeDefender)
            {
                state.Units.Add(new UnitState
                {
                    Id = state.AllocateId(), OwnerId = defenderPlayer, TileId = targetTile,
                    Type = UnitType.Militia, HitPoints = 16
                });
            }
            return new Fixture
            {
                State = state,
                AttackerPlayer = attackerPlayer,
                DefenderPlayer = defenderPlayer,
                TargetTile = targetTile,
                District = district,
                Attacker = attacker
            };
        }

        private sealed class Fixture
        {
            public GameState State;
            public EntityId AttackerPlayer;
            public EntityId DefenderPlayer;
            public EntityId TargetTile;
            public DistrictState District;
            public UnitState Attacker;
        }
    }
}
