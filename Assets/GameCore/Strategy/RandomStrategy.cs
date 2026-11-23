using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using ProjectNST.GameCore.Actions;
using ProjectNST.GameCore.Battle;
using ProjectNST.GameCore.Board;
using ProjectNST.GameCore.Items;
using ProjectNST.GameCore.Observation;
using ProjectNST.GameCore.Shop;
using ProjectNST.GameCore.Stages;

namespace ProjectNST.GameCore.Strategy
{
    /// <summary>
    /// Optional public-rule values that are not currently included in GameObservation.
    /// A null value disables the corresponding action candidate instead of guessing.
    /// </summary>
    public sealed class RandomStrategySettings
    {
        public int? ShopRerollGoldCost { get; }
        public int? NormalExperienceGoldCost { get; }
        public int? SelectBExperienceGoldCost { get; }
        public int PreparationDecisionLimit { get; }

        public RandomStrategySettings(int preparationDecisionLimit = 30,
            int? shopRerollGoldCost = null, int? normalExperienceGoldCost = null,
            int? selectBExperienceGoldCost = null)
        {
            if (preparationDecisionLimit < 1) throw new ArgumentOutOfRangeException(nameof(preparationDecisionLimit));
            ValidateOptionalCost(shopRerollGoldCost, nameof(shopRerollGoldCost));
            ValidateOptionalCost(normalExperienceGoldCost, nameof(normalExperienceGoldCost));
            ValidateOptionalCost(selectBExperienceGoldCost, nameof(selectBExperienceGoldCost));
            PreparationDecisionLimit = preparationDecisionLimit;
            ShopRerollGoldCost = shopRerollGoldCost;
            NormalExperienceGoldCost = normalExperienceGoldCost;
            SelectBExperienceGoldCost = selectBExperienceGoldCost;
        }

        private static void ValidateOptionalCost(int? value, string name)
        { if (value.HasValue && value.Value < 0) throw new ArgumentOutOfRangeException(name); }
    }

    /// <summary>
    /// Public-observation-only random policy. It proposes at most one declaration and never
    /// invokes a Rule or mutates game state. Per-trial progress lives in StrategyState.Payload.
    /// </summary>
    public sealed class RandomStrategy : IPlayerStrategy
    {
        public const string StableId = "RandomStrategy";
        public const string StableVersion = "1";
        public const int StateSchemaVersion = 1;
        private const string PayloadPrefix = "rg1|";
        private readonly RandomStrategySettings _settings;

        public string StrategyId => StableId;
        public string Version => StableVersion;

        public RandomStrategy(RandomStrategySettings settings = null)
        { _settings = settings ?? new RandomStrategySettings(); }

