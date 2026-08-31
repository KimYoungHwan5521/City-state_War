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
                Gold = 10
            });
            state.Players.Add(new PlayerState
            {
                Id = playerTwo,
                Slot = PlayerSlot.PlayerTwo,
                Gold = 10
            });
            state.Players.Add(new PlayerState
            {
                Id = neutral,
                Slot = PlayerSlot.Neutral
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
            return state;
        }
    }
}
