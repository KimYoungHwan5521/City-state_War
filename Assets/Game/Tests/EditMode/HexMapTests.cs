using System.Collections.Generic;
using System.Linq;
using LittleCiv.Core;
using NUnit.Framework;

namespace LittleCiv.Tests
{
    public sealed class HexMapTests
    {
        private static readonly HexCoord[] PrototypeWorldCoords =
        {
            new HexCoord(0, 0),
            new HexCoord(1, 0),
            new HexCoord(0, -1),
            new HexCoord(1, -1),
            new HexCoord(2, -1),
            new HexCoord(-1, 0),
            new HexCoord(2, 0),
            new HexCoord(-1, 1),
            new HexCoord(0, 1),
            new HexCoord(1, 1)
        };

        [Test]
        public void HexRadius_HasExpectedTileCounts()
        {
            Assert.That(HexCoord.WithinRadius(3).Count, Is.EqualTo(37));
            Assert.That(HexCoord.Ring(4).Count, Is.EqualTo(24));
            Assert.That(HexCoord.WithinRadius(4).Count, Is.EqualTo(61));
        }

        [Test]
        public void HexDirections_AreNeighborsAndOpposites()
        {
            var origin = new HexCoord(0, 0);
            for (var index = 0; index < 6; index++)
            {
                Assert.That(HexCoord.Distance(origin, HexCoord.Direction(index)), Is.EqualTo(1));
                Assert.That(
                    HexCoord.Direction(index) + HexCoord.Direction((index + 3) % 6),
                    Is.EqualTo(origin));
                Assert.That(HexCoord.Side(4, index).Count, Is.EqualTo(5));
            }
        }

        [Test]
        public void PrototypeMap_EachCityViewContainsThirtySevenBuildableAndTwentyFourBoundaryTiles()
        {
            var state = CreatePrototypeState();
            var topology = WorldMapGenerator.PopulateTiles(state);

            Assert.That(topology.CityViews.Count, Is.EqualTo(10));
            foreach (var view in topology.CityViews)
            {
                Assert.That(view.Tiles.Count, Is.EqualTo(61));
                Assert.That(view.Tiles.Count(tile => tile.IsBuildable), Is.EqualTo(37));
                Assert.That(view.Tiles.Count(tile => !tile.IsBuildable), Is.EqualTo(24));
                Assert.That(view.Tiles.Select(tile => new HexCoord(tile.LocalQ, tile.LocalR)).Distinct().Count(),
                    Is.EqualTo(61));
            }
        }

        [Test]
        public void PrototypeMap_AdjacentCitiesShareExactlyFiveTileIdsInReverseSideOrder()
        {
            var state = CreatePrototypeState();
            var topology = WorldMapGenerator.PopulateTiles(state);

            foreach (var city in state.Cities)
            {
                for (var direction = 0; direction < 6; direction++)
                {
                    var neighbor = FindCityAt(state, new HexCoord(city.WorldQ, city.WorldR) + HexCoord.Direction(direction));
                    if (neighbor == null)
                    {
                        continue;
                    }

                    var ownIds = SideTileIds(topology.FindView(city.Id), direction);
                    var neighborIds = SideTileIds(topology.FindView(neighbor.Id), (direction + 3) % 6);
                    neighborIds.Reverse();
                    Assert.That(ownIds, Is.EqualTo(neighborIds));
                }
            }
        }

        [Test]
        public void PrototypeMap_ThreeMutuallyAdjacentCitiesShareOneCornerTile()
        {
            var state = CreatePrototypeState();
            var topology = WorldMapGenerator.PopulateTiles(state);
            var triangles = FindCityTriangles(state);

            Assert.That(triangles.Count, Is.GreaterThan(0));
            foreach (var triangle in triangles)
            {
                var shared = topology.FindView(triangle[0].Id).Tiles.Select(tile => tile.TileId)
                    .Intersect(topology.FindView(triangle[1].Id).Tiles.Select(tile => tile.TileId))
                    .Intersect(topology.FindView(triangle[2].Id).Tiles.Select(tile => tile.TileId))
                    .ToList();
                Assert.That(shared.Count, Is.EqualTo(1));
                var tile = state.Tiles.Single(item => item.Id == shared[0]);
                Assert.That(tile.IsSharedBoundary, Is.True);
                Assert.That(tile.VisibleCityIds.Count, Is.EqualTo(3));
            }

            Assert.That(state.Tiles.Where(tile => tile.IsSharedBoundary).Max(tile => tile.VisibleCityIds.Count),
                Is.EqualTo(3));
        }

        [Test]
        public void GeneratedTopology_SurvivesJsonRoundTripInStateHash()
        {
            var state = CreatePrototypeState();
            WorldMapGenerator.PopulateTiles(state);
            var json = LittleCiv.Runtime.GameStateJsonSerializer.Serialize(state);
            var restored = LittleCiv.Runtime.GameStateJsonSerializer.Deserialize(json);

            Assert.That(GameStateHasher.Compute(restored), Is.EqualTo(GameStateHasher.Compute(state)));
        }

