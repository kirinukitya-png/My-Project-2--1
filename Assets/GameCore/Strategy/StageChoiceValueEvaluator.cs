using System;
using System.Collections.Generic;
using System.Linq;
using ProjectNST.GameCore.Economy;
using ProjectNST.GameCore.Items;
using ProjectNST.GameCore.Observation;
using ProjectNST.GameCore.Stages;

namespace ProjectNST.GameCore.Strategy
{
    public enum ChoiceValueStatus { Evaluated, IntentionallyOmitted, NotApplicable, MissingPublicInput, InvalidPublicState, UnsupportedChoice }
    public enum ChoiceValueFailure
    {
        None, OutsideChoicePhase, NoLegalChoices, InconsistentChoiceDetails, MissingEncounterCount, InvalidEncounterCount,
        BossOverride, UnsupportedDefinition, MissingReward, MissingHealth, MissingGrowth, MissingEconomy, InvalidBoard,
        InvalidNumericInput
    }
    public enum ChoiceTerminalClass { CombatVictoryRequired, EnemylessCompletionOnSuccessfulResolution }

    public sealed class StageChoiceValueSettings
    {
        public HpValueSettings Hp { get; }
        public EconomyValueSettings Economy { get; }
        public GrowthValueSettings Growth { get; }
        public decimal RewardWeight { get; }
        public decimal ActivatableUnitWeight { get; }
        public decimal AttributeBlockWeight { get; }
        public decimal MissingBoardSlotPenalty { get; }
        public decimal StageStrengthSlope { get; }
        public decimal StageStrengthIntercept { get; }
        public decimal NormalBaseRisk { get; }
        public decimal NormalDeficitWeight { get; }
        public decimal HardBaseRisk { get; }
        public decimal HardDeficitWeight { get; }
        public decimal HardAdditionalStrengthRequirement { get; }
        public decimal MissingHpRiskWeight { get; }

        public StageChoiceValueSettings(HpValueSettings hp = null, EconomyValueSettings economy = null,
            GrowthValueSettings growth = null, decimal rewardWeight = .75m, decimal activatableUnitWeight = 2m,
            decimal attributeBlockWeight = 1m, decimal missingBoardSlotPenalty = 2m, decimal stageStrengthSlope = 6m,
            decimal stageStrengthIntercept = 4m, decimal normalBaseRisk = 2m, decimal normalDeficitWeight = 1m,
            decimal hardBaseRisk = 6m, decimal hardDeficitWeight = 1.5m, decimal hardAdditionalStrengthRequirement = 6m,
            decimal missingHpRiskWeight = 1m)
        {
            if (rewardWeight < 0m || rewardWeight > 1m) throw new ArgumentOutOfRangeException(nameof(rewardWeight));
            var defaults = new HeuristicV14Settings();
            Hp = hp ?? new HpValueSettings();
            Economy = economy ?? defaults.Economy;
            Growth = growth ?? defaults.Growth;
            RewardWeight = rewardWeight;
            ActivatableUnitWeight = Nonnegative(activatableUnitWeight, nameof(activatableUnitWeight));
            AttributeBlockWeight = Nonnegative(attributeBlockWeight, nameof(attributeBlockWeight));
            MissingBoardSlotPenalty = Nonnegative(missingBoardSlotPenalty, nameof(missingBoardSlotPenalty));
            StageStrengthSlope = Nonnegative(stageStrengthSlope, nameof(stageStrengthSlope));
            StageStrengthIntercept = Nonnegative(stageStrengthIntercept, nameof(stageStrengthIntercept));
            NormalBaseRisk = Nonnegative(normalBaseRisk, nameof(normalBaseRisk));
            NormalDeficitWeight = Nonnegative(normalDeficitWeight, nameof(normalDeficitWeight));
            HardBaseRisk = Nonnegative(hardBaseRisk, nameof(hardBaseRisk));
            HardDeficitWeight = Nonnegative(hardDeficitWeight, nameof(hardDeficitWeight));
            HardAdditionalStrengthRequirement = Nonnegative(hardAdditionalStrengthRequirement, nameof(hardAdditionalStrengthRequirement));
            MissingHpRiskWeight = Nonnegative(missingHpRiskWeight, nameof(missingHpRiskWeight));
        }

