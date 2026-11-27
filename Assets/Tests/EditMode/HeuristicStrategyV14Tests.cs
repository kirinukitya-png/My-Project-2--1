using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using NUnit.Framework;
using ProjectNST.GameCore.Actions;
using ProjectNST.GameCore.Battle;
using ProjectNST.GameCore.Board;
using ProjectNST.GameCore.Characters;
using ProjectNST.GameCore.Economy;
using ProjectNST.GameCore.Enemies;
using ProjectNST.GameCore.Items;
using ProjectNST.GameCore.Observation;
using ProjectNST.GameCore.Shop;
using ProjectNST.GameCore.Stages;
using ProjectNST.GameCore.Strategy;

namespace ProjectNST.GameCore.Tests
{
    public sealed partial class HeuristicStrategyTests
    {
        private static GameObservation V14Observation(int gold = 20, int level = 4, int xp = 0,
            StageChoice choice = StageChoice.SelectA, BoardState board = null, HandState hand = null,
            CharacterDefinition[] defs = null, CharacterInstance[] owned = null, ShopOffer[] offers = null,
            CharacterStatDefinition stats = null, ItemDefinition[] items = null, int gain = 1, int[] curve = null,
            int[] quotas = null, int[] weights = null, int rerollCost = 1, EnemyDefinition battleEnemy = null,
            BattleTargetDefinition[] battleTargets = null, int enemyCount = 1)
        {
            defs ??= Array.Empty<CharacterDefinition>();
            var cfg = Config(1);
            var initial = StageProgressRules.BeginCurrentEncounter(StageProgressRules.CreateInitialState(cfg), cfg).NextState;
            var stage = StageSelectionRules.Select(initial, cfg, choice).NextState;
            curve ??= Enumerable.Range(1, 11).ToArray();
            var economy = new EconomyRulesConfiguration(4, 2, gain, curve, 10, 1, 3, 3, 1, 0);
            var shop = new ShopState(choice == StageChoice.SelectB ? ShopKind.Item : ShopKind.Character,
                offers ?? Array.Empty<ShopOffer>(), 3, 1, 1);
            var config = new CharacterShopConfiguration(quotas ?? new[] { defs.Length, 0, 0, 0, 0 },
                new[] { 15, 15, 15, 15, 15 }, new[] { weights ?? new[] { 1, 0, 0, 0, 0 } }, new[] { 3 });
            var enemies = EnemyBoardState.CreateEmpty();
            for (int slot = 0; battleEnemy != null && slot < enemyCount; slot++)
            {
                Assert.That(EnemyInstanceFactory.TryCreate(battleEnemy, stage.EncounterIndex, slot, 0, 0, 0, 0, 0, 0,
                    out var foe, out _), Is.True);
                enemies = enemies.TryPlace(foe).NextState;
            }
            var source = new ObservationSource(stage, cfg, new EconomyState(gold, xp, level, curve.Length + 1, gold, xp, level, 30, 30),
                economy, board ?? BoardState.CreateStandard(), hand ?? HandState.CreateStandard(),
                new CharacterOwnershipState(owned ?? Array.Empty<CharacterInstance>()), defs, defs, defs,
                items ?? Array.Empty<ItemDefinition>(), shop, new ItemEffectState(),
                enemyDefinitions: battleEnemy == null ? Array.Empty<EnemyDefinition>() : new[] { battleEnemy },
                enemyBoard: enemies, characterStatSettings: stats, characterShopConfig: config,
                stageRewardConfig: new StageRewardConfiguration(5, 2), shopRerollGoldCost: rerollCost,
                battleTargetDefinitions: battleTargets ?? (battleEnemy == null ? null : new[] { EnemyTargetProbabilityEvaluatorTests.Definition(1) }));
            var result = ObservationBuilder.Build(source);
            Assert.That(result.Succeeded, Is.True, string.Join(";", result.Errors));
            return result.Observation;
        }

        private static HeuristicActionEvaluation V14Candidate<T>(GameObservation o, HeuristicStrategy strategy = null)
            where T : GameAction => (strategy ?? HeuristicStrategy.CreateV14()).GetEvaluations(o, new ActionId("v14-eval"))
                .First(r => r.Action is T);

