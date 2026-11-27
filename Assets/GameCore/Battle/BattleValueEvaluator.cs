using System;
using System.Collections.Generic;
using System.Linq;
using ProjectNST.GameCore.Actions;
using ProjectNST.GameCore.Board;
using ProjectNST.GameCore.Observation;
using ProjectNST.GameCore.Shop;

namespace ProjectNST.GameCore.Battle
{
    public enum BattleValueStatus
    {
        Evaluated, Disabled, NoEnemy, PublicStatsUnavailable, EnemyContextUnavailable, UnsupportedTarget,
        TargetUnavailable, InvalidDefinition, AttackStateLimitExceeded, DefenseStateLimitExceeded, InvalidInput,
        InvalidCombatState, PurchasePreviewUnavailable, ReferenceUnavailable, DifferentContext, ItemBlockUnavailable
    }

    public enum BattleValuePhase { Input, Attack, Defense, Reference, Preview }

    public sealed class BattleValueRound
    {
        public AllyRoundAttackResult Attack { get; }
        public EnemyRoundDefenseResult Defense { get; }
        public decimal? Value { get; }
        internal AllyRoundAttackPlan RetainedPlan { get; set; }

        internal BattleValueRound(AllyRoundAttackResult attack, EnemyRoundDefenseResult defense, decimal? value)
        {
            Attack = attack;
            Defense = defense;
            Value = value;
        }
    }

    public sealed class BattleValueEvaluation
    {
        internal BattleValueEvaluator Owner { get; }
        public BattleValueStatus Status { get; }
        public BattleValuePhase? FailurePhase { get; }
        public BattleValueRound Current { get; }
        public BattleValueRound Reference { get; }
        public decimal? RawCorrection { get; }
        public decimal? Correction { get; }
        public int? AttributeBlock { get; }
        public int? InitialBlock { get; }
        public AllyRoundEnemyEffects IgnoredEffects => Current?.Defense?.IgnoredEffects ??
            Current?.Attack?.IgnoredEffects ?? AllyRoundEnemyEffects.None;
        public bool UsesPublicMeanStats => Current?.Attack.UsesPublicMeanStats == true ||
            Current?.Defense?.UsesPublicMeanStats == true;
        public bool HasCorrection => Correction.HasValue;
        internal BattleBoardInput RetainedBoard { get; set; }

        internal BattleValueEvaluation(BattleValueEvaluator owner, BattleValueStatus status, BattleValuePhase? phase = null,
            BattleValueRound current = null, BattleValueRound reference = null, decimal? raw = null,
            decimal? correction = null, int? attributeBlock = null, int? initialBlock = null)
        {
            Owner = owner;
            Status = status;
            FailurePhase = phase;
            Current = current;
            Reference = reference;
            RawCorrection = raw;
            Correction = correction;
            AttributeBlock = attributeBlock;
            InitialBlock = initialBlock;
        }
    }

    public sealed class BattleValueComparison
    {
        public BattleValueStatus Status { get; }
        public BattleValueEvaluation Before { get; }
        public BattleValueEvaluation After { get; }
        public decimal? DeltaR { get; }
        public decimal? DeltaCorrection { get; }
        public decimal? ActionContribution => 8m * DeltaCorrection;

        internal BattleValueComparison(BattleValueStatus status, BattleValueEvaluation before, BattleValueEvaluation after)
        {
            Status = status;
            Before = before;
            After = after;
            if (status == BattleValueStatus.Evaluated || status == BattleValueStatus.Disabled || status == BattleValueStatus.NoEnemy)
            {
                DeltaR = after.RawCorrection - before.RawCorrection;
                DeltaCorrection = after.Correction - before.Correction;
            }
        }
    }

    /// <summary>One evaluator instance per Decision. Stores public inputs only and never applies Actions.</summary>
    public sealed class BattleValueEvaluator
    {
        private sealed class Entry
        {
            internal BattleValueEvaluation CurrentOnly;
            internal AllyRoundAttackPlan Plan;
            internal AllyDefenseMember[] Defense;
            internal BattleValueRound Reference;
            internal BattleValueEvaluation Combined;
        }

