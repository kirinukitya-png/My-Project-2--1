using System;
using ProjectNST.GameCore.Battle;
using ProjectNST.GameCore.Board;
using ProjectNST.GameCore.Observation;
using ProjectNST.GameCore.Shop;
using ProjectNST.GameCore.Stages;
using ProjectNST.GameCore.Strategy;
using ProjectNST.GameCore.Synergy;

namespace ProjectNST.GameCore.Items
{
    public enum ItemBattleSkipReason { None, Disabled, Event, OutsidePreparation, BoardUnchanged, Enemyless, AllEnemiesAtZeroHp }

    public sealed class PermanentItemEffectEvaluation : ItemEffectValueResult
    {
        public ItemPreviewStatsKind? StatsKind => Preview?.After?.Character?.StatsKind;
        public IntegratedCompositionComparison? Composition { get; }
        public decimal? PermanentCompositionDelta => Composition is IntegratedCompositionComparison comparison
            ? comparison.TotalDelta - comparison.Delta.Capacity : (decimal?)null;
        public decimal? CompositionActionValue => PermanentItemEffectEvaluator.ActionScale * PermanentCompositionDelta;
        public BattleValueComparison Battle { get; }
        public BattleValueDecisionFailure BattleFailure { get; }
        public ItemBattleSkipReason BattleSkipReason { get; }
        public decimal? BattleActionValue => BattleSkipReason != ItemBattleSkipReason.None ? 0m : Battle?.ActionContribution;
        public override decimal? EffectValue => Succeeded ? CompositionActionValue + BattleActionValue : null;

        internal PermanentItemEffectEvaluation(ItemPreviewResult preview, ItemEffectEvaluationStatus status,
            IntegratedCompositionComparison? composition = null, ItemBattleSkipReason skip = ItemBattleSkipReason.None,
            BattleValueComparison battle = null, BattleValueDecisionFailure failure = null) : base(preview, status)
        {
            Composition = composition;
            BattleSkipReason = skip;
            Battle = battle;
            BattleFailure = failure;
        }

    }

    /// <summary>One evaluator per public Decision, reusing composition and battle baselines.</summary>
    public sealed class PermanentItemEffectEvaluator
    {
        internal const decimal ActionScale = 8m;

        private readonly GameObservation _observation;
        private readonly IntegratedCompositionSettings _compositionSettings;
        private readonly BattleValueSettings _battleSettings;
        private readonly bool _useCache;
        private readonly bool _includeDetails;
        private IntegratedCompositionValueEvaluator _composition;
        private BattleBoardInput _board;
        private BattleValueEvaluator _battle;
        private BattleValueEvaluation _battleBaseline;

        public int BattleRequests => _battle?.Requests ?? 0;
        internal int ReferenceEvaluations => _battle?.ReferenceEvaluations ?? 0;

        public PermanentItemEffectEvaluator(GameObservation observation,
            IntegratedCompositionSettings compositionSettings = null, BattleValueSettings battleSettings = null,
            bool useCache = true)
            : this(observation, compositionSettings, battleSettings, useCache, false)
        {
        }

        internal PermanentItemEffectEvaluator(GameObservation observation,
            IntegratedCompositionSettings compositionSettings, BattleValueSettings battleSettings,
            bool useCache, bool includeDetails)
        {
            _observation = observation ?? throw new ArgumentNullException(nameof(observation));
            _compositionSettings = compositionSettings ?? new IntegratedCompositionSettings();
            _battleSettings = battleSettings ?? new BattleValueSettings();
            _useCache = useCache;
            _includeDetails = includeDetails;
        }

