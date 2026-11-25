using System;
using System.Collections.Generic;
using ProjectNST.GameCore.Board;
using ProjectNST.GameCore.Characters;
using ProjectNST.GameCore.Economy;
using ProjectNST.GameCore.Randomness;

namespace ProjectNST.GameCore.Shop
{
    /// <summary>
    /// Immutable transaction calculations integrating shop purchase/sale with Board, Hand and ownership.
    /// Approved rules change: only existing ★1×2 plus the purchased ★1 starts a merge; a second merge is
    /// permitted only because that first merge produced a ★2. It never uses a purchased ★1 to directly
    /// upgrade existing ★2 copies, and it preserves surplus candidates. Record this policy in a future RulesVersion.
    /// </summary>
    public static class BoardShopTransactionRules
    {
        public static BoardShopTransactionResult TryPurchaseCharacter(ShopState shop, string offerId,
            IReadOnlyList<CharacterDefinition> definitions, AssignedCharacterCosts assignedCosts,
            EconomyState economy, CharacterPool pool, BoardState board, HandState hand,
            CharacterOwnershipState ownership, CharacterInstanceFactory instanceFactory,
            CharacterStatDefinition statSettings, Pcg32State statsRandomState,
            CharacterLocation placement, bool ownershipScopeMatchesPool = false)
        {
            if (!TryValidateInputs(shop, economy, pool, board, hand, ownership, instanceFactory,
                    out BoardShopTransactionFailureReason inputFailure))
                return Fail(inputFailure, shop, economy, pool, board, hand, ownership, instanceFactory, statsRandomState);
            if (definitions == null || assignedCosts == null)
                return Fail(BoardShopTransactionFailureReason.MissingDefinition, shop, economy, pool, board, hand, ownership, instanceFactory, statsRandomState);
            if (!BoardHandStateValidator.Validate(board, hand, ownership).IsValid)
                return Fail(BoardShopTransactionFailureReason.InvalidInputState, shop, economy, pool, board, hand, ownership, instanceFactory, statsRandomState);

            if (!TryFindAvailableCharacterOffer(shop, offerId, out ShopOffer offer))
                return Fail(BoardShopTransactionFailureReason.InvalidOffer, shop, economy, pool, board, hand, ownership, instanceFactory, statsRandomState);
            CharacterDefinition definition = FindUniqueDefinition(definitions, offer.DefinitionId);
            if (definition == null || !assignedCosts.Costs.TryGetValue(offer.DefinitionId, out int runCost))
                return Fail(BoardShopTransactionFailureReason.MissingDefinition, shop, economy, pool, board, hand, ownership, instanceFactory, statsRandomState);
            if (offer.CharacterCost != runCost || offer.DisplayPrice != runCost)
                return Fail(BoardShopTransactionFailureReason.CostMismatch, shop, economy, pool, board, hand, ownership, instanceFactory, statsRandomState);
            if (economy.Gold < runCost)
                return Fail(BoardShopTransactionFailureReason.InsufficientGold, shop, economy, pool, board, hand, ownership, instanceFactory, statsRandomState);
            if (!pool.Entries.TryGetValue(offer.DefinitionId, out CharacterPoolEntry entry))
                return Fail(BoardShopTransactionFailureReason.PoolEntryMissing, shop, economy, pool, board, hand, ownership, instanceFactory, statsRandomState);
            if (entry.ReservedCopies < 1)
                return Fail(BoardShopTransactionFailureReason.MissingReservation, shop, economy, pool, board, hand, ownership, instanceFactory, statsRandomState);
            if (ownershipScopeMatchesPool && !PoolOwnershipMatches(pool, ownership))
                return Fail(BoardShopTransactionFailureReason.PoolOwnershipMismatch, shop, economy, pool, board, hand, ownership, instanceFactory, statsRandomState);
            if (statSettings == null)
                return Fail(BoardShopTransactionFailureReason.InvalidStatsConfiguration, shop, economy, pool, board, hand, ownership, instanceFactory, statsRandomState);
            try { Pcg32RandomSource.ValidateState(statsRandomState); }
            catch (ArgumentException)
            {
                return Fail(BoardShopTransactionFailureReason.InvalidRandomState, shop, economy, pool, board, hand, ownership, instanceFactory, statsRandomState);
            }

            bool isBoardDestination = placement.Kind == CharacterLocationKind.Board;
            bool isHandDestination = placement.Kind == CharacterLocationKind.Hand;
            if (!isBoardDestination && !isHandDestination)
                return Fail(BoardShopTransactionFailureReason.InvalidPlacement, shop, economy, pool, board, hand, ownership, instanceFactory, statsRandomState);
            if (isBoardDestination)
            {
                if (!board.TryGetCharacterIdAt(placement.SlotIndex, out _))
                    return Fail(BoardShopTransactionFailureReason.InvalidPlacement, shop, economy, pool, board, hand, ownership, instanceFactory, statsRandomState);
            }
            else
            {
                if (!hand.TryGetSlot(placement.SlotIndex, out _))
                    return Fail(BoardShopTransactionFailureReason.InvalidPlacement, shop, economy, pool, board, hand, ownership, instanceFactory, statsRandomState);
            }

            List<Candidate> oneStarCandidates = FindCandidates(board, hand, ownership, definition.DefinitionId, 1);
            if (oneStarCandidates.Count < 2)
            {
                if (isBoardDestination)
                {
                    board.TryGetCharacterIdAt(placement.SlotIndex, out string atBoard);
                    if (atBoard != null)
                        return Fail(BoardShopTransactionFailureReason.PlacementOccupied, shop, economy, pool, board, hand, ownership, instanceFactory, statsRandomState);
                    if (BoardContainsDefinition(board, ownership, definition.DefinitionId))
                        return Fail(BoardShopTransactionFailureReason.DuplicateDefinitionOnBoard, shop, economy, pool, board, hand, ownership, instanceFactory, statsRandomState);
                }
                else
                {
                    hand.TryGetSlot(placement.SlotIndex, out HandSlotContent handContent);
                    if (!handContent.IsEmpty)
                        return Fail(BoardShopTransactionFailureReason.PlacementOccupied, shop, economy, pool, board, hand, ownership, instanceFactory, statsRandomState);
                }
                return TryNormalPurchase(shop, offerId, definitions, assignedCosts, economy, pool, board, hand,
                    ownership, instanceFactory, statSettings, statsRandomState, placement, definition, ownershipScopeMatchesPool);
            }

            return TryMergePurchase(shop, offer, definition, runCost, economy, pool, board, hand, ownership,
                instanceFactory, statSettings, statsRandomState, placement, oneStarCandidates, entry, ownershipScopeMatchesPool);
        }