        private readonly Dictionary<string, Entry> _cache;
        private readonly Dictionary<BattleValueStatus, int> _statuses = new Dictionary<BattleValueStatus, int>();
        private readonly GameObservation _observation;
        private readonly bool _includeDetails;
        private bool _captureBaselineDetails;
        internal bool RetainTraceInputs { get; set; }
        internal bool RetainCandidateDetails { get; set; }
        private BattleBoardInput _current;
        private CharacterPurchasePreview _purchase;
        private readonly EnemyDefenseAttacker[] _reference;
        private readonly bool _hasEnemy;
        private readonly bool _invalidEnemyContext;
        public BattleValueSettings Settings { get; }
        public EnemyEvaluationContext Context { get; }
        public int Requests { get; private set; }
        public int Evaluations { get; private set; }
        public int CacheHits { get; private set; }
        public int CacheLookups { get; private set; }
        public int PlanBuilds { get; private set; }
        public int AttackEvaluations { get; private set; }
        public int DefenseEvaluations { get; private set; }
        public int ReferenceEvaluations { get; private set; }
        public int ReferenceCacheHits { get; private set; }
        public int TargetEvaluations { get; private set; }
        public int AttackPeakStates { get; private set; }
        public int DefensePeakStates { get; private set; }
        public int LimitHits { get; private set; }
        public int PublicMeanEvaluations { get; private set; }
        public int AttackBranches { get; private set; }
        public int AttackMerges { get; private set; }
        public int DefenseBranches { get; private set; }
        public int DefenseMerges { get; private set; }
        public int AttackLimitHits { get; private set; }
        public int DefenseLimitHits { get; private set; }
        public int EffectEvaluations { get; private set; }
        public int CachedBoards => _cache?.Count ?? 0;
        public IReadOnlyDictionary<BattleValueStatus, int> StatusCounts =>
            new System.Collections.ObjectModel.ReadOnlyDictionary<BattleValueStatus, int>(
                new Dictionary<BattleValueStatus, int>(_statuses));

        public BattleValueEvaluator(EnemyEvaluationContext context, BattleValueSettings settings = null,
            bool useCache = true, bool includeDetails = false)
            : this(context, settings, useCache, includeDetails, null)
        {
        }

        private BattleValueEvaluator(EnemyEvaluationContext context, BattleValueSettings settings,
            bool useCache, bool includeDetails, GameObservation observation, bool currentOnly = false)
        {
            Context = context ?? throw new ArgumentNullException(nameof(context));
            Settings = settings ?? new BattleValueSettings();
            _observation = observation;
            _includeDetails = includeDetails;
            if (Settings.BattleCorrectionWeight == 0m) return;
            _invalidEnemyContext = Context.PlayerHP < 0 || Context.Enemies.Any(e =>
                e.InitialEnemy.Slot < 0 || e.InitialEnemy.Slot >= 6 || e.InitialEnemy.HP < 0 ||
                e.Gap < 0 || e.Gap == int.MaxValue) ||
                Context.Enemies.Select(e => e.InitialEnemy.Slot).Distinct().Count() != Context.Enemies.Count;
            _hasEnemy = Context.Enemies.Any(enemy => enemy.InitialEnemy.HP > 0);
            if (useCache && Context.IsAvailable && _hasEnemy && !_invalidEnemyContext)
                _cache = new Dictionary<string, Entry>(StringComparer.Ordinal);
            if (currentOnly || !Context.IsAvailable || !_hasEnemy || _invalidEnemyContext) return;
            var target = Settings.ReferenceEnemyTarget;
            _reference = new[] { new EnemyDefenseAttacker(new AllyRoundEnemyInput(0, 1, Settings.ReferenceEnemyDEF),
                Settings.ReferenceEnemyATK, Settings.ReferenceEnemyGap,
                EnemyTargetObservation.Resolve(new[] { target.TargetId }, new[] { target })) };
        }

        public static BattleValueEvaluator FromObservation(GameObservation observation, BattleValueSettings settings = null,
            bool useCache = true, bool includeDetails = false) =>
            new BattleValueEvaluator(EnemyEvaluationContext.FromObservation(observation), settings, useCache, includeDetails, observation);