        [TestCase(4, 0, 1, StageChoice.SelectA, -.18)]
        [TestCase(4, 3, 1, StageChoice.SelectA, 5.82)]
        [TestCase(8, 7, 1, StageChoice.SelectA, 5.07)]
        [TestCase(9, 8, 1, StageChoice.SelectA, -1.68)]
        [TestCase(4, 3, 1, StageChoice.SelectB, 6.80)]
        [TestCase(3, 2, 6, StageChoice.SelectA, 19.52)]
        public void V14ExperienceUsesPublicCurveGrowthAndFunds(int level, int xp, int gain, StageChoice choice, double expected)
        {
            var evaluation = V14Candidate<BuyExperienceAction>(V14Observation(level: level, xp: xp, gain: gain, choice: choice));
            Assert.That(evaluation.Score, Is.EqualTo(expected).Within(1e-10));
            Assert.That(evaluation.Reason, Does.Contain("growth=").And.Contain("holding="));
        }

        [TestCase(20, 17, .5, 2.74)]
        [TestCase(20, 17, 3, 22.74)]
        [TestCase(20, 17, 5, 38.74)]
        [TestCase(5, 8, -.2, .71)]
        [TestCase(40, 49, -.2, 1.55)]
        [TestCase(40, 43, 0, 1.05)]
        public void V14WorkedGoldAndFormationExamples(int gold, int after, double composition, double expected)
        {
            var evaluator = new EconomyValueEvaluator(new HeuristicV14Settings().Economy,
                new EconomyInterestContext(10, 1, 3, InterestRewardEligibility.Eligible));
            var funds = evaluator.Compare(gold, after).TotalDelta;
            Assert.That(8m * (decimal)composition + funds, Is.EqualTo((decimal)expected));
            Assert.That(evaluator.Compare(after, gold).TotalDelta, Is.EqualTo(-funds));
        }

        [TestCase(false, .99, false)]
        [TestCase(false, 1, false)]
        [TestCase(false, 1.01, true)]
        [TestCase(true, 1.99, false)]
        [TestCase(true, 2, false)]
        [TestCase(true, 2.01, true)]
        public void V14PreparationRequiresStrictlyPositiveMarginAboveThreshold(bool enemyless, double score, bool continuePreparation)
        {
            var o = V14Observation(level: 1, choice: enemyless ? StageChoice.SelectD : StageChoice.SelectA);
            var strategy = HeuristicStrategy.CreateV14(new HeuristicV14Settings(
                growth: new GrowthValueSettings((decimal)score + (enemyless ? 1.40m : 1.68m), .5m, 0)));
            var decision = strategy.ChooseAction(o, Context(strategy));
            if (continuePreparation) Assert.That(decision.Action, Is.TypeOf<BuyExperienceAction>());
            else if (enemyless) Assert.That(decision.HasAction, Is.False);
            else Assert.That(decision.Action, Is.TypeOf<StartBattleAction>());
        }

        [TestCase(false, .50, -.425)]
        [TestCase(false, .70, 1.973)]
        [TestCase(false, 1, 5.57)]
        [TestCase(true, .70, 2.45)]
        public void V14RerollFormulaAndRequiredBenefit(bool enemyless, double discount, double expected)
        {
            var settings = new HeuristicV14Settings(futureDiscount: (decimal)discount);
            decimal result = enemyless ? ShopOpportunityMath.Score(4, 0, -.35m, settings.Shop.FutureDiscount) :
                ShopOpportunityMath.Score(11.99m, 6m, -.42m, settings.Shop.FutureDiscount);
            Assert.That(result, Is.EqualTo((decimal)expected));
            Assert.That(result > settings.RequiredBenefit(enemyless, true), Is.EqualTo(!enemyless && discount == 1));
        }

