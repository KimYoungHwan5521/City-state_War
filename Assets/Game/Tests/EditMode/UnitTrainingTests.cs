using System.Linq;
using LittleCiv.Core;
using NUnit.Framework;

namespace LittleCiv.Tests
{
    public sealed class UnitTrainingTests
    {
        [Test]
        public void StartTrainingPaysGoldAndOneTurnUnitCompletesNextTurnWithoutMovement()
        {
            var fixture = CreateFixture(5000);
            var command = TrainingCommand(fixture.State, fixture.City, fixture.District, UnitType.Militia);

            var started = new TurnProcessor().Resolve(fixture.State, new[] { command });

            Assert.That(fixture.City.Gold, Is.EqualTo(18));
            Assert.That(fixture.State.UnitTrainings.Single().RemainingTurns, Is.EqualTo(1));
            Assert.That(started.Events.Any(item => item.Type == GameEventType.UnitTrainingStarted), Is.True);

            var completed = new TurnProcessor().Resolve(fixture.State, new GameCommand[0]);
            var unit = fixture.State.Units.Single(item => item.CreatedTurn == 2 && item.Type == UnitType.Militia);
            Assert.That(unit.TileId, Is.EqualTo(fixture.District.TileId));
            Assert.That(unit.RemainingMovement, Is.Zero);
            Assert.That(unit.CarriedFood, Is.Zero);
            Assert.That(completed.Events.Any(item => item.Type == GameEventType.UnitTrainingCompleted), Is.True);
        }

        [Test]
        public void OccupiedOrUnstaffedMilitaryDistrictPausesTrainingProgress()
        {
            var fixture = CreateFixture(5001);
            UnitTrainingState training;
            Assert.That(UnitTrainingResolver.TryStart(
                fixture.State,
                TrainingCommand(fixture.State, fixture.City, fixture.District, UnitType.IronInfantry),
                out training), Is.True);
            fixture.District.ControllerId = fixture.State.Cities[1].OwnerId;

            UnitTrainingResolver.Advance(fixture.State);

            Assert.That(training.RemainingTurns, Is.EqualTo(2));
        }

        [Test]
        public void MilitaryDistrictCannotRunTwoTrainingsAtOnce()
        {
            var fixture = CreateFixture(5002);
            UnitTrainingState first;
            UnitTrainingState second;

            Assert.That(UnitTrainingResolver.TryStart(
                fixture.State,
                TrainingCommand(fixture.State, fixture.City, fixture.District, UnitType.Supply),
                out first), Is.True);
            Assert.That(UnitTrainingResolver.TryStart(
                fixture.State,
                TrainingCommand(fixture.State, fixture.City, fixture.District, UnitType.Militia),
                out second), Is.False);
        }

        [Test]
        public void CompletedUnitWaitsWithoutUpkeepUntilDistrictTileHasSpace()
        {
            var fixture = CreateFixture(5003);
            for (var index = 0; index < UnitRules.CombatUnitsPerTile; index++)
                AddUnit(fixture.State, fixture.City, fixture.District.TileId, UnitType.Militia);
            UnitTrainingState training;
            UnitTrainingResolver.TryStart(
                fixture.State,
                TrainingCommand(fixture.State, fixture.City, fixture.District, UnitType.Militia),
                out training);

            var waiting = UnitTrainingResolver.Advance(fixture.State);

            Assert.That(training.IsAwaitingDeployment, Is.True);
            Assert.That(fixture.State.UnitTrainings, Does.Contain(training));
            Assert.That(waiting.WaitingTrainingIds, Does.Contain(training.Id));
            Assert.That(MaintenanceResolver.Resolve(fixture.State).DisbandedUnits, Is.Empty);
        }

        [Test]
        public void AwaitingDeploymentUnitAppearsImmediatelyAfterSpaceOpens()
        {
            var fixture = CreateFixture(5007);
            for (var index = 0; index < UnitRules.CombatUnitsPerTile; index++)
                AddUnit(fixture.State, fixture.City, fixture.District.TileId, UnitType.Militia);
            Assert.That(UnitTrainingResolver.TryStart(
                fixture.State,
                TrainingCommand(fixture.State, fixture.City, fixture.District, UnitType.Militia),
                out var training), Is.True);
            UnitTrainingResolver.Advance(fixture.State);
            Assert.That(training.IsAwaitingDeployment, Is.True);
            var movedUnit = fixture.State.Units.First(item => item.TileId == fixture.District.TileId);
            movedUnit.TileId = fixture.State.Tiles.First(item =>
                item.CityId == fixture.City.Id && item.Id != fixture.District.TileId).Id;

            var deployed = UnitTrainingResolver.DeployWaiting(fixture.State);

            Assert.That(deployed, Has.Count.EqualTo(1));
            Assert.That(fixture.State.UnitTrainings.Any(item => item.Id == training.Id), Is.False);
            Assert.That(fixture.State.Units.Count(item =>
                item.TileId == fixture.District.TileId && !UnitRules.IsSupply(item.Type)),
                Is.EqualTo(UnitRules.CombatUnitsPerTile));
        }

        [Test]
        public void TrainingStateSurvivesCopyAndChangesDeterministicHash()
        {
            var fixture = CreateFixture(5004);
            UnitTrainingState training;
            UnitTrainingResolver.TryStart(
                fixture.State,
                TrainingCommand(fixture.State, fixture.City, fixture.District, UnitType.GunpowderInfantry),
                out training);
            var copy = GameStateCopy.Clone(fixture.State);

            Assert.That(GameStateHasher.Compute(copy), Is.EqualTo(GameStateHasher.Compute(fixture.State)));
            copy.UnitTrainings[0].RemainingTurns--;
            Assert.That(GameStateHasher.Compute(copy), Is.Not.EqualTo(GameStateHasher.Compute(fixture.State)));
        }

