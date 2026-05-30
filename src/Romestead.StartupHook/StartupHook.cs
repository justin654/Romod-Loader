using System.Reflection;
using System.Runtime.Loader;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Text.Json.Serialization;
using Romestead.ModLoader;
using Romestead.RomodFormat.Package;
using Romestead.StartupHook;
using Romestead.StartupHook.RomodIntegration;

public static class StartupHook
{
    private static readonly object InitLock = new();
    private static bool _initialized;

    public static void Initialize()
    {
        lock (InitLock)
        {
            if (_initialized)
            {
                return;
            }

            _initialized = true;
        }

        var bootLogger = new ModLogger("romestead-loader");

        try
        {
            var gameRoot = AppContext.BaseDirectory;
            var host = DetectHost();
            var modRoot = ResolveModRoot(gameRoot);
            var modsDirectory = ResolveModsDirectory(modRoot);
            var hookDirectory = Path.GetDirectoryName(typeof(StartupHook).Assembly.Location)!;
            var logPath = Path.Combine(modRoot, "artifacts", "loader", "romestead-loader.log");

            ModLogger.SetLogFile(logPath);
            ModRegistries.Diagnostics.SetLogPath(logPath);
            var apis = CreateApiRegistry(host.Kind);
            var config = LoadConfig(modRoot, bootLogger);
            LogRegisteredApis(bootLogger, apis, host);

            bootLogger.Info($"Detected host kind: {host.Kind}");
            bootLogger.Info($"Entry assembly: {host.EntryAssemblyName}");
            bootLogger.Info($"Entry point: {host.EntryPoint}");
            bootLogger.Info($"Game root: {gameRoot}");
            bootLogger.Info($"Mod root: {modRoot}");
            bootLogger.Info($"Mods directory: {modsDirectory}");
            WarnOnFrameworkMismatch(bootLogger);

            RegisterAssemblyResolver(gameRoot, hookDirectory, modsDirectory);
            MultiplayerCompatibilityCoordinator.Install(host.Kind, bootLogger);
            SharedContentBootstrap.Install(host.Kind, bootLogger);
            if (host.Kind == HostKind.Client)
            {
                LoadClientCore(hookDirectory, apis, bootLogger);
            }

            if (!Directory.Exists(modsDirectory))
            {
                bootLogger.Warn("Mods directory does not exist. Nothing to load.");
                return;
            }

            LoadAllDiscoveredMods(modsDirectory, gameRoot, modRoot, config, bootLogger, apis, host.Kind);

            LogLoadedMods(bootLogger, host);
            LogSkippedMods(bootLogger, host);
            LogFailedMods(bootLogger, host);
            BuildAndLogCompatibilityReport(bootLogger, host);
            LogPatchAndCapabilitySummary(bootLogger, host);
        }
        catch (Exception ex)
        {
            bootLogger.Error("Startup hook failed.", ex);
            ModRegistries.Diagnostics.RegisterError(new ModLoadErrorInfo("Startup hook", ex.Message));
        }
    }

    private static void LoadClientCore(string hookDirectory, IModApiResolver apis, ModLogger bootLogger)
    {
        var clientCorePath = Path.Combine(hookDirectory, "Romestead.ModLoader.ClientCore.dll");
        if (!File.Exists(clientCorePath))
        {
            bootLogger.Warn($"Romestead.ModLoader.ClientCore.dll not found at {clientCorePath}; client-side patches will not run.");
            return;
        }

        try
        {
            var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(clientCorePath);
            var clientCoreType = assembly.GetType("Romestead.ModLoader.ClientCore.ClientCore")
                ?? throw new InvalidOperationException("Type Romestead.ModLoader.ClientCore.ClientCore was not found in the loaded assembly.");
            var installMethod = clientCoreType.GetMethod("Install", BindingFlags.Public | BindingFlags.Static)
                ?? throw new InvalidOperationException("ClientCore.Install(IModLogger, IModApiResolver) was not found.");
            installMethod.Invoke(null, [bootLogger, apis]);
        }
        catch (Exception ex)
        {
            bootLogger.Error("Failed to load Romestead.ModLoader.ClientCore.", ex);
            ModRegistries.Diagnostics.RegisterError(new ModLoadErrorInfo("Romestead.ModLoader.ClientCore", ex.Message));
        }
    }

    private static ModLoaderConfig LoadConfig(string modRoot, ModLogger bootLogger)
    {
        var configPath = Path.Combine(modRoot, "mods.json");

        try
        {
            if (!File.Exists(configPath))
            {
                var defaultConfig = new ModLoaderConfig();
                File.WriteAllText(configPath, JsonSerializer.Serialize(defaultConfig, ModLoaderConfig.JsonOptions));
                ModRegistries.Diagnostics.SetConfig(configPath, defaultConfig.DisabledMods, defaultConfig.EnforceMultiplayerCompatibility);
                bootLogger.Info($"Created default mod config: {configPath}");
                return defaultConfig;
            }

            var config = JsonSerializer.Deserialize<ModLoaderConfig>(
                File.ReadAllText(configPath),
                ModLoaderConfig.JsonOptions) ?? new ModLoaderConfig();
            ModRegistries.Diagnostics.SetConfig(configPath, config.DisabledMods, config.EnforceMultiplayerCompatibility);
            bootLogger.Info($"Loaded mod config: {configPath}");
            return config;
        }
        catch (Exception ex)
        {
            bootLogger.Error($"Failed to read mod config {configPath}. Continuing with all mods enabled.", ex);
            ModRegistries.Diagnostics.SetConfig(configPath, [], false);
            ModRegistries.Diagnostics.RegisterError(new ModLoadErrorInfo("mods.json", ex.Message));
            return new ModLoaderConfig();
        }
    }

