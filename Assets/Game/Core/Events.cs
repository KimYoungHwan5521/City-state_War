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
        TurnEnded = 10
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
