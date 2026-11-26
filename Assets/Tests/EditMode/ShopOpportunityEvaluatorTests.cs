using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ProjectNST.GameCore.Actions;
using ProjectNST.GameCore.Board;
using ProjectNST.GameCore.Characters;
using ProjectNST.GameCore.Economy;
using ProjectNST.GameCore.Enemies;
using ProjectNST.GameCore.Observation;
using ProjectNST.GameCore.Stages;
using ProjectNST.GameCore.Strategy;

namespace ProjectNST.GameCore.Shop.Tests
{
    public sealed class ShopOpportunityEvaluatorTests
    {
        private static EconomyValueSettings Economy => EconomyValueSettings.TwoSegment(.70m, .35m, 8, .70m);
        private static ShopOpportunityEvaluator Evaluator(decimal weight = 8m, decimal discount = .70m) =>
            new ShopOpportunityEvaluator(new ShopOpportunitySettings(Economy, weight, discount));

        [TestCase(0, 0)]
        [TestCase(1, 2.9701)]
        [TestCase(5, 14.2625)]
        [TestCase(10, 27.1)]
        [TestCase(20, 48.8)]
        [TestCase(50, 87.5)]
        [TestCase(100, 100)]
        public void HitProbability(int percent, double expectedPercent)
        {
            decimal p = percent / 100m;
            var result = ShopOpportunityMath.ExpectedMaximum(new[] { (0m, 1m - p), (1m, p) }, 3);
            Assert.That(result * 100m, Is.EqualTo((decimal)expectedPercent));
        }

        [Test]
        public void CdfMatchesExplicitSmallEnumerationAndIsOrderIndependent()
        {
            var rows = new[] { (Benefit: 0m, Probability: .7m), (Benefit: 10m, Probability: .2m), (Benefit: 30m, Probability: .1m) };
            decimal exhaustive = 0;
            foreach (var a in rows)
            foreach (var b in rows)
            foreach (var c in rows)
                exhaustive += Math.Max(a.Benefit, Math.Max(b.Benefit, c.Benefit)) * a.Probability * b.Probability * c.Probability;
            Assert.That(exhaustive, Is.EqualTo(11.99m));
            Assert.That(ShopOpportunityMath.ExpectedMaximum(rows, 3), Is.EqualTo(exhaustive));
            Assert.That(ShopOpportunityMath.ExpectedMaximum(rows.Reverse(), 3), Is.EqualTo(exhaustive));
            Assert.That(ShopOpportunityMath.ExpectedMaximum(rows, 1), Is.EqualTo(5m));
            Assert.That(ShopOpportunityMath.ExpectedMaximum(rows, 1) * 3, Is.EqualTo(15m));
            Assert.That(ShopOpportunityMath.ExpectedMaximum(rows, 0), Is.Zero);
            Assert.That(ShopOpportunityMath.ExpectedMaximum(new[] { (0m, .4m), (-3m, .6m), (99m, 0m) }, 3), Is.Zero);
            Assert.That(ShopOpportunityMath.ExpectedMaximum(new[] { (7m, .25m), (7m, .75m) }, 3), Is.EqualTo(7m));
            Assert.That(ShopOpportunityMath.ExpectedMaximum(new[] { (7m, 1m) }, 20), Is.EqualTo(7m));
        }

        [TestCase(0, -6.42)]
        [TestCase(.25, -3.4225)]
        [TestCase(.50, -.425)]
        [TestCase(.70, 1.973)]
        [TestCase(1, 5.57)]
        public void DiscountsOnlyFuture(double discount, double expected) =>
            Assert.That(ShopOpportunityMath.Score(11.99m, 6m, -.42m, (decimal)discount), Is.EqualTo((decimal)expected));