        public BattleValueEvaluation EvaluateCurrent()
        {
            if (TryShortcut(out var result)) return CountRequest(result);
            if (_observation == null) return CountRequest(new BattleValueEvaluation(this, BattleValueStatus.InvalidInput, BattleValuePhase.Input));
            _captureBaselineDetails = RetainTraceInputs;
            try { return Evaluate(_current ??= BattleBoardInput.FromObservation(_observation)); }
            finally { _captureBaselineDetails = false; }
        }

        public BattleValueEvaluation CreateBaseline(BattleBoardInput board) => Evaluate(board);

        internal static BattleValueEvaluator ForCurrentRound(EnemyEvaluationContext context, int attackLimit,
            int defenseLimit, bool useCache, bool includeDetails) =>
            new BattleValueEvaluator(context, new BattleValueSettings(battleCorrectionWeight: 1m, defenseWeight: 0m,
                allyAttackMaxStates: attackLimit, enemyBlockMaxStates: defenseLimit), useCache, includeDetails, null,
                currentOnly: true);

        // Current-only Item comparisons use the round's damage and defense outputs directly.
        internal BattleValueEvaluation EvaluateCurrentRound(BattleBoardInput board)
        {
            if (TryShortcut(out var result)) return CountRequest(result);
            if (!TryPrepare(board, out int attribute, out int block, out var failure))
                return CountRequest(new BattleValueEvaluation(this, failure, BattleValuePhase.Input));
            var entry = GetEntry(board, attribute, block);
            if (entry.CurrentOnly != null) return CountRequest(entry.CurrentOnly);
            Evaluations++;
            if (entry.Plan.Status != AllyRoundAttackStatus.Evaluated)
                return CountRequest(new BattleValueEvaluation(this, AttackStatus(entry.Plan.Status), BattleValuePhase.Attack));
            var round = EvaluateRound(entry, Context.Enemies, block, false);
            var status = round.Value.HasValue ? BattleValueStatus.Evaluated : RoundStatus(round);
            BattleValuePhase? phase = status == BattleValueStatus.Evaluated ? (BattleValuePhase?)null :
                round.Defense == null ? BattleValuePhase.Attack : BattleValuePhase.Defense;
            result = new BattleValueEvaluation(this, status, phase, current: round, attributeBlock: attribute, initialBlock: block);
            if (_cache != null && status == BattleValueStatus.Evaluated) entry.CurrentOnly = result;
            return CountRequest(result);
        }

        public BattleValueEvaluation Evaluate(BattleBoardInput board)
        {
            if (TryShortcut(out var result)) return CountRequest(result);
            if (!TryPrepare(board, out int attribute, out int block, out var failure))
                return CountRequest(new BattleValueEvaluation(this, failure, BattleValuePhase.Input));
            if (_cache != null) CacheLookups++;
            var entry = GetEntry(board, attribute, block);
            if (entry.Combined != null)
            {
                CacheHits++;
                return CountRequest(entry.Combined);
            }
            Evaluations++;
            if (entry.Plan.Status != AllyRoundAttackStatus.Evaluated)
                result = new BattleValueEvaluation(this, AttackStatus(entry.Plan.Status), BattleValuePhase.Attack);
            else
            {
                var current = EvaluateRound(entry, Context.Enemies, block, false);
                if (!current.Value.HasValue)
                {
                    var phase = current.Defense == null ? BattleValuePhase.Attack : BattleValuePhase.Defense;
                    result = new BattleValueEvaluation(this, RoundStatus(current), phase, current,
                        attributeBlock: attribute, initialBlock: block);
                }
                else
                {
                    var reference = GetReference(entry, block);
                    if (!reference.Value.HasValue)
                        result = new BattleValueEvaluation(this, BattleValueStatus.ReferenceUnavailable, BattleValuePhase.Reference,
                            current, reference, attributeBlock: attribute, initialBlock: block);
                    else
                        result = Complete(current, reference, attribute, block);
                }
            }
            if (result.UsesPublicMeanStats) PublicMeanEvaluations++;
            if (result.IgnoredEffects != AllyRoundEnemyEffects.None) EffectEvaluations++;
            if (RetainTraceInputs) result.RetainedBoard = board;
            entry.Combined = result;
            return CountRequest(result);
        }

