using System;
using System.Collections.Generic;
using ProjectNST.GameCore.Board;
using ProjectNST.GameCore.Characters;
using ProjectNST.GameCore.Economy;
using ProjectNST.GameCore.Randomness;
using ProjectNST.GameCore.Shop;
using ProjectNST.GameCore.Stages;

namespace ProjectNST.GameCore.Items
{
    /// <summary>Pure item-use and battle-effect restoration rules. No enemy, UI, or Unity state is changed.</summary>
    public static class ItemEffectRules
    {
        public static ItemEffectResult TryUseHandItem(ItemDefinition item, int handSlotIndex,
            ItemUseTarget target, BoardState board, HandState hand,
            CharacterOwnershipState ownership, EconomyState economy,
            StageProgressState stage, ItemEffectState effectState,
            IReadOnlyList<CharacterDefinition> characterDefinitions,
            CharacterStatDefinition statSettings, Pcg32State statsRandomState)
        {
            if (item == null || target == null || board == null || hand == null || ownership == null ||
                economy == null || stage == null || effectState == null)
                return Fail(ItemEffectFailure.InvalidInput, board, hand, ownership, economy, effectState,
                    stage, null, statsRandomState);
            if (stage.Phase != StageProgressPhase.Preparation)
                return Fail(ItemEffectFailure.InvalidPhase, board, hand, ownership, economy, effectState,
                    stage, null, statsRandomState);
            if (!BoardHandStateValidator.Validate(board, hand, ownership).IsValid)
                return Fail(ItemEffectFailure.InvalidState, board, hand, ownership, economy, effectState,
                    stage, null, statsRandomState);
            if (!hand.TryGetSlot(handSlotIndex, out HandSlotContent content) ||
                content.Kind != HandSlotContentKind.Item || content.ItemDefinitionId != item.DefinitionId)
                return Fail(ItemEffectFailure.ItemNotInHand, board, hand, ownership, economy, effectState,
                    stage, null, statsRandomState);

            return Apply(item, ItemSource.Hand, handSlotIndex, target, board, hand, ownership,
                economy, stage, effectState, null, characterDefinitions, statSettings, statsRandomState);
        }

        public static ItemEffectResult TryUseEventItem(ItemDefinition item, EventItemState eventItem,
            ItemUseTarget target, BoardState board, HandState hand,
            CharacterOwnershipState ownership, EconomyState economy,
            StageProgressState stage, ItemEffectState effectState,
            IReadOnlyList<CharacterDefinition> characterDefinitions,
            CharacterStatDefinition statSettings, Pcg32State statsRandomState)
        {
            if (item == null || eventItem == null || target == null || board == null || hand == null ||
                ownership == null || economy == null || stage == null || effectState == null)
                return Fail(ItemEffectFailure.InvalidInput, board, hand, ownership, economy, effectState,
                    stage, eventItem, statsRandomState);
            if (stage.Phase != StageProgressPhase.EventPending)
                return Fail(ItemEffectFailure.EventNotPending, board, hand, ownership, economy, effectState,
                    stage, eventItem, statsRandomState);
            if (!eventItem.IsAvailable)
                return Fail(ItemEffectFailure.EventItemUnavailable, board, hand, ownership, economy,
                    effectState, stage, eventItem, statsRandomState);
            if (eventItem.DefinitionId != item.DefinitionId)
                return Fail(ItemEffectFailure.EventItemMismatch, board, hand, ownership, economy,
                    effectState, stage, eventItem, statsRandomState);
            if (!IsValidDefinition(item))
                return Fail(ItemEffectFailure.InvalidItem, board, hand, ownership, economy,
                    effectState, stage, eventItem, statsRandomState);
            if (item.Target != ItemEffectTarget.OneAlly)
                return Fail(ItemEffectFailure.UnsupportedEffect, board, hand, ownership, economy,
                    effectState, stage, eventItem, statsRandomState);
            if (!BoardHandStateValidator.Validate(board, hand, ownership).IsValid)
                return Fail(ItemEffectFailure.InvalidState, board, hand, ownership, economy,
                    effectState, stage, eventItem, statsRandomState);

            return Apply(item, ItemSource.Event, -1, target, board, hand, ownership,
                economy, stage, effectState, eventItem, characterDefinitions, statSettings, statsRandomState);
        }

