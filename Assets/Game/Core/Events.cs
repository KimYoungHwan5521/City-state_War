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
        DistrictMaintenanceSuspended = 19
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
