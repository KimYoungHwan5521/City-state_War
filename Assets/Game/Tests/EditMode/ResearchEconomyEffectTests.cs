using System.Linq;
using LittleCiv.Core;
using NUnit.Framework;

namespace LittleCiv.Tests
{
    public sealed class ResearchEconomyEffectTests
    {
        [Test]
        public void PrintingAndMassMediaApplyDistrictBonusThenFinalMultiplier()
        {
            var fixture = CreateFixture();
            AddDistrict(fixture, DistrictType.Culture, 1, 0);
            fixture.Player.CompletedResearch.Add(ResearchType.Printing);
            fixture.Player.CompletedResearch.Add(ResearchType.MassMedia);

            var result = CityEconomyResolver.CalculateBreakdown(fixture.State, fixture.City);

            Assert.That(result.Culture.DistrictBase, Is.EqualTo(2));
            Assert.That(result.Culture.ResearchBonus, Is.EqualTo(1));
            Assert.That(result.Culture.MultiplierBonus, Is.EqualTo(1));
            Assert.That(result.Culture.Total, Is.EqualTo(5));
        }

        [Test]
        public void CurrencyAndEconomicAdministrationApplyBeforeFlooringFinalGold()
        {
            var fixture = CreateFixture();
            AddDistrict(fixture, DistrictType.Commerce, 1, 0);
            fixture.Player.CompletedResearch.Add(ResearchType.Currency);
            fixture.Player.CompletedResearch.Add(ResearchType.EconomicAdministration);

            var result = CityEconomyResolver.CalculateBreakdown(fixture.State, fixture.City);

            Assert.That(result.Gold.ResearchBonus, Is.EqualTo(1));
            Assert.That(result.Gold.MultiplierBonus, Is.EqualTo(1));
            Assert.That(result.Gold.Total, Is.EqualTo(6));
        }

        [Test]
        public void FinanceMakesEachCommerceNeighborWorthTwoUpToTwoNeighbors()
        {
            var fixture = CreateFixture();
            AddDistrict(fixture, DistrictType.Commerce, 1, 0);
            AddDistrict(fixture, DistrictType.Commerce, 1, -1);
            fixture.Player.CompletedResearch.Add(ResearchType.Finance);

            var result = CityEconomyResolver.CalculateBreakdown(fixture.State, fixture.City);

            Assert.That(result.Gold.AdjacencyBonus, Is.EqualTo(4));
            Assert.That(result.Gold.Total, Is.EqualTo(10));
        }

        [Test]
        public void FertilizerAddsOnePerOperationalAgricultureDistrict()
        {
            var fixture = CreateFixture();
            AddDistrict(fixture, DistrictType.Agriculture, 1, 0);
            fixture.Player.CompletedResearch.Add(ResearchType.Fertilizer);

            var result = CityEconomyResolver.CalculateBreakdown(fixture.State, fixture.City);

            Assert.That(result.Food.ResearchBonus, Is.EqualTo(1));
            Assert.That(result.Food.Total, Is.EqualTo(9));
        }

        [Test]
        public void OccupiedDistrictProvidesNeitherResearchYieldNorFinanceAdjacency()
        {
            var fixture = CreateFixture();
            var first = AddDistrict(fixture, DistrictType.Commerce, 1, 0);
            var occupied = AddDistrict(fixture, DistrictType.Commerce, 1, -1);
            fixture.Player.CompletedResearch.Add(ResearchType.Currency);
            fixture.Player.CompletedResearch.Add(ResearchType.Finance);
            occupied.ControllerId = fixture.State.Players.Single(item =>
                item.Slot == PlayerSlot.PlayerTwo).Id;
            occupied.IsOperational = false;

            var result = CityEconomyResolver.CalculateBreakdown(fixture.State, fixture.City);

            Assert.That(first.IsOperational, Is.True);
            Assert.That(result.Gold.DistrictBase, Is.EqualTo(2));
            Assert.That(result.Gold.ResearchBonus, Is.EqualTo(1));
            Assert.That(result.Gold.AdjacencyBonus, Is.Zero);
            Assert.That(result.Gold.Total, Is.EqualTo(5));
        }