        /// <summary>Ends a pending item event when this item has no eligible Board/Hand character.</summary>
        public static ItemEffectResult EndEventWithoutEligibleTarget(ItemDefinition item,
            EventItemState eventItem, BoardState board, HandState hand,
            CharacterOwnershipState ownership, EconomyState economy,
            StageProgressState stage, ItemEffectState effectState, Pcg32State statsRandomState)
        {
            if (item == null || eventItem == null || board == null || hand == null || ownership == null ||
                economy == null || stage == null || effectState == null)
                return Fail(ItemEffectFailure.InvalidInput, board, hand, ownership, economy, effectState,
                    stage, eventItem, statsRandomState);
            if (stage.Phase != StageProgressPhase.EventPending)
                return Fail(ItemEffectFailure.EventNotPending, board, hand, ownership, economy,
                    effectState, stage, eventItem, statsRandomState);
            if (!eventItem.IsAvailable)
                return Fail(ItemEffectFailure.EventItemUnavailable, board, hand, ownership, economy,
                    effectState, stage, eventItem, statsRandomState);
            if (item.DefinitionId != eventItem.DefinitionId)
                return Fail(ItemEffectFailure.EventItemMismatch, board, hand, ownership, economy,
                    effectState, stage, eventItem, statsRandomState);
            if (!IsValidDefinition(item))
                return Fail(ItemEffectFailure.InvalidItem, board, hand, ownership, economy,
                    effectState, stage, eventItem, statsRandomState);
            if (!BoardHandStateValidator.Validate(board, hand, ownership).IsValid)
                return Fail(ItemEffectFailure.InvalidState, board, hand, ownership, economy,
                    effectState, stage, eventItem, statsRandomState);
            if (HasEligibleEventTarget(item, board, hand, ownership))
                return Fail(ItemEffectFailure.EligibleEventTargetExists, board, hand, ownership, economy,
                    effectState, stage, eventItem, statsRandomState);

            StageProgressResult completed = StageProgressRules.CompleteEvent(stage, false);
            if (!completed.Succeeded)
                return Fail(ItemEffectFailure.EventNotPending, board, hand, ownership, economy,
                    effectState, stage, eventItem, statsRandomState);
            return Succeed(board, hand, ownership, economy, effectState, completed.NextState,
                eventItem.WithStatus(EventItemStatus.EndedWithoutUse), null,
                Array.Empty<ItemEffectCommand>(), Array.Empty<ItemEffectCommand>(),
                Array.Empty<string>(), false, statsRandomState);
        }

