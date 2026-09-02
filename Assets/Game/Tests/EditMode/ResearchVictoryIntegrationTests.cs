using System.Collections.Generic;
using System.Linq;
using LittleCiv.Core;
using NUnit.Framework;

namespace LittleCiv.Tests
{
    public sealed class ResearchVictoryIntegrationTests
    {
        [Test]
        public void SchoolThroughNuclearProjectProducesScienceVictoryInTurnOrder()
        {
            var state = PrototypeMatchFactory.Create(9950);
            var player = state.Players.Single(item => item.Slot == PlayerSlot.PlayerOne);
            var city = state.Cities.Single(item => item.OwnerId == player.Id);
            city.Gold = 500;
            city.Population = 10;
            var processor = new TurnProcessor();

            var school = Select(state, player, ResearchType.School);
            processor.Resolve(state, new[] { school });
            processor.Resolve(state, new List<GameCommand>());
            var schoolTurn = processor.Resolve(state, new List<GameCommand>());
            Assert.That(schoolTurn.Events.Any(item => item.Type == GameEventType.ResearchCompleted &&
                item.PrimaryValue == (int)ResearchType.School), Is.True);

            CompleteImmediately(state, processor, player, city, ResearchType.IronWorking);
            CompleteImmediately(state, processor, player, city, ResearchType.Gunpowder);
            CompleteImmediately(state, processor, player, city, ResearchType.Vehicles);
            CompleteImmediately(state, processor, player, city, ResearchType.NuclearFission);

            var tile = state.MapTopology.FindView(city.Id).Tiles
                .Where(item => item.IsBuildable)
                .Select(item => state.Tiles.Single(tileState => tileState.Id == item.TileId))
                .First(item => state.Districts.All(district => district.TileId != item.Id));
            processor.Resolve(state, new[] { new GameCommand
            {
                CommandId = state.AllocateId(), PlayerId = player.Id, TurnNumber = state.TurnNumber,
                Type = GameCommandType.StartDistrict, SubjectId = city.Id, TargetId = tile.Id,
                PrimaryValue = (int)DistrictType.NuclearFacility
            }});
            for (var turn = 0; turn < DistrictConstructionResolver.NuclearConstructionTurns; turn++)
                processor.Resolve(state, new List<GameCommand>());
            var facility = state.Districts.Single(item => item.CityId == city.Id &&
                item.Type == DistrictType.NuclearFacility);
            Assert.That(facility.IsOperational, Is.True);

            processor.Resolve(state, new[] { new GameCommand
            {
                CommandId = state.AllocateId(), PlayerId = player.Id, TurnNumber = state.TurnNumber,
                Type = GameCommandType.StartNuclearProject, SubjectId = facility.Id
            }});
            processor.Resolve(state, new List<GameCommand>());
            processor.Resolve(state, new List<GameCommand>());
            var victoryTurn = processor.Resolve(state, new List<GameCommand>());

            Assert.That(state.Victory, Is.EqualTo(VictoryType.Science));
            Assert.That(state.WinnerId, Is.EqualTo(player.Id));
            Assert.That(victoryTurn.Events.Any(item => item.Type == GameEventType.NuclearProjectCompleted),
                Is.True);
            Assert.That(victoryTurn.Events.Any(item => item.Type == GameEventType.VictoryTriggered &&
                item.PrimaryValue == (int)VictoryType.Science), Is.True);
        }

        private static void CompleteImmediately(GameState state, TurnProcessor processor,
            PlayerState player, CityState city, ResearchType type)
        {
            city.ResearchPoints = ResearchRules.Cost(type);
            processor.Resolve(state, new[] { Select(state, player, type) });
            Assert.That(player.CompletedResearch, Does.Contain(type));
        }

        private static GameCommand Select(GameState state, PlayerState player, ResearchType type) =>
            new GameCommand
            {
                CommandId = state.AllocateId(), PlayerId = player.Id, TurnNumber = state.TurnNumber,
                Type = GameCommandType.SelectResearch, PrimaryValue = (int)type
            };
    }
}
