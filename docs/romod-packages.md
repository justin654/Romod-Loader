# `.romod` packages

`.romod` is a zip-based, data-only mod format for Romestead. Authors who only
want to add declarative content (items, recipes, icons, stats, skills, skill
effects, player classes, value overrides, text, aggro tuning, crafting
stations, placeables, maps) can ship a `.romod` archive instead of writing and
building a C# mod.

`.romod` packages plug into the same loader that already runs C# mods. They
go through the same content registries (`IItemRegistry`,
`IRecipeRegistry`, …) and the same shared/client drains. There is **no**
second runtime path: a `.romod` is just another mod source. C# mods and
`.romod` packages can coexist freely; cross-source duplicate ids are
detected before registration.

> **Registry availability vs. host drain.** All content registries
> (`IItemRegistry`, `IRecipeRegistry`, `IIconRegistry`, …) are present on
> both the client and the dedicated server. But not every content kind is
> drained on every host — items, recipes, stats, value overrides, crafting
> stations, placeables, and related shared content land on both hosts via
> `SharedContentBootstrap` (and associated hooks); icons, skills, skill effects,
> player classes, aggro tuning, and map file data are drained only by
> `ClientCore` on the client today. See the *What gets injected on which host*
> table in the workspace README for the current matrix.

## Quick start

```powershell
# scaffold
dotnet ./artifacts/romod-tool/romestead-mod.dll init my.pack ./romods/my.pack

# edit ./romods/my.pack/romestead.mod.toml and ./romods/my.pack/content/*.toml
# drop assets into ./romods/my.pack/assets/

# validate without writing an output
dotnet ./artifacts/romod-tool/romestead-mod.dll validate ./romods/my.pack

# pack — refuses to write the output if validation fails
dotnet ./artifacts/romod-tool/romestead-mod.dll pack ./romods/my.pack -o ./artifacts/mods/my.pack.romod
```

`build.ps1` packs every directory under `./romods/` automatically, so
dropping a new source folder there is enough to ship a sample.

## Folder layout

A package source folder looks like:

```text
EmberPack/
  romestead.mod.toml
  content/
    ember_sword.item.toml
    ember_sword.recipe.toml
    ember_sword.icon.toml
  assets/
    icons/
      ember_sword.png
```

The runtime archive (`EmberPack.romod`) is just a zip of that folder with
those exact paths preserved.

File names are arbitrary. The **content kind** is decided by the suffix
before `.toml`:

| Pattern              | Kind          |
|----------------------|---------------|
| `*.item.toml`        | Item          |
| `*.recipe.toml`      | Recipe        |
| `*.icon.toml`        | Icon          |
| `*.stat.toml`        | Stat          |
| `*.skill.toml`       | Skill         |
| `*.skill-effect.toml`| Skill effect  |
| `*.player-class.toml`| Player class  |
| `*.value-override.toml` | Value overrides (items + entity health templates) |
| `*.text.toml`        | Text (localization entry) |
| `*.aggro-tuning.toml`| Aggro tuning (enemy leash / threat) |
| `*.crafting-station.toml` | Crafting station (bench identity for recipes) |
| `*.placeable.toml`   | Placeable crafting bench (world object) |
| `*.map.toml`         | Map aliases + client-side file redirects |

You can have any number of files of any kind. There is no `items.toml`
catch-all. Authors are encouraged to use one file per content entry —
diffs stay readable that way.

## Manifest (`romestead.mod.toml`)

```toml
id = "justin.emberpack"
name = "Ember Pack"
version = "0.1.0"
schemaVersion = 1
syncMode = "RequiredOnClient"
author = "Justin"
description = "Adds ember-themed items."
homepage = "https://example.com"

[dependencies]
"other.mod.id" = ">=1.0.0"
```

| Field           | Required | Default              | Notes                                                                 |
|-----------------|----------|----------------------|-----------------------------------------------------------------------|
| `id`            | yes      | —                    | Stable mod id. Use reverse-DNS to avoid collisions.                   |
| `name`          | yes      | —                    | Display name.                                                         |
| `version`       | yes      | —                    | Dotted, e.g. `1.2.3`.                                                 |
| `schemaVersion` | no       | `1`                  | Bumped when the file format changes. Migrators run automatically.     |
| `syncMode`      | no       | `RequiredOnClient`   | `ClientOnly`, `ServerOnly`, `RequiredOnClient`, `Incompatible`.       |
| `author`        | no       | —                    | Display only.                                                         |
| `description`   | no       | —                    | Display only.                                                         |
| `homepage`      | no       | —                    | Display only.                                                         |
| `[dependencies]`| no       | empty                | Per-id table; value is a version spec, e.g. `">=1.0.0"` or `"*"`.     |

