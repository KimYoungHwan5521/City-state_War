using System;
using System.Collections.Generic;

namespace LittleCiv.Core
{
    [Serializable]
    public sealed class TurnRecord
    {
        public int TurnNumber;
        public List<GameCommand> Commands = new List<GameCommand>();
        public List<GameEvent> Events = new List<GameEvent>();
        public GameState StateSnapshot;
        public ulong StateHash;
    }

    [Serializable]
    public sealed class MatchJournal
    {
        public GameState InitialState;
        public List<TurnRecord> Turns = new List<TurnRecord>();

        public MatchJournal()
        {
        }

        public MatchJournal(GameState initialState)
        {
            InitialState = GameStateCopy.Clone(initialState ?? throw new ArgumentNullException(nameof(initialState)));
        }

        public TurnResolution ResolveAndRecord(
            GameState liveState,
            TurnProcessor processor,
            IReadOnlyList<GameCommand> commands)
        {
            if (liveState == null) throw new ArgumentNullException(nameof(liveState));
            if (processor == null) throw new ArgumentNullException(nameof(processor));

            var resolution = processor.Resolve(liveState, commands);
            var record = new TurnRecord
            {
                TurnNumber = resolution.ResolvedTurnNumber,
                StateSnapshot = GameStateCopy.Clone(liveState),
                StateHash = resolution.ResultStateHash
            };
            for (var i = 0; i < resolution.Commands.Count; i++)
            {
                record.Commands.Add(GameCommandCopy.Clone(resolution.Commands[i]));
            }
            for (var i = 0; i < resolution.Events.Count; i++)
            {
                record.Events.Add(CloneEvent(resolution.Events[i]));
            }
            Turns.Add(record);
            return resolution;
        }

        public GameState Replay()
        {
            var replayState = GameStateCopy.Clone(InitialState);
            var processor = new TurnProcessor();
            for (var i = 0; i < Turns.Count; i++)
            {
                var resolution = processor.Resolve(replayState, Turns[i].Commands);
                if (resolution.ResultStateHash != Turns[i].StateHash)
                {
                    throw new InvalidOperationException($"Replay diverged on turn {Turns[i].TurnNumber}.");
                }
            }

            return replayState;
        }

        private static GameEvent CloneEvent(GameEvent source)
        {
            return new GameEvent
            {
                Sequence = source.Sequence,
                TurnNumber = source.TurnNumber,
                Type = source.Type,
                SourceId = source.SourceId,
                TargetId = source.TargetId,
                PrimaryValue = source.PrimaryValue,
                SecondaryValue = source.SecondaryValue
            };
        }
    }
}
