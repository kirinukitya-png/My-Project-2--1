using System;
using System.Collections.Generic;
using System.Linq;
using ProjectNST.GameCore.Observation;

namespace ProjectNST.GameCore.Battle
{
    public enum EnemyRoundDefenseStatus
    {
        Evaluated, NoEnemy, NoEligibleEnemyAttack, AttackEvaluationUnavailable, TargetUnavailable,
        UnsupportedTarget, InvalidTargetDefinition, PublicStatsUnavailable, StateLimitExceeded, InvalidInput
    }

    public readonly struct AllyDefenseMember
    {
        public int Slot { get; }
        public decimal Defense { get; }
        public AllyAttackStatsKind StatsKind { get; }

        public AllyDefenseMember(int slot, decimal defense, AllyAttackStatsKind statsKind = AllyAttackStatsKind.Actual)
        {
            Slot = slot;
            Defense = defense;
            StatsKind = statsKind;
        }

        public static AllyDefenseMember FromObservation(CharacterObservation character)
        {
            if (character == null) throw new ArgumentNullException(nameof(character));
            if (character.LocationKind != ObservationLocationKind.Board)
                throw new ArgumentException("The character must occupy a Board slot.", nameof(character));
            return new AllyDefenseMember(character.SlotIndex, (decimal)character.Defense + character.TemporaryDefenseBonus);
        }
    }

    public readonly struct EnemyDefenseAttacker
    {
        public AllyRoundEnemyInput InitialEnemy { get; }
        public int Attack { get; }
        public int Gap { get; }
        public EnemyTargetObservation Target { get; }

        public EnemyDefenseAttacker(AllyRoundEnemyInput initialEnemy, int attack, int gap, EnemyTargetObservation target)
        {
            InitialEnemy = initialEnemy;
            Attack = attack;
            Gap = gap;
            Target = target;
        }

        public static EnemyDefenseAttacker FromObservation(EnemyObservation enemy) =>
            new EnemyDefenseAttacker(AllyRoundEnemyInput.FromObservation(enemy),
                enemy.Attack, enemy.Gap, enemy.GetInitialTarget());
    }

    public readonly struct EnemyDefenseTargetDetail
    {
        public EnemyTargetOutcome Target { get; }
        public decimal TotalDefense { get; }
        public decimal Damage { get; }

        internal EnemyDefenseTargetDetail(EnemyTargetOutcome target, decimal defense, decimal damage)
        {
            Target = target;
            TotalDefense = defense;
            Damage = damage;
        }
    }

    public sealed class EnemyDefenseAttackSummary
    {
        public int Slot { get; }
        public int InitialHP { get; }
        public int Gap { get; }
        public decimal SurvivalProbability { get; }
        public decimal AttackProbability { get; }
        public string TargetId { get; }
        public int TargetSetCount { get; }
        public int UniqueDamageCount { get; }
        public decimal ExpectedDamageBeforeBlock { get; }
        public decimal ExpectedAbsorbed { get; }
        public decimal ExpectedIncoming { get; }
        public IReadOnlyList<EnemyDefenseTargetDetail> Targets { get; }
        public IReadOnlyList<(decimal Damage, decimal Probability)> DamageDistribution { get; }

        internal EnemyDefenseAttackSummary(EnemyDefenseAttacker enemy, decimal survival, decimal attacks,
            int targetSets, int damageCount, decimal gross, decimal absorbed, decimal incoming,
            List<EnemyDefenseTargetDetail> targets, IReadOnlyList<(decimal, decimal)> damageDistribution)
        {
            Slot = enemy.InitialEnemy.Slot;
            InitialHP = enemy.InitialEnemy.HP;
            Gap = enemy.Gap;
            SurvivalProbability = survival;
            AttackProbability = attacks;
            TargetId = enemy.Target?.TargetId;
            TargetSetCount = targetSets;
            UniqueDamageCount = damageCount;
            ExpectedDamageBeforeBlock = gross;
            ExpectedAbsorbed = absorbed;
            ExpectedIncoming = incoming;
            Targets = targets == null ? Array.AsReadOnly(Array.Empty<EnemyDefenseTargetDetail>()) : targets.AsReadOnly();
            DamageDistribution = damageDistribution ?? Array.AsReadOnly(Array.Empty<(decimal, decimal)>());
        }
    }

