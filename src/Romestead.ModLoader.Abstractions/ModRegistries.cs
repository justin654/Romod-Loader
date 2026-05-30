namespace Romestead.ModLoader;

/// <summary>
/// Process-wide singletons for the content registries and lifecycle event source.
/// Mods normally reach these through IModContext; the framework "core" mod
/// reads from them directly to commit pending content to the game.
/// </summary>
public static class ModRegistries
{
    public static IItemRegistry Items { get; } = new ItemRegistry();
    public static IRecipeRegistry Recipes { get; } = new RecipeRegistry();
    public static ITextRegistry Text { get; } = new TextRegistry();
    public static IIconRegistry Icons { get; } = new IconRegistry();
    public static ISkillRegistry Skills { get; } = new SkillRegistry();
    public static ISkillEffectRegistry SkillEffects { get; } = new SkillEffectRegistry();
    public static IPlayerClassRegistry PlayerClasses { get; } = new PlayerClassRegistry();
    public static IAggroTuningRegistry AggroTuning { get; } = new AggroTuningRegistry();
    public static IStatRegistry Stats { get; } = new StatRegistry();
    public static IValueOverrideRegistry ValueOverrides { get; } = new ValueOverrideRegistry();
    public static ICraftingStationRegistry CraftingStations { get; } = new CraftingStationRegistry();
    public static IMapRegistry Maps { get; } = new MapRegistry();
    public static IPlaceableRegistry Placeables { get; } = new PlaceableRegistry();
    public static IModUiRegistry Ui { get; } = new ModUiRegistry();
    public static IModCapabilityApi Capabilities { get; } = new ModCapabilityRegistry();
    public static IModOverlayRegistry Overlays { get; } = new ModOverlayRegistry();
    public static IModWindowRegistry Windows { get; } = new ModWindowRegistry();
    public static IModCraftingRegistry Crafting { get; } = new ModCraftingRegistry();
    public static IContentRegistry Content { get; } = new ContentRegistry(Items, Recipes, Text, Icons, Skills, SkillEffects, PlayerClasses, AggroTuning, Stats, ValueOverrides, CraftingStations, Maps, Placeables);
    public static LoadedModRegistry LoadedMods { get; } = new LoadedModRegistry();
    public static LoaderDiagnostics Diagnostics { get; } = new LoaderDiagnostics();
    public static ModLifecycle Lifecycle { get; } = new ModLifecycle();

    internal static void SetCapabilityState(string capabilityId, ModCapabilityState state, string summary)
    {
        if (Capabilities is ModCapabilityRegistry registry)
        {
            registry.SetState(capabilityId, state, summary);
        }
    }
}

public sealed class LoadedModRegistry
{
    private readonly List<LoadedModInfo> _mods = new();

    public IReadOnlyList<LoadedModInfo> Mods => _mods;

    public void Register(LoadedModInfo mod)
    {
        ArgumentNullException.ThrowIfNull(mod);
        _mods.Add(mod);
    }
}

public sealed class LoaderDiagnostics
{
    private readonly List<SkippedModInfo> _skippedMods = new();
    private readonly List<FailedModInfo> _failedMods = new();
    private readonly List<ModLoadErrorInfo> _errors = new();
    private readonly List<ModContentInfo> _content = new();
    private readonly List<ModMetadataInfo> _metadata = new();
    private readonly List<ContentDiagnosticInfo> _contentDiagnostics = new();
    private readonly HashSet<string> _contentDiagnosticKeys = new(StringComparer.Ordinal);
    private readonly List<PatchGroupInstallResult> _patchGroups = new();
    private readonly List<ModCapabilityStatusInfo> _capabilityStates = new();
    private readonly Dictionary<string, int> _capabilityIndexes = new(StringComparer.Ordinal);

    public string ConfigPath { get; private set; } = "";
    public string LogPath { get; private set; } = "";
    public IReadOnlyList<string> DisabledModIds { get; private set; } = [];
    public IReadOnlyList<SkippedModInfo> SkippedMods => _skippedMods;
    public IReadOnlyList<FailedModInfo> FailedMods => _failedMods;
    public IReadOnlyList<ModLoadErrorInfo> Errors => _errors;
    public IReadOnlyList<ModContentInfo> Content => _content;
    public IReadOnlyList<ModMetadataInfo> Metadata => _metadata;
    public IReadOnlyList<ContentDiagnosticInfo> ContentDiagnostics => _contentDiagnostics;
    public IReadOnlyList<PatchGroupInstallResult> PatchGroups => _patchGroups;
    public IReadOnlyList<ModCapabilityStatusInfo> CapabilityStates => _capabilityStates;
    public ModCompatibilityReport? LocalCompatibilityReport { get; private set; }