        public PermanentItemEffectEvaluation Evaluate(ItemPreviewResult preview)
        {
            var status = HpValueEvaluator.PreviewStatus(preview);
            if (preview?.Before != null && !ReferenceEquals(preview.Before.Observation, _observation))
                status = ItemEffectEvaluationStatus.DifferentObservation;
            if (status != ItemEffectEvaluationStatus.Evaluated)
                return new PermanentItemEffectEvaluation(preview, status);

            var change = preview.After.Character;
            if ((preview.Item.DefinitionId != "Item006" && preview.Item.DefinitionId != "Item007") ||
                preview.Item.Duration != ItemEffectDuration.Immediate || change == null)
                return new PermanentItemEffectEvaluation(preview, ItemEffectEvaluationStatus.UnsupportedItem);
            if (_observation.Board.Columns != BoardState.StandardColumns || _observation.Board.Rows != BoardState.StandardRows ||
                _observation.Hand.SlotCount != HandState.StandardSlotCount)
                return new PermanentItemEffectEvaluation(preview, ItemEffectEvaluationStatus.CompositionUnavailable);

            _composition ??= new IntegratedCompositionValueEvaluator(_observation, _compositionSettings,
                includeDetails: _includeDetails, useCache: _useCache, retainDiagnosticStates: false, permanentStatsOnly: true);
            var composition = _composition.ComparePermanentItemEffect(change);
            var skip = BattleSkip(preview, change);
            if (skip != ItemBattleSkipReason.None)
                return new PermanentItemEffectEvaluation(preview, status, composition, skip);

            _battle ??= BattleValueEvaluator.FromObservation(_observation, _battleSettings, _useCache, _includeDetails);
            _board ??= BattleBoardInput.FromObservation(_observation);
            var before = _battleBaseline ?? _battle.Evaluate(_board);
            if (!before.HasCorrection)
                return BattleFailed(preview, composition, _battle.Compare(before, before), before, BattleValueCandidateSource.CurrentBoard);
            if (_useCache) _battleBaseline = before;
            var original = change.Before;
            int slot = original.SlotIndex;
            var member = new BattleBoardMember(new UnitBoardMember(slot, original.DefinitionId, change.Star),
                (decimal)change.Attack.Value + change.TemporaryAttack,
                (decimal)change.Defense.Value + change.TemporaryDefense,
                change.StatsKind == ItemPreviewStatsKind.PublicMean ? AllyAttackStatsKind.PublicMean : AllyAttackStatsKind.Actual,
                original.Attributes);
            var after = _battle.Evaluate(_board.WithSlot(slot, member));
            var comparison = _battle.Compare(before, after);
            return after.HasCorrection
                ? new PermanentItemEffectEvaluation(preview, status, composition, battle: comparison)
                : BattleFailed(preview, composition, comparison, after, BattleValueCandidateSource.Action);
        }

        private PermanentItemEffectEvaluation BattleFailed(ItemPreviewResult preview, IntegratedCompositionComparison composition,
            BattleValueComparison comparison, BattleValueEvaluation failed, BattleValueCandidateSource source) =>
            new PermanentItemEffectEvaluation(preview, ItemEffectEvaluationStatus.BattleUnavailable, composition,
                battle: comparison, failure: new BattleValueDecisionFailure(source, preview.Item.DefinitionId, failed, _battleSettings));

        private ItemBattleSkipReason BattleSkip(ItemPreviewResult preview, ItemCharacterChange change)
        {
            if (preview.Source == ItemPreviewSource.Event) return ItemBattleSkipReason.Event;
            if (_observation.Stage.Phase != ObservationPhase.Preparation) return ItemBattleSkipReason.OutsidePreparation;
            if (_battleSettings.BattleCorrectionWeight == 0m) return ItemBattleSkipReason.Disabled;
            if (_observation.Stage.EnemyCategory == StageEnemyCategory.None) return ItemBattleSkipReason.Enemyless;
            if (preview.Before.AllPhysicalEnemiesHaveZeroHp) return ItemBattleSkipReason.AllEnemiesAtZeroHp;
            if (change.Before.LocationKind != ObservationLocationKind.Board) return ItemBattleSkipReason.BoardUnchanged;
            return ItemBattleSkipReason.None;
        }
    }
}