Invalid `syncMode` values fail with a clear error listing the allowed
values. Unknown top-level fields warn but do not fail.

## Sync mode rules

* `ClientOnly` packages are skipped on the dedicated server.
* `ServerOnly` packages are skipped on the client.
* `RequiredOnClient` (default) loads on both hosts.
* `Incompatible` is treated the same as the existing C# loader treats it
  (the mod participates in compatibility reporting but does not run).

`.romod` packages are pure data, but **not every kind drains on every
host**. Shared bootstrap covers items, recipes, stats, value overrides,
crafting stations, placeables, and related shared plumbing; icons, skills,
skill effects, player classes, aggro tuning, and map file data follow the
same **client vs server** split as C# mods (see *Registry availability vs.
host drain* at the top of this doc and the full *What gets injected on which
host* table in the workspace [README](../README.md)). Use `syncMode` so a
package that only makes sense on the client is not expected on a headless
dedicated server.

## Dependencies

```toml
[dependencies]
"other.mod.id" = ">=1.0.0"
"another.mod"  = "*"
```

* A required dependency id must be present among the discovered C# mods
  or `.romod` packages.
* Version specs support exactly three forms:
  * `*` — any version
  * `>=X.Y.Z` — minimum version
  * bare `X.Y.Z` — interpreted as `>=X.Y.Z`
* Mods load in dependency order. Ties break alphabetically by id.
* Missing dependencies, version-too-low, and dependency cycles are
  errors. **The affected mod is skipped, and the skip cascades transitively
  to anything that depends on it.** Mods that do not depend on the failed
  chain still load normally.
* `romestead.modloader.core` is a built-in pseudo-dependency. Listing it
  is always satisfied.

This is intentionally not a full package manager — no caret ranges, no
alternates, no constraint solving.

## Duplicate ids