        public BattleValueRound EvaluateReference(BattleBoardInput board)
        {
            if (TryShortcut(out _) || !TryPrepare(board, out int attribute, out int block, out _)) return null;
            var entry = GetEntry(board, attribute, block);
            return entry.Plan.Status == AllyRoundAttackStatus.Evaluated ? GetReference(entry, block) : null;
        }

        public BattleValueComparison Compare(BattleValueEvaluation before, BattleValueEvaluation after)
        {
            if (before == null || after == null)
                return new BattleValueComparison(BattleValueStatus.InvalidInput, before, after);
            if (!ReferenceEquals(before.Owner, this) || !ReferenceEquals(after.Owner, this))
                return new BattleValueComparison(BattleValueStatus.DifferentContext, before, after);
            return new BattleValueComparison(before.HasCorrection ? after.Status : before.Status, before, after);
        }

        public BattleValueComparison Compare(BattleValueEvaluation baseline, BattleBoardInput after) =>
            Compare(baseline, Evaluate(after));

        /// <summary>Projects a Board/Hand transfer whose legality has already been checked by the caller.</summary>
        public BattleValueComparison CompareTransfer(BattleValueEvaluation baseline, int boardSlot, int handSlot)
        {
            if (TryShortcut(out var result)) return Compare(baseline, CountRequest(result));
            if (_observation == null || boardSlot < 0 || boardSlot >= _observation.Board.Slots.Count ||
                handSlot < 0 || handSlot >= _observation.Hand.Slots.Count)
                return Compare(baseline, CountRequest(new BattleValueEvaluation(this, BattleValueStatus.InvalidInput, BattleValuePhase.Input)));
            var member = _observation.Hand.Slots[handSlot].Character;
            var board = _current ??= BattleBoardInput.FromObservation(_observation);
            return Compare(baseline, board.WithSlot(boardSlot,
                member == null ? null : BattleBoardMember.FromObservation(member, boardSlot)));
        }

        public BattleValueEvaluation EvaluatePurchasePreview(CharacterPurchasePreviewResult preview)
        {
            if (TryShortcut(out var result)) return CountRequest(result);
            if (_observation == null || preview?.Succeeded != true || !ReferenceEquals(preview.SourceObservation, _observation))
                return CountRequest(new BattleValueEvaluation(this, BattleValueStatus.PurchasePreviewUnavailable, BattleValuePhase.Preview));
            var board = _current ??= BattleBoardInput.FromObservation(_observation);
            foreach (var change in preview.SlotChanges)
            {
                if (change.Location.Kind != CharacterLocationKind.Board) continue;
                var roll = change.CharacterAfter;
                var after = roll == null ? null : BattleBoardMember.FromRoll(roll, change.Location.SlotIndex,
                    _observation.CharacterStats.Get(roll));
                board = board.WithSlot(change.Location.SlotIndex, after);
            }
            return Evaluate(board);
        }

        public BattleValueComparison ComparePurchasePreview(BattleValueEvaluation baseline, CharacterPurchasePreviewResult preview) =>
            Compare(baseline, EvaluatePurchasePreview(preview));

        public BattleValueComparison ComparePurchase(BattleValueEvaluation baseline, BuyCharacterAction action) =>
            Compare(baseline, EvaluatePurchasePreview(_observation == null || action == null || TryShortcut(out _) ? null :
                (_purchase ??= new CharacterPurchasePreview(_observation)).Preview(action)));

        public BattleValueComparison CompareHypotheticalPurchase(BattleValueEvaluation baseline,
            RosterDefinitionObservation definition, int runCost, int price, int gold, CharacterLocation placement) =>
            Compare(baseline, EvaluatePurchasePreview(_observation == null || definition == null ||
                runCost < 1 || runCost > 5 || price != runCost || gold < 0 || TryShortcut(out _) ? null :
                (_purchase ??= new CharacterPurchasePreview(_observation)).PreviewHypothetical(definition, runCost, price, gold, placement)));

