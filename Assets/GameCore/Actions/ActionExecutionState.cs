using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using ProjectNST.GameCore.Battle;
using ProjectNST.GameCore.Board;
using ProjectNST.GameCore.Characters;
using ProjectNST.GameCore.Economy;
using ProjectNST.GameCore.Enemies;
using ProjectNST.GameCore.Items;
using ProjectNST.GameCore.Randomness;
using ProjectNST.GameCore.Shop;
using ProjectNST.GameCore.Stages;

namespace ProjectNST.GameCore.Actions
{
    /// <summary>
    /// Temporary immutable aggregate for action calculation. CharacterOwnershipState is the only
    /// owner of CharacterInstance objects; Board/Hand contain IDs, Economy owns Gold/HP/XP/Level,
    /// and this object does not duplicate those values.
    /// </summary>
    public sealed class ActionExecutionState
    {
        private readonly ReadOnlyCollection<ActionId> _executedActionIds;
        private readonly HashSet<string> _executedActionIdSet;
        private readonly CharacterInstanceFactory _instanceFactory;

        public StageProgressState Stage { get; }
        public EconomyState Economy { get; }
        public ShopState Shop { get; }
        public CharacterPool CharacterPool { get; }
        /// <summary>Returns a transaction-local copy; callers cannot mutate the aggregate's ID sequence.</summary>
        public CharacterInstanceFactory InstanceFactory => _instanceFactory.CreateFork();
        public BoardState Board { get; }
        public HandState Hand { get; }
        public CharacterOwnershipState Ownership { get; }
        public ItemEffectState ItemEffects { get; }
        public EventItemState EventItem { get; }
        public EnemyBoardState EnemyBoard { get; }
        public EnemyPreparationState EnemyPreparation { get; }
        public BattleState Battle { get; }
        public BattleResult BattleResult { get; }
        public SimulationRandomStreamsState RandomState { get; }
        public IReadOnlyList<ActionId> ExecutedActionIds => _executedActionIds;

        public ActionExecutionState(StageProgressState stage, EconomyState economy,
            CharacterPool characterPool, CharacterInstanceFactory instanceFactory,
            BoardState board, HandState hand,
            CharacterOwnershipState ownership, ItemEffectState itemEffects,
            SimulationRandomStreamsState randomState, ShopState shop = null,
            EventItemState eventItem = null, EnemyBoardState enemyBoard = null,
            EnemyPreparationState enemyPreparation = null, BattleState battle = null,
            BattleResult battleResult = null, IEnumerable<ActionId> executedActionIds = null)
        {
            if (stage == null) throw new ArgumentNullException(nameof(stage));
            if (economy == null) throw new ArgumentNullException(nameof(economy));
            if (characterPool == null) throw new ArgumentNullException(nameof(characterPool));
            if (instanceFactory == null) throw new ArgumentNullException(nameof(instanceFactory));
            if (board == null) throw new ArgumentNullException(nameof(board));
            if (hand == null) throw new ArgumentNullException(nameof(hand));
            if (ownership == null) throw new ArgumentNullException(nameof(ownership));
            if (itemEffects == null) throw new ArgumentNullException(nameof(itemEffects));
            ValidateRandomState(randomState);
            // Stage 9 battle rules keep their working Battle RNG in BattleState. The aggregate's
            // seven-stream checkpoint mirrors that one stream so there is only one accepted value.
            if (battle != null && !battle.BattleRandomState.Equals(randomState.Battle))
                throw new ArgumentException("BattleState and aggregate Battle RNG checkpoints must match.", nameof(randomState));
            if (battleResult != null && !battleResult.BattleRandomCheckpoint.Equals(randomState.Battle))
                throw new ArgumentException("BattleResult and aggregate Battle RNG checkpoints must match.", nameof(randomState));
            BoardHandStateValidationResult validation = BoardHandStateValidator.Validate(board, hand, ownership);
            if (!validation.IsValid)
                throw new ArgumentException("Board, Hand, and Ownership are inconsistent: " + string.Join("; ", validation.Errors), nameof(board));

            var ids = new List<ActionId>();
            _executedActionIdSet = new HashSet<string>(StringComparer.Ordinal);
            if (executedActionIds != null)
            {
                foreach (ActionId id in executedActionIds)
                {
                    if (string.IsNullOrWhiteSpace(id.Value))
                        throw new ArgumentException("Executed Action IDs must be non-empty.", nameof(executedActionIds));
                    if (!_executedActionIdSet.Add(id.Value))
                        throw new ArgumentException("Executed Action IDs must be unique using ordinal comparison.", nameof(executedActionIds));
                    ids.Add(id);
                }
            }
            _executedActionIds = Array.AsReadOnly(ids.ToArray());

            Stage = stage; Economy = economy; Shop = shop; CharacterPool = characterPool;
            _instanceFactory = instanceFactory.CreateFork();
            Board = board; Hand = hand; Ownership = ownership; ItemEffects = itemEffects;
            EventItem = eventItem; EnemyBoard = enemyBoard; EnemyPreparation = enemyPreparation;
            Battle = battle; BattleResult = battleResult; RandomState = randomState;
        }

