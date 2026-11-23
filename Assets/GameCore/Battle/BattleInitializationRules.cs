using System;
using System.Collections.Generic;
using ProjectNST.GameCore.Board;
using ProjectNST.GameCore.Characters;
using ProjectNST.GameCore.Economy;
using ProjectNST.GameCore.Enemies;
using ProjectNST.GameCore.Items;
using ProjectNST.GameCore.Randomness;
using ProjectNST.GameCore.Stages;
using ProjectNST.GameCore.Synergy;

namespace ProjectNST.GameCore.Battle
{
    /// <summary>Pure, atomic creation of the initial BattleState from existing GameCore states.</summary>
    public static class BattleInitializationRules
    {
        public static BattleInitializationResult Initialize(EconomyState player,
            StageProgressState stage, BoardState board, HandState hand,
            CharacterOwnershipState ownership, IReadOnlyList<CharacterDefinition> characterDefinitions,
            EnemyBoardState enemyBoard, IReadOnlyList<EnemyDefinition> enemyDefinitions,
            ItemEffectState itemEffects, IEnumerable<CharacterDefinition> selectedRoster,
            IEnumerable<CharacterDefinition> fullCharacterRoster, Pcg32State battleRandomState,
            BattleConfig config = null, BattleState existingBattle = null)
        {
            BattleConfig effectiveConfig = config ?? BattleConfig.Default;
            BattleStartFailure startFailure = BattleStartRules.Validate(
                player, stage, board, hand, ownership, existingBattle);
            if (startFailure != BattleStartFailure.None)
                return Failure(startFailure, battleRandomState);
            if (enemyBoard == null || enemyDefinitions == null || itemEffects == null ||
                characterDefinitions == null || selectedRoster == null || fullCharacterRoster == null)
                return Failure(BattleStartFailure.InvalidInput, battleRandomState);
            if (enemyBoard.Columns != EnemyBoardState.StandardColumns ||
                enemyBoard.Rows != EnemyBoardState.StandardRows ||
                enemyBoard.SlotCount != EnemyInstanceFactory.SlotCount)
                return Failure(BattleStartFailure.InvalidEnemyBoard, battleRandomState);

            try { Pcg32RandomSource.ValidateState(battleRandomState); }
            catch (ArgumentException) { return Failure(BattleStartFailure.InvalidBattleRandomState, battleRandomState); }

            if (!TryIndexCharacters(characterDefinitions, out Dictionary<string, CharacterDefinition> characters))
                return Failure(BattleStartFailure.InvalidDefinitions, battleRandomState);
            if (!TryIndexEnemies(enemyDefinitions, out Dictionary<string, EnemyDefinition> enemiesById))
                return Failure(BattleStartFailure.InvalidDefinitions, battleRandomState);

            var allies = new List<BattleUnitState>();
            var seenInstances = new HashSet<string>(StringComparer.Ordinal);
            for (int slot = 0; slot < board.SlotCount; slot++)
            {
                board.TryGetCharacterIdAt(slot, out string instanceId);
                if (instanceId == null) continue;
                if (!seenInstances.Add(instanceId) || !ownership.TryGet(instanceId, out CharacterInstanceView instance))
                    return Failure(BattleStartFailure.InvalidBoardHandOwnershipState, battleRandomState);
                if (!characters.TryGetValue(instance.DefinitionId, out CharacterDefinition definition))
                    return Failure(BattleStartFailure.MissingCharacterDefinition, battleRandomState);
                // Stage 9-B must snapshot existing stats without consuming Stats RNG.
                if (!instance.HasRolledStats)
                    return Failure(BattleStartFailure.CharacterStatsUnavailable, battleRandomState);

                long attack = (long)instance.Attack + instance.TemporaryAttackBonus;
                long defense = (long)instance.Defense + instance.TemporaryDefenseBonus;
                if (!FitsInt(attack) || !FitsInt(defense))
                    return Failure(BattleStartFailure.InvalidInput, battleRandomState);

                allies.Add(new BattleUnitState(instance.InstanceId, instance.DefinitionId,
                    BattleUnitSide.Ally, slot, (int)attack, (int)defense, false, 0, 0,
                    0, 0, 1, 0, instance.Star, definition.Units, definition.Attributes,
                    Array.Empty<string>(), 0, Array.Empty<string>(), Array.Empty<string>(),
                    Array.Empty<string>(), instance.TemporaryAttackBonus, instance.TemporaryDefenseBonus));
            }

            SynergyDetectionResult synergy = SynergyDetector.Detect(
                board, hand, ownership, selectedRoster, fullCharacterRoster);
            if (!synergy.IsValid)
                return Failure(BattleStartFailure.InvalidSynergyRoster, battleRandomState);

            var moves = new List<UnitMoveState>();
            foreach (UnitSynergyResult move in synergy.EstablishedUnits)
            {
                var participantIds = new List<string>(move.ParticipantCount);
                var slots = new List<int>(move.ParticipantCount);
                var stars = new List<int>(move.ParticipantCount);
                foreach (SynergyParticipant participant in move.Participants)
                {
                    if (!ownership.TryGet(participant.InstanceId, out CharacterInstanceView instance))
                        return Failure(BattleStartFailure.InvalidBoardHandOwnershipState, battleRandomState);
                    participantIds.Add(participant.InstanceId);
                    slots.Add(participant.BoardSlotIndex);
                    stars.Add(instance.Star);
                }
                moves.Add(new UnitMoveState(move.UnitName, participantIds, slots, stars));
            }

            var battleEnemies = new List<BattleUnitState>();
            var seenEnemies = new HashSet<string>(StringComparer.Ordinal);
            for (int slot = 0; slot < enemyBoard.SlotCount; slot++)
            {
                if (!enemyBoard.TryGetAt(slot, out EnemyInstance enemy) || enemy == null) continue;
                if (enemy.SlotIndex != slot || !seenEnemies.Add(enemy.InstanceId))
                    return Failure(BattleStartFailure.InvalidEnemyBoard, battleRandomState);
                if (!enemiesById.TryGetValue(enemy.DefinitionId, out EnemyDefinition definition))
                    return Failure(BattleStartFailure.MissingEnemyDefinition, battleRandomState);
                // Preserve HP-zero placements in EnemyBoardState, but exclude them from battle units.
                if (enemy.CurrentHealth <= 0) continue;
                long nextActionRound = (long)enemy.Gap + 1L;
                if (nextActionRound > int.MaxValue)
                    return Failure(BattleStartFailure.InvalidCombatCounter, battleRandomState);
                battleEnemies.Add(new BattleUnitState(enemy.InstanceId, enemy.DefinitionId,
                    BattleUnitSide.Enemy, slot, enemy.Attack, enemy.Defense, true,
                    enemy.MaxHealth, enemy.CurrentHealth, enemy.Gap, enemy.Cooldown,
                    (int)nextActionRound, 0, 0, Array.Empty<string>(), Array.Empty<string>(),
                    definition.TargetIds, 0, definition.CipEffectIds, definition.AeEffectIds,
                    definition.PigEffectIds));
            }

            int attributeBlock = synergy.AttributeBlockBonus;
            long rawBlock = (long)attributeBlock + itemEffects.TemporaryPlayerBlockBonus +
                itemEffects.PermanentPlayerBlockBonus;
            if (rawBlock > int.MaxValue)
                return Failure(BattleStartFailure.InvalidInput, battleRandomState);
            int initialBlock = (int)Math.Max(0L, rawBlock);

            try
            {
                var state = new BattleState(stage.EncounterIndex, player.CurrentHP, initialBlock,
                    attributeBlock, itemEffects.TemporaryPlayerBlockBonus,
                    itemEffects.PermanentPlayerBlockBonus, allies, battleEnemies, moves,
                    battleRandomState, effectiveConfig.MaxRounds, playerMaximumHP: player.MaximumHP);
                return new BattleInitializationResult(true, BattleStartFailure.None, state, battleRandomState);
            }
            catch (ArgumentException)
            {
                return Failure(BattleStartFailure.InvalidInput, battleRandomState);
            }
        }