        /// <summary>Removes all battle-only character/player effects; returns enemy commands for Stage 8/9 cleanup.</summary>
        public static ItemEffectResult RestoreBattleEffects(BoardState board, HandState hand,
            CharacterOwnershipState ownership, EconomyState economy, ItemEffectState effectState,
            Pcg32State statsRandomState)
        {
            if (board == null || hand == null || ownership == null || economy == null || effectState == null)
                return Fail(ItemEffectFailure.InvalidInput, board, hand, ownership, economy, effectState,
                    null, null, statsRandomState);
            if (!BoardHandStateValidator.Validate(board, hand, ownership).IsValid)
                return Fail(ItemEffectFailure.InvalidState, board, hand, ownership, economy, effectState,
                    null, null, statsRandomState);
            if (!effectState.HasActiveBattleEffects)
                return Fail(ItemEffectFailure.NoBattleEffectsToRestore, board, hand, ownership, economy,
                    effectState, null, null, statsRandomState);

            var changed = new Dictionary<string, CharacterInstance>(StringComparer.Ordinal);
            var restoredIds = new List<string>();
            foreach (CharacterInstanceView view in ownership.GetOwnedInstances())
            {
                if (!ownership.TryCreateOwnedInstanceCopy(view.InstanceId, out CharacterInstance instance))
                    return Fail(ItemEffectFailure.InvalidState, board, hand, ownership, economy,
                        effectState, null, null, statsRandomState);
                bool hadTemporary = instance.HasTemporaryStar || instance.TemporaryAttackBonus != 0 ||
                    instance.TemporaryDefenseBonus != 0;
                if (!hadTemporary) continue;
                instance.ClearBattleOnlyItemEffects();
                changed.Add(instance.InstanceId, instance);
                restoredIds.Add(instance.InstanceId);
            }

            EconomyState nextEconomy = economy;
            if (effectState.TemporaryPlayerHpDelta != 0)
            {
                if (!economy.HasHealth)
                    return Fail(ItemEffectFailure.HealthUnavailable, board, hand, ownership, economy,
                        effectState, null, null, statsRandomState);
                long restoredHp = (long)economy.CurrentHP - effectState.TemporaryPlayerHpDelta;
                int clamped = (int)Math.Max(0L, Math.Min(economy.MaximumHP, restoredHp));
                nextEconomy = economy.WithHealth(clamped);
            }

            if (!TryCreateOwnership(ownership, changed, out CharacterOwnershipState nextOwnership))
                return Fail(ItemEffectFailure.InvalidState, board, hand, ownership, economy,
                    effectState, null, null, statsRandomState);
            return Succeed(board, hand, nextOwnership, nextEconomy, effectState.AfterBattle(), null, null, null,
                Array.Empty<ItemEffectCommand>(), effectState.EnemyCommands, restoredIds, false, statsRandomState);
        }

