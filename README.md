# Romestead Modding Workspace

> **Experimental — early development.** Breaking changes are expected. This project is not affiliated with or endorsed by the Romestead developers; use at your own risk, keep backups before installing, and re-apply the loader after game updates or Steam file verification. See [LICENSE](LICENSE). After cloning, copy `Workspace.local.props.example` to `Workspace.local.props` (or set `ROMESTEAD_GAME_ROOT` / `ROMESTEAD_SERVER_ROOT`); see [CONTRIBUTING.md](CONTRIBUTING.md).

A mod loader for **Romestead** (a .NET 8 MonoGame title). Supports both the
client (`Romestead.exe`) and the dedicated server (`Server.exe`). The loader is
installed by IL-patching the entry assembly (`Romestead.dll` on client,
`Server.dll` on the dedicated server): a single `call Initialize()` is injected
at the top of `Main`, so the hook activates no matter how the host is launched
(Steam, desktop shortcut, direct exe, the Steam-restart relaunch, server
console — all the same).

The workspace can live outside the game install. Local client/server roots are
stored in `Workspace.local.props`, and the install scripts deploy the built
mods plus `mods.json` into each target host's `romestead_modding/` runtime
folder.

## Which mod format should I use?

Two first-class mod formats coexist in the same `artifacts/mods/` folder:

- **`.romod`** — A zip of TOML files plus assets. Data-only: the same declarative
  content surface as C# `IContentRegistry` (items, recipes, icons, stats, skills,
  skill effects, player classes, value overrides, text, aggro tuning, crafting
  stations, placeables, maps). No build step beyond `romestead-mod pack`. Pick
  this when you only add content and do not need code.
- **C# mod (`mods/<Name>/*.dll`)** — A compiled `IRomesteadMod` /
  `IContentMod`. Required for Harmony patches, scene hooks, custom UI,
  runtime API consumption, or any logic that needs to react at runtime.

`.romod` packages and C# mods participate in the same dependency graph and
the same duplicate-id checks. You can ship the same mod as both formats
(though there is rarely a reason to).

See [docs/romod-packages.md](docs/romod-packages.md) for the `.romod`
format spec and quick start. For how the C# mod surface lines up with
`.romod`, see [docs/modding-api-overview.md](docs/modding-api-overview.md).

## Layout

- `src/Romestead.ModLoader.Abstractions`
  Shared mod interfaces and metadata attributes — what mods compile against.
  No game references; safe to depend on from any host.
- `src/Romestead.StartupHook`
  The bootstrapper called by the Cecil-injected hook. Runs on **both** client
  and server. Discovers compiled C# mod folders and `.romod` packages under
  `artifacts/mods/`, registers an
  `AssemblyLoadContext.Default.Resolving` fallback that probes hook and mod
  folders, runs `SharedContentBootstrap` (strongly-typed `ItemDataBase.AddItems`
  + `ItemRecipeDataBase.AddItemRecipes` Harmony patches), and invokes each
  mod's `Initialize`. `.romod` packages are wrapped in a `RomodDataMod`
  adapter and registered through the same `IContentRegistry` path as C# mods —
  there is no separate runtime path. References only `Shared.dll` +
  `CandideServer.dll` + Tomlyn (transitively via `Romestead.RomodFormat`).
- `src/Romestead.ModLoader.ClientCore`
  Client-only loader infrastructure (not a mod). Installs the rest of the
  Harmony patches — icons, skills, player classes, recipe-station filtering,
  scene `LoadContent` hooks, aggro tuning, the in-game Mod Loader settings
  panel. Loaded explicitly by the StartupHook on `HostKind.Client` only; never
  copied to the server.
- `src/Romestead.ModLoader.Installer`
  Mono.Cecil-based installer that patches the host entry assembly
  (`Romestead.dll` or `Server.dll`) and registers the hook in `*.deps.json`
  so the .NET host adds it to TPA.
- `src/Romestead.RomodFormat`
  Game-agnostic package format library for `.romod` files (TOML manifest +
  typed TOML content + assets, zipped). Read by both the runtime loader and
  the CLI packer. References only Tomlyn; no game DLLs.
- `src/Romestead.ModLoader.RomodTool`
  CLI packer / validator (`romestead-mod init|validate|pack`). Builds to
  `artifacts/romod-tool/romestead-mod.dll`. See
  [docs/romod-packages.md](docs/romod-packages.md) for the format spec.
- `mods/Romestead.NewItemsMod`, `mods/Romestead.SkipIntroMod`, `mods/Romestead.IconDumpMod`
  Sample / reference **C# mods** — kept for things `.romod` can't express:
  custom UI + equipment (NewItemsMod), a Harmony scene patch (SkipIntroMod),
  and a dev-only reflection/icon dumper (IconDumpMod).
- `romods/EmberPack`, `romods/CalmBeastsPack`, `romods/MapTweaksPack`
  Sample **`.romod` packages** (data-only, no compile). EmberPack is a full
  item→station→placeable bench demo; CalmBeastsPack shows aggro tuning;
  MapTweaksPack shows a map alias + file redirect. The build packs each to
  `artifacts/mods/<name>.romod`.
- `tests/Romestead.RomodFormat.Tests`
  xUnit tests for the package format (reader, validator, mapping).
- `docs/`
  Focused references: [docs/README.md](docs/README.md) (index), [romod-packages.md](docs/romod-packages.md), [modding-api-overview.md](docs/modding-api-overview.md), [scripts.md](docs/scripts.md).
- `artifacts/`
  Build output. `artifacts/startuphook/` and `artifacts/clientcore/` hold the
  loader binaries; `artifacts/mods/` holds the built user mods.
  `artifacts/loader/romestead-loader.log` (inside the installed host) is the
  runtime log.

## Architecture at a glance

Three runtime assemblies, two mod-authoring formats, one mod-facing API:

