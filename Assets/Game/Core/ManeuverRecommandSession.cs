using System;
using System.Collections.Generic;

namespace LittleCiv.Core
{
    public enum ManeuverChoice
    {
        None = 0,
        Fight = 1,
        Detour = 2,
        Wait = 3
    }

    public enum ManeuverResolutionReason
    {
        PlayerChoice = 0,
        RequestTimedOut = 1,
        PlayerBudgetExhausted = 2
    }

    [Serializable]
    public sealed class ManeuverRequest
    {
        public EntityId PlayerId;
        public EntityId UnitId;
        public EntityId LastValidTileId;
        public EntityId BlockedTileId;
        public int RemainingMovement;
        public MovementStopReason StopReason;
    }

    [Serializable]
    public sealed class ManeuverResolution
    {
        public EntityId PlayerId;
        public EntityId UnitId;
        public EntityId LastValidTileId;
        public EntityId BlockedTileId;
        public MovementStopReason StopReason;
        public ManeuverChoice Choice;
        public ManeuverResolutionReason Reason;
        public List<EntityId> DetourPath = new List<EntityId>();
    }

    public struct ManeuverPublicStatus
    {
        public bool IsRecommandPending;
        public EntityId ActingPlayerId;
    }

    public sealed class ManeuverRecommandSession
    {
        public const int SecondsPerRequest = 20;
        public const int MaximumSecondsPerPlayerPerTurn = 40;

        private readonly GameState state;
        private readonly List<ManeuverRequest> pending = new List<ManeuverRequest>();
        private readonly HashSet<EntityId> requestedUnits = new HashSet<EntityId>();
        private readonly Dictionary<EntityId, double> usedSeconds = new Dictionary<EntityId, double>();
        private readonly List<ManeuverResolution> resolutions = new List<ManeuverResolution>();
        private double activeElapsedSeconds;
        private bool isStarted;

        public ManeuverRecommandSession(GameState state)
        {
            this.state = state ?? throw new ArgumentNullException(nameof(state));
        }

        public bool HasPendingRequest => pending.Count > 0;
        public int ResolvedCount => resolutions.Count;

        public bool Enqueue(ManeuverRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (isStarted || !ContainsPlayer(request.PlayerId) || !request.UnitId.IsValid ||
                !requestedUnits.Add(request.UnitId))
            {
                return false;
            }

            var copy = CloneRequest(request);
            if (RemainingBudget(copy.PlayerId) <= 0)
            {
                ResolveImmediately(copy, ManeuverResolutionReason.PlayerBudgetExhausted);
                return true;
            }

            pending.Add(copy);
            pending.Sort(CompareRequests);
            return true;
        }

        public ManeuverPublicStatus GetPublicStatus()
        {
            return new ManeuverPublicStatus
            {
                IsRecommandPending = HasPendingRequest,
                ActingPlayerId = HasPendingRequest ? pending[0].PlayerId : default
            };
        }

        public ManeuverRequest GetPrivateRequest(EntityId requestingPlayerId)
        {
            if (!HasPendingRequest || pending[0].PlayerId != requestingPlayerId) return null;
            isStarted = true;
            return CloneRequest(pending[0]);
        }

        public IReadOnlyList<ManeuverResolution> GetOwnResolutions(EntityId playerId)
        {
            var result = new List<ManeuverResolution>();
            for (var i = 0; i < resolutions.Count; i++)
            {
                var resolution = resolutions[i];
                if (resolution.PlayerId != playerId) continue;
                result.Add(CreateResolution(
                    new ManeuverRequest
                    {
                        PlayerId = resolution.PlayerId,
                        UnitId = resolution.UnitId,
                        LastValidTileId = resolution.LastValidTileId,
                        BlockedTileId = resolution.BlockedTileId,
                        StopReason = resolution.StopReason
                    },
                    resolution.Choice,
                    resolution.Reason,
                    resolution.DetourPath));
            }
            return result;
        }

        public bool Respond(EntityId playerId, ManeuverChoice choice, IReadOnlyList<EntityId> detourPath = null)
        {
            isStarted = true;
            if (!HasPendingRequest || pending[0].PlayerId != playerId || choice == ManeuverChoice.None)
            {
                return false;
            }

            if (choice == ManeuverChoice.Detour && (detourPath == null || detourPath.Count == 0))
            {
                return false;
            }

            ConsumeActiveTime();
            var request = pending[0];
            pending.RemoveAt(0);
            resolutions.Add(CreateResolution(
                request,
                choice,
                ManeuverResolutionReason.PlayerChoice,
                detourPath));
            activeElapsedSeconds = 0;
            ResolveBudgetExhaustedFrontRequests();
            return true;
        }