        private static ItemEffectResult Apply(ItemDefinition item, ItemSource source, int handSlotIndex,
            ItemUseTarget target, BoardState board, HandState hand, CharacterOwnershipState ownership,
            EconomyState economy, StageProgressState stage, ItemEffectState effectState,
            EventItemState eventItem, IReadOnlyList<CharacterDefinition> definitions,
            CharacterStatDefinition statSettings, Pcg32State statsRandomState)
        {
            if (!IsValidDefinition(item))
                return Fail(ItemEffectFailure.InvalidItem, board, hand, ownership, economy, effectState,
                    stage, eventItem, statsRandomState);
            if (!IsTargetValid(item.Target, source, target, board, hand, ownership))
                return Fail(ItemEffectFailure.InvalidTarget, board, hand, ownership, economy, effectState,
                    stage, eventItem, statsRandomState);

            List<string> characterIds = ResolveCharacterTargets(item.Target, source, target, board, hand);
            if ((item.Target == ItemEffectTarget.OneAlly || item.Target == ItemEffectTarget.AllAllies) &&
                characterIds.Count == 0)
                return Fail(ItemEffectFailure.TargetMissing, board, hand, ownership, economy,
                    effectState, stage, eventItem, statsRandomState);
            for (int i = 0; i < characterIds.Count; i++)
                if (!ownership.Contains(characterIds[i]))
                    return Fail(ItemEffectFailure.TargetMissing, board, hand, ownership, economy,
                        effectState, stage, eventItem, statsRandomState);

            Pcg32RandomSource workingStatsRandom = null;
            bool hasStarEffect = ContainsEffect(item, ItemEffectKind.Star);
            if (hasStarEffect)
            {
                if (definitions == null || statSettings == null)
                    return Fail(ItemEffectFailure.StatsUnavailable, board, hand, ownership, economy,
                        effectState, stage, eventItem, statsRandomState);
                try { workingStatsRandom = Pcg32RandomSource.FromState(statsRandomState); }
                catch (ArgumentException)
                {
                    return Fail(ItemEffectFailure.StatsUnavailable, board, hand, ownership, economy,
                        effectState, stage, eventItem, statsRandomState);
                }
            }

            var workingCharacters = new Dictionary<string, CharacterInstance>(StringComparer.Ordinal);
            var commands = new List<ItemEffectCommand>();
            long tempHpDelta = 0;
            int permanentBlockDelta = 0;
            int temporaryBlockDelta = 0;
            bool battleOnly = item.Duration == ItemEffectDuration.ThisBattle;
            EconomyState nextEconomy = economy;

            foreach (ItemEffectDefinition effect in item.Effects)
            {
                if (effect.Kind == ItemEffectKind.Star && (effect.Value < 1 || effect.Value > 3))
                    return Fail(ItemEffectFailure.InvalidEffectValue, board, hand, ownership, economy,
                        effectState, stage, eventItem, statsRandomState);

                if (item.Target == ItemEffectTarget.OneAlly || item.Target == ItemEffectTarget.AllAllies)
                {
                    foreach (string instanceId in characterIds)
                    {
                        if (!workingCharacters.TryGetValue(instanceId, out CharacterInstance instance))
                        {
                            if (!ownership.TryCreateOwnedInstanceCopy(instanceId, out instance))
                                return Fail(ItemEffectFailure.TargetMissing, board, hand, ownership,
                                    economy, effectState, stage, eventItem, statsRandomState);
                            workingCharacters.Add(instanceId, instance);
                        }
                        ItemEffectFailure failure = ApplyToCharacter(effect, item.Duration, instance,
                            definitions, statSettings, workingStatsRandom);
                        if (failure != ItemEffectFailure.None)
                            return Fail(failure, board, hand, ownership, economy, effectState,
                                stage, eventItem, statsRandomState);
                    }
                }
                else if (item.Target == ItemEffectTarget.OneEnemy || item.Target == ItemEffectTarget.AllEnemies)
                {
                    int enemySlot = item.Target == ItemEffectTarget.OneEnemy ? target.SlotIndex : -1;
                    commands.Add(new ItemEffectCommand(effect.Kind, item.Target, effect.Value,
                        enemySlot, item.Duration));
                }
                else if (item.Target == ItemEffectTarget.Player)
                {
                    if (effect.Kind == ItemEffectKind.Damage)
                    {
                        if (!nextEconomy.HasHealth)
                            return Fail(ItemEffectFailure.HealthUnavailable, board, hand, ownership,
                                economy, effectState, stage, eventItem, statsRandomState);
                        long hp = (long)nextEconomy.CurrentHP - effect.Value;
                        int clamped = (int)Math.Max(0L, Math.Min(nextEconomy.MaximumHP, hp));
                        long actualDelta = (long)clamped - nextEconomy.CurrentHP;
                        if (actualDelta < int.MinValue || actualDelta > int.MaxValue)
                            return Fail(ItemEffectFailure.ArithmeticOverflow, board, hand, ownership,
                                economy, effectState, stage, eventItem, statsRandomState);
                        nextEconomy = nextEconomy.WithHealth(clamped);
                        if (battleOnly) tempHpDelta += actualDelta;
                    }
                    else
                    {
                        bool ok;
                        if (battleOnly) temporaryBlockDelta = SafeDelta(temporaryBlockDelta, effect.Value, out ok);
                        else permanentBlockDelta = SafeDelta(permanentBlockDelta, effect.Value, out ok);
                        if (!ok)
                            return Fail(ItemEffectFailure.ArithmeticOverflow, board, hand, ownership,
                                economy, effectState, stage, eventItem, statsRandomState);
                    }
                }
            }

            if (tempHpDelta < int.MinValue || tempHpDelta > int.MaxValue)
                return Fail(ItemEffectFailure.ArithmeticOverflow, board, hand, ownership,
                    economy, effectState, stage, eventItem, statsRandomState);
            if (!effectState.TryWith(permanentBlockDelta, temporaryBlockDelta, (int)tempHpDelta,
                    commands, battleOnly, out ItemEffectState nextEffectState))
                return Fail(ItemEffectFailure.ArithmeticOverflow, board, hand, ownership,
                    economy, effectState, stage, eventItem, statsRandomState);
            if (!TryCreateOwnership(ownership, workingCharacters, out CharacterOwnershipState nextOwnership))
                return Fail(ItemEffectFailure.InvalidState, board, hand, ownership, economy,
                    effectState, stage, eventItem, statsRandomState);

            HandState nextHand = hand;
            if (source == ItemSource.Hand && !hand.TryWithSlot(handSlotIndex,
                    HandSlotContent.Empty(), out nextHand))
                return Fail(ItemEffectFailure.ItemNotInHand, board, hand, ownership, economy,
                    effectState, stage, eventItem, statsRandomState);
            EventItemState nextEvent = source == ItemSource.Event
                ? eventItem.WithStatus(EventItemStatus.Consumed) : eventItem;
            Pcg32State nextRandom = workingStatsRandom != null
                ? workingStatsRandom.CaptureState() : statsRandomState;
            bool consumedRandom = workingStatsRandom != null && !StateEquals(statsRandomState, nextRandom);
            return Succeed(board, nextHand, nextOwnership, nextEconomy, nextEffectState,
                stage, nextEvent, item.DefinitionId, commands, Array.Empty<ItemEffectCommand>(),
                Array.Empty<string>(), consumedRandom, nextRandom);
        }

