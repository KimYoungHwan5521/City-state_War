using System;

namespace LittleCiv.Core
{
    public sealed class SimultaneousTurnSimulator
    {
        private readonly TurnProcessor processor = new TurnProcessor();

        public SimultaneousTurnSimulator(GameState initialState)
        {
            State = initialState ?? throw new ArgumentNullException(nameof(initialState));
            Journal = new MatchJournal(initialState);
            Planning = new TurnPlanningSession(State);
        }

        public GameState State { get; }
        public MatchJournal Journal { get; }
        public TurnPlanningSession Planning { get; private set; }

        public TurnResolution ResolveConfirmedTurn()
        {
            if (!Planning.IsClosed)
            {
                throw new InvalidOperationException("Both players must be confirmed before resolving the turn.");
            }

            var commands = Planning.BuildResolutionBatch();
            var resolution = Journal.ResolveAndRecord(State, processor, commands);
            Planning = new TurnPlanningSession(State);
            return resolution;
        }
    }
}
