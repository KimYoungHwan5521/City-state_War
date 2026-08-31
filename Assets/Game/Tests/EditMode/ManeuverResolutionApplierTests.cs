using System.Collections.Generic;
using LittleCiv.Core;
using NUnit.Framework;

namespace LittleCiv.Tests
{
    public sealed class ManeuverResolutionApplierTests
    {
        [Test]
        public void Wait_ReturnsToLastTileAndBuildsAutomaticDefense()
        {
            var fixture = CreateFixture(sharedStart: false);
            fixture.Unit.TileId = fixture.Blocked;

            ManeuverResolutionApplier.Apply(fixture.State, Resolution(fixture, ManeuverChoice.Wait));

            Assert.That(fixture.Unit.TileId, Is.EqualTo(fixture.Start));
            Assert.That(fixture.Unit.HasAutomaticDefense, Is.True);
            Assert.That(fixture.Unit.RemainingMovement, Is.Zero);
        }

        [Test]
        public void Wait_OnSharedTileDoesNotBuildAutomaticDefense()
        {
            var fixture = CreateFixture(sharedStart: true);

            ManeuverResolutionApplier.Apply(fixture.State, Resolution(fixture, ManeuverChoice.Wait));

            Assert.That(fixture.Unit.HasAutomaticDefense, Is.False);
            Assert.That(fixture.Unit.RemainingMovement, Is.EqualTo(2));
        }

        [Test]
        public void Detour_UsesRemainingMovementAndCannotRequestAgain()
        {
            var fixture = CreateFixture(sharedStart: false);
            var resolution = Resolution(fixture, ManeuverChoice.Detour);
            resolution.DetourPath.Add(fixture.Detour);

            var result = ManeuverResolutionApplier.Apply(fixture.State, resolution);

            Assert.That(result.Movement.StopReason, Is.EqualTo(MovementStopReason.Completed));
            Assert.That(fixture.Unit.TileId, Is.EqualTo(fixture.Detour));
            Assert.That(fixture.Unit.HasAutomaticDefense, Is.True);
            Assert.That(fixture.Unit.RemainingMovement, Is.Zero);
        }

        [Test]
        public void DetourBlockedAgain_DefaultsToWaitAtLastReachedTile()
        {
            var fixture = CreateFixture(sharedStart: false);
            fixture.State.Units.Add(new UnitState
            {
                Id = fixture.State.AllocateId(),
                OwnerId = fixture.EnemyPlayer,
                TileId = fixture.Detour,
                Type = UnitType.Militia,
                HitPoints = 16
            });
            var resolution = Resolution(fixture, ManeuverChoice.Detour);
            resolution.DetourPath.Add(fixture.Detour);

            var result = ManeuverResolutionApplier.Apply(fixture.State, resolution);

            Assert.That(result.Movement.StopReason, Is.EqualTo(MovementStopReason.EnemyOccupied));
            Assert.That(fixture.Unit.TileId, Is.EqualTo(fixture.Start));
            Assert.That(fixture.Unit.HasAutomaticDefense, Is.True);
        }

        [Test]
        public void FightAgainstUnpreparedArrival_MakesBothSidesAttackers()
        {
            var fixture = CreateFixture(sharedStart: false);
            fixture.State.Units.Add(new UnitState
            {
                Id = fixture.State.AllocateId(), OwnerId = fixture.EnemyPlayer,
                TileId = fixture.Blocked, Type = UnitType.Militia, HitPoints = 16,
                HasAutomaticDefense = false
            });

            var result = ManeuverResolutionApplier.Apply(
                fixture.State,
                Resolution(fixture, ManeuverChoice.Fight));

            Assert.That(result.Combat.BothSidesAreAttackers, Is.True);
            Assert.That(result.Combat.TargetTileId, Is.EqualTo(fixture.Blocked));
        }

        [Test]
        public void FightAgainstPreparedUnit_KeepsDefenderRole()
        {
            var fixture = CreateFixture(sharedStart: false);
            fixture.State.Units.Add(new UnitState
            {
                Id = fixture.State.AllocateId(), OwnerId = fixture.EnemyPlayer,
                TileId = fixture.Blocked, Type = UnitType.Militia, HitPoints = 16,
                HasAutomaticDefense = true
            });

            var result = ManeuverResolutionApplier.Apply(
                fixture.State,
                Resolution(fixture, ManeuverChoice.Fight));

            Assert.That(result.Combat.BothSidesAreAttackers, Is.False);
        }

        [Test]
        public void SwapConflictFight_IsAlwaysBothAttackers()
        {
            var fixture = CreateFixture(sharedStart: false);
            var resolution = Resolution(fixture, ManeuverChoice.Fight);
            resolution.StopReason = MovementStopReason.SwapConflict;

            var result = ManeuverResolutionApplier.Apply(fixture.State, resolution);

            Assert.That(result.Combat.BothSidesAreAttackers, Is.True);
        }

        private static Fixture CreateFixture(bool sharedStart)
        {
            var state = GameState.CreateNew(222);
            var player = state.AllocateId();
            var enemy = state.AllocateId();
            state.Players.Add(new PlayerState { Id = player, Slot = PlayerSlot.PlayerOne });
            state.Players.Add(new PlayerState { Id = enemy, Slot = PlayerSlot.PlayerTwo });
            var start = state.AllocateId();
            var blocked = state.AllocateId();
            var detour = state.AllocateId();
            state.Tiles.Add(new TileState { Id = start, ControllerId = player, IsSharedBoundary = sharedStart });
            state.Tiles.Add(new TileState { Id = blocked, ControllerId = enemy });
            state.Tiles.Add(new TileState { Id = detour, ControllerId = player });
            state.MapTopology.CityViews.Add(new CityMapView
            {
                CityId = state.AllocateId(),
                Tiles = new List<CityTilePlacement>
                {
                    new CityTilePlacement { TileId = start, LocalQ = 0, LocalR = 0 },
                    new CityTilePlacement { TileId = blocked, LocalQ = 1, LocalR = 0 },
                    new CityTilePlacement { TileId = detour, LocalQ = 0, LocalR = 1 }
                }
            });
            var unit = new UnitState
            {
                Id = state.AllocateId(), OwnerId = player, TileId = start,
                Type = UnitType.Militia, HitPoints = 16, RemainingMovement = 2
            };
            state.Units.Add(unit);
            return new Fixture
            {
                State = state,
                Player = player,
                EnemyPlayer = enemy,
                Unit = unit,
                Start = start,
                Blocked = blocked,
                Detour = detour
            };
        }

        private static ManeuverResolution Resolution(Fixture fixture, ManeuverChoice choice)
        {
            return new ManeuverResolution
            {
                PlayerId = fixture.Player,
                UnitId = fixture.Unit.Id,
                LastValidTileId = fixture.Start,
                BlockedTileId = fixture.Blocked,
                StopReason = MovementStopReason.EnemyOccupied,
                Choice = choice
            };
        }

        private sealed class Fixture
        {
            public GameState State;
            public EntityId Player;
            public EntityId EnemyPlayer;
            public UnitState Unit;
            public EntityId Start;
            public EntityId Blocked;
            public EntityId Detour;
        }
    }
}
