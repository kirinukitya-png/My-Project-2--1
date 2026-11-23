using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using ProjectNST.GameCore.Randomness;

namespace ProjectNST.GameCore.Battle
{
    public enum BattleExecutionStatus { InProgress, Completed }
    public enum BattlePhase
    {
        BattleStart,
        RoundStart,
        PlayerTurn,
        EnemyTurnStart,
        EnemyTurn,
        RoundEnd,
        RoundLimitReached,
        Completed
    }
    public enum BattleOutcome { Pending, Victory, Defeat, Unresolved, Undecided = Unresolved }
    public enum BattleActionKind { None, BattleStart, Ally, EnemyTurnStart, Enemy }

    /// <summary>Immutable, presentation-free state sufficient to start and checkpoint a battle.</summary>
    public sealed class BattleState
    {
        private readonly BattleUnitState[] _allies;
        private readonly BattleUnitState[] _enemies;
        private readonly UnitMoveState[] _unitMoves;
        private readonly ReadOnlyCollection<BattleUnitState> _readOnlyAllies;
        private readonly ReadOnlyCollection<BattleUnitState> _readOnlyEnemies;
        private readonly ReadOnlyCollection<UnitMoveState> _readOnlyUnitMoves;
        private readonly ReadOnlyCollection<string> _usedUnitMoveIds;
        private readonly ReadOnlyDictionary<string, int> _participationCounts;
        private readonly ReadOnlyCollection<BattlePendingEffectTrigger> _pendingEffectTriggers;
        private readonly ReadOnlyCollection<string> _processedEffectTriggerIds;
        private readonly BattlePersistentEffectCommand[] _persistentEffectCommands;
        private readonly ReadOnlyCollection<BattlePersistentEffectCommand> _readOnlyPersistentEffectCommands;

        public BattleExecutionStatus Status { get; }
        public int EncounterIndex { get; }
        public int CurrentRound { get; }
        public BattlePhase Phase { get; }
        public IReadOnlyList<BattleUnitState> Allies => _readOnlyAllies;
        public IReadOnlyList<BattleUnitState> Enemies => _readOnlyEnemies;
        public int PlayerStartingHP { get; }
        public int PlayerCurrentHP { get; }
        public int PlayerMaximumHP { get; }
        public int PendingPlayerHpDelta { get; }
        public int PlayerBlock { get; }
        public long TotalBlockAbsorbed { get; }
        public int AttributeBlock { get; }
        public int PendingPlayerBlockBonus { get; }
        public int PermanentPlayerBlockBonus { get; }
        public IReadOnlyList<UnitMoveState> UnitMoves => _readOnlyUnitMoves;
        public IReadOnlyList<string> UsedUnitMoveIds => _usedUnitMoveIds;
        public IReadOnlyDictionary<string, int> ParticipationCounts => _participationCounts;
        public Pcg32State BattleRandomState { get; }
        public BattleUnitSide NextActorSide { get; }
        public int NextActorIndex { get; }
        public int MaxRounds { get; }
        public BattleOutcome Outcome { get; }
        public bool BattleOutcomeCheckPending { get; }
        public string PendingActionRequestId { get; }
        public BattleActionKind PendingActionKind { get; }
        public string PendingActorInstanceId { get; }
        public int NextActionRequestSequence { get; }
        public IReadOnlyList<BattlePendingEffectTrigger> PendingEffectTriggers => _pendingEffectTriggers;
        public IReadOnlyList<string> ProcessedEffectTriggerIds => _processedEffectTriggerIds;
        /// <summary>Persistent character commands emitted by resolved triggers; callers commit them atomically with each effect result.</summary>
        public IReadOnlyList<BattlePersistentEffectCommand> PersistentEffectCommands => _readOnlyPersistentEffectCommands;