        private bool TryShortcut(out BattleValueEvaluation result)
        {
            BattleValueStatus status;
            if (Settings.BattleCorrectionWeight == 0m) status = BattleValueStatus.Disabled;
            else if (!Context.IsAvailable) status = BattleValueStatus.EnemyContextUnavailable;
            else if (_invalidEnemyContext) status = BattleValueStatus.InvalidInput;
            else if (!_hasEnemy) status = BattleValueStatus.NoEnemy;
            else
            {
                result = null;
                return false;
            }
            bool zero = status == BattleValueStatus.Disabled || status == BattleValueStatus.NoEnemy;
            result = new BattleValueEvaluation(this, status, zero ? (BattleValuePhase?)null : BattleValuePhase.Input,
                raw: zero ? (decimal?)0m : null, correction: zero ? (decimal?)0m : null);
            return true;
        }

        private BattleValueEvaluation CountRequest(BattleValueEvaluation value)
        {
            Requests++;
            _statuses.TryGetValue(value.Status, out int count);
            _statuses[value.Status] = count + 1;
            return value;
        }

        private static bool TryPrepare(BattleBoardInput board, out int attribute, out int block, out BattleValueStatus failure)
        {
            attribute = block = 0;
            failure = BattleValueStatus.InvalidInput;
            if (board == null || board.Columns != 3 || board.Rows != 3 || board.Members.Count > 9 ||
                board.Members.Any(m => m?.Member.DefinitionId == null ||
                    !Enum.IsDefined(typeof(AllyAttackStatsKind), m.StatsKind) ||
                    m.EffectiveAttack < int.MinValue || m.EffectiveAttack > int.MaxValue ||
                    m.EffectiveDefense < int.MinValue || m.EffectiveDefense > int.MaxValue ||
                    (m.StatsKind == AllyAttackStatsKind.Actual && m.EffectiveDefense != decimal.Truncate(m.EffectiveDefense))) ||
                board.UnitRequirements.Any(r => r == null)) return false;
            if (!board.ItemBlock.HasValue)
            {
                failure = BattleValueStatus.ItemBlockUnavailable;
                return false;
            }
            if (board.ItemBlock.Value > int.MaxValue) return false;
            attribute = board.AttributeBlock;
            long combined = Math.Max(0L, board.ItemBlock.Value + attribute);
            if (combined > int.MaxValue) return false;
            block = (int)combined;
            return true;
        }

        private Entry GetEntry(BattleBoardInput board, int attribute, int block)
        {
            string key = _cache == null ? null : board.CacheKey(block) + "|" + attribute;
            if (_cache != null && _cache.TryGetValue(key, out var cached)) return cached;
            PlanBuilds++;
            var entry = new Entry
            {
                Plan = AllyRoundAttackPlan.FromRequirements(board.Members.Select(m =>
                    new AllyAttackMember(m.Member, m.EffectiveAttack, m.StatsKind)).ToArray(), board.UnitRequirements),
                Defense = board.Members.Select(m => new AllyDefenseMember(m.Member.SlotIndex, m.EffectiveDefense, m.StatsKind)).ToArray()
            };
            if (_cache != null) _cache.Add(key, entry);
            return entry;
        }

        private BattleValueRound GetReference(Entry entry, int block)
        {
            if (entry.Reference != null)
            {
                ReferenceCacheHits++;
                return entry.Reference;
            }
            ReferenceEvaluations++;
            return entry.Reference = EvaluateRound(entry, _reference, block, true);
        }

        private BattleValueEvaluation Complete(BattleValueRound current, BattleValueRound reference, int attribute, int block)
        {
            try
            {
                decimal raw = current.Value.Value - reference.Value.Value;
                decimal correction = Settings.BattleCorrectionWeight * raw;
                // Keep two opposite corrections and their proposed Action contribution representable.
                if (Math.Abs(correction) <= decimal.MaxValue / 16m && Math.Abs(raw) <= decimal.MaxValue / 2m)
                    return new BattleValueEvaluation(this, BattleValueStatus.Evaluated, current: current,
                        reference: reference, raw: raw, correction: correction, attributeBlock: attribute, initialBlock: block);
            }
            catch (OverflowException) { }
            return new BattleValueEvaluation(this, BattleValueStatus.InvalidCombatState, BattleValuePhase.Input,
                current, reference, attributeBlock: attribute, initialBlock: block);
        }