        public static BoardShopTransactionResult TrySellCharacter(string instanceId,
            EconomyState economy, ShopState shop, CharacterPool pool, BoardState board, HandState hand,
            CharacterOwnershipState ownership, CharacterInstanceFactory instanceFactory,
            Pcg32State statsRandomState, bool ownershipScopeMatchesPool = false)
        {
            if (!TryValidateInputs(shop, economy, pool, board, hand, ownership, instanceFactory,
                    out BoardShopTransactionFailureReason inputFailure))
                return Fail(inputFailure, shop, economy, pool, board, hand, ownership, instanceFactory, statsRandomState);
            BoardHandStateValidationResult inputValidation = BoardHandStateValidator.Validate(board, hand, ownership);
            if (!inputValidation.IsValid || (ownershipScopeMatchesPool && !PoolOwnershipMatches(pool, ownership)))
                return Fail(BoardShopTransactionFailureReason.InvalidInputState, shop, economy, pool, board, hand, ownership, instanceFactory, statsRandomState);
            if (string.IsNullOrWhiteSpace(instanceId) || !ownership.TryGet(instanceId, out CharacterInstanceView target))
                return Fail(BoardShopTransactionFailureReason.NotOwned, shop, economy, pool, board, hand, ownership, instanceFactory, statsRandomState);
            if (target.HasTemporaryStar)
                return Fail(BoardShopTransactionFailureReason.TemporaryStarCannotBeSold, shop, economy, pool, board, hand, ownership, instanceFactory, statsRandomState);
            if (!BoardHandStateValidator.TryFindLocation(board, hand, instanceId, out CharacterLocation location))
                return Fail(BoardShopTransactionFailureReason.InvalidInputState, shop, economy, pool, board, hand, ownership, instanceFactory, statsRandomState);
            if (!pool.Entries.TryGetValue(target.DefinitionId, out CharacterPoolEntry entry))
                return Fail(BoardShopTransactionFailureReason.PoolEntryMissing, shop, economy, pool, board, hand, ownership, instanceFactory, statsRandomState);

            int poolCopies = target.PoolCopies;
            int saleValueCopies = CopiesForStar(target.Star);
            if (entry.OwnedCopies < poolCopies || SumOwnedCopies(ownership, target.DefinitionId) < poolCopies)
                return Fail(BoardShopTransactionFailureReason.InsufficientOwnedCopies, shop, economy, pool, board, hand, ownership, instanceFactory, statsRandomState);
            long goldGain = (long)target.RunCost * saleValueCopies;
            long goldAfter = (long)economy.Gold + goldGain;
            if (goldGain > int.MaxValue || goldAfter > int.MaxValue)
                return Fail(BoardShopTransactionFailureReason.GoldOverflow, shop, economy, pool, board, hand, ownership, instanceFactory, statsRandomState);

            if (!ownership.TryWithRemoved(instanceId, out CharacterOwnershipState nextOwnership))
                return Fail(BoardShopTransactionFailureReason.InvalidInputState, shop, economy, pool, board, hand, ownership, instanceFactory, statsRandomState);
            BoardState nextBoard = board;
            HandState nextHand = hand;
            if (location.Kind == CharacterLocationKind.Board)
                nextBoard.TryWithCharacterAt(location.SlotIndex, null, out nextBoard);
            else
                nextHand.TryWithSlot(location.SlotIndex, HandSlotContent.Empty(), out nextHand);

            CharacterPool nextPool;
            try
            {
                nextPool = ReplacePoolEntry(pool, new CharacterPoolEntry(entry.DefinitionId,
                    checked(entry.RemainingCopies + poolCopies), entry.InitialStockLimit,
                    entry.ReservedCopies, entry.OwnedCopies - poolCopies));
            }
            catch (Exception)
            {
                return Fail(BoardShopTransactionFailureReason.PoolOwnershipMismatch, shop, economy, pool, board, hand, ownership, instanceFactory, statsRandomState);
            }
            if (!BoardHandStateValidator.Validate(nextBoard, nextHand, nextOwnership).IsValid ||
                (ownershipScopeMatchesPool && !PoolOwnershipMatches(nextPool, nextOwnership)))
                return Fail(BoardShopTransactionFailureReason.InvalidInputState, shop, economy, pool, board, hand, ownership, instanceFactory, statsRandomState);

            return Succeed(BoardShopTransactionKind.Sale, economy.WithGold((int)goldAfter), shop, nextPool,
                nextBoard, nextHand, nextOwnership, instanceFactory, statsRandomState, (int)goldGain,
                poolCopies, poolCopies, 0, -poolCopies, ownershipScopeMatchesPool, Array.Empty<CharacterMergeStageSummary>(), null);
        }

