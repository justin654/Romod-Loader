using HarmonyLib;
using Candide.GameModels;
using Candide.GameModels.Managers;
using Candide.Multiplayer;
using Candide.World;
using CandideServer.Models.Worlds;
using Microsoft.Xna.Framework;
using Romestead.ModLoader;
using Romestead.StartupHook;
using Shared.Entity;
using Shared.Models.Items;

namespace Romestead.ModLoader.ClientCore;

/// <summary>
/// Client-side host for the built-in <b>Debug Wand</b> (item id
/// <see cref="SharedContentBootstrap.DebugWandItemId"/>). Using the wand opens a
/// draggable in-game debug menu with developer shortcuts: teleport between maps,
/// full heal, god-mode stats, and noclip/fly.
///
/// The wand is a normal usable item; we intercept
/// <see cref="ItemInstanceManager.TryUseItem"/> for its id so the menu opens
/// instead of casting the placeholder spell it carries. The wand is auto-granted
/// to the local player on the first gameplay frame so it's always available.
///
/// <para><b>Teleport model.</b> The game has two distinct map systems:</para>
/// <list type="bullet">
/// <item>The <i>exterior</i> overworld, swapped in-place by
/// <c>WorldManager.ChangeWorldMap</c> (only valid for exterior region maps).</item>
/// <item>Generic worlds (interiors / dungeons / POIs) loaded from a .tmx file by
/// <c>OldInteriorWorldHandler.Load</c>.</item>
/// </list>
/// We therefore teleport in two tiers: if a world for the chosen map is already
/// registered in <c>GameState.Worlds</c> we switch to it authoritatively via
/// <c>WorldManager.MovePlayerToWorldAndPosition</c>; otherwise we load the .tmx
/// file directly and make it the active world. The map list is read straight from
/// the game's <c>Content/maps</c> folder.
/// </summary>
internal static class DebugWandHost
{
    private const string WindowId = "romestead.debug.menu";

    private static IModWindowHandle? _window;
    private static bool _grantAttempted;
    private static bool _godMode;
    private static bool _flying;
    private static IModLogger? _log;

    // Teleport UI state.
    private static string? _teleportFilter;
    private static string? _teleportStatus;
    private static List<string>? _diskMaps;
    private const int MaxMapButtons = 40;

    // God-mode + fly originals so toggling off restores vanilla behaviour.
    private static readonly Dictionary<string, float> _savedStats = new(StringComparer.Ordinal);
    private static float? _savedGravityScale;

    // Stats boosted by god mode and the absolute value we slam them to.
    private static readonly (string Id, float Value)[] GodStats =
    {
        ("Health", 100000f),
        ("Strength", 9999f),
        ("Armor", 9999f),
        ("CritChance", 1f),
        ("EnergyRegeneration", 1000f),
    };

    /// <summary>
    /// Grants the wand to the local player exactly once per process, after the
    /// inventory exists. Called every frame from the standard-mode update pump;
    /// self-guards so it only does work until the grant resolves.
    /// </summary>
    internal static void EnsureGranted(IModLogger log)
    {
        if (_grantAttempted)
        {
            return;
        }

        if (!GameState.TryGetLocalPlayerInventory(out var inventory) || inventory?.Model is null)
        {
            return; // gameplay/inventory not ready yet; try again next frame.
        }

        _grantAttempted = true;
        _log = log;

        try
        {
            var (existingSlot, _) = inventory.Model.FindFirstSlotIndexWithBaseId(SharedContentBootstrap.DebugWandItemId, false);
            if (existingSlot >= 0)
            {
                log.Info("[debug-wand] already present in inventory; not granting again.");
                return;
            }

            SimpleInventoryManager.AddItemCheat(SharedContentBootstrap.DebugWandItemId, 1, string.Empty);
            log.Info("[debug-wand] granted debug wand to the local player inventory.");
        }
        catch (Exception ex)
        {
            log.Error("[debug-wand] failed to grant the debug wand.", ex);
        }
    }

