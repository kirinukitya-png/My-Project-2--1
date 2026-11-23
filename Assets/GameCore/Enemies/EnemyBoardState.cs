using System;
using System.Collections.Generic;

namespace ProjectNST.GameCore.Enemies
{
    public enum EnemyBoardFailure
    {
        None,
        InvalidDimensions,
        InvalidSlotIndex,
        NullInstance,
        SlotOccupied,
        DuplicateInstanceId
    }

    public sealed class EnemyBoardOperationResult
    {
        public bool Succeeded { get; }
        public EnemyBoardFailure Failure { get; }
        public EnemyBoardState NextState { get; }

        internal EnemyBoardOperationResult(bool succeeded, EnemyBoardFailure failure, EnemyBoardState nextState)
        {
            Succeeded = succeeded;
            Failure = failure;
            NextState = nextState;
        }
    }

    /// <summary>Immutable 2D enemy placement. It stores immutable EnemyInstance values by slot.</summary>
    public sealed class EnemyBoardState
    {
        public const int StandardColumns = 2;
        public const int StandardRows = 3;
        private readonly EnemyInstance[] _slots;

        public int Columns { get; }
        public int Rows { get; }
        public int SlotCount => _slots.Length;

        private EnemyBoardState(int columns, int rows, EnemyInstance[] slots)
        {
            Columns = columns;
            Rows = rows;
            _slots = (EnemyInstance[])slots.Clone();
        }

        public static EnemyBoardState CreateEmpty(int columns = StandardColumns, int rows = StandardRows)
        {
            if (columns <= 0) throw new ArgumentOutOfRangeException(nameof(columns));
            if (rows <= 0) throw new ArgumentOutOfRangeException(nameof(rows));
            int count = checked(columns * rows);
            return new EnemyBoardState(columns, rows, new EnemyInstance[count]);
        }

        public bool TryGetAt(int slotIndex, out EnemyInstance instance)
        {
            instance = null;
            if (slotIndex < 0 || slotIndex >= _slots.Length) return false;
            instance = _slots[slotIndex];
            return true;
        }

        public bool TryFindInstance(string instanceId, out int slotIndex)
        {
            slotIndex = -1;
            if (string.IsNullOrWhiteSpace(instanceId)) return false;
            for (int i = 0; i < _slots.Length; i++)
            {
                if (_slots[i] != null && string.Equals(_slots[i].InstanceId, instanceId, StringComparison.Ordinal))
                {
                    slotIndex = i;
                    return true;
                }
            }
            return false;
        }

        public EnemyBoardOperationResult TryPlace(EnemyInstance instance)
        {
            if (instance == null)
                return new EnemyBoardOperationResult(false, EnemyBoardFailure.NullInstance, this);
            int slot = instance.SlotIndex;
            if (slot < 0 || slot >= _slots.Length)
                return new EnemyBoardOperationResult(false, EnemyBoardFailure.InvalidSlotIndex, this);
            if (TryFindInstance(instance.InstanceId, out _))
                return new EnemyBoardOperationResult(false, EnemyBoardFailure.DuplicateInstanceId, this);
            if (_slots[slot] != null)
                return new EnemyBoardOperationResult(false, EnemyBoardFailure.SlotOccupied, this);

            EnemyInstance[] next = (EnemyInstance[])_slots.Clone();
            next[slot] = instance;
            return new EnemyBoardOperationResult(true, EnemyBoardFailure.None,
                new EnemyBoardState(Columns, Rows, next));
        }

        public EnemyBoardState Clone() => new EnemyBoardState(Columns, Rows, _slots);

        /// <summary>Clears the previous encounter when the next stage starts.</summary>
        public EnemyBoardState ClearForNextStage() => CreateEmpty(Columns, Rows);

        /// <summary>Clears enemy placements after the run is completed.</summary>
        public EnemyBoardState ClearForCompleted() => CreateEmpty(Columns, Rows);

        public static bool TryCreate(int columns, int rows, IEnumerable<EnemyInstance> instances,
            out EnemyBoardState state, out EnemyBoardFailure failure)
        {
            state = null;
            if (columns <= 0 || rows <= 0)
            {
                failure = EnemyBoardFailure.InvalidDimensions;
                return false;
            }

            int count;
            try { count = checked(columns * rows); }
            catch (OverflowException)
            {
                failure = EnemyBoardFailure.InvalidDimensions;
                return false;
            }

            var slots = new EnemyInstance[count];
            var ids = new HashSet<string>(StringComparer.Ordinal);
            if (instances != null)
            {
                foreach (EnemyInstance instance in instances)
                {
                    if (instance == null)
                    {
                        failure = EnemyBoardFailure.NullInstance;
                        return false;
                    }
                    if (instance.SlotIndex < 0 || instance.SlotIndex >= count)
                    {
                        failure = EnemyBoardFailure.InvalidSlotIndex;
                        return false;
                    }
                    if (!ids.Add(instance.InstanceId))
                    {
                        failure = EnemyBoardFailure.DuplicateInstanceId;
                        return false;
                    }
                    if (slots[instance.SlotIndex] != null)
                    {
                        failure = EnemyBoardFailure.SlotOccupied;
                        return false;
                    }
                    slots[instance.SlotIndex] = instance;
                }
            }

            state = new EnemyBoardState(columns, rows, slots);
            failure = EnemyBoardFailure.None;
            return true;
        }
    }
}