        private static BoardShopTransactionResult TryNormalPurchase(ShopState shop, string offerId,
            IReadOnlyList<CharacterDefinition> definitions, AssignedCharacterCosts costs, EconomyState economy,
            CharacterPool pool, BoardState board, HandState hand, CharacterOwnershipState ownership,
            CharacterInstanceFactory factory, CharacterStatDefinition stats, Pcg32State randomState,
            CharacterLocation placement, CharacterDefinition definition, bool ownershipScopeMatchesPool)
        {
            PlacementApproval approval = new PlacementApproval(true);
            Pcg32RandomSource random;
            try { random = Pcg32RandomSource.FromState(randomState); }
            catch (ArgumentException)
            {
                return Fail(BoardShopTransactionFailureReason.InvalidRandomState, shop, economy, pool, board, hand, ownership, factory, randomState);
            }
            CharacterPurchaseResult purchase = ShopTransactionRules.TryPurchaseCharacter(shop, offerId,
                definitions, costs, economy, pool, factory, stats, random, approval);
            if (!purchase.Succeeded)
                return Fail(MapPurchaseFailure(purchase.FailureReason), shop, economy, pool, board, hand, ownership, factory, randomState);

            if (!ownership.TryWithAdded(purchase.PurchasedCharacter, out CharacterOwnershipState nextOwnership))
                return Fail(BoardShopTransactionFailureReason.InvalidInputState, shop, economy, pool, board, hand, ownership, factory, randomState);
            BoardState nextBoard = board;
            HandState nextHand = hand;
            if (placement.Kind == CharacterLocationKind.Board)
                nextBoard.TryWithCharacterAt(placement.SlotIndex, purchase.PurchasedCharacter.InstanceId, out nextBoard);
            else
                nextHand.TryWithSlot(placement.SlotIndex, HandSlotContent.Character(purchase.PurchasedCharacter.InstanceId), out nextHand);

            if (!BoardHandStateValidator.Validate(nextBoard, nextHand, nextOwnership).IsValid ||
                (ownershipScopeMatchesPool && !PoolOwnershipMatches(purchase.NextPool, nextOwnership)))
                return Fail(BoardShopTransactionFailureReason.InvalidInputState, shop, economy, pool, board, hand, ownership, factory, randomState);

            return Succeed(BoardShopTransactionKind.NormalPurchase, purchase.NextEconomy, purchase.NextShop,
                purchase.NextPool, nextBoard, nextHand, nextOwnership, purchase.NextInstanceFactory,
                purchase.NextStatsRandomState, -purchase.PurchasedCharacter.RunCost, 1,
                0, -1, 1, ownershipScopeMatchesPool, Array.Empty<CharacterMergeStageSummary>(), purchase.PurchasedCharacter.InstanceId);
        }