        [Test]
        public void V14EmptyBoardPrefersSmallPositiveIncreaseAndSkipsForecast()
        {
            var d = Def("d");
            var shop = new[] { new ShopOffer("a", ShopOfferKind.Character, "d", 1, 1) };
            var o = V14Observation(level: 1, defs: new[] { d }, offers: shop,
                stats: Stats(new CharacterStatOption(1, 0, 1)));
            string trace = null;
            var strategy = HeuristicStrategy.CreateV14(new HeuristicV14Settings(compositionWeight: .70m),
                decisionDiagnosticSink: x => trace = x);
            var decision = strategy.ChooseAction(o, Context(strategy));
            Assert.That(decision.Action, Is.TypeOf<BuyCharacterAction>());
            Assert.That(((BuyCharacterAction)decision.Action).Placement.Kind, Is.EqualTo(CharacterLocationKind.Board));
            Assert.That(trace, Does.Contain("EmptyBoardPriority").And.Not.Contain("shopStatus="));
            var inspect = V14Candidate<BuyCharacterAction>(o, strategy);
            Assert.That(inspect.Score, Is.InRange(.01, 1.0));
        }

        [TestCase(7, 3, false, false, false)]
        [TestCase(7, 3, true, false, true)]
        [TestCase(7, 0, true, false, false)]
        [TestCase(7, 3, true, true, false)]
        [TestCase(8, 3, false, false, true)]
        public void V14LowGoldXpRequiresNewReachableHandAddition(int gold, int xp, bool hasHand, bool existingSpace, bool allowed)
        {
            var ds = Enumerable.Range(0, 5).Select(i => Def("d" + i)).ToArray();
            var board = BoardState.CreateStandard();
            var hand = HandState.CreateStandard();
            int occupied = existingSpace ? 3 : 4;
            var owned = new List<CharacterInstance>();
            for (int i = 0; i < occupied; i++)
            {
                owned.Add(Inst("b" + i, ds[i]));
                board = Put(board, i, "b" + i);
            }
            if (hasHand)
            {
                owned.Add(Inst("h", ds[4]));
                hand = Put(hand, 0, HandSlotContent.Character("h"));
            }
            var o = V14Observation(gold, 4, xp, board: board, hand: hand, defs: ds, owned: owned.ToArray());
            Assert.That(V14Candidate<BuyExperienceAction>(o).IsSelectable, Is.EqualTo(allowed));
        }

        [TestCase(3, true)]
        [TestCase(4, false)]
        public void V14LowGoldXpUsesPurchaseFundsAfterXp(int offerCost, bool passes)
        {
            var ds = Enumerable.Range(0, 5).Select(i => Def("d" + i)).ToArray();
            var board = BoardState.CreateStandard();
            for (int i = 0; i < 4; i++) board = Put(board, i, "b" + i);
            var o = V14Observation(7, 4, 3, board: board, defs: ds, owned: ds.Take(4).Select((d, i) => Inst("b" + i, d)).ToArray(),
                offers: new[] { new ShopOffer("a", ShopOfferKind.Character, "d4", offerCost, offerCost) },
                stats: new CharacterStatDefinition(new[] { new CharacterCostStats(offerCost,
                    new[] { new CharacterStatOption(2, 1, 1) }, Array.Empty<CharacterStatOption>(), Array.Empty<CharacterStatOption>()) }));
            Assert.That(V14Candidate<BuyExperienceAction>(o).IsSelectable, Is.EqualTo(passes));
        }

        [TestCase(11, false)]
        [TestCase(12, true)]
        public void V14RerollGoldGateAndWholeShopScore(int gold, bool allowed)
        {
            var d = Def("d");
            var o = V14Observation(gold: gold, defs: new[] { d }, stats: Stats(new CharacterStatOption(1, 0, 1)));
            var candidate = V14Candidate<RerollShopAction>(o);
            Assert.That(candidate.IsSelectable, Is.EqualTo(allowed));
            if (allowed)
            {
                var direct = new ShopOpportunityEvaluator(new HeuristicV14Settings().Shop).Evaluate(o);
                Assert.That(candidate.Score, Is.EqualTo((double)direct.Score.Value));
            }
        }