        public StrategyDecision ChooseAction(GameObservation observation, StrategyContext context)
        {
            if (observation == null) throw new ArgumentNullException(nameof(observation));
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (!context.IsFor(this)) throw new ArgumentException("Context belongs to another Strategy.", nameof(context));
            if (context.StrategyRandomState == null)
                throw new ArgumentException("RandomStrategy requires a Strategy RNG checkpoint.", nameof(context));

            StrategyPayload previous = ReadPayload(context.State);
            if (!observation.IsStrategyActionAllowed)
                return context.NoAction(NewState(0, string.Empty), "No player decision is required in this phase.");

            List<GameAction> candidates = BuildCandidates(observation, context.NextActionId);
            int decisionCount = observation.Stage.Phase == ObservationPhase.Preparation
                ? checked(previous.PreparationDecisionCount + 1) : 0;

            if (observation.Stage.Phase == ObservationPhase.Preparation &&
                decisionCount >= _settings.PreparationDecisionLimit)
            {
                GameAction start = candidates.FirstOrDefault(a => a.Kind == GameActionKind.StartBattle);
                if (start != null && !string.Equals(Signature(start), previous.LastActionSignature, StringComparison.Ordinal))
                    return context.DecideWithRng(start, NewState(decisionCount, Signature(start)),
                        context.StrategyRandomState, "Preparation decision limit reached; prioritizing StartBattle.");
            }

            if (!string.IsNullOrEmpty(previous.LastActionSignature))
            {
                candidates.RemoveAll(a => string.Equals(Signature(a), previous.LastActionSignature, StringComparison.Ordinal));
                candidates.RemoveAll(a => IsImmediateReverse(previous.LastActionSignature, a));
            }
            if (candidates.Count == 0)
            {
                StrategyState noActionState = NewState(decisionCount, string.Empty);
                return context.NoActionWithRng(noActionState, context.StrategyRandomState,
                    "No observable legal candidates are available.");
            }

            candidates.Sort(CompareActions);
            if (candidates.Count == 1)
            {
                GameAction only = candidates[0];
                return context.DecideWithRng(only, NewState(decisionCount, Signature(only)),
                    context.StrategyRandomState, "One observable candidate; Strategy RNG was not consumed.");
            }

            StrategyRandomSource random = context.CreateStrategyRandomSource();
            int selectedIndex = random.NextInt(0, candidates.Count);
            GameAction selected = candidates[selectedIndex];
            return context.DecideWithRng(selected, NewState(decisionCount, Signature(selected)),
                random.CaptureState(), "Selected uniformly from " + candidates.Count.ToString(CultureInfo.InvariantCulture) + " observable candidates.");
        }

        /// <summary>Exposed for deterministic inspection and tests; returned list is read-only.</summary>
        public IReadOnlyList<GameAction> GetCandidates(GameObservation observation, ActionId actionId)
        {
            if (observation == null) throw new ArgumentNullException(nameof(observation));
            var actions = BuildCandidates(observation, actionId);
            actions.Sort(CompareActions);
            return Array.AsReadOnly(actions.ToArray());
        }

        private List<GameAction> BuildCandidates(GameObservation o, ActionId id)
        {
            var result = new List<GameAction>();
            var keys = new HashSet<string>(StringComparer.Ordinal);
            Action<GameAction> add = action => { if (keys.Add(Signature(action))) result.Add(action); };

            if (!o.IsStrategyActionAllowed) return result;
            switch (o.Stage.Phase)
            {
                case ObservationPhase.AwaitingStageChoice:
                    foreach (StageChoice choice in o.Stage.AvailableChoices.OrderBy(c => (int)c))
                        add(new SelectStageChoiceAction(id, choice));
                    break;
                case ObservationPhase.Preparation:
                    AddPreparationCandidates(o, id, add);
                    break;
                case ObservationPhase.EventPending:
                    AddEventCandidates(o, id, add);
                    break;
                default:
                    break;
            }
            return result;
        }