    public void SetConfig(string configPath, IReadOnlyList<string> disabledModIds)
    {
        ConfigPath = configPath;
        DisabledModIds = disabledModIds;
    }

    public void SetDisabledModIds(IReadOnlyList<string> disabledModIds)
    {
        DisabledModIds = disabledModIds;
    }

    public void SetLogPath(string logPath)
    {
        LogPath = logPath;
    }

    public void RegisterSkipped(SkippedModInfo mod)
    {
        ArgumentNullException.ThrowIfNull(mod);
        _skippedMods.Add(mod);
    }

    public void RegisterFailed(FailedModInfo mod)
    {
        ArgumentNullException.ThrowIfNull(mod);
        _failedMods.Add(mod);
    }

    public void RegisterError(ModLoadErrorInfo error)
    {
        ArgumentNullException.ThrowIfNull(error);
        _errors.Add(error);
    }

    public void RegisterContent(ModContentInfo content)
    {
        ArgumentNullException.ThrowIfNull(content);
        _content.Add(content);
    }

    public void RegisterMetadata(ModMetadataInfo metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        _metadata.Add(metadata);
    }

    public void RegisterContentDiagnostic(ContentDiagnosticInfo diagnostic)
    {
        ArgumentNullException.ThrowIfNull(diagnostic);
        var key = $"{diagnostic.ModId}|{diagnostic.ContentType}|{diagnostic.ContentId}|{diagnostic.Status}|{diagnostic.Detail}";
        if (_contentDiagnosticKeys.Add(key))
        {
            _contentDiagnostics.Add(diagnostic);
        }
    }

    public void RegisterPatchGroupResult(PatchGroupInstallResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        _patchGroups.Add(result);
    }

    public void SetCapabilityState(ModCapabilityStatusInfo status)
    {
        ArgumentNullException.ThrowIfNull(status);
        if (_capabilityIndexes.TryGetValue(status.Id, out var index))
        {
            _capabilityStates[index] = status;
            return;
        }

        _capabilityIndexes[status.Id] = _capabilityStates.Count;
        _capabilityStates.Add(status);
    }

    public void SetLocalCompatibilityReport(ModCompatibilityReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        LocalCompatibilityReport = report;
    }
}

internal sealed class ModCapabilityRegistry : IModCapabilityApi
{
    private readonly object _sync = new();
    private readonly List<ModCapabilityStatusInfo> _capabilities = new();
    private readonly Dictionary<string, int> _indexes = new(StringComparer.Ordinal);

    public IReadOnlyList<ModCapabilityStatusInfo> Capabilities
    {
        get
        {
            lock (_sync)
            {
                return _capabilities.ToArray();
            }
        }
    }

    public bool TryGetCapability(string capabilityId, out ModCapabilityStatusInfo capability)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(capabilityId);

        lock (_sync)
        {
            if (_indexes.TryGetValue(capabilityId, out var index))
            {
                capability = _capabilities[index];
                return true;
            }
        }

        capability = new ModCapabilityStatusInfo(capabilityId, ModCapabilityState.Unavailable, "Capability is not registered.");
        return false;
    }

    public bool IsAvailable(string capabilityId) =>
        TryGetCapability(capabilityId, out var capability) &&
        capability.State is ModCapabilityState.Available or ModCapabilityState.Degraded;

    internal void SetState(string capabilityId, ModCapabilityState state, string summary)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(capabilityId);
        summary ??= "";

        ModCapabilityStatusInfo status;
        lock (_sync)
        {
            status = new ModCapabilityStatusInfo(capabilityId, state, summary);
            if (_indexes.TryGetValue(capabilityId, out var index))
            {
                _capabilities[index] = status;
            }
            else
            {
                _indexes[capabilityId] = _capabilities.Count;
                _capabilities.Add(status);
            }
        }

        ModRegistries.Diagnostics.SetCapabilityState(status);
    }
}

