using System;
using System.Collections.Generic;
using ProjectNST.GameCore.Characters;
using ProjectNST.GameCore.Randomness;

namespace ProjectNST.GameCore.Board
{
    /// <summary>One-step, three-copy character merge. Candidate order is Board index then Hand index.</summary>
    public static class MergeRules
    {
        public static CharacterMergeResult TryMerge(BoardState board, HandState hand,
            CharacterOwnershipState ownership, CharacterDefinition definition, int targetStar,
            CharacterStatDefinition stats, Pcg32State statsRandomState)
        {
            if (board == null || hand == null || ownership == null ||
                !BoardHandStateValidator.Validate(board, hand, ownership).IsValid)
                return Fail(board, hand, ownership, CharacterMergeFailureReason.InvalidInputState, statsRandomState);
            if (definition == null || string.IsNullOrWhiteSpace(definition.DefinitionId))
                return Fail(board, hand, ownership, CharacterMergeFailureReason.InvalidDefinitionId, statsRandomState);
            if (targetStar < 1 || targetStar > 3)
                return Fail(board, hand, ownership, CharacterMergeFailureReason.InvalidStar, statsRandomState);
            if (targetStar == 3)
                return Fail(board, hand, ownership, CharacterMergeFailureReason.MaximumStarReached, statsRandomState);
            if (stats == null)
                return Fail(board, hand, ownership, CharacterMergeFailureReason.InvalidStatsDefinition, statsRandomState);

            Pcg32RandomSource workingRandom;
            try { workingRandom = Pcg32RandomSource.FromState(statsRandomState); }
            catch (ArgumentException)
            {
                return Fail(board, hand, ownership, CharacterMergeFailureReason.InvalidRandomState, statsRandomState);
            }

            var candidates = new List<Candidate>();
            for (int slot = 0; slot < board.SlotCount; slot++)
            {
                board.TryGetCharacterIdAt(slot, out string id);
                AddCandidate(candidates, ownership, id, CharacterLocationKind.Board, slot, definition.DefinitionId, targetStar);
            }
            for (int slot = 0; slot < hand.SlotCount; slot++)
            {
                hand.TryGetSlot(slot, out HandSlotContent content);
                if (content.Kind == HandSlotContentKind.Character)
                    AddCandidate(candidates, ownership, content.CharacterInstanceId, CharacterLocationKind.Hand,
                        slot, definition.DefinitionId, targetStar);
            }
            if (candidates.Count < 3)
                return Fail(board, hand, ownership, CharacterMergeFailureReason.InsufficientCandidates, statsRandomState);

            Candidate survivor = candidates[0];
            Candidate consumedA = candidates[1];
            Candidate consumedB = candidates[2];
            if (!ownership.TryCreateOwnedInstanceCopy(survivor.InstanceId, out CharacterInstance upgraded))
                return Fail(board, hand, ownership, CharacterMergeFailureReason.InvalidInputState, statsRandomState);

            if (!upgraded.TrySetStar(targetStar + 1, definition, stats, workingRandom))
                return Fail(board, hand, ownership, CharacterMergeFailureReason.StatsRerollFailed, statsRandomState);

            if (!ownership.TryWithReplaced(upgraded, out CharacterOwnershipState nextOwnership))
                return Fail(board, hand, ownership, CharacterMergeFailureReason.InvalidInputState, statsRandomState);
            if (!nextOwnership.TryWithRemoved(consumedA.InstanceId, out nextOwnership) ||
                !nextOwnership.TryWithRemoved(consumedB.InstanceId, out nextOwnership))
                return Fail(board, hand, ownership, CharacterMergeFailureReason.InvalidInputState, statsRandomState);

            BoardState nextBoard = board;
            HandState nextHand = hand;
            ClearLocation(consumedA, ref nextBoard, ref nextHand);
            ClearLocation(consumedB, ref nextBoard, ref nextHand);

            if (!BoardHandStateValidator.Validate(nextBoard, nextHand, nextOwnership).IsValid)
                return Fail(board, hand, ownership, CharacterMergeFailureReason.InvalidResultState, statsRandomState);

            return new CharacterMergeResult(true, CharacterMergeFailureReason.None,
                nextBoard, nextHand, nextOwnership, survivor.InstanceId,
                new[] { consumedA.InstanceId, consumedB.InstanceId }, targetStar,
                targetStar + 1, workingRandom.CaptureState());
        }

        private static void AddCandidate(List<Candidate> candidates, CharacterOwnershipState ownership,
            string instanceId, CharacterLocationKind locationKind, int slot, string definitionId, int star)
        {
            if (instanceId == null || !ownership.TryGet(instanceId, out CharacterInstanceView instance)) return;
            if (!string.Equals(instance.DefinitionId, definitionId, StringComparison.Ordinal) || instance.Star != star) return;
            candidates.Add(new Candidate(instanceId, new CharacterLocation(locationKind, slot)));
        }

        private static void ClearLocation(Candidate candidate, ref BoardState board, ref HandState hand)
        {
            if (candidate.Location.Kind == CharacterLocationKind.Board)
                board.TryWithCharacterAt(candidate.Location.SlotIndex, null, out board);
            else
                hand.TryWithSlot(candidate.Location.SlotIndex, HandSlotContent.Empty(), out hand);
        }

        private static CharacterMergeResult Fail(BoardState board, HandState hand,
            CharacterOwnershipState ownership, CharacterMergeFailureReason reason, Pcg32State randomState)
        {
            return new CharacterMergeResult(false, reason, board, hand, ownership, null,
                Array.Empty<string>(), 0, 0, randomState);
        }

        private readonly struct Candidate
        {
            public string InstanceId { get; }
            public CharacterLocation Location { get; }
            public Candidate(string instanceId, CharacterLocation location)
            { InstanceId = instanceId; Location = location; }
        }
    }
}
