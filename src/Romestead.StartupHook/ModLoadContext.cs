using Romestead.ModLoader;

namespace Romestead.StartupHook;

internal sealed class ModLoadContext : IModContext
{
    private readonly IModApiResolver _loggingApis;
    private readonly IContentRegistry _content;
    private readonly IModUiRegistry _ui;

    public ModLoadContext(
        string gameRoot,
        string modRoot,
        string modDirectory,
        IModLogger logger,
        IModApiResolver apis,
        IContentRegistry content)
    {
        GameRoot = gameRoot;
        ModRoot = modRoot;
        ModDirectory = modDirectory;
        Logger = logger;
        _content = content ?? throw new ArgumentNullException(nameof(content));
        _loggingApis = new LoggingModApiResolver(apis, logger);
        if (!apis.TryGet<IModUiRegistry>(out var ui) || ui is null)
        {
            throw new InvalidOperationException("Mod API 'Romestead.ModLoader.IModUiRegistry' is not registered.");
        }

        _ui = new ContextualModUiRegistry(
            ui,
            new ModSettingsBuildContext
            {
                GameRoot = gameRoot,
                ModRoot = modRoot,
                ModDirectory = modDirectory,
                Logger = logger,
                Apis = _loggingApis
            });
    }

    public string GameRoot { get; }
    public string ModRoot { get; }
    public string ModDirectory { get; }
    public IModLogger Logger { get; }
    public IModApiResolver Apis => _loggingApis;

    // Content registries delegate to the per-mod duplicate-checking wrapper so
    // registering during Initialize is duplicate-checked and attributed exactly
    // like registering during RegisterContent — there is one content path.
    public IItemRegistry Items => _content.Items;
    public IRecipeRegistry Recipes => _content.Recipes;
    public ITextRegistry Text => _content.Text;
    public IIconRegistry Icons => _content.Icons;
    public ISkillRegistry Skills => _content.Skills;
    public ISkillEffectRegistry SkillEffects => _content.SkillEffects;
    public IPlayerClassRegistry PlayerClasses => _content.PlayerClasses;
    public IStatRegistry Stats => _content.Stats;
    public IValueOverrideRegistry ValueOverrides => _content.ValueOverrides;
    public ICraftingStationRegistry CraftingStations => _content.CraftingStations;
    public IPlaceableRegistry Placeables => _content.Placeables;
    public IAggroTuningRegistry AggroTuning => _content.AggroTuning;
    public IMapRegistry Maps => _content.Maps;
    public IModUiRegistry Ui => _ui;
    public IModOverlayRegistry Overlays => GetRequired<IModOverlayRegistry>();
    public IModWindowRegistry Windows => GetRequired<IModWindowRegistry>();
    public IModCraftingRegistry Crafting => GetRequired<IModCraftingRegistry>();
    public IModLifecycle Lifecycle => GetRequired<IModLifecycle>();

    private TApi GetRequired<TApi>() where TApi : class
    {
        if (Apis.TryGet<TApi>(out var api) && api is not null)
        {
            return api;
        }

        throw new InvalidOperationException($"Mod API '{typeof(TApi).FullName}' is not registered.");
    }

    private sealed class LoggingModApiResolver(IModApiResolver inner, IModLogger logger) : IModApiResolver
    {
        private readonly HashSet<Type> _resolvedTypes = new();

        public bool TryGet<TApi>(out TApi? api) where TApi : class
        {
            var resolved = inner.TryGet<TApi>(out api);
            if (resolved && api is not null && _resolvedTypes.Add(typeof(TApi)))
            {
                logger.Info($"Resolved {typeof(TApi).Name}.");
            }

            return resolved;
        }
    }

    private sealed class ContextualModUiRegistry(IModUiRegistry inner, ModSettingsBuildContext buildContext) : IModUiRegistry
    {
        public IReadOnlyList<ModSettingsPageDefinition> Pages => inner.Pages;
        public IReadOnlyList<ModSidebarEntryDefinition> SidebarEntries => inner.SidebarEntries;

        public void RegisterSettingsPage(ModSettingsPageDefinition page)
        {
            ArgumentNullException.ThrowIfNull(page);

            inner.RegisterSettingsPage(new ModSettingsPageDefinition
            {
                Id = page.Id,
                Title = page.Title,
                Icon = page.Icon,
                Order = page.Order,
                Build = _ => page.Build(buildContext)
            });
        }

        public void RegisterSidebarEntry(ModSidebarEntryDefinition entry)
        {
            ArgumentNullException.ThrowIfNull(entry);

            inner.RegisterSidebarEntry(new ModSidebarEntryDefinition
            {
                Id = entry.Id,
                Title = entry.Title,
                Icon = entry.Icon,
                Order = entry.Order,
                TargetPageId = entry.TargetPageId
            });
        }
    }
}