        public bool HasExecuted(ActionId actionId) =>
            !string.IsNullOrWhiteSpace(actionId.Value) && _executedActionIdSet.Contains(actionId.Value);

        internal bool HasSameActionHistory(ActionExecutionState other)
        {
            if (other == null || _executedActionIds.Count != other._executedActionIds.Count) return false;
            for (int i = 0; i < _executedActionIds.Count; i++)
                if (_executedActionIds[i] != other._executedActionIds[i]) return false;
            return true;
        }

        internal ActionExecutionState WithRecordedAction(ActionId actionId)
        {
            if (HasExecuted(actionId)) throw new InvalidOperationException("Action ID is already recorded.");
            var ids = new List<ActionId>(_executedActionIds) { actionId };
            return new ActionExecutionState(Stage, Economy, CharacterPool, InstanceFactory,
                Board, Hand, Ownership, ItemEffects, RandomState, Shop, EventItem, EnemyBoard, EnemyPreparation, Battle,
                BattleResult, ids);
        }

        public ActionExecutionState With(StageProgressState stage = null, EconomyState economy = null,
            ShopState shop = null, CharacterPool characterPool = null, BoardState board = null,
            CharacterInstanceFactory instanceFactory = null,
            HandState hand = null, CharacterOwnershipState ownership = null,
            ItemEffectState itemEffects = null, EventItemState eventItem = null,
            EnemyBoardState enemyBoard = null, EnemyPreparationState enemyPreparation = null,
            BattleState battle = null, BattleResult battleResult = null,
            SimulationRandomStreamsState? randomState = null)
        {
            // Optional references use null as "retain current". To clear an optional object,
            // construct a new ActionExecutionState explicitly with null for that field.
            return new ActionExecutionState(stage ?? Stage, economy ?? Economy,
                characterPool ?? CharacterPool, instanceFactory ?? InstanceFactory, board ?? Board, hand ?? Hand,
                ownership ?? Ownership, itemEffects ?? ItemEffects,
                randomState ?? RandomState, shop ?? Shop, eventItem ?? EventItem,
                enemyBoard ?? EnemyBoard, enemyPreparation ?? EnemyPreparation,
                battle ?? Battle, battleResult ?? BattleResult, _executedActionIds);
        }

        internal static void ValidateRandomState(SimulationRandomStreamsState state)
        {
            if (state.Version != SimulationRandomStreamsState.CurrentVersion)
                throw new ArgumentException("Unsupported random checkpoint version.", nameof(state));
            try
            {
                Pcg32RandomSource.ValidateState(state.Shop);
                Pcg32RandomSource.ValidateState(state.Stats);
                Pcg32RandomSource.ValidateState(state.Encounter);
                Pcg32RandomSource.ValidateState(state.Battle);
                Pcg32RandomSource.ValidateState(state.Event);
                Pcg32RandomSource.ValidateState(state.Roster);
                Pcg32RandomSource.ValidateState(state.Economy);
            }
            catch (ArgumentException ex)
            {
                throw new ArgumentException("Invalid random checkpoint.", nameof(state), ex);
            }
        }
    }
}
