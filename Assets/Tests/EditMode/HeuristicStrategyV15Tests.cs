using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using NUnit.Framework;
using ProjectNST.GameCore.Actions;
using ProjectNST.GameCore.Board;
using ProjectNST.GameCore.Characters;
using ProjectNST.GameCore.Economy;
using ProjectNST.GameCore.Enemies;
using ProjectNST.GameCore.Items;
using ProjectNST.GameCore.Observation;
using ProjectNST.GameCore.Randomness;
using ProjectNST.GameCore.Sessions;
using ProjectNST.GameCore.Shop;
using ProjectNST.GameCore.Stages;
using ProjectNST.GameCore.Strategy;
using ProjectNST.GameCore.Synergy;
using ProjectNST.SimulationExport;

namespace ProjectNST.GameCore.Tests
{
    public sealed partial class GameSessionTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public void V15CheckpointTraceRerunAndReplay(bool optionEnabled)
        {
            var enemy = new EnemyDefinition("v15-replay-enemy", "Replay enemy", 0, 0, 1,
                null, null, null, "T001", null, null, null);
            var session = ConfigurationWithStage(OneEncounterConfig(), enemy: enemy);
            var settings = optionEnabled ? new HeuristicV15Settings() :
                new HeuristicV15Settings(new IntegratedCompositionSettings(handOption: new HandSynergyOptionSettings(0)));
            var config = new SimulationRunnerConfiguration(session, 1, 3, 200, recordExecutionHistory: true,
                checkpointConfigurationId: HeuristicStrategy.GetV15ConfigurationIdentity(settings));
            var runner = new SimulationRunner(config);
            var original = runner.RunTrial(35, 88235, HeuristicStrategy.CreateV15(settings));
            var checkpoint = runner.CreateCheckpoint(original);
            Assert.That(checkpoint.SchemaVersion, Is.EqualTo(2));
            var expanded = new SimulationRunner(new SimulationRunnerConfiguration(session, 10, 3, 200,
                checkpointConfigurationId: HeuristicStrategy.GetV15ConfigurationIdentity(settings)));
            Assert.That(expanded.RestoreTrial(checkpoint, HeuristicStrategy.CreateV15(settings)).CurrentState.RandomState,
                Is.EqualTo(checkpoint.GameRandomState));
            var traced = HeuristicStrategy.CreateV15(settings);
            int traces = 0;
            traced.ConfigureDecisionTrace(_ => traces++, (_, error) => Assert.Fail(error.ToString()));
            traced.ConfigureV15TraceDetails(true);
            var rerun = runner.RunTrial(35, 88235, traced);
            Assert.That(traces, Is.GreaterThan(0));
            Assert.That(rerun.ExecutionHistory.Select(x => (x.Kind, x.AfterFingerprint)),
                Is.EqualTo(original.ExecutionHistory.Select(x => (x.Kind, x.AfterFingerprint))));
            Assert.That(new SimulationReplayVerifier().Verify(config, original, original.ExecutionHistory).Succeeded, Is.True);

            var alternate = optionEnabled
                ? new HeuristicV15Settings(new IntegratedCompositionSettings(handOption: new HandSynergyOptionSettings(0)))
                : new HeuristicV15Settings();
            var alternateRunner = new SimulationRunner(new SimulationRunnerConfiguration(session, 10, 3, 200,
                checkpointConfigurationId: HeuristicStrategy.GetV15ConfigurationIdentity(alternate)));
            Assert.Throws<ArgumentException>(() => alternateRunner.RestoreTrial(checkpoint, HeuristicStrategy.CreateV15(alternate)));
        }