internal sealed class ContentRegistry(
    IItemRegistry items,
    IRecipeRegistry recipes,
    ITextRegistry text,
    IIconRegistry icons,
    ISkillRegistry skills,
    ISkillEffectRegistry skillEffects,
    IPlayerClassRegistry playerClasses,
    IAggroTuningRegistry aggroTuning,
    IStatRegistry stats,
    IValueOverrideRegistry valueOverrides,
    ICraftingStationRegistry craftingStations,
    IMapRegistry maps,
    IPlaceableRegistry placeables) : IContentRegistry
{
    public IItemRegistry Items { get; } = items;
    public IRecipeRegistry Recipes { get; } = recipes;
    public ITextRegistry Text { get; } = text;
    public IIconRegistry Icons { get; } = icons;
    public ISkillRegistry Skills { get; } = skills;
    public ISkillEffectRegistry SkillEffects { get; } = skillEffects;
    public IPlayerClassRegistry PlayerClasses { get; } = playerClasses;
    public IAggroTuningRegistry AggroTuning { get; } = aggroTuning;
    public IStatRegistry Stats { get; } = stats;
    public IValueOverrideRegistry ValueOverrides { get; } = valueOverrides;
    public ICraftingStationRegistry CraftingStations { get; } = craftingStations;
    public IMapRegistry Maps { get; } = maps;
    public IPlaceableRegistry Placeables { get; } = placeables;
}

internal sealed class MapRegistry : IMapRegistry
{
    private readonly Dictionary<string, string> _aliases = new(StringComparer.Ordinal);
    private readonly Dictionary<string, MapFileRegistration> _files = new(StringComparer.Ordinal);
    private readonly HashSet<string> _observedMapLoads = new(StringComparer.Ordinal);
    private readonly List<string> _observedMapLoadOrder = new();

    public IReadOnlyDictionary<string, string> Aliases => _aliases;

    public IReadOnlyDictionary<string, MapFileRegistration> Files => _files;

    public IReadOnlyCollection<string> ObservedMapLoads => _observedMapLoadOrder;

    internal bool RecordObservedMapLoad(string mapId)
    {
        var normalized = MapKeyNormalizer.Normalize(mapId);
        if (string.IsNullOrWhiteSpace(normalized) || !_observedMapLoads.Add(normalized))
        {
            return false;
        }

        _observedMapLoadOrder.Add(normalized);
        return true;
    }

    public void RegisterAlias(string originalMapId, string replacementMapId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(originalMapId);
        ArgumentException.ThrowIfNullOrWhiteSpace(replacementMapId);

        var original = MapKeyNormalizer.Normalize(originalMapId);
        var replacement = MapKeyNormalizer.Normalize(replacementMapId);
        if (_aliases.ContainsKey(original))
        {
            return;
        }

        _aliases[original] = replacement;
    }

    public bool TryResolveAlias(string mapId, out string replacementMapId)
    {
        replacementMapId = mapId;
        if (string.IsNullOrWhiteSpace(mapId))
        {
            return false;
        }

        return _aliases.TryGetValue(MapKeyNormalizer.Normalize(mapId), out replacementMapId!);
    }

    public void RegisterFile(string mapId, string sourcePath, MapFileFormat format)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mapId);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);

        var normalizedMapId = MapKeyNormalizer.Normalize(mapId);
        if (!MapFileCacheKey.TryValidateNormalizedMapId(normalizedMapId, out var mapIdError))
        {
            throw new ArgumentException(mapIdError, nameof(mapId));
        }

        var extension = format switch
        {
            MapFileFormat.Cmx => ".cmx",
            MapFileFormat.Tmx => ".tmx",
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, null),
        };

        if (!MapFileCacheKey.TryBuildCacheRelativePath(normalizedMapId, extension, out _, out var cachePathError))
        {
            throw new ArgumentException(cachePathError, nameof(mapId));
        }

        var fullSourcePath = Path.GetFullPath(sourcePath);
        if (!string.Equals(Path.GetExtension(fullSourcePath), extension, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                $"source path extension must be {extension} for format {format}",
                nameof(sourcePath));
        }

        if (_files.ContainsKey(normalizedMapId))
        {
            return;
        }

        _files[normalizedMapId] = new MapFileRegistration(normalizedMapId, fullSourcePath, format);
    }

    public bool TryResolveFile(string mapId, out string sourcePath, out MapFileFormat format)
    {
        sourcePath = mapId;
        format = default;
        if (!TryResolveFile(mapId, out var registration))
        {
            return false;
        }

        sourcePath = registration.SourcePath;
        format = registration.Format;
        return true;
    }

    public bool TryResolveFile(string mapId, out MapFileRegistration registration)
    {
        registration = null!;
        if (string.IsNullOrWhiteSpace(mapId))
        {
            return false;
        }

        return _files.TryGetValue(MapKeyNormalizer.Normalize(mapId), out registration!);
    }
}