        private static ItemEffectFailure ApplyToCharacter(ItemEffectDefinition effect,
            ItemEffectDuration duration, CharacterInstance instance,
            IReadOnlyList<CharacterDefinition> definitions, CharacterStatDefinition statSettings,
            Pcg32RandomSource random)
        {
            bool temporary = duration == ItemEffectDuration.ThisBattle;
            switch (effect.Kind)
            {
                case ItemEffectKind.Attack:
                    return instance.TryApplyItemStatDelta(effect.Value, 0, temporary)
                        ? ItemEffectFailure.None : ItemEffectFailure.ArithmeticOverflow;
                case ItemEffectKind.Defense:
                    return instance.TryApplyItemStatDelta(0, effect.Value, temporary)
                        ? ItemEffectFailure.None : ItemEffectFailure.ArithmeticOverflow;
                case ItemEffectKind.Star:
                    if (instance.Star >= effect.Value || (instance.HasTemporaryStar && !temporary))
                        return ItemEffectFailure.CharacterNotEligible;
                    if (!TryFindDefinition(definitions, instance.DefinitionId, out CharacterDefinition definition))
                        return ItemEffectFailure.MissingCharacterDefinition;
                    bool success = temporary
                        ? instance.TrySetTemporaryStar(effect.Value, definition, statSettings, random)
                        : instance.TrySetStar(effect.Value, definition, statSettings, random);
                    return success ? ItemEffectFailure.None : ItemEffectFailure.StatsUnavailable;
                default:
                    return ItemEffectFailure.UnsupportedEffect;
            }
        }

        private static bool IsValidDefinition(ItemDefinition item)
        {
            if (!Enum.IsDefined(typeof(ItemEffectTarget), item.Target) ||
                !Enum.IsDefined(typeof(ItemEffectDuration), item.Duration) ||
                item.Duration == ItemEffectDuration.Unspecified || item.Effects == null || item.Effects.Count == 0)
                return false;
            foreach (ItemEffectDefinition effect in item.Effects)
                if (effect == null || !Enum.IsDefined(typeof(ItemEffectKind), effect.Kind) ||
                    !IsSupported(item.Target, effect.Kind)) return false;
            return item.Target != ItemEffectTarget.Unspecified;
        }