        private static BoardShopTransactionResult TryMergePurchase(ShopState shop, ShopOffer offer,
            CharacterDefinition definition, int runCost, EconomyState economy, CharacterPool pool,
            BoardState board, HandState hand, CharacterOwnershipState ownership,
            CharacterInstanceFactory factory, CharacterStatDefinition stats, Pcg32State initialRandomState,
            CharacterLocation requestedPlacement, List<Candidate> oneStarCandidates, CharacterPoolEntry poolEntry,
            bool ownershipScopeMatchesPool)
        {
            Pcg32RandomSource workingRandom;
            try { workingRandom = Pcg32RandomSource.FromState(initialRandomState); }
            catch (ArgumentException)
            {
                return Fail(BoardShopTransactionFailureReason.InvalidRandomState, shop, economy, pool, board, hand, ownership, factory, initialRandomState);
            }

            Candidate first = oneStarCandidates[0];
            Candidate second = oneStarCandidates[1];
            if (!ownership.TryGet(first.InstanceId, out CharacterInstanceView firstView) ||
                !ownership.TryGet(second.InstanceId, out CharacterInstanceView secondView))
                return Fail(BoardShopTransactionFailureReason.MergeValidationFailed, shop, economy, pool, board, hand, ownership, factory, initialRandomState);
            int mergedPoolCopies;
            try { mergedPoolCopies = checked(firstView.PoolCopies + secondView.PoolCopies + 1); }
            catch (OverflowException)
            { return Fail(BoardShopTransactionFailureReason.MergeValidationFailed, shop, economy, pool, board, hand, ownership, factory, initialRandomState); }
            if (!ownership.TryCreateOwnedInstanceCopy(first.InstanceId, out CharacterInstance survivor) ||
                !survivor.TrySetStar(2, definition, stats, workingRandom) ||
                !survivor.TrySetPoolCopies(mergedPoolCopies))
                return Fail(BoardShopTransactionFailureReason.StatsRerollFailed, shop, economy, pool, board, hand, ownership, factory, initialRandomState);
            if (!ownership.TryWithReplaced(survivor, out CharacterOwnershipState nextOwnership) ||
                !nextOwnership.TryWithRemoved(second.InstanceId, out nextOwnership))
                return Fail(BoardShopTransactionFailureReason.MergeValidationFailed, shop, economy, pool, board, hand, ownership, factory, initialRandomState);

            BoardState nextBoard = board;
            HandState nextHand = hand;
            ClearCandidate(second, ref nextBoard, ref nextHand);
            if (!BoardHandStateValidator.Validate(nextBoard, nextHand, nextOwnership).IsValid)
                return Fail(BoardShopTransactionFailureReason.MergeValidationFailed, shop, economy, pool, board, hand, ownership, factory, initialRandomState);

            var stages = new List<CharacterMergeStageSummary>
            {
                new CharacterMergeStageSummary(1, 2, first.InstanceId,
                    new[] { second.InstanceId }, true)
            };
            int stageTwoCount = FindCandidates(nextBoard, nextHand, nextOwnership, definition.DefinitionId, 2).Count;
            if (stageTwoCount >= 3)
            {
                CharacterMergeResult secondMerge = MergeRules.TryMerge(nextBoard, nextHand, nextOwnership,
                    definition, 2, stats, workingRandom.CaptureState());
                if (!secondMerge.Succeeded)
                    return Fail(BoardShopTransactionFailureReason.MergeValidationFailed, shop, economy, pool, board, hand, ownership, factory, initialRandomState);
                stages.Add(new CharacterMergeStageSummary(2, 3, secondMerge.SurvivingInstanceId,
                    secondMerge.ConsumedInstanceIds, false));
                nextBoard = secondMerge.NextBoard;
                nextHand = secondMerge.NextHand;
                nextOwnership = secondMerge.NextOwnership;
                workingRandom = Pcg32RandomSource.FromState(secondMerge.NextStatsRandomState);
            }

            // Only move the final surviving instance when the requested Board slot was empty at transaction start.
            if (requestedPlacement.Kind == CharacterLocationKind.Board &&
                board.TryGetCharacterIdAt(requestedPlacement.SlotIndex, out string originalDestination) &&
                originalDestination == null)
            {
                string finalSurvivor = stages[stages.Count - 1].SurvivingInstanceId;
                if (!BoardHandStateValidator.TryFindLocation(nextBoard, nextHand, finalSurvivor, out CharacterLocation source))
                    return Fail(BoardShopTransactionFailureReason.MergeValidationFailed, shop, economy, pool, board, hand, ownership, factory, initialRandomState);
                if (source.Kind == CharacterLocationKind.Board)
                    nextBoard.TryWithCharacterAt(source.SlotIndex, null, out nextBoard);
                else
                    nextHand.TryWithSlot(source.SlotIndex, HandSlotContent.Empty(), out nextHand);
                if (!nextBoard.TryWithCharacterAt(requestedPlacement.SlotIndex, finalSurvivor, out nextBoard))
                    return Fail(BoardShopTransactionFailureReason.InvalidPlacement, shop, economy, pool, board, hand, ownership, factory, initialRandomState);
            }

            ShopState nextShop;
            CharacterPool nextPool;
            EconomyState nextEconomy;
            try
            {
                nextShop = shop.WithReplacedOffer(new ShopOffer(offer.OfferId, offer.Kind, offer.DefinitionId,
                    offer.DisplayPrice, offer.CharacterCost, ShopOfferStatus.Purchased));
                nextPool = ReplacePoolEntry(pool, new CharacterPoolEntry(poolEntry.DefinitionId,
                    poolEntry.RemainingCopies, poolEntry.InitialStockLimit,
                    poolEntry.ReservedCopies - 1, checked(poolEntry.OwnedCopies + 1)));
                nextEconomy = economy.WithGold(economy.Gold - runCost);
            }
            catch (Exception)
            {
                return Fail(BoardShopTransactionFailureReason.InvalidInputState, shop, economy, pool, board, hand, ownership, factory, initialRandomState);
            }

            if (!BoardHandStateValidator.Validate(nextBoard, nextHand, nextOwnership).IsValid ||
                (ownershipScopeMatchesPool && !PoolOwnershipMatches(nextPool, nextOwnership)))
                return Fail(BoardShopTransactionFailureReason.InvalidInputState, shop, economy, pool, board, hand, ownership, factory, initialRandomState);
            return Succeed(BoardShopTransactionKind.MergePurchase, nextEconomy, nextShop, nextPool,
                nextBoard, nextHand, nextOwnership, factory, workingRandom.CaptureState(), -runCost,
                1, 0, -1, 1, ownershipScopeMatchesPool, stages, stages[stages.Count - 1].SurvivingInstanceId);
        }

