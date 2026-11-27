using System;
using System.Collections.Generic;
using System.Linq;
using ProjectNST.GameCore.Battle;
using ProjectNST.GameCore.Observation;
using ProjectNST.GameCore.Shop;
using ProjectNST.GameCore.Stages;
using ProjectNST.GameCore.Synergy;

namespace ProjectNST.GameCore.Items
{
    public sealed class TemporaryItemEffectSettings
    {
        public decimal Kappa { get; }
        public decimal Beta { get; }
        public int AttackStateLimit { get; }
        public int DefenseStateLimit { get; }

        public TemporaryItemEffectSettings(decimal kappa = .10m, decimal beta = .60m,
            int attackStateLimit = 256, int defenseStateLimit = 256)
        {
            if (kappa < 0m) throw new ArgumentOutOfRangeException(nameof(kappa));
            if (beta < 0m) throw new ArgumentOutOfRangeException(nameof(beta));
            if (attackStateLimit < 1 || attackStateLimit > AllyRoundAttackEvaluator.MaximumStateLimit)
                throw new ArgumentOutOfRangeException(nameof(attackStateLimit));
            if (defenseStateLimit < 1 || defenseStateLimit > EnemyRoundDefenseEvaluator.MaximumBlockStateLimit)
                throw new ArgumentOutOfRangeException(nameof(defenseStateLimit));

            // Bound the scale for valid integer combat inputs (six enemies).
            const decimal deltaBound = 6m * int.MaxValue;
            if (kappa > decimal.MaxValue / (16m * deltaBound))
                throw new ArgumentOutOfRangeException(nameof(kappa));
            if (kappa > 0m && beta > (decimal.MaxValue / (16m * deltaBound)) / Math.Max(1m, kappa))
                throw new ArgumentOutOfRangeException(nameof(beta));

            Kappa = kappa;
            Beta = beta;
            AttackStateLimit = attackStateLimit;
            DefenseStateLimit = defenseStateLimit;
        }
    }

    public enum TemporaryItemBattleSide { Before, After }

    public sealed class TemporaryItemRound
    {
        public BattleBoardInput Board { get; }
        public EnemyEvaluationContext Enemies { get; }
        public BattleValueEvaluation Evaluation { get; }
        public int PhysicalEnemyCount => Enemies.Enemies.Count;
        public int LivingEnemyCount => Enemies.Enemies.Count(enemy => enemy.InitialEnemy.HP > 0);
        public decimal? Out => _zeroRound ? 0m : Evaluation?.Current?.Attack.ExpectedEffectiveDamage;
        public decimal? Incoming => _zeroRound ? 0m : Evaluation?.Current?.Defense?.Incoming;
        public decimal? InitialThreat => _zeroRound ? 0m : Evaluation?.Current?.Defense?.InitialThreat;
        public decimal? ExpectedDefeats => _zeroRound ? 0m : Evaluation?.Current?.Attack.ExpectedDefeats;
        public decimal? BlockAbsorbed => _zeroRound ? 0m : Evaluation?.Current?.Defense?.ExpectedAbsorbed;
        public decimal? RemainingBlock => _zeroRound && Board.ItemBlock.HasValue
            ? Math.Max(0L, Board.ItemBlock.Value + Board.AttributeBlock)
            : Evaluation?.Current?.Defense?.ExpectedRemainingBlock;

        private readonly bool _zeroRound;

        internal TemporaryItemRound(BattleBoardInput board, EnemyEvaluationContext enemies,
            BattleValueEvaluation evaluation = null, bool zeroRound = false)
        {
            Board = board;
            Enemies = enemies;
            Evaluation = evaluation;
            _zeroRound = zeroRound;
        }
    }

    public sealed class TemporaryItemEffectEvaluation : ItemEffectValueResult
    {
        public TemporaryItemRound Before { get; }
        public TemporaryItemRound After { get; }
        public long? DirectHpLoss { get; }
        public int? DirectDefeats => Before?.LivingEnemyCount - After?.LivingEnemyCount;
        public decimal? DamageDelta => DirectHpLoss + After?.Out - Before?.Out;
        public decimal? AvoidedIncoming => Before?.Incoming - After?.Incoming;
        public decimal? DamageValue { get; }
        public decimal? DefenseValue { get; }
        public override decimal? EffectValue => DamageValue + DefenseValue;
        public ItemBattleSkipReason BattleSkipReason { get; }
        public TemporaryItemBattleSide? FailedSide { get; }
        public BattleValueDecisionFailure BattleFailure { get; }

