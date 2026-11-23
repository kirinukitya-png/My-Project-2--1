using System;
using System.Collections.Generic;

namespace ProjectNST.GameCore.Enemies
{
    public enum EnemyFormationFailure
    {
        None,
        InvalidInput,
        InvalidSlotCount,
        MissingEnemyDefinition,
        DuplicateEnemyDefinitionId,
        EnemyInstanceCreationFailed,
        BoardCreationFailed
    }

    public sealed class EnemyFormationResult
    {
        public bool Succeeded { get; }
        public EnemyFormationFailure Failure { get; }
        public EnemyInstanceFailure InstanceFailure { get; }
        public EnemyBoardFailure BoardFailure { get; }
        public EnemyFormationDefinition Formation { get; }
        public EnemyBoardState Board { get; }

        internal EnemyFormationResult(bool succeeded, EnemyFormationFailure failure,
            EnemyInstanceFailure instanceFailure, EnemyBoardFailure boardFailure,
            EnemyFormationDefinition formation, EnemyBoardState board)
        {
            Succeeded = succeeded;
            Failure = failure;
            InstanceFailure = instanceFailure;
            BoardFailure = boardFailure;
            Formation = formation;
            Board = board;
        }
    }

    /// <summary>Creates one immutable enemy board from a selected, six-slot formation.</summary>
    public static class EnemyFormationRules
    {
        public static EnemyFormationResult CreateBoard(EnemyFormationDefinition formation,
            IReadOnlyList<EnemyDefinition> definitions, int encounterIndex,
            EnemyPreparationState preparation = null)
        {
            if (formation == null || definitions == null || encounterIndex < 0)
                return Fail(EnemyFormationFailure.InvalidInput, formation);
            if (formation.Slots == null || formation.Slots.Count != EnemyInstanceFactory.SlotCount)
                return Fail(EnemyFormationFailure.InvalidSlotCount, formation);
            preparation = preparation ?? new EnemyPreparationState();

            var definitionById = new Dictionary<string, EnemyDefinition>(StringComparer.Ordinal);
            foreach (EnemyDefinition definition in definitions)
            {
                if (definition == null || string.IsNullOrWhiteSpace(definition.DefinitionId))
                    return Fail(EnemyFormationFailure.InvalidInput, formation);
                if (definitionById.ContainsKey(definition.DefinitionId))
                    return Fail(EnemyFormationFailure.DuplicateEnemyDefinitionId, formation);
                definitionById.Add(definition.DefinitionId, definition);
            }

            var instances = new List<EnemyInstance>();
            for (int i = 0; i < formation.Slots.Count; i++)
            {
                EnemyFormationSlotDefinition slot = formation.Slots[i];
                if (slot == null) return Fail(EnemyFormationFailure.InvalidSlotCount, formation);
                if (!slot.HasEnemy) continue;
                if (!definitionById.TryGetValue(slot.EnemyDefinitionId, out EnemyDefinition definition))
                    return Fail(EnemyFormationFailure.MissingEnemyDefinition, formation);
                if (!EnemyInstanceFactory.TryCreate(definition, encounterIndex, i,
                    preparation.GetAttackBonus(i), preparation.GetDefenseBonus(i),
                    preparation.GetHealthBonus(i), slot.Gap, preparation.GetGapBonus(i),
                    slot.Cooldown, out EnemyInstance instance, out EnemyInstanceFailure instanceFailure))
                    return Fail(EnemyFormationFailure.EnemyInstanceCreationFailed, formation, instanceFailure);
                instances.Add(instance);
            }

            if (!EnemyBoardState.TryCreate(EnemyBoardState.StandardColumns,
                EnemyBoardState.StandardRows, instances, out EnemyBoardState board,
                out EnemyBoardFailure boardFailure))
                return Fail(EnemyFormationFailure.BoardCreationFailed, formation,
                    EnemyInstanceFailure.None, boardFailure);
            return new EnemyFormationResult(true, EnemyFormationFailure.None,
                EnemyInstanceFailure.None, EnemyBoardFailure.None, formation, board);
        }

        private static EnemyFormationResult Fail(EnemyFormationFailure failure,
            EnemyFormationDefinition formation, EnemyInstanceFailure instanceFailure = EnemyInstanceFailure.None,
            EnemyBoardFailure boardFailure = EnemyBoardFailure.None) =>
            new EnemyFormationResult(false, failure, instanceFailure, boardFailure, formation, null);
    }
}