        [Test]
        public void V14LeavesUnknownCharacterOfferUnscoredAndClosesCombat()
        {
            var d = Def("d");
            var o = V14Observation(gold: 12, level: 9, defs: new[] { d }, offers: new[] { OfferV14("a", "d") });
            var strategy = HeuristicStrategy.CreateV14();
            Assert.That(V14Candidate<BuyCharacterAction>(o, strategy).IsSelectable, Is.False);
            Assert.That(V14Candidate<RerollShopAction>(o, strategy).IsSelectable, Is.False);
            Assert.That(strategy.ChooseAction(o, Context(strategy)).Action, Is.TypeOf<StartBattleAction>());
        }

        [Test]
        public void V14OverLevelRetreatPrecedesExperienceAndRetainsUpperLimitRepair()
        {
            var a = Def("a"); var b = Def("b");
            var board = Put(Put(BoardState.CreateStandard(), 0, "a"), 1, "b");
            var o = V14Observation(4, 1, board: board, defs: new[] { a, b }, owned: new[] { Inst("a", a), Inst("b", b) });
            var strategy = HeuristicStrategy.CreateV14();
            var nearLimit = new StrategyState(strategy.StrategyId, strategy.Version, 2, "hs2|29|0|2|");
            var actual = strategy.ChooseAction(o, Context(strategy, state: nearLimit));
            Assert.That(actual.Action, Is.TypeOf<MoveBoardToHandAction>());
            Assert.That(actual.Diagnostic, Does.Contain("LevelRepair"));
        }

        [Test]
        public void V14ItemShopAndPhaseSeparation()
        {
            var item = new ItemDefinition("item", "Item", 2, "", ItemEffectTarget.Player,
                ItemEffectDuration.Immediate, new[] { new ItemEffectDefinition(ItemEffectKind.Block, 1) });
            var o = V14Observation(choice: StageChoice.SelectB, level: 9, items: new[] { item },
                offers: new[] { new ShopOffer("item-offer", ShopOfferKind.Item, "item", 2, null) });
            var strategy = HeuristicStrategy.CreateV14();
            Assert.That(V14Candidate<BuyItemAction>(o, strategy).IsSelectable, Is.False);
            Assert.That(V14Candidate<RerollShopAction>(o, strategy).IsSelectable, Is.False);
            Assert.That(strategy.ChooseAction(o, Context(strategy)).HasAction, Is.False);
            var choice = Build(Choice());
            var old = HeuristicStrategy.CreateV13();
            Assert.That(strategy.ChooseAction(choice, Context(strategy)).Action.Kind,
                Is.EqualTo(old.ChooseAction(choice, Context(old)).Action.Kind));
        }

        [Test]
        public void V14IdentityFingerprintAndDiagnosticsAreDeterministic()
        {
            var strategy = HeuristicStrategy.CreateV14();
            string trace = null;
            var verbose = HeuristicStrategy.CreateV14(decisionDiagnosticSink: t => trace = t);
            var o = V14Observation(level: 4, xp: 3);
            var a = strategy.ChooseAction(o, Context(strategy));
            var b = verbose.ChooseAction(o, Context(verbose));
            Assert.That(a.Action.Kind, Is.EqualTo(b.Action.Kind));
            Assert.That(a.NextState.Payload, Is.EqualTo(b.NextState.Payload));
            Assert.That(a.NextState.SchemaVersion, Is.EqualTo(2));
            Assert.That(a.StrategyRngAdvanced || b.StrategyRngAdvanced, Is.False);
            Assert.That(trace, Does.Contain("VisibleObservationFingerprint=visible-v14-").And.Contain("xp=3->0"));
            Assert.That(HeuristicStrategy.GetV14ObservationFingerprint(o), Is.EqualTo(HeuristicStrategy.GetV14ObservationFingerprint(o)));
            Assert.That(HeuristicStrategy.GetV14ObservationFingerprint(o), Is.Not.EqualTo(HeuristicStrategy.GetV14ObservationFingerprint(
                V14Observation(level: 4, xp: 3, gain: 2))));
            string id = HeuristicStrategy.GetV14ConfigurationIdentity();
            Assert.That(id, Is.Not.EqualTo(HeuristicStrategy.GetV14ConfigurationIdentity(new HeuristicV14Settings(futureDiscount: .5m))));
            var previous = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("fr-FR");
                Assert.That(HeuristicStrategy.GetV14ConfigurationIdentity(), Is.EqualTo(id));
            }
            finally { CultureInfo.CurrentCulture = previous; }
            Assert.Throws<ArgumentException>(() => new HeuristicV14Settings(growth: new GrowthValueSettings(12, .5m, 4)));
        }