        private static decimal Nonnegative(decimal value, string parameter) =>
            value >= 0m ? value : throw new ArgumentOutOfRangeException(parameter);
    }

    public sealed class ChoiceBoardStrength
    {
        public decimal BoardCombat { get; }
        public int BoardCount { get; }
        public int MissingSlots { get; }
        public int ActivatableUnitCount { get; }
        public int AttributeBlock { get; }
        public SynergyEvaluation? Synergy { get; }

        internal ChoiceBoardStrength(decimal boardCombat, int count, int missingSlots, int units, int block,
            SynergyEvaluation? synergy = null)
        {
            BoardCombat = boardCombat;
            BoardCount = count;
            MissingSlots = missingSlots;
            ActivatableUnitCount = units;
            AttributeBlock = block;
            Synergy = synergy;
        }

        internal decimal Value(StageChoiceValueSettings settings) => BoardCombat +
            settings.ActivatableUnitWeight * ActivatableUnitCount + settings.AttributeBlockWeight * AttributeBlock -
            settings.MissingBoardSlotPenalty * MissingSlots;
    }

    public sealed class StageChoiceValue
    {
        public StageChoice Choice { get; }
        public bool IsLegal { get; }
        public ChoiceValueStatus Status { get; internal set; }
        public ChoiceValueFailure Failure { get; internal set; }
        public bool IsFinalEncounter { get; }
        public ChoiceTerminalClass? TerminalClass { get; internal set; }
        public int? GoldReward { get; }
        public int? ExperienceReward { get; }
        public decimal? GoldValue { get; internal set; }
        public decimal? InterestValue { get; internal set; }
        public int? AssumedPreRewardGold { get; internal set; }
        public IReadOnlyList<(int Gold, decimal Probability, decimal HoldingDelta)> InterestOutcomes { get; internal set; }
            = Array.Empty<(int, decimal, decimal)>();
        public GrowthValueComparison? Experience { get; internal set; }
        public decimal? ExperienceValue { get; internal set; }
        public decimal? RawRewardValue => GoldValue + InterestValue + ExperienceValue;
        public decimal? WeightedRewardValue { get; internal set; }
        public HpValueComparison Healing { get; internal set; }
        public decimal? HealingValue { get; internal set; }
        public (int Price, bool CanBuy, decimal NetValue)? DiscountPrice { get; internal set; }
        public (int Price, bool CanBuy, decimal NetValue)? NormalPrice { get; internal set; }
        public GrowthValueComparison? PurchaseExperience { get; internal set; }
        public decimal? DiscountValue { get; internal set; }
        public ChoiceValueStatus ShopStatus => ChoiceValueStatus.IntentionallyOmitted;
        public decimal ShopValue => 0m;
        public ChoiceValueStatus RewardStatus { get; internal set; } = ChoiceValueStatus.NotApplicable;
        public ChoiceValueFailure RewardFailure { get; internal set; }
        public ChoiceValueStatus DiscountStatus { get; internal set; } = ChoiceValueStatus.NotApplicable;
        public ChoiceValueFailure DiscountFailure { get; internal set; }
        public ChoiceValueStatus HealingStatus { get; internal set; } = ChoiceValueStatus.NotApplicable;
        public ChoiceValueStatus RiskStatus { get; internal set; } = ChoiceValueStatus.NotApplicable;
        public ChoiceBoardStrength Board { get; internal set; }
        public decimal? Q { get; internal set; }
        public decimal? RequiredStrength { get; internal set; }
        public decimal? StrengthDeficit { get; internal set; }
        public decimal? HpMultiplier { get; internal set; }
        public decimal? BaseRisk { get; internal set; }
        public decimal? DeficitRisk { get; internal set; }
        public decimal? Risk { get; internal set; }
        public decimal? Score { get; internal set; }
        public decimal? SecondaryScore { get; internal set; }