    /// <summary>Opens (or refreshes) the debug menu window.</summary>
    internal static void OpenMenu(IModLogger log)
    {
        _log = log;
        try
        {
            _window = ModRegistries.Windows.Open(new ModWindowDefinition
            {
                Id = WindowId,
                Title = "Debug Menu",
                Style = ModWindowStyle.Dark,
                Width = 420,
                Sections = BuildSections()
            });
            log.Info("[debug-wand] opened debug menu.");
        }
        catch (Exception ex)
        {
            log.Error("[debug-wand] failed to open the debug menu.", ex);
        }
    }

    private static void Refresh() => _window?.Update(BuildSections());

    /// <summary>Surfaces a one-line status message in the teleport section and refreshes the window.</summary>
    private static void SetStatus(string? message)
    {
        _teleportStatus = message;
        Refresh();
    }

    private static IReadOnlyList<ModSection> BuildSections()
    {
        var player = new ModSection
        {
            Title = "Player",
            Rows =
            {
                new ModButtonRow { Label = "Full Heal (HP + Energy + Mana)", OnClick = _ => HealAll() },
                new ModButtonRow { Label = _godMode ? "God Mode: ON (click to disable)" : "God Mode: OFF", OnClick = _ => ToggleGodMode() },
                new ModButtonRow { Label = _flying ? "Fly / Noclip: ON (click to disable)" : "Fly / Noclip: OFF", OnClick = _ => ToggleFly() },
                new ModButtonRow { Label = "Dump World Info (to log)", OnClick = _ => DumpWorldInfo() },
            }
        };

        return new[] { player, BuildActiveWorldsSection(), BuildTeleportSection() };
    }

    /// <summary>
    /// Worlds already registered this session (exterior + any interior/dungeon the
    /// player has entered). These are the most reliable teleport targets because
    /// the server already knows about them.
    /// </summary>
    private static ModSection BuildActiveWorldsSection()
    {
        var section = new ModSection { Title = "Active Worlds (reliable)" };
        var worlds = GameState.Worlds;
        if (worlds is null || worlds.Count == 0)
        {
            section.Rows.Add(new ModLabelRow { Text = "No registered worlds yet." });
            return section;
        }

        foreach (var world in worlds.Values.OrderBy(w => w.Name ?? w.MapName ?? string.Empty, StringComparer.Ordinal))
        {
            var id = world.Id;
            var label = !string.IsNullOrEmpty(world.Name)
                ? world.Name
                : (!string.IsNullOrEmpty(world.MapName) ? world.MapName : id.ToString());
            section.Rows.Add(new ModButtonRow { Label = $"Go: {label}", OnClick = _ => TeleportToWorld(id) });
        }

        return section;
    }