        public void AdvanceTime(double deltaSeconds)
        {
            if (deltaSeconds < 0) throw new ArgumentOutOfRangeException(nameof(deltaSeconds));
            isStarted = true;
            while (deltaSeconds > 0 && HasPendingRequest)
            {
                ResolveBudgetExhaustedFrontRequests();
                if (!HasPendingRequest) return;
                var allowed = Math.Min(SecondsPerRequest, RemainingBudget(pending[0].PlayerId));
                var remainingForRequest = allowed - activeElapsedSeconds;
                var consumed = Math.Min(deltaSeconds, remainingForRequest);
                activeElapsedSeconds += consumed;
                deltaSeconds -= consumed;
                if (activeElapsedSeconds >= allowed)
                {
                    ConsumeActiveTime();
                    var request = pending[0];
                    pending.RemoveAt(0);
                    resolutions.Add(CreateResolution(
                        request,
                        ManeuverChoice.Wait,
                        ManeuverResolutionReason.RequestTimedOut));
                    activeElapsedSeconds = 0;
                }
            }
            ResolveBudgetExhaustedFrontRequests();
        }

        public double RemainingBudget(EntityId playerId)
        {
            usedSeconds.TryGetValue(playerId, out var used);
            return Math.Max(0, MaximumSecondsPerPlayerPerTurn - used);
        }

        private void ResolveBudgetExhaustedFrontRequests()
        {
            while (HasPendingRequest && RemainingBudget(pending[0].PlayerId) <= 0)
            {
                var request = pending[0];
                pending.RemoveAt(0);
                ResolveImmediately(request, ManeuverResolutionReason.PlayerBudgetExhausted);
                activeElapsedSeconds = 0;
            }
        }

        private void ResolveImmediately(ManeuverRequest request, ManeuverResolutionReason reason)
        {
            resolutions.Add(CreateResolution(request, ManeuverChoice.Wait, reason));
        }

        private void ConsumeActiveTime()
        {
            var playerId = pending[0].PlayerId;
            usedSeconds.TryGetValue(playerId, out var used);
            usedSeconds[playerId] = Math.Min(
                MaximumSecondsPerPlayerPerTurn,
                used + activeElapsedSeconds);
        }

        private bool ContainsPlayer(EntityId playerId)
        {
            for (var i = 0; i < state.Players.Count; i++)
            {
                if (state.Players[i].Id == playerId) return true;
            }
            return false;
        }

        private int CompareRequests(ManeuverRequest left, ManeuverRequest right)
        {
            var slot = FindSlot(left.PlayerId).CompareTo(FindSlot(right.PlayerId));
            return slot != 0 ? slot : left.UnitId.CompareTo(right.UnitId);
        }

        private PlayerSlot FindSlot(EntityId playerId)
        {
            for (var i = 0; i < state.Players.Count; i++)
            {
                if (state.Players[i].Id == playerId) return state.Players[i].Slot;
            }
            return PlayerSlot.Neutral;
        }

        private static ManeuverRequest CloneRequest(ManeuverRequest source)
        {
            return new ManeuverRequest
            {
                PlayerId = source.PlayerId,
                UnitId = source.UnitId,
                LastValidTileId = source.LastValidTileId,
                BlockedTileId = source.BlockedTileId,
                RemainingMovement = source.RemainingMovement,
                StopReason = source.StopReason
            };
        }

        private static ManeuverResolution CreateResolution(
            ManeuverRequest request,
            ManeuverChoice choice,
            ManeuverResolutionReason reason,
            IReadOnlyList<EntityId> detourPath = null)
        {
            var result = new ManeuverResolution
            {
                PlayerId = request.PlayerId,
                UnitId = request.UnitId,
                LastValidTileId = request.LastValidTileId,
                BlockedTileId = request.BlockedTileId,
                StopReason = request.StopReason,
                Choice = choice,
                Reason = reason
            };
            if (detourPath != null)
            {
                for (var i = 0; i < detourPath.Count; i++) result.DetourPath.Add(detourPath[i]);
            }
            return result;
        }
    }
}