        internal StageChoiceValue(StageChoice choice, bool final, StageChoiceObservation details = null)
        {
            Choice = choice;
            IsLegal = true; // Rows are emitted for AvailableChoices only.
            IsFinalEncounter = final;
            GoldReward = details?.PostBattleGold;
            ExperienceReward = details?.PostBattleXP;
        }
    }

    public sealed class StageChoiceEvaluation
    {
        public IReadOnlyList<StageChoiceValue> Choices { get; }
        public IReadOnlyList<StageChoiceValue> ConfirmedBest { get; }
        public decimal? ConfirmedBestScore => ConfirmedBest.Count == 0 ? (decimal?)null : ConfirmedBest[0].Score;
        public decimal? ReferenceMaximum { get; }
        public ChoiceValueFailure Failure { get; }
        public ChoiceValueStatus Status => Failure == ChoiceValueFailure.OutsideChoicePhase
            ? ChoiceValueStatus.NotApplicable : StageChoiceValueEvaluator.Classify(Failure);
        public bool RequiresLegacyFallback => Failure != ChoiceValueFailure.None && Failure != ChoiceValueFailure.OutsideChoicePhase;

        internal StageChoiceEvaluation(IEnumerable<StageChoiceValue> choices, ChoiceValueFailure failure = ChoiceValueFailure.None)
        {
            var rows = choices.OrderBy(row => row.Choice).ToArray();
            Choices = Array.AsReadOnly(rows);
            var best = rows.Where(row => row.Score.HasValue).OrderByDescending(row => row.TerminalClass)
                .ThenByDescending(row => row.Score).ThenByDescending(row => row.SecondaryScore).FirstOrDefault();
            ReferenceMaximum = best?.Score;
            // A known lower terminal class cannot challenge a successfully scored higher class.
            var blocking = rows.FirstOrDefault(row => !row.Score.HasValue &&
                (best == null || !row.TerminalClass.HasValue ||
                    (int)row.TerminalClass.Value >= (int)(best.TerminalClass ?? ChoiceTerminalClass.CombatVictoryRequired)));
            Failure = failure != ChoiceValueFailure.None ? failure : blocking?.Failure ?? ChoiceValueFailure.None;
            ConfirmedBest = Array.AsReadOnly(Failure == ChoiceValueFailure.None && best != null
                ? rows.Where(row => row.Score == best.Score && row.TerminalClass == best.TerminalClass &&
                    row.SecondaryScore == best.SecondaryScore).ToArray()
                : Array.Empty<StageChoiceValue>());
        }
    }

    /// <summary>Scores public stage choices without creating actions, enemies, or shop offers.</summary>
    public sealed class StageChoiceValueEvaluator
    {
        private static readonly HeuristicStrategySettings CombatSettings = HeuristicStrategy.CreateV13Settings();
        private readonly GameObservation _observation;
        private readonly int? _totalEncounters;
        private readonly int? _normalPrice;
        private readonly bool _useCache;
        private readonly bool _includeDetails;
        private StageChoiceEvaluation _cached;

        public StageChoiceValueSettings Settings { get; }
        public int BattleEvaluationRequests => 0;

        public StageChoiceValueEvaluator(GameObservation observation, int? totalEncounterCount,
            StageChoiceValueSettings settings = null, int? normalExperiencePurchasePrice = null,
            bool useCache = true, bool includeDetails = false)
        {
            _observation = observation ?? throw new ArgumentNullException(nameof(observation));
            _totalEncounters = totalEncounterCount;
            _normalPrice = normalExperiencePurchasePrice;
            Settings = settings ?? new StageChoiceValueSettings();
            _useCache = useCache;
            _includeDetails = includeDetails;
        }

        public StageChoiceEvaluation Evaluate()
        {
            if (_cached != null) return _cached;
            var result = EvaluateCore(null);
            if (_useCache) _cached = result;
            return result;
        }