        [TestCase(false, 2.00, false)]
        [TestCase(false, 2.01, true)]
        [TestCase(true, 4.00, false)]
        [TestCase(true, 4.01, true)]
        public void V14RerollBoundaryIsAppliedInSelection(bool enemyless, double desired, bool rerolls)
        {
            var a = Def("a"); var b = Def("b");
            var observation = V14Observation(level: 9, choice: enemyless ? StageChoice.SelectD : StageChoice.SelectA,
                board: Put(BoardState.CreateStandard(), 0, "a"), owned: new[] { Inst("a", a) },
                defs: new[] { a, b }, stats: Stats(new CharacterStatOption(2, 1, 1)));
            var baseSettings = new HeuristicV14Settings(futureDiscount: 1m);
            var direct = new ShopOpportunityEvaluator(baseSettings.Shop).Evaluate(observation);
            decimal discount = ((decimal)desired - direct.RerollEconomy.Value.TotalDelta) / direct.ExpectedFutureOpportunity.Value;
            if (!rerolls && ShopOpportunityMath.Score(direct.ExpectedFutureOpportunity.Value, 0,
                direct.RerollEconomy.Value.TotalDelta, discount) > (decimal)desired)
                discount -= .00000000000000000001m;
            var strategy = HeuristicStrategy.CreateV14(new HeuristicV14Settings(futureDiscount: discount));
            var decision = strategy.ChooseAction(observation, Context(strategy));
            Assert.That(decision.Action is RerollShopAction, Is.EqualTo(rerolls));
        }

        [Test]
        public void V14OppositePlacementScoresCancelAndCarryNoDeploymentBonus()
        {
            var a = Def("a"); var b = Def("b");
            var board = Put(BoardState.CreateStandard(), 0, "a");
            var hand = Put(HandState.CreateStandard(), 0, HandSlotContent.Character("b"));
            var roster = new[] { a, b }; var owned = new[] { Inst("a", a), Inst("b", b) };
            var before = V14Observation(gold: 0, board: board, hand: hand, defs: roster, owned: owned);
            var after = V14Observation(gold: 0, board: Put(board, 1, "b"), defs: roster, owned: owned);
            var strategy = HeuristicStrategy.CreateV14();
            var deploy = strategy.GetEvaluations(before, new ActionId("v14-deploy")).Single(x =>
                x.Action is MoveHandToBoardAction move && move.HandSlot == 0 && move.BoardSlot == 1);
            var retreat = strategy.GetEvaluations(after, new ActionId("v14-retreat")).Single(x =>
                x.Action is MoveBoardToHandAction move && move.HandSlot == 0 && move.BoardSlot == 1);
            Assert.That(deploy.Score + retreat.Score, Is.EqualTo(0).Within(1e-10));
            Assert.That(deploy.Score, Is.LessThan(100));
        }

        [TestCase(false, 3.5, true)]
        [TestCase(true, 3.5, false)]
        public void V14QualifyingActionsUseRawScoresAfterTypeSpecificGates(bool enemyless, double rerollScore, bool choosesReroll)
        {
            var a = Def("a"); var b = Def("b");
            var o = V14Observation(level: 4, xp: 3, choice: enemyless ? StageChoice.SelectD : StageChoice.SelectA,
                board: Put(BoardState.CreateStandard(), 0, "a"), owned: new[] { Inst("a", a) },
                defs: new[] { a, b }, stats: Stats(new CharacterStatOption(2, 1, 1)));
            var direct = new ShopOpportunityEvaluator(new HeuristicV14Settings().Shop).Evaluate(o);
            decimal discount = ((decimal)rerollScore - direct.RerollEconomy.Value.TotalDelta) / direct.ExpectedFutureOpportunity.Value;
            var growth = new GrowthValueSettings((3m + (enemyless ? 1.40m : 1.68m)) / .625m, .5m, 0);
            var strategy = HeuristicStrategy.CreateV14(new HeuristicV14Settings(growth: growth, futureDiscount: discount));
            Assert.That(V14Candidate<BuyExperienceAction>(o, strategy).Score, Is.EqualTo(3).Within(1e-10));
            var selected = strategy.ChooseAction(o, Context(strategy)).Action;
            Assert.That(selected.Kind, Is.EqualTo(choosesReroll ? GameActionKind.RerollShop : GameActionKind.BuyExperience));
        }