    public readonly struct EnemyDefenseCounters
    {
        public int AttackOutcomes { get; }
        public int SurvivalMasks { get; }
        public int EligibleMasks { get; }
        public int TargetEvaluations { get; }
        public int GeneratedBlockBranches { get; }
        public int MergedBlockBranches { get; }
        public int PeakBlockStates { get; }

        internal EnemyDefenseCounters(int outcomes, int survival, int eligible, int targets,
            int branches, int merges, int peak)
        {
            AttackOutcomes = outcomes;
            SurvivalMasks = survival;
            EligibleMasks = eligible;
            TargetEvaluations = targets;
            GeneratedBlockBranches = branches;
            MergedBlockBranches = merges;
            PeakBlockStates = peak;
        }
    }

    public sealed class EnemyRoundDefenseResult
    {
        public EnemyRoundDefenseStatus Status { get; }
        public AllyRoundAttackStatus? AttackStatus { get; }
        public int? UnavailableEnemySlot { get; }
        public bool UsesPublicMeanStats { get; }
        public AllyRoundEnemyEffects IgnoredEffects { get; }
        public decimal? InitialThreat { get; }
        public decimal? SurvivingThreat { get; }
        public decimal? AvoidedThreat => InitialThreat - SurvivingThreat;
        public decimal? ExpectedAttacks { get; }
        public decimal? ExpectedAttackPower { get; }
        public decimal? ExpectedDamageBeforeBlock { get; }
        public decimal? ExpectedAbsorbed { get; }
        /// <summary>Potential damage, with all eligible attacks processed regardless of Player HP.</summary>
        public decimal? Incoming { get; }
        public decimal? ExpectedRemainingBlock { get; }
        public decimal? RemainingPlayerHPEstimate { get; }
        public IReadOnlyList<(decimal Block, decimal Probability)> RemainingBlockDistribution { get; }
        public IReadOnlyList<EnemyDefenseAttackSummary> Enemies { get; }
        public EnemyDefenseCounters Counters { get; }

        internal EnemyRoundDefenseResult(EnemyRoundDefenseStatus status, AllyRoundAttackStatus? attackStatus = null,
            int? unavailableSlot = null, bool mean = false, AllyRoundEnemyEffects effects = AllyRoundEnemyEffects.None,
            decimal? initialThreat = null, decimal? survivingThreat = null, decimal? attacks = null,
            decimal? power = null, decimal? gross = null, decimal? absorbed = null, decimal? incoming = null,
            decimal? remaining = null, int? playerHP = null, EnemyDefenseCounters counters = default,
            List<(decimal, decimal)> blocks = null, List<EnemyDefenseAttackSummary> enemies = null)
        {
            Status = status;
            AttackStatus = attackStatus;
            UnavailableEnemySlot = unavailableSlot;
            UsesPublicMeanStats = mean;
            IgnoredEffects = effects;
            InitialThreat = initialThreat;
            SurvivingThreat = survivingThreat;
            ExpectedAttacks = attacks;
            ExpectedAttackPower = power;
            ExpectedDamageBeforeBlock = gross;
            ExpectedAbsorbed = absorbed;
            Incoming = incoming;
            ExpectedRemainingBlock = remaining;
            RemainingPlayerHPEstimate = incoming.HasValue && playerHP.HasValue
                ? Math.Max(0m, playerHP.Value - incoming.Value) : (decimal?)null;
            RemainingBlockDistribution = blocks == null ? Array.AsReadOnly(Array.Empty<(decimal, decimal)>()) : blocks.AsReadOnly();
            Enemies = enemies == null ? Array.AsReadOnly(Array.Empty<EnemyDefenseAttackSummary>()) : enemies.AsReadOnly();
            Counters = counters;
        }
    }

    /// <summary>Evaluates potential first-round damage using the completed ally-round joint distribution.</summary>
    public static class EnemyRoundDefenseEvaluator
    {
        public const int DefaultMaxBlockStates = 256;
        public const int MaximumBlockStateLimit = 4096;
        private const decimal MassTolerance = 0.000000000000000000000001m;

