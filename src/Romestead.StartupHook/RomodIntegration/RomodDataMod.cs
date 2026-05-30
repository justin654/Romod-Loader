using Romestead.ModLoader;
using Romestead.RomodFormat.Package;

namespace Romestead.StartupHook.RomodIntegration;

/// <summary>
/// Adapter that makes a parsed <c>.romod</c> package look like any other
/// <see cref="IRomesteadMod"/> + <see cref="IContentMod"/>. The loader's
/// regular code path then runs unchanged: the wrapper just pushes
/// definitions into <see cref="IContentRegistry"/>, and the existing
/// <c>SharedContentBootstrap</c> + ClientCore drains consume them.
///
/// Deliberately:
/// * does not patch any game type with Harmony,
/// * does not touch any game database directly,
/// * has no client-only references — works on the dedicated server too.
/// </summary>
internal sealed class RomodDataMod : IRomesteadMod, IContentMod
{
    private readonly RomodPackageDocument _document;
    private readonly RomodToDefinitionMapper.Mapped _mapped;

    public RomodDataMod(RomodPackageDocument document, RomodToDefinitionMapper.Mapped mapped)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
        _mapped = mapped ?? throw new ArgumentNullException(nameof(mapped));
    }

    public string Id => _document.Manifest.Id;
    public string Name => _document.Manifest.Name;
    public string Version => _document.Manifest.Version;
    public MultiplayerSyncMode SyncMode => RomodToDefinitionMapper.MapSyncMode(_document.Manifest.SyncMode);
    public string ArchivePath => _document.ArchivePath;
    public RomodPackageDocument Document => _document;
    public RomodToDefinitionMapper.Mapped MappedContent => _mapped;

    public void Initialize(IModContext context)
    {
        // .romod packages are pure-data — no lifecycle hooks. The mod
        // context is here only because IRomesteadMod requires it.
        context.Logger.Info($"Loaded .romod package {Name} v{Version} (id={Id}).");
    }

    public void RegisterContent(IContentRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);

        foreach (var text in _mapped.Texts) { registry.Text.Register(text); }
        foreach (var stat in _mapped.Stats) { registry.Stats.Register(stat); }
        foreach (var station in _mapped.CraftingStations) { registry.CraftingStations.Register(station); }
        foreach (var placeable in _mapped.Placeables) { registry.Placeables.Register(placeable); }
        foreach (var icon in _mapped.Icons) { registry.Icons.Register(icon); }
        foreach (var item in _mapped.Items) { registry.Items.Register(item); }
        foreach (var recipe in _mapped.Recipes) { registry.Recipes.Register(recipe); }
        foreach (var skill in _mapped.Skills) { registry.Skills.Register(skill); }
        foreach (var effect in _mapped.SkillEffects) { registry.SkillEffects.Register(effect); }
        foreach (var playerClass in _mapped.PlayerClasses) { registry.PlayerClasses.Register(playerClass); }
        foreach (var tuning in _mapped.AggroTuning) { registry.AggroTuning.Register(tuning); }
        foreach (var alias in _mapped.MapAliases) { registry.Maps.RegisterAlias(alias.Original, alias.Replacement); }
        foreach (var file in _mapped.MapFiles) { registry.Maps.RegisterFile(file.MapId, file.SourcePath, file.Format); }
        foreach (var valueOverride in _mapped.ValueOverrides) { registry.ValueOverrides.Register(valueOverride); }
    }
}