| Assembly / artifact | Where it lives | Runs on | What it does |
|---|---|---|---|
| `Romestead.StartupHook.dll` | game install root (both hosts) | both | Cecil entry, mod discovery, registry init, multiplayer compatibility coordinator, `SharedContentBootstrap` (items + recipes). Drives both C# and `.romod` mods through one pipeline. |
| `Romestead.RomodFormat.dll` + `Tomlyn.dll` | game install root (both hosts) | both | Game-agnostic `.romod` reader / validator / packer. Used by the StartupHook and the CLI packer. |
| `Romestead.ModLoader.ClientCore.dll` | client install root only | client | All client-only Harmony patches: UI, icons, skills, player classes, scenes, aggro. |
| `mods/<ModId>/*.dll` | per-mod | per `SyncMode` | User C# mods. Register definitions into `ModRegistries.*.Pending`; the loader drains them. |
| `mods/<ModId>.romod` | per-mod | per `syncMode` | User `.romod` packages. Same destination registries as C# mods — no second path. |

The mod-facing API is the same on both hosts: every mod is a `IRomesteadMod`
implementation under `mods/`. Per-mod `SyncMode` controls which hosts run it.
Loader infrastructure (StartupHook + ClientCore) is never a mod.

## Install — Client

Close the game first. Then from the workspace root:

```powershell
powershell -ExecutionPolicy Bypass -File .\install.ps1
```

This builds everything, backs up `Romestead.dll` and `Romestead.deps.json` to
`*.modloader-backup`, injects the hook, copies `Romestead.StartupHook.dll` and
`Romestead.ModLoader.ClientCore.dll` (plus `0Harmony.dll`) into the game
folder, deploys `artifacts/mods/` plus `mods.json` into
`<game-root>\romestead_modding\`, and registers the hook in
`Romestead.deps.json`. After install, launch Romestead the normal way.

## Dev Loop

For the normal edit-build-deploy loop, use:

```powershell
powershell -ExecutionPolicy Bypass -File .\dev-install.ps1
```

Defaults:

- installs the `Client`
- builds `Debug`
- deploys into the configured game root from `Workspace.local.props`

Useful variants:

```powershell
# Debug deploy to client, then launch the game.
powershell -ExecutionPolicy Bypass -File .\dev-install.ps1 -Launch

# Deploy to the dedicated server instead.
powershell -ExecutionPolicy Bypass -File .\dev-install.ps1 -Target Server

# Deploy both client and server in one pass.
powershell -ExecutionPolicy Bypass -File .\dev-install.ps1 -Target Both

# Use Release instead of Debug.
powershell -ExecutionPolicy Bypass -File .\dev-install.ps1 -Configuration Release
```

## Install - Dedicated Server

Run `install-server.ps1`:

```powershell
powershell -ExecutionPolicy Bypass -File .\install-server.ps1
```

Otherwise pass `-ServerRoot` explicitly:

```powershell
powershell -ExecutionPolicy Bypass -File .\install-server.ps1 -ServerRoot "D:\GameServers\Romestead"
```

This calls `build.ps1 -ServerOnly` (which skips `ClientCore` and any mod that
references client-only DLLs - see below), patches `Server.dll`, registers the
hook in `Server.deps.json`, copies the StartupHook into the server folder, and
deploys `artifacts/mods/` plus `mods.json` into the server runtime folder.
`ClientCore.dll` is **never** copied to the server. After install, launch the
dedicated server normally - the loader log lives at
`<server-install>/romestead_modding/artifacts/loader/romestead-loader.log`.


## Uninstall

Client:

```powershell
powershell -ExecutionPolicy Bypass -File .\uninstall.ps1
```

Server:

```powershell
powershell -ExecutionPolicy Bypass -File .\uninstall-server.ps1
```

Each restores its host's entry DLL and `deps.json` from backup. Hook DLLs left
in the install folder are harmless and can be deleted manually for a fully
clean revert.

## Mod Author Basics

Create one folder under `romestead_modding/mods/` for your mod project and
make the built DLL land under `romestead_modding/artifacts/mods/<AssemblyName>/`.
The folder and entry DLL name should match, for example:

```text
artifacts/mods/Romestead.NewItemsMod/Romestead.NewItemsMod.dll
```

Each mod needs one concrete `IRomesteadMod` type with a manifest:

```csharp
using Romestead.ModLoader;

namespace Example.Mod;

[ModManifest("example.mod", "Example Mod", "0.1.0")]
public sealed class ExampleMod : IRomesteadMod, IContentMod
{
    public void Initialize(IModContext context)
    {
        context.Logger.Info("Example Mod initialized.");
    }