        internal StageChoiceEvaluation EvaluateCore(ChoiceBoardStrength fixedStrength)
        {
            var stage = _observation.Stage;
            if (stage.Phase != ObservationPhase.AwaitingStageChoice) return Reject(ChoiceValueFailure.OutsideChoicePhase);
            if (stage.AvailableChoices.Count == 0) return Reject(ChoiceValueFailure.NoLegalChoices);
            if (!_totalEncounters.HasValue) return Reject(ChoiceValueFailure.MissingEncounterCount);
            if (stage.EncounterIndex < 0 || stage.StageNumber <= 0 || stage.RoundNumber <= 0 ||
                stage.EncounterIndex >= _totalEncounters.Value) return Reject(ChoiceValueFailure.InvalidEncounterCount);

            var legal = new HashSet<StageChoice>(stage.AvailableChoices);
            var details = stage.ChoiceDetails;
            if (stage.HasSelection || !_observation.IsStrategyActionAllowed || details.Any(row => row == null) ||
                legal.Count != stage.AvailableChoices.Count || details.Select(row => row.Choice).Distinct().Count() != details.Count ||
                !legal.SetEquals(details.Where(row => row.IsAvailable).Select(row => row.Choice)))
                return Reject(ChoiceValueFailure.InconsistentChoiceDetails);
            bool final = stage.EncounterIndex == _totalEncounters.Value - 1;
            var context = new Decision(this, fixedStrength);
            var rows = details.Where(row => row.IsAvailable).Select(row => context.Evaluate(row, final));
            return new StageChoiceEvaluation(rows);
        }

        private static StageChoiceEvaluation Reject(ChoiceValueFailure failure) =>
            new StageChoiceEvaluation(Array.Empty<StageChoiceValue>(), failure);

        private sealed class Decision
        {
            private readonly StageChoiceValueEvaluator _owner;
            private GameObservation Observation => _owner._observation;
            private StageChoiceValueSettings Settings => _owner.Settings;
            private readonly Dictionary<int, decimal> _holdingValues = new Dictionary<int, decimal>();
            private readonly Dictionary<int, GrowthValueComparison> _growthValues = new Dictionary<int, GrowthValueComparison>();
            private EconomyValueEvaluator _economy;
            private GrowthValueEvaluator.Baseline _growth;
            private ChoiceBoardStrength _strength;
            private readonly ChoiceBoardStrength _fixedStrength;

            public Decision(StageChoiceValueEvaluator owner, ChoiceBoardStrength fixedStrength)
            {
                _owner = owner;
                _fixedStrength = fixedStrength;
            }

