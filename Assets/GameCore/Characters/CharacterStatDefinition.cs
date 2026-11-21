using System;
using System.Collections.Generic;

namespace ProjectNST.GameCore.Characters
{
    /// <summary>Immutable copy of configured, weighted ATK/DEF candidates by cost and star.</summary>
    public sealed class CharacterStatDefinition
    {
        private readonly List<Entry> _options;

        public CharacterStatDefinition(IEnumerable<CharacterCostStats> costs)
        {
            _options = new List<Entry>();
            if (costs == null) return;
            foreach (CharacterCostStats cost in costs)
            {
                if (cost == null) continue;
                _options.Add(new Entry(cost.Cost, cost.CopyByStar()));
            }
        }

        public bool TryGetOptions(int cost, int star, out IReadOnlyList<CharacterStatOption> options)
        {
            if (star >= 1 && star <= 3)
            {
                foreach (Entry entry in _options)
                {
                    if (entry.Cost != cost) continue;
                    options = entry.ByStar[star - 1];
                    return true;
                }
            }
            options = null;
            return false;
        }

        private sealed class Entry
        {
            public readonly int Cost;
            public readonly IReadOnlyList<CharacterStatOption>[] ByStar;
            public Entry(int cost, IReadOnlyList<CharacterStatOption>[] byStar) { Cost = cost; ByStar = byStar; }
        }
    }

    public sealed class CharacterCostStats
    {
        private readonly CharacterStatOption[][] _byStar;
        public int Cost { get; }

        public CharacterCostStats(int cost, IEnumerable<CharacterStatOption> star1,
            IEnumerable<CharacterStatOption> star2, IEnumerable<CharacterStatOption> star3)
        {
            Cost = cost;
            _byStar = new[] { Copy(star1), Copy(star2), Copy(star3) };
        }

        internal IReadOnlyList<CharacterStatOption>[] CopyByStar()
        {
            return new IReadOnlyList<CharacterStatOption>[]
            {
                Array.AsReadOnly((CharacterStatOption[])_byStar[0].Clone()),
                Array.AsReadOnly((CharacterStatOption[])_byStar[1].Clone()),
                Array.AsReadOnly((CharacterStatOption[])_byStar[2].Clone())
            };
        }

        private static CharacterStatOption[] Copy(IEnumerable<CharacterStatOption> source)
        {
            return source == null ? Array.Empty<CharacterStatOption>() : new List<CharacterStatOption>(source).ToArray();
        }
    }

    /// <summary>One weighted ATK/DEF outcome; both values are selected together.</summary>
    public readonly struct CharacterStatOption
    {
        public int Attack { get; }
        public int Defense { get; }
        public int Weight { get; }

        public CharacterStatOption(int attack, int defense, int weight)
        {
            Attack = attack;
            Defense = defense;
            Weight = weight;
        }
    }
}