    public void RegisterContent(IContentRegistry registry)
    {
        registry.Items.Register(new ItemDefinition
        {
            Id = "material:mod:example_item",
            NameTextId = "item.example.mod.example_item.name",
            Name = "Example Item",
            DescriptionTextId = "item.example.mod.example_item.description",
            Description = "An item added by a mod.",
            Icon = "ember_orchids",
            MaxStackSize = 25,
            Tier = 1
        });

        registry.Recipes.Register(new RecipeDefinition
        {
            ResultItemId = "material:mod:example_item",
            ResultAmount = 1,
            Station = "campfire",
            Ingredients =
            [
                new RecipeIngredient("material:coal", 1)
            ]
        });

        registry.Skills.Register(new SkillDefinition
        {
            Id = "skill:mod:masonry",
            NameTextId = "skill.example.mod.masonry.name",
            Name = "Masonry",
            DescriptionTextId = "skill.example.mod.masonry.description",
            Description = "Improves stonework and construction techniques by {0}.",
            Icon = "trowel",
            Value = 0.05f,
            ExperienceGainFactor = 1.0f
        });

        registry.SkillEffects.Register(new SkillEffectDefinition
        {
            SkillId = "skill:mod:masonry",
            Type = SkillEffectType.ExperienceGainMultiplier,
            TargetSkillId = "skill:construction",
            ValuePerLevel = 0.03f
        });

        registry.PlayerClasses.Register(new PlayerClassDefinition
        {
            Id = "player_class:mod:example_mason",
            NameTextId = "player_class.example.mod.example_mason.name",
            Name = "Mason",
            BonusSkill = "skill:mod:masonry",
            StartingClothes =
            [
                "armor:civilian:8",
                "armor:civilian:legs"
            ],
            StartingInventory =
            [
                new RecipeIngredient("placeable:workbench", 1),
                new RecipeIngredient("food:meat_small", 2)
            ]
        });
    }
}
```

Use stable, unique manifest IDs and content IDs. The Mod Loader settings page
uses the manifest ID, name, version, registered item IDs, and registered recipe
IDs for display and diagnostics. Content mods should prefer `IContentMod`
instead of patching game databases directly; the loader owns the safe injection
timing.

### Runtime APIs

`IModContext` exposes a typed API resolver through `context.Apis`, plus
helper extensions:

```csharp
var worldMap = context.GetApi<IWorldMapApi>();
worldMap.RevealAll();
```

The resolver is the preferred way to consume runtime capabilities that do not
fit the content registries.

| API | Client | Dedicated server |
|---|---|---|
| `IItemRegistry`, `IRecipeRegistry`, `ITextRegistry`, `IIconRegistry`, `ISkillRegistry`, `ISkillEffectRegistry`, `IPlayerClassRegistry`, `IAggroTuningRegistry`, `IContentRegistry` | ✓ | ✓ |
| `IMultiplayerApi` (session mode / authority checks) | ✓ | ✓ |
| `IModLifecycle` (`GameReady` event) | ✓ | — |
| `ISceneApi` (current scene + scene-changed events) | ✓ | — |
| `IWorldMapApi` (queued full-map reveal) | ✓ | — |

On the dedicated server, `context.GetApi<IWorldMapApi>()` (and the other
client-only APIs) throws `InvalidOperationException`. Use
`context.TryGetApi<TApi>(out var api)` if you want a mod to run on both hosts
and gracefully no-op the client-only bits:

```csharp
if (context.TryGetApi<IWorldMapApi>(out var worldMap) && worldMap is not null)
{
    worldMap.RevealAll();
}
```

`IModCapabilityApi` is available on both client and dedicated server and lets a
mod distinguish "API exists on this host" from "the fragile feature behind that
API survived this game build." For client-backed features, prefer a capability
check when you want clean degradation:

```csharp
var capabilities = context.GetApi<IModCapabilityApi>();
if (capabilities.TryGetCapability(ModCapabilityId.WorldMap, out var worldMapCapability) &&
    worldMapCapability.State is ModCapabilityState.Available or ModCapabilityState.Degraded &&
    context.TryGetApi<IWorldMapApi>(out var worldMap) &&
    worldMap is not null)
{
    worldMap.RevealAll();
}
```

When a fragile client capability is unavailable, the API stays registered on
the client but degrades safely instead of throwing from routine use:

- `IWorldMapApi.RevealAll()` becomes a logged no-op and `IsReady` stays `false`
- `IModOverlayRegistry.Show(...)` returns an inert hidden handle
- `IModWindowRegistry.Open(...)` returns an inert closed handle
- `IModCraftingRegistry.OpenStation(...)` returns an inert closed handle
- `IModLifecycle` / `ISceneApi` stay inactive (`IsGameReady == false`, `CurrentScene == null`, no events raised)

A clean pattern for a content mod that targets both hosts: register
items/recipes inside `RegisterContent` (drained on both hosts via
`SharedContentBootstrap`), and gate any UI/scene/world-map calls behind a
`TryGetApi` plus capability check inside `Initialize`.

`IModContext` exposes the full content-registry surface — `Items`, `Recipes`,
`Text`, `Icons`, `Skills`, `SkillEffects`, `PlayerClasses`, `Stats`,
`ValueOverrides`, `CraftingStations`, `Placeables`, `AggroTuning`, and `Maps` —
the same set as the `IContentRegistry` passed to `RegisterContent`. Both surfaces
are backed by the same per-mod, duplicate-checked registry, so it makes no
difference whether you register content in `Initialize` (via `context.*`) or in
`RegisterContent` (via `registry.*`): it is duplicate-checked and attributed to
your mod either way. `RegisterContent` remains the recommended home for bulk
content registration. The lifecycle and client-only UI members (`Lifecycle`,
`Ui`, `Overlays`, `Windows`, `Crafting`) are only on `IModContext`.

### Settings UI and Sidebar Entries

Mods can register declarative settings pages and native left-hand sidebar
entries through `context.Ui`:

```csharp
context.Ui.RegisterSettingsPage(new ModSettingsPageDefinition
{
    Id = "example.mod.settings",
    Title = "Example Mod",
    Icon = "scroll:red",
    Order = 100,
    Build = _ => new ModSettingsPage
    {
        Sections =
        [
            new ModSection
            {
                Title = "Overview",
                Rows =
                [
                    new ModInfoRow { Label = "Version", Value = "0.1.0" },
                    new ModToggleRow
                    {
                        Label = "Enable feature",
                        Value = true,
                        OnChanged = (context, enabled) => context.RefreshCurrentPage()
                    }
                ]
            }
        ]
    }
});

