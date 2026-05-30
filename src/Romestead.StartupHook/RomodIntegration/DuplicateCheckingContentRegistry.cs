using Romestead.ModLoader;

namespace Romestead.StartupHook.RomodIntegration;

/// <summary>
/// Wraps the real content registries with a layer that consults a
/// <see cref="DuplicateContentIdChecker"/> before each registration.
/// Conflicts are logged as errors and silently dropped so a misbehaving
/// mod can't corrupt the rest of the load.
///
/// We deliberately do NOT throw — the existing loader is permissive about
/// individual content failures, and we want bad content from one mod to
/// surface as a clear error in the log without taking down the others.
/// </summary>
internal sealed class DuplicateCheckingContentRegistry : IContentRegistry
{
    private readonly IContentRegistry _inner;
    private readonly DuplicateContentIdChecker _checker;
    private readonly string _modId;
    private readonly string _sourceLabel;
    private readonly IModLogger _log;

    public DuplicateCheckingContentRegistry(
        IContentRegistry inner,
        DuplicateContentIdChecker checker,
        string modId,
        string sourceLabel,
        IModLogger log)
    {
        _inner = inner;
        _checker = checker;
        _modId = modId;
        _sourceLabel = sourceLabel;
        _log = log;

        Items = new ItemsProxy(this);
        Recipes = new RecipesProxy(this);
        Text = inner.Text;
        Icons = new IconsProxy(this);
        Skills = new SkillsProxy(this);
        SkillEffects = inner.SkillEffects;
        PlayerClasses = new PlayerClassesProxy(this);
        AggroTuning = inner.AggroTuning;
        Stats = new StatsProxy(this);
        CraftingStations = new CraftingStationsProxy(this);
        Maps = new MapsProxy(this);
        Placeables = new PlaceablesProxy(this);
        ValueOverrides = inner.ValueOverrides;
    }

    public IItemRegistry Items { get; }
    public IRecipeRegistry Recipes { get; }
    public ITextRegistry Text { get; }
    public IIconRegistry Icons { get; }
    public ISkillRegistry Skills { get; }
    public ISkillEffectRegistry SkillEffects { get; }
    public IPlayerClassRegistry PlayerClasses { get; }
    public IAggroTuningRegistry AggroTuning { get; }
    public IStatRegistry Stats { get; }
    public ICraftingStationRegistry CraftingStations { get; }
    public IMapRegistry Maps { get; }
    public IPlaceableRegistry Placeables { get; }
    public IValueOverrideRegistry ValueOverrides { get; }

    private bool Claim(string kind, string id, string? perFileLabel, bool allowReplaceExisting)
    {
        var source = new DuplicateContentIdChecker.Source(_modId, perFileLabel ?? _sourceLabel);
        if (_checker.TryClaim(kind, id, source, allowReplaceExisting, out var conflict))
        {
            return true;
        }

        _log.Error(DuplicateContentIdChecker.FormatConflictMessage(conflict!));
        ModRegistries.Diagnostics.RegisterError(new ModLoadErrorInfo(
            source.FilePath,
            DuplicateContentIdChecker.FormatConflictMessage(conflict!)));
        return false;
    }

    private sealed class ItemsProxy(DuplicateCheckingContentRegistry parent) : IItemRegistry
    {
        public IReadOnlyList<ItemDefinition> Pending => parent._inner.Items.Pending;
        public void Register(ItemDefinition item)
        {
            ArgumentNullException.ThrowIfNull(item);
            if (parent.Claim("item", item.Id, null, allowReplaceExisting: false))
            {
                parent._inner.Items.Register(item);
            }
        }
    }

    private sealed class RecipesProxy(DuplicateCheckingContentRegistry parent) : IRecipeRegistry
    {
        public IReadOnlyList<RecipeDefinition> Pending => parent._inner.Recipes.Pending;
        public void Register(RecipeDefinition recipe)
        {
            ArgumentNullException.ThrowIfNull(recipe);
            if (parent.Claim("recipe", recipe.ResultItemId, null, allowReplaceExisting: false))
            {
                parent._inner.Recipes.Register(recipe);
            }
        }
    }

    private sealed class IconsProxy(DuplicateCheckingContentRegistry parent) : IIconRegistry
    {
        public IReadOnlyList<IconDefinition> Pending => parent._inner.Icons.Pending;
        public void Register(IconDefinition icon)
        {
            ArgumentNullException.ThrowIfNull(icon);
            if (parent.Claim("icon", icon.Id, null, allowReplaceExisting: icon.ReplaceExisting))
            {
                parent._inner.Icons.Register(icon);
            }
        }
    }