internal sealed class StatRegistry : IStatRegistry
{
    private readonly List<StatDefinition> _pending = new();
    private readonly HashSet<string> _ids = new(StringComparer.Ordinal);

    public IReadOnlyList<StatDefinition> Pending => _pending;

    public void Register(StatDefinition stat)
    {
        ArgumentNullException.ThrowIfNull(stat);
        if (_ids.Add(stat.Id))
        {
            _pending.Add(stat);
        }
    }
}

internal sealed class ItemRegistry : IItemRegistry
{
    private readonly List<ItemDefinition> _pending = new();
    private readonly HashSet<string> _ids = new(StringComparer.Ordinal);

    public IReadOnlyList<ItemDefinition> Pending => _pending;

    public void Register(ItemDefinition item)
    {
        ArgumentNullException.ThrowIfNull(item);
        ValidateEquipmentDisplay(item);
        if (_ids.Add(item.Id))
        {
            _pending.Add(item);
        }
    }

    private static void ValidateEquipmentDisplay(ItemDefinition item)
    {
        var display = item.Equipment?.Display;
        if (display is null)
        {
            ValidateHeldVfx(item);
            return;
        }

        if (display.Fragments.Count == 0)
        {
            throw new ArgumentException($"Item '{item.Id}' custom equipment display must define at least one fragment.", nameof(item));
        }

        foreach (var fragment in display.Fragments)
        {
            if (string.IsNullOrWhiteSpace(fragment.SkinName))
            {
                throw new ArgumentException($"Item '{item.Id}' equipment display fragment is missing SkinName.", nameof(item));
            }

            if (fragment.TexturePath is not null)
            {
                if (string.IsNullOrWhiteSpace(fragment.TexturePath))
                {
                    throw new ArgumentException($"Item '{item.Id}' equipment display texture path is blank.", nameof(item));
                }

                if (fragment.SpriteWidth <= 0 || fragment.SpriteHeight <= 0)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(item),
                        $"Item '{item.Id}' equipment display frame size must be greater than zero.");
                }
            }
        }

        ValidateHeldVfx(item);
    }

    private static void ValidateHeldVfx(ItemDefinition item)
    {
        var heldVfx = item.Equipment?.HeldVfx;
        if (heldVfx is null)
        {
            return;
        }

        if (heldVfx.ParticleEmitterId is not null && string.IsNullOrWhiteSpace(heldVfx.ParticleEmitterId))
        {
            throw new ArgumentException($"Item '{item.Id}' held VFX particle emitter id is blank.", nameof(item));
        }

        if (heldVfx.ParticleLineLength < 0f || heldVfx.ParticleLineWidth < 0f || heldVfx.ParticleLineHeight < 0f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(item),
                $"Item '{item.Id}' held VFX particle line dimensions cannot be negative.");
        }

        if (heldVfx.ParticleSpawnFrequency is <= 0f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(item),
                $"Item '{item.Id}' held VFX particle spawn frequency must be greater than zero.");
        }

        if (heldVfx.ParticleAmountSpawned is <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(item),
                $"Item '{item.Id}' held VFX particle amount must be greater than zero.");
        }

        if (heldVfx.LightRadius < 0f || heldVfx.LightDuration <= 0f || heldVfx.LightFlickerAmount < 0f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(item),
                $"Item '{item.Id}' held VFX light values are invalid.");
        }
    }
}

internal sealed class RecipeRegistry : IRecipeRegistry
{
    private readonly List<RecipeDefinition> _pending = new();
    private readonly HashSet<string> _ids = new(StringComparer.Ordinal);

    public IReadOnlyList<RecipeDefinition> Pending => _pending;

    public void Register(RecipeDefinition recipe)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        if (_ids.Add(recipe.ResultItemId))
        {
            _pending.Add(recipe);
        }
    }
}

internal sealed class CraftingStationRegistry : ICraftingStationRegistry
{
    private readonly List<CraftingStationDefinition> _pending = new();
    private readonly HashSet<string> _ids = new(StringComparer.Ordinal);

