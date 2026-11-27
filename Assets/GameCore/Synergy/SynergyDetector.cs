using System;
using System.Collections.Generic;
using System.Linq;
using ProjectNST.GameCore.Board;
using ProjectNST.GameCore.Characters;
using ProjectNST.GameCore.Observation;

namespace ProjectNST.GameCore.Synergy
{
    /// <summary>
    /// Detects the existing board Unit and Attribute conditions without changing input states
    /// or consuming randomness. Only established Unit groups are returned.
    /// </summary>
    public static class SynergyDetector
    {
        public static SynergyDetectionResult Detect(BoardState board, HandState hand,
            CharacterOwnershipState ownership,
            IEnumerable<CharacterDefinition> selectedRoster,
            IEnumerable<CharacterDefinition> fullCharacterRoster)
            => Detect(board, hand, ownership, selectedRoster, fullCharacterRoster, null);

        internal static SynergyDetectionResult Detect(BoardState board, HandState hand,
            CharacterOwnershipState ownership, IEnumerable<CharacterDefinition> selectedRoster,
            IEnumerable<CharacterDefinition> fullCharacterRoster, RosterRules cachedRules)
        {
            var errors = new List<string>();
            var units = new List<UnitSynergyResult>();
            var attributes = new List<AttributeSynergyResult>();
            if (board == null) errors.Add("Board state is missing.");
            if (hand == null) errors.Add("Hand state is missing.");
            if (ownership == null) errors.Add("Character ownership state is missing.");
            if (selectedRoster == null) errors.Add("Selected roster is missing.");
            if (fullCharacterRoster == null) errors.Add("Full character roster is missing.");
            if (errors.Count > 0) return new SynergyDetectionResult(false, errors, units, attributes);

            if (board.Columns != BoardState.StandardColumns || board.Rows != BoardState.StandardRows)
                errors.Add("Synergy detection requires the standard 3x3 battle board.");
            BoardHandStateValidationResult stateValidation = BoardHandStateValidator.Validate(board, hand, ownership);
            if (!stateValidation.IsValid)
                foreach (string error in stateValidation.Errors) errors.Add(error);

            RosterRules rules = cachedRules ?? BuildRosterRules(selectedRoster, fullCharacterRoster);
            errors.AddRange(rules.Errors);

            var boardParticipants = new List<SynergyParticipant>();
            var unitParticipants = new Dictionary<string, List<SynergyParticipant>>(StringComparer.Ordinal);
            var attributeParticipants = new Dictionary<string, List<SynergyParticipant>>(StringComparer.Ordinal);
            var unitOrder = new List<string>();
            var attributeOrder = new List<string>();

            for (int slot = 0; slot < board.SlotCount; slot++)
            {
                board.TryGetCharacterIdAt(slot, out string instanceId);
                if (instanceId == null) continue;
                if (!ownership.TryGet(instanceId, out CharacterInstanceView instance)) continue;
                if (!rules.Catalog.TryGetValue(instance.DefinitionId, out CharacterDefinition definition))
                {
                    errors.Add("Board slot " + slot + " references unknown DefinitionId '" + instance.DefinitionId + "'.");
                    continue;
                }

                var participant = new SynergyParticipant(definition.DefinitionId, instance.InstanceId, slot);
                boardParticipants.Add(participant);
                AddParticipantTags(definition.Units, participant, unitParticipants, unitOrder);
                AddParticipantTags(definition.Attributes, participant, attributeParticipants, attributeOrder);
            }

            if (errors.Count > 0) return new SynergyDetectionResult(false, errors, units, attributes);

            foreach (string unitName in unitOrder)
            {
                List<SynergyParticipant> participants = unitParticipants[unitName];
                if (participants.Count < 2) continue;
                if (!rules.RequirementsByName.TryGetValue(unitName, out UnitRequirementObservation requirement)) continue;

                var boardMemberIds = new HashSet<string>(StringComparer.Ordinal);
                foreach (SynergyParticipant participant in participants)
                    boardMemberIds.Add(participant.DefinitionId);
                if (!boardMemberIds.SetEquals(requirement.RequiredDefinitionIds)) continue;

                var slots = new List<int>(participants.Count);
                foreach (SynergyParticipant participant in participants) slots.Add(participant.BoardSlotIndex);
                if (!AreConnected(slots, board.Columns)) continue;
                units.Add(new UnitSynergyResult(unitName, participants));
            }

            foreach (string attributeName in attributeOrder)
            {
                List<SynergyParticipant> participants = attributeParticipants[attributeName];
                if (IsSharedAttribute(participants.Count))
                    attributes.Add(new AttributeSynergyResult(attributeName, participants));
            }

            units.Sort(CompareUnits);
            return new SynergyDetectionResult(true, errors, units, attributes);
        }

