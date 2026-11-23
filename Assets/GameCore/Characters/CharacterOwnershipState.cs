using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace ProjectNST.GameCore.Characters
{
    /// <summary>
    /// Immutable ownership collection. It is the only GameCore state model that stores CharacterInstance objects;
    /// public reads return immutable value views rather than the mutable instances themselves.
    /// </summary>
    public sealed class CharacterOwnershipState
    {
        private readonly Dictionary<string, CharacterInstance> _instances;
        public IReadOnlyDictionary<string, CharacterInstanceView> Instances => CreateReadOnlyViews();
        public int OwnedCount => _instances.Count;

        public CharacterOwnershipState(IEnumerable<CharacterInstance> instances)
        {
            if (instances == null) throw new ArgumentNullException(nameof(instances));
            _instances = new Dictionary<string, CharacterInstance>(StringComparer.Ordinal);
            foreach (CharacterInstance instance in instances)
            {
                if (instance == null) throw new ArgumentException("Owned instances cannot contain null.", nameof(instances));
                if (_instances.ContainsKey(instance.InstanceId))
                    throw new ArgumentException("InstanceId values must be unique in ownership state.", nameof(instances));
                _instances.Add(instance.InstanceId, instance.CreateCopy());
            }
        }

        public bool Contains(string instanceId) =>
            !string.IsNullOrWhiteSpace(instanceId) && _instances.ContainsKey(instanceId);

        public bool TryGet(string instanceId, out CharacterInstanceView instance)
        {
            if (_instances.TryGetValue(instanceId ?? string.Empty, out CharacterInstance owned))
            {
                instance = new CharacterInstanceView(owned);
                return true;
            }
            instance = null;
            return false;
        }

        /// <summary>Internal rules receive a detached working copy; mutations cannot affect this state.</summary>
        internal bool TryCreateOwnedInstanceCopy(string instanceId, out CharacterInstance instance)
        {
            if (_instances.TryGetValue(instanceId ?? string.Empty, out CharacterInstance owned))
            {
                instance = owned.CreateCopy();
                return true;
            }
            instance = null;
            return false;
        }

        public IReadOnlyList<CharacterInstanceView> GetOwnedInstances()
        {
            var copy = new CharacterInstanceView[_instances.Count];
            int index = 0;
            foreach (CharacterInstance instance in _instances.Values) copy[index++] = new CharacterInstanceView(instance);
            return Array.AsReadOnly(copy);
        }

        /// <summary>Returns a copied ownership collection with the instance added; fails without changing this state on duplicate IDs.</summary>
        public bool TryWithAdded(CharacterInstance instance, out CharacterOwnershipState nextState)
        {
            nextState = this;
            if (instance == null || _instances.ContainsKey(instance.InstanceId)) return false;
            var copy = new List<CharacterInstance>(_instances.Values) { instance };
            nextState = new CharacterOwnershipState(copy);
            return true;
        }

        /// <summary>Returns a copied state with an existing instance replaced by a detached copy.</summary>
        public bool TryWithReplaced(CharacterInstance instance, out CharacterOwnershipState nextState)
        {
            nextState = this;
            if (instance == null || !_instances.TryGetValue(instance.InstanceId, out CharacterInstance current) ||
                !string.Equals(instance.DefinitionId, current.DefinitionId, StringComparison.Ordinal) ||
                instance.RunCost != current.RunCost) return false;
            var copy = new List<CharacterInstance>(_instances.Values.Count);
            foreach (KeyValuePair<string, CharacterInstance> pair in _instances)
                copy.Add(string.Equals(pair.Key, instance.InstanceId, StringComparison.Ordinal)
                    ? instance : pair.Value);
            nextState = new CharacterOwnershipState(copy);
            return true;
        }

        /// <summary>Returns a copied ownership collection with the ID removed.</summary>
        public bool TryWithRemoved(string instanceId, out CharacterOwnershipState nextState)
        {
            nextState = this;
            if (string.IsNullOrWhiteSpace(instanceId) || !_instances.ContainsKey(instanceId)) return false;
            var copy = new List<CharacterInstance>(_instances.Values);
            copy.RemoveAll(instance => string.Equals(instance.InstanceId, instanceId, StringComparison.Ordinal));
            nextState = new CharacterOwnershipState(copy);
            return true;
        }

        private IReadOnlyDictionary<string, CharacterInstanceView> CreateReadOnlyViews()
        {
            var copy = new Dictionary<string, CharacterInstanceView>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, CharacterInstance> pair in _instances)
                copy.Add(pair.Key, new CharacterInstanceView(pair.Value));
            return new ReadOnlyDictionary<string, CharacterInstanceView>(copy);
        }
    }
}