context.Ui.RegisterSidebarEntry(new ModSidebarEntryDefinition
{
    Id = "example.mod.sidebar",
    Title = "Example Mod",
    Icon = "scroll:red",
    Order = 100,
    TargetPageId = "example.mod.settings"
});
```

`RegisterSettingsPage(...)` adds the page content. `RegisterSidebarEntry(...)`
adds a real pause-menu sidebar button that opens that page. You can register a
page without a sidebar entry if you only want it reachable from another mod UI
page. The built-in `Romestead.NewItemsMod` now uses this pattern as a working
reference.

### Multiplayer Sync Mode

Mods can declare how they participate in multiplayer compatibility directly on
their manifest:

```csharp
[ModManifest("example.mod", "Example Mod", "0.1.0", SyncMode = MultiplayerSyncMode.ClientOnly)]
```

Available modes are:

- `ClientOnly`
- `ServerOnly`
- `RequiredOnClient`
- `Incompatible`

If you do not set `SyncMode`, the loader defaults to `RequiredOnClient`. That
strict default is intentional for content and gameplay mods.

Use `IMultiplayerApi` for live checks:

```csharp
var multiplayer = context.GetApi<IMultiplayerApi>();
if (multiplayer.IsMultiplayer && multiplayer.IsClient)
{
    context.Logger.Info("Running as a multiplayer client.");
}
```

### What gets injected on which host

Mods always register into the same in-process `ModRegistries.*.Pending` lists,
but only some content types are drained into the game on both hosts. Today:

| Content type | Client | Dedicated server |
|---|---|---|
| `Items` (`registry.Items`) | ✓ | ✓ |
| `Recipes` (`registry.Recipes`) | ✓ | ✓ |
| `Stats` (`registry.Stats`) | ✓ | ✓ |
| `Icons` (`registry.Icons`) | ✓ | — (client-only by design) |
| `Skills` (`registry.Skills`) | ✓ | — |
| `SkillEffects` (`registry.SkillEffects`) | ✓ | — |
| `PlayerClasses` (`registry.PlayerClasses`) | ✓ | — |
| `AggroTuning` (`registry.AggroTuning`) | ✓ | — (consumer patches live in `ClientCore`; server-side aggro tuning would require promoting them) |
| `Text` (`registry.Text`) | ✓ | not user-visible on a headless server |

Items and recipes are shared because they affect inventory and crafting on both
sides — the strongly-typed `SharedContentBootstrap` runs the patch on both
hosts. Everything else currently only fires inside `ClientCore` (Harmony
patches on `IconDataBase.AddIcons`, `SkillsDataBase.AddData`,
`PlayerClassDataBase.AddPlayerClass`, etc.). If a future need arises to
authoritatively register a modded skill or player class server-side, the right
move is to promote that drain into `SharedContentBootstrap` — don't add a
second reflection-based path.

### Authoring mods for the dedicated server

The mod loader is built so a server admin with **only** the dedicated server
installed (no client install on the box) can build and run mods. To stay
server-buildable, your mod's `.csproj` must not directly reference client-only
game DLLs. In practice:

- ✅ `<ProjectReference Include="..\..\src\Romestead.StartupHook\Romestead.StartupHook.csproj" />` is always safe (StartupHook itself is server-buildable).
- ✅ `<Reference Include="Shared">` and `<Reference Include="CandideServer">` are safe — both DLLs ship with the dedicated server.
- ❌ `<Reference Include="Romestead">` (the client entry assembly) — only available on the client.
- ❌ `<Reference Include="MonoGame.Framework">`, `<Reference Include="CandideCreator.Shared">` — present on both installs today but conceptually client-side; assume client-only.

If you do need client-side references for, say, a UI mod, mark it
`SyncMode=ClientOnly` and accept that it will not compile on a server-only
machine. `build.ps1 -ServerOnly` skips `Romestead.ModLoader.ClientCore` and
`Romestead.SkipIntroMod` for exactly that reason — both reference
`Romestead.dll` directly. Add new client-only mods to that skip list in
`build.ps1` if you go that route.

Runtime selection (`SyncMode=ClientOnly` mods being skipped on the dedicated
server) is independent of build selection — a mod can be `SyncMode=ServerOnly`
or `RequiredOnClient` (the default) and still be perfectly server-buildable.

### Weapons, Armor, and Equipment

Any `ItemDefinition` can become equippable by setting `Equipment`. The slot,
material, stat bonuses, and (for weapons/shields) combat stats all live on
`EquipmentDefinition`. The loader maps these into the game's
`EquippableItem` / `WeaponStats` / `ShieldStats` types using strong types
inside `SharedContentBootstrap` — no per-mod Harmony, same as regular items.
Equipment items inject on both client and server.

```csharp
registry.Items.Register(new ItemDefinition
{
    Id = "weapon:mod:ember_sword",
    NameTextId = "item.example.mod.ember_sword.name",
    Name = "Ember Sword",
    DescriptionTextId = "item.example.mod.ember_sword.description",
    Description = "A blade smelted in volcanic resin. Burns what it cuts.",
    Icon = "sword:iron",
    MaxStackSize = 1,
    Tier = 3,
    Equipment = new EquipmentDefinition
    {
        Slot = EquipmentSlot.Weapon,
        Material = EquipmentMaterial.Iron,
        Display = new EquipmentDisplayDefinition
        {
            Id = "cdd:mod:ember_sword",
            Fragments =
            [
                new EquipmentDisplayFragmentDefinition
                {
                    SkinName = "ExampleModEmberSword",
                    TexturePath = Path.Combine(modDirectory, "assets", "equipment", "ember_sword_held.png"),
                    SpriteWidth = 48,
                    SpriteHeight = 48,
                    SkinTag = EquipmentSkinTag.Tool,
                    SpacTag = EquipmentSpacTag.Tool,
                    Layer = 1f
                }
            ]
        },
        HeldVfx = new EquipmentHeldVfxDefinition
        {
            ParticleEmitterId = "flame_small",
            ParticleOffsetZ = 14f,
            ParticleLineLength = 26f,
            ParticleLineWidth = 1.5f,
            ParticleLineHeight = 4f,
            ParticleSpawnFrequency = 0.025f,
            ParticleAmountSpawned = 1,
            LightOffsetX = 10f,
            LightOffsetZ = 18f,
            LightRadius = 58f,
            LightIntensity = 1.45f
        },
        Weapon = new WeaponStatsDefinition
        {
            Class = WeaponClassPreset.Sword,
            Damage =
            [
                new DamageRange { Type = DamageTypeId.Slashing, Min = 14, Max = 16 },
                new DamageRange { Type = DamageTypeId.Pyro,     Min =  3, Max =  6 }
            ],
            SwingTimer = 0.45f,
            BaseAttackRange = 26f,
            BaseKnockback = 60f,
            StunPower = 0.25f
        }
    }
});
```

Armor uses the same shape with a non-weapon slot and optional stat bonuses:

```csharp
registry.Items.Register(new ItemDefinition
{
    Id = "armor:mod:ember_helmet",
    Name = "Ember Helmet",
    Description = "An iron helmet lined with ember-resin.",
    Icon = "helmet:iron",
    MaxStackSize = 1,
    Tier = 3,
    Equipment = new EquipmentDefinition
    {
        Slot = EquipmentSlot.Helmet,
        Material = EquipmentMaterial.Iron,
        DisplayId = "cdd:head:iron",
        StatBonuses =
        [
            new StatBonusDefinition { StatId = "Armor", Additive = 14f },
            new StatBonusDefinition { StatId = "Health", Additive = 20f },
            new StatBonusDefinition { StatId = "EnergyRegeneration", Additive = 0.5f }
        ]
    }
});
```

Shields use `Slot = EquipmentSlot.Offhand` with `Shield = new ShieldStatsDefinition { … }`.

**Icon vs. DisplayId.** These are two separate IDs. An equippable item needs an
inventory icon and either a vanilla `DisplayId` or a custom `Display`:

- **`Icon`** is the inventory sprite (the small picture shown in the bag and
  hotbar). Conventions seen in vanilla: `"sword:iron"`, `"helmet:iron"`,
  `"crossbow:bronze"`, `"iron_armor"`. To reuse a vanilla icon, dig through
  `ItemDataBase` for the closest match; to ship a custom one, register an
  `IconDefinition` (see *Item Icons*) and use its ID.
- **`EquipmentDefinition.DisplayId`** is the on-character model — what
  actually appears in your character's hand or on their head while equipped.
  Vanilla uses the `cdd:` prefix: `"cdd:iron_sword"`, `"cdd:head:iron"`,
  `"cdd:armor:iron"`. If you use neither `DisplayId` nor `Display`, a weapon
  can swing an invisible blade. To reuse a vanilla model, point at one of those
  IDs.

- **`EquipmentDefinition.Display`** registers a custom on-character model. If
  `Display.Id` and `DisplayId` are both omitted, the loader generates
  `cdd:mod:{itemId}` and assigns that to the item.

**Custom held weapon sprites.** Romestead's player-held weapons are not normal
item icons. They are player skin slices that swap into the character SPAC
animation. For a normal sword/dagger/spear/bow/hammer, register one
`EquipmentDisplayFragmentDefinition` with `SkinTag = EquipmentSkinTag.Tool`,
`SpacTag = EquipmentSpacTag.Tool`, and a PNG sheet using the vanilla held-tool
layout: 48x48 frames in a 23x5 grid (1104x240 pixels). The SPAC animation
chooses frame positions; your sheet supplies the art for those frames.

For full-color custom art, leave `Palette` empty. If you want a vanilla-style
palette swap instead, point `SkinName` at an existing vanilla skin such as
`"Sword"` and add palette rows like `new EquipmentDisplayPaletteDefinition
{ PaletteId = "weapon", Row = 5 }`.

**Held equipment VFX.** `EquipmentDefinition.HeldVfx` adds a client-only
cosmetic effect while the item is equipped. It can reuse a vanilla particle
emitter such as `"flame_small"` and/or attach a flickering light to the holder.
Offsets can rotate with the character direction, so `ParticleLineLength` and
`ParticleLineAngleDegrees` can be tuned to sit along a blade instead of at the
player's feet. This is visual only: it is not saved, synchronized, or used for
damage.

**Sensible value ranges** (for weapons, derived from vanilla iron-tier
gear so your mod's items don't feel broken):

- `SwingTimer`: ~0.4 (fast dagger / sword) to ~1.2 (sledgehammer). Lower = faster swing.
- `BaseAttackRange`: melee weapons ~25–30 (world units, **not tiles**). Spears and polearms are larger; daggers are smaller.
- `BaseKnockback`: ~40 (light) to ~120 (heavy).
- `StunPower`: 0–1, often 0.25 for swords / 0.5+ for blunt weapons.
- `EnergyCost`: 0 for default-cost weapons; non-zero only if the weapon should be unusually exhausting.

A new modded weapon that "feels broken" almost always has either a tiny
`BaseAttackRange` (you registered it with a number that would be fine in
tiles but is sub-pixel in world units) or a `SwingTimer` that's way larger
than vanilla.

**Available slots** (`EquipmentSlot`): `Helmet`, `Armor`, `Boots`, `Trinket`,
`Weapon`, `Offhand`, `LightSource`, `LumberAxe`, `Pickaxe`, `FishingRod`,
`Ammunition`, `Back`. The two-handed sword pattern (weapon that also occupies
offhand) is handled via `EquipmentDefinition.ExtraSlot`.

**Available weapon classes** (`WeaponClassPreset`): `Sword`, `Spear`, `Crossbow`,
`Shield`, `Arrow`, `SpellTome`, `Dagger`, `Sledgehammer`, `Bow`, `Fists`,
`GrapplingHook`, `Javelin`, `Quiver`. The preset binds the weapon to the
matching vanilla skill (e.g. `Sword` → `skill:swords`). Defining a brand-new
weapon class with a custom skill is a future-phase feature.

**Damage channels** (`DamageTypeId`): three physical (`Slashing`, `Piercing`,
`Bludgeoning`) and five elemental (`Pyro`, `Chloro`, `Aqua`, `Cosmo`, `Necro`).
Each entry in `Damage` adds Min/Max for that channel; omit a channel to leave
it at zero.

**Stat bonuses** (`StatBonusDefinition`): match the game's
`StatModificationData` (`Additive`, `AdditiveMultiplier`, `BaseMultiplier`,
`BonusMultiplier`, `Multiplier`). The `StatId` must be one of the vanilla
stat IDs registered in the game's stat database — **a stat ID the game does
not recognize renders as "Error" in the equipment UI**, including both the
stat name and the value formatter, so this is the failure mode to watch for.

The vanilla entity stat IDs (from `Shared.Models.Stats.StatId`) are:

- Core: `Health`, `Energy`, `EnergyRegeneration`, `Armor`, `MagicResistance`, `KnockbackResistance`, `MovementSpeed`
- Damage (flat): `MeleeDamage`, `RangedDamage`, `MagicDamage`, `ThrowingDamage`
- Damage (multipliers): `MeleeDamageModifier`, `RangedDamageModifier`, `MagicDamageModifier`, `ThrowingDamageModifier`, `HealingDoneModifier`
- Damage-channel multipliers: `TrueDamageModifier`, `SlashingDamageModifier`, `PiercingDamageModifier`, `BludgeoningDamageModifier`, `PyroDamageModifier`, `ChloroDamageModifier`, `AquaDamageModifier`, `CosmoDamageModifier`, `NecroDamageModifier`
- Damage-channel resistances: `TrueResistance`, `SlashingResistance`, `PiercingResistance`, `BludgeoningResistance`, `PyroResistance`, `ChloroResistance`, `AquaResistance`, `CosmoResistance`, `NecroResistance`
- Attack/crit: `AttackSpeed`, `AttackRangeModifier`, `CritChance`, `CritDamage`, `WeaponEnergyCost`, `Knockback`
- Utility: `PickaxePower`, `AxePower`, `LightSource`, `SiegeDamageTakenModifier`

(For citizen stats — `Citizen_Efficiency`, `Citizen_Expertise`, `Citizen_Happiness`, `Citizen_FoodCost`, `Citizen_LoyaltyGain`, `Citizen_ExperienceGain` — those exist but are not driven by `EquippableItem.StatBonuses`.)

### Endless-Cast Offhands (Spell Tomes)

Vanilla scrolls in Romestead are not consumables — they're equippable
offhand `SpellTome` weapons that cast a spell on use and never deplete (they
just cost energy per cast). Modded weapons can do the same by setting
`WeaponStatsDefinition.SpellTome`. The two spell IDs must already exist in
the game's `SpellDataBase`; injecting *custom* spells is a future phase.

```csharp
registry.Items.Register(new ItemDefinition
{
    Id = "weapon:mod:ember_tome",
    Name = "Ember Tome",
    Description = "A bound scroll that burns with the same fire that smelts the resin.",
    Icon = "scroll:red",
    MaxStackSize = 1,
    Tier = 3,
    Equipment = new EquipmentDefinition
    {
        Slot = EquipmentSlot.Offhand,
        Material = EquipmentMaterial.Iron,
        Weapon = new WeaponStatsDefinition
        {
            Class = WeaponClassPreset.SpellTome,
            SwingTimer = 0.5f,
            EnergyCost = 30f,           // energy per tap-cast
            SpecialEnergyCost = 40f,    // energy per charged cast
            MovementFactor = 0.5f,      // slow movement while wielding
            SpellTome = new SpellTomeDefinition
            {
                SpellId = "item:scroll:bolt:3",          // tap-cast: existing Ember Scroll bolt
                ChargedSpellId = "item:scroll:shield:3", // hold-cast: existing tier-3 shield
                ChargeTime = 1f,
                Target = SpellTarget.Self,
                ChargedTarget = SpellTarget.Self
            }
        }
    }
});
```

The tome is reusable forever; the energy cost (`EnergyCost`,
`SpecialEnergyCost`) is the throttle. Set both to 0 only if you want a
genuinely-free cast — usually overpowered.

**Useful vanilla spell IDs to point at** (in `Shared.Data.SpellDataBase`):

| Spell ID | What it does |
|---|---|
| `item:scroll:bolt:0` … `item:scroll:bolt:4` | Fire-bolt projectile, tier 0–4 (tier 3 = "Ember") |
| `item:scroll:shield:0` … `item:scroll:shield:4` | Charged defensive shield, tier 0–4 |
| `item:tectonic_scroll:boulder` / `:arrow` / `:tremor` | Earth spells |
| `item:minerva_scroll:feathers` / `:strike` | Wind / lightning |
| `item:diana_scroll:arrow` / `:arrowcone` | Cosmic-themed projectiles |
| `item:heal_scroll:regen` | Healing-over-time |
| `item:mirage_scroll:confusion` / `:watersplash` | Crowd-control |
| `item:druid_scroll:root` | Roots target |
| `item:scroll:snakeskin:0` (+ `:snakeskin_charged:0`) | Necro projectile + charged variant |

**`SpellTarget` values** mirror the game's `Shared.Aura.Args.Target`:
`Self`, `Target`, `TargetGround`, `ProjectilePosition`. Most projectile
spells (bolts, arrows) use `Self` — the projectile spawns from the caster
toward the mouse. Use `TargetGround` for AoEs that hit where the player
clicks.

### Custom Stats

Mods can register brand-new entity, citizen, or world stats. The stat shows
up alongside vanilla stats in the game's stat database, items can grant
bonuses to it via `StatBonusDefinition`, and the entity system treats it
identically to a built-in stat. The drain is a third Harmony prefix in
`SharedContentBootstrap` on `EntityStatsDataBase.AddStatDefinitions`, so
custom stats inject on **both** client and server — no separate path.

```csharp
registry.Stats.Register(new StatDefinition
{
    Id = "Mana",
    Name = "Mana",
    Description = "Your maximum magical reserves.",
    Icon = "ui:energy",            // reuse a vanilla icon, or register a new one
    Type = ModStatType.Entity,
    Flags = ModStatFlags.All,      // which item-bonus kinds may modify this stat
    StringFormat = "0.",
    MinValue = 0f,
    MaxValue = 999999f,
    DefaultValue = 100f
});

