using System;
using System.Collections.Generic;
using System.Linq;

namespace ProjectNST.GameCore.Battle
{
    public enum BattleValueCandidateSource { CurrentBoard, Action, CurrentShop, FutureShop }

    public sealed class BattleValueDecisionFailure
    {
        public BattleValueCandidateSource Source { get; }
        public string CandidateId { get; }
        public BattleValueStatus Status { get; }
        public BattleValuePhase? Phase { get; }
        public AllyRoundAttackStatus? AttackStatus { get; }
        public EnemyRoundDefenseStatus? DefenseStatus { get; }
        public int? StateLimit { get; }
        public int? CompletedAttackLayers { get; }
        public int? AttackPeakStates { get; }
        public int? DefensePeakStates { get; }

        internal BattleValueDecisionFailure(BattleValueCandidateSource source, string candidateId,
            BattleValueEvaluation result, BattleValueSettings settings)
        {
            Source = source;
            CandidateId = candidateId;
            Status = result.Status;
            Phase = result.FailurePhase;
            var round = Phase == BattleValuePhase.Reference ? result.Reference : result.Current;
            AttackStatus = round?.Attack.Status;
            DefenseStatus = round?.Defense?.Status;
            CompletedAttackLayers = round?.Attack.CompletedActionLayers;
            AttackPeakStates = round?.Attack.PeakStates;
            DefensePeakStates = round?.Defense?.Counters.PeakBlockStates;
            StateLimit = AttackStatus == AllyRoundAttackStatus.StateLimitExceeded ? (int?)settings.AllyAttackMaxStates :
                DefenseStatus == EnemyRoundDefenseStatus.StateLimitExceeded ? (int?)settings.EnemyBlockMaxStates : null;
        }
    }

    /// <summary>Records whether the caller must use original scores for an entire Decision.</summary>
    public sealed class BattleValueDecision
    {
        private readonly BattleValueEvaluator _evaluator;
        private readonly List<BattleValueDecisionFailure> _failures = new List<BattleValueDecisionFailure>();
        public bool RequiresFallback => _failures.Count != 0;
        public int LegacyUnavailableCount { get; private set; }
        public IReadOnlyList<BattleValueDecisionFailure> Failures => Array.AsReadOnly(_failures
            .OrderBy(f => f.Source).ThenBy(f => f.CandidateId, StringComparer.Ordinal).ThenBy(f => f.Status).ToArray());

        public BattleValueDecision(BattleValueEvaluator evaluator)
        {
            _evaluator = evaluator ?? throw new ArgumentNullException(nameof(evaluator));
        }

        public void Record(BattleValueCandidateSource source, string candidateId,
            BattleValueEvaluation result, bool legacyEvaluable = true)
        {
            if (string.IsNullOrWhiteSpace(candidateId)) throw new ArgumentException("A candidate ID is required.", nameof(candidateId));
            if (!legacyEvaluable)
            {
                LegacyUnavailableCount++;
                return;
            }
            if (result == null || !ReferenceEquals(result.Owner, _evaluator))
                throw new ArgumentException("An evaluation from this Decision is required.", nameof(result));
            if (!result.HasCorrection)
                _failures.Add(new BattleValueDecisionFailure(source, candidateId, result, _evaluator.Settings));
        }
    }
}
