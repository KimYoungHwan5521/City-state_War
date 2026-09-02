namespace LittleCiv.Core
{
    public static class PrototypeMatchFactory
    {
        private static readonly HexCoord[] CityCoordinates =
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

        private static readonly string[] CityNames =
        {
            "A", "B", "N1", "N2", "N3", "N4", "N5", "N6", "N7", "N8"
        };

        public static GameState Create(long matchSeed)
        {
            var state = GameState.CreateNew(matchSeed);
            var playerOne = state.AllocateId();
            var playerTwo = state.AllocateId();
            var neutral = state.AllocateId();
            state.Players.Add(new PlayerState
            {
                Id = playerOne,
                Slot = PlayerSlot.PlayerOne,
                Gold = 10,
                ResearchUnlocksEnabled = true,
                UnlockedUnitTypes = { UnitType.Militia, UnitType.Supply },
                UnlockedDistrictTypes = { DistrictType.Agriculture, DistrictType.Commerce, DistrictType.Military }
            });
            state.Players.Add(new PlayerState
            {
                Id = playerTwo,
                Slot = PlayerSlot.PlayerTwo,
                Gold = 10,
                ResearchUnlocksEnabled = true,
                UnlockedUnitTypes = { UnitType.Militia, UnitType.Supply },
                UnlockedDistrictTypes = { DistrictType.Agriculture, DistrictType.Commerce, DistrictType.Military }
            });
            state.Players.Add(new PlayerState
            {
                Id = neutral,
                Slot = PlayerSlot.Neutral,
                ResearchUnlocksEnabled = true,
                UnlockedUnitTypes = { UnitType.Militia, UnitType.Supply },
                UnlockedDistrictTypes = { DistrictType.Agriculture, DistrictType.Commerce, DistrictType.Military }
            });

            for (var index = 0; index < CityCoordinates.Length; index++)
            {
                var ownerId = index == 0 ? playerOne : index == 1 ? playerTwo : neutral;
                state.Cities.Add(new CityState
                {
                    Id = state.AllocateId(),
                    Name = CityNames[index],
                    OwnerId = ownerId,
                    WorldQ = CityCoordinates[index].Q,
                    WorldR = CityCoordinates[index].R,
                    Population = 4
                });
            }

            WorldMapGenerator.PopulateTiles(state);
            AddStartingGovernmentAndMilitia(state);
            return state;
        }

        private static void AddStartingGovernmentAndMilitia(GameState state)
        {
            foreach (var city in state.Cities)
            {
                var centerPlacement = state.MapTopology.FindView(city.Id).Tiles.Find(
                    tile => tile.LocalQ == 0 && tile.LocalR == 0);
                state.Districts.Add(new DistrictState
                {
                    Id = state.AllocateId(),
                    CityId = city.Id,
                    TileId = centerPlacement.TileId,
                    Type = DistrictType.Government,
                    ControllerId = city.OwnerId,
                    IsOperational = true,
                    AssignedCitizens = 1
                });
                state.Units.Add(new UnitState
                {
                    Id = state.AllocateId(),
                    OwnerId = city.OwnerId,
                    HomeCityId = city.Id,
                    TileId = centerPlacement.TileId,
                    Type = UnitType.Militia,
                    HitPoints = 16,
                    CarriedFood = 6,
                    RemainingMovement = UnitRules.Movement(UnitType.Militia)
                });
            }
        }
    }
}