registry.Stats.Register(new StatDefinition
{
    Id = "ManaRegeneration",
    Name = "Mana Regeneration",
    Description = "Mana regeneration per second.",
    Icon = "ui:energy_regeneration",
    Type = ModStatType.Entity,
    DefaultValue = 2f
});
```

Once registered, items can grant Mana or ManaRegeneration just like any
vanilla stat:

```csharp
new StatBonusDefinition { StatId = "Mana", Additive = 50f }
new StatBonusDefinition { StatId = "ManaRegeneration", Additive = 0.5f }
```

**`ModStatType`** (mirrors the game's `StatType`):

- `Entity` — per-player / per-NPC stat (the common case; Health, Energy, etc. all use this).
- `Citizen` — per-citizen settlement stat (`Citizen_Efficiency`, `Citizen_Happiness`, etc.).
- `World` — world-level stat.

**`ModStatFlags`** controls which kinds of `StatModificationData` an item is
allowed to apply to your stat. Leave at `All` unless you have a specific
reason — vanilla stats all use `All`.

**`Flags`, `StringFormat`, and percentage stats.** If your stat is a
percentage modifier (e.g. `"Crit Chance"`), set `IsPercentage = true` and
`StringFormat = "P0"` so the UI renders it correctly. For damage-like flat
stats, `"0."` is the usual format.

**What this does and doesn't do:**

- ✅ The stat exists in `EntityStatsDataBase`, has a name, description, icon, and is queryable.
- ✅ Items can grant bonuses to the stat via `StatBonusDefinition`.
- ✅ The entity system treats it like any vanilla stat.
- ❌ The stat **doesn't render in the player HUD by default** — that's a separate piece of work (custom UI bar, planned for a later phase).
- ❌ The stat **isn't consumed automatically** when a weapon is used — you'd hook that yourself for now (also planned to land in the API as `WeaponStatsDefinition.ManaCost` etc.).

### Placeable Custom Crafting Benches

A **placeable crafting bench** is a world object the player crafts, drops into
the world, walks up to, and presses **E** to open a crafting window scoped to a
custom station — exactly like the vanilla campfire, cauldron, or war table.
You supply ids and art; the loader generates all three pieces of content the
game needs and wires them together.

```csharp
// 1. Register the crafting station the bench opens.
registry.CraftingStations.Register(new CraftingStationDefinition
{
    Id = "embercraft",
    Name = "Embercraft Bench",
    IconId = "icon_ember_resin"
});