    public IReadOnlyList<CraftingStationDefinition> Pending => _pending;

    public void Register(CraftingStationDefinition station)
    {
        ArgumentNullException.ThrowIfNull(station);
        if (string.IsNullOrWhiteSpace(station.Id))
        {
            throw new ArgumentException("Crafting station id is required.", nameof(station));
        }

        if (_ids.Add(station.Id))
        {
            _pending.Add(station);
        }
    }
}

internal sealed class PlaceableRegistry : IPlaceableRegistry
{
    private readonly List<ModPlaceableStation> _pending = new();
    private readonly HashSet<string> _ids = new(StringComparer.Ordinal);

    public IReadOnlyList<ModPlaceableStation> Pending => _pending;

    public void Register(ModPlaceableStation placeable)
    {
        ArgumentNullException.ThrowIfNull(placeable);
        if (string.IsNullOrWhiteSpace(placeable.Id))
        {
            throw new ArgumentException("Placeable id is required.", nameof(placeable));
        }

        if (string.IsNullOrWhiteSpace(placeable.StationId))
        {
            throw new ArgumentException("Placeable station id is required.", nameof(placeable));
        }

        if (string.IsNullOrWhiteSpace(placeable.DisplayName))
        {
            throw new ArgumentException("Placeable display name is required.", nameof(placeable));
        }

        if (string.IsNullOrWhiteSpace(placeable.IconId))
        {
            throw new ArgumentException("Placeable icon id is required.", nameof(placeable));
        }

        if (string.IsNullOrWhiteSpace(placeable.TexturePath))
        {
            throw new ArgumentException("Placeable texture path is required.", nameof(placeable));
        }

        if (placeable.SpriteWidth < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(placeable), "Placeable sprite width cannot be negative.");
        }

        if (placeable.SpriteHeight < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(placeable), "Placeable sprite height cannot be negative.");
        }

        if (_ids.Add(placeable.Id))
        {
            _pending.Add(placeable);
        }
    }
}

internal sealed class TextRegistry : ITextRegistry
{
    private readonly List<TextDefinition> _pending = new();
    private readonly HashSet<string> _ids = new(StringComparer.Ordinal);

    public IReadOnlyList<TextDefinition> Pending => _pending;

    public void Register(TextDefinition text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (_ids.Add(text.Id))
        {
            _pending.Add(text);
        }
    }
}

internal sealed class IconRegistry : IIconRegistry
{
    private readonly List<IconDefinition> _pending = new();
    private readonly HashSet<string> _ids = new(StringComparer.Ordinal);

    public IReadOnlyList<IconDefinition> Pending => _pending;

    public void Register(IconDefinition icon)
    {
        ArgumentNullException.ThrowIfNull(icon);
        if (_ids.Add(icon.Id))
        {
            _pending.Add(icon);
        }
    }
}

internal sealed class PlayerClassRegistry : IPlayerClassRegistry
{
    private readonly List<PlayerClassDefinition> _pending = new();
    private readonly HashSet<string> _ids = new(StringComparer.Ordinal);

    public IReadOnlyList<PlayerClassDefinition> Pending => _pending;

    public void Register(PlayerClassDefinition playerClass)
    {
        ArgumentNullException.ThrowIfNull(playerClass);
        if (_ids.Add(playerClass.Id))
        {
            _pending.Add(playerClass);
        }
    }
}

internal sealed class SkillRegistry : ISkillRegistry
{
    private readonly List<SkillDefinition> _pending = new();
    private readonly HashSet<string> _ids = new(StringComparer.Ordinal);

    public IReadOnlyList<SkillDefinition> Pending => _pending;

    public void Register(SkillDefinition skill)
    {
        ArgumentNullException.ThrowIfNull(skill);
        if (_ids.Add(skill.Id))
        {
            _pending.Add(skill);
        }
    }
}

internal sealed class SkillEffectRegistry : ISkillEffectRegistry
{
    private readonly List<SkillEffectDefinition> _pending = new();

    public IReadOnlyList<SkillEffectDefinition> Pending => _pending;

    public void Register(SkillEffectDefinition effect)
    {
        ArgumentNullException.ThrowIfNull(effect);
        _pending.Add(effect);
    }
}

internal sealed class AggroTuningRegistry : IAggroTuningRegistry
{
    private readonly List<AggroTuningDefinition> _pending = new();
    private readonly HashSet<string> _ids = new(StringComparer.Ordinal);