        private static void ClearCandidate(Candidate candidate, ref BoardState board, ref HandState hand)
        {
            if (candidate.Location.Kind == CharacterLocationKind.Board)
                board.TryWithCharacterAt(candidate.Location.SlotIndex, null, out board);
            else
                hand.TryWithSlot(candidate.Location.SlotIndex, HandSlotContent.Empty(), out hand);
        }

        private static List<Candidate> FindCandidates(BoardState board, HandState hand,
            CharacterOwnershipState ownership, string definitionId, int star)
        {
            var result = new List<Candidate>();
            for (int i = 0; i < board.SlotCount; i++)
            {
                board.TryGetCharacterIdAt(i, out string id);
                AddCandidate(result, ownership, id, definitionId, star, CharacterLocationKind.Board, i);
            }
            for (int i = 0; i < hand.SlotCount; i++)
            {
                hand.TryGetSlot(i, out HandSlotContent content);
                if (content.Kind == HandSlotContentKind.Character)
                    AddCandidate(result, ownership, content.CharacterInstanceId, definitionId, star, CharacterLocationKind.Hand, i);
            }
            return result;
        }

        private static void AddCandidate(List<Candidate> candidates, CharacterOwnershipState ownership,
            string id, string definitionId, int star, CharacterLocationKind kind, int slot)
        {
            if (id != null && ownership.TryGet(id, out CharacterInstanceView view) &&
                string.Equals(view.DefinitionId, definitionId, StringComparison.Ordinal) && view.Star == star)
                candidates.Add(new Candidate(id, new CharacterLocation(kind, slot)));
        }