            public StageChoiceValue Evaluate(StageChoiceObservation detail, bool final)
            {
                var row = new StageChoiceValue(detail.Choice, final, detail);
                if (detail.BossRewardOverridesChoice || detail.EnemyCategory == StageEnemyCategory.Boss)
                    return Failed(row, ChoiceValueFailure.BossOverride);
                if (!Supported(detail)) return Failed(row, ChoiceValueFailure.UnsupportedDefinition);
                bool combat = detail.EnemyCategory != StageEnemyCategory.None;
                row.TerminalClass = !final ? (ChoiceTerminalClass?)null : combat ?
                    ChoiceTerminalClass.CombatVictoryRequired : ChoiceTerminalClass.EnemylessCompletionOnSuccessfulResolution;
                try
                {
                    row.HealingValue = 0m;
                    row.Risk = 0m;
                    row.GoldValue = row.InterestValue = row.ExperienceValue = row.DiscountValue = 0m;
                    if (detail.SelectionEffect == StageSelectionEffectKind.HealPlayer)
                    {
                        row.HealingStatus = Classify(HealthFailure);
                        if (HealthFailure != ChoiceValueFailure.None) return Failed(row, HealthFailure);
                        row.Healing = new HpValueEvaluator(Settings.Hp).CompareHealing(Observation.Player.HP,
                            detail.SelectionEffectAmount, Observation.Player.MaximumHP);
                        row.HealingValue = row.Healing.Delta;
                    }
                    if (combat)
                    {
                        row.RiskStatus = Classify(HealthFailure);
                        if (HealthFailure != ChoiceValueFailure.None) return Failed(row, HealthFailure);
                        row.Board = _strength ??= _fixedStrength ?? ReadBoard();
                        if (row.Board == null)
                        {
                            row.RiskStatus = ChoiceValueStatus.InvalidPublicState;
                            return Failed(row, ChoiceValueFailure.InvalidBoard);
                        }
                        EvaluateRisk(row, detail.EnemyCategory == StageEnemyCategory.Hard);
                        row.GoldValue = row.InterestValue = row.ExperienceValue = null;
                        row.RewardFailure = EvaluateRewards(row);
                        row.RewardStatus = Classify(row.RewardFailure);
                        if (!final && row.RewardFailure != ChoiceValueFailure.None) return Failed(row, row.RewardFailure);
                    }
                    if (detail.ShopMode == StageShopMode.Item)
                    {
                        row.DiscountValue = null;
                        row.DiscountFailure = EvaluateDiscount(row, detail.ExperiencePurchaseGoldCost);
                        row.DiscountStatus = Classify(row.DiscountFailure);
                        if (!final && row.DiscountFailure != ChoiceValueFailure.None) return Failed(row, row.DiscountFailure);
                    }
                    row.WeightedRewardValue = final ? 0m : Settings.RewardWeight * row.RawRewardValue;
                    row.Score = final ? -row.Risk : row.HealingValue + row.WeightedRewardValue + row.DiscountValue - row.Risk;
                    row.SecondaryScore = final ? row.HealingValue : 0m;
                    row.Status = ChoiceValueStatus.Evaluated;
                    return row;
                }
                catch (OverflowException)
                {
                    return Failed(row, ChoiceValueFailure.InvalidNumericInput);
                }
            }

            private ChoiceValueFailure HealthFailure => !Observation.Player.HasHealth ? ChoiceValueFailure.MissingHealth :
                Observation.Player.MaximumHP <= 0 || Observation.Player.HP < 0 || Observation.Player.HP > Observation.Player.MaximumHP
                    ? ChoiceValueFailure.InvalidNumericInput : ChoiceValueFailure.None;

            private ChoiceBoardStrength ReadBoard()
            {
                var board = Observation.Board;
                var members = board.Slots.Where(slot => slot.Character != null).ToArray();
                if (board.Columns != 3 || board.Rows != 3 || board.Slots.Count != 9 ||
                    members.Any(slot => !Observation.SelectedRoster.Any(def => def.DefinitionId == slot.Character.DefinitionId)))
                    return null;
                var synergy = new SynergyValueEvaluator(Observation).EvaluateCurrent(_owner._includeDetails);
                double combat = members.Sum(slot => CharacterPowerEvaluator.Evaluate(slot.Character, CombatSettings.AttackValue,
                    CombatSettings.DefenseValue, CombatSettings.CharacterStarValue, CombatSettings.IncludeTemporaryCharacterBonuses));
                int missing = Math.Max(0, Math.Min(Observation.Player.Level, board.Slots.Count) - members.Length);
                return new ChoiceBoardStrength((decimal)combat, members.Length, missing, synergy.ActivatableUnitCount,
                    synergy.EstablishedAttributeCount, synergy);
            }

            private void EvaluateRisk(StageChoiceValue row, bool hard)
            {
                var settings = Settings;
                row.Q = row.Board.Value(settings);
                row.RequiredStrength = settings.StageStrengthSlope * Observation.Stage.StageNumber +
                    settings.StageStrengthIntercept + (hard ? settings.HardAdditionalStrengthRequirement : 0m);
                row.StrengthDeficit = Math.Max(0m, row.RequiredStrength.Value - row.Q.Value);
                row.HpMultiplier = 1m + settings.MissingHpRiskWeight *
                    (Observation.Player.MaximumHP - Observation.Player.HP) / Observation.Player.MaximumHP;
                row.BaseRisk = hard ? settings.HardBaseRisk : settings.NormalBaseRisk;
                row.DeficitRisk = row.StrengthDeficit * (hard ? settings.HardDeficitWeight : settings.NormalDeficitWeight);
                row.Risk = (row.BaseRisk + row.DeficitRisk) * row.HpMultiplier;
            }

