# Stage 3 character/status migration

## Current Unity behavior observed

- `CharacterData` contains `characterName`, `icon` (`Sprite`), `cost`, `attributes`, and `units`. GameCore copies name, cost, attributes and units. `icon` remains Unity presentation data.
- Shop initialization shuffles the `CharacterData` references and writes the assigned cost back to each selected asset. The composition roster also resets costs to 1. Stage 3 deliberately does neither: an adapter copies the current value as a definition's `BaseCost`, and each instance receives a separate `RunCost`.
- `OwnedCharacter` stores the ScriptableObject reference, star (1–3), attack, defense, battle attack/defense bonuses, and `hasRolledStats`. Temporary star changes cache star/attack/defense/hasRolledStats once and restore those values later. Battle bonuses are not included in that backup.
- On construction, `OwnedCharacter` rolls stats at star 1. `TrySetStar` rerolls the stats for every successful call, including a call with the same star. Temporary star changes use that same reroll. `GetAttack` and `GetDefense` call `EnsureStats`, so either getter can lazily roll the pair when not initialized.
- `CharacterStatSettings.TryGetRandomStats` finds the first matching cost row and selects from the requested star's options. Null entries and zero weights are skipped; a negative weight, no positive total, or total over `Int32.MaxValue` fails. It makes one `Random.Range(0, totalWeight)` integer draw, then walks the options in array order. The selected option supplies Attack and Defense together; they are not independently rolled. No scaling formula is applied.
- The active saved asset is `Assets/Data2/CharacterStatSettings.asset`, referenced by the saved shop and composition scenes. Its configured options are equal-weight (weight 1):

| Cost | ★1 (ATK/DEF) | ★2 (ATK/DEF) | ★3 (ATK/DEF) |
|---|---|---|---|
| 1 | 1/2, 2/1 | 2/2, 3/1, 1/3 | 5/5 |
| 2 | 2/2, 3/0, 1/3 | 3/2, 2/3 | 7/7 |
| 3 | 3/1, 2/3 | 3/3, 4/2, 2/4 | 9/9 |
| 4 | 3/3, 4/1, 2/4 | 4/3, 3/4 | 11/11 |
| 5 | 4/2, 3/4 | 4/4, 5/3, 3/5 | 13/13 |

`CharacterStatSettings.cs` also contains fallback initializer values that differ from this saved asset. Runtime behavior uses the asset assigned in the scene; the adapter maps the actual object passed to it and does not assume the source initializer is authoritative.

## GameCore API

- `CharacterDefinition`: copied, read-only metadata. Definition IDs are supplied explicitly; the editor helper uses the asset GUID, prefixed with `character-data:`. Character names are not IDs.
- `CharacterInstance`: owns instance ID, definition ID, run cost, star, base attack/defense, battle bonuses, and the rolled/unrolled state. `CharacterInstanceFactory` allocates deterministic per-session IDs. The caller must keep one factory per session and persist its last-issued number if restoring a session.
- `CharacterStatDefinition`: immutable copied cost/star candidate table.
- `CharacterStatGenerator.TryGenerate`: uses `IRandomSource`, performs one unbiased integer draw for a valid table, and chooses the weighted ATK/DEF pair together. Use `SimulationRandomStreams.Stats`.
- Initialization and star changes are explicit method calls. Reading `Attack`/`Defense` never consumes RNG; this intentionally makes the side effect visible to a future GameSession. No existing game code calls this API.

Example:

```csharp
var definition = CharacterDataAssetIdMapper.ToDefinition(characterData); // Editor only
var statDefinition = CharacterStatSettingsMapper.ToDefinition(sceneSettings);
var streams = new SimulationRandomStreams(trialSeed);
var factory = new CharacterInstanceFactory();
var instance = factory.Create(definition, runCost: 2);
bool initialized = instance.EnsureStats(definition, statDefinition, streams.Stats);
```

For runtime code where `UnityEditor.AssetDatabase` is unavailable, obtain a stable asset identifier from the content/build pipeline and pass it to `CharacterDataMapper.ToDefinition`. Do not use `GetInstanceID()` or character name as a persistent ID.

## Compatibility notes

- Stage 3 matches the existing weighted selection rule, star options, reroll-on-star-change rule, and temporary-star backup behavior. PCG output will not match UnityEngine.Random output.
- The new `RunCost` captures cost at instance creation. Existing `OwnedCharacter` looks up `data.cost` again whenever stats reroll; because the shared asset can later be modified by game initialization, this can differ from a retained legacy instance. This is intentionally represented as explicit per-instance cost and is not connected to gameplay in this stage. Reconcile the migration boundary before replacing callers.
- Getter-triggered lazy generation is represented by explicit `EnsureStats`; integrating it requires a separately reviewed call-site change and is out of scope.
- No changes are made to existing ScriptableObjects, Managers, scenes, prefabs, snapshot tooling, or existing runtime RNG usage.
