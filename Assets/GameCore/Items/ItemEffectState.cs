using System;
using System.Collections.Generic;
using ProjectNST.GameCore.Shop;

namespace ProjectNST.GameCore.Items
{
    /// <summary>Enemy-side command emitted by the item rules; does not mutate enemy state.</summary>
    public sealed class ItemEffectCommand
    {
        public ItemEffectKind Effect { get; }
        public ItemEffectTarget Target { get; }
        public int Value { get; }
        public int EnemySlotIndex { get; }
        public ItemEffectDuration Duration { get; }

        internal ItemEffectCommand(ItemEffectKind effect, ItemEffectTarget target, int value,
            int enemySlotIndex, ItemEffectDuration duration)
        {
            Effect = effect;
            Target = target;
            Value = value;
            EnemySlotIndex = enemySlotIndex;
            Duration = duration;
        }
    }

    /// <summary>Immutable player-side temporary effects and pending enemy commands for one run.</summary>
    public sealed class ItemEffectState
    {
        private readonly ItemEffectCommand[] _enemyCommands;
        public int PermanentPlayerBlockBonus { get; }
        public int TemporaryPlayerBlockBonus { get; }
        public int TemporaryPlayerHpDelta { get; }
        public IReadOnlyList<ItemEffectCommand> EnemyCommands { get; }
        public bool HasActiveBattleEffects { get; }

        public ItemEffectState() : this(0, 0, 0, Array.Empty<ItemEffectCommand>(), false) { }

        internal ItemEffectState(int permanentPlayerBlockBonus, int temporaryPlayerBlockBonus,
            int temporaryPlayerHpDelta, IEnumerable<ItemEffectCommand> enemyCommands,
            bool hasActiveBattleEffects)
        {
            var copy = new List<ItemEffectCommand>(enemyCommands ?? Array.Empty<ItemEffectCommand>());
            if (copy.Exists(command => command == null))
                throw new ArgumentException("Enemy commands cannot contain null entries.", nameof(enemyCommands));
            PermanentPlayerBlockBonus = permanentPlayerBlockBonus;
            TemporaryPlayerBlockBonus = temporaryPlayerBlockBonus;
            TemporaryPlayerHpDelta = temporaryPlayerHpDelta;
            _enemyCommands = copy.ToArray();
            EnemyCommands = Array.AsReadOnly(_enemyCommands);
            HasActiveBattleEffects = hasActiveBattleEffects;
        }

        internal bool TryWith(int permanentBlockDelta, int temporaryBlockDelta, int temporaryHpDelta,
            IEnumerable<ItemEffectCommand> additionalCommands, bool addBattleEffects,
            out ItemEffectState next)
        {
            next = this;
            long permanent = (long)PermanentPlayerBlockBonus + permanentBlockDelta;
            long temporary = (long)TemporaryPlayerBlockBonus + temporaryBlockDelta;
            long hp = (long)TemporaryPlayerHpDelta + temporaryHpDelta;
            if (!FitsInt(permanent) || !FitsInt(temporary) || !FitsInt(hp)) return false;
            var commands = new List<ItemEffectCommand>(_enemyCommands);
            if (additionalCommands != null)
                foreach (ItemEffectCommand command in additionalCommands)
                    if (command.Duration == ItemEffectDuration.ThisBattle) commands.Add(command);
            next = new ItemEffectState((int)permanent, (int)temporary, (int)hp, commands,
                HasActiveBattleEffects || addBattleEffects);
            return true;
        }

        internal ItemEffectState AfterBattle()
        {
            return new ItemEffectState(PermanentPlayerBlockBonus, 0, 0,
                Array.Empty<ItemEffectCommand>(), false);
        }

        /// <summary>Clears battle effects and the run's permanent Block bonus at the end of a battle.</summary>
        internal ItemEffectState AfterBattleAndResetPermanentBlock()
        {
            return new ItemEffectState(0, 0, 0, Array.Empty<ItemEffectCommand>(), false);
        }

        private static bool FitsInt(long value) => value >= int.MinValue && value <= int.MaxValue;
    }

    public enum EventItemStatus { Available = 0, Consumed = 1, EndedWithoutUse = 2 }

    /// <summary>One event-provided Item; separate from Hand ownership and finite shop stock.</summary>
    public sealed class EventItemState
    {
        public string DefinitionId { get; }
        public EventItemStatus Status { get; }
        public bool IsAvailable => Status == EventItemStatus.Available;

        public EventItemState(string definitionId, EventItemStatus status = EventItemStatus.Available)
        {
            if (string.IsNullOrWhiteSpace(definitionId)) throw new ArgumentException("DefinitionId is required.", nameof(definitionId));
            if (!Enum.IsDefined(typeof(EventItemStatus), status)) throw new ArgumentOutOfRangeException(nameof(status));
            DefinitionId = definitionId;
            Status = status;
        }

        internal EventItemState WithStatus(EventItemStatus status) => new EventItemState(DefinitionId, status);
    }
}