        [Test]
        public void InvalidNumericalInputsAndOverflowAreRejected()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => ShopOpportunityMath.ExpectedMaximum(new[] { (1m, 1m) }, -1));
            Assert.Throws<ArgumentOutOfRangeException>(() => ShopOpportunityMath.ExpectedMaximum(new[] { (1m, -.1m) }, 3));
            Assert.Throws<ArgumentException>(() => ShopOpportunityMath.ExpectedMaximum(new[] { (1m, .9m) }, 3));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ShopOpportunitySettings(Economy, -1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ShopOpportunitySettings(Economy, 8, 1.01m));
            Assert.Throws<OverflowException>(() => ShopOpportunityMath.Score(decimal.MaxValue, 0, 2, 1));
        }

        [Test]
        public void SpecifiedMixedCostExamplePreservesUnknownMass()
        {
            string[] ids = Enumerable.Range(0, 30).Select(i => "d" + i).ToArray();
            var known = ids.Take(10).Select((id, i) => (id, i < 3 ? 1 : i < 7 ? 2 : 3)).ToArray();
            var d = ShopCostDistribution.Estimate(ids, new[] { 6, 6, 6, 6, 6 },
                new[] { 80, 15, 3, 2, 0 }, known.Append(known[0]));
            Assert.That(d.UnavailableReason, Is.Null);
            Assert.That((d.KnownCount, d.UnknownCount), Is.EqualTo((10, 20)));
            var unknown = d.Outcomes.Single(x => x.DefinitionId == "d10" && x.Cost == 1);
            Assert.That(unknown.RunCostProbability, Is.EqualTo(.15m));
            Assert.That(unknown.SlotProbability, Is.EqualTo(.02m).Within(ShopOpportunityMath.ProbabilityTolerance));
            Assert.That(d.Outcomes.Single(x => x.DefinitionId == "d0").SlotProbability,
                Is.EqualTo(.8m / 6m).Within(ShopOpportunityMath.ProbabilityTolerance));
            Assert.That(d.Outcomes.Any(x => x.Cost == 5), Is.False);
            Assert.That(d.ProbabilityTotal, Is.EqualTo(1m).Within(ShopOpportunityMath.ProbabilityTolerance));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void EntireRosterUnknownOrKnownWithCustomWeights(bool known)
        {
            var d = ShopCostDistribution.Estimate(new[] { "b", "a" }, new[] { 1, 0, 1, 0, 0 },
                new[] { 1, 99, 3, 0, 0 }, known ? new[] { ("a", 1), ("b", 3) } : Array.Empty<(string, int)>());
            Assert.That(d.UnavailableReason, Is.Null);
            Assert.That(d.Outcomes.Select(o => o.Cost).Distinct(), Is.EqualTo(new[] { 1, 3 }));
            Assert.That(d.Outcomes.Where(o => o.Cost == 1).Sum(o => o.SlotProbability), Is.EqualTo(.25m));
            Assert.That(d.Outcomes.Sum(o => o.SlotProbability), Is.EqualTo(1m));
            Assert.That(d.Outcomes.Count, Is.EqualTo(known ? 2 : 4));
        }

        [TestCase(0, "Conflicting")]
        [TestCase(1, "exceeds")]
        [TestCase(2, "Roster size")]
        [TestCase(3, "No Cost")]
        [TestCase(4, "RunCost")]
        [TestCase(5, "RunCost")]
        public void ContradictoryPublicDistributionIsUnavailable(int mode, string message)
        {
            var known = mode == 0 ? new[] { ("a", 1), ("a", 2) } : mode == 1 ? new[] { ("a", 1), ("b", 1) }
                : mode == 4 ? new[] { ("c", 1) } : mode == 5 ? new[] { ("a", 6) } : Array.Empty<(string, int)>();
            var distribution = ShopCostDistribution.Estimate(new[] { "a", "b" },
                new[] { 1, mode == 2 ? 0 : 1, 0, 0, 0 }, new[] { mode == 3 ? 0 : 1, 0, 2, 0, 0 }, known);
            Assert.That(distribution.UnavailableReason, Does.Contain(message));
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        [TestCase(5)]
        public void FutureAffordabilityRetainsUnaffordableProbability(int remaining)
        {
            var o = Observe(remaining + 1, 1, counts: new[] { 1, 1, 1, 1, 1 });
            var r = Evaluator().Evaluate(o, true);
            Assert.That(r.Status, Is.EqualTo(ShopOpportunityStatus.Evaluated));
            Assert.That(r.RemainingGold, Is.EqualTo(remaining));
            Assert.That(r.FutureMass.Value.InsufficientGold, Is.EqualTo((5 - remaining) / 5m));
            Assert.That(r.Distribution.ProbabilityTotal, Is.EqualTo(1m));
            if (remaining == 0)
            {
                Assert.That(r.ExpectedFutureOpportunity, Is.Zero);
                Assert.That(r.HypotheticalPreviewCount, Is.Zero);
            }
        }

        [TestCase(20, 1, 3, -.42)]
        [TestCase(8, 2, 3, -1.54)]
        [TestCase(31, 1, 3, -.35)]
        [TestCase(30, 1, 3, -.42)]
        [TestCase(20, 1, 0, -.35)]
        [TestCase(20, 0, 3, 0)]
        public void RerollCostUsesFixedPublicInterest(int gold, int price, int reward, double expected)
        {
            var r = Evaluator().Evaluate(Observe(gold, price, baseReward: reward));
            Assert.That(r.RerollEconomy.Value.TotalDelta, Is.EqualTo((decimal)expected));
            Assert.That(r.Score, Is.EqualTo(r.DiscountedFutureOpportunity - r.CurrentOpportunity + (decimal)expected));
        }

        [Test]
        public void FuturePurchaseUsesRemainingGoldAcrossReserveBoundary()
        {
            var o = Observe(9, 2, counts: new[] { 1, 0, 0, 0, 0 }, offers: new[] { Offer("a", "d0") });
            var r = Evaluator().Evaluate(o, true);
            var current = r.Details.Single(x => x.OfferId == "a");
            var future = r.Details.Single(x => x.OfferId == null);
            Assert.That(current.Benefit - future.Benefit, Is.EqualTo(.35m));
            Assert.That(r.HypotheticalPreviewCount, Is.Zero, "The formation deltas are reusable across balances.");
        }

        [TestCase(CharacterLocationKind.Board)]
        [TestCase(CharacterLocationKind.Hand)]
        public void RealAndHypotheticalPurchaseUseTheSameComposition(CharacterLocationKind kind)
        {
            var o = Observe(20, 1, offers: new[] { Offer("a", "d0") });
            var evaluator = new CompositionValueEvaluator(o);
            var actual = evaluator.ComparePurchase(new BuyCharacterAction(new ActionId("purchase"), "a", new GameActionLocation(kind, 0)));
            var hypothetical = evaluator.CompareHypotheticalPurchase(o.SelectedRoster[0], 1, 1, 20,
                new CharacterLocation(kind, 0));
            Assert.That(hypothetical.TotalDelta, Is.EqualTo(actual.TotalDelta));
            Assert.That(hypothetical.Preview.FinalLocation, Is.EqualTo(actual.Preview.FinalLocation));
            Assert.That(o.Characters, Is.Empty);
            Assert.That(o.Shop.Offers[0].Status, Is.EqualTo(ShopOfferStatus.Available));
            var expensive = evaluator.CompareHypotheticalPurchase(o.SelectedRoster[0], 2, 2, 20,
                new CharacterLocation(kind, 0));
            Assert.That(expensive.TotalDelta, Is.GreaterThan(hypothetical.TotalDelta));
        }

        [TestCase(0, 1)]
        [TestCase(2, 2)]
        [TestCase(3, 2)]
        public void HypotheticalMergeUsesVisibleMaterialAndPreservesIntermediate(int twoStars, int stages)
        {
            var owned = new[] { ("a", "d0", 1, 1), ("b", "d0", 1, 1) }.Concat(
                Enumerable.Range(0, twoStars).Select(i => ("two" + i, "d0", 1, 2))).ToArray();
            string[] board = twoStars == 0 ? new[] { "a" } : new[] { "two0" };
            string[] hand = twoStars == 0 ? new[] { "b" } : twoStars == 3
                ? new[] { "two1", "two2", "a", "b" } : new[] { "a", "b", "two1" };
            var o = Observe(20, 1, board: board, hand: hand, owned: owned, offers: new[] { Offer("a", "d0") });
            var evaluator = new CompositionValueEvaluator(o);
            var hypothetical = evaluator.CompareHypotheticalPurchase(o.SelectedRoster[0], 1, 1, 20,
                new CharacterLocation(CharacterLocationKind.Hand, 4));
            var actual = evaluator.ComparePurchase(new BuyCharacterAction(new ActionId("purchase"), "a",
                new GameActionLocation(CharacterLocationKind.Hand, 4)));
            Assert.That(hypothetical.Preview.MergeStageCount, Is.EqualTo(stages));
            Assert.That(hypothetical.TotalDelta, Is.EqualTo(actual.TotalDelta));
            if (twoStars == 3) Assert.That(hypothetical.Preview.StarsAfter.TwoStar, Is.EqualTo(1));
            Assert.That(o.Characters.Count, Is.EqualTo(owned.Length));
        }

        [Test]
        public void FullHandDirectMergeIsRulePossibleButMayBeUnreachableFromCandidates()
        {
            var owned = new[] { ("a", "d0", 1, 1), ("b", "d0", 1, 1), ("c", "d1", 2, 1),
                ("d", "d2", 3, 1), ("e", "d3", 4, 1), ("f", "d4", 5, 1) };
            var o = Observe(20, 1, board: new[] { "a" }, hand: new[] { "b", "c", "d", "e", "f" },
                owned: owned, offers: new[] { Offer("a", "d0") });
            var direct = new CharacterPurchasePreview(o).PreviewHypothetical(o.SelectedRoster[0], 1, 1, 20,
                new CharacterLocation(CharacterLocationKind.Hand, 0));
            Assert.That(direct.MergeStageCount, Is.EqualTo(1));
            var result = Evaluator().Evaluate(o, true);
            Assert.That(result.Details.Single(x => x.OfferId == "a").Status,
                Is.EqualTo(ShopPurchaseOpportunityStatus.NoReachablePlacement));
        }

        [Test]
        public void CurrentShopKeepsOneChoiceAndBreaksEqualOfferTiesById()
        {
            var r = Evaluator().Evaluate(Observe(20, 1, offers: new[] { Offer("b", "d0"),
                Offer("a", "d0"), Offer("c", "d1", 2), Offer("p", "d4", 5, ShopOfferStatus.Purchased),
                ShopOffer.CreateEmpty("empty") }), true);
            Assert.That(r.Status, Is.EqualTo(ShopOpportunityStatus.Evaluated));
            var current = r.Details.Where(x => x.OfferId != null).ToArray();
            Assert.That(current.Length, Is.EqualTo(3));
            Assert.That(r.CurrentOpportunity, Is.EqualTo(current.Max(x => x.Benefit)));
            Assert.That(current[0].Benefit, Is.EqualTo(current[1].Benefit));
            var tied = Evaluator().Evaluate(Observe(20, 1, offers: new[] { Offer("b", "d0"), Offer("a", "d0") }));
            Assert.That(tied.BestCurrent.OfferId, Is.EqualTo("a"));
            Assert.That(tied.Details, Is.Empty);
        }

        [Test]
        public void MissingCurrentStatsStopsScoringButFutureMissingStatsAreSeparateMass()
        {
            var now = Evaluator().Evaluate(Observe(20, 1, stats: false, offers: new[] { Offer("a", "d0") }));
            Assert.That(now.Status, Is.EqualTo(ShopOpportunityStatus.CurrentOfferUnavailable));
            Assert.That(now.UnavailableReason, Does.Contain("PublicStatsUnavailable"));
            Assert.That(now.Score, Is.Null);
            var future = Evaluator().Evaluate(Observe(20, 1, stats: false));
            Assert.That(future.Status, Is.EqualTo(ShopOpportunityStatus.Evaluated));
            Assert.That(future.ExpectedFutureOpportunity, Is.Zero);
            Assert.That(future.FutureMass.Value.Unavailable, Is.EqualTo(1m));
            Assert.That(future.FutureMass.Value.NonPositive, Is.Zero);
        }

        [Test]
        public void PublicStatusItemAndUnaffordableRerollAreDistinct()
        {
            Assert.That(Evaluator().Evaluate(Observe(20, 1, item: true)).Status, Is.EqualTo(ShopOpportunityStatus.ItemShopUnsupported));
            Assert.That(Evaluator().Evaluate(Observe(1, 2)).Status, Is.EqualTo(ShopOpportunityStatus.InsufficientGold));
            Assert.That(Evaluator().Evaluate(Observe(20, null)).Status, Is.EqualTo(ShopOpportunityStatus.PublicConfigurationUnavailable));
            Assert.That(Evaluator().Evaluate(Observe(20, 1, unselected: true)).Status, Is.EqualTo(ShopOpportunityStatus.RerollUnavailable));
            Assert.That(Evaluator().Evaluate(Observe(20, 1, slots: 0)).ExpectedFutureOpportunity, Is.Zero);
            var zero = Evaluator(0).Evaluate(Observe(20, 1, offers: new[] { Offer("a", "d0") }));
            Assert.That(zero.CurrentOpportunity, Is.Zero);
            Assert.That(zero.FutureMass.Value.NonPositive, Is.EqualTo(1m));
        }

        [Test]
        public void DuplicateBoardAndOverLevelAreDistinguishedFromPreviewFailure()
        {
            var o = Observe(1, 0, level: 1, board: new[] { "x" }, owned: new[] { ("x", "d0", 1, 1) },
                offers: new[] { Offer("dup", "d0"), Offer("cost", "d1", 2) });
            var preview = new CharacterPurchasePreview(o);
            var board = new CharacterLocation(CharacterLocationKind.Board, 1);
            Assert.That(preview.PreviewHypothetical(o.SelectedRoster[0], 1, 1, 1, board).Failure,
                Is.EqualTo(CharacterPurchasePreviewFailure.DuplicateBoardDefinition));
            Assert.That(preview.PreviewHypothetical(o.SelectedRoster[1], 2, 2, 1, board).Failure,
                Is.EqualTo(CharacterPurchasePreviewFailure.InsufficientGold));
            var projected = preview.PreviewHypothetical(o.SelectedRoster[2], 1, 1, 1, board);
            Assert.That(projected.After.OverLevelCount, Is.EqualTo(1));
            var r = Evaluator().Evaluate(o, true);
            Assert.That(r.Details.Single(x => x.OfferId == "cost").Status, Is.EqualTo(ShopPurchaseOpportunityStatus.InsufficientGold));
            Assert.That(r.BestCurrent?.Placement?.Kind, Is.Not.EqualTo(CharacterLocationKind.Board));
            var unavailable = new CompositionValueEvaluator(o).ComparePurchase(new BuyCharacterAction(new ActionId("missing"),
                "missing", new GameActionLocation(CharacterLocationKind.Board, 1)));
            Assert.That(unavailable.Failure, Is.EqualTo(PurchaseCompositionFailure.PreviewUnavailable));
        }

        [TestCase("A", 20, 0, 0.4, 1, 6, 2.642304)]
        [TestCase("B", 20, 0, 0.13333333333333333, 1, 24, 5.341)]
        [TestCase("C", 20, 0, 0, 1, 24, -0.42)]
        [TestCase("D", 20, 0, 0.08333333333333333, 3, 30, 4.202)]
        [TestCase("E", 20, 22, 0.13333333333333333, 1, 24, -16.659)]
        [TestCase("F", 20, 0, 0.25, 3, 12, 3.926)]
        [TestCase("G", 20, 0, 0.4, 1, 18, 9.228)]
        [TestCase("H", 20, 0, 0, 1, 0, -0.42)]
        [TestCase("I", 20, 0, 0.08333333333333333, 3, 30, 4.202)]
        [TestCase("J", 1, 0, 0, 1, 0, -0.77)]
        [TestCase("O", 20, 0, 0.016666666666666666, 2, 36, 0.79)]
        public void WorkedSingleBenefitScenario(string example, int gold, int current, double probability, int cost,
            int compositionPoints, double expectedScore)
        {
            var economy = new EconomyValueEvaluator(Economy, new EconomyInterestContext(10, 1, 3, InterestRewardEligibility.Eligible));
            decimal p = (decimal)probability;
            int remaining = gold - 1;
            decimal benefit = p == 0 ? 0 : compositionPoints + economy.Compare(remaining, remaining - cost).TotalDelta;
            decimal future = ShopOpportunityMath.ExpectedMaximum(new[] { (0m, 1 - p), (benefit, p) }, 3);
            decimal total = ShopOpportunityMath.Score(future, current, economy.Compare(gold, remaining).TotalDelta, .70m);
            Assert.That(total, Is.EqualTo((decimal)expectedScore).Within(.001m), example);
        }

        [TestCase(3, 2.055)]
        [TestCase(7, 6.903)]
        [TestCase(12, 7.949)]
        [TestCase(40, 8.158)]
        public void WorkedMultiCostCaseUsesRemainingGold(int gold, double expected)
        {
            var economy = new EconomyValueEvaluator(Economy, new EconomyInterestContext(10, 1, 3, InterestRewardEligibility.Eligible));
            int remaining = gold - 1;
            var outcomes = new[] { (0m, .2m), (6m, .1m), (8m, .2m), (14m, .5m) };
            var values = outcomes.Select((x, cost) => (cost == 0 || cost > remaining ? 0m :
                x.Item1 + economy.Compare(remaining, remaining - cost).TotalDelta, x.Item2));
            var future = ShopOpportunityMath.ExpectedMaximum(values, 3);
            Assert.That(ShopOpportunityMath.Score(future, 0, economy.Compare(gold, remaining).TotalDelta, .7m),
                Is.EqualTo((decimal)expected).Within(.001m));
        }

        [Test]
        public void HypotheticalPurchaseUsesSharedAttributeAndUnitRequirements()
        {
            var o = Observe(20, 1, board: new[] { "a" }, owned: new[] { ("a", "d0", 1, 1) }, synergies: true);
            var evaluator = new CompositionValueEvaluator(o);
            var gain = evaluator.CompareHypotheticalPurchase(o.SelectedRoster.Single(x => x.DefinitionId == "d1"),
                2, 2, 20, new CharacterLocation(CharacterLocationKind.Board, 1));
            Assert.That(gain.Comparison.Value.Delta.AttributeSynergy, Is.Positive);
            Assert.That(gain.Comparison.Value.Delta.CompletedUnitSynergy, Is.Positive);
            Assert.That(o.Board.Slots.Count(x => x.IsOccupied), Is.EqualTo(1));
        }

        [Test]
        public void BothPhysicalAreasFullExcludePlacementsEvenWhenDirectMergeCanExecute()
        {
            var board = Enumerable.Range(0, 9).Select(i => "b" + i).ToArray();
            var hand = Enumerable.Range(0, 5).Select(i => "h" + i).ToArray();
            var owners = board.Select((id, i) => (id, "d" + i, 1, 1))
                .Concat(hand.Select(id => (id, "d0", 1, 1))).ToArray();
            var o = Observe(20, 1, level: 9, counts: new[] { 9, 0, 0, 0, 0 }, board: board, hand: hand,
                owned: owners, offers: new[] { Offer("a", "d0") });
            var destination = new CharacterLocation(CharacterLocationKind.Board, 0);
            Assert.That(new CharacterPurchasePreview(o).PreviewHypothetical(o.SelectedRoster[0], 1, 1, 20,
                destination).MergeStageCount, Is.EqualTo(1));
            var result = Evaluator().Evaluate(o);
            Assert.That(result.CurrentOpportunity, Is.Zero);
            Assert.That(result.FutureMass.Value.NoReachablePlacement, Is.EqualTo(1m).Within(ShopOpportunityMath.ProbabilityTolerance));
        }

        [Test]
        public void CountsUseAReadOnlySnapshotAndRepeatedEvaluationIsDeterministic()
        {
            int[] counts = { 1, 1, 1, 1, 1 };
            var o = Observe(20, 1, counts: counts);
            counts[0] = 42;
            var published = o.Shop.CostWeights.CharactersPerCost;
            Assert.That(published[0], Is.EqualTo(1));
            Assert.Throws<NotSupportedException>(() => ((IList<int>)published)[0] = 42);
            var evaluator = Evaluator();
            var first = evaluator.Evaluate(o, true);
            var second = evaluator.Evaluate(o, true);
            Assert.That(second.Score, Is.EqualTo(first.Score));
            Assert.That(second.Details.Select(x => (x.DefinitionId, x.Cost, x.Benefit)),
                Is.EqualTo(first.Details.Select(x => (x.DefinitionId, x.Cost, x.Benefit))));
        }

        private static ShopOffer Offer(string id, string definition, int cost = 1,
            ShopOfferStatus status = ShopOfferStatus.Available) => new ShopOffer(id, ShopOfferKind.Character, definition, cost, cost, status);

        private static GameObservation Observe(int gold, int? price, int[] counts = null, ShopOffer[] offers = null,
            bool stats = true, int slots = 3, int level = 3, string[] board = null, string[] hand = null,
            (string Id, string Definition, int Cost, int Star)[] owned = null,
            int baseReward = 3, bool item = false, bool unselected = false, bool synergies = false)
        {
            counts ??= new[] { 1, 1, 1, 1, 1 };
            var defs = Enumerable.Range(0, counts.Sum()).Select(i => new CharacterDefinition("d" + i, "d" + i, 1,
                synergies && i < 2 ? new[] { "Fire" } : Array.Empty<string>(),
                synergies && i < 2 ? new[] { "Pair" } : Array.Empty<string>())).ToArray();
            var byId = defs.ToDictionary(d => d.DefinitionId);
            var instances = (owned ?? Array.Empty<(string, string, int, int)>())
                .Select(x => new CharacterInstance(x.Item1, byId[x.Item2], x.Item3, x.Item4)).ToArray();
            var boardIds = new string[9];
            (board ?? Array.Empty<string>()).CopyTo(boardIds, 0);
            var h = HandState.CreateStandard();
            for (int i = 0; i < (hand?.Length ?? 0); i++)
                Assert.That(h.TryWithSlot(i, HandSlotContent.Character(hand[i]), out h), Is.True);
            var stage = new StageProgressConfig(5, 5, item ? 1 : 5, 4, 0, 0, 0, 1, 0, 0, 3, 6, 0, 0, 10, 4, 2, true);
            var state = StageProgressRules.CreateInitialState(stage);
            if (!unselected) state = StageProgressRules.BeginCurrentEncounter(state, stage).NextState;
            if (item) state = StageSelectionRules.Select(state, stage, StageChoice.SelectB).NextState;
            var config = new CharacterShopConfiguration(counts, new[] { 15, 15, 15, 15, 15 },
                new[] { new[] { 1, 1, 1, 1, 1 } }, new[] { slots });
            var snapshot = new ShopState(item ? ShopKind.Item : ShopKind.Character, offers ?? Array.Empty<ShopOffer>(),
                Math.Max(slots, offers?.Length ?? 0), 1, 1);
            var stat = stats ? new CharacterStatDefinition(Enumerable.Range(1, 5).Select(c =>
            {
                var options = new[] { new CharacterStatOption(c + 2, c, 1) };
                return new CharacterCostStats(c, options, options, options);
            })) : null;
            var source = new ObservationSource(state, stage, new EconomyState(gold, 0, level, 12, gold, 0, level, 30, 30),
                new EconomyRulesConfiguration(4, 2, 1, Enumerable.Range(1, 11), 10, 1, 3, baseReward, 1, 0),
                new BoardState(3, 3, boardIds), h, new CharacterOwnershipState(instances),
                defs, defs, defs, Array.Empty<ItemDefinition>(), shop: snapshot, enemyBoard: EnemyBoardState.CreateEmpty(),
                enemyDefinitions: Array.Empty<EnemyDefinition>(), characterStatSettings: stat, characterShopConfig: config,
                stageRewardConfig: new StageRewardConfiguration(5, 2), shopRerollGoldCost: price);
            var result = ObservationBuilder.Build(source);
            Assert.That(result.Succeeded, Is.True, string.Join("; ", result.Errors));
            return result.Observation;
        }
    }
}
