# Modding API overview (C# vs `.romod`)

This document stays aligned with **`Romestead.ModLoader.Abstractions`** and the **`.romod`** pipeline (`Romestead.RomodFormat` + `RomodDataMod` + `RomodToDefinitionMapper`). When in doubt, treat the source as truth:

- C# contracts: `src/Romestead.ModLoader.Abstractions/`
- TOML suffixes and parsers: `src/Romestead.RomodFormat/Content/RomodContentKind.cs`, `RomodContentParserRegistry.cs`, `Content/Types/*.cs`
- What a `.romod` actually registers at runtime: `src/Romestead.StartupHook/RomodIntegration/RomodDataMod.cs`

## Two entry points for C# mods

1. **`IRomesteadMod.Initialize(IModContext context)`** — lifecycle, logging, typed registries for common content, UI, client-only surfaces, and `Apis`.
2. **`IContentMod.RegisterContent(IContentRegistry registry)`** — push definitions into **all** content registries the loader drains (including ones not mirrored on `IModContext`).

`.romod` packages implement both interfaces via `RomodDataMod`, but only `RegisterContent` does work: `Initialize` just logs.

## `IModContext` (per-mod singletons)

Available in `Initialize`. Maps to the same backing registries the loader uses everywhere.

| Member | Purpose |
|--------|---------|
| `GameRoot`, `ModRoot`, `ModDirectory` | Paths |
| `Logger` | Structured mod log |
| `Apis` | `IModApiResolver` — `GetApi<T>`, `TryGetApi<T>` |
| `Items`, `Recipes`, `Text`, `Icons`, `Skills`, `SkillEffects`, `PlayerClasses`, `Stats`, `ValueOverrides`, `CraftingStations`, `Placeables`, `AggroTuning`, `Maps` | Content registration |
| `Ui` | Declarative mod settings / sidebar |
| `Overlays`, `Windows`, `Crafting` | Client-only UI hosts (throw on dedicated server) |
| `Lifecycle` | `GameReady` / scene signals (client-oriented) |

`IModContext` now exposes the **full** content-registry surface — the same set
as `IContentRegistry` (below). There is one content path: the `IModContext`
content properties and the `IContentRegistry` handed to `RegisterContent` are
backed by the same per-mod, duplicate-checked registry. Whether you register a
bench in `Initialize` (`context.Placeables`) or in `RegisterContent`
(`registry.Placeables`), it is duplicate-checked, attributed to your mod, and
gets generated text ids identically. `RegisterContent(IContentMod)` remains the
recommended home for bulk content registration; `Initialize` is the right place
for content that depends on values resolved at startup.

## `IContentRegistry` (`RegisterContent`)

The content surface handed to `IContentMod.RegisterContent`. It exposes the
**same** content registries as `IModContext` (everything in the parity matrix
below). There is no second “hidden” registry set — `ModLoadContext` forwards
`IModContext` content properties to the same per-mod `IContentRegistry`
instance used for `RegisterContent`.

## `.romod` parity matrix

A `.romod` is a zip with `romestead.mod.toml` + `content/*.toml` + `assets/`. Content kinds are determined by the filename pattern `*.<kind>.toml` (see `RomodContentKindExtensions`).

| `IContentRegistry` | In `.romod` today? | TOML kind / notes |
|--------------------|-------------------|-------------------|
| `Items` | Yes | `*.item.toml` — includes equipment, weapon, shield, spell tome fields parsed by `ItemTomlParser` |
| `Recipes` | Yes | `*.recipe.toml` |
| `Icons` | Yes | `*.icon.toml` |
| `Stats` | Yes | `*.stat.toml` |
| `Skills` | Yes | `*.skill.toml` |
| `SkillEffects` | Yes | `*.skill-effect.toml` |
| `PlayerClasses` | Yes | `*.player-class.toml` |
| `ValueOverrides` | Yes | `*.value-override.toml` — `[[items]]`, `[[entityHealth]]` |
| `Text` | Yes | `*.text.toml` — `{ id, text }` localization entry |
| `AggroTuning` | Yes | `*.aggro-tuning.toml` — `{ id, type, value, applyToBosses }` (client-consumed) |
| `CraftingStations` | Yes | `*.crafting-station.toml` — `{ id, name, iconId }` |
| `Maps` | Yes | `*.map.toml` — `[[aliases]]` + `[[files]]` (client-consumed) |
| `Placeables` | Yes | `*.placeable.toml` — asset-backed bench; pair with a crafting station |

Adding a new row requires: new `RomodContentKind`, TOML parser, validation rules, `RomodToDefinitionMapper` case, and a line in `RomodDataMod.RegisterContent` — see [romod-packages.md](romod-packages.md) *Schema evolution*.

## Runtime APIs (`context.Apis`)

Same contract on both hosts unless noted. Full tables and examples live in the workspace [README](../README.md) (*Runtime APIs*, capability degradation, multiplayer). Prefer `TryGetApi` for client-only types when authoring cross-host mods.

## Manifest alignment

| C# | `.romod` manifest |
|----|-------------------|
| `[ModManifest("id", "Name", "1.0.0", SyncMode = …)]` | `id`, `name`, `version`, `syncMode` in `romestead.mod.toml` |

`MultiplayerSyncMode` string values match the TOML `syncMode` enum names (`ClientOnly`, `ServerOnly`, …).