            private ChoiceValueFailure EvaluateRewards(StageChoiceValue row)
            {
                try
                {
                    var rules = Observation.EconomyRules;
                    if (Observation.Player.Gold < 0 || (rules != null && (rules.GoldPerInterestThreshold <= 0 ||
                        rules.InterestGoldPerThreshold < 0 || rules.MaximumInterestGold < 0)))
                        return ChoiceValueFailure.InvalidNumericInput;
                    if (row.GoldReward.HasValue && row.GoldReward.Value >= 0 && Observation.EconomyRules != null)
                        EvaluateGold(row);
                    if (row.ExperienceReward.HasValue && row.ExperienceReward.Value >= 0 && TryGrowth(row.ExperienceReward.Value, out var xp))
                    {
                        row.Experience = xp;
                        row.ExperienceValue = xp.TotalDelta;
                    }
                    if (row.GoldReward < 0 || row.ExperienceReward < 0) return ChoiceValueFailure.InvalidNumericInput;
                    if (!row.GoldReward.HasValue || !row.ExperienceReward.HasValue) return ChoiceValueFailure.MissingReward;
                    if (!row.GoldValue.HasValue) return ChoiceValueFailure.MissingEconomy;
                    return row.ExperienceValue.HasValue ? ChoiceValueFailure.None : ChoiceValueFailure.MissingGrowth;
                }
                catch (OverflowException)
                {
                    return ChoiceValueFailure.InvalidNumericInput;
                }
            }

            private void EvaluateGold(StageChoiceValue row)
            {
                int g = Observation.Player.Gold;
                int reward = row.GoldReward.Value;
                int after = checked(g + reward);
                row.AssumedPreRewardGold = g;
                row.GoldValue = Holding(after) - Holding(g);
                row.InterestValue = 0m;
                if (reward == 0) return;

                var rules = Observation.EconomyRules;
                int threshold = rules.GoldPerInterestThreshold;
                int remainder = g % threshold;
                int guaranteed = (int)EconomyRules.CalculateExpectedInterestGold(g - remainder, threshold,
                    rules.InterestGoldPerThreshold, rules.MaximumInterestGold);
                int extra = Math.Min(rules.InterestGoldPerThreshold, rules.MaximumInterestGold - guaranteed);
                decimal probability = extra == 0 ? 0m : (decimal)remainder / threshold;
                decimal baseHolding = Holding(after);
                decimal low = Holding(checked(after + guaranteed)) - baseHolding;
                if (probability == 0m)
                    row.InterestOutcomes = Array.AsReadOnly(new[] { (guaranteed, 1m, low) });
                else
                {
                    decimal high = Holding(checked(after + guaranteed + extra)) - baseHolding;
                    row.InterestOutcomes = Array.AsReadOnly(new[] { (guaranteed, 1m - probability, low),
                        (guaranteed + extra, probability, high) });
                }
                row.InterestValue = row.InterestOutcomes.Sum(branch => branch.Probability * branch.HoldingDelta);
            }

            private ChoiceValueFailure EvaluateDiscount(StageChoiceValue row, int discountPrice)
            {
                try
                {
                    var rules = Observation.EconomyRules;
                    int? normal = _owner._normalPrice ?? rules?.ExperiencePurchaseGoldCost;
                    if (rules == null || !normal.HasValue) return ChoiceValueFailure.MissingEconomy;
                    if (normal.Value < 0 || discountPrice < 0 || discountPrice > normal.Value || rules.ExperiencePerPurchase <= 0)
                        return ChoiceValueFailure.InvalidNumericInput;
                    if (!TryGrowth(rules.ExperiencePerPurchase, out var growth)) return ChoiceValueFailure.MissingGrowth;
                    row.PurchaseExperience = growth;
                    row.DiscountPrice = Buy(discountPrice, growth.TotalDelta);
                    row.NormalPrice = Buy(normal.Value, growth.TotalDelta);
                    row.DiscountValue = row.DiscountPrice.Value.NetValue - row.NormalPrice.Value.NetValue;
                    return ChoiceValueFailure.None;
                }
                catch (OverflowException)
                {
                    return ChoiceValueFailure.InvalidNumericInput;
                }
            }

