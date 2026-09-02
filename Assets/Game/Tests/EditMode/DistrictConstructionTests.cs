using System.Collections.Generic;
using System.Linq;
using LittleCiv.Core;
using NUnit.Framework;

namespace LittleCiv.Tests
{
    public sealed class DistrictConstructionTests
    {
        [Test]
        public void ThreeFreeCitizens_CanStartThreeDistrictsSimultaneously()
        {
            var state = PrototypeMatchFactory.Create(4200);
            var city = state.Cities[0];
            var tiles = EmptyBuildableTiles(state, city).Take(4).ToArray();
            var commands = new List<GameCommand>();
            for (var i = 0; i < tiles.Length; i++)
            {
                commands.Add(BuildCommand(state, city, tiles[i], i + 1, DistrictType.Agriculture));
            }

            var result = new TurnProcessor().Resolve(state, commands);
            var newDistricts = state.Districts.Where(item => item.CityId == city.Id &&
                                                             item.Type != DistrictType.Government).ToArray();

            Assert.That(newDistricts, Has.Length.EqualTo(3));
            Assert.That(newDistricts.All(item => item.AssignedCitizens == 1), Is.True);
            Assert.That(newDistricts.All(item => item.RemainingConstructionTurns == 3), Is.True);
            Assert.That(newDistricts.All(item => !item.IsOperational), Is.True);
            Assert.That(DistrictConstructionResolver.CountFreeCitizens(state, city), Is.Zero);
            Assert.That(result.Events.Count(item => item.Type == GameEventType.DistrictConstructionStarted &&
                                                    item.SourceId == city.OwnerId),
                Is.EqualTo(3));
            Assert.That(result.Events.Count(item => item.Type == GameEventType.CommandRejected &&
                                                    item.SecondaryValue == (int)CommandValidationError.InvalidPayload),
                Is.EqualTo(1));
        }

        [Test]
        public void Construction_CompletesAfterThreeLaterConstructionPhases()
        {
            var state = PrototypeMatchFactory.Create(4201);
            var city = state.Cities[0];
            var tile = EmptyBuildableTiles(state, city).First();
            var processor = new TurnProcessor();
            processor.Resolve(state, new[] { BuildCommand(state, city, tile, 1, DistrictType.Commerce) });
            var district = state.Districts.Single(item => item.TileId == tile.Id);

            processor.Resolve(state, new GameCommand[0]);
            Assert.That(district.RemainingConstructionTurns, Is.EqualTo(2));
            processor.Resolve(state, new GameCommand[0]);
            Assert.That(district.RemainingConstructionTurns, Is.EqualTo(1));
            var completion = processor.Resolve(state, new GameCommand[0]);

            Assert.That(district.RemainingConstructionTurns, Is.Zero);
            Assert.That(district.IsOperational, Is.True);
            Assert.That(district.AssignedCitizens, Is.EqualTo(1));
            Assert.That(completion.Events.Any(item =>
                item.Type == GameEventType.DistrictConstructionCompleted && item.SourceId == district.Id), Is.True);
        }

        [Test]
        public void Construction_PausesWhileDistrictIsOccupied()
        {
            var state = PrototypeMatchFactory.Create(4202);
            var city = state.Cities[0];
            var tile = EmptyBuildableTiles(state, city).First();
            Assert.That(DistrictConstructionResolver.TryStart(
                state, BuildCommand(state, city, tile, 1, DistrictType.Military), out var district), Is.True);
            district.ControllerId = state.Players[1].Id;

            DistrictConstructionResolver.Advance(state);

            Assert.That(district.RemainingConstructionTurns, Is.EqualTo(3));
            Assert.That(district.IsOperational, Is.False);
        }

        [Test]
        public void ConstructionState_SurvivesCopyAndJsonRoundTrip()
        {
            var state = PrototypeMatchFactory.Create(4203);
            var city = state.Cities[0];
            state.Players.First(item => item.Id == city.OwnerId)
                .UnlockedDistrictTypes.Add(DistrictType.Science);
            var tile = EmptyBuildableTiles(state, city).First();
            Assert.That(DistrictConstructionResolver.TryStart(
                state, BuildCommand(state, city, tile, 1, DistrictType.Science), out _), Is.True);

            var copy = GameStateCopy.Clone(state);
            var json = LittleCiv.Runtime.GameStateJsonSerializer.Serialize(state);
            var restored = LittleCiv.Runtime.GameStateJsonSerializer.Deserialize(json);

            Assert.That(GameStateHasher.Compute(copy), Is.EqualTo(GameStateHasher.Compute(state)));
            Assert.That(GameStateHasher.Compute(restored), Is.EqualTo(GameStateHasher.Compute(state)));
        }

        private static IEnumerable<TileState> EmptyBuildableTiles(GameState state, CityState city)
        {
            var occupied = new HashSet<EntityId>(state.Districts.Select(item => item.TileId));
            return state.MapTopology.FindView(city.Id).Tiles
                .Where(item => item.IsBuildable && !occupied.Contains(item.TileId))
                .OrderBy(item => item.LocalQ)
                .ThenBy(item => item.LocalR)
                .Select(item => state.Tiles.Single(tile => tile.Id == item.TileId));
        }

        private static GameCommand BuildCommand(
            GameState state,
            CityState city,
            TileState tile,
            long commandId,
            DistrictType type)
        {
            return new GameCommand
            {
                CommandId = new EntityId(commandId + 100000),
                PlayerId = city.OwnerId,
                TurnNumber = state.TurnNumber,
                Type = GameCommandType.StartDistrict,
                SubjectId = city.Id,
                TargetId = tile.Id,
                PrimaryValue = (int)type
            };
        }
    }
}