    public IReadOnlyList<AggroTuningDefinition> Pending => _pending;

    public void Register(AggroTuningDefinition tuning)
    {
        ArgumentNullException.ThrowIfNull(tuning);
        if (string.IsNullOrWhiteSpace(tuning.Id))
        {
            throw new ArgumentException("Aggro tuning id is required.", nameof(tuning));
        }

        if (_ids.Add(tuning.Id))
        {
            _pending.Add(tuning);
        }
    }
}

internal sealed class ValueOverrideRegistry : IValueOverrideRegistry
{
    private readonly List<ValueOverrideDefinition> _pending = new();

    public IReadOnlyList<ValueOverrideDefinition> Pending => _pending;

    public void Register(ValueOverrideDefinition valueOverride)
    {
        ArgumentNullException.ThrowIfNull(valueOverride);
        _pending.Add(valueOverride);
    }
}

internal sealed class ModUiRegistry : IModUiRegistry
{
    private readonly List<ModSettingsPageDefinition> _pages = new();
    private readonly HashSet<string> _pageIds = new(StringComparer.Ordinal);
    private readonly List<ModSidebarEntryDefinition> _sidebarEntries = new();
    private readonly HashSet<string> _sidebarEntryIds = new(StringComparer.Ordinal);

    public IReadOnlyList<ModSettingsPageDefinition> Pages => _pages;
    public IReadOnlyList<ModSidebarEntryDefinition> SidebarEntries => _sidebarEntries;

    public void RegisterSettingsPage(ModSettingsPageDefinition page)
    {
        ArgumentNullException.ThrowIfNull(page);
        if (string.IsNullOrWhiteSpace(page.Id))
        {
            throw new ArgumentException("Settings page id is required.", nameof(page));
        }

        if (_pageIds.Add(page.Id))
        {
            _pages.Add(page);
            _pages.Sort(static (left, right) => CompareByOrderThenTitle(left.Order, left.Title, right.Order, right.Title));
            return;
        }

        throw new InvalidOperationException($"A settings page with id '{page.Id}' is already registered.");
    }

    public void RegisterSidebarEntry(ModSidebarEntryDefinition entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (string.IsNullOrWhiteSpace(entry.Id))
        {
            throw new ArgumentException("Sidebar entry id is required.", nameof(entry));
        }

        if (string.IsNullOrWhiteSpace(entry.TargetPageId))
        {
            throw new ArgumentException("Sidebar entry target page id is required.", nameof(entry));
        }

        if (_sidebarEntryIds.Add(entry.Id))
        {
            _sidebarEntries.Add(entry);
            _sidebarEntries.Sort(static (left, right) => CompareByOrderThenTitle(left.Order, left.Title, right.Order, right.Title));
            return;
        }

        throw new InvalidOperationException($"A sidebar entry with id '{entry.Id}' is already registered.");
    }

    private static int CompareByOrderThenTitle(int leftOrder, string leftTitle, int rightOrder, string rightTitle)
    {
        var orderCompare = leftOrder.CompareTo(rightOrder);
        return orderCompare != 0
            ? orderCompare
            : StringComparer.OrdinalIgnoreCase.Compare(leftTitle, rightTitle);
    }
}

/// <summary>
/// Concrete implementation of IModLifecycle. Public so the core mod can raise events.
/// </summary>
public sealed class ModLifecycle : IModLifecycle, ISceneApi
{
    private bool _isGameReady;
    private SceneInfo? _currentScene;

    public bool IsGameReady =>
        ModRegistries.Capabilities.IsAvailable(ModCapabilityId.Lifecycle) &&
        _isGameReady;

    public SceneInfo? CurrentScene =>
        ModRegistries.Capabilities.IsAvailable(ModCapabilityId.Scene)
            ? _currentScene
            : null;

    public event Action? GameReady;
    public event Action<SceneInfo>? SceneChanged;

    public void RaiseGameReady()
    {
        if (!ModRegistries.Capabilities.IsAvailable(ModCapabilityId.Lifecycle))
        {
            return;
        }

        _isGameReady = true;
        GameReady?.Invoke();
    }

    public void RaiseSceneChanged(SceneInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);
        if (!ModRegistries.Capabilities.IsAvailable(ModCapabilityId.Scene))
        {
            return;
        }

        _currentScene = info;
        SceneChanged?.Invoke(info);
    }
}