            private (int Price, bool CanBuy, decimal NetValue) Buy(int price, decimal growth)
            {
                bool affordable = Observation.Player.Gold >= price && Observation.Player.Level < Observation.Player.MaximumLevel;
                return (price, affordable, affordable
                    ? Math.Max(0m, growth + Holding(Observation.Player.Gold - price) - Holding(Observation.Player.Gold)) : 0m);
            }

            private decimal Holding(int gold)
            {
                if (_holdingValues.TryGetValue(gold, out var value)) return value;
                _economy ??= new EconomyValueEvaluator(Settings.Economy,
                    new EconomyInterestContext(1, 0, 0, InterestRewardEligibility.Ineligible));
                value = _economy.Evaluate(gold).HoldingValue;
                _holdingValues.Add(gold, value);
                return value;
            }

            private bool TryGrowth(int xp, out GrowthValueComparison value)
            {
                value = default;
                if (Observation.EconomyRules?.ExperienceCurve == null) return false;
                if (_growthValues.TryGetValue(xp, out value)) return true;
                try
                {
                    _growth ??= GrowthValueEvaluator.CreateBaseline(Settings.Growth, Observation);
                    value = _growth.CompareExperienceGain(xp);
                    _growthValues.Add(xp, value);
                    return true;
                }
                catch (ArgumentException) { return false; }
            }

            private static bool Supported(StageChoiceObservation detail)
            {
                var noEffect = detail.SelectionEffect == StageSelectionEffectKind.None;
                bool noReward = detail.PostBattleGold.GetValueOrDefault() == 0 && detail.PostBattleXP.GetValueOrDefault() == 0;
                switch (detail.Choice)
                {
                    case StageChoice.SelectA:
                        return detail.EnemyCategory == StageEnemyCategory.Normal && detail.ShopMode == StageShopMode.Character && noEffect;
                    case StageChoice.SelectC:
                        return detail.EnemyCategory == StageEnemyCategory.Hard && detail.ShopMode == StageShopMode.Character && noEffect;
                    case StageChoice.SelectB:
                        return detail.EnemyCategory == StageEnemyCategory.None && detail.ShopMode == StageShopMode.Item && noEffect && noReward;
                    case StageChoice.SelectD:
                        return detail.EnemyCategory == StageEnemyCategory.None && detail.ShopMode == StageShopMode.Character && noReward &&
                            detail.SelectionEffect == StageSelectionEffectKind.HealPlayer &&
                            detail.SelectionEffectTiming == StageSelectionEffectTiming.AtSelection &&
                            detail.SelectionEffectCapsAtMaximum && detail.SelectionEffectAmount >= 0;
                    default:
                        return false;
                }
            }

            private static StageChoiceValue Failed(StageChoiceValue row, ChoiceValueFailure failure)
            {
                row.Failure = failure;
                row.Status = Classify(failure);
                return row;
            }
        }

        internal static ChoiceValueStatus Classify(ChoiceValueFailure failure)
        {
            switch (failure)
            {
                case ChoiceValueFailure.None: return ChoiceValueStatus.Evaluated;
                case ChoiceValueFailure.OutsideChoicePhase:
                case ChoiceValueFailure.NoLegalChoices: return ChoiceValueStatus.NotApplicable;
                case ChoiceValueFailure.BossOverride:
                case ChoiceValueFailure.UnsupportedDefinition: return ChoiceValueStatus.UnsupportedChoice;
                case ChoiceValueFailure.MissingEncounterCount:
                case ChoiceValueFailure.MissingEconomy:
                case ChoiceValueFailure.MissingGrowth:
                case ChoiceValueFailure.MissingReward:
                case ChoiceValueFailure.MissingHealth: return ChoiceValueStatus.MissingPublicInput;
                default: return ChoiceValueStatus.InvalidPublicState;
            }
        }
    }
}
