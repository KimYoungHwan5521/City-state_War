using System;
using System.Collections.Generic;

namespace LittleCiv.Core
{
    public enum PlayerSlot
    {
        None = 0,
        PlayerOne = 1,
        PlayerTwo = 2,
        Neutral = 3
    }

    public enum DistrictType
    {
        Government = 0,
        Agriculture = 1,
        Commerce = 2,
        Science = 3,
        Culture = 4,
        Military = 5,
        NuclearFacility = 6
    }

    public enum UnitType
    {
        Militia = 0,
        IronInfantry = 1,
        GunpowderInfantry = 2,
        MechanizedInfantry = 3,
        Supply = 4,
        MotorizedSupply = 5
    }

    [Serializable]
    public sealed class GameState
    {
        public const int CurrentSchemaVersion = 1;

        public int SchemaVersion = CurrentSchemaVersion;
        public long MatchSeed;
        public int TurnNumber = 1;
        public long NextEntityId = 1;
        public List<PlayerState> Players = new List<PlayerState>();
        public List<CityState> Cities = new List<CityState>();
        public List<TileState> Tiles = new List<TileState>();
        public List<UnitState> Units = new List<UnitState>();
        public List<DistrictState> Districts = new List<DistrictState>();

        public static GameState CreateNew(long matchSeed)
        {
            return new GameState
            {
                MatchSeed = matchSeed,
                TurnNumber = 1,
                NextEntityId = 1
            };
        }

        public EntityId AllocateId()
        {
            if (NextEntityId <= 0 || NextEntityId == long.MaxValue)
            {
                throw new InvalidOperationException("The entity ID range has been exhausted or corrupted.");
            }

            return new EntityId(NextEntityId++);
        }
    }

    [Serializable]
    public sealed class PlayerState
    {
        public EntityId Id;
        public PlayerSlot Slot;
        public int Gold;
        public int StoredFood;
        public int ReserveTimeSeconds = 180;
    }

    [Serializable]
    public sealed class CityState
    {
        public EntityId Id;
        public EntityId OwnerId;
        public int WorldQ;
        public int WorldR;
        public int Population = 4;
    }

    [Serializable]
    public sealed class TileState
    {
        public EntityId Id;
        public EntityId CityId;
        public int Q;
        public int R;
        public EntityId ControllerId;
        public int GroundFood;
    }

    [Serializable]
    public sealed class UnitState
    {
        public EntityId Id;
        public EntityId OwnerId;
        public EntityId TileId;
        public UnitType Type;
        public int HitPoints;
        public int CarriedFood;
        public bool IsStarving;
    }

    [Serializable]
    public sealed class DistrictState
    {
        public EntityId Id;
        public EntityId CityId;
        public EntityId TileId;
        public DistrictType Type;
        public EntityId ControllerId;
        public bool IsOperational = true;
    }
}