        [Test]
        public void IrrigationSecondCitizenBoostsWholeAgricultureYieldByFiftyPercent()
        {
            var fixture = CreateFixture();
            fixture.City.Population = 10;
            fixture.Player.CompletedResearch.Add(ResearchType.Irrigation);
            fixture.Player.CompletedResearch.Add(ResearchType.Fertilizer);
            var agriculture = AddDistrict(fixture, DistrictType.Agriculture, 1, 0);
            fixture.State.Tiles.Single(item => item.Id == agriculture.TileId).ResourceType = TileResourceType.Food;
            var command = new GameCommand
            {
                Type = GameCommandType.AssignCitizen, PlayerId = fixture.Player.Id,
                SubjectId = agriculture.Id, PrimaryValue = 2
            };

            Assert.That(AgricultureCitizenResolver.TryAssign(fixture.State, command), Is.True);
            var result = CityEconomyResolver.CalculateBreakdown(fixture.State, fixture.City);

            Assert.That(agriculture.AssignedCitizens, Is.EqualTo(2));
            Assert.That(result.Food.StaffingBonus, Is.EqualTo(2));
            Assert.That(result.Food.Total, Is.EqualTo(13));
        }

        [Test]
        public void SecondCitizenRequiresIrrigationAndFreeCitizen()
        {
            var fixture = CreateFixture();
            var agriculture = AddDistrict(fixture, DistrictType.Agriculture, 1, 0);
            var command = new GameCommand
            {
                Type = GameCommandType.AssignCitizen, PlayerId = fixture.Player.Id,
                SubjectId = agriculture.Id, PrimaryValue = 2
            };

            Assert.That(AgricultureCitizenResolver.TryAssign(fixture.State, command), Is.False);
            fixture.Player.CompletedResearch.Add(ResearchType.Irrigation);
            Assert.That(AgricultureCitizenResolver.TryAssign(fixture.State, command), Is.False);
        }

        [Test]
        public void MechanizedAgricultureReturnsSecondCitizenAndKeepsBoost()
        {
            var fixture = CreateFixture();
            fixture.City.Population = 10;
            var agriculture = AddDistrict(fixture, DistrictType.Agriculture, 1, 0);
            agriculture.AssignedCitizens = 2;
            fixture.Player.CompletedResearch.Add(ResearchType.Fertilizer);
            fixture.Player.CurrentResearch = ResearchType.MechanizedAgriculture;
            fixture.Player.ResearchProgress.Add(new ResearchProgressState
                { Type = ResearchType.MechanizedAgriculture, Progress = 59 });
            fixture.City.ResearchPoints = 1;

            ResearchResolver.Advance(fixture.State);
            var result = CityEconomyResolver.CalculateBreakdown(fixture.State, fixture.City);

            Assert.That(agriculture.AssignedCitizens, Is.EqualTo(1));
            Assert.That(fixture.Player.CompletedResearch, Does.Contain(ResearchType.MechanizedAgriculture));
            Assert.That(result.Food.StaffingBonus, Is.EqualTo(1));
        }

        [Test]
        public void OccupiedAgricultureGetsNoStaffingBonus()
        {
            var fixture = CreateFixture();
            fixture.Player.CompletedResearch.Add(ResearchType.MechanizedAgriculture);
            var agriculture = AddDistrict(fixture, DistrictType.Agriculture, 1, 0);
            agriculture.ControllerId = fixture.State.Players.Single(item => item.Slot == PlayerSlot.PlayerTwo).Id;
            agriculture.IsOperational = false;

            var result = CityEconomyResolver.CalculateBreakdown(fixture.State, fixture.City);

            Assert.That(result.Food.StaffingBonus, Is.Zero);
            Assert.That(result.Food.DistrictBase, Is.Zero);
        }

        private static Fixture CreateFixture()
        {
            var state = PrototypeMatchFactory.Create(9600);
            var player = state.Players.Single(item => item.Slot == PlayerSlot.PlayerOne);
            var city = state.Cities.Single(item => item.OwnerId == player.Id);
            return new Fixture { State = state, Player = player, City = city };
        }

        private static DistrictState AddDistrict(Fixture fixture, DistrictType type, int q, int r)
        {
            var placement = fixture.State.MapTopology.FindView(fixture.City.Id).Tiles.Single(item =>
                item.LocalQ == q && item.LocalR == r);
            var tile = fixture.State.Tiles.Single(item => item.Id == placement.TileId);
            tile.ResourceType = TileResourceType.None;
            var district = new DistrictState
            {
                Id = fixture.State.AllocateId(), CityId = fixture.City.Id, TileId = tile.Id,
                Type = type, ControllerId = fixture.City.OwnerId,
                IsOperational = true, AssignedCitizens = 1
            };
            fixture.State.Districts.Add(district);
            return district;
        }

        private sealed class Fixture
        {
            public GameState State;
            public PlayerState Player;
            public CityState City;
        }
    }
}