        private static bool IsSupported(ItemEffectTarget target, ItemEffectKind effect)
        {
            switch (target)
            {
                case ItemEffectTarget.OneAlly: return effect == ItemEffectKind.Attack || effect == ItemEffectKind.Defense || effect == ItemEffectKind.Star;
                case ItemEffectTarget.AllAllies: return effect == ItemEffectKind.Attack || effect == ItemEffectKind.Defense;
                case ItemEffectTarget.OneEnemy: return effect == ItemEffectKind.Attack || effect == ItemEffectKind.Defense || effect == ItemEffectKind.Damage || effect == ItemEffectKind.Gap;
                case ItemEffectTarget.AllEnemies: return effect == ItemEffectKind.Attack || effect == ItemEffectKind.Defense || effect == ItemEffectKind.Damage;
                case ItemEffectTarget.Player: return effect == ItemEffectKind.Damage || effect == ItemEffectKind.Block;
                default: return false;
            }
        }

        private static bool IsTargetValid(ItemEffectTarget effectTarget, ItemSource source,
            ItemUseTarget target, BoardState board, HandState hand, CharacterOwnershipState ownership)
        {
            if (target == null) return false;
            switch (effectTarget)
            {
                case ItemEffectTarget.OneAlly:
                    if (source == ItemSource.Hand && target.Kind == ItemUseTargetKind.BoardCharacter)
                        return HasBoardCharacter(board, target.SlotIndex);
                    if (source == ItemSource.Event && target.Kind == ItemUseTargetKind.BoardCharacter)
                        return HasBoardCharacter(board, target.SlotIndex);
                    if (source == ItemSource.Event && target.Kind == ItemUseTargetKind.HandCharacter)
                        return HasHandCharacter(hand, target.SlotIndex);
                    return false;
                case ItemEffectTarget.AllAllies:
                    return source == ItemSource.Hand && target.Kind == ItemUseTargetKind.BoardArea &&
                        IsBoardIndex(board, target.SlotIndex);
                case ItemEffectTarget.OneEnemy:
                    return source == ItemSource.Hand && target.Kind == ItemUseTargetKind.EnemyCharacter && target.IsPresent;
                case ItemEffectTarget.AllEnemies:
                    return source == ItemSource.Hand && target.Kind == ItemUseTargetKind.EnemyArea && target.EnemyCount > 0;
                case ItemEffectTarget.Player:
                    return source == ItemSource.Hand && target.Kind == ItemUseTargetKind.PlayerArea &&
                        IsBoardIndex(board, target.SlotIndex);
                default: return false;
            }
        }

        private static List<string> ResolveCharacterTargets(ItemEffectTarget targetKind, ItemSource source,
            ItemUseTarget target, BoardState board, HandState hand)
        {
            var ids = new List<string>();
            if (targetKind == ItemEffectTarget.OneAlly)
            {
                if (target.Kind == ItemUseTargetKind.BoardCharacter && board.TryGetCharacterIdAt(target.SlotIndex, out string id))
                    ids.Add(id);
                else if (source == ItemSource.Event && target.Kind == ItemUseTargetKind.HandCharacter &&
                    hand.TryGetSlot(target.SlotIndex, out HandSlotContent content) &&
                    content.Kind == HandSlotContentKind.Character)
                    ids.Add(content.CharacterInstanceId);
            }
            else if (targetKind == ItemEffectTarget.AllAllies)
            {
                for (int i = 0; i < board.SlotCount; i++)
                    if (board.TryGetCharacterIdAt(i, out string id) && id != null) ids.Add(id);
            }
            return ids;
        }

        private static bool HasEligibleEventTarget(ItemDefinition item, BoardState board,
            HandState hand, CharacterOwnershipState ownership)
        {
            if (!IsValidDefinition(item) || item.Target != ItemEffectTarget.OneAlly ||
                item.Duration == ItemEffectDuration.Unspecified) return false;
            for (int i = 0; i < board.SlotCount; i++)
                if (board.TryGetCharacterIdAt(i, out string id) && id != null &&
                    IsEligibleCharacter(item, ownership, id)) return true;
            for (int i = 0; i < hand.SlotCount; i++)
                if (hand.TryGetSlot(i, out HandSlotContent content) &&
                    content.Kind == HandSlotContentKind.Character &&
                    IsEligibleCharacter(item, ownership, content.CharacterInstanceId)) return true;
            return false;
        }