        private BattleValueRound EvaluateRound(Entry entry, IReadOnlyList<EnemyDefenseAttacker> enemies, int block, bool reference)
        {
            AttackEvaluations++;
            bool details = _includeDetails || _captureBaselineDetails || RetainCandidateDetails;
            var attack = reference ? AllyRoundAttackEvaluator.EvaluateAgainstImmortalReference(entry.Plan,
                enemies[0].InitialEnemy, Settings.AllyAttackMaxStates, details) :
                AllyRoundAttackEvaluator.Evaluate(entry.Plan, enemies.Select(e => e.InitialEnemy).ToArray(),
                    Settings.AllyAttackMaxStates, details);
            AttackBranches += attack.GeneratedBranches;
            AttackMerges += attack.MergedBranches;
            AttackPeakStates = Math.Max(AttackPeakStates, attack.PeakStates);
            if (attack.Status == AllyRoundAttackStatus.StateLimitExceeded) { LimitHits++; AttackLimitHits++; }
            if (attack.Status != AllyRoundAttackStatus.Evaluated)
                return new BattleValueRound(attack, null, null) { RetainedPlan = RetainTraceInputs ? entry.Plan : null };
            DefenseEvaluations++;
            var defense = EnemyRoundDefenseEvaluator.Evaluate(entry.Defense, enemies, attack, block,
                reference ? null : Context.PlayerHP, Settings.EnemyBlockMaxStates, details);
            DefenseBranches += defense.Counters.GeneratedBlockBranches;
            DefenseMerges += defense.Counters.MergedBlockBranches;
            TargetEvaluations += defense.Counters.TargetEvaluations;
            DefensePeakStates = Math.Max(DefensePeakStates, defense.Counters.PeakBlockStates);
            if (defense.Status == EnemyRoundDefenseStatus.StateLimitExceeded) { LimitHits++; DefenseLimitHits++; }
            decimal? value = null;
            if (defense.Incoming.HasValue)
            {
                try { value = attack.ExpectedEffectiveDamage + Settings.DefenseWeight * (defense.InitialThreat - defense.Incoming); }
                catch (OverflowException) { }
            }
            return new BattleValueRound(attack, defense, value) { RetainedPlan = RetainTraceInputs ? entry.Plan : null };
        }

        private static BattleValueStatus AttackStatus(AllyRoundAttackStatus status)
        {
            switch (status)
            {
                case AllyRoundAttackStatus.StateLimitExceeded: return BattleValueStatus.AttackStateLimitExceeded;
                case AllyRoundAttackStatus.PublicStatsUnavailable: return BattleValueStatus.PublicStatsUnavailable;
                case AllyRoundAttackStatus.InvalidCombatState: return BattleValueStatus.InvalidCombatState;
                default: return BattleValueStatus.InvalidInput;
            }
        }

        private static BattleValueStatus RoundStatus(BattleValueRound round)
        {
            if (round.Defense == null) return AttackStatus(round.Attack.Status);
            switch (round.Defense.Status)
            {
                case EnemyRoundDefenseStatus.StateLimitExceeded: return BattleValueStatus.DefenseStateLimitExceeded;
                case EnemyRoundDefenseStatus.TargetUnavailable: return BattleValueStatus.TargetUnavailable;
                case EnemyRoundDefenseStatus.UnsupportedTarget: return BattleValueStatus.UnsupportedTarget;
                case EnemyRoundDefenseStatus.InvalidTargetDefinition: return BattleValueStatus.InvalidDefinition;
                case EnemyRoundDefenseStatus.PublicStatsUnavailable: return BattleValueStatus.PublicStatsUnavailable;
                case EnemyRoundDefenseStatus.Evaluated:
                case EnemyRoundDefenseStatus.NoEligibleEnemyAttack: return BattleValueStatus.InvalidCombatState;
                default: return BattleValueStatus.InvalidInput;
            }
        }
    }
}