        internal sealed class RosterRules
        {
            internal IReadOnlyDictionary<string, CharacterDefinition> Catalog { get; }
            internal IReadOnlyDictionary<string, UnitRequirementObservation> RequirementsByName { get; }
            internal IReadOnlyList<UnitRequirementObservation> Requirements { get; }
            internal IReadOnlyList<string> Errors { get; }

            internal RosterRules(Dictionary<string, CharacterDefinition> catalog,
                Dictionary<string, UnitRequirementObservation> requirements, List<string> errors)
            {
                Catalog = catalog;
                RequirementsByName = requirements;
                Requirements = Array.AsReadOnly(requirements.Values.OrderBy(x => x.UnitName, StringComparer.Ordinal).ToArray());
                Errors = errors.AsReadOnly();
            }
        }

        internal static RosterRules BuildRosterRules(IEnumerable<CharacterDefinition> selectedRoster,
            IEnumerable<CharacterDefinition> fullCharacterRoster)
        {
            var catalog = new Dictionary<string, CharacterDefinition>(StringComparer.Ordinal);
            var selectedCatalog = new Dictionary<string, CharacterDefinition>(StringComparer.Ordinal);
            var fullMembers = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            var selectedMembers = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            var requirements = new Dictionary<string, UnitRequirementObservation>(StringComparer.Ordinal);
            var errors = new List<string>();
            AddDefinitions(fullCharacterRoster, catalog, new HashSet<string>(StringComparer.Ordinal), fullMembers, errors, "full roster");
            AddDefinitions(selectedRoster, selectedCatalog, new HashSet<string>(StringComparer.Ordinal), selectedMembers, errors, "selected roster");
            foreach (var pair in selectedCatalog)
            {
                if (!catalog.TryGetValue(pair.Key, out CharacterDefinition fullDefinition))
                    errors.Add("Selected roster DefinitionId '" + pair.Key + "' is absent from the full roster.");
                else if (!TagsEqual(fullDefinition.Units, pair.Value.Units) ||
                         !TagsEqual(fullDefinition.Attributes, pair.Value.Attributes))
                    errors.Add("DefinitionId '" + pair.Key + "' has conflicting tags in the selected and full rosters.");
            }
            if (errors.Count == 0)
            {
                foreach (var pair in selectedMembers)
                {
                    if (pair.Value.Count < 2 || pair.Value.Count > BoardState.StandardColumns * BoardState.StandardRows ||
                        !fullMembers.TryGetValue(pair.Key, out var full) || pair.Value.Count != full.Count)
                        continue;
                    requirements.Add(pair.Key, new UnitRequirementObservation(pair.Key,
                        pair.Value.OrderBy(id => id, StringComparer.Ordinal).ToArray()));
                }
            }
            return new RosterRules(catalog, requirements, errors);
        }