        [Test]
        public void CompletedUnitCannotUsePredictedMoveCommandOrGainAutomaticDefense()
        {
            var fixture = CreateFixture(5005);
            UnitTrainingState training;
            Assert.That(UnitTrainingResolver.TryStart(
                fixture.State,
                TrainingCommand(fixture.State, fixture.City, fixture.District, UnitType.Militia),
                out training), Is.True);
            training.RemainingTurns = 1;
            var predictedUnitId = new EntityId(fixture.State.NextEntityId);
            var destination = fixture.State.Tiles.First(item =>
                item.CityId == fixture.City.Id && item.Id != fixture.District.TileId &&
                MapTraversal.AreAdjacent(fixture.State, fixture.District.TileId, item.Id));
            var move = new GameCommand
            {
                CommandId = new EntityId(900001), PlayerId = fixture.City.OwnerId,
                TurnNumber = fixture.State.TurnNumber, Type = GameCommandType.MoveUnit,
                SubjectId = predictedUnitId, Path = { destination.Id }
            };

            var resolution = new TurnProcessor().Resolve(fixture.State, new[] { move });
            var unit = fixture.State.Units.Single(item => item.Id == predictedUnitId);

            Assert.That(unit.TileId, Is.EqualTo(fixture.District.TileId));
            Assert.That(unit.RemainingMovement, Is.Zero);
            Assert.That(unit.HasAutomaticDefense, Is.False);
            Assert.That(resolution.Events.Any(item => item.Type == GameEventType.MovementBlocked &&
                item.SecondaryValue == (int)MovementStopReason.TrainedThisTurn), Is.True);
        }

        [Test]
        public void CompletedUnitCanMoveAndAttackStartingNextTurn()
        {
            var fixture = CreateFixture(5006);
            var unit = new UnitState
            {
                Id = fixture.State.AllocateId(), OwnerId = fixture.City.OwnerId,
                TileId = fixture.District.TileId, Type = UnitType.Militia,
                HitPoints = UnitRules.MaximumHitPoints(UnitType.Militia),
                RemainingMovement = UnitRules.Movement(UnitType.Militia),
                CreatedTurn = fixture.State.TurnNumber
            };
            fixture.State.Units.Add(unit);
            var destination = fixture.State.Tiles.First(item =>
                item.CityId == fixture.City.Id && item.Id != unit.TileId &&
                MapTraversal.AreAdjacent(fixture.State, unit.TileId, item.Id));
            var blocked = MovementResolver.Resolve(fixture.State, new GameCommand
            {
                PlayerId = unit.OwnerId, SubjectId = unit.Id, Path = { destination.Id }
            });
            Assert.That(blocked.StopReason, Is.EqualTo(MovementStopReason.TrainedThisTurn));

            fixture.State.TurnNumber++;
            var moved = MovementResolver.Resolve(fixture.State, new GameCommand
            {
                PlayerId = unit.OwnerId, SubjectId = unit.Id, Path = { destination.Id }
            });
            Assert.That(moved.StopReason, Is.EqualTo(MovementStopReason.Completed));

            unit.TileId = fixture.District.TileId;
            var attackTarget = destination.Id;
            Assert.DoesNotThrow(() => CombatResolver.Resolve(fixture.State, new CombatEngagementRequest
            {
                AttackingPlayerId = unit.OwnerId, AttackingUnitId = unit.Id,
                TargetTileId = attackTarget
            }));
        }

        private static Fixture CreateFixture(long seed)
        {
            var state = PrototypeMatchFactory.Create(seed);
            var city = state.Cities[0];
            var player = state.Players.First(item => item.Id == city.OwnerId);
            foreach (UnitType type in System.Enum.GetValues(typeof(UnitType)))
                if (!player.UnlockedUnitTypes.Contains(type))
                    player.UnlockedUnitTypes.Add(type);
            city.Gold = 20;
            var occupied = state.Districts.Select(item => item.TileId).ToArray();
            var tile = state.Tiles.First(item => item.CityId == city.Id && !occupied.Contains(item.Id));
            var district = new DistrictState
            {
                Id = state.AllocateId(), CityId = city.Id, TileId = tile.Id,
                Type = DistrictType.Military, ControllerId = city.OwnerId,
                IsOperational = true, AssignedCitizens = 1
            };
            state.Districts.Add(district);
            return new Fixture { State = state, City = city, District = district };
        }

        private static GameCommand TrainingCommand(
            GameState state, CityState city, DistrictState district, UnitType type)
        {
            return new GameCommand
            {
                CommandId = state.AllocateId(), PlayerId = city.OwnerId,
                TurnNumber = state.TurnNumber, Type = GameCommandType.StartTraining,
                SubjectId = district.Id, PrimaryValue = (int)type
            };
        }

        private static void AddUnit(GameState state, CityState city, EntityId tileId, UnitType type)
        {
            state.Units.Add(new UnitState
            {
                Id = state.AllocateId(), OwnerId = city.OwnerId, TileId = tileId,
                Type = type, HitPoints = UnitRules.MaximumHitPoints(type)
            });
        }

        private sealed class Fixture
        {
            public GameState State;
            public CityState City;
            public DistrictState District;
        }
    }
}
