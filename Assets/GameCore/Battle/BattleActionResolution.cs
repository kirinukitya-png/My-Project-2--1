using System;
using System.Collections.Generic;

namespace ProjectNST.GameCore.Battle
{
    public enum BattleActionResolutionKind
    {
        ActionCompleted,
        UnitMoveCompleted,
        PassAfterUnitMove,
        BoundaryCompleted
    }

    /// <summary>Immutable acknowledgement from a later battle resolver.</summary>
    public sealed class BattleActionResolution
    {
        private readonly string[] _participantIds;
        public string RequestId { get; }
        public bool Succeeded { get; }
        public BattleActionResolutionKind Kind { get; }
        public string UnitMoveId { get; }
        public IReadOnlyList<string> ParticipantInstanceIds { get; }
        public bool EndsActorTurn { get; }

        public BattleActionResolution(string requestId, bool succeeded,
            BattleActionResolutionKind kind, string unitMoveId = null,
            IEnumerable<string> participantInstanceIds = null, bool endsActorTurn = true)
        {
            if (string.IsNullOrWhiteSpace(requestId)) throw new ArgumentException("RequestId is required.", nameof(requestId));
            if (!Enum.IsDefined(typeof(BattleActionResolutionKind), kind)) throw new ArgumentOutOfRangeException(nameof(kind));
            _participantIds = participantInstanceIds == null
                ? Array.Empty<string>() : new List<string>(participantInstanceIds).ToArray();
            var unique = new HashSet<string>(StringComparer.Ordinal);
            foreach (string id in _participantIds)
                if (string.IsNullOrWhiteSpace(id) || !unique.Add(id))
                    throw new ArgumentException("Participant IDs must be non-empty and unique.", nameof(participantInstanceIds));
            if (kind == BattleActionResolutionKind.UnitMoveCompleted)
            {
                if (string.IsNullOrWhiteSpace(unitMoveId) || _participantIds.Length < 2)
                    throw new ArgumentException("A completed UnitMove requires its ID and participants.");
            }
            else if (unitMoveId != null || _participantIds.Length != 0)
                throw new ArgumentException("Only a UnitMove completion can contain move data.");
            if (kind == BattleActionResolutionKind.PassAfterUnitMove && !endsActorTurn)
                throw new ArgumentException("Passing after UnitMove participation must end the actor's turn.", nameof(endsActorTurn));

            RequestId = requestId;
            Succeeded = succeeded;
            Kind = kind;
            UnitMoveId = unitMoveId;
            ParticipantInstanceIds = Array.AsReadOnly(_participantIds);
            EndsActorTurn = endsActorTurn;
        }

        public static BattleActionResolution BoundaryCompleted(string requestId, bool succeeded = true) =>
            new BattleActionResolution(requestId, succeeded, BattleActionResolutionKind.BoundaryCompleted);

        public static BattleActionResolution ActionCompleted(string requestId, bool succeeded = true) =>
            new BattleActionResolution(requestId, succeeded, BattleActionResolutionKind.ActionCompleted);

        public static BattleActionResolution UnitMoveCompleted(string requestId, string moveId,
            IEnumerable<string> participantIds, bool endsActorTurn = false, bool succeeded = true) =>
            new BattleActionResolution(requestId, succeeded,
                BattleActionResolutionKind.UnitMoveCompleted, moveId, participantIds, endsActorTurn);

        public static BattleActionResolution PassAfterUnitMove(string requestId) =>
            new BattleActionResolution(requestId, true, BattleActionResolutionKind.PassAfterUnitMove);
    }
}
