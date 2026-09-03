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
        public void EmptyGovernmentDefersConquestUntilFinalVictoryPhase()
        {
            var fixture = CreateFixture(DistrictType.Government, includeDefender: false);

            var result = OccupationResolver.Resolve(
                fixture.State,
                fixture.AttackerPlayer,
                fixture.TargetTile);

            Assert.That(result.ConquestVictoryTriggered, Is.False);
            Assert.That(fixture.State.Victory, Is.EqualTo(VictoryType.None));
            Assert.That(VictoryResolver.ResolveConquest(fixture.State), Is.EqualTo(fixture.AttackerPlayer));
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
            Assert.That(result.Occupation.ConquestVictoryTriggered, Is.False);
            Assert.That(VictoryResolver.ResolveConquest(fixture.State), Is.EqualTo(fixture.AttackerPlayer));
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
            VictoryResolver.ResolveConquest(fixture.State);

            Assert.Throws<System.InvalidOperationException>(() =>
                new TurnProcessor().Resolve(fixture.State, new List<GameCommand>()));
        }

        [TestCase(DistrictType.Commerce, 6)]
        [TestCase(DistrictType.Science, 4)]
        [TestCase(DistrictType.NuclearFacility, 10)]
        public void FirstOccupationGrantsRecordedPrimaryReward(DistrictType type, int amount)
        {
            var fixture = CreateFixture(type, includeDefender: false);
            var attackerCity = fixture.State.Cities.Single(item => item.OwnerId == fixture.AttackerPlayer);
            var before = type == DistrictType.Science ? attackerCity.ResearchPoints : attackerCity.Gold;

            var result = OccupationResolver.Resolve(fixture.State, fixture.AttackerPlayer, fixture.TargetTile);

            var after = type == DistrictType.Science ? attackerCity.ResearchPoints : attackerCity.Gold;
            Assert.That(result.PillageRewardGranted, Is.True);
            Assert.That(result.PillagePrimaryReward, Is.EqualTo(amount));
            Assert.That(after - before, Is.EqualTo(amount));
        }

        [Test]
        public void AgricultureStealsAtMostSixStoredFood()
        {
            var fixture = CreateFixture(DistrictType.Agriculture, includeDefender: false);
            var victimCity = fixture.State.Cities.Single(item => item.OwnerId == fixture.DefenderPlayer);
            var attackerCity = fixture.State.Cities.Single(item => item.OwnerId == fixture.AttackerPlayer);
            victimCity.StoredFood = 4;

            var result = OccupationResolver.Resolve(fixture.State, fixture.AttackerPlayer, fixture.TargetTile);

            Assert.That(result.PillageFoodReward, Is.EqualTo(4));
            Assert.That(victimCity.StoredFood, Is.Zero);
            Assert.That(attackerCity.StoredFood, Is.EqualTo(4));
        }

        [Test]
        public void MilitaryGrantsGoldAndDropsStolenFoodOnOccupiedTile()
        {
            var fixture = CreateFixture(DistrictType.Military, includeDefender: false);
            var victimCity = fixture.State.Cities.Single(item => item.OwnerId == fixture.DefenderPlayer);
            var attackerCity = fixture.State.Cities.Single(item => item.OwnerId == fixture.AttackerPlayer);
            var startingGold = attackerCity.Gold;
            victimCity.StoredFood = 8;
            fixture.Attacker.TileId = fixture.TargetTile;

            var result = OccupationResolver.Resolve(fixture.State, fixture.AttackerPlayer, fixture.TargetTile);
            var tile = fixture.State.Tiles.Single(item => item.Id == fixture.TargetTile);

            Assert.That(attackerCity.Gold - startingGold, Is.EqualTo(3));
            Assert.That(victimCity.StoredFood, Is.EqualTo(5));
            Assert.That(result.PillageFoodReward, Is.EqualTo(3));
            Assert.That(tile.GroundFood, Is.EqualTo(3));
            Assert.That(tile.GroundFoodOwnerId, Is.EqualTo(fixture.AttackerPlayer));
        }

        [Test]
        public void CulturePillageAddsFourConversionProgressInVictimCity()
        {
            var fixture = CreateFixture(DistrictType.Culture, includeDefender: false);
            var victimCity = fixture.State.Cities.Single(item => item.OwnerId == fixture.DefenderPlayer);

            OccupationResolver.Resolve(fixture.State, fixture.AttackerPlayer, fixture.TargetTile);

            Assert.That(victimCity.CultureInfluences.Single(item =>
                item.CultureOwnerId == fixture.AttackerPlayer).ConversionProgress, Is.EqualTo(4));
        }

        [Test]
        public void CulturePillageUsesExistingProgressAndCanConvertCitizen()
        {
            var fixture = CreateFixture(DistrictType.Culture, includeDefender: false);
            var victimCity = fixture.State.Cities.Single(item => item.OwnerId == fixture.DefenderPlayer);
            var influence = CityCultureRules.GetOrCreate(victimCity, fixture.AttackerPlayer);
            influence.ConversionProgress = 8;

            OccupationResolver.Resolve(fixture.State, fixture.AttackerPlayer, fixture.TargetTile);

            Assert.That(influence.PreferredCitizens, Is.EqualTo(1));
            Assert.That(influence.ConversionProgress, Is.EqualTo(2));
            Assert.That(CityCultureRules.NativeCitizens(victimCity), Is.EqualTo(3));
        }

        [Test]
        public void RewardCannotRepeatUntilRepairCompletes()
        {
            var fixture = CreateFixture(DistrictType.Commerce, includeDefender: false);
            var attackerCity = fixture.State.Cities.Single(item => item.OwnerId == fixture.AttackerPlayer);
            var startingGold = attackerCity.Gold;
            OccupationResolver.Resolve(fixture.State, fixture.AttackerPlayer, fixture.TargetTile);
            OccupationResolver.Resolve(fixture.State, fixture.DefenderPlayer, fixture.TargetTile);

            var repeated = OccupationResolver.Resolve(fixture.State, fixture.AttackerPlayer, fixture.TargetTile);
            Assert.That(repeated.PillageRewardGranted, Is.False);
            Assert.That(attackerCity.Gold - startingGold, Is.EqualTo(6));

            OccupationResolver.Resolve(fixture.State, fixture.DefenderPlayer, fixture.TargetTile);
            var repair = new GameCommand
            {
                CommandId = fixture.State.AllocateId(), PlayerId = fixture.DefenderPlayer,
                TurnNumber = fixture.State.TurnNumber, Type = GameCommandType.RepairDistrict,
                SubjectId = fixture.District.Id
            };
            Assert.That(DistrictConstructionResolver.TryStartRepair(fixture.State, repair, out _), Is.True);
            DistrictConstructionResolver.AdvanceRepairs(fixture.State);
            DistrictConstructionResolver.AdvanceRepairs(fixture.State);

            var afterRepair = OccupationResolver.Resolve(fixture.State, fixture.AttackerPlayer, fixture.TargetTile);
            Assert.That(afterRepair.PillageRewardGranted, Is.True);
            Assert.That(attackerCity.Gold - startingGold, Is.EqualTo(12));
        }

        private static Fixture CreateFixture(DistrictType type, bool includeDefender)
        {
            var state = GameState.CreateNew(555);
            var attackerPlayer = state.AllocateId();
            var defenderPlayer = state.AllocateId();
            state.Players.Add(new PlayerState { Id = attackerPlayer, Slot = PlayerSlot.PlayerOne });
            state.Players.Add(new PlayerState { Id = defenderPlayer, Slot = PlayerSlot.PlayerTwo });
            var defenderCity = state.AllocateId();
            var attackerCity = state.AllocateId();
            state.Cities.Add(new CityState { Id = attackerCity, OwnerId = attackerPlayer });
            state.Cities.Add(new CityState { Id = defenderCity, OwnerId = defenderPlayer });
            var sourceTile = state.AllocateId();
            var targetTile = state.AllocateId();
            state.Tiles.Add(new TileState
            {
                Id = sourceTile, CityId = attackerCity, ControllerId = attackerPlayer
            });
            state.Tiles.Add(new TileState
            {
                Id = targetTile, CityId = defenderCity, Q = 1, ControllerId = defenderPlayer
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
                HomeCityId = attackerCity, Type = UnitType.Militia, HitPoints = 16, RemainingMovement = 2
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