        internal TemporaryItemEffectEvaluation(ItemPreviewResult preview, ItemEffectEvaluationStatus status,
            TemporaryItemRound before = null, TemporaryItemRound after = null, long? direct = null,
            ItemBattleSkipReason skip = ItemBattleSkipReason.None, TemporaryItemEffectSettings settings = null,
            TemporaryItemBattleSide? failedSide = null, BattleValueDecisionFailure failure = null) : base(preview, status)
        {
            Before = before;
            After = after;
            DirectHpLoss = direct;
            BattleSkipReason = skip;
            FailedSide = failedSide;
            BattleFailure = failure;
            if (!Succeeded) return;
            decimal scale = PermanentItemEffectEvaluator.ActionScale * settings.Kappa;
            DamageValue = skip == ItemBattleSkipReason.Disabled ? 0m : scale * DamageDelta;
            DefenseValue = skip == ItemBattleSkipReason.Disabled ? 0m : scale * settings.Beta * AvoidedIncoming;
        }
    }

    /// <summary>Evaluates a single public Item effect using the existing exact current-round model.</summary>
    public sealed class TemporaryItemEffectEvaluator
    {
        private readonly GameObservation _observation;
        private readonly bool _useCache;
        private readonly bool _includeDetails;
        private readonly EnemyEvaluationContext _currentEnemies;
        // All fields except HP and DEF are fixed by the evaluator's source observation.
        private readonly Dictionary<string, BattleValueEvaluator> _projectedContexts = new Dictionary<string, BattleValueEvaluator>();
        private BattleValueEvaluator _currentEvaluator;
        private BattleBoardInput _board;
        private BattleValueEvaluation _baseline;

        public TemporaryItemEffectSettings Settings { get; }
        public int RoundRequests { get; private set; }

        public TemporaryItemEffectEvaluator(GameObservation observation, TemporaryItemEffectSettings settings = null,
            bool useCache = true, bool includeDetails = false)
        {
            _observation = observation ?? throw new ArgumentNullException(nameof(observation));
            Settings = settings ?? new TemporaryItemEffectSettings();
            _useCache = useCache;
            _includeDetails = includeDetails;
            _currentEnemies = EnemyEvaluationContext.FromObservation(observation);
        }

        public TemporaryItemEffectEvaluation Evaluate(ItemPreviewResult preview)
        {
            var status = HpValueEvaluator.PreviewStatus(preview);
            if (preview?.Before != null && !ReferenceEquals(preview.Before.Observation, _observation))
                status = ItemEffectEvaluationStatus.DifferentObservation;
            if (status != ItemEffectEvaluationStatus.Evaluated)
                return new TemporaryItemEffectEvaluation(preview, status);

            string id = preview.Item.DefinitionId;
            if (preview.Item.Duration != ItemEffectDuration.ThisBattle || preview.Source == ItemPreviewSource.Event ||
                (id != "Item001" && id != "Item002" && id != "Item003" && id != "Item004"))
                return new TemporaryItemEffectEvaluation(preview, ItemEffectEvaluationStatus.UnsupportedItem);

            _board ??= BattleBoardInput.FromObservation(_observation);
            var change = preview.After.Character;
            var afterBoard = _board;
            if (change != null)
            {
                var original = change.Before;
                if (original.SlotIndex >= UnitMoveRoundPlanner.SlotCount)
                    return new TemporaryItemEffectEvaluation(preview, ItemEffectEvaluationStatus.BattleUnavailable,
                        failedSide: TemporaryItemBattleSide.After);
                afterBoard = _board.WithSlot(original.SlotIndex, new BattleBoardMember(
                    new UnitBoardMember(original.SlotIndex, original.DefinitionId, change.Star),
                    (decimal)change.Attack.Value + change.TemporaryAttack,
                    (decimal)change.Defense.Value + change.TemporaryDefense, attributes: original.Attributes));
            }

            var afterEnemies = ProjectEnemies(preview.After.Enemies);
            long direct = preview.After.Enemies.Sum(enemy => (long)enemy.DirectHpLoss);
            var skip = Settings.Kappa == 0m ? ItemBattleSkipReason.Disabled :
                preview.Before.IsEnemylessEncounter ? ItemBattleSkipReason.Enemyless :
                preview.Before.AllPhysicalEnemiesHaveZeroHp ? ItemBattleSkipReason.AllEnemiesAtZeroHp : ItemBattleSkipReason.None;
            if (skip != ItemBattleSkipReason.None)
            {
                bool zero = skip != ItemBattleSkipReason.Disabled;
                return new TemporaryItemEffectEvaluation(preview, status,
                    new TemporaryItemRound(_board, _currentEnemies, zeroRound: zero),
                    new TemporaryItemRound(afterBoard, afterEnemies, zeroRound: zero),
                    direct, skip, Settings);
            }

            _currentEvaluator ??= CreateRoundEvaluator(_currentEnemies);
            var baseline = _baseline ?? EvaluateRound(_currentEvaluator, _board);
            var before = new TemporaryItemRound(_board, _currentEnemies, baseline);
            if (baseline.Status != BattleValueStatus.Evaluated)
                return BattleFailed(preview, before, new TemporaryItemRound(afterBoard, afterEnemies), direct,
                    TemporaryItemBattleSide.Before, baseline);
            if (_useCache) _baseline = baseline;

            if (preview.After.AllPhysicalEnemiesHaveZeroHp)
                return new TemporaryItemEffectEvaluation(preview, status, before,
                    new TemporaryItemRound(afterBoard, afterEnemies, zeroRound: true), direct, settings: Settings);

            var projected = EvaluateRound(EvaluatorFor(afterEnemies), afterBoard);
            var after = new TemporaryItemRound(afterBoard, afterEnemies, projected);
            return projected.Status == BattleValueStatus.Evaluated
                ? new TemporaryItemEffectEvaluation(preview, status, before, after, direct, settings: Settings)
                : BattleFailed(preview, before, after, direct, TemporaryItemBattleSide.After, projected);
        }