        private void AddPreparationCandidates(GameObservation o, ActionId id, Action<GameAction> add)
        {
            if (o.Shop != null)
            {
                var offers = o.Shop.Offers.Where(x => x.Status == ShopOfferStatus.Available && !string.IsNullOrWhiteSpace(x.OfferId))
                    .OrderBy(x => x.OfferId, StringComparer.Ordinal).ToArray();
                if (o.Shop.Kind == ObservationShopKind.Character)
                {
                    foreach (ShopOfferObservation offer in offers.Where(x => x.Kind == ShopOfferKind.Character &&
                        x.DisplayPrice <= o.Player.Gold))
                    {
                        foreach (int slot in EmptyBoardSlots(o.Board))
                            if (!BoardContainsDefinition(o.Board, offer.DefinitionId))
                                add(new BuyCharacterAction(id, offer.OfferId, new GameActionLocation(CharacterLocationKind.Board, slot)));
                        foreach (int slot in EmptyHandSlots(o.Hand))
                            add(new BuyCharacterAction(id, offer.OfferId, new GameActionLocation(CharacterLocationKind.Hand, slot)));
                    }
                }
                else if (o.Shop.Kind == ObservationShopKind.Item)
                {
                    foreach (ShopOfferObservation offer in offers.Where(x => x.Kind == ShopOfferKind.Item &&
                        x.DisplayPrice <= o.Player.Gold))
                        foreach (int slot in EmptyHandSlots(o.Hand))
                            add(new BuyItemAction(id, offer.OfferId, slot));
                }

                if (_settings.ShopRerollGoldCost.HasValue && o.Stage.CanReroll &&
                    o.Player.Gold >= _settings.ShopRerollGoldCost.Value)
                {
                    ShopKind kind = o.Shop.Kind == ObservationShopKind.Character ? ShopKind.Character :
                        o.Shop.Kind == ObservationShopKind.Item ? ShopKind.Item : ShopKind.None;
                    if (kind != ShopKind.None) add(new RerollShopAction(id, kind));
                }
            }

            if (o.Stage.HasSelection && o.Player.Level < o.Player.MaximumLevel)
            {
                int? cost = o.Stage.Choice == StageChoice.SelectB
                    ? _settings.SelectBExperienceGoldCost : _settings.NormalExperienceGoldCost;
                if (cost.HasValue && o.Player.Gold >= cost.Value) add(new BuyExperienceAction(id));
            }

            AddBoardMoves(o, id, add);
            AddHandMoves(o, id, add);
            AddCrossMoves(o, id, add);

            foreach (CharacterObservation character in o.Characters.OrderBy(c => c.InstanceId, StringComparer.Ordinal))
                add(new SellCharacterAction(id, character.InstanceId));
            foreach (HandSlotObservation slot in o.Hand.Slots.OrderBy(s => s.SlotIndex))
                if (slot.Kind == HandSlotContentKind.Item && slot.Item != null)
                    add(new SellItemAction(id, slot.SlotIndex));

            AddHandItemUseCandidates(o, id, add);

            int boardCount = o.Board.Slots.Count(s => s.Character != null);
            bool livingBoard = o.Player.HasHealth && o.Player.HP > 0;
            bool enemyStage = o.Stage.EnemyCategory.HasValue && o.Stage.EnemyCategory.Value != StageEnemyCategory.None;
            // A successful Preparation observation guarantees an enemy board for combat choices; an explicitly empty Formation is valid.
            if (o.Stage.HasSelection && o.Stage.Choice != StageChoice.SelectB && o.Stage.Choice != StageChoice.SelectD &&
                boardCount <= o.Player.Level && livingBoard && enemyStage)
                add(new StartBattleAction(id));
        }

        private static void AddBoardMoves(GameObservation o, ActionId id, Action<GameAction> add)
        {
            var occupied = o.Board.Slots.Where(s => s.Character != null).OrderBy(s => s.SlotIndex).ToArray();
            var empty = EmptyBoardSlots(o.Board).ToArray();
            foreach (BoardSlotObservation source in occupied)
                foreach (int target in empty)
                    add(new MoveOrSwapBoardAction(id, source.SlotIndex, target));
            for (int i = 0; i < occupied.Length; i++)
                for (int j = i + 1; j < occupied.Length; j++)
                {
                    BoardSlotObservation a = occupied[i], b = occupied[j];
                    if (WouldBoardDuplicate(o.Board, a.SlotIndex, b.Character.DefinitionId,
                        b.SlotIndex, a.Character.DefinitionId)) continue;
                    add(new MoveOrSwapBoardAction(id, a.SlotIndex, b.SlotIndex));
                }
        }

