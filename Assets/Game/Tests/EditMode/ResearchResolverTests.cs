using System.Collections.Generic;
using System.Linq;
using LittleCiv.Core;
using NUnit.Framework;

namespace LittleCiv.Tests
{
    public sealed class ResearchResolverTests
    {
        [Test]
        public void SchoolCompletesOnThirdGovernmentScienceTurnAndUnlocksScienceDistrict()
        {
            var state = PrototypeMatchFactory.Create(8001);
            var player = state.Players.Single(item => item.Slot == PlayerSlot.PlayerOne);
            var select = Select(state, player, ResearchType.School);

            var first = new TurnProcessor().Resolve(state, new[] { select });
            var second = new TurnProcessor().Resolve(state, new List<GameCommand>());
            var third = new TurnProcessor().Resolve(state, new List<GameCommand>());

            Assert.That(first.Events.Any(item => item.Type == GameEventType.ResearchCompleted), Is.False);
            Assert.That(second.Events.Any(item => item.Type == GameEventType.ResearchCompleted), Is.False);
            Assert.That(third.Events.Any(item => item.Type == GameEventType.ResearchCompleted &&
                item.PrimaryValue == (int)ResearchType.School), Is.True);
            Assert.That(player.CompletedResearch, Does.Contain(ResearchType.School));
            Assert.That(player.UnlockedDistrictTypes, Does.Contain(DistrictType.Science));
        }

        [Test]
        public void SelectionRequiresCompletedPrerequisiteAndSwitchingPreservesProgress()
        {
            var state = PrototypeMatchFactory.Create(8002);
            var player = state.Players.Single(item => item.Slot == PlayerSlot.PlayerOne);
            var command = Select(state, player, ResearchType.IronWorking);
            Assert.That(ResearchResolver.TrySelect(state, command, out _), Is.False);
            player.CompletedResearch.Add(ResearchType.School);
            Assert.That(ResearchResolver.TrySelect(state, command, out _), Is.True);
            var city = state.Cities.Single(item => item.OwnerId == player.Id);
            city.ResearchPoints = 5;
            ResearchResolver.Advance(state);
            Assert.That(ResearchResolver.Progress(player, ResearchType.IronWorking), Is.EqualTo(5));

            var arts = Select(state, player, ResearchType.Arts);
            Assert.That(ResearchResolver.TrySelect(state, arts, out _), Is.True);
            city.ResearchPoints = 2;
            ResearchResolver.Advance(state);
            Assert.That(ResearchResolver.Progress(player, ResearchType.Arts), Is.EqualTo(2));
            Assert.That(ResearchResolver.Progress(player, ResearchType.IronWorking), Is.EqualTo(5));
        }

        [Test]
        public void CompletionAppliesUnitDefenseAndFoodCapacityUnlocks()
        {
            AssertUnlock(ResearchType.IronWorking, UnitType.IronInfantry, null, 100);
            AssertUnlock(ResearchType.Fortification, null, DefenseFacilityType.Wall, 100);
            AssertUnlock(ResearchType.Salting, null, null, 150);
            AssertUnlock(ResearchType.Canning, null, null, 200);
        }

        [Test]
        public void NoSelectedResearchStoresAtMostOneTurnsScience()
        {
            var state = PrototypeMatchFactory.Create(8003);
            var player = state.Players.Single(item => item.Slot == PlayerSlot.PlayerOne);
            var city = state.Cities.Single(item => item.OwnerId == player.Id);
            city.LastScienceProduction = 3;
            city.ResearchPoints = 12;

            ResearchResolver.Advance(state);

            Assert.That(city.ResearchPoints, Is.EqualTo(3));
        }

        [TestCase(ResearchType.Salting, UnitType.Militia, 9)]
        [TestCase(ResearchType.Canning, UnitType.Militia, 12)]
        [TestCase(ResearchType.Salting, UnitType.MechanizedInfantry, 15)]
        [TestCase(ResearchType.Canning, UnitType.MotorizedSupply, 80)]
        public void PreservationResearchChangesActualUnitCapacity(
            ResearchType research, UnitType type, int expected)
        {
            var state = PrototypeMatchFactory.Create(8200 + (int)research + (int)type);
            var player = state.Players.Single(item => item.Slot == PlayerSlot.PlayerOne);
            player.FoodCapacityPercent = research == ResearchType.Salting ? 150 : 200;
            var unit = state.Units.First(item => item.OwnerId == player.Id);
            unit.Type = type;

            Assert.That(UnitRules.FoodCapacity(state, unit), Is.EqualTo(expected));
        }

        [Test]
        public void LoadingUsesResearchedCapacityInsteadOfBaseCapacity()
        {
            var state = PrototypeMatchFactory.Create(8300);
            var player = state.Players.Single(item => item.Slot == PlayerSlot.PlayerOne);
            player.FoodCapacityPercent = 150;
            var city = state.Cities.Single(item => item.OwnerId == player.Id);
            city.StoredFood = 20;
            var militia = state.Units.First(item => item.OwnerId == player.Id);
            militia.CarriedFood = 6;
            var load = new GameCommand
            {
                PlayerId = player.Id, SubjectId = militia.Id, TargetId = city.Id, PrimaryValue = 99
            };

            Assert.That(UnitFoodResolver.TryLoad(state, load, out var loaded), Is.True);
            Assert.That(loaded, Is.EqualTo(3));
            Assert.That(militia.CarriedFood, Is.EqualTo(9));
        }

        private static void AssertUnlock(
            ResearchType type, UnitType? unit, DefenseFacilityType? defense, int capacity)
        {
            var state = PrototypeMatchFactory.Create(8100 + (int)type);
            var player = state.Players.Single(item => item.Slot == PlayerSlot.PlayerOne);
            var prerequisite = ResearchRules.Prerequisite(type);
            if (prerequisite != ResearchType.None) player.CompletedResearch.Add(prerequisite);
            var command = Select(state, player, type);
            Assert.That(ResearchResolver.TrySelect(state, command, out _), Is.True);
            var city = state.Cities.Single(item => item.OwnerId == player.Id);
            city.ResearchPoints = ResearchRules.Cost(type);
            ResearchResolver.Advance(state);
            if (unit.HasValue) Assert.That(player.UnlockedUnitTypes, Does.Contain(unit.Value));
            if (defense.HasValue) Assert.That(player.UnlockedDefenseTypes, Does.Contain(defense.Value));
            Assert.That(player.FoodCapacityPercent, Is.EqualTo(capacity));
        }

        private static GameCommand Select(GameState state, PlayerState player, ResearchType type)
        {
            return new GameCommand
            {
                CommandId = state.AllocateId(), PlayerId = player.Id, TurnNumber = state.TurnNumber,
                Type = GameCommandType.SelectResearch, PrimaryValue = (int)type
            };
        }
    }
}