Content ids must be unique **per content kind, across all mods** (`.romod`
and C# alike). The loader's duplicate checker runs before each
`IContentRegistry` registration, so a collision surfaces as a clear
error instead of one mod silently overwriting the other:

```text
Duplicate item id 'weapon:mod:ember_sword'.
Defined by:
- justin.emberpack content/ember_sword.item.toml
- other.firepack    Romestead.OtherMod.dll
```

* Duplicates are scoped per **content kind** among entries that expose a
  stable id for validation (`item`, `recipe` result, `icon`, `skill`,
  `stat`, `player-class`, `text`, `aggro-tuning`, `crafting-station`,
  `placeable`, composite `skill-effect`, …). An item id and a stat id that
  happen to share text do not collide.
* `*.map.toml` files are **not** keyed in that duplicate-id pass today;
  overlapping map registrations are not caught as “duplicate map” at
  validate time the way items are.
* `ValueOverride` files (`*.value-override.toml`) do not contribute a
  single top-level id to the package duplicate-id pass; overlapping
  `[[items]]` targets across multiple files are not flagged as duplicates
  at validate time — avoid defining the same item twice in one package.
* Within a single `.romod` package, duplicate ids inside one kind are
  also rejected — the validator catches them before the package is
  even handed to the registry.
* **Last-mod-wins is never silent.** A second registration is dropped
  and the conflict is reported through the same diagnostics channel
  used for failed mods.
* **Icons** allow intentional replacement: an `IconDefinition` with
  `replaceExisting = true` overrides an earlier icon entry, mirroring
  the existing icon database semantics. Every other content kind
  treats a second registration as a hard error.

## Unknown fields

| Situation                                | Behavior                              |
|------------------------------------------|---------------------------------------|
| Unknown content kind (e.g. `*.aura.toml`)| Warning, file skipped                 |
| Unknown field in a known file            | Warning, field ignored                |
| Missing required field                   | **Error**                             |
| Invalid enum / numeric value             | **Error** (lists allowed values)      |
| Duplicate id (per kind)                  | **Error**                             |
| Missing dependency                       | **Error**, mod skipped                |
| Dependency version too low               | **Error**, mod skipped                |
| Dependency cycle                         | **Error**, affected mods skipped      |
| Asset path referenced but not present    | **Error**, mod fails to load          |
| Loose file outside `content/` / `assets/`| Warning, file ignored                 |

Errors look like:

```text
[justin.emberpack] content/ember_sword.icon.toml: Icon 'icon:emberpack:ember_sword' references texture 'assets/icons/ember_sword.png' but no such file exists in the package.
```

Warnings look like:

```text
[justin.emberpack] Unknown field 'rarity' in content/ember_sword.item.toml. Ignoring.
```

## Examples

### Item

```toml
id = "weapon:mod:emberpack:ember_sword"
name = "Ember Sword"
description = "A blade smelted in volcanic resin. Burns what it cuts."
icon = "icon:emberpack:ember_sword"
maxStackSize = 1
tier = 3

[equipment]
slot = "Weapon"
material = "Iron"
displayId = "cdd:iron_sword"

# Or replace displayId with a custom held sprite:
#
# [equipment.display]
# id = "cdd:mod:emberpack:ember_sword"
#
# [[equipment.display.fragments]]
# skinName = "EmberPackEmberSword"
# texture = "assets/equipment/ember_sword_held.png"
# spriteWidth = 48
# spriteHeight = 48
# skinTag = 8
# spacTag = 8
# layer = 1.0

[equipment.heldVfx]
particleEmitterId = "flame_small"
particleOffsetZ = 14
particleLineLength = 26
particleLineWidth = 1.5
particleLineHeight = 4
particleSpawnFrequency = 0.025
particleAmountSpawned = 1
lightOffsetX = 10
lightOffsetZ = 18
lightRadius = 58
lightIntensity = 1.45

[equipment.weapon]
class = "Sword"
swingTimer = 0.45
baseAttackRange = 26
baseKnockback = 60
stunPower = 0.25

[[equipment.weapon.damage]]
type = "Slashing"
min = 14
max = 16

[[equipment.weapon.damage]]
type = "Pyro"
min = 3
max = 6
```

Allowed `slot` / `extraSlot` values:
`Helmet`, `Armor`, `Boots`, `Trinket`, `Weapon`, `Offhand`,
`LightSource`, `LumberAxe`, `Pickaxe`, `FishingRod`, `Ammunition`,
`Back`.

Allowed `material`: `Flint`, `Copper`, `Bronze`, `Iron`, `Silver`,
`Gold`, `Steel`, `Cheat`, `Rusted`.

Allowed `class`: `Sword`, `Spear`, `Crossbow`, `Shield`, `Arrow`,
`SpellTome`, `Dagger`, `Sledgehammer`, `Bow`, `Fists`, `GrapplingHook`,
`Javelin`, `Quiver`.

Allowed damage `type`: `Slashing`, `Piercing`, `Bludgeoning`, `Pyro`,
`Chloro`, `Aqua`, `Cosmo`, `Necro`.

**Spell tomes (offhand weapons).** Under `[equipment.weapon]` you can add
`[equipment.weapon.spellTome]` with `spellId`, `chargedSpellId`, `chargeTime`,
`target`, and `chargedTarget` (same semantics as
`WeaponStatsDefinition.SpellTome` in the C# API). Spell ids must already exist
in the game.

### Recipe

```toml
resultItemId = "weapon:mod:emberpack:ember_sword"
resultAmount = 1
station = "anvil"

[[ingredients]]
itemId = "material:iron_bar"
amount = 8

[[ingredients]]
itemId = "material:coal"
amount = 3
```

### Icon

```toml
id = "icon:emberpack:ember_sword"
texture = "assets/icons/ember_sword.png"
spriteWidth = 32
spriteHeight = 32
replaceExisting = false
```

`texture` is a forward-slash path relative to the package root. The
loader extracts assets to:

```text
romestead_modding/artifacts/cache/romods/<manifestId>/<version>/assets/...
```

and points `IconDefinition.TexturePath` at the extracted file. Cache
re-extraction happens automatically when the archive changes.

### Stat

```toml
id = "Mana"
name = "Mana"
description = "Your maximum magical reserves."
icon = "ui:energy"
type = "Entity"            # Entity | Citizen | World
flags = "All"              # comma-separated, e.g. "Additive, Multiplier"
minValue = 0
maxValue = 9999
defaultValue = 100
isPercentage = false
# stringFormat is optional; defaults to "P0" when isPercentage = true,
# otherwise "0.".
```

Validators check that `minValue <= maxValue` and `defaultValue` lies
inside `[minValue, maxValue]`. `flags` is a comma-separated list of
`ModStatFlags` names; numeric tokens are rejected.

### Skill

```toml
id = "skill:mod:masonry"
name = "Masonry"
description = "Improves stonework and construction techniques by {0}."
icon = "trowel"
value = 0.05               # per-level effect; the {0} in description is formatted with this
experienceGainFactor = 1.0
```

### Skill effect

```toml
skillId = "skill:mod:masonry"
type = "ExperienceGainMultiplier"
targetSkillId = "skill:construction"
valuePerLevel = 0.03
```

The composite key `(skillId, type, targetSkillId)` is used for
duplicate detection within a package.

### Player class

```toml
id = "player_class:mod:mason"
name = "Mason"
bonusSkill = "skill:mod:masonry"

[[skillBonuses]]
skillId = "skill:woodcutting"
level = 2

startingClothes = ["armor:civilian:8", "armor:civilian:legs"]

[[startingInventory]]
itemId = "placeable:workbench"
amount = 1

startingFavourPoints = 1
```

### Text

A single localization entry, resolved by the game's StringId translation
system. One entry per file. Useful for arbitrary keys that aren't an item /
skill name or description (those have their own `nameTextId` /
`descriptionTextId` fields).

```toml
id = "item.emberpack.ember_sword.lore"
text = "Forged in the calderas of the old world, its edge never cools."
```

### Aggro tuning

Tweaks enemy aggro/leash behavior. One rule per file. Aggro tuning is consumed
**client-side** (the `ClientCore` patches), so an aggro-only package should use
`syncMode = "ClientOnly"`.

```toml
id = "justin.calmbeasts:max-leash"
type = "MaxLossRadiusTiles"   # MaxLossRadiusTiles | LossRadiusMultiplier | DisableAllyChainAggro | ThreatDecayMultiplier
value = 20                    # meaning depends on type; ignored for DisableAllyChainAggro
applyToBosses = false
```

`value` is a tile count for `MaxLossRadiusTiles`, a multiplier for
`LossRadiusMultiplier` / `ThreatDecayMultiplier`, and ignored for
`DisableAllyChainAggro`. An unrecognized `type` is reported at load time with
the list of allowed values.

### Crafting station

Gives mod recipes their own bench identity (name + icon in the crafting
window header). Match `id` from a recipe's `station` field so those recipes
appear under this station.

```toml
id = "embercraft"
name = "Embercraft Bench"
iconId = "icon:emberpack:ember_sword"   # vanilla icon id, or one registered via *.icon.toml
```

A recipe targets the station by id:

```toml
resultItemId = "weapon:mod:emberpack:ember_sword"
station = "embercraft"
# ...ingredients...
```

### Placeable crafting bench

A world object the player crafts, drops, and presses **E** to open a crafting
window scoped to `stationId`. The loader clones a vanilla bench (`template`) and
re-points it at your station; you supply ids plus world art. `texture` is an
archive-relative PNG (extracted like an icon). Pair it with a
`*.crafting-station.toml` (the `stationId`) and recipes on that station.

```toml
id = "justin.emberpack.embercraft-bench"
stationId = "embercraft"                 # must match a *.crafting-station.toml id
displayName = "Embercraft Bench"
description = "Place it, then press E to open the Embercraft window."
iconId = "icon:emberpack:ember_sword"    # inventory icon for the placeable item
texture = "assets/placeables/embercraft_bench.png"
spriteWidth = 32
spriteHeight = 48
collisionWidth = 28
collisionHeight = 16
template = "WarTable"                     # Cauldron | Campfire | WarTable (default)
```

Optional fields: `spriteOffsetX/Y`, `collisionOffsetX/Y`. The placeable's `id`
is also the item id the player crafts/carries — grant it via a recipe or a
player class's starting inventory. An unrecognized `template` is reported at
load time with the allowed values.

### Map

Map tweaks redirect map loads. A single `*.map.toml` may declare any number of
`[[aliases]]` (point one map id at another) and `[[files]]` (replace a map's
geometry with a packaged file). Map tweaks affect **client geometry only**
(the server stays authoritative for entities/loot), so a map package should use
`syncMode = "ClientOnly"`. `source` is an archive-relative file (extracted like
any asset).

```toml
[[aliases]]
original = "maps/interiors_new/insula_2"
replacement = "maps/dungeons/plains/plains_crypt_ruin"

[[files]]
mapId = "maps/interiors_new/insula_1"
source = "assets/maps/insula_1.tmx"
format = "Tmx"                              # Cmx | Tmx
```

A `*.map.toml` must declare at least one `[[aliases]]` or `[[files]]` entry.
Like value overrides, a map file does not contribute a single top-level id to
the package duplicate-id pass; a duplicate alias/file id across mods is caught
at load time.

### Value override

File pattern: `*.value-override.toml`. Tweaks numeric fields on **existing**
items (by id) and max health on **entity template** rows (by base doodad
guid). The runtime maps these into `IValueOverrideRegistry` like C# mods;
see `ValueOverrideBootstrap` in the StartupHook.

```toml
[[entityHealth]]
baseId = "00000000-0000-0000-0000-000000000000"  # entity template guid from game data
maxHealth = 120

[[items]]
id = "weapon:iron_sword"
maxStackSize = 1
tier = 2

[items.weapon]
swingTimer = 0.42
baseAttackRange = 28

[[items.weapon.damage]]
type = "Slashing"
min = 12
max = 15
```

`baseId` must be a GUID string. Item `id` is the game's item id. Only
fields you set are applied. The package duplicate-id validator does not
assign a single id per value-override **file** — avoid repeating the same
`[[items]]` id across multiple `*.value-override.toml` files in one
package.

For deeper field documentation see the C# definition types in
[`src/Romestead.ModLoader.Abstractions/`](../src/Romestead.ModLoader.Abstractions/);
the TOML key names match the C# property names in camelCase.

## Packaging command

The CLI ships at `artifacts/romod-tool/romestead-mod.dll`:

```text
romestead-mod init     <ModId> [destinationFolder]
romestead-mod validate <folderOrFile>
romestead-mod pack     <folder> -o <output.romod>
```

`pack` runs the full validation pipeline before writing the output. If
anything reports an error, the output file is not produced.

`validate` accepts either a source folder or an already-built `.romod`
file. Exit code is non-zero when errors are reported, so it composes
with CI.

The build script auto-packs every folder under `romods/` into
`artifacts/mods/<folder>.romod`, so dropping a new sample folder in
that directory is a complete contribution.

## Install location

`.romod` files go in the same folder as C# mod folders:

```text
artifacts/mods/
  Romestead.NewItemsMod/
    Romestead.NewItemsMod.dll
  EmberPack.romod
  AnotherPackage.romod
```

`mods.json` disable rules apply by manifest id, regardless of whether
the mod is a `.romod` or a DLL:

```json
{
  "disabledMods": ["justin.emberpack"]
}
```

## Schema evolution

`schemaVersion` lets the format grow without breaking older packages.

* The current loader supports `schemaVersion = 1`.
* When a future version bumps the schema, a migrator implementing
  `Romestead.RomodFormat.Schema.IRomodSchemaMigrator` is registered on
  `RomodSchemaMigratorRegistry`. Migrators run automatically before
  validation; package authors don't have to do anything.
* Adding a new content kind (e.g. `*.spell.toml`) requires:
  * a new value on `RomodContentKind`,
  * a parser implementing `IRomodContentParser` (registered with the
    default `RomodContentParserRegistry`),
  * a mapper case in `RomodToDefinitionMapper` (in
    `Romestead.StartupHook`) that converts the TOML model into the
    appropriate `*Definition` from `Romestead.ModLoader.Abstractions`,
  * registration from `RomodDataMod.RegisterContent` into the matching
    `IContentRegistry` property when the kind is wired for `.romod`.

Existing content parsers should not need to change when a new content
kind is added — each kind owns its own parser, model, and mapper case.
The reader, validator, dependency sorter, and runtime loader are
kind-agnostic.

## What `.romod` packages CANNOT do

* Run code. Use a C# mod if you need a Harmony patch, a scene hook, or
  custom UI.
* Express runtime behavior. `.romod` now covers **every** content registry the
  C# API exposes — items, recipes, icons, stats, skills, skill effects, player
  classes, value overrides, text, aggro tuning, crafting stations, placeables,
  and maps (see `RomodDataMod.RegisterContent`). Anything beyond declarative
  content (Harmony patches, scene hooks, custom UI, runtime API consumption)
  still needs a C# mod.
* Add brand-new game systems beyond what those registries support.
* Register client-only assets (e.g. UI widgets) other than icons. Use
  `syncMode = "ClientOnly"` and stick to data the existing client-side
  drains know how to consume; otherwise add a C# mod.

If your `.romod` content depends on a content type that doesn't have a
shared-loader path yet, the loader logs a clear "unsupported content
type" message rather than silently dropping it.