        internal BattleState(int encounterIndex, int playerHP, int playerBlock, int attributeBlock,
            int pendingPlayerBlockBonus, int permanentPlayerBlockBonus,
            IEnumerable<BattleUnitState> allies, IEnumerable<BattleUnitState> enemies,
            IEnumerable<UnitMoveState> unitMoves, Pcg32State battleRandomState, int maxRounds,
            int currentRound = 1, BattlePhase phase = BattlePhase.BattleStart,
            BattleUnitSide nextActorSide = BattleUnitSide.Ally, int nextActorIndex = 0,
            IEnumerable<string> usedUnitMoveIds = null,
            IReadOnlyDictionary<string, int> participationCounts = null,
            string pendingActionRequestId = null,
            BattleActionKind pendingActionKind = BattleActionKind.None,
            string pendingActorInstanceId = null, int nextActionRequestSequence = 0,
            BattleExecutionStatus status = BattleExecutionStatus.InProgress,
            BattleOutcome outcome = BattleOutcome.Pending,
            int? playerCurrentHP = null,
            IEnumerable<BattlePendingEffectTrigger> pendingEffectTriggers = null,
            int? playerMaximumHP = null,
            int pendingPlayerHpDelta = 0,
            IEnumerable<string> processedEffectTriggerIds = null,
            bool battleOutcomeCheckPending = false,
            long totalBlockAbsorbed = 0,
            IEnumerable<BattlePersistentEffectCommand> persistentEffectCommands = null)
        {
            if (encounterIndex < 0) throw new ArgumentOutOfRangeException(nameof(encounterIndex));
            int maximumHP = playerMaximumHP ?? playerHP;
            if (playerHP < 0 || (playerCurrentHP.HasValue && playerCurrentHP.Value < 0) ||
                maximumHP < playerHP || (playerCurrentHP.HasValue && playerCurrentHP.Value > maximumHP) ||
                playerBlock < 0 || attributeBlock < 0 || maxRounds <= 0 ||
                currentRound < 1 || currentRound > maxRounds || nextActorIndex < 0 ||
                nextActionRequestSequence < 0 || totalBlockAbsorbed < 0)
                throw new ArgumentOutOfRangeException("Battle state values must be non-negative and MaxRounds must be positive.");
            if (!Enum.IsDefined(typeof(BattlePhase), phase) ||
                !Enum.IsDefined(typeof(BattleUnitSide), nextActorSide) ||
                !Enum.IsDefined(typeof(BattleActionKind), pendingActionKind) ||
                !Enum.IsDefined(typeof(BattleExecutionStatus), status) ||
                !Enum.IsDefined(typeof(BattleOutcome), outcome))
                throw new ArgumentOutOfRangeException(nameof(phase));
            _allies = Copy(allies, nameof(allies));
            _enemies = Copy(enemies, nameof(enemies));
            _unitMoves = Copy(unitMoves, nameof(unitMoves));
            if (Array.Exists(_allies, unit => unit.Side != BattleUnitSide.Ally) ||
                Array.Exists(_enemies, unit => unit.Side != BattleUnitSide.Enemy))
                throw new ArgumentException("Battle unit sides do not match their collection.");
            ValidateUniqueUnits(_allies, nameof(allies));
            ValidateUniqueUnits(_enemies, nameof(enemies));
            if (nextActorIndex > Math.Max(_allies.Length, _enemies.Length))
                throw new ArgumentOutOfRangeException(nameof(nextActorIndex));
            _readOnlyAllies = Array.AsReadOnly(_allies);
            _readOnlyEnemies = Array.AsReadOnly(_enemies);
            _readOnlyUnitMoves = Array.AsReadOnly(_unitMoves);
            _usedUnitMoveIds = Array.AsReadOnly(CopyUniqueStrings(usedUnitMoveIds, nameof(usedUnitMoveIds)));
            var countCopy = new Dictionary<string, int>(StringComparer.Ordinal);
            if (participationCounts != null)
                foreach (KeyValuePair<string, int> pair in participationCounts)
                {
                    if (string.IsNullOrWhiteSpace(pair.Key) || pair.Value < 0 ||
                        !ContainsAlly(pair.Key, out BattleUnitState participant) ||
                        pair.Value > ClampParticipationLimit(participant.Star))
                        throw new ArgumentException("Participation counts must reference allies and stay within their Star limits.", nameof(participationCounts));
                    countCopy.Add(pair.Key, pair.Value);
                }
            _participationCounts = new ReadOnlyDictionary<string, int>(countCopy);
            var effectCopy = pendingEffectTriggers == null
                ? new List<BattlePendingEffectTrigger>()
                : new List<BattlePendingEffectTrigger>(pendingEffectTriggers);
            if (effectCopy.Exists(effect => effect == null))
                throw new ArgumentException("Pending effect triggers cannot contain null.", nameof(pendingEffectTriggers));
            var effectIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (BattlePendingEffectTrigger effect in effectCopy)
                if (!effectIds.Add(effect.TriggerId))
                    throw new ArgumentException("Pending effect trigger IDs must be unique.", nameof(pendingEffectTriggers));
            _pendingEffectTriggers = effectCopy.AsReadOnly();
            string[] processed = CopyUniqueStrings(processedEffectTriggerIds, nameof(processedEffectTriggerIds));
            var processedSet = new HashSet<string>(processed, StringComparer.Ordinal);
            foreach (BattlePendingEffectTrigger trigger in effectCopy)
                if (processedSet.Contains(trigger.TriggerId))
                    throw new ArgumentException("Pending effect triggers cannot already be processed.", nameof(pendingEffectTriggers));
            _processedEffectTriggerIds = Array.AsReadOnly(processed);

            var commandCopy = persistentEffectCommands == null
                ? new List<BattlePersistentEffectCommand>()
                : new List<BattlePersistentEffectCommand>(persistentEffectCommands);
            if (commandCopy.Exists(command => command == null))
                throw new ArgumentException("Persistent effect commands cannot contain null.", nameof(persistentEffectCommands));
            _persistentEffectCommands = commandCopy.ToArray();
            _readOnlyPersistentEffectCommands = Array.AsReadOnly(_persistentEffectCommands);

            foreach (string moveId in _usedUnitMoveIds)
                if (Array.FindIndex(_unitMoves, move => string.Equals(move.MoveId, moveId, StringComparison.Ordinal)) < 0)
                    throw new ArgumentException("Used UnitMove IDs must refer to an established UnitMove.", nameof(usedUnitMoveIds));
            if (pendingActionKind == BattleActionKind.None)
            {
                if (pendingActionRequestId != null || pendingActorInstanceId != null)
                    throw new ArgumentException("Pending action data must be empty when no action is pending.");
            }
            else if (string.IsNullOrWhiteSpace(pendingActionRequestId))
                throw new ArgumentException("A pending action requires a request ID.", nameof(pendingActionRequestId));
            if (pendingActionKind == BattleActionKind.Ally && string.IsNullOrWhiteSpace(pendingActorInstanceId))
                throw new ArgumentException("A pending ally action requires an actor ID.", nameof(pendingActorInstanceId));
            if (pendingActionKind == BattleActionKind.Enemy && string.IsNullOrWhiteSpace(pendingActorInstanceId))
                throw new ArgumentException("A pending enemy action requires an actor ID.", nameof(pendingActorInstanceId));

            Status = status;
            EncounterIndex = encounterIndex;
            CurrentRound = currentRound;
            Phase = phase;
            PlayerStartingHP = playerHP;
            PlayerCurrentHP = playerCurrentHP ?? playerHP;
            PlayerMaximumHP = maximumHP;
            PendingPlayerHpDelta = pendingPlayerHpDelta;
            PlayerBlock = playerBlock;
            TotalBlockAbsorbed = totalBlockAbsorbed;
            AttributeBlock = attributeBlock;
            PendingPlayerBlockBonus = pendingPlayerBlockBonus;
            PermanentPlayerBlockBonus = permanentPlayerBlockBonus;
            BattleRandomState = battleRandomState;
            NextActorSide = nextActorSide;
            NextActorIndex = nextActorIndex;
            MaxRounds = maxRounds;
            Outcome = outcome;
            BattleOutcomeCheckPending = battleOutcomeCheckPending;
            PendingActionRequestId = pendingActionRequestId;
            PendingActionKind = pendingActionKind;
            PendingActorInstanceId = pendingActorInstanceId;
            NextActionRequestSequence = nextActionRequestSequence;
        }

