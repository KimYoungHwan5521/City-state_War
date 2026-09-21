using System.Linq;
using LittleCiv.Core;
using NUnit.Framework;

namespace LittleCiv.Tests
{
    public sealed class CityEconomyBreakdownTests
    {
        [Test]
        public void StartingCityBreakdownExplainsGovernmentConsumptionAndMilitiaUpkeep()
        {
            var state = PrototypeMatchFactory.Create(4900);
            var city = state.Cities[0];

            var result = CityEconomyResolver.CalculateBreakdown(state, city);

            Assert.That(result.Food.Government, Is.EqualTo(6));
            Assert.That(result.Food.Total, Is.EqualTo(6));
            Assert.That(result.PopulationConsumption, Is.EqualTo(4));
            Assert.That(result.UnitFoodConsumption, Is.EqualTo(1));
            Assert.That(result.FoodNet, Is.EqualTo(1));
            Assert.That(result.Gold.Total, Is.EqualTo(2));
            Assert.That(result.UnitUpkeep, Is.EqualTo(1));
            Assert.That(result.GrowthRequired, Is.EqualTo(12));
            Assert.That(result.FamineRequired, Is.EqualTo(4));
        }

        [Test]
        public void DistrictBreakdownSeparatesBaseResourceAndAdjacencyBonuses()
        {
            var state = PrototypeMatchFactory.Create(4901);
            var city = state.Cities[0];
            var foodTile = state.Tiles.First(item =>
                item.CityId == city.Id && item.ResourceType == TileResourceType.Food);
            AddDistrict(state, city, foodTile, DistrictType.Agriculture);
            var firstCommerce = TileAt(state, city, 1, 0);
            var secondCommerce = TileAt(state, city, 1, -1);
            firstCommerce.ResourceType = TileResourceType.None;
            secondCommerce.ResourceType = TileResourceType.None;
            AddDistrict(state, city, firstCommerce, DistrictType.Commerce);
            AddDistrict(state, city, secondCommerce, DistrictType.Commerce);

            var result = CityEconomyResolver.CalculateBreakdown(state, city);

            Assert.That(result.Food.DistrictBase, Is.EqualTo(2));
            Assert.That(result.Food.ResourceBonus, Is.EqualTo(2));
            Assert.That(result.Gold.DistrictBase, Is.EqualTo(4));
            Assert.That(result.Gold.ResourceBonus, Is.Zero);
            Assert.That(result.Gold.AdjacencyBonus, Is.EqualTo(2));
            Assert.That(result.Gold.Total, Is.EqualTo(8));
        }

        [Test]
        public void ThreeMilitiaAndOneSupplyCostExactlyFourGoldWithoutPaidDistricts()
        {
            var state = PrototypeMatchFactory.Create(4902);
            var city = state.Cities[1];
            var government = state.Districts.Single(item => item.CityId == city.Id &&
                item.Type == DistrictType.Government);
            var governmentTile = state.Tiles.Single(item => item.Id == government.TileId);
            for (var index = 0; index < 2; index++)
            {
                state.Units.Add(new UnitState
                {
                    Id = state.AllocateId(), OwnerId = city.OwnerId, HomeCityId = city.Id,
                    TileId = governmentTile.Id, Type = UnitType.Militia,
                    HitPoints = UnitRules.MaximumHitPoints(UnitType.Militia)
                });
            }
            state.Units.Add(new UnitState
            {
                Id = state.AllocateId(), OwnerId = city.OwnerId, HomeCityId = city.Id,
                TileId = governmentTile.Id, Type = UnitType.Supply,
                HitPoints = UnitRules.MaximumHitPoints(UnitType.Supply)
            });

            var result = CityEconomyResolver.CalculateBreakdown(state, city);

            Assert.That(result.UnitUpkeep, Is.EqualTo(4));
            Assert.That(result.FacilityUpkeep, Is.Zero);
        }

        [Test]
        public void BothPlayerCitiesPayTheirOwnFourUnitUpkeepWithoutCrossCharging()
        {
            var state = PrototypeMatchFactory.Create(4903);
            var competitors = state.Players.Where(item => item.Slot != PlayerSlot.Neutral).ToArray();
            state.Units.RemoveAll(unit => competitors.Any(player => player.Id == unit.OwnerId));
            foreach (var player in competitors)
            {
                var city = state.Cities.Single(item => item.OwnerId == player.Id);
                city.Gold = 10;
                var government = state.Districts.Single(item => item.CityId == city.Id &&
                    item.Type == DistrictType.Government);
                for (var index = 0; index < 3; index++)
                    AddUnit(state, city, government.TileId, UnitType.Militia);
                AddUnit(state, city, government.TileId, UnitType.Supply);
            }

            new TurnProcessor().Resolve(state, new GameCommand[0]);

            foreach (var player in competitors)
            {
                var city = state.Cities.Single(item => item.OwnerId == player.Id);
                var result = CityEconomyResolver.CalculateBreakdown(state, city);
                Assert.That(result.UnitUpkeep, Is.EqualTo(4), player.Slot.ToString());
                Assert.That(result.FacilityUpkeep, Is.Zero, player.Slot.ToString());
                Assert.That(city.Gold, Is.EqualTo(8), player.Slot + " 도시만 자기 유지비 4금을 내야 한다.");
            }
        }

        private static TileState TileAt(GameState state, CityState city, int q, int r)
        {
            var placement = state.MapTopology.FindView(city.Id).Tiles.Single(item =>
                item.LocalQ == q && item.LocalR == r);
            return state.Tiles.Single(item => item.Id == placement.TileId);
        }

        private static void AddDistrict(GameState state, CityState city, TileState tile, DistrictType type)
        {
            state.Districts.Add(new DistrictState
            {
                Id = state.AllocateId(), CityId = city.Id, TileId = tile.Id, Type = type,
                ControllerId = city.OwnerId, IsOperational = true, AssignedCitizens = 1
            });
        }

        private static void AddUnit(GameState state, CityState city, EntityId tileId, UnitType type)
        {
            state.Units.Add(new UnitState
            {
                Id = state.AllocateId(), OwnerId = city.OwnerId, HomeCityId = city.Id,
                TileId = tileId, Type = type, HitPoints = UnitRules.MaximumHitPoints(type),
                RemainingMovement = UnitRules.Movement(type)
            });
        }
    }
}