        public static EnemyRoundDefenseResult Evaluate(IReadOnlyList<AllyDefenseMember> board,
            IReadOnlyList<EnemyDefenseAttacker> enemies, AllyRoundAttackResult attack, decimal initialBlock,
            int? playerHP = null, int maxBlockStates = DefaultMaxBlockStates, bool includeDetails = false)
        {
            if (board == null || enemies == null || attack == null || board.Count > 9 || enemies.Count > 6 ||
                initialBlock < 0m || initialBlock > int.MaxValue || playerHP < 0 ||
                maxBlockStates < 1 || maxBlockStates > MaximumBlockStateLimit)
                return new EnemyRoundDefenseResult(EnemyRoundDefenseStatus.InvalidInput);
            if (attack.Status != AllyRoundAttackStatus.Evaluated && attack.Status != AllyRoundAttackStatus.NoEnemy)
                return new EnemyRoundDefenseResult(EnemyRoundDefenseStatus.AttackEvaluationUnavailable, attack.Status);

            var bySlot = new EnemyDefenseAttacker[6];
            int present = 0, live = 0, eligible = 0;
            decimal threat = 0m;
            var effects = attack.IgnoredEffects;
            foreach (var enemy in enemies)
            {
                int slot = enemy.InitialEnemy.Slot;
                if (slot < 0 || slot >= 6 || enemy.InitialEnemy.HP < 0 || enemy.Gap < 0 ||
                    (present & (1 << slot)) != 0)
                    return new EnemyRoundDefenseResult(EnemyRoundDefenseStatus.InvalidInput, attack.Status);
                int bit = 1 << slot;
                present |= bit;
                bySlot[slot] = enemy;
                if (enemy.InitialEnemy.HP == 0) continue;
                effects |= enemy.InitialEnemy.Effects;
                live |= bit;
                if (enemy.Gap != 0) continue;
                eligible |= bit;
                threat += Math.Max(0, enemy.Attack);
            }
            if ((live == 0) != (attack.Status == AllyRoundAttackStatus.NoEnemy) ||
                !MatchesInitialEnemies(attack, bySlot, live))
                return new EnemyRoundDefenseResult(EnemyRoundDefenseStatus.InvalidInput, attack.Status);

            var survivalMasks = new SortedDictionary<int, decimal>();
            if (live == 0) survivalMasks.Add(0, 1m);
            else
            {
                foreach (var outcome in attack.Outcomes)
                {
                    if (outcome == null || outcome.Probability < 0m || outcome.Probability > 1m ||
                        (outcome.SurvivingSlotMask & ~live) != 0)
                        return new EnemyRoundDefenseResult(EnemyRoundDefenseStatus.InvalidInput, attack.Status);
                    if (outcome.Probability > 0m)
                        Accumulate(survivalMasks, outcome.SurvivingSlotMask, outcome.Probability);
                }
                if (!Normalize(survivalMasks))
                    return new EnemyRoundDefenseResult(EnemyRoundDefenseStatus.InvalidInput, attack.Status);
            }
            var attackMasks = new SortedDictionary<int, decimal>();
            int used = 0;
            foreach (var state in survivalMasks)
            {
                int mask = state.Key & eligible;
                used |= mask;
                Accumulate(attackMasks, mask, state.Value);
            }
            if (!Normalize(attackMasks))
                return new EnemyRoundDefenseResult(EnemyRoundDefenseStatus.InvalidInput, attack.Status);

            var defense = new decimal[9];
            var occupied = new List<int>();
            int occupiedMask = 0;
            bool missing = false, mean = attack.UsesPublicMeanStats;
            foreach (var ally in board)
            {
                if (ally.Slot < 0 || ally.Slot >= 9 || (occupiedMask & (1 << ally.Slot)) != 0 ||
                    !Enum.IsDefined(typeof(AllyAttackStatsKind), ally.StatsKind) ||
                    ally.Defense < int.MinValue || ally.Defense > int.MaxValue ||
                    (ally.StatsKind == AllyAttackStatsKind.Actual && decimal.Truncate(ally.Defense) != ally.Defense))
                    return new EnemyRoundDefenseResult(EnemyRoundDefenseStatus.InvalidInput, attack.Status);
                occupiedMask |= 1 << ally.Slot;
                occupied.Add(ally.Slot);
                defense[ally.Slot] = ally.Defense;
                missing |= ally.StatsKind == AllyAttackStatsKind.Unavailable;
                mean |= ally.StatsKind == AllyAttackStatsKind.PublicMean;
            }
            if (used != 0 && missing)
                return new EnemyRoundDefenseResult(EnemyRoundDefenseStatus.PublicStatsUnavailable, attack.Status);

            var damage = new SortedDictionary<decimal, decimal>[6];
            var targetDetails = new List<EnemyDefenseTargetDetail>[6];
            var targetCounts = new int[6];
            int targets = 0, branches = 0, merges = 0, peak = 1;
            EnemyDefenseCounters Counters() => new EnemyDefenseCounters(attack.Outcomes.Count, survivalMasks.Count,
                attackMasks.Count, targets, branches, merges, peak);
            EnemyRoundDefenseResult Fail(EnemyRoundDefenseStatus status, int? slot = null) =>
                new EnemyRoundDefenseResult(status, attack.Status, slot, mean, effects, counters: Counters());

            for (int slot = 0; slot < 6; slot++)
            {
                if ((used & (1 << slot)) == 0) continue;
                var enemy = bySlot[slot];
                var selected = EnemyTargetProbabilityEvaluator.Evaluate(enemy.Target, slot, occupied);
                targets++;
                if (selected.Status != EnemyTargetProbabilityStatus.Evaluated)
                    return Fail(TargetFailure(selected.Status), slot);

                targetCounts[slot] = selected.Outcomes.Count;
                damage[slot] = new SortedDictionary<decimal, decimal>();
                if (includeDetails) targetDetails[slot] = new List<EnemyDefenseTargetDetail>();
                foreach (var outcome in selected.Outcomes)
                {
                    decimal totalDefense = outcome.Slots.Sum(s => defense[s]);
                    decimal hit = Math.Max(0m, enemy.Attack - totalDefense);
                    if (hit > int.MaxValue) return Fail(EnemyRoundDefenseStatus.InvalidInput, slot);
                    Accumulate(damage[slot], hit, outcome.Probability);
                    if (includeDetails) targetDetails[slot].Add(new EnemyDefenseTargetDetail(outcome, totalDefense, hit));
                }
                if (!Normalize(damage[slot])) return Fail(EnemyRoundDefenseStatus.InvalidInput, slot);
            }

            var finalBlocks = new SortedDictionary<decimal, decimal>();
            var grossByEnemy = new decimal[6];
            var absorbedByEnemy = new decimal[6];
            var incomingByEnemy = new decimal[6];
            decimal attacks = 0m, power = 0m, survivingThreat = 0m;
            foreach (var state in attackMasks)
            {
                var current = new SortedDictionary<decimal, decimal> { [initialBlock] = 1m };
                for (int slot = 0; slot < 6; slot++)
                {
                    if ((state.Key & (1 << slot)) == 0) continue;
                    attacks += state.Value;
                    power += state.Value * bySlot[slot].Attack;
                    survivingThreat += state.Value * Math.Max(0, bySlot[slot].Attack);
                    var next = new SortedDictionary<decimal, decimal>();
                    foreach (var block in current)
                    foreach (var hit in damage[slot])
                    {
                        branches++;
                        decimal probability = block.Value * hit.Value;
                        decimal absorbed = Math.Min(block.Key, hit.Key);
                        decimal remaining = block.Key - absorbed;
                        if (next.ContainsKey(remaining)) merges++;
                        else if (next.Count == maxBlockStates) return Fail(EnemyRoundDefenseStatus.StateLimitExceeded, slot);
                        Accumulate(next, remaining, probability);
                        decimal weight = state.Value * probability;
                        grossByEnemy[slot] += weight * hit.Key;
                        absorbedByEnemy[slot] += weight * absorbed;
                        incomingByEnemy[slot] += weight * (hit.Key - absorbed);
                    }
                    if (!Normalize(next)) return Fail(EnemyRoundDefenseStatus.InvalidInput, slot);
                    current = next;
                    peak = Math.Max(peak, current.Count);
                }
                foreach (var block in current)
                {
                    if (!finalBlocks.ContainsKey(block.Key) && finalBlocks.Count == maxBlockStates)
                        return Fail(EnemyRoundDefenseStatus.StateLimitExceeded);
                    Accumulate(finalBlocks, block.Key, state.Value * block.Value);
                }
                peak = Math.Max(peak, finalBlocks.Count);
            }
            if (!Normalize(finalBlocks)) return Fail(EnemyRoundDefenseStatus.InvalidInput);
            decimal remainingBlock = finalBlocks.Sum(entry => entry.Key * entry.Value);
            var summaries = new List<EnemyDefenseAttackSummary>();
            for (int slot = 0; slot < 6; slot++)
            {
                int bit = 1 << slot;
                if ((present & bit) == 0) continue;
                decimal survival = survivalMasks.Where(x => (x.Key & bit) != 0).Sum(x => x.Value);
                var distribution = includeDetails && damage[slot] != null
                    ? damage[slot].Select(hit => (hit.Key, hit.Value)).ToList().AsReadOnly() : null;
                summaries.Add(new EnemyDefenseAttackSummary(bySlot[slot], survival, (eligible & bit) != 0 ? survival : 0m,
                    targetCounts[slot], damage[slot]?.Count ?? 0, grossByEnemy[slot], absorbedByEnemy[slot],
                    incomingByEnemy[slot], targetDetails[slot], distribution));
            }
            var status = live == 0 ? EnemyRoundDefenseStatus.NoEnemy :
                used == 0 ? EnemyRoundDefenseStatus.NoEligibleEnemyAttack : EnemyRoundDefenseStatus.Evaluated;
            return new EnemyRoundDefenseResult(status, attack.Status, mean: mean, effects: effects,
                initialThreat: threat, survivingThreat: survivingThreat, attacks: attacks, power: power,
                gross: grossByEnemy.Sum(), absorbed: absorbedByEnemy.Sum(), incoming: incomingByEnemy.Sum(),
                remaining: remainingBlock, playerHP: playerHP, counters: Counters(),
                blocks: finalBlocks.Select(entry => (entry.Key, entry.Value)).ToList(), enemies: summaries);
        }