    private sealed class SkillsProxy(DuplicateCheckingContentRegistry parent) : ISkillRegistry
    {
        public IReadOnlyList<SkillDefinition> Pending => parent._inner.Skills.Pending;
        public void Register(SkillDefinition skill)
        {
            ArgumentNullException.ThrowIfNull(skill);
            if (parent.Claim("skill", skill.Id, null, allowReplaceExisting: false))
            {
                parent._inner.Skills.Register(skill);
            }
        }
    }

    private sealed class PlayerClassesProxy(DuplicateCheckingContentRegistry parent) : IPlayerClassRegistry
    {
        public IReadOnlyList<PlayerClassDefinition> Pending => parent._inner.PlayerClasses.Pending;
        public void Register(PlayerClassDefinition playerClass)
        {
            ArgumentNullException.ThrowIfNull(playerClass);
            if (parent.Claim("player-class", playerClass.Id, null, allowReplaceExisting: false))
            {
                parent._inner.PlayerClasses.Register(playerClass);
            }
        }
    }

    private sealed class StatsProxy(DuplicateCheckingContentRegistry parent) : IStatRegistry
    {
        public IReadOnlyList<StatDefinition> Pending => parent._inner.Stats.Pending;
        public void Register(StatDefinition stat)
        {
            ArgumentNullException.ThrowIfNull(stat);
            if (parent.Claim("stat", stat.Id, null, allowReplaceExisting: false))
            {
                parent._inner.Stats.Register(stat);
            }
        }
    }

    private sealed class CraftingStationsProxy(DuplicateCheckingContentRegistry parent) : ICraftingStationRegistry
    {
        public IReadOnlyList<CraftingStationDefinition> Pending => parent._inner.CraftingStations.Pending;
        public void Register(CraftingStationDefinition station)
        {
            ArgumentNullException.ThrowIfNull(station);
            if (parent.Claim("crafting-station", station.Id, null, allowReplaceExisting: false))
            {
                parent._inner.CraftingStations.Register(station);
            }
        }
    }

    private sealed class PlaceablesProxy(DuplicateCheckingContentRegistry parent) : IPlaceableRegistry
    {
        public IReadOnlyList<ModPlaceableStation> Pending => parent._inner.Placeables.Pending;
        public void Register(ModPlaceableStation placeable)
        {
            ArgumentNullException.ThrowIfNull(placeable);
            if (parent.Claim("placeable", placeable.Id, null, allowReplaceExisting: false))
            {
                parent._inner.Placeables.Register(placeable);
            }
        }
    }

    private sealed class MapsProxy(DuplicateCheckingContentRegistry parent) : IMapRegistry
    {
        public IReadOnlyDictionary<string, string> Aliases => parent._inner.Maps.Aliases;

        public IReadOnlyDictionary<string, MapFileRegistration> Files => parent._inner.Maps.Files;

        public IReadOnlyCollection<string> ObservedMapLoads => parent._inner.Maps.ObservedMapLoads;

        public void RegisterAlias(string originalMapId, string replacementMapId)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(originalMapId);
            ArgumentException.ThrowIfNullOrWhiteSpace(replacementMapId);
            var original = MapKeyNormalizer.Normalize(originalMapId);
            if (parent.Claim("map-alias", original, null, allowReplaceExisting: false))
            {
                parent._inner.Maps.RegisterAlias(originalMapId, replacementMapId);
            }
        }

        public bool TryResolveAlias(string mapId, out string replacementMapId) =>
            parent._inner.Maps.TryResolveAlias(mapId, out replacementMapId);

        public void RegisterFile(string mapId, string sourcePath, MapFileFormat format)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(mapId);
            ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
            var normalized = MapKeyNormalizer.Normalize(mapId);
            if (parent.Claim("map-file", normalized, null, allowReplaceExisting: false))
            {
                parent._inner.Maps.RegisterFile(mapId, sourcePath, format);
            }
        }

        public bool TryResolveFile(string mapId, out string sourcePath, out MapFileFormat format) =>
            parent._inner.Maps.TryResolveFile(mapId, out sourcePath, out format);

        public bool TryResolveFile(string mapId, out MapFileRegistration registration) =>
            parent._inner.Maps.TryResolveFile(mapId, out registration);
    }
}