        private static bool TryValidateInputs(ShopState shop, EconomyState economy, CharacterPool pool,
            BoardState board, HandState hand, CharacterOwnershipState ownership, CharacterInstanceFactory factory,
            out BoardShopTransactionFailureReason reason)
        {
            reason = BoardShopTransactionFailureReason.InvalidInputState;
            return shop != null && economy != null && pool != null && board != null && hand != null &&
                ownership != null && factory != null;
        }

        private static bool TryFindAvailableCharacterOffer(ShopState shop, string offerId, out ShopOffer found)
        {
            found = null;
            if (shop.Kind != ShopKind.Character || string.IsNullOrWhiteSpace(offerId)) return false;
            foreach (ShopOffer offer in shop.Offers)
            {
                if (!string.Equals(offer.OfferId, offerId, StringComparison.Ordinal)) continue;
                found = offer;
                return offer.Kind == ShopOfferKind.Character && offer.Status == ShopOfferStatus.Available;
            }
            return false;
        }

        private static CharacterDefinition FindUniqueDefinition(IReadOnlyList<CharacterDefinition> definitions, string id)
        {
            CharacterDefinition found = null;
            foreach (CharacterDefinition definition in definitions)
            {
                if (definition == null || !string.Equals(definition.DefinitionId, id, StringComparison.Ordinal)) continue;
                if (found != null) return null;
                found = definition;
            }
            return found;
        }

        private static bool BoardContainsDefinition(BoardState board, CharacterOwnershipState ownership, string definitionId)
        {
            for (int i = 0; i < board.SlotCount; i++)
            {
                board.TryGetCharacterIdAt(i, out string id);
                if (id != null && ownership.TryGet(id, out CharacterInstanceView view) &&
                    string.Equals(view.DefinitionId, definitionId, StringComparison.Ordinal)) return true;
            }
            return false;
        }

        private static bool PoolOwnershipMatches(CharacterPool pool, CharacterOwnershipState ownership)
        {
            var copiesByDefinition = new Dictionary<string, long>(StringComparer.Ordinal);
            foreach (CharacterInstanceView instance in ownership.GetOwnedInstances())
            {
                if (!pool.Entries.ContainsKey(instance.DefinitionId) || instance.PoolCopies < 0 || instance.HasTemporaryStar) return false;
                if (!copiesByDefinition.TryGetValue(instance.DefinitionId, out long copies)) copies = 0;
                copiesByDefinition[instance.DefinitionId] = copies + instance.PoolCopies;
            }
            foreach (CharacterPoolEntry entry in pool.Entries.Values)
            {
                copiesByDefinition.TryGetValue(entry.DefinitionId, out long copies);
                if (copies != entry.OwnedCopies) return false;
            }
            return true;
        }