        [Test]
        public void V15DecisionDoesNotChangeGameCheckpointOrRandomStreams()
        {
            var session = Create(seed: 1501);
            var observed = session.GetObservation();
            Assert.That(observed.Succeeded, Is.True);
            Assert.That(observed.Observation.Stage.Phase, Is.EqualTo(ObservationPhase.Preparation));
            var fingerprint = SimulationStateFingerprint.Create(session.CurrentState);
            var streams = session.CurrentState.RandomState;
            var strategy = HeuristicStrategy.CreateV15();
            var state = StrategyState.EmptyFor(strategy.StrategyId, strategy.Version);
            var rng = StrategyRandomSource.Create(1502, strategy.StrategyId, strategy.Version).CaptureState();
            var context = new StrategyContext(strategy.StrategyId, strategy.Version, 0, 0,
                new ActionId("decision-only"), state, rng);
            var next = strategy.ChooseAction(observed.Observation, context);
            Assert.That(next.NextStrategyRngState, Is.EqualTo(rng));
            Assert.That(session.CurrentState.RandomState, Is.EqualTo(streams));
            Assert.That(SimulationStateFingerprint.Create(session.CurrentState), Is.EqualTo(fingerprint));
        }
    }

    public sealed partial class HeuristicStrategyTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public void V15TraceUsesScoredValuesAndPreservesDecision(bool details)
        {
            var obs = V15OptionShop(3, true);
            var strategy = HeuristicStrategy.CreateV15();
            var context = Context(strategy);
            var expected = strategy.ChooseAction(obs, context);
            string trace = null;
            strategy.ConfigureV15TraceDetails(details);
            strategy.ConfigureDecisionTrace(value => trace = value, (_, error) => Assert.Fail(error.ToString()));
            var actual = strategy.ChooseAction(obs, context);

            Assert.That(SimulationStateFingerprint.Create(actual.Action),
                Is.EqualTo(SimulationStateFingerprint.Create(expected.Action)));
            Assert.That(actual.NextState.Payload, Is.EqualTo(expected.NextState.Payload));
            Assert.That(actual.NextStrategyRngState, Is.EqualTo(expected.NextStrategyRngState));
            Assert.That(trace, Does.Contain("TraceFormat=v15-1").And.Contain("  Before total=")
                .And.Contain("  After total=").And.Contain("  Delta total=").And.Contain("ShopBest ")
                .And.Contain("Shop current=6.85 future=5.1625").And.Contain("rerollScore=-3.58625")
                .And.Contain("synergyReuse=").And.Contain("mergePlanReuse="));
            Assert.That(trace.Contains("Detail Current staticRound=True"), Is.EqualTo(details));
            Assert.That(trace.Contains("FutureOffer "), Is.EqualTo(details));
        }

        [Test]
        public void V15TraceSeparatesDisabledOptionAndUnevaluatedRerollFromNumericZero()
        {
            var settings = new HeuristicV15Settings(new IntegratedCompositionSettings(
                handOption: new HandSynergyOptionSettings(0)));
            var strategy = HeuristicStrategy.CreateV15(settings);
            string trace = null;
            strategy.ConfigureDecisionTrace(value => trace = value, (_, error) => Assert.Fail(error.ToString()));
            strategy.ConfigureV15TraceDetails(true);
            strategy.ChooseAction(V15OptionShop(3, true), Context(strategy));
            Assert.That(trace, Does.Contain("Option status=Disabled value=0")
                .And.Contain("legal=Unavailable").And.Contain("optionSearches=0"));

            strategy.ChooseAction(V14Observation(gold: 11, level: 9), Context(strategy));
            Assert.That(trace, Does.Contain("outcome=LowGoldReroll")
                .And.Contain("CurrentComposition Unavailable").And.Contain("Shop current=Unavailable future=Unavailable"));
        }

        [Test]
        public void V15TraceSinkFailureDoesNotChangeDecision()
        {
            var obs = V15OptionShop(3, true);
            var strategy = HeuristicStrategy.CreateV15();
            var context = Context(strategy);
            var expected = strategy.ChooseAction(obs, context);
            strategy.ConfigureV15TraceDetails(true);
            var capture = new DecisionTraceCapture(System.IO.Path.GetTempPath(), strategy.StrategyId, strategy.Version,
                0, 12345, true, true, true, (_, _) => throw new System.IO.IOException("diagnostic write"));
            capture.Register(strategy.ConfigureDecisionTrace);
            var actual = strategy.ChooseAction(obs, context);
            capture.Complete(1);

            Assert.That(capture.Generated, Is.EqualTo(1));
            Assert.That(capture.WriteFailures, Is.EqualTo(1));
            Assert.That(capture.FirstError, Does.Contain("diagnostic write"));
            Assert.That(SimulationStateFingerprint.Create(actual.Action),
                Is.EqualTo(SimulationStateFingerprint.Create(expected.Action)));
            Assert.That(actual.NextState.Payload, Is.EqualTo(expected.NextState.Payload));
            Assert.That(actual.NextStrategyRngState, Is.EqualTo(expected.NextStrategyRngState));
        }

        [Test]
        public void V14TraceKeepsItsExistingFormat()
        {
            var strategy = HeuristicStrategy.CreateV14();
            string trace = null;
            strategy.ConfigureDecisionTrace(value => trace = value, (_, error) => Assert.Fail(error.ToString()));
            strategy.ChooseAction(V15OptionShop(3, true), Context(strategy));

            Assert.That(trace, Does.Contain("VisibleObservationFingerprint=visible-v14-")
                .And.Not.Contain("TraceFormat=v15-1"));
        }

        private static HeuristicV15CandidateEvaluation V15Row<T>(GameObservation observation,
            Func<T, bool> match = null, HeuristicStrategy strategy = null, bool details = false) where T : GameAction =>
            (strategy ?? HeuristicStrategy.CreateV15()).InspectV15Preparation(observation, new ActionId("v15"),
                includeDetails: details).First(r => r.Action is T action && (match == null || match(action)));

        private static GameObservation V15OptionShop(int occupied, bool useful, bool both = false)
        {
            var a = Def("A", units: new[] { "ABC" });
            var b = Def("B", units: new[] { "ABC" });
            var c = Def("C", units: new[] { "ABC" });
            var x = Def("X");
            var d = Def("D");
            var defs = both || !useful ? new[] { a, b, c, x, d } : new[] { a, b, c, x };
            var item = new ItemDefinition("filler", "filler", 0, "", ItemEffectTarget.Player,
                ItemEffectDuration.Immediate, new[] { new ItemEffectDefinition(ItemEffectKind.Block, 1) });
            var hand = HandState.CreateStandard();
            for (int i = 0; i < occupied; i++) hand = Put(hand, i, HandSlotContent.Item(item.DefinitionId));
            var board = Put(Put(Put(BoardState.CreateStandard(), 0, "a"), 1, "b"), 3, "x");
            var owned = new[] { V12Fighter("a", a, 4, 0), V12Fighter("b", b, 4, 0), V12Fighter("x", x, 4, 0) };
            var offers = both ? new[] { OfferV14("a-unrelated", "D"), OfferV14("z-needed", "C") } :
                new[] { OfferV14("purchase", useful ? "C" : "D") };
            return V14Observation(40, 3, board: board, hand: hand, defs: defs, owned: owned, offers: offers,
                items: new[] { item }, stats: Stats(new CharacterStatOption(4, 0, 1)));
        }

        [TestCase(3, false, -515)]
        [TestCase(3, true, 685)]
        [TestCase(4, false, -2915)]
        [TestCase(4, true, -1715)]
        public void V15PurchaseScoresUsePublicPowerAndSixComponents(int occupied, bool useful, int scoreCents)
        {
            var row = V15Row<BuyCharacterAction>(V15OptionShop(occupied, useful),
                a => a.Placement.Kind == CharacterLocationKind.Hand);
            Assert.That(row.Score, Is.EqualTo(scoreCents / 100m));
            Assert.That(row.Composition.Value.Delta.HandReserve, Is.EqualTo(1.4m));
            Assert.That(row.Composition.Value.Delta.MergeProgress, Is.Zero);
            Assert.That(row.Composition.Value.Delta.Capacity, Is.EqualTo(occupied == 3 ? -2m : -5m));
            Assert.That(row.Composition.Value.Delta.HandUnitOption, Is.EqualTo(useful ? 1.5m : 0m));
            Assert.That(row.Economy.Value.TotalDelta, Is.EqualTo(-0.35m));
            Assert.That(row.Passes, Is.EqualTo(useful && occupied == 3));
        }

        [Test]
        public void V15PurchaseAndSaleRankingsRecognizeUsefulReserve()
        {
            var obs = V15OptionShop(3, true, true);
            var oldBuy = HeuristicStrategy.CreateV14().GetEvaluations(obs, new ActionId("old")).Where(e =>
                e.Action is BuyCharacterAction a && a.Placement.Kind == CharacterLocationKind.Hand).First();
            var newBuy = V15Row<BuyCharacterAction>(obs, a => a.Placement.Kind == CharacterLocationKind.Hand);
            Assert.That(((BuyCharacterAction)oldBuy.Action).OfferId, Is.EqualTo("a-unrelated"));
            Assert.That(((BuyCharacterAction)newBuy.Action).OfferId, Is.EqualTo("z-needed"));

            var aDef = Def("A", units: new[] { "ABC" });
            var bDef = Def("B", units: new[] { "ABC" });
            var cDef = Def("C", units: new[] { "ABC" });
            var dDef = Def("D");
            var xDef = Def("X");
            var board = Put(Put(Put(BoardState.CreateStandard(), 0, "a"), 1, "b"), 3, "x");
            var hand = Put(Put(HandState.CreateStandard(), 0, HandSlotContent.Character("a-useful")), 1,
                HandSlotContent.Character("z-other"));
            var item = new ItemDefinition("filler", "filler", 0, "", ItemEffectTarget.Player,
                ItemEffectDuration.Immediate, new[] { new ItemEffectDefinition(ItemEffectKind.Block, 1) });
            hand = Put(Put(hand, 2, HandSlotContent.Item(item.DefinitionId)), 3, HandSlotContent.Item(item.DefinitionId));
            obs = V14Observation(40, 3, board: board, hand: hand, defs: new[] { aDef, bDef, cDef, dDef, xDef },
                owned: new[] { V12Fighter("a", aDef, 4, 0), V12Fighter("b", bDef, 4, 0), V12Fighter("x", xDef, 4, 0),
                    V12Fighter("a-useful", cDef, 4, 0), V12Fighter("z-other", dDef, 4, 0) }, items: new[] { item });
            var useful = V15Row<SellCharacterAction>(obs, s => s.InstanceId == "a-useful");
            var other = V15Row<SellCharacterAction>(obs, s => s.InstanceId == "z-other");
            Assert.That(useful.Composition.Value.Delta.HandUnitOption, Is.EqualTo(-1.5m));
            Assert.That(other.Composition.Value.Delta.Capacity, Is.EqualTo(2m));
            Assert.That(other.Score - useful.Score, Is.EqualTo(12m));
            Assert.That(((SellCharacterAction)V15Row<SellCharacterAction>(obs).Action).InstanceId, Is.EqualTo("z-other"));
        }

        [Test]
        public void V15BoardMoveCanScoreTwelveWithUnchangedBoardSynergy()
        {
            var a = Def("A", units: new[] { "AB", "AC" });
            var b = Def("B", new[] { "one", "two" }, new[] { "AB" });
            var c = Def("C", units: new[] { "AC" });
            var x = Def("X", new[] { "one", "two" });
            var board = Put(Put(Put(BoardState.CreateStandard(), 0, "a"), 1, "b"), 3, "x");
            var hand = Put(HandState.CreateStandard(), 0, HandSlotContent.Character("c"));
            var obs = V14Observation(0, 4, board: board, hand: hand, defs: new[] { a, b, c, x },
                owned: new[] { Inst("a", a, 2), V12Fighter("b", b, 4, 0), V12Fighter("c", c, 1, 0), V12Fighter("x", x, 4, 0) });
            bool Target(MoveOrSwapBoardAction move) => move.SourceSlot == 0 && move.TargetSlot == 2;
            var old = HeuristicStrategy.CreateV14().GetEvaluations(obs, new ActionId("old"))
                .Single(r => r.Action is MoveOrSwapBoardAction m && Target(m));
            Assert.That(old.Score, Is.Zero);
            var row = V15Row<MoveOrSwapBoardAction>(obs, Target);
            Assert.That(row.Score, Is.EqualTo(12m));
            Assert.That(row.Composition.Value.Delta.BoardSynergy, Is.Zero);
            Assert.That(row.Composition.Value.Delta.HandUnitOption, Is.EqualTo(1.5m));
            Assert.That(row.Composition.Value.TotalDelta, Is.EqualTo(1.5m));
            var strategy = HeuristicStrategy.CreateV15();
            Assert.That(strategy.ChooseAction(obs, Context(strategy)).Action, Is.TypeOf<MoveOrSwapBoardAction>());
            var selected = (MoveOrSwapBoardAction)strategy.ChooseAction(obs, Context(strategy)).Action;
            Assert.That(V15Row<MoveOrSwapBoardAction>(obs, m => m.SourceSlot == selected.SourceSlot &&
                m.TargetSlot == selected.TargetSlot).Score, Is.EqualTo(12m));
        }

        [TestCase(1)]
        [TestCase(2)]
        public void V15StationaryMergeAddsExactlyOneActivation(int initialStar)
        {
            var members = UnitMoveBoardFixture.Board("...BAC.D.");
            var defs = UnitMoveBoardFixture.Definitions("AB,AC,AD", members);
            var a = defs.Single(d => d.DefinitionId == "A");
            var rng = new Pcg32RandomSource(3, 9);
            var stats = Stats(new CharacterStatOption(2, 2, 1));
            var owned = new List<CharacterInstance>();
            var board = BoardState.CreateStandard();
            foreach (var m in members)
            {
                var c = Inst(m.DefinitionId, defs.Single(d => d.DefinitionId == m.DefinitionId),
                    runCost: m.DefinitionId == "A" ? 1 : 2);
                if (m.DefinitionId == "A" && initialStar == 2) Assert.That(c.TrySetTemporaryStar(2, a, stats, rng), Is.True);
                owned.Add(c);
                board = Put(board, m.SlotIndex, c.InstanceId);
            }
            var hand = HandState.CreateStandard();
            var stars = initialStar == 2 ? new[] { 1, 1, 2 } : new[] { 1 };
            for (int i = 0; i < stars.Length; i++)
            {
                var material = Inst("material-" + i, a, stars[i]);
                owned.Add(material);
                hand = Put(hand, i, HandSlotContent.Character(material.InstanceId));
            }
            var obs = V14Observation(40, 4, board: board, hand: hand, defs: defs, owned: owned.ToArray(),
                offers: new[] { OfferV14("a", "A") }, stats: stats, quotas: new[] { 1, 3, 0, 0, 0 });
            var row = V15Row<BuyCharacterAction>(obs, x => x.Placement.Kind == CharacterLocationKind.Hand);
            Assert.That(row.Composition.Value.Delta.Synergy.UnitActivationValue * 8m, Is.EqualTo(64m));
            Assert.That(row.Score, Is.EqualTo(8m * row.Composition.Value.TotalDelta + row.Economy.Value.TotalDelta));
            var shop = V15Row<RerollShopAction>(obs, details: true).Shop;
            Assert.That(shop.BestCurrent.Benefit, Is.EqualTo(row.Score));
            Assert.That(shop.ExpectedFutureOpportunity, Is.EqualTo(shop.BestCurrent.Benefit));
            var withoutOption = HeuristicStrategy.CreateV15(new HeuristicV15Settings(
                new IntegratedCompositionSettings(handOption: new HandSynergyOptionSettings(0m))));
            var disabled = V15Row<BuyCharacterAction>(obs, x => x.Placement.Kind == CharacterLocationKind.Hand, withoutOption);
            Assert.That(disabled.Composition.Value.Delta.Synergy.UnitActivationValue, Is.EqualTo(8m));
            Assert.That(disabled.Composition.Value.After.HandOption.Status, Is.EqualTo(HandSynergyOptionStatus.Disabled));
            var disabledShop = V15Row<RerollShopAction>(obs, strategy: withoutOption).Shop;
            Assert.That(disabledShop.ExpectedFutureOpportunity, Is.EqualTo(disabledShop.BestCurrent.Benefit));
        }

        [Test]
        public void V15CrossMoveRealizesOptionAndSameDefinitionExchangeReadsStar()
        {
            var a = Def("A", units: new[] { "ABC" });
            var b = Def("B", units: new[] { "ABC" });
            var c = Def("C", units: new[] { "ABC" });
            var obs = V14Observation(0, 3, board: Put(Put(BoardState.CreateStandard(), 0, "a"), 1, "b"),
                hand: Put(HandState.CreateStandard(), 0, HandSlotContent.Character("c")),
                defs: new[] { a, b, c }, owned: new[] { Inst("a", a), Inst("b", b), V12Fighter("c", c, 4, 0) });
            var deploy = V15Row<MoveHandToBoardAction>(obs, m => m.BoardSlot == 2);
            Assert.That(deploy.Composition.Value.Delta.BoardSynergy, Is.EqualTo(10m));
            Assert.That(deploy.Composition.Value.Delta.HandUnitOption, Is.EqualTo(-1.5m));
            Assert.That(8m * (deploy.Composition.Value.Delta.BoardSynergy + deploy.Composition.Value.Delta.HandUnitOption), Is.EqualTo(68m));
            Assert.That(deploy.Score, Is.EqualTo(deploy.Composition.Value.TotalDelta * 8m));

            a = Def("A", units: new[] { "AB", "AC" });
            b = Def("B", units: new[] { "AB" });
            c = Def("C", units: new[] { "AC" });
            var board = Put(Put(Put(BoardState.CreateStandard(), 0, "a"), 1, "b"), 3, "c");
            obs = V14Observation(0, 3, board: board, hand: Put(HandState.CreateStandard(), 0, HandSlotContent.Character("up")),
                defs: new[] { a, b, c }, owned: new[] { Inst("a", a), Inst("b", b), Inst("c", c), Inst("up", a, 2) });
            var swap = V15Row<MoveHandToBoardAction>(obs, m => m.BoardSlot == 0);
            Assert.That(swap.Composition.Value.Delta.BoardSynergy, Is.EqualTo(8m));
            Assert.That(swap.Score, Is.EqualTo(V15Row<MoveBoardToHandAction>(obs, m => m.BoardSlot == 0).Score));
            var strategy = HeuristicStrategy.CreateV15();
            var action = strategy.ChooseAction(obs, Context(strategy)).Action;
            Assert.That(action is MoveHandToBoardAction || action is MoveBoardToHandAction, Is.True);
        }

        [Test]
        public void V15ShopUsesHandOptionInCurrentAndFutureAndChargesRerollOnce()
        {
            var obs = V15OptionShop(3, true);
            var row = V15Row<RerollShopAction>(obs, details: true);
            Assert.That(row.Minimum, Is.EqualTo(2m));
            Assert.That(row.Shop.FutureDiscount, Is.EqualTo(0.70m));
            Assert.That(row.Shop.CurrentOpportunity, Is.EqualTo(6.85m));
            Assert.That(row.Shop.ExpectedFutureOpportunity, Is.EqualTo(5.1625m));
            Assert.That(row.Economy.Value.TotalDelta, Is.EqualTo(-0.35m));
            Assert.That(row.Score, Is.EqualTo(-3.58625m));
            Assert.That(row.Shop.BestCurrent.Benefit, Is.EqualTo(V15Row<BuyCharacterAction>(obs,
                a => a.Placement.Kind == CharacterLocationKind.Hand).Score));
            var disabled = HeuristicStrategy.CreateV15(new HeuristicV15Settings(
                new IntegratedCompositionSettings(handOption: new HandSynergyOptionSettings(0m))));
            var noOption = V15Row<RerollShopAction>(obs, strategy: disabled);
            Assert.That(noOption.Shop.CurrentOpportunity, Is.Zero);
            Assert.That(noOption.Shop.ExpectedFutureOpportunity, Is.EqualTo(2.80546875m));
            var buy = V15Row<BuyCharacterAction>(obs, a => a.Placement.Kind == CharacterLocationKind.Hand, disabled, true);
            Assert.That(buy.Composition.Value.Before.HandOption.Status, Is.EqualTo(HandSynergyOptionStatus.Disabled));
            Assert.That(buy.Composition.Value.After.HandOption.Details.LegalCandidates, Is.Null);
        }

        [Test]
        public void V15UnknownCostMassRemainsUnchanged()
        {
            var a = Def("a");
            var b = Def("b");
            var obs = V14Observation(12, 2, board: Put(BoardState.CreateStandard(), 0, "a"),
                owned: new[] { V12Fighter("a", a, 4, 0) }, defs: new[] { a, b },
                quotas: new[] { 1, 1, 0, 0, 0 }, weights: new[] { 1, 1, 0, 0, 0 },
                stats: Stats(new CharacterStatOption(4, 0, 1)));
            var result = V15Row<RerollShopAction>(obs).Shop;
            var legacy = new ShopOpportunityEvaluator(new HeuristicV14Settings().Shop).Evaluate(obs);
            Assert.That(result.Distribution.UnknownCount, Is.EqualTo(1));
            Assert.That(result.FutureMass.Value.Unavailable, Is.EqualTo(0.5m));
            Assert.That(result.ExpectedFutureOpportunity, Is.EqualTo(legacy.ExpectedFutureOpportunity).Within(0.0000001m));
        }

        [Test]
        public void V15ForecastUsesGoldRemainingAfterReroll()
        {
            var a = Def("a");
            var b = Def("b");
            var obs = V14Observation(12, 2, board: Put(BoardState.CreateStandard(), 0, "a"),
                owned: new[] { Inst("a", a) }, defs: new[] { a, b }, rerollCost: 9,
                quotas: new[] { 1, 0, 0, 1, 0 }, weights: new[] { 0, 0, 0, 1, 0 },
                stats: Stats(new CharacterStatOption(4, 0, 1)));
            var shop = V15Row<RerollShopAction>(obs).Shop;
            Assert.That(shop.FutureMass.Value.InsufficientGold, Is.EqualTo(1m));
            Assert.That(shop.ExpectedFutureOpportunity, Is.Zero);
            Assert.That(shop.Score, Is.EqualTo(shop.RerollEconomy.Value.TotalDelta));
        }

        [Test]
        public void V15UsesRawScoreAfterTheRerollAndXpGates()
        {
            var a = Def("a");
            var b = Def("b");
            var obs = V14Observation(level: 4, xp: 3, board: Put(BoardState.CreateStandard(), 0, "a"),
                owned: new[] { Inst("a", a) }, defs: new[] { a, b }, stats: Stats(new CharacterStatOption(2, 1, 1)));
            var probe = V15Row<RerollShopAction>(obs);
            var strategy = HeuristicStrategy.CreateV15(new HeuristicV15Settings(preparation: new HeuristicV14Settings(
                growth: new GrowthValueSettings(4.68m / 0.625m, .5m, 0),
                futureDiscount: (3.5m - probe.Economy.Value.TotalDelta) / probe.Shop.ExpectedFutureOpportunity.Value)));
            Assert.That(V15Row<BuyExperienceAction>(obs, strategy: strategy).Score, Is.EqualTo(3m).Within(0.0000001m));
            Assert.That(V15Row<RerollShopAction>(obs, strategy: strategy).Score, Is.EqualTo(3.5m).Within(0.0000001m));
            Assert.That(strategy.ChooseAction(obs, Context(strategy)).Action, Is.TypeOf<RerollShopAction>());
        }

        [TestCase(StageChoice.SelectA, 200, false)]
        [TestCase(StageChoice.SelectA, 201, true)]
        [TestCase(StageChoice.SelectD, 400, false)]
        [TestCase(StageChoice.SelectD, 401, true)]
        public void V15RerollThresholdAndRawRankingStayUnchanged(StageChoice choice, int desiredCents, bool selected)
        {
            var a = Def("a");
            var obs = V14Observation(level: 1, choice: choice, board: Put(BoardState.CreateStandard(), 0, "a"),
                defs: new[] { a }, owned: new[] { Inst("a", a) }, stats: Stats(new CharacterStatOption(4, 0, 1)));
            var row = V15Row<RerollShopAction>(obs);
            decimal desired = desiredCents / 100m;
            decimal future = row.Shop.ExpectedFutureOpportunity.Value;
            decimal discount = (desired - row.Economy.Value.TotalDelta) / future;
            if (!selected && ShopOpportunityMath.Score(future, 0, row.Economy.Value.TotalDelta, discount) > desired)
                discount -= 0.000000000000000000000000001m;
            var strategy = HeuristicStrategy.CreateV15(new HeuristicV15Settings(preparation: new HeuristicV14Settings(
                growth: new GrowthValueSettings(0m, 0.5m, 0m), futureDiscount: discount)));
            var actual = V15Row<RerollShopAction>(obs, strategy: strategy);
            Assert.That(actual.Score, Is.EqualTo(desired).Within(0.000000000000000001m));
            Assert.That(actual.Passes, Is.EqualTo(selected));
            Assert.That(strategy.ChooseAction(obs, Context(strategy)).Action is RerollShopAction, Is.EqualTo(selected));
        }

        [Test]
        public void V15ChoiceItemAndXpBehaviorMatchesV14()
        {
            var item = new ItemDefinition("item", "Item", 2, "", ItemEffectTarget.Player,
                ItemEffectDuration.Immediate, new[] { new ItemEffectDefinition(ItemEffectKind.Block, 1) });
            var old = HeuristicStrategy.CreateV14();
            var next = HeuristicStrategy.CreateV15();
            var itemShop = V14Observation(choice: StageChoice.SelectB, level: 9, items: new[] { item },
                offers: new[] { new ShopOffer("item-offer", ShopOfferKind.Item, "item", 2, null) });
            Assert.That(V15Row<BuyItemAction>(itemShop).Score, Is.Null);
            Assert.That(V15Row<RerollShopAction>(itemShop).Status, Is.EqualTo("ItemPolicy"));
            foreach (var obs in new[] { Build(Choice()), itemShop, V14Observation(level: 4, xp: 3),
                V14Observation(gold: 7, level: 4, xp: 3) })
            {
                var a = old.ChooseAction(obs, Context(old));
                var b = next.ChooseAction(obs, Context(next));
                Assert.That(SimulationStateFingerprint.Create(b.Action), Is.EqualTo(SimulationStateFingerprint.Create(a.Action)));
                Assert.That(b.NextState.Payload, Is.EqualTo(a.NextState.Payload));
            }
        }

        [TestCase(StageChoice.SelectA, 99, false)]
        [TestCase(StageChoice.SelectA, 100, false)]
        [TestCase(StageChoice.SelectA, 101, true)]
        [TestCase(StageChoice.SelectD, 199, false)]
        [TestCase(StageChoice.SelectD, 200, false)]
        [TestCase(StageChoice.SelectD, 201, true)]
        public void V15KeepsStrictNormalThreshold(StageChoice choice, int score, bool selected)
        {
            var obs = V14Observation(level: 1, choice: choice);
            var strategy = HeuristicStrategy.CreateV15(new HeuristicV15Settings(preparation: new HeuristicV14Settings(
                growth: new GrowthValueSettings(score / 100m + (choice == StageChoice.SelectD ? 1.40m : 1.68m), .5m, 0))));
            var decision = strategy.ChooseAction(obs, Context(strategy));
            Assert.That(decision.Action is BuyExperienceAction, Is.EqualTo(selected));
            if (!selected && choice == StageChoice.SelectA) Assert.That(decision.Action, Is.TypeOf<StartBattleAction>());
            if (!selected && choice == StageChoice.SelectD) Assert.That(decision.HasAction, Is.False);
        }

        [Test]
        public void V15PriorityAndSuppressionAvoidNeedlessForecasts()
        {
            var d = Def("d");
            var obs = V14Observation(12, 1, defs: new[] { d }, offers: new[] { OfferV14("a", "d") },
                stats: Stats(new CharacterStatOption(2, 0, 1)));
            var profiler = new SimulationProfileCollector();
            var strategy = HeuristicStrategy.CreateV15(profiler: profiler);
            var first = strategy.ChooseAction(obs, Context(strategy));
            Assert.That(first.Action, Is.TypeOf<BuyCharacterAction>());
            Assert.That(((BuyCharacterAction)first.Action).Placement.SlotIndex, Is.Zero);
            Assert.That(first.Diagnostic, Does.Contain("EmptyBoardPriority"));
            Assert.That(profiler.Counters.ContainsKey("Strategy.V15.RerollEvaluations"), Is.False);
            Assert.That(profiler.Counters["Strategy.V15.IntegratedContexts"], Is.EqualTo(1));
            var repeat = strategy.ChooseAction(obs, Context(strategy, state: first.NextState));
            Assert.That(((BuyCharacterAction)repeat.Action).Placement.SlotIndex, Is.EqualTo(1));
            var limit = new StrategyState(strategy.StrategyId, strategy.Version, 2, "hs2|29|0|2|");
            Assert.That(strategy.ChooseAction(obs, Context(strategy, state: limit)).Diagnostic, Does.Contain("Preparation limit"));
            Assert.That(profiler.Counters.ContainsKey("Strategy.V15.RerollEvaluations"), Is.False);
            Assert.That(V15Row<RerollShopAction>(V14Observation(11)).Status, Is.EqualTo("LowGoldReroll"));
            var a = Def("a");
            var b = Def("b");
            var over = V14Observation(12, 1, board: Put(Put(BoardState.CreateStandard(), 0, "a"), 1, "b"),
                defs: new[] { a, b }, owned: new[] { Inst("a", a), Inst("b", b) });
            Assert.That(strategy.ChooseAction(over, Context(strategy, state: limit)).Diagnostic, Does.Contain("LevelRepair"));
            Assert.That(profiler.Counters.ContainsKey("Strategy.V15.RerollEvaluations"), Is.False);

            var previousReroll = new StrategyState(strategy.StrategyId, strategy.Version, 2, "hs2|1|0|2|NDox");
            Assert.That(strategy.ChooseAction(V14Observation(level: 9, defs: new[] { d }),
                Context(strategy, state: previousReroll)).Action, Is.TypeOf<StartBattleAction>());
            Assert.That(profiler.Counters.ContainsKey("Strategy.V15.RerollEvaluations"), Is.False);
        }

        [Test]
        public void V15IdentityAndCandidateValuesAreDeterministic()
        {
            var obs = V15OptionShop(3, true);
            var normal = HeuristicStrategy.CreateV15();
            string trace = null;
            var traced = HeuristicStrategy.CreateV15(decisionDiagnosticSink: t => trace = t);
            var fingerprint = SimulationStateFingerprint.Create(obs);
            var rng = StrategyRandomSource.Create(123, normal.StrategyId, normal.Version).CaptureState();
            var first = normal.ChooseAction(obs, Context(normal, rng: rng));
            var second = traced.ChooseAction(obs, Context(traced, rng: rng));
            Assert.That(SimulationStateFingerprint.Create(second.Action), Is.EqualTo(SimulationStateFingerprint.Create(first.Action)));
            Assert.That(second.NextState.Payload, Is.EqualTo(first.NextState.Payload));
            Assert.That(second.NextStrategyRngState, Is.EqualTo(rng));
            Assert.That(second.NextState.SchemaVersion, Is.EqualTo(2));
            Assert.That(trace, Does.Contain("visible-v15-"));
            var cached = normal.InspectV15Preparation(obs, new ActionId("test"), includeDetails: true);
            var uncached = normal.InspectV15Preparation(obs, new ActionId("test"), useCache: false);
            Assert.That(uncached.Select(r => (r.Score, r.Status)), Is.EqualTo(cached.Select(r => (r.Score, r.Status))));
            Assert.That(SimulationStateFingerprint.Create(obs), Is.EqualTo(fingerprint));
            var id = HeuristicStrategy.GetV15ConfigurationIdentity();
            Assert.That(id, Is.Not.EqualTo(HeuristicStrategy.GetV14ConfigurationIdentity()));
            var variant = new HeuristicV15Settings(new IntegratedCompositionSettings(handOption: new HandSynergyOptionSettings(0)));
            Assert.That(id, Is.Not.EqualTo(HeuristicStrategy.GetV15ConfigurationIdentity(variant)));
            Assert.That(HeuristicStrategy.GetV15ObservationFingerprint(obs, variant),
                Is.Not.EqualTo(HeuristicStrategy.GetV15ObservationFingerprint(obs)));
            var prior = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("fr-FR");
                Assert.That(HeuristicStrategy.GetV15ConfigurationIdentity(), Is.EqualTo(id));
            }
            finally { CultureInfo.CurrentCulture = prior; }
        }
    }
}