// 2. Register the placeable bench itself.
registry.Placeables.Register(new ModPlaceableStation
{
    Id = "romestead.new-items.embercraft-bench", // also the placeable item's id
    StationId = "embercraft",                    // recipes on this station show up
    DisplayName = "Embercraft Bench",
    Description = "A custom bench for Embercraft recipes.",
    IconId = "icon_ember_resin",                 // inventory icon for the item
    TexturePath = Path.Combine(_modDirectory, "assets", "placeables", "embercraft_bench.png"),
    SpriteWidth = 32,
    SpriteHeight = 48,
    Template = VanillaBenchTemplate.WarTable     // which vanilla bench to clone
});

// 3. Register recipes against that station. They appear when the player opens the bench.
registry.Recipes.Register(new RecipeDefinition
{
    ResultItemId = "material:mod:ember_resin",
    ResultAmount = 1,
    Station = "embercraft",
    Ingredients = [ new RecipeIngredient("material:coal", 1) ]
});
```

The player gets the bench like any other item — grant it through a player
class's starting inventory, or register a recipe that produces it (its result
item id is the `ModPlaceableStation.Id`).

**`VanillaBenchTemplate`** picks the vanilla bench whose entity layout and
interaction controller are cloned:

- `WarTable` (default) - opens the standard crafting window and avoids the cauldron-specific animated mesh render path.
- `Cauldron` - opens the standard crafting window with cauldron-specific rendering.
- `Campfire` - opens the standard crafting window with campfire-specific behaviour.

#### How it works (the clone-the-template approach)

Rather than author binary doodad/construction assets (which needs the game's
editor and is off-limits), the loader clones the matching vanilla bench at
runtime and re-points it at your station. One `Placeables.Register` call
generates four linked pieces of content:

| Generated content | Cloned from | What changes |
| --- | --- | --- |
| **Doodad / entity** (art, collision, interaction controller, Furniture component) | the vanilla bench entity for `Template` | `CraftingStationFlags` set to `[StationId]`; registered under a deterministic guid |
| **Construction** (`"<Id>:0"`) | the matching vanilla construction shell | `Id`, `Name`, `IconId`, and `SpawnedId` pointing at the cloned doodad guid |
| **Placeable item** (`Id`) | `placeable:workbench` | `Id`, `Icon`, `Name`, and the place-construction spell args pointing at the generated construction |
| **Decoration record** (`DecorationId`) | synthesized by the loader | deterministic save-backed world record used to persist the placed bench across cold restarts |

The runtime chain is:

`item -> construction -> decoration-backed placement record -> spawned entity`

The item still casts `spell:place-construction`, and the placed entity still
uses `CraftingStationFlags` to decide which crafting window opens on `E`. The
extra decoration record is the important persistence layer: it survives a full
process restart and lets the loader rebuild the spawned bench entity on world
load if the game drops that entity during cold-load reconstruction.

**Where each piece is injected:**

- The **construction**, **placeable item**, and **decoration data** are shared
  content injected from `Romestead.StartupHook`, so they exist on both client
  and server at content-load time.
- The **entity clone** is registered into the runtime doodad/entity tables used
  by the active host.
- The **placement hook** routes custom benches through the server decoration
  spawn path, and the **cold-load repair hook** respawns the bench entity from
  the saved decoration if it is missing after `OnServerGameStateLoaded`.

**Deterministic ids.** Client and server must agree on both the cloned doodad
guid and the decoration id. `ModPlaceableStation.DeriveDoodadGuid()` computes a
stable guid from the placeable id, and `DecorationId` is derived
deterministically the same way so every process can reconstruct the same saved
record without extra coordination.

**What this does and doesn't do:**

- Player crafts or receives the item, places it, presses `E`, and gets a
  crafting window scoped to `StationId` showing that station's recipes.
- The placed bench now persists across save/leave and full game restart.
- Dedicated server deployment is supported by the normal `install-server.ps1`
  flow.
- Custom world art is supported. `TexturePath` points at the bench sprite,
  while `SpriteWidth`, `SpriteHeight`, and optional sprite offsets let you tune
  how it is framed in-world without changing the inventory icon. By default,
  custom placeable art uses a bottom-center pivot (`SpriteOffsetY = -SpriteHeight / 2`).
- Generated item and construction names resolve from `DisplayName`; generated
  item descriptions resolve from `Description` or a short loader fallback. Mods
  can still override those generated keys through `registry.Text`.

### What's deferred for later phases

- **Custom spells.** The engine has `Shared.Data.SpellDataBase` for spells; the modding API currently only points at existing spell IDs. Promoting a `SpellDefinition` into `SharedContentBootstrap` is the natural next step.
- **Custom `WeaponClass`** (defining a new skill / weapon-type combo) instead of picking a preset.
- **Custom auras.** `EntityAuraId` on `EquipmentDefinition` works only with auras that already exist in `ItemAuraDataBase` / aura databases.
- **`CastSpellTomeArgs`** (the optional advanced cast-event tuning on top of `SpellTomeArgs`) - `SpellTomeArgs` covers the common case; the extended struct is exposable when needed.

### Item Names and Descriptions

`ItemDefinition.NameTextId` and `DescriptionTextId` are the in-game translation
keys assigned to `ItemData`. `Name` and `Description` are the English fallback
text the loader returns for those keys. If you omit the text IDs, the loader
uses `Name` and `Description` as both the key and the fallback text.

You can also register arbitrary text entries:

```csharp
registry.Text.Register(new TextDefinition
{
    Id = "item.example.mod.example_item.name",
    Text = "Example Item"
});
```

### Item Icons

`ItemDefinition.Icon` is the icon ID the game will look up. You can reuse an
existing game icon, or register a new icon ID backed by a PNG beside your mod:

```csharp
registry.Icons.Register(new IconDefinition
{
    Id = "icon_example_item",
    TexturePath = Path.Combine(_modDirectory, "assets", "icons", "icon_example_item.png"),
    SpriteWidth = 32,
    SpriteHeight = 32
});