        private static bool MatchesInitialEnemies(AllyRoundAttackResult attack, EnemyDefenseAttacker[] enemies, int live)
        {
            int mask = 0;
            foreach (var state in attack.InitialEnemies)
            {
                int bit = 1 << state.Slot;
                if ((live & bit) == 0) return false;
                var input = enemies[state.Slot].InitialEnemy;
                if (state.HP != input.HP || state.InitialDefense != input.Defense) return false;
                mask |= bit;
            }
            return mask == live;
        }

        private static EnemyRoundDefenseStatus TargetFailure(EnemyTargetProbabilityStatus status)
        {
            switch (status)
            {
                case EnemyTargetProbabilityStatus.Unavailable: return EnemyRoundDefenseStatus.TargetUnavailable;
                case EnemyTargetProbabilityStatus.UnsupportedTarget: return EnemyRoundDefenseStatus.UnsupportedTarget;
                case EnemyTargetProbabilityStatus.InvalidDefinition: return EnemyRoundDefenseStatus.InvalidTargetDefinition;
                default: return EnemyRoundDefenseStatus.InvalidInput;
            }
        }

        private static void Accumulate<TKey>(SortedDictionary<TKey, decimal> distribution, TKey key, decimal probability)
        {
            distribution.TryGetValue(key, out decimal existing);
            distribution[key] = existing + probability;
        }

        private static bool Normalize<TKey>(SortedDictionary<TKey, decimal> distribution)
        {
            if (distribution.Count == 0) return false;
            decimal correction = 1m - distribution.Values.Sum();
            if (Math.Abs(correction) > MassTolerance) return false;
            if (correction != 0m)
            {
                var largest = distribution.Aggregate((left, right) => left.Value >= right.Value ? left : right);
                distribution[largest.Key] += correction;
            }
            return true;
        }
    }
}