        [Test]
        public void PrototypeMap_AllCitiesHaveAValidShortestPath()
        {
            var state = PrototypeMatchFactory.Create(1001);
            foreach (var start in state.Cities)
            {
                foreach (var destination in state.Cities)
                {
                    var path = WorldMapPathfinder.FindCityPath(state, start.Id, destination.Id);
                    Assert.That(path.Count, Is.GreaterThan(0));
                    Assert.That(path[0], Is.EqualTo(start.Id));
                    Assert.That(path[path.Count - 1], Is.EqualTo(destination.Id));
                    for (var index = 1; index < path.Count; index++)
                    {
                        var previous = state.Cities.Find(city => city.Id == path[index - 1]);
                        var current = state.Cities.Find(city => city.Id == path[index]);
                        Assert.That(AreAdjacent(previous, current), Is.True);
                    }
                }
            }
        }

        [Test]
        public void PrototypeMatch_StartsEveryCityWithGovernmentAndMilitiaAtCenter()
        {
            var state = PrototypeMatchFactory.Create(1001);

            Assert.That(state.Districts.Count, Is.EqualTo(10));
            Assert.That(state.Units.Count, Is.EqualTo(10));
            foreach (var city in state.Cities)
            {
                var center = state.MapTopology.FindView(city.Id).Tiles.Single(
                    tile => tile.LocalQ == 0 && tile.LocalR == 0);
                var government = state.Districts.Single(district => district.CityId == city.Id);
                var militia = state.Units.Single(unit => unit.TileId == center.TileId);
                Assert.That(government.TileId, Is.EqualTo(center.TileId));
                Assert.That(government.Type, Is.EqualTo(DistrictType.Government));
                Assert.That(militia.OwnerId, Is.EqualTo(city.OwnerId));
                Assert.That(militia.Type, Is.EqualTo(UnitType.Militia));
                Assert.That(militia.HitPoints, Is.EqualTo(16));
                Assert.That(militia.CarriedFood, Is.EqualTo(6));
            }
        }

        [Test]
        public void SharedTileUnitSelection_ResolvesEveryConnectedCity()
        {
            var state = PrototypeMatchFactory.Create(1001);
            var sharedTile = state.Tiles.First(tile => tile.VisibleCityIds.Count == 3);
            var unit = state.Units[0];
            unit.TileId = sharedTile.Id;

            var visibleCities = MapVisibilityResolver.ResolveCitiesForTile(
                state,
                unit.TileId,
                state.Cities[0].Id);

            Assert.That(visibleCities, Is.EqualTo(sharedTile.VisibleCityIds.OrderBy(id => id.Value).ToList()));
        }

        private static GameState CreatePrototypeState()
        {
            var state = GameState.CreateNew(1001);
            var playerOne = state.AllocateId();
            var playerTwo = state.AllocateId();
            var neutral = state.AllocateId();
            state.Players.Add(new PlayerState { Id = playerOne, Slot = PlayerSlot.PlayerOne });
            state.Players.Add(new PlayerState { Id = playerTwo, Slot = PlayerSlot.PlayerTwo });
            state.Players.Add(new PlayerState { Id = neutral, Slot = PlayerSlot.Neutral });

            for (var index = 0; index < PrototypeWorldCoords.Length; index++)
            {
                var owner = index == 0 ? playerOne : index == 1 ? playerTwo : neutral;
                state.Cities.Add(new CityState
                {
                    Id = state.AllocateId(),
                    OwnerId = owner,
                    WorldQ = PrototypeWorldCoords[index].Q,
                    WorldR = PrototypeWorldCoords[index].R
                });
            }

            return state;
        }

        private static CityState FindCityAt(GameState state, HexCoord world)
        {
            return state.Cities.Find(city => city.WorldQ == world.Q && city.WorldR == world.R);
        }

        private static List<EntityId> SideTileIds(CityMapView view, int direction)
        {
            var placements = new Dictionary<HexCoord, EntityId>();
            foreach (var tile in view.Tiles)
            {
                placements.Add(new HexCoord(tile.LocalQ, tile.LocalR), tile.TileId);
            }

            return HexCoord.Side(4, direction).Select(coord => placements[coord]).ToList();
        }

        private static List<CityState[]> FindCityTriangles(GameState state)
        {
            var result = new List<CityState[]>();
            for (var first = 0; first < state.Cities.Count; first++)
            {
                for (var second = first + 1; second < state.Cities.Count; second++)
                {
                    for (var third = second + 1; third < state.Cities.Count; third++)
                    {
                        var a = state.Cities[first];
                        var b = state.Cities[second];
                        var c = state.Cities[third];
                        if (AreAdjacent(a, b) && AreAdjacent(a, c) && AreAdjacent(b, c))
                        {
                            result.Add(new[] { a, b, c });
                        }
                    }
                }
            }

            return result;
        }

        private static bool AreAdjacent(CityState left, CityState right)
        {
            return HexCoord.Distance(
                new HexCoord(left.WorldQ, left.WorldR),
                new HexCoord(right.WorldQ, right.WorldR)) == 1;
        }
    }
}
