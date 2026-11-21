# Stage 2 deterministic random system

Unity-independent assembly: `ProjectNST.GameCore` (`noEngineReferences: true`). Nothing in the current game calls it in Stage 2.

## Algorithms and versions

- PRNG: PCG-XSH-RR 32, version `PCG-XSH-RR-32-v1`. It has a compact 64-bit state plus an odd sequence increment, is fast and straightforward to checkpoint, and provides a documented 32-bit output permutation. `NextInt` uses rejection sampling to avoid modulo bias; `NextUnitDouble` combines two outputs into 53 bits.
- Trial seed derivation: SplitMix64 finalizer, version `SplitMix64-v1`. For a fixed MasterSeed, the bijective mixing composition maps distinct `ulong` TrialIndex values to distinct `ulong` TrialSeed values.

A run record should retain MasterSeed, TrialIndex, TrialSeed, `SplitMix64-v1`, and `PCG-XSH-RR-32-v1`. Changing either algorithm requires a new version and preserving the old implementation for old run records.

## Use

```csharp
SimulationSeed seed = TrialSeedGenerator.Create(masterSeed, trialIndex);
var streams = new SimulationRandomStreams(seed);
int offerIndex = streams.Shop.NextInt(0, offerCount);
streams.Stats.Shuffle(statsOptions);
SimulationRandomStreamsState checkpoint = streams.CaptureState();
streams.RestoreState(checkpoint);
```

`NextInt(minInclusive, maxExclusive)` rejects empty/reversed ranges. `Shuffle` mutates the supplied `IList<T>` using Fisher-Yates. `NextUnitDouble` is in `[0,1)`.

## Stream registry

Stable numeric IDs: Shop `0x01`, Stats `0x02`, Encounter `0x03`, Battle `0x04`, Event `0x05`. Never renumber or reuse these IDs. Each stream's PCG seed and sequence are derived directly from TrialSeed and its ID, so construction order and consumption in other streams do not affect it.

## Checkpoints

`Pcg32State` stores algorithm version, state and increment. `SimulationRandomStreamsState` stores its version, TrialSeed and all five stream checkpoints. Restoration validates all state data before changing any stream. Checkpoints are immutable in-memory values; persistence format/versioning is a later concern.