        [TestCase(5, false, true)]
        [TestCase(5, true, false)]
        [TestCase(4, false, false)]
        public void V14ItemSaleRequiresFullHandWithoutPositiveUse(int occupied, bool useful, bool sale)
        {
            var effect = new ItemEffectDefinition(useful ? ItemEffectKind.Block : ItemEffectKind.Star, useful ? 1 : 2);
            var item = new ItemDefinition("item", "Item", 2, "", useful ? ItemEffectTarget.Player : ItemEffectTarget.OneAlly,
                ItemEffectDuration.Immediate, new[] { effect });
            var hand = HandState.CreateStandard();
            for (int i = 0; i < occupied; i++) hand = Put(hand, i, HandSlotContent.Item("item"));
            var o = V14Observation(gold: 0, level: 9, hand: hand, items: new[] { item });
            var evaluation = V14Candidate<SellItemAction>(o);
            Assert.That(evaluation.IsSelectable, Is.EqualTo(sale));
            if (sale) Assert.That(evaluation.Score, Is.EqualTo(9));
            if (useful) Assert.That(V14Candidate<UseHandItemAction>(o).Score, Is.EqualTo(14));
        }

        [TestCase(true, true)]
        [TestCase(false, false)]
        public void V14OverLevelWithoutHandSpaceUsesLegacyExperienceThenSale(bool canLevelNow, bool buysXp)
        {
            var a = Def("a"); var b = Def("b");
            var item = new ItemDefinition("item", "Item", 0, "", ItemEffectTarget.Player,
                ItemEffectDuration.Immediate, new[] { new ItemEffectDefinition(ItemEffectKind.Block, 1) });
            var hand = HandState.CreateStandard();
            for (int i = 0; i < 5; i++) hand = Put(hand, i, HandSlotContent.Item("item"));
            var board = Put(Put(BoardState.CreateStandard(), 0, "a"), 1, "b");
            var o = V14Observation(canLevelNow ? 4 : 3, 1, board: board, hand: hand,
                defs: new[] { a, b }, owned: new[] { Inst("a", a), Inst("b", b) }, items: new[] { item });
            var strategy = HeuristicStrategy.CreateV14();
            var decision = strategy.ChooseAction(o, Context(strategy));
            Assert.That(decision.Action.Kind, Is.EqualTo(buysXp ? GameActionKind.BuyExperience : GameActionKind.SellCharacter));
            Assert.That(decision.Diagnostic, Does.Contain("LevelRepair"));
        }

        [Test]
        public void V14RerollIsDeferredAtPreparationLimitAndFilteredImmediatelyAfterReroll()
        {
            var a = Def("a");
            var o = V14Observation(level: 9, defs: new[] { a });
            string trace = null;
            var strategy = HeuristicStrategy.CreateV14(decisionDiagnosticSink: s => trace = s);
            var previousReroll = new StrategyState(strategy.StrategyId, strategy.Version, 2, "hs2|1|0|2|NDox");
            var limited = new StrategyState(strategy.StrategyId, strategy.Version, 2, "hs2|29|0|2|");
            Assert.That(strategy.ChooseAction(o, Context(strategy, state: previousReroll)).Action.Kind, Is.EqualTo(GameActionKind.StartBattle));
            Assert.That(trace, Does.Contain("RepeatedOrReverse").And.Not.Contain("shopStatus="));
            Assert.That(strategy.ChooseAction(o, Context(strategy, state: limited)).Action.Kind, Is.EqualTo(GameActionKind.StartBattle));
            Assert.That(trace, Does.Contain("Preparation limit").And.Not.Contain("shopStatus="));
        }

        private static ShopOffer OfferV14(string id, string definition) =>
            new ShopOffer(id, ShopOfferKind.Character, definition, 1, 1);
    }
}