        private TemporaryItemEffectEvaluation BattleFailed(ItemPreviewResult preview, TemporaryItemRound before,
            TemporaryItemRound after, long direct, TemporaryItemBattleSide side, BattleValueEvaluation battle)
        {
            var source = side == TemporaryItemBattleSide.Before ? BattleValueCandidateSource.CurrentBoard : BattleValueCandidateSource.Action;
            return new TemporaryItemEffectEvaluation(preview, ItemEffectEvaluationStatus.BattleUnavailable, before, after, direct,
                failedSide: side, failure: new BattleValueDecisionFailure(source, preview.Item.DefinitionId, battle, battle.Owner.Settings));
        }

        private BattleValueEvaluation EvaluateRound(BattleValueEvaluator evaluator, BattleBoardInput board)
        {
            RoundRequests++;
            return evaluator.EvaluateCurrentRound(board);
        }

        private BattleValueEvaluator CreateRoundEvaluator(EnemyEvaluationContext context) =>
            BattleValueEvaluator.ForCurrentRound(context, Settings.AttackStateLimit, Settings.DefenseStateLimit, _useCache, _includeDetails);

        private BattleValueEvaluator EvaluatorFor(EnemyEvaluationContext context)
        {
            if (ReferenceEquals(context, _currentEnemies)) return _currentEvaluator;
            if (!_useCache) return CreateRoundEvaluator(context);
            string key = string.Join("|", context.Enemies.Select(enemy => FormattableString.Invariant(
                $"{enemy.InitialEnemy.Slot}/{enemy.InitialEnemy.HP}/{enemy.InitialEnemy.Defense}")));
            if (!_projectedContexts.TryGetValue(key, out var evaluator))
                _projectedContexts.Add(key, evaluator = CreateRoundEvaluator(context));
            return evaluator;
        }

        private EnemyEvaluationContext ProjectEnemies(IReadOnlyList<ItemEnemyChange> changes)
        {
            if (changes.Count == 0 || changes.All(change => change.HP == change.Before.HP && change.Defense == change.Before.Defense))
                return _currentEnemies;

            return new EnemyEvaluationContext(_currentEnemies.Enemies.Select(enemy =>
            {
                var change = changes.FirstOrDefault(candidate => candidate.Before.SlotIndex == enemy.InitialEnemy.Slot);
                if (change == null) return enemy;
                var initial = new AllyRoundEnemyInput(enemy.InitialEnemy.Slot, change.HP, change.Defense, enemy.InitialEnemy.Effects);
                return new EnemyDefenseAttacker(initial, enemy.Attack, enemy.Gap, enemy.Target);
            }), _currentEnemies.PlayerHP);
        }
    }
}
