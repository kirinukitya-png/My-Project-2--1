using System;
using System.Collections.Generic;
using ProjectNST.GameCore.Characters;

namespace ProjectNST.GameCore.Board
{
    /// <summary>Cross-state invariants for a committed Board, Hand, and owned-instance snapshot.</summary>
    public static class BoardHandStateValidator
    {
        public static BoardHandStateValidationResult Validate(BoardState board, HandState hand,
            CharacterOwnershipState ownership)
        {
            var errors = new List<string>();
            if (board == null) errors.Add("Board state is missing.");
            if (hand == null) errors.Add("Hand state is missing.");
            if (ownership == null) errors.Add("Character ownership state is missing.");
            if (board == null || hand == null || ownership == null)
                return new BoardHandStateValidationResult(errors);

            if (board.SlotCount != (long)board.Columns * board.Rows)
                errors.Add("Board slot count does not match its dimensions.");

            var occurrences = new Dictionary<string, List<CharacterLocation>>(StringComparer.Ordinal);
            var boardDefinitionIds = new HashSet<string>(StringComparer.Ordinal);

            for (int i = 0; i < board.SlotCount; i++)
            {
                board.TryGetCharacterIdAt(i, out string id);
                if (id == null) continue;
                AddLocation(occurrences, id, new CharacterLocation(CharacterLocationKind.Board, i));
                if (!ownership.TryGet(id, out CharacterInstanceView instance))
                {
                    errors.Add("Board slot " + i + " references unowned CharacterInstance '" + id + "'.");
                    continue;
                }
                if (!boardDefinitionIds.Add(instance.DefinitionId))
                    errors.Add("Board contains duplicate DefinitionId '" + instance.DefinitionId + "'.");
            }

            for (int i = 0; i < hand.SlotCount; i++)
            {
                hand.TryGetSlot(i, out HandSlotContent content);
                if (content == null)
                {
                    errors.Add("Hand slot " + i + " has no valid content value.");
                    continue;
                }

                switch (content.Kind)
                {
                    case HandSlotContentKind.Empty:
                        if (content.CharacterInstanceId != null || content.ItemDefinitionId != null)
                            errors.Add("Empty hand slot " + i + " contains a value.");
                        break;
                    case HandSlotContentKind.Item:
                        if (string.IsNullOrWhiteSpace(content.ItemDefinitionId) || content.CharacterInstanceId != null)
                            errors.Add("Hand slot " + i + " has invalid Item content.");
                        break;
                    case HandSlotContentKind.Character:
                        if (string.IsNullOrWhiteSpace(content.CharacterInstanceId) || content.ItemDefinitionId != null)
                        {
                            errors.Add("Hand slot " + i + " has invalid Character content.");
                            break;
                        }
                        AddLocation(occurrences, content.CharacterInstanceId,
                            new CharacterLocation(CharacterLocationKind.Hand, i));
                        if (!ownership.Contains(content.CharacterInstanceId))
                            errors.Add("Hand slot " + i + " references unowned CharacterInstance '" +
                                content.CharacterInstanceId + "'.");
                        break;
                    default:
                        errors.Add("Hand slot " + i + " has an unsupported content kind.");
                        break;
                }
            }

            foreach (KeyValuePair<string, List<CharacterLocation>> pair in occurrences)
            {
                if (pair.Value.Count > 1)
                    errors.Add("CharacterInstance '" + pair.Key + "' is placed in more than one Board/Hand slot.");
            }

            foreach (CharacterInstanceView instance in ownership.GetOwnedInstances())
            {
                if (!occurrences.TryGetValue(instance.InstanceId, out List<CharacterLocation> locations) || locations.Count == 0)
                    errors.Add("Owned CharacterInstance '" + instance.InstanceId + "' is not placed in Board or Hand.");
                else if (locations.Count != 1)
                    errors.Add("Owned CharacterInstance '" + instance.InstanceId + "' must occupy exactly one slot.");
            }

            return new BoardHandStateValidationResult(errors);
        }

        /// <summary>Finds a location only when the ID appears exactly once across both containers.</summary>
        public static bool TryFindLocation(BoardState board, HandState hand, string instanceId,
            out CharacterLocation location)
        {
            location = default;
            if (board == null || hand == null || string.IsNullOrWhiteSpace(instanceId)) return false;

            bool found = false;
            CharacterLocation candidate = default;
            int count = 0;
            for (int i = 0; i < board.SlotCount; i++)
            {
                board.TryGetCharacterIdAt(i, out string id);
                if (!string.Equals(id, instanceId, StringComparison.Ordinal)) continue;
                candidate = new CharacterLocation(CharacterLocationKind.Board, i);
                found = true;
                count++;
            }
            for (int i = 0; i < hand.SlotCount; i++)
            {
                hand.TryGetSlot(i, out HandSlotContent content);
                if (content.Kind != HandSlotContentKind.Character ||
                    !string.Equals(content.CharacterInstanceId, instanceId, StringComparison.Ordinal)) continue;
                candidate = new CharacterLocation(CharacterLocationKind.Hand, i);
                found = true;
                count++;
            }

            if (!found || count != 1) return false;
            location = candidate;
            return true;
        }

        private static void AddLocation(Dictionary<string, List<CharacterLocation>> occurrences,
            string id, CharacterLocation location)
        {
            if (!occurrences.TryGetValue(id, out List<CharacterLocation> locations))
            {
                locations = new List<CharacterLocation>();
                occurrences.Add(id, locations);
            }
            locations.Add(location);
        }
    }
}