        private static void AddDefinitions(IEnumerable<CharacterDefinition> source,
            Dictionary<string, CharacterDefinition> catalog, HashSet<string> ids,
            Dictionary<string, HashSet<string>> membersByUnit, List<string> errors, string sourceName)
        {
            foreach (CharacterDefinition definition in source)
            {
                // Existing Unity roster counting skips null CharacterData entries.
                if (definition == null) continue;
                if (string.IsNullOrWhiteSpace(definition.DefinitionId))
                {
                    errors.Add("A definition in the " + sourceName + " has an empty DefinitionId.");
                    continue;
                }

                if (catalog.TryGetValue(definition.DefinitionId, out CharacterDefinition existing))
                {
                    if (!TagsEqual(existing.Units, definition.Units) ||
                        !TagsEqual(existing.Attributes, definition.Attributes) ||
                        existing.BaseCost != definition.BaseCost ||
                        !string.Equals(existing.CharacterName, definition.CharacterName, StringComparison.Ordinal))
                        errors.Add("Duplicate DefinitionId '" + definition.DefinitionId + "' has conflicting definition data.");
                }
                else catalog.Add(definition.DefinitionId, definition);

                if (!ids.Add(definition.DefinitionId)) continue;
                if (definition.Units == null) continue;
                var tagsOnDefinition = new HashSet<string>(StringComparer.Ordinal);
                foreach (string tag in definition.Units)
                {
                    if (string.IsNullOrWhiteSpace(tag) || !tagsOnDefinition.Add(tag)) continue;
                    if (!membersByUnit.TryGetValue(tag, out HashSet<string> members))
                    {
                        members = new HashSet<string>(StringComparer.Ordinal);
                        membersByUnit.Add(tag, members);
                    }
                    members.Add(definition.DefinitionId);
                }
            }
        }

        private static void AddParticipantTags(IReadOnlyList<string> tags, SynergyParticipant participant,
            Dictionary<string, List<SynergyParticipant>> participantsByTag, List<string> tagOrder)
        {
            if (tags == null) return;
            var uniqueTags = new HashSet<string>(StringComparer.Ordinal);
            foreach (string tag in tags)
            {
                if (string.IsNullOrWhiteSpace(tag) || !uniqueTags.Add(tag)) continue;
                if (!participantsByTag.TryGetValue(tag, out List<SynergyParticipant> participants))
                {
                    participants = new List<SynergyParticipant>();
                    participantsByTag.Add(tag, participants);
                    tagOrder.Add(tag);
                }
                participants.Add(participant);
            }
        }

        internal static bool AreConnected(IList<int> slots, int columns)
        {
            if (slots == null || slots.Count < 2 || columns <= 0) return false;
            var remaining = new HashSet<int>(slots);
            var queue = new Queue<int>();
            queue.Enqueue(slots[0]);
            remaining.Remove(slots[0]);
            while (queue.Count > 0)
            {
                int current = queue.Dequeue();
                var found = new List<int>();
                foreach (int candidate in remaining)
                {
                    if (AreOrthogonalNeighbors(current, candidate, columns))
                        found.Add(candidate);
                }
                found.Sort();
                foreach (int neighbor in found)
                {
                    remaining.Remove(neighbor);
                    queue.Enqueue(neighbor);
                }
            }
            return remaining.Count == 0;
        }

        internal static bool IsSharedAttribute(int participantCount) => participantCount >= 2;

        internal static bool AreOrthogonalNeighbors(int first, int second, int columns) =>
            Math.Abs(first / columns - second / columns) + Math.Abs(first % columns - second % columns) == 1;

        private static int CompareUnits(UnitSynergyResult left, UnitSynergyResult right) =>
            UnitMovePriority.Compare(left.UnitName, left.ParticipantBoardSlotIndexes,
                right.UnitName, right.ParticipantBoardSlotIndexes);

        private static bool TagsEqual(IReadOnlyList<string> left, IReadOnlyList<string> right)
        {
            var leftSet = new HashSet<string>(StringComparer.Ordinal);
            var rightSet = new HashSet<string>(StringComparer.Ordinal);
            if (left != null)
                foreach (string tag in left)
                    if (!string.IsNullOrWhiteSpace(tag)) leftSet.Add(tag);
            if (right != null)
                foreach (string tag in right)
                    if (!string.IsNullOrWhiteSpace(tag)) rightSet.Add(tag);
            return leftSet.SetEquals(rightSet);
        }
    }
}
