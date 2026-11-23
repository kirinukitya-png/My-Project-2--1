using System;
using System.Collections.Generic;

namespace ProjectNST.GameCore.Battle
{
    public enum BattleEffectTriggerKind
    {
        EnemyBattleStart,
        EnemyAttackAftermath,
        EnemyDefeated
    }

    /// <summary>Immutable handoff describing post-attack effects that must resolve before scheduling continues.</summary>
    public sealed class BattlePendingEffectTrigger
    {
        private readonly string[] _effectIds;
        private readonly string[] _targetInstanceIds;

        public string TriggerId { get; }
        public string ActionRequestId { get; }
        public BattleEffectTriggerKind Kind { get; }
        public string SourceInstanceId { get; }
        public string ActorInstanceId { get; }
        public IReadOnlyList<string> EffectIds { get; }
        public IReadOnlyList<string> TargetInstanceIds { get; }

        public BattlePendingEffectTrigger(string triggerId, string actionRequestId,
            BattleEffectTriggerKind kind, string sourceInstanceId, string actorInstanceId,
            IEnumerable<string> effectIds, IEnumerable<string> targetInstanceIds)
        {
            if (string.IsNullOrWhiteSpace(triggerId)) throw new ArgumentException("Trigger ID is required.", nameof(triggerId));
            if (string.IsNullOrWhiteSpace(actionRequestId)) throw new ArgumentException("Action request ID is required.", nameof(actionRequestId));
            if (!Enum.IsDefined(typeof(BattleEffectTriggerKind), kind)) throw new ArgumentOutOfRangeException(nameof(kind));
            if (string.IsNullOrWhiteSpace(sourceInstanceId)) throw new ArgumentException("Source instance ID is required.", nameof(sourceInstanceId));
            if (string.IsNullOrWhiteSpace(actorInstanceId)) throw new ArgumentException("Actor instance ID is required.", nameof(actorInstanceId));
            _effectIds = Copy(effectIds, nameof(effectIds), requireOne: true, requireUnique: false);
            _targetInstanceIds = Copy(targetInstanceIds, nameof(targetInstanceIds), requireOne: false, requireUnique: true);
            TriggerId = triggerId;
            ActionRequestId = actionRequestId;
            Kind = kind;
            SourceInstanceId = sourceInstanceId;
            ActorInstanceId = actorInstanceId;
            EffectIds = Array.AsReadOnly(_effectIds);
            TargetInstanceIds = Array.AsReadOnly(_targetInstanceIds);
        }

        private static string[] Copy(IEnumerable<string> values, string parameter, bool requireOne, bool requireUnique)
        {
            var copy = values == null ? new List<string>() : new List<string>(values);
            if (requireOne && copy.Count == 0) throw new ArgumentException("At least one effect ID is required.", parameter);
            var unique = requireUnique ? new HashSet<string>(StringComparer.Ordinal) : null;
            foreach (string value in copy)
                if (string.IsNullOrWhiteSpace(value) || (unique != null && !unique.Add(value)))
                    throw new ArgumentException("IDs must be non-empty and unique.", parameter);
            return copy.ToArray();
        }
    }
}