        /// <summary>Replaces one ally snapshot immutably, preserving its stable identity and slot.</summary>
        public bool TryWithAllyState(BattleUnitState replacement, out BattleState nextState)
        {
            nextState = this;
            int index = FindSameUnit(_allies, replacement, BattleUnitSide.Ally);
            if (index < 0) return false;
            BattleUnitState[] nextAllies = (BattleUnitState[])_allies.Clone();
            nextAllies[index] = replacement;
            nextState = CopyWith(nextAllies, null);
            return true;
        }

        /// <summary>Replaces one enemy snapshot immutably, preserving its stable identity and slot.</summary>
        public bool TryWithEnemyState(BattleUnitState replacement, out BattleState nextState)
        {
            nextState = this;
            int index = FindSameUnit(_enemies, replacement, BattleUnitSide.Enemy);
            if (index < 0) return false;
            BattleUnitState[] nextEnemies = (BattleUnitState[])_enemies.Clone();
            nextEnemies[index] = replacement;
            nextState = CopyWith(null, nextEnemies);
            return true;
        }

        internal BattleState CopyWith(BattleUnitState[] allies = null, BattleUnitState[] enemies = null,
            int? currentRound = null, BattlePhase? phase = null, BattleUnitSide? nextActorSide = null,
            int? nextActorIndex = null, IEnumerable<string> usedUnitMoveIds = null,
            IReadOnlyDictionary<string, int> participationCounts = null,
            bool replaceRoundRecords = false, string pendingActionRequestId = null,
            BattleActionKind? pendingActionKind = null, string pendingActorInstanceId = null,
            int? nextActionRequestSequence = null, bool replacePendingAction = false,
            int? playerCurrentHP = null, int? playerBlock = null,
            int? pendingPlayerBlockBonus = null, int? permanentPlayerBlockBonus = null,
            bool? battleOutcomeCheckPending = null,
            Pcg32State? battleRandomState = null,
            IEnumerable<BattlePendingEffectTrigger> pendingEffectTriggers = null,
            bool replacePendingEffectTriggers = false,
            int? playerMaximumHP = null, int? pendingPlayerHpDelta = null,
            IEnumerable<string> processedEffectTriggerIds = null,
            bool replaceProcessedEffectTriggerIds = false,
            BattleExecutionStatus? status = null,
            BattleOutcome? outcome = null,
            long? totalBlockAbsorbed = null,
            IEnumerable<BattlePersistentEffectCommand> persistentEffectCommands = null,
            bool replacePersistentEffectCommands = false)
        {
            IEnumerable<string> moves = replaceRoundRecords ? usedUnitMoveIds : _usedUnitMoveIds;
            IReadOnlyDictionary<string, int> counts = replaceRoundRecords ? participationCounts : _participationCounts;
            return new BattleState(EncounterIndex, PlayerStartingHP, playerBlock ?? PlayerBlock, AttributeBlock,
                pendingPlayerBlockBonus ?? PendingPlayerBlockBonus,
                permanentPlayerBlockBonus ?? PermanentPlayerBlockBonus,
                allies ?? _allies, enemies ?? _enemies, _unitMoves, battleRandomState ?? BattleRandomState, MaxRounds,
                currentRound ?? CurrentRound, phase ?? Phase, nextActorSide ?? NextActorSide,
                nextActorIndex ?? NextActorIndex, moves, counts,
                replacePendingAction ? pendingActionRequestId : PendingActionRequestId,
                replacePendingAction ? pendingActionKind ?? BattleActionKind.None : PendingActionKind,
                replacePendingAction ? pendingActorInstanceId : PendingActorInstanceId,
                nextActionRequestSequence ?? NextActionRequestSequence,
                status ?? Status, outcome ?? Outcome, playerCurrentHP ?? PlayerCurrentHP,
                replacePendingEffectTriggers ? pendingEffectTriggers : _pendingEffectTriggers,
                playerMaximumHP ?? PlayerMaximumHP, pendingPlayerHpDelta ?? PendingPlayerHpDelta,
                replaceProcessedEffectTriggerIds ? processedEffectTriggerIds : _processedEffectTriggerIds,
                battleOutcomeCheckPending ?? BattleOutcomeCheckPending,
                totalBlockAbsorbed ?? TotalBlockAbsorbed,
                replacePersistentEffectCommands ? persistentEffectCommands : _persistentEffectCommands);
        }