        private static bool TryIndexCharacters(IReadOnlyList<CharacterDefinition> definitions,
            out Dictionary<string, CharacterDefinition> byId)
        {
            byId = new Dictionary<string, CharacterDefinition>(StringComparer.Ordinal);
            foreach (CharacterDefinition definition in definitions)
            {
                if (definition == null || string.IsNullOrWhiteSpace(definition.DefinitionId) ||
                    byId.ContainsKey(definition.DefinitionId)) return false;
                byId.Add(definition.DefinitionId, definition);
            }
            return true;
        }

        private static bool TryIndexEnemies(IReadOnlyList<EnemyDefinition> definitions,
            out Dictionary<string, EnemyDefinition> byId)
        {
            byId = new Dictionary<string, EnemyDefinition>(StringComparer.Ordinal);
            foreach (EnemyDefinition definition in definitions)
            {
                if (definition == null || string.IsNullOrWhiteSpace(definition.DefinitionId) ||
                    byId.ContainsKey(definition.DefinitionId)) return false;
                byId.Add(definition.DefinitionId, definition);
            }
            return true;
        }

        private static bool FitsInt(long value) => value >= int.MinValue && value <= int.MaxValue;

        private static BattleInitializationResult Failure(BattleStartFailure failure, Pcg32State unchangedRandomState) =>
            new BattleInitializationResult(false, failure, null, unchangedRandomState);
    }
}