    /// <summary>
    /// Every .tmx/.cmx under <c>Content/maps</c>, filterable. Loading an arbitrary
    /// map file is best-effort (client-side load) and may not fully sync — use the
    /// Active Worlds section for guaranteed teleports.
    /// </summary>
    private static ModSection BuildTeleportSection()
    {
        var section = new ModSection { Title = "Teleport to Map File" };
        var current = GameState.CurrentWorld;
        section.Rows.Add(new ModInfoRow { Label = "Current map", Value = current?.MapName ?? "(unknown)" });

        section.Rows.Add(new ModTextInputRow
        {
            Label = "Filter",
            Value = _teleportFilter ?? string.Empty,
            Placeholder = "e.g. cave, town, plains",
            OnChanged = (_, text) => _teleportFilter = text
        });
        section.Rows.Add(new ModButtonBarRow
        {
            Buttons = new[]
            {
                new ModBarButton { Label = "Search", OnClick = _ => Refresh() },
                new ModBarButton { Label = "Clear", OnClick = _ => { _teleportFilter = null; Refresh(); } }
            }
        });

        var maps = GetDiskMaps();
        var filter = _teleportFilter?.Trim();
        var matches = string.IsNullOrEmpty(filter)
            ? maps
            : maps.Where(m => m.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();

        section.Rows.Add(new ModLabelRow
        {
            Text = string.IsNullOrEmpty(filter)
                ? $"{maps.Count} maps on disk. Type a filter and Search to narrow."
                : $"Showing {Math.Min(MaxMapButtons, matches.Count)} of {matches.Count} match(es)."
        });

        if (!string.IsNullOrEmpty(_teleportStatus))
        {
            section.Rows.Add(new ModLabelRow { Text = _teleportStatus });
        }

        foreach (var map in matches.Take(MaxMapButtons))
        {
            var target = map; // capture per-iteration for the closure
            section.Rows.Add(new ModButtonRow { Label = target, OnClick = _ => Teleport(target) });
        }

        return section;
    }

    private static EntityWrapper? LocalEntity() => GameState.LocalPlayer?.Character?.Entity;

    private static void HealAll()
    {
        var entity = LocalEntity();
        if (entity is null)
        {
            _log?.Warn("[debug-wand] heal: no local player entity.");
            return;
        }

        try { entity.Health = entity.MaxHealth; } catch (Exception ex) { _log?.Info($"[debug-wand] heal HP failed: {ex.Message}"); }

        try
        {
            var maxEnergy = entity.Stats.Get("Energy");
            entity.Energy = maxEnergy > 0f ? maxEnergy : 100f;
        }
        catch (Exception ex) { _log?.Info($"[debug-wand] heal energy failed: {ex.Message}"); }

        try { ManaTracker.SetCurrent(entity, "Mana", ManaTracker.GetMax(entity, "Mana")); }
        catch (Exception ex) { _log?.Info($"[debug-wand] heal mana failed: {ex.Message}"); }

        _log?.Info("[debug-wand] full heal applied.");
    }

    private static void ToggleGodMode()
    {
        var entity = LocalEntity();
        if (entity is null)
        {
            _log?.Warn("[debug-wand] god mode: no local player entity.");
            return;
        }

        _godMode = !_godMode;
        try
        {
            if (_godMode)
            {
                _savedStats.Clear();
                foreach (var (id, value) in GodStats)
                {
                    _savedStats[id] = entity.Stats.Get(id);
                    entity.Stats.SetBaseStat(id, value);
                }

                entity.Health = entity.MaxHealth;
            }
            else
            {
                foreach (var (id, saved) in _savedStats)
                {
                    entity.Stats.SetBaseStat(id, saved);
                }
            }

            _log?.Info($"[debug-wand] god mode -> {_godMode}.");
        }
        catch (Exception ex)
        {
            _log?.Error("[debug-wand] god mode toggle failed.", ex);
        }

        Refresh();
    }

    private static void ToggleFly()
    {
        var entity = LocalEntity();
        if (entity is null)
        {
            _log?.Warn("[debug-wand] fly: no local player entity.");
            return;
        }

        _flying = !_flying;
        try
        {
            entity.NoTerrainCollision = _flying;
            entity.NoEntityCollision = _flying;

            if (_flying)
            {
                _savedGravityScale ??= entity.GravityScale;
                entity.GravityScale = 0f;
            }
            else if (_savedGravityScale is { } restore)
            {
                entity.GravityScale = restore;
            }

            _log?.Info($"[debug-wand] fly/noclip -> {_flying}.");
        }
        catch (Exception ex)
        {
            _log?.Error("[debug-wand] fly toggle failed.", ex);
        }

        Refresh();
    }

    /// <summary>Switch to a world already registered in <see cref="GameState.Worlds"/>.</summary>
    private static void TeleportToWorld(Guid worldId)
    {
        _log?.Info($"[debug-wand] teleport to registered world {worldId}.");
        try
        {
            var spawn = GameState.TryGetWorld(worldId, out var world) ? GetSpawnFor(world) : Vector3.Zero;
            var ok = WorldManager.MovePlayerToWorldAndPosition(worldId, spawn);
            _log?.Info($"[debug-wand] MovePlayerToWorldAndPosition({worldId}, {spawn}) -> {ok}.");
            _window?.Close();
        }
        catch (Exception ex)
        {
            _log?.Error($"[debug-wand] teleport to world {worldId} failed.", ex);
        }
    }

    /// <summary>Teleport by map file id (path under Content/maps, no extension).</summary>
    private static void Teleport(string mapId)
    {
        _log?.Info($"[debug-wand] teleport requested -> '{mapId}'.");
        try
        {
            // Tier 1: a world for this map already exists -> authoritative switch.
            var worlds = GameState.Worlds;
            if (worlds is not null)
            {
                foreach (var world in worlds.Values)
                {
                    if (!MapNameMatches(world.MapName, mapId))
                    {
                        continue;
                    }

                    _log?.Info($"[debug-wand] matched registered world '{world.Name}' ({world.Id}); switching.");
                    var ok = WorldManager.MovePlayerToWorldAndPosition(world.Id, GetSpawnFor(world));
                    _log?.Info($"[debug-wand] MovePlayerToWorldAndPosition -> {ok}.");
                    _window?.Close();
                    return;
                }
            }

            // Tier 2: not registered -> load the .tmx client-side and make it active.
            // First validate the map can actually be read. WorldLoader.LoadWorld only
            // reads the .cmx/.tmx from disk and returns null when the data is missing;
            // it NEVER disconnects. The real loader (OldInteriorWorldHandler.Load) raises
            // an unrecoverable "missing data files" kick when the map can't resolve, so we
            // must never call it for a map that fails this probe (e.g. dev/dark_world).
            if (!CanLoadMapFile(mapId))
            {
                _log?.Warn($"[debug-wand] map '{mapId}' has missing/unreadable data files; aborting to avoid a disconnect.");
                SetStatus($"Can't teleport: '{mapId}' is missing data files (skipped to avoid a kick).");
                return;
            }

            _log?.Info("[debug-wand] map probe ok; loading directly (OldInteriorWorldHandler.Load).");
            var loaded = OldInteriorWorldHandler.Load(mapId, false);
            if (loaded is null)
            {
                _log?.Warn($"[debug-wand] Load returned null for '{mapId}'.");
                SetStatus($"Can't teleport: '{mapId}' failed to load.");
                return;
            }

            OldInteriorWorldHandler.SetMainWorldAndClearActive(loaded);

            var startPosition = loaded.SharedWorld?.PlayerStartPosition ?? Vector3.Zero;
            _log?.Info($"[debug-wand] loaded '{loaded.Name}'; placing player at {startPosition}.");

            try { PlayerManager.SpawnLocalPlayer(startPosition); }
            catch (Exception ex) { _log?.Info($"[debug-wand] SpawnLocalPlayer failed: {ex.Message}"); }

            var entity = LocalEntity();
            if (entity is not null)
            {
                try { entity.Position = startPosition; }
                catch (Exception ex) { _log?.Info($"[debug-wand] set position failed: {ex.Message}"); }
            }

            _teleportStatus = null;
            _window?.Close();
        }
        catch (Exception ex)
        {
            _log?.Error($"[debug-wand] teleport to '{mapId}' failed.", ex);
        }
    }

    /// <summary>
    /// Non-destructive probe: returns true only if the map's data files can be read.
    /// <see cref="WorldLoader.LoadWorld"/> just reads the .cmx/.tmx and returns null on
    /// failure (it never disconnects), so it's safe to call before the real loader that
    /// would otherwise kick the player with a "missing data files" error.
    /// </summary>
    private static bool CanLoadMapFile(string mapId)
    {
        try
        {
            return WorldLoader.LoadWorld(mapId) is not null;
        }
        catch (Exception ex)
        {
            _log?.Info($"[debug-wand] map probe threw for '{mapId}': {ex.Message}");
            return false;
        }
    }

    private static Vector3 GetSpawnFor(WorldModel? world)
    {
        // WorldModel has no start position; fall back to the player's current spot
        // (keeps them near where they were if the coordinate space lines up) or origin.
        var entity = LocalEntity();
        if (entity is not null)
        {
            try { return entity.Position; } catch { /* fall through */ }
        }

        return Vector3.Zero;
    }

    private static bool MapNameMatches(string? worldMapName, string mapId)
    {
        if (string.IsNullOrEmpty(worldMapName))
        {
            return false;
        }

        var normalized = worldMapName.Replace('\\', '/').Trim();
        var lastSlash = normalized.LastIndexOf('/');
        var lastDot = normalized.LastIndexOf('.');
        if (lastDot > lastSlash)
        {
            normalized = normalized[..lastDot];
        }

        return string.Equals(normalized, mapId, StringComparison.OrdinalIgnoreCase);
    }

    private static void DumpWorldInfo()
    {
        try
        {
            var current = GameState.CurrentWorld;
            _log?.Info($"[debug-wand] === World Info === current: name='{current?.Name}' map='{current?.MapName}' id={current?.Id} type={current?.Type} interior={current?.Interior}");
            _log?.Info($"[debug-wand] OutsideWorldId={GameState.OutsideWorldId} LocalPlayerId={GameState.LocalPlayerId}");

            var entity = LocalEntity();
            try { _log?.Info($"[debug-wand] player position={entity?.Position}"); }
            catch (Exception ex) { _log?.Info($"[debug-wand] player position unavailable: {ex.Message}"); }

            var worlds = GameState.Worlds;
            _log?.Info($"[debug-wand] registered worlds: {(worlds?.Count ?? 0)}");
            if (worlds is not null)
            {
                foreach (var world in worlds.Values)
                {
                    _log?.Info($"[debug-wand]   id={world.Id} name='{world.Name}' map='{world.MapName}' type={world.Type} interior={world.Interior} parent={world.ParentWorldId}");
                }
            }

            _log?.Info($"[debug-wand] maps on disk: {GetDiskMaps().Count}");
        }
        catch (Exception ex)
        {
            _log?.Error("[debug-wand] dump world info failed.", ex);
        }
    }

    /// <summary>Enumerates every .tmx/.cmx under Content/maps as relative ids (cached).</summary>
    private static IReadOnlyList<string> GetDiskMaps()
    {
        if (_diskMaps is not null)
        {
            return _diskMaps;
        }

        var results = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var root = ResolveMapsRoot();
            if (root is not null && Directory.Exists(root))
            {
                foreach (var file in Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories))
                {
                    var ext = Path.GetExtension(file);
                    if (!ext.Equals(".tmx", StringComparison.OrdinalIgnoreCase) &&
                        !ext.Equals(".cmx", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
                    relative = relative[..^ext.Length]; // strip extension
                    results.Add(relative);
                }
            }
            else
            {
                _log?.Warn($"[debug-wand] maps root not found (looked at '{root}').");
            }
        }
        catch (Exception ex)
        {
            _log?.Error("[debug-wand] failed to enumerate maps.", ex);
        }

        _diskMaps = results.ToList();
        _log?.Info($"[debug-wand] discovered {_diskMaps.Count} map file(s) on disk.");
        return _diskMaps;
    }

    private static string? ResolveMapsRoot()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "Content", "maps"),
            Path.Combine(Directory.GetCurrentDirectory(), "Content", "maps")
        };

        return candidates.FirstOrDefault(Directory.Exists) ?? candidates[0];
    }
}

/// <summary>
/// Intercepts use of the debug wand so it opens the debug menu instead of
/// casting its placeholder spell. Runs before the vanilla body, so the wand's
/// spell is never actually cast.
/// </summary>
[HarmonyPatch(typeof(ItemInstanceManager), nameof(ItemInstanceManager.TryUseItem))]
internal static class ItemInstanceManagerTryUseItemPatch
{
    private static bool Prefix(ItemInstanceModel item, ref bool __result)
    {
        if (item?.Data?.Id != SharedContentBootstrap.DebugWandItemId)
        {
            return true; // not the wand; run vanilla.
        }

        if (CoreState.Logger is { } log)
        {
            DebugWandHost.OpenMenu(log);
        }

        __result = true;
        return false; // skip the vanilla spell cast.
    }
}