    private static string ResolveModRoot(string gameRoot)
    {
        var configured = Environment.GetEnvironmentVariable("ROMESTEAD_MOD_ROOT");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return Path.GetFullPath(configured);
        }

        return Path.Combine(gameRoot, "romestead_modding");
    }

    private static string ResolveModsDirectory(string modRoot)
    {
        var configured = Environment.GetEnvironmentVariable("ROMESTEAD_MOD_DLL_DIR");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return Path.GetFullPath(configured);
        }

        return Path.Combine(modRoot, "artifacts", "mods");
    }

    private static void RegisterAssemblyResolver(string gameRoot, string hookDirectory, string modsDirectory)
    {
        var searchDirectories = new List<string>
        {
            hookDirectory,
            gameRoot
        };

        if (Directory.Exists(modsDirectory))
        {
            searchDirectories.Add(modsDirectory);
            searchDirectories.AddRange(Directory.GetDirectories(modsDirectory, "*", SearchOption.TopDirectoryOnly));
        }

        var pathsBySimpleName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var directory in searchDirectories.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(directory))
            {
                continue;
            }

            foreach (var dllPath in Directory.GetFiles(directory, "*.dll", SearchOption.TopDirectoryOnly))
            {
                var simpleName = Path.GetFileNameWithoutExtension(dllPath);
                if (!pathsBySimpleName.ContainsKey(simpleName))
                {
                    pathsBySimpleName[simpleName] = dllPath;
                }
            }
        }

        AssemblyLoadContext.Default.Resolving += (_, assemblyName) =>
        {
            var loaded = AssemblyLoadContext.Default.Assemblies.FirstOrDefault(assembly =>
                string.Equals(assembly.GetName().Name, assemblyName.Name, StringComparison.OrdinalIgnoreCase));
            if (loaded is not null)
            {
                return loaded;
            }

            if (assemblyName.Name is null)
            {
                return null;
            }

            if (!pathsBySimpleName.TryGetValue(assemblyName.Name, out var candidatePath))
            {
                return null;
            }

            return File.Exists(candidatePath)
                ? AssemblyLoadContext.Default.LoadFromAssemblyPath(candidatePath)
                : null;
        };
    }

    private static HostInfo DetectHost()
    {
        var entryAssembly = Assembly.GetEntryAssembly();
        var entryAssemblyName = entryAssembly?.GetName().Name ?? "<unknown>";
        var entryPoint = entryAssembly?.EntryPoint?.DeclaringType?.FullName is { Length: > 0 } typeName
            ? $"{typeName}.{entryAssembly!.EntryPoint!.Name}"
            : "<unknown>";

        var hostKind =
            string.Equals(entryAssemblyName, "Server", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(entryPoint, "Server.Program.Main", StringComparison.Ordinal)
                ? HostKind.DedicatedServer
                : HostKind.Client;

        return new HostInfo(hostKind, entryAssemblyName, entryPoint);
    }

    private static ModApiRegistry CreateApiRegistry(HostKind hostKind)
    {
        var apis = new ModApiRegistry();
        var lifecycle = ModRegistries.Lifecycle;
        InitializeCapabilityStates(hostKind);

        apis.Register<IItemRegistry>(ModRegistries.Items);
        apis.Register<IRecipeRegistry>(ModRegistries.Recipes);
        apis.Register<ITextRegistry>(ModRegistries.Text);
        apis.Register<IIconRegistry>(ModRegistries.Icons);
        apis.Register<ISkillRegistry>(ModRegistries.Skills);
        apis.Register<ISkillEffectRegistry>(ModRegistries.SkillEffects);
        apis.Register<IPlayerClassRegistry>(ModRegistries.PlayerClasses);
        apis.Register<IAggroTuningRegistry>(ModRegistries.AggroTuning);
        apis.Register<IStatRegistry>(ModRegistries.Stats);
        apis.Register<IValueOverrideRegistry>(ModRegistries.ValueOverrides);
        apis.Register<ICraftingStationRegistry>(ModRegistries.CraftingStations);
        apis.Register<IModUiRegistry>(ModRegistries.Ui);
        apis.Register<IContentRegistry>(ModRegistries.Content);
        apis.Register<IModCapabilityApi>(ModRegistries.Capabilities);
        apis.Register<IMultiplayerApi>(new MultiplayerApi());

        if (hostKind == HostKind.Client)
        {
            apis.Register<IModLifecycle>(lifecycle);
            apis.Register<ISceneApi>(lifecycle);
            apis.Register<IWorldMapApi>(new WorldMapApi());
            apis.Register<IModOverlayRegistry>(ModRegistries.Overlays);
            apis.Register<IModWindowRegistry>(ModRegistries.Windows);
            apis.Register<IModCraftingRegistry>(ModRegistries.Crafting);
        }

        return apis;
    }

    private static void InitializeCapabilityStates(HostKind hostKind)
    {
        var clientSummary = hostKind == HostKind.Client
            ? "Pending client patch installation."
            : "Client-only API on dedicated server.";
        var clientState = hostKind == HostKind.Client
            ? ModCapabilityState.Unavailable
            : ModCapabilityState.Unavailable;

        ModRegistries.SetCapabilityState(ModCapabilityId.Lifecycle, clientState, clientSummary);
        ModRegistries.SetCapabilityState(ModCapabilityId.Scene, clientState, clientSummary);
        ModRegistries.SetCapabilityState(ModCapabilityId.WorldMap, clientState, clientSummary);
        ModRegistries.SetCapabilityState(ModCapabilityId.Overlays, clientState, clientSummary);
        ModRegistries.SetCapabilityState(ModCapabilityId.Windows, clientState, clientSummary);
        ModRegistries.SetCapabilityState(ModCapabilityId.CraftingUi, clientState, clientSummary);
    }

    private static void LogRegisteredApis(ModLogger bootLogger, ModApiRegistry apis, HostInfo host)
    {
        var lines = apis.GetRegisteredApiNames()
            .Select(name => $"- {name}");
        bootLogger.Info($"[modloader] [{host.Kind}] Registered APIs:{Environment.NewLine}{string.Join(Environment.NewLine, lines)}");
    }

    private static void LogLoadedMods(ModLogger bootLogger, HostInfo host)
    {
        if (ModRegistries.LoadedMods.Mods.Count == 0)
        {
            bootLogger.Info($"[modloader] [{host.Kind}] Loaded mods: <none>");
            return;
        }

        var lines = ModRegistries.LoadedMods.Mods
            .Select(mod => $"- {mod.Id} v{mod.Version} SyncMode={mod.SyncMode}");
        bootLogger.Info($"[modloader] [{host.Kind}] Loaded mods:{Environment.NewLine}{string.Join(Environment.NewLine, lines)}");
    }

    private static void LogSkippedMods(ModLogger bootLogger, HostInfo host)
    {
        foreach (var mod in ModRegistries.Diagnostics.SkippedMods)
        {
            bootLogger.Warn($"[modloader] [{host.Kind}] Skipped mod {mod.Id} v{mod.Version} SyncMode={mod.SyncMode} Reason={mod.Reason}");
        }
    }

    private static void LogFailedMods(ModLogger bootLogger, HostInfo host)
    {
        foreach (var mod in ModRegistries.Diagnostics.FailedMods)
        {
            bootLogger.Error($"[modloader] [{host.Kind}] Failed mod {mod.Id} v{mod.Version} SyncMode={mod.SyncMode} Reason={mod.Reason}");
        }
    }

    private static void BuildAndLogCompatibilityReport(ModLogger bootLogger, HostInfo host)
    {
        var report = BuildLocalCompatibilityReport();
        ModRegistries.Diagnostics.SetLocalCompatibilityReport(report);

        var lines = report.Entries
            .Select(entry => $"- {entry.Id} v{entry.Version} SyncMode={entry.SyncMode} LoadState={entry.LoadState} Present={entry.Present}");
        bootLogger.Info($"[modloader] [{host.Kind}] Local compatibility report:{Environment.NewLine}{string.Join(Environment.NewLine, lines)}");

        var strictEntries = report.Entries
            .Where(entry => entry.SyncMode is not MultiplayerSyncMode.ClientOnly)
            .ToArray();
        if (strictEntries.Length == 0)
        {
            bootLogger.Info($"[modloader] [{host.Kind}] Strict-sync relevant mods: <none>");
            return;
        }

        var strictLines = strictEntries
            .Select(entry => $"- {entry.Id} v{entry.Version} SyncMode={entry.SyncMode} LoadState={entry.LoadState}");
        bootLogger.Info($"[modloader] [{host.Kind}] Strict-sync relevant mods:{Environment.NewLine}{string.Join(Environment.NewLine, strictLines)}");
    }

    private static void LogPatchAndCapabilitySummary(ModLogger bootLogger, HostInfo host)
    {
        if (ModRegistries.Diagnostics.PatchGroups.Count == 0)
        {
            bootLogger.Info($"[modloader] [{host.Kind}] Patch groups: <none>");
        }
        else
        {
            var patchLines = ModRegistries.Diagnostics.PatchGroups
                .Select(group => $"- {group.Id} Host={group.HostKind} Success={group.Success} Message={group.Message}");
            bootLogger.Info($"[modloader] [{host.Kind}] Patch groups:{Environment.NewLine}{string.Join(Environment.NewLine, patchLines)}");
        }

        if (ModRegistries.Diagnostics.CapabilityStates.Count == 0)
        {
            bootLogger.Info($"[modloader] [{host.Kind}] Capabilities: <none>");
            return;
        }

        var capabilityLines = ModRegistries.Diagnostics.CapabilityStates
            .Select(state => $"- {state.Id} State={state.State} Summary={state.Summary}");
        bootLogger.Info($"[modloader] [{host.Kind}] Capabilities:{Environment.NewLine}{string.Join(Environment.NewLine, capabilityLines)}");
    }

    private static void WarnOnFrameworkMismatch(ModLogger bootLogger)
    {
        var hostFramework = Assembly.GetEntryAssembly()?.GetCustomAttribute<TargetFrameworkAttribute>()?.FrameworkName;
        var loaderFramework = typeof(StartupHook).Assembly.GetCustomAttribute<TargetFrameworkAttribute>()?.FrameworkName;
        if (string.IsNullOrWhiteSpace(hostFramework) || string.IsNullOrWhiteSpace(loaderFramework))
        {
            return;
        }

        if (string.Equals(hostFramework, loaderFramework, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        bootLogger.Warn(
            $"[modloader] Target framework mismatch detected. Host='{hostFramework}' Loader='{loaderFramework}'. Rebuild/reinstall the loader against the current game runtime.");
    }

    private static ModCompatibilityReport BuildLocalCompatibilityReport()
    {
        var entries = new List<ModCompatibilityEntry>();
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var mod in ModRegistries.LoadedMods.Mods.OrderBy(mod => mod.Id, StringComparer.OrdinalIgnoreCase))
        {
            if (ids.Add(mod.Id))
            {
                entries.Add(new ModCompatibilityEntry(
                    mod.Id,
                    mod.Name,
                    mod.Version,
                    mod.SyncMode,
                    true,
                    ModCompatibilityLoadState.Loaded,
                    null));
            }
        }

        foreach (var mod in ModRegistries.Diagnostics.SkippedMods.OrderBy(mod => mod.Id, StringComparer.OrdinalIgnoreCase))
        {
            if (ids.Add(mod.Id))
            {
                entries.Add(new ModCompatibilityEntry(
                    mod.Id,
                    mod.Name,
                    mod.Version,
                    mod.SyncMode,
                    true,
                    ModCompatibilityLoadState.Skipped,
                    mod.Reason));
            }
        }

        foreach (var mod in ModRegistries.Diagnostics.FailedMods.OrderBy(mod => mod.Id, StringComparer.OrdinalIgnoreCase))
        {
            if (ids.Add(mod.Id))
            {
                entries.Add(new ModCompatibilityEntry(
                    mod.Id,
                    mod.Name,
                    mod.Version,
                    mod.SyncMode,
                    true,
                    ModCompatibilityLoadState.Failed,
                    mod.Reason));
            }
        }

        foreach (var modId in ModRegistries.Diagnostics.DisabledModIds
            .Where(modId => !ids.Contains(modId))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(modId => modId, StringComparer.OrdinalIgnoreCase))
        {
            entries.Add(new ModCompatibilityEntry(
                modId,
                modId,
                "<unknown>",
                MultiplayerSyncMode.RequiredOnClient,
                false,
                ModCompatibilityLoadState.Missing,
                "Configured in mods.json but no matching mod was found on disk."));
        }

        return new ModCompatibilityReport(entries);
    }

    private static IEnumerable<string> DiscoverModEntryAssemblies(string modsDirectory)
    {
        foreach (var directory in Directory.GetDirectories(modsDirectory, "*", SearchOption.TopDirectoryOnly))
        {
            var assemblyName = Path.GetFileName(directory);
            var assemblyPath = Path.Combine(directory, $"{assemblyName}.dll");
            if (File.Exists(assemblyPath))
            {
                yield return assemblyPath;
            }
        }

        foreach (var assemblyPath in Directory.GetFiles(modsDirectory, "*.dll", SearchOption.TopDirectoryOnly))
        {
            yield return assemblyPath;
        }
    }

    /// <summary>
    /// Unified discovery + load pipeline that handles both C# DLL mods and
    /// .romod packages. Builds a single ordered list (dependencies-first,
    /// id-alphabetical for ties), then dispatches each entry to its kind's
    /// loader. A shared <see cref="DuplicateContentIdChecker"/> threads
    /// through both paths so cross-source duplicate ids surface as errors.
    /// </summary>
    private static void LoadAllDiscoveredMods(
        string modsDirectory,
        string gameRoot,
        string modRoot,
        ModLoaderConfig config,
        ModLogger bootLogger,
        IModApiResolver apis,
        HostKind hostKind)
    {
        var dllMods = DiscoverDllMods(modsDirectory, bootLogger);
        var romodResult = RomodPackageDiscovery.Discover(modsDirectory, bootLogger);

        foreach (var (path, reason) in romodResult.FailedPackages)
        {
            var fallbackId = Path.GetFileNameWithoutExtension(path);
            ModRegistries.Diagnostics.RegisterError(new ModLoadErrorInfo(path, reason));
            ModRegistries.Diagnostics.RegisterFailed(new FailedModInfo(
                fallbackId, fallbackId, "<unknown>", path,
                MultiplayerSyncMode.RequiredOnClient, reason));
        }

        var allDiscovered = new List<DiscoveredMod>(dllMods.Count + romodResult.Discovered.Count);
        allDiscovered.AddRange(dllMods);
        allDiscovered.AddRange(romodResult.Discovered);

        // Detect cross-source duplicate mod ids BEFORE sorting so the sort sees a clean set.
        var firstById = new Dictionary<string, DiscoveredMod>(StringComparer.OrdinalIgnoreCase);
        var unique = new List<DiscoveredMod>(allDiscovered.Count);
        foreach (var mod in allDiscovered)
        {
            if (firstById.TryAdd(mod.Id, mod))
            {
                unique.Add(mod);
                continue;
            }

            var first = firstById[mod.Id];
            var msg = $"Duplicate mod id '{mod.Id}'. Defined by {first.SourcePath} and {mod.SourcePath}. " +
                      "Loading the first; skipping the second.";
            bootLogger.Error(msg);
            ModRegistries.Diagnostics.RegisterError(new ModLoadErrorInfo(mod.SourcePath, msg));
            ModRegistries.Diagnostics.RegisterFailed(new FailedModInfo(
                mod.Id, mod.Name, mod.Version, mod.SourcePath, mod.SyncMode, msg));
        }

        var sort = ModDependencySorter.Sort(unique, bootLogger);

        var allDepIssues = sort.MissingDependencyErrors
            .Concat(sort.VersionMismatchErrors)
            .Concat(sort.CycleErrors)
            .ToArray();
        foreach (var msg in allDepIssues)
        {
            bootLogger.Error(msg);
            ModRegistries.Diagnostics.RegisterError(new ModLoadErrorInfo("dependency-resolution", msg));
        }

        var loadedIds = new HashSet<string>(sort.Order.Select(m => m.Id), StringComparer.OrdinalIgnoreCase);
        var skippedForDeps = unique.Where(m => !loadedIds.Contains(m.Id)).ToArray();
        foreach (var mod in skippedForDeps)
        {
            // dependency-skipped: still record as failed so the diagnostics surface it
            var reason = "Skipped due to unresolved dependency or dependency cycle (see preceding errors).";
            bootLogger.Error($"{mod.Id}: {reason}");
            ModRegistries.Diagnostics.RegisterFailed(new FailedModInfo(
                mod.Id, mod.Name, mod.Version, mod.SourcePath, mod.SyncMode, reason));
        }

        bootLogger.Info(
            $"Mod load order: " +
            (sort.Order.Count == 0
                ? "<none>"
                : string.Join(" -> ", sort.Order.Select(m => $"{m.Id}({(m.Kind == DiscoveredModKind.Romod ? "romod" : "dll")})"))));

        var assetCacheRoot = Path.Combine(modRoot, "artifacts", "cache", "romods");
        var duplicateChecker = new DuplicateContentIdChecker();

        foreach (var mod in sort.Order)
        {
            switch (mod)
            {
                case DiscoveredDllMod dll:
                    LoadAndInitializeMod(dll.SourcePath, gameRoot, modRoot, config, bootLogger, apis, hostKind, duplicateChecker);
                    break;
                case DiscoveredRomodMod romod:
                    LoadAndInitializeRomod(romod, modRoot, assetCacheRoot, config, bootLogger, apis, hostKind, duplicateChecker);
                    break;
            }
        }
    }

    /// <summary>
    /// Loads a single .romod package: extracts assets, maps TOML models to
    /// loader Definitions, wraps in a <see cref="RomodDataMod"/>, applies
    /// the standard skip/disable/host filters, and registers content through
    /// the same <see cref="IContentRegistry"/> path used by C# mods.
    /// </summary>
    private static void LoadAndInitializeRomod(
        DiscoveredRomodMod romod,
        string modRoot,
        string assetCacheRoot,
        ModLoaderConfig config,
        ModLogger bootLogger,
        IModApiResolver apis,
        HostKind hostKind,
        DuplicateContentIdChecker duplicateChecker)
    {
        var manifest = romod.Document.Manifest;

        ModRegistries.Diagnostics.RegisterMetadata(new ModMetadataInfo(
            manifest.Id,
            romod.SyncMode,
            manifest.Author,
            manifest.Description,
            manifest.Homepage,
            manifest.Dependencies.Select(d => d.ModId).ToArray()));

        if (ShouldSkipForHost(romod.SyncMode, hostKind, out var hostSkipReason))
        {
            ModRegistries.Diagnostics.RegisterSkipped(new SkippedModInfo(
                romod.Id, romod.Name, romod.Version, hostSkipReason, romod.SourcePath, romod.SyncMode));
            return;
        }

        if (config.IsDisabled(romod.Id))
        {
            ModRegistries.Diagnostics.RegisterSkipped(new SkippedModInfo(
                romod.Id, romod.Name, romod.Version, "Disabled in mods.json", romod.SourcePath, romod.SyncMode));
            return;
        }

        try
        {
            bootLogger.Info($"Loading .romod package: {romod.SourcePath}");

            var bridgeLog = new RomodLoggerBridge(bootLogger);
            var packageCacheRoot = RomodAssetExtractor.Extract(romod.Document, assetCacheRoot, bridgeLog);

            var mapped = RomodToDefinitionMapper.Map(romod.Document, packageCacheRoot);
            var dataMod = new RomodDataMod(romod.Document, mapped);

            var logger = new ModLogger(romod.Id);
            var wrappedRegistry = new DuplicateCheckingContentRegistry(
                ModRegistries.Content,
                duplicateChecker,
                romod.Id,
                romod.SourcePath,
                logger);
            var context = new ModLoadContext(
                AppContext.BaseDirectory,
                modRoot,
                packageCacheRoot,
                logger,
                apis,
                wrappedRegistry);

            logger.Info($"Initializing {romod.Name} v{romod.Version}");
            logger.Info($"Multiplayer sync mode: {romod.SyncMode}");
            dataMod.Initialize(context);

            logger.Info($"Registering content for {romod.Name} v{romod.Version}");
            var itemStart = ModRegistries.Items.Pending.Count;
            var recipeStart = ModRegistries.Recipes.Pending.Count;
            var iconStart = ModRegistries.Icons.Pending.Count;
            var skillStart = ModRegistries.Skills.Pending.Count;
            var skillEffectStart = ModRegistries.SkillEffects.Pending.Count;
            var playerClassStart = ModRegistries.PlayerClasses.Pending.Count;
            var placeableStart = ModRegistries.Placeables.Pending.Count;

            dataMod.RegisterContent(wrappedRegistry);

            var newItems = ModRegistries.Items.Pending.Skip(itemStart).ToArray();
            var newSkills = ModRegistries.Skills.Pending.Skip(skillStart).ToArray();
            var newSkillEffects = ModRegistries.SkillEffects.Pending.Skip(skillEffectStart).ToArray();
            var newPlayerClasses = ModRegistries.PlayerClasses.Pending.Skip(playerClassStart).ToArray();
            var newPlaceables = ModRegistries.Placeables.Pending.Skip(placeableStart).ToArray();
            var newTextIds = newItems.Select(item => item.NameTextId ?? item.Name)
                .Concat(newItems.Select(item => item.DescriptionTextId ?? item.Description))
                .Concat(newSkills.Select(skill => skill.NameTextId ?? skill.Name))
                .Concat(newSkills.Select(skill => skill.DescriptionTextId ?? skill.Description))
                .Concat(newPlayerClasses.Select(playerClass => playerClass.NameTextId ?? playerClass.Name))
                .Concat(GetPlaceableTextIds(newPlaceables))
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            ModRegistries.Diagnostics.RegisterContent(new ModContentInfo(
                romod.Id,
                newItems.Select(item => item.Id)
                    .Concat(newPlaceables.Select(placeable => placeable.Id))
                    .Distinct(StringComparer.Ordinal)
                    .ToArray(),
                ModRegistries.Recipes.Pending.Skip(recipeStart).Select(recipe => recipe.ResultItemId).ToArray(),
                newTextIds,
                ModRegistries.Icons.Pending.Skip(iconStart).Select(icon => icon.Id).ToArray(),
                newSkills.Select(skill => skill.Id).ToArray(),
                newSkillEffects.Select(GetSkillEffectDiagnosticId).ToArray(),
                newPlayerClasses.Select(pc => pc.Id).ToArray(),
                []));

            ModRegistries.LoadedMods.Register(new LoadedModInfo(
                romod.Id, romod.Name, romod.Version, romod.SourcePath, romod.SyncMode));
        }
        catch (Exception ex)
        {
            bootLogger.Error($".romod package {romod.Id} failed.", ex);
            ModRegistries.Diagnostics.RegisterError(new ModLoadErrorInfo(romod.SourcePath, ex.Message));
            ModRegistries.Diagnostics.RegisterFailed(new FailedModInfo(
                romod.Id, romod.Name, romod.Version, romod.SourcePath, romod.SyncMode, ex.Message));
        }
    }

    /// <summary>
    /// Discovers C# DLL mod folders and builds a <see cref="DiscoveredDllMod"/>
    /// for each. Metadata (id/version/syncMode/dependencies) is read from
    /// the optional <c>mod.json</c> next to the DLL; missing fields fall
    /// back to the assembly file name so the rest of the pipeline always
    /// has *some* id to sort and key on.
    /// </summary>
    private static IReadOnlyList<DiscoveredDllMod> DiscoverDllMods(string modsDirectory, ModLogger bootLogger)
    {
        var result = new List<DiscoveredDllMod>();
        foreach (var entryAssemblyPath in DiscoverModEntryAssemblies(modsDirectory))
        {
            var metadata = LoadMetadata(entryAssemblyPath, bootLogger);
            var fallbackName = Path.GetFileNameWithoutExtension(entryAssemblyPath);
            var id = metadata?.Id is { Length: > 0 } mid ? mid : fallbackName;
            var name = metadata?.Name is { Length: > 0 } mname ? mname : fallbackName;
            var version = metadata?.Version is { Length: > 0 } v ? v : "<unknown>";
            var syncMode = metadata?.SyncMode ?? MultiplayerSyncMode.RequiredOnClient;
            var deps = (metadata?.Dependencies ?? [])
                .Where(dep => !string.IsNullOrWhiteSpace(dep))
                .Select(dep => new DependencyRequirement(dep, null))
                .ToArray();

            result.Add(new DiscoveredDllMod
            {
                Id = id,
                Name = name,
                Version = version,
                SyncMode = syncMode,
                SourcePath = entryAssemblyPath,
                Dependencies = deps
            });
        }
        return result;
    }

    private sealed class RomodLoggerBridge(ModLogger inner) : Romestead.RomodFormat.IRomodLog
    {
        public void Info(string message) => inner.Info(message);
        public void Warn(string message) => inner.Warn(message);
        public void Error(string message) => inner.Error(message);
    }

    private sealed class DiscoveredDllMod : DiscoveredMod
    {
        public override DiscoveredModKind Kind => DiscoveredModKind.Dll;
    }

    private static void LoadAndInitializeMod(
        string entryAssemblyPath,
        string gameRoot,
        string modRoot,
        ModLoaderConfig config,
        ModLogger bootLogger,
        IModApiResolver apis,
        HostKind hostKind,
        DuplicateContentIdChecker duplicateChecker)
    {
        Assembly assembly;
        var metadata = LoadMetadata(entryAssemblyPath, bootLogger);
        if (metadata?.SyncMode is { } metadataSyncMode &&
            ShouldSkipForHost(metadataSyncMode, hostKind, out var metadataSkipReason))
        {
            ModRegistries.Diagnostics.RegisterMetadata(CreateMetadataInfo(entryAssemblyPath, metadata));
            ModRegistries.Diagnostics.RegisterSkipped(new SkippedModInfo(
                metadata.Id ?? Path.GetFileNameWithoutExtension(entryAssemblyPath),
                metadata.Name ?? Path.GetFileNameWithoutExtension(entryAssemblyPath),
                metadata.Version ?? "<unknown>",
                metadataSkipReason,
                entryAssemblyPath,
                metadataSyncMode));
            return;
        }

        try
        {
            bootLogger.Info($"Loading mod assembly: {entryAssemblyPath}");
            assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(entryAssemblyPath);
        }
        catch (Exception ex)
        {
            bootLogger.Error($"Failed to load mod assembly: {entryAssemblyPath}", ex);
            ModRegistries.Diagnostics.RegisterError(new ModLoadErrorInfo(entryAssemblyPath, ex.Message));
            if (metadata is not null)
            {
                ModRegistries.Diagnostics.RegisterFailed(CreateFailedModInfo(entryAssemblyPath, metadata, ex.Message));
            }
            return;
        }

        foreach (var modType in GetModTypes(assembly))
        {
            ModManifestAttribute? manifest = null;
            try
            {
                manifest = modType.GetCustomAttribute<ModManifestAttribute>();
                if (manifest is null)
                {
                    bootLogger.Warn($"Skipping {modType.FullName} because it is missing [ModManifest].");
                    continue;
                }

                if (metadata is not null)
                {
                    if (string.IsNullOrWhiteSpace(metadata.Id) ||
                        string.Equals(metadata.Id, manifest.Id, StringComparison.OrdinalIgnoreCase))
                    {
                        ModRegistries.Diagnostics.RegisterMetadata(CreateMetadataInfo(manifest, metadata));
                    }
                    else
                    {
                        ModRegistries.Diagnostics.RegisterError(new ModLoadErrorInfo(
                            Path.Combine(Path.GetDirectoryName(entryAssemblyPath)!, "mod.json"),
                            $"Metadata id '{metadata.Id}' does not match manifest id '{manifest.Id}'."));
                        ModRegistries.Diagnostics.RegisterMetadata(CreateMetadataInfo(manifest, null));
                    }
                }
                else
                {
                    ModRegistries.Diagnostics.RegisterMetadata(CreateMetadataInfo(manifest, null));
                }

                if (ShouldSkipForHost(manifest.SyncMode, hostKind, out var hostSkipReason))
                {
                    ModRegistries.Diagnostics.RegisterSkipped(new SkippedModInfo(
                        manifest.Id,
                        manifest.Name,
                        manifest.Version,
                        hostSkipReason,
                        entryAssemblyPath,
                        manifest.SyncMode));
                    continue;
                }

                if (config.IsDisabled(manifest.Id) && !string.Equals(manifest.Id, "romestead.modloader.core", StringComparison.Ordinal))
                {
                    ModRegistries.Diagnostics.RegisterSkipped(new SkippedModInfo(
                        manifest.Id,
                        manifest.Name,
                        manifest.Version,
                        "Disabled in mods.json",
                        entryAssemblyPath,
                        manifest.SyncMode));
                    continue;
                }

                if (Activator.CreateInstance(modType) is not IRomesteadMod mod)
                {
                    bootLogger.Warn($"Skipping {modType.FullName} because it could not be instantiated.");
                    ModRegistries.Diagnostics.RegisterError(new ModLoadErrorInfo(modType.FullName ?? entryAssemblyPath, "Could not instantiate mod type."));
                    continue;
                }

                var logger = new ModLogger(manifest.Id);
                var wrappedRegistry = new DuplicateCheckingContentRegistry(
                    ModRegistries.Content,
                    duplicateChecker,
                    manifest.Id,
                    entryAssemblyPath,
                    logger);
                var context = new ModLoadContext(
                    gameRoot,
                    modRoot,
                    Path.GetDirectoryName(entryAssemblyPath)!,
                    logger,
                    apis,
                    wrappedRegistry);

                logger.Info($"Initializing {manifest.Name} v{manifest.Version}");
                logger.Info($"Multiplayer sync mode: {manifest.SyncMode}");

                // Snapshot pending counts before Initialize so content registered
                // through the context registries during Initialize is attributed and
                // duplicate-checked identically to content registered in RegisterContent.
                var itemStart = ModRegistries.Items.Pending.Count;
                var recipeStart = ModRegistries.Recipes.Pending.Count;
                var textStart = ModRegistries.Text.Pending.Count;
                var iconStart = ModRegistries.Icons.Pending.Count;
                var skillStart = ModRegistries.Skills.Pending.Count;
                var skillEffectStart = ModRegistries.SkillEffects.Pending.Count;
                var playerClassStart = ModRegistries.PlayerClasses.Pending.Count;
                var placeableStart = ModRegistries.Placeables.Pending.Count;
                var aggroTuningStart = ModRegistries.AggroTuning.Pending.Count;

                mod.Initialize(context);

                if (mod is IContentMod contentMod)
                {
                    logger.Info($"Registering content for {manifest.Name} v{manifest.Version}");
                    contentMod.RegisterContent(wrappedRegistry);
                    var newItems = ModRegistries.Items.Pending.Skip(itemStart).ToArray();
                    var newSkills = ModRegistries.Skills.Pending.Skip(skillStart).ToArray();
                    var newSkillEffects = ModRegistries.SkillEffects.Pending.Skip(skillEffectStart).ToArray();
                    var newPlayerClasses = ModRegistries.PlayerClasses.Pending.Skip(playerClassStart).ToArray();
                    var newPlaceables = ModRegistries.Placeables.Pending.Skip(placeableStart).ToArray();
                    var newAggroTuning = ModRegistries.AggroTuning.Pending.Skip(aggroTuningStart).ToArray();
                    var newTextIds = ModRegistries.Text.Pending.Skip(textStart).Select(text => text.Id)
                        .Concat(newItems.Select(item => item.NameTextId ?? item.Name))
                        .Concat(newItems.Select(item => item.DescriptionTextId ?? item.Description))
                        .Concat(newItems.Select(item => $"{item.Id}*item:name"))
                        .Concat(newItems.Select(item => $"{item.Id}*item:description"))
                        .Concat(newSkills.Select(skill => skill.NameTextId ?? skill.Name))
                        .Concat(newSkills.Select(skill => skill.DescriptionTextId ?? skill.Description))
                        .Concat(newSkills.Select(skill => $"{skill.Id}*skills:name"))
                        .Concat(newSkills.Select(skill => $"{skill.Id}*skills:description"))
                        .Concat(newPlayerClasses.Select(playerClass => playerClass.NameTextId ?? playerClass.Name))
                        .Concat(newPlayerClasses.Select(playerClass => $"{playerClass.Id}*player_class:name"))
                        .Concat(GetPlaceableTextIds(newPlaceables))
                        .Where(id => !string.IsNullOrWhiteSpace(id))
                        .Distinct(StringComparer.Ordinal)
                        .ToArray();
                    ModRegistries.Diagnostics.RegisterContent(new ModContentInfo(
                        manifest.Id,
                        newItems.Select(item => item.Id)
                            .Concat(newPlaceables.Select(placeable => placeable.Id))
                            .Distinct(StringComparer.Ordinal)
                            .ToArray(),
                        ModRegistries.Recipes.Pending.Skip(recipeStart).Select(recipe => recipe.ResultItemId).ToArray(),
                        newTextIds,
                        ModRegistries.Icons.Pending.Skip(iconStart).Select(icon => icon.Id).ToArray(),
                        newSkills.Select(skill => skill.Id).ToArray(),
                        newSkillEffects.Select(GetSkillEffectDiagnosticId).ToArray(),
                        newPlayerClasses.Select(playerClass => playerClass.Id).ToArray(),
                        newAggroTuning.Select(tuning => tuning.Id).ToArray()));
                }

                ModRegistries.LoadedMods.Register(new LoadedModInfo(
                    manifest.Id,
                    manifest.Name,
                    manifest.Version,
                    entryAssemblyPath,
                    manifest.SyncMode));
            }
            catch (Exception ex)
            {
                bootLogger.Error($"Mod type {modType.FullName} failed.", ex);
                ModRegistries.Diagnostics.RegisterError(new ModLoadErrorInfo(modType.FullName ?? entryAssemblyPath, ex.Message));
                ModRegistries.Diagnostics.RegisterFailed(CreateFailedModInfo(modType, entryAssemblyPath, metadata, manifest, ex.Message));
            }
        }
    }

    private static IEnumerable<Type> GetModTypes(Assembly assembly)
    {
        Type[] types;
        try
        {
            types = assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            types = ex.Types.Where(type => type is not null).Cast<Type>().ToArray();
        }

        return types.Where(type =>
            !type.IsAbstract &&
            !type.IsInterface &&
            typeof(IRomesteadMod).IsAssignableFrom(type));
    }

    private static string GetSkillEffectDiagnosticId(SkillEffectDefinition effect) =>
        $"{effect.SkillId}:{effect.Type}:{effect.TargetSkillId}";

    private static IEnumerable<string> GetPlaceableTextIds(IEnumerable<ModPlaceableStation> placeables)
    {
        foreach (var placeable in placeables)
        {
            yield return $"{placeable.Id}*item:name";
            yield return $"{placeable.Id}*item:description";
            yield return $"{placeable.ConstructionId}*construction:name";
            yield return $"{placeable.ConstructionId}*construction:description";
        }
    }

    private static bool ShouldSkipForHost(MultiplayerSyncMode syncMode, HostKind hostKind, out string reason)
    {
        switch (hostKind)
        {
            case HostKind.DedicatedServer when syncMode == MultiplayerSyncMode.ClientOnly:
                reason = "Skipped on DedicatedServer because SyncMode=ClientOnly.";
                return true;
            case HostKind.Client when syncMode == MultiplayerSyncMode.ServerOnly:
                reason = "Skipped on Client because SyncMode=ServerOnly.";
                return true;
            default:
                reason = "";
                return false;
        }
    }

    private static ModMetadataInfo CreateMetadataInfo(ModManifestAttribute manifest, ModMetadataFile? metadata) =>
        new(
            manifest.Id,
            manifest.SyncMode,
            metadata?.Author,
            metadata?.Description,
            metadata?.Homepage,
            metadata?.Dependencies ?? []);

    private static ModMetadataInfo CreateMetadataInfo(string entryAssemblyPath, ModMetadataFile metadata) =>
        new(
            string.IsNullOrWhiteSpace(metadata.Id) ? Path.GetFileNameWithoutExtension(entryAssemblyPath) : metadata.Id,
            metadata.SyncMode ?? MultiplayerSyncMode.RequiredOnClient,
            metadata.Author,
            metadata.Description,
            metadata.Homepage,
            metadata.Dependencies);

    private static FailedModInfo CreateFailedModInfo(
        Type modType,
        string entryAssemblyPath,
        ModMetadataFile? metadata,
        ModManifestAttribute? manifest,
        string reason)
    {
        if (manifest is not null)
        {
            return new FailedModInfo(
                manifest.Id,
                manifest.Name,
                manifest.Version,
                entryAssemblyPath,
                manifest.SyncMode,
                reason);
        }

        var id = metadata?.Id;
        var fallbackName = Path.GetFileNameWithoutExtension(entryAssemblyPath);
        return new FailedModInfo(
            string.IsNullOrWhiteSpace(id) ? fallbackName : id,
            metadata?.Name ?? modType.Name,
            metadata?.Version ?? "<unknown>",
            entryAssemblyPath,
            metadata?.SyncMode ?? MultiplayerSyncMode.RequiredOnClient,
            reason);
    }

    private static FailedModInfo CreateFailedModInfo(
        string entryAssemblyPath,
        ModMetadataFile? metadata,
        string reason)
    {
        var fallbackName = Path.GetFileNameWithoutExtension(entryAssemblyPath);
        return new FailedModInfo(
            string.IsNullOrWhiteSpace(metadata?.Id) ? fallbackName : metadata.Id,
            metadata?.Name ?? fallbackName,
            metadata?.Version ?? "<unknown>",
            entryAssemblyPath,
            metadata?.SyncMode ?? MultiplayerSyncMode.RequiredOnClient,
            reason);
    }

    private static ModMetadataFile? LoadMetadata(string entryAssemblyPath, ModLogger bootLogger)
    {
        var metadataPath = Path.Combine(Path.GetDirectoryName(entryAssemblyPath)!, "mod.json");
        if (!File.Exists(metadataPath))
        {
            return null;
        }

        try
        {
            var metadata = JsonSerializer.Deserialize<ModMetadataFile>(
                File.ReadAllText(metadataPath),
                ModLoaderConfig.JsonOptions);
            bootLogger.Info($"Loaded mod metadata: {metadataPath}");
            return metadata;
        }
        catch (Exception ex)
        {
            bootLogger.Error($"Failed to read mod metadata {metadataPath}.", ex);
            ModRegistries.Diagnostics.RegisterError(new ModLoadErrorInfo(metadataPath, ex.Message));
            return null;
        }
    }

    private sealed class ModLoaderConfig
    {
        public static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Converters = { new JsonStringEnumConverter() }
        };

        public List<string> DisabledMods { get; init; } = [];
        public bool EnforceMultiplayerCompatibility { get; init; }

        public bool IsDisabled(string modId) =>
            DisabledMods.Contains(modId, StringComparer.OrdinalIgnoreCase);
    }

    private sealed class ModMetadataFile
    {
        public string? Id { get; init; }
        public string? Name { get; init; }
        public string? Version { get; init; }
        public MultiplayerSyncMode? SyncMode { get; init; }
        public string? Author { get; init; }
        public string? Description { get; init; }
        public string? Homepage { get; init; }
        public List<string> Dependencies { get; init; } = [];
    }
}

internal enum HostKind
{
    Client,
    DedicatedServer
}

internal sealed record HostInfo(
    HostKind Kind,
    string EntryAssemblyName,
    string EntryPoint);