        private static long SumOwnedCopies(CharacterOwnershipState ownership, string definitionId)
        {
            long result = 0;
            foreach (CharacterInstanceView instance in ownership.GetOwnedInstances())
                if (string.Equals(instance.DefinitionId, definitionId, StringComparison.Ordinal))
                    result += instance.PoolCopies;
            return result;
        }

        private static int CopiesForStar(int star)
        {
            switch (star)
            {
                case 1: return 1;
                case 2: return 3;
                case 3: return 9;
                default: return 0;
            }
        }

        private static CharacterPool ReplacePoolEntry(CharacterPool pool, CharacterPoolEntry replacement)
        {
            var entries = new List<CharacterPoolEntry>(pool.Entries.Count);
            foreach (CharacterPoolEntry entry in pool.Entries.Values)
                entries.Add(entry.DefinitionId == replacement.DefinitionId ? replacement : entry);
            return new CharacterPool(entries);
        }

        private static BoardShopTransactionFailureReason MapPurchaseFailure(CharacterPurchaseFailureReason reason)
        {
            switch (reason)
            {
                case CharacterPurchaseFailureReason.InvalidOffer: return BoardShopTransactionFailureReason.InvalidOffer;
                case CharacterPurchaseFailureReason.MissingDefinition: return BoardShopTransactionFailureReason.MissingDefinition;
                case CharacterPurchaseFailureReason.CostMismatch: return BoardShopTransactionFailureReason.CostMismatch;
                case CharacterPurchaseFailureReason.InsufficientGold: return BoardShopTransactionFailureReason.InsufficientGold;
                case CharacterPurchaseFailureReason.MissingReservation: return BoardShopTransactionFailureReason.MissingReservation;
                case CharacterPurchaseFailureReason.InvalidStatsConfiguration: return BoardShopTransactionFailureReason.InvalidStatsConfiguration;
                case CharacterPurchaseFailureReason.PlacementNotApproved: return BoardShopTransactionFailureReason.InvalidPlacement;
                case CharacterPurchaseFailureReason.InstanceIdExhausted: return BoardShopTransactionFailureReason.InstanceIdExhausted;
                default: return BoardShopTransactionFailureReason.InvalidInputState;
            }
        }

        private static BoardShopTransactionResult Succeed(BoardShopTransactionKind kind, EconomyState economy,
            ShopState shop, CharacterPool pool, BoardState board, HandState hand, CharacterOwnershipState ownership,
            CharacterInstanceFactory factory, Pcg32State randomState, int goldDelta, int poolDelta,
            int remainingDelta, int reservedDelta, int ownedDelta, bool poolScopeVerified,
            IEnumerable<CharacterMergeStageSummary> stages, string survivor)
        {
            return new BoardShopTransactionResult(true, BoardShopTransactionFailureReason.None, kind,
                economy, shop, pool, board, hand, ownership, factory, randomState, goldDelta, poolDelta,
                remainingDelta, reservedDelta, ownedDelta, poolScopeVerified, stages, survivor);
        }

        private static BoardShopTransactionResult Fail(BoardShopTransactionFailureReason reason,
            ShopState shop, EconomyState economy, CharacterPool pool, BoardState board, HandState hand,
            CharacterOwnershipState ownership, CharacterInstanceFactory factory, Pcg32State randomState)
        {
            return new BoardShopTransactionResult(false, reason, BoardShopTransactionKind.None,
                economy, shop, pool, board, hand, ownership, factory, randomState, 0, 0, 0, 0, 0, false,
                Array.Empty<CharacterMergeStageSummary>(), null);
        }

        private sealed class PlacementApproval : ICharacterPlacementApproval
        {
            private readonly bool _approved;
            public PlacementApproval(bool approved) => _approved = approved;
            public bool CanAcceptPurchasedCharacter(CharacterInstance character) => _approved && character != null;
        }

        private readonly struct Candidate
        {
            public string InstanceId { get; }
            public CharacterLocation Location { get; }
            public Candidate(string id, CharacterLocation location) { InstanceId = id; Location = location; }
        }
    }
}
