using System;

namespace LittleCiv.Core
{
    public enum GameEventType
    {
        None = 0,
        TurnStarted = 1,
        CommandAccepted = 2,
        CommandRejected = 3,
        UnitMoved = 4,
        MovementBlocked = 5,
        CombatResolved = 6,
        UnitDestroyed = 7,
        DistrictOccupied = 8,
        VictoryTriggered = 9,
        TurnEnded = 10,
        PhaseStarted = 11,
        DefaultActionApplied = 12,
        DistrictConstructionStarted = 13,
        DistrictConstructionCompleted = 14,
        PopulationIncreased = 15,
        PopulationDecreased = 16,
        CitizenAssignmentRemoved = 17,
        UnitDisbanded = 18,
        DistrictMaintenanceSuspended = 19,
        UnitTrainingStarted = 20,
        UnitTrainingCompleted = 21,
        UnitDeploymentWaiting = 22,
        UnitFoodLoaded = 23,
        UnitFoodConsumed = 24,
        UnitFoodTransferred = 25,
        GroundFoodDropped = 26,
        GroundFoodReturned = 27,
        UnitRecovered = 28,
        UnitStarvationStarted = 29,
        UnitStarvationEnded = 30,
        UnitStarvedToDeath = 31,
        UnitPromoted = 32,
        DistrictRepairStarted = 33,
        DistrictRepairCompleted = 34,
        DefenseFacilityConstructionStarted = 35,
        DefenseFacilityConstructionCompleted = 36,
        ModernDefenseDeactivated = 37,
        ModernDefenseReactivationStarted = 38,
        ModernDefenseReactivated = 39,
        DistrictPillaged = 40,
        ResearchSelected = 41,
        ResearchProgressed = 42,
        ResearchCompleted = 43,
        NuclearProjectStarted = 44,
        NuclearProjectProgressed = 45,
        NuclearProjectCompleted = 46,
        CultureInfluenceChanged = 47,
        NeutralCultureResolved = 48,
        NeutralResearchProgressed = 49,
        NeutralResearchCompleted = 50,
        NeutralTradeExecuted = 51,
        NeutralTradeReservationApplied = 52,
        NeutralCityOccupationStarted = 53,
        NeutralCityIndependenceChanged = 54,
        NeutralCityRebelled = 55,
        NeutralOccupationYieldCollected = 56,
        NeutralUnitsLevied = 57,
        NeutralLevyReturned = 58,
        NeutralLevyBidLost = 59,
        NeutralLevyAuctionTied = 60,
        NeutralLevyConditionalMoveCancelled = 61,
        NeutralLevyTerminated = 62
    }

    [Serializable]
    public sealed class GameEvent
    {
        public long Sequence;
        public int TurnNumber;
        public GameEventType Type;
        public EntityId SourceId;
        public EntityId TargetId;
        public int PrimaryValue;
        public int SecondaryValue;
    }
}