        private static BattleUnitState[] Copy(IEnumerable<BattleUnitState> source, string name)
        {
            var copy = source == null ? new List<BattleUnitState>() : new List<BattleUnitState>(source);
            if (copy.Exists(unit => unit == null)) throw new ArgumentException("Battle units cannot contain null.", name);
            return copy.ToArray();
        }

        private static UnitMoveState[] Copy(IEnumerable<UnitMoveState> source, string name)
        {
            var copy = source == null ? new List<UnitMoveState>() : new List<UnitMoveState>(source);
            if (copy.Exists(move => move == null)) throw new ArgumentException("UnitMoves cannot contain null.", name);
            return copy.ToArray();
        }

        private bool ContainsAlly(string instanceId, out BattleUnitState ally)
        {
            foreach (BattleUnitState unit in _allies)
                if (string.Equals(unit.InstanceId, instanceId, StringComparison.Ordinal))
                { ally = unit; return true; }
            ally = null;
            return false;
        }

        private static int FindSameUnit(BattleUnitState[] units, BattleUnitState replacement,
            BattleUnitSide expectedSide)
        {
            if (replacement == null || replacement.Side != expectedSide) return -1;
            for (int i = 0; i < units.Length; i++)
                if (string.Equals(units[i].InstanceId, replacement.InstanceId, StringComparison.Ordinal) &&
                    string.Equals(units[i].DefinitionId, replacement.DefinitionId, StringComparison.Ordinal) &&
                    units[i].SlotIndex == replacement.SlotIndex) return i;
            return -1;
        }

        private static void ValidateUniqueUnits(BattleUnitState[] units, string parameter)
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var slots = new HashSet<int>();
            int lastSlot = -1;
            foreach (BattleUnitState unit in units)
            {
                if (!ids.Add(unit.InstanceId) || !slots.Add(unit.SlotIndex) || unit.SlotIndex < lastSlot)
                    throw new ArgumentException("Battle units must have unique IDs and ascending slot order.", parameter);
                lastSlot = unit.SlotIndex;
            }
        }

        private static string[] CopyUniqueStrings(IEnumerable<string> source, string parameter)
        {
            var copy = source == null ? new List<string>() : new List<string>(source);
            var unique = new HashSet<string>(StringComparer.Ordinal);
            foreach (string value in copy)
                if (string.IsNullOrWhiteSpace(value) || !unique.Add(value))
                    throw new ArgumentException("Values must be non-empty and unique.", parameter);
            return copy.ToArray();
        }

        private static int ClampParticipationLimit(int star) => Math.Max(1, Math.Min(3, star));
    }
}
