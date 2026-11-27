using System;
using System.Collections.Generic;
using ProjectNST.GameCore.Battle;

namespace ProjectNST.GameCore.Observation
{
    public enum EnemyTargetStatus { Unavailable, Available, UnsupportedTarget, InvalidDefinition }

    /// <summary>The initial target rule for an enemy already visible in the observation.</summary>
    public sealed class EnemyTargetObservation
    {
        public EnemyTargetStatus Status { get; }
        public string TargetId { get; }
        // BattleTargetDefinition owns read-only copies of its ordered weights.
        public BattleTargetDefinition Definition { get; }

        private EnemyTargetObservation(EnemyTargetStatus status, string targetId, BattleTargetDefinition definition = null)
        {
            Status = status;
            TargetId = targetId;
            Definition = definition;
        }

        public static EnemyTargetObservation Resolve(IReadOnlyList<string> targetIds,
            IReadOnlyList<BattleTargetDefinition> definitions)
        {
            if (targetIds == null) throw new ArgumentNullException(nameof(targetIds));
            string id = targetIds.Count == 0 ? "T001" : targetIds[0];
            if (definitions == null || definitions.Count == 0)
                return new EnemyTargetObservation(EnemyTargetStatus.Unavailable, id);
            if (string.IsNullOrWhiteSpace(id))
                return new EnemyTargetObservation(EnemyTargetStatus.InvalidDefinition, id);

            BattleTargetDefinition found = null;
            foreach (var definition in definitions)
            {
                if (definition == null || !string.Equals(definition.TargetId, id, StringComparison.Ordinal)) continue;
                if (found != null)
                    return new EnemyTargetObservation(EnemyTargetStatus.InvalidDefinition, id);
                found = definition;
            }
            return found == null
                ? new EnemyTargetObservation(EnemyTargetStatus.UnsupportedTarget, id)
                : new EnemyTargetObservation(EnemyTargetStatus.Available, id, found);
        }
    }
}