        private static bool IsEligibleCharacter(ItemDefinition item, CharacterOwnershipState ownership, string id)
        {
            if (!ownership.TryGet(id, out CharacterInstanceView character)) return false;
            foreach (ItemEffectDefinition effect in item.Effects)
                if (effect.Kind == ItemEffectKind.Star && (effect.Value < 1 || effect.Value > 3 || character.Star >= effect.Value))
                    return false;
            return true;
        }

        private static bool HasBoardCharacter(BoardState board, int index) =>
            board.TryGetCharacterIdAt(index, out string id) && id != null;

        private static bool HasHandCharacter(HandState hand, int index) =>
            hand.TryGetSlot(index, out HandSlotContent content) && content.Kind == HandSlotContentKind.Character;

        private static bool IsBoardIndex(BoardState board, int index) =>
            board.TryGetCharacterIdAt(index, out _);

        private static bool TryFindDefinition(IReadOnlyList<CharacterDefinition> definitions,
            string id, out CharacterDefinition result)
        {
            result = null;
            if (definitions == null) return false;
            foreach (CharacterDefinition definition in definitions)
            {
                if (definition == null || definition.DefinitionId != id) continue;
                if (result != null && !ReferenceEquals(result, definition)) return false;
                result = definition;
            }
            return result != null;
        }

        private static bool TryCreateOwnership(CharacterOwnershipState original,
            IDictionary<string, CharacterInstance> changed, out CharacterOwnershipState next)
        {
            var instances = new List<CharacterInstance>(original.OwnedCount);
            foreach (CharacterInstanceView view in original.GetOwnedInstances())
            {
                if (changed != null && changed.TryGetValue(view.InstanceId, out CharacterInstance replacement))
                    instances.Add(replacement);
                else if (original.TryCreateOwnedInstanceCopy(view.InstanceId, out CharacterInstance copy))
                    instances.Add(copy);
                else
                {
                    next = original;
                    return false;
                }
            }
            next = new CharacterOwnershipState(instances);
            return true;
        }

        private static int SafeDelta(int current, int value, out bool valid)
        {
            long sum = (long)current + value;
            valid = sum >= int.MinValue && sum <= int.MaxValue;
            return valid ? (int)sum : current;
        }

        private static bool ContainsEffect(ItemDefinition item, ItemEffectKind kind)
        {
            foreach (ItemEffectDefinition effect in item.Effects) if (effect.Kind == kind) return true;
            return false;
        }

        private static bool StateEquals(Pcg32State left, Pcg32State right) =>
            left.AlgorithmVersion == right.AlgorithmVersion && left.State == right.State && left.Increment == right.Increment;

        private static ItemEffectResult Succeed(BoardState board, HandState hand,
            CharacterOwnershipState ownership, EconomyState economy, ItemEffectState effectState,
            StageProgressState stage, EventItemState eventItem, string consumed,
            IEnumerable<ItemEffectCommand> commands, IEnumerable<ItemEffectCommand> expired,
            IEnumerable<string> restored, bool rngConsumed, Pcg32State rng) =>
            new ItemEffectResult(true, ItemEffectFailure.None, board, hand, ownership, economy,
                effectState, stage, eventItem, consumed, commands, expired, restored, rngConsumed, rng);

        private static ItemEffectResult Fail(ItemEffectFailure failure, BoardState board,
            HandState hand, CharacterOwnershipState ownership, EconomyState economy,
            ItemEffectState effectState, StageProgressState stage, EventItemState eventItem,
            Pcg32State rng) =>
            new ItemEffectResult(false, failure, board, hand, ownership, economy, effectState,
                stage, eventItem, null, Array.Empty<ItemEffectCommand>(),
                Array.Empty<ItemEffectCommand>(), Array.Empty<string>(), false, rng);

        private enum ItemSource { Hand, Event }
    }
}