registry.Items.Register(new ItemDefinition
{
    Id = "material:mod:example_item",
    NameTextId = "item.example.mod.example_item.name",
    Name = "Example Item",
    DescriptionTextId = "item.example.mod.example_item.description",
    Description = "An item added by a mod.",
    Icon = "icon_example_item"
});
```

Store `_modDirectory` from `Initialize(IModContext context)` if you need paths
relative to your deployed mod folder. Item icon PNGs should be 32×32 pixels to
match the game's inventory icon size. Set `ReplaceExisting = true` on
`IconDefinition` only when intentionally replacing a vanilla icon. A simple
prefix like `icon_` keeps item icons easy to scan later when your mod also adds
weapon, armor, or UI icons.

### Skills

Skills appear in character progression and can be used by player classes as
their `BonusSkill`:

```csharp
registry.Skills.Register(new SkillDefinition
{
    Id = "skill:mod:masonry",
    NameTextId = "skill.example.mod.masonry.name",
    Name = "Masonry",
    DescriptionTextId = "skill.example.mod.masonry.description",
    Description = "Improves stonework and construction techniques by {0}.",
    Icon = "trowel",
    Value = 0.05f,
    ExperienceGainFactor = 1.0f
});
```

`Value` is the per-level effect shown in skill descriptions. Keep `{0}` in the
description if the game should format that value in UI text. `Icon` can reuse a
vanilla icon or a custom icon registered through `registry.Icons`.

### Skill Effects

Skill effects let modded skills affect real gameplay through explicit loader
hooks. The first supported effect is `ExperienceGainMultiplier`, which increases
experience gained for a target skill by `ValuePerLevel * source skill level`:

```csharp
registry.SkillEffects.Register(new SkillEffectDefinition
{
    SkillId = "skill:mod:masonry",
    Type = SkillEffectType.ExperienceGainMultiplier,
    TargetSkillId = "skill:construction",
    ValuePerLevel = 0.03f
});
```

With this example, each Masonry level adds 3% more construction experience.
`TargetSkillId` can point to a vanilla skill or another modded skill.

### Player Classes

The character creation screen uses `PlayerClassDefinition` entries. Registering
a class adds it to the same vanilla class list as Woodcutter, Miner, Mechanicus,
and the combat classes:

```csharp
registry.PlayerClasses.Register(new PlayerClassDefinition
{
    Id = "player_class:mod:mason",
    NameTextId = "player_class.example.mod.mason.name",
    Name = "Mason",
    BonusSkill = "skill:construction",
    SkillBonuses =
    [
        new SkillBonusDefinition("skill:woodcutting", 2)
    ],
    StartingClothes =
    [
        "armor:civilian:8",
        "armor:civilian:legs"
    ],
    StartingInventory =
    [
        new RecipeIngredient("placeable:workbench", 1),
        new RecipeIngredient("food:meat_small", 2)
    ],
    StartingFavourPoints = 1
});
```

`BonusSkill` is the main skill highlighted on the character creation panel.
`SkillBonuses` can set extra skill levels. Starting clothes are equipped first
when possible; starting inventory items are added afterward. `BonusSkill` can
point to either a vanilla skill such as `skill:construction`, `skill:mining`,
`skill:woodcutting`, `skill:swords`, `skill:spears`, `skill:shields`,
`skill:throwing`, or `skill:tomes`, or to a modded skill registered with
`registry.Skills`.

Mods can also include a `mod.json` beside the deployed DLL. The build script
copies the sample metadata files into `artifacts/mods/<AssemblyName>/`.

```json
{
  "id": "example.mod",
  "author": "Your Name",
  "description": "Adds an example item and recipe.",
  "homepage": "https://example.com",
  "dependencies": [
    "romestead.modloader.core"
  ]
}
```

`id` should match the `[ModManifest]` ID. The Mod Loader detail page shows
metadata when present.

## Notes

- **Steam can overwrite the patch.** Game updates and "Verify integrity of
  game files" will replace `Romestead.dll` (client) or `Server.dll` (server).
  Re-run `install.ps1` or `install-server.ps1` to reapply.
- `mods.json` controls loader-level mod disables. It is created automatically
  next to this README if missing:

  ```json
  {
    "disabledMods": [
      "romestead.new-items"
    ]
  }
  ```

  Disabled mods are skipped on the next launch. The client and the dedicated
  server each have their own `mods.json` inside their respective install's
  `romestead_modding/` directory — they're independent.
- Prefer existing registries and typed APIs before adding new Harmony patches.
  Use private Harmony only for one-off experiments, and promote a new seam into
  the loader once multiple mods need it.
- The dedicated server drops into an interactive `Console.ReadKey` menu
  (load / create / exit) after the loader finishes initializing. Running it
  with redirected stdin throws `Cannot read keys when console input has been
  redirected` — that's just the menu choking on the pipe, not a crash.
- `Launch-RomesteadModded.ps1` is a thin convenience wrapper that just runs
  `Romestead.exe` directly. Launching via Steam works identically.
- `diag-launch.ps1` runs the game with `COREHOST_TRACE=1` and captures
  stdout/stderr to `artifacts/diag/` — useful if the patched DLL fails to
  load and you need to see the runtime's assembly-resolution decisions.
- Build flags: `build.ps1` accepts `-Configuration Debug|Release` (default
  `Release`) and `-ServerOnly` (skips client-only projects so the build works
  on a server-only machine). `install-server.ps1` always passes `-ServerOnly`.