        private static void AddHandMoves(GameObservation o, ActionId id, Action<GameAction> add)
        {
            HandSlotObservation[] slots = o.Hand.Slots.OrderBy(s => s.SlotIndex).ToArray();
            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i].Kind == HandSlotContentKind.Empty) continue;
                for (int j = i + 1; j < slots.Length; j++)
                    if (slots[j].Kind != HandSlotContentKind.Empty)
                        add(new MoveOrSwapHandAction(id, slots[i].SlotIndex, slots[j].SlotIndex));
                foreach (HandSlotObservation target in slots.Where(s => s.Kind == HandSlotContentKind.Empty))
                    add(new MoveOrSwapHandAction(id, slots[i].SlotIndex, target.SlotIndex));
            }
        }

        private static void AddCrossMoves(GameObservation o, ActionId id, Action<GameAction> add)
        {
            foreach (BoardSlotObservation board in o.Board.Slots.Where(s => s.Character != null).OrderBy(s => s.SlotIndex))
            foreach (HandSlotObservation hand in o.Hand.Slots.OrderBy(s => s.SlotIndex))
            {
                if (hand.Kind == HandSlotContentKind.Item) continue;
                if (hand.Kind == HandSlotContentKind.Character && WouldBoardDuplicate(o.Board,
                    board.SlotIndex, hand.Character.DefinitionId, -1, null)) continue;
                add(new MoveBoardToHandAction(id, board.SlotIndex, hand.SlotIndex));
            }

            foreach (HandSlotObservation hand in o.Hand.Slots.Where(s => s.Kind == HandSlotContentKind.Character).OrderBy(s => s.SlotIndex))
            foreach (BoardSlotObservation board in o.Board.Slots.OrderBy(s => s.SlotIndex))
            {
                if (board.Character != null && WouldBoardDuplicate(o.Board,
                    board.SlotIndex, hand.Character.DefinitionId, -1, null)) continue;
                add(new MoveHandToBoardAction(id, hand.SlotIndex, board.SlotIndex));
            }
        }

        private static void AddHandItemUseCandidates(GameObservation o, ActionId id, Action<GameAction> add)
        {
            EnemyObservation[] enemies = o.Enemies.OrderBy(e => e.SlotIndex).ToArray();
            foreach (HandSlotObservation slot in o.Hand.Slots.OrderBy(s => s.SlotIndex))
            {
                ItemObservation item = slot.Item;
                if (slot.Kind != HandSlotContentKind.Item || item == null || !IsSupportedItem(item)) continue;
                switch (item.Target)
                {
                    case ItemEffectTarget.OneAlly:
                        foreach (BoardSlotObservation target in o.Board.Slots.Where(s => s.Character != null).OrderBy(s => s.SlotIndex))
                            if (IsCharacterEligible(item, target.Character))
                                add(new UseHandItemAction(id, slot.SlotIndex,
                                    new GameActionTarget(ItemUseTargetKind.BoardCharacter, target.SlotIndex, 0, true)));
                        break;
                    case ItemEffectTarget.AllAllies:
                        if (o.Board.Slots.Count > 0)
                            add(new UseHandItemAction(id, slot.SlotIndex,
                                new GameActionTarget(ItemUseTargetKind.BoardArea, 0, 0, true)));
                        break;
                    case ItemEffectTarget.OneEnemy:
                        foreach (EnemyObservation target in enemies)
                            add(new UseHandItemAction(id, slot.SlotIndex,
                                new GameActionTarget(ItemUseTargetKind.EnemyCharacter, target.SlotIndex, 0, true)));
                        break;
                    case ItemEffectTarget.AllEnemies:
                        if (enemies.Length > 0)
                            add(new UseHandItemAction(id, slot.SlotIndex,
                                new GameActionTarget(ItemUseTargetKind.EnemyArea, -1, enemies.Length, true)));
                        break;
                    case ItemEffectTarget.Player:
                        if (o.Board.Slots.Count > 0)
                            add(new UseHandItemAction(id, slot.SlotIndex,
                                new GameActionTarget(ItemUseTargetKind.PlayerArea, 0, 0, true)));
                        break;
                }
            }
        }

        private static void AddEventCandidates(GameObservation o, ActionId id, Action<GameAction> add)
        {
            EventItemObservation eventItem = o.EventItem;
            if (eventItem == null || !eventItem.IsAvailable || eventItem.Item == null || !IsSupportedEventItem(eventItem.Item)) return;
            bool found = false;
            foreach (BoardSlotObservation slot in o.Board.Slots.Where(s => s.Character != null).OrderBy(s => s.SlotIndex))
            {
                if (!IsCharacterEligible(eventItem.Item, slot.Character)) continue;
                found = true;
                add(new UseEventItemAction(id, new GameActionTarget(ItemUseTargetKind.BoardCharacter,
                    slot.SlotIndex, 0, true)));
            }
            foreach (HandSlotObservation slot in o.Hand.Slots.Where(s => s.Kind == HandSlotContentKind.Character).OrderBy(s => s.SlotIndex))
            {
                if (!IsCharacterEligible(eventItem.Item, slot.Character)) continue;
                found = true;
                add(new UseEventItemAction(id, new GameActionTarget(ItemUseTargetKind.HandCharacter,
                    slot.SlotIndex, 0, true)));
            }
            if (!found) add(new EndEventAction(id));
        }

        private static bool IsSupportedEventItem(ItemObservation item) => item.Target == ItemEffectTarget.OneAlly &&
            item.Duration != ItemEffectDuration.Unspecified && item.Effects.Count > 0 &&
            item.Effects.All(e => e.Kind == ItemEffectKind.Attack || e.Kind == ItemEffectKind.Defense || e.Kind == ItemEffectKind.Star);

        private static bool IsSupportedItem(ItemObservation item)
        {
            if (item.Duration == ItemEffectDuration.Unspecified || item.Effects.Count == 0) return false;
            foreach (ItemEffectObservation effect in item.Effects)
            {
                bool ok = item.Target switch
                {
                    ItemEffectTarget.OneAlly => effect.Kind == ItemEffectKind.Attack || effect.Kind == ItemEffectKind.Defense || effect.Kind == ItemEffectKind.Star,
                    ItemEffectTarget.AllAllies => effect.Kind == ItemEffectKind.Attack || effect.Kind == ItemEffectKind.Defense,
                    ItemEffectTarget.OneEnemy => effect.Kind == ItemEffectKind.Attack || effect.Kind == ItemEffectKind.Defense || effect.Kind == ItemEffectKind.Damage || effect.Kind == ItemEffectKind.Gap,
                    ItemEffectTarget.AllEnemies => effect.Kind == ItemEffectKind.Attack || effect.Kind == ItemEffectKind.Defense || effect.Kind == ItemEffectKind.Damage,
                    ItemEffectTarget.Player => effect.Kind == ItemEffectKind.Damage || effect.Kind == ItemEffectKind.Block,
                    _ => false
                };
                if (!ok) return false;
            }
            return true;
        }

        private static bool IsCharacterEligible(ItemObservation item, CharacterObservation character) =>
            character != null && item.Effects.All(e => e.Kind != ItemEffectKind.Star ||
                (e.Value >= 1 && e.Value <= 3 && character.Star < e.Value));

        private static IEnumerable<int> EmptyBoardSlots(BoardObservation board) =>
            board.Slots.Where(s => s.Character == null).Select(s => s.SlotIndex).OrderBy(i => i);
        private static IEnumerable<int> EmptyHandSlots(HandObservation hand) =>
            hand.Slots.Where(s => s.Kind == HandSlotContentKind.Empty).Select(s => s.SlotIndex).OrderBy(i => i);
        private static bool BoardContainsDefinition(BoardObservation board, string id) =>
            board.Slots.Any(s => s.Character != null && s.Character.DefinitionId == id);

        private static bool WouldBoardDuplicate(BoardObservation board, int removeA, string addA, int removeB, string addB)
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (BoardSlotObservation slot in board.Slots)
            {
                if (slot.Character == null || slot.SlotIndex == removeA || slot.SlotIndex == removeB) continue;
                if (!ids.Add(slot.Character.DefinitionId)) return true;
            }
            if (addA != null && !ids.Add(addA)) return true;
            if (addB != null && !ids.Add(addB)) return true;
            return false;
        }

        private static StrategyState NewState(int prepCount, string lastSignature) =>
            new StrategyState(StableId, StableVersion, StateSchemaVersion,
                PayloadPrefix + prepCount.ToString(CultureInfo.InvariantCulture) + "|" + Convert.ToBase64String(Encoding.UTF8.GetBytes(lastSignature ?? string.Empty)));

        private static StrategyPayload ReadPayload(StrategyState state)
        {
            if (state == null || state.SchemaVersion != StateSchemaVersion || string.IsNullOrEmpty(state.Payload) ||
                !state.Payload.StartsWith(PayloadPrefix, StringComparison.Ordinal)) return new StrategyPayload(0, string.Empty);
            try
            {
                string[] parts = state.Payload.Substring(PayloadPrefix.Length).Split('|');
                if (parts.Length != 2 || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out int count) || count < 0)
                    return new StrategyPayload(0, string.Empty);
                string last = Encoding.UTF8.GetString(Convert.FromBase64String(parts[1]));
                return new StrategyPayload(count, last);
            }
            catch (FormatException) { return new StrategyPayload(0, string.Empty); }
        }

        private static bool IsImmediateReverse(string previous, GameAction next)
        {
            string[] p = previous.Split(':');
            if (p.Length < 3 || !int.TryParse(p[0], out int kind)) return false;
            if (next is MoveOrSwapBoardAction board && kind == (int)GameActionKind.MoveOrSwapBoard)
                return board.SourceSlot == Parse(p[2]) && board.TargetSlot == Parse(p[1]);
            if (next is MoveOrSwapHandAction hand && kind == (int)GameActionKind.MoveOrSwapHand)
                return hand.SourceSlot == Parse(p[2]) && hand.TargetSlot == Parse(p[1]);
            if (next is MoveBoardToHandAction b2h && kind == (int)GameActionKind.MoveHandToBoard)
                return b2h.BoardSlot == Parse(p[2]) && b2h.HandSlot == Parse(p[1]);
            if (next is MoveHandToBoardAction h2b && kind == (int)GameActionKind.MoveBoardToHand)
                return h2b.HandSlot == Parse(p[2]) && h2b.BoardSlot == Parse(p[1]);
            return false;
        }

        private static int Parse(string value) => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int result) ? result : -1;

        private static string Signature(GameAction a)
        {
            string id = a.ActionId.Value;
            switch (a)
            {
                case SelectStageChoiceAction x: return K(a, (int)x.Choice);
                case BuyCharacterAction x: return K(a, x.OfferId, (int)x.Placement.Kind, x.Placement.SlotIndex);
                case BuyItemAction x: return K(a, x.OfferId, x.HandSlot);
                case RerollShopAction x: return K(a, (int)x.ShopKind);
                case BuyExperienceAction: return K(a);
                case MoveOrSwapBoardAction x: return K(a, x.SourceSlot, x.TargetSlot);
                case MoveOrSwapHandAction x: return K(a, x.SourceSlot, x.TargetSlot);
                case MoveBoardToHandAction x: return K(a, x.BoardSlot, x.HandSlot);
                case MoveHandToBoardAction x: return K(a, x.HandSlot, x.BoardSlot);
                case SellCharacterAction x: return K(a, x.InstanceId);
                case SellItemAction x: return K(a, x.HandSlot);
                case UseHandItemAction x: return K(a, x.HandSlot, (int)x.Target.Kind, x.Target.SlotIndex, x.Target.EnemyCount, x.Target.IsPresent ? 1 : 0);
                case UseEventItemAction x: return K(a, (int)x.Target.Kind, x.Target.SlotIndex, x.Target.EnemyCount, x.Target.IsPresent ? 1 : 0);
                case EndEventAction: return K(a);
                case StartBattleAction: return K(a);
                default: return K(a, id);
            }
        }

        private static string K(GameAction action, params int[] values) => ((int)action.Kind).ToString(CultureInfo.InvariantCulture) + ":" + string.Join(":", values.Select(x => x.ToString(CultureInfo.InvariantCulture)));
        private static string K(GameAction action, string text, params int[] values) => ((int)action.Kind).ToString(CultureInfo.InvariantCulture) + ":s" + text.Length.ToString(CultureInfo.InvariantCulture) + ":" + text + ":" + string.Join(":", values.Select(x => x.ToString(CultureInfo.InvariantCulture)));
        private static string K(GameAction action, string text) => ((int)action.Kind).ToString(CultureInfo.InvariantCulture) + ":s" + text.Length.ToString(CultureInfo.InvariantCulture) + ":" + text;
        private static int CompareActions(GameAction left, GameAction right)
        {
            int c = ((int)left.Kind).CompareTo((int)right.Kind);
            if (c != 0) return c;
            switch (left)
            {
                case SelectStageChoiceAction a: return ((int)a.Choice).CompareTo((int)((SelectStageChoiceAction)right).Choice);
                case BuyCharacterAction a:
                    var bc = (BuyCharacterAction)right;
                    c = string.CompareOrdinal(a.OfferId, bc.OfferId);
                    if (c != 0) return c;
                    c = ((int)a.Placement.Kind).CompareTo((int)bc.Placement.Kind);
                    return c != 0 ? c : a.Placement.SlotIndex.CompareTo(bc.Placement.SlotIndex);
                case BuyItemAction a:
                    var bi = (BuyItemAction)right;
                    c = string.CompareOrdinal(a.OfferId, bi.OfferId);
                    return c != 0 ? c : a.HandSlot.CompareTo(bi.HandSlot);
                case RerollShopAction a: return ((int)a.ShopKind).CompareTo((int)((RerollShopAction)right).ShopKind);
                case MoveOrSwapBoardAction a:
                    var bb = (MoveOrSwapBoardAction)right;
                    c = a.SourceSlot.CompareTo(bb.SourceSlot); return c != 0 ? c : a.TargetSlot.CompareTo(bb.TargetSlot);
                case MoveOrSwapHandAction a:
                    var bh = (MoveOrSwapHandAction)right;
                    c = a.SourceSlot.CompareTo(bh.SourceSlot); return c != 0 ? c : a.TargetSlot.CompareTo(bh.TargetSlot);
                case MoveBoardToHandAction a:
                    var b2h = (MoveBoardToHandAction)right;
                    c = a.BoardSlot.CompareTo(b2h.BoardSlot); return c != 0 ? c : a.HandSlot.CompareTo(b2h.HandSlot);
                case MoveHandToBoardAction a:
                    var h2b = (MoveHandToBoardAction)right;
                    c = a.HandSlot.CompareTo(h2b.HandSlot); return c != 0 ? c : a.BoardSlot.CompareTo(h2b.BoardSlot);
                case SellCharacterAction a: return string.CompareOrdinal(a.InstanceId, ((SellCharacterAction)right).InstanceId);
                case SellItemAction a: return a.HandSlot.CompareTo(((SellItemAction)right).HandSlot);
                case UseHandItemAction a:
                    var ui = (UseHandItemAction)right;
                    c = a.HandSlot.CompareTo(ui.HandSlot); return c != 0 ? c : CompareTargets(a.Target, ui.Target);
                case UseEventItemAction a: return CompareTargets(a.Target, ((UseEventItemAction)right).Target);
                default: return 0;
            }
        }
        private static int CompareTargets(GameActionTarget a, GameActionTarget b)
        {
            int c = ((int)a.Kind).CompareTo((int)b.Kind);
            if (c != 0) return c;
            c = a.SlotIndex.CompareTo(b.SlotIndex); if (c != 0) return c;
            c = a.EnemyCount.CompareTo(b.EnemyCount); return c != 0 ? c : a.IsPresent.CompareTo(b.IsPresent);
        }
        private sealed class StrategyPayload
        {
            public int PreparationDecisionCount { get; }
            public string LastActionSignature { get; }
            public StrategyPayload(int count, string signature) { PreparationDecisionCount = count; LastActionSignature = signature; }
        }
    }
}






