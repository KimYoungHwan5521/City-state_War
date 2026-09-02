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

    public enum DefenseFacilityType
    {
        None = 0,
        Wall = 1,
        Moat = 2,
        ModernDefense = 3
    }

    public enum ResearchType
    {
        None = 0, School = 1, IronWorking = 2, Gunpowder = 3, Vehicles = 4,
        NuclearFission = 5, Arts = 6, Printing = 7, MassMedia = 8,
        Currency = 9, Finance = 10, EconomicAdministration = 11,
        Irrigation = 12, Fertilizer = 13, MechanizedAgriculture = 14,
        Salting = 15, Canning = 16, Fortification = 17,
        AdvancedFortification = 18, ModernDefense = 19
    }

    public enum TileResourceType
    {
        None = 0,
        Food = 1,
        Commerce = 2,
        Science = 3,
        Culture = 4
    }

    public enum VictoryType
    {
        None = 0,
        Culture = 1,
        Science = 2,
        Conquest = 3
    }

    [Serializable]
    public sealed class GameState
    {
        public const int CurrentSchemaVersion = 22;

        public int SchemaVersion = CurrentSchemaVersion;
        public long MatchSeed;
        public int TurnNumber = 1;
        public long NextEntityId = 1;
        public VictoryType Victory;
        public EntityId WinnerId;
        public List<PlayerState> Players = new List<PlayerState>();
        public List<CityState> Cities = new List<CityState>();
        public List<TileState> Tiles = new List<TileState>();
        public List<UnitState> Units = new List<UnitState>();
        public List<DistrictState> Districts = new List<DistrictState>();
        public List<UnitTrainingState> UnitTrainings = new List<UnitTrainingState>();
        public List<DefenseFacilityState> DefenseFacilities = new List<DefenseFacilityState>();
        public List<NuclearProjectState> NuclearProjects = new List<NuclearProjectState>();
        public WorldMapTopology MapTopology = new WorldMapTopology();

        public static GameState CreateNew(long matchSeed)
        {
            return new GameState
            {
                MatchSeed = matchSeed,
                TurnNumber = 1,
                NextEntityId = 1
            };
        }

        public bool IsGameOver => Victory != VictoryType.None;

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
        public List<UnitType> UnlockedUnitTypes = new List<UnitType>();
        public List<DistrictType> UnlockedDistrictTypes = new List<DistrictType>();
        public List<DefenseFacilityType> UnlockedDefenseTypes = new List<DefenseFacilityType>();
        public ResearchType CurrentResearch;
        public List<ResearchType> CompletedResearch = new List<ResearchType>();
        public List<ResearchProgressState> ResearchProgress = new List<ResearchProgressState>();
        public int FoodCapacityPercent = 100;
        public bool ResearchUnlocksEnabled;
        public bool HasCompletedNuclearProject;
        public bool HasMetCultureVictoryCondition;
    }

    [Serializable]
    public sealed class ResearchProgressState
    {
        public ResearchType Type;
        public int Progress;
    }

    [Serializable]
    public sealed class CityState
    {
        public EntityId Id;
        public string Name;
        public EntityId OwnerId;
        public int WorldQ;
        public int WorldR;
        public int Population = 4;
        public int GovernmentCitizens = 1;
        public int Gold = 10;
        public int StoredFood;
        public int GrowthProgress;
        public int FamineProgress;
        public int ResearchPoints;
        public int LastFoodProduction;
        public int LastUnitFoodConsumption;
        public int LastGoldProduction;
        public int LastScienceProduction;
        public int LastCultureProduction;
        public EntityId CultureSubjectToId;
        public List<CultureInfluenceState> CultureInfluences = new List<CultureInfluenceState>();
    }

    [Serializable]
    public sealed class CultureInfluenceState
    {
        public EntityId CultureOwnerId;
        public int PreferredCitizens;
        public int ConversionProgress;
        public int ReversionProgress;
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
        public EntityId GroundFoodOwnerId;
        public int GroundFoodReturnTurn;
        public bool IsSharedBoundary;
        public int DefenseBonusPercent;
        public TileResourceType ResourceType;
        public List<EntityId> VisibleCityIds = new List<EntityId>();
    }

    [Serializable]
    public sealed class UnitState
    {
        public EntityId Id;
        public EntityId OwnerId;
        public EntityId HomeCityId;
        public EntityId TileId;
        public UnitType Type;
        public int HitPoints;
        public int CarriedFood;
        public bool IsStarving;
        public int RemainingMovement;
        public bool HasAutomaticDefense;
        public int MaintenancePriority;
        public int CreatedTurn;
        public int ManeuverRecommandTurn;
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
        public int AssignedCitizens;
        public int RemainingConstructionTurns;
        public int CitizenRemovalPriority;
        public int MaintenancePriority;
        public bool IsMaintenanceSuspended;
        public bool IsPillaged;
        public int RemainingRepairTurns;
    }

    [Serializable]
    public sealed class UnitTrainingState
    {
        public EntityId Id;
        public EntityId DistrictId;
        public EntityId OwnerId;
        public UnitType Type;
        public int RemainingTurns;
        public bool IsAwaitingDeployment;
    }

    [Serializable]
    public sealed class DefenseFacilityState
    {
        public EntityId Id;
        public EntityId CityId;
        public EntityId TileId;
        public DefenseFacilityType Type;
        public DefenseFacilityType BuildingType;
        public int RemainingConstructionTurns;
        public bool IsModernDefenseActive;
        public int RemainingReactivationTurns;
    }

    [Serializable]
    public sealed class NuclearProjectState
    {
        public EntityId Id;
        public EntityId DistrictId;
        public EntityId OwnerId;
        public int RemainingTurns;
        public bool IsCompleted;
    }
}
