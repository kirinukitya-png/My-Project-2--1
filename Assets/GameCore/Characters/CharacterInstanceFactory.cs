using System;

namespace ProjectNST.GameCore.Characters
{
    /// <summary>Deterministic per-session ID allocator for new character instances.</summary>
    public sealed class CharacterInstanceFactory
    {
        private readonly string _prefix;
        private long _nextId;

        public CharacterInstanceFactory(string prefix = "C", long lastIssuedNumber = 0)
        {
            if (string.IsNullOrWhiteSpace(prefix)) throw new ArgumentException("A non-empty ID prefix is required.", nameof(prefix));
            if (lastIssuedNumber < 0) throw new ArgumentOutOfRangeException(nameof(lastIssuedNumber));
            _prefix = prefix;
            _nextId = lastIssuedNumber;
        }

        public CharacterInstance Create(CharacterDefinition definition, int runCost, int star = 1)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            if (runCost < 1) throw new ArgumentOutOfRangeException(nameof(runCost));
            if (star < 1 || star > 3) throw new ArgumentOutOfRangeException(nameof(star));
            if (_nextId == long.MaxValue) throw new InvalidOperationException("Character instance ID sequence is exhausted.");
            _nextId++;
            return new CharacterInstance(_prefix + _nextId, definition, runCost, star);
        }

        public long LastIssuedNumber => _nextId;

        /// <summary>Creates a transaction-local allocator copy. Commit the returned fork only on success.</summary>
        public CharacterInstanceFactory CreateFork()
        {
            return new CharacterInstanceFactory(_prefix, _nextId);
        }
    }
}
