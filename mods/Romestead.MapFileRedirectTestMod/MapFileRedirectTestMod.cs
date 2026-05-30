using Romestead.ModLoader;

namespace Romestead.MapFileRedirectTestMod;

/// <summary>
/// Phase 2 map file redirect helper. Redirects affect <b>client geometry only</b> (tiles/collision);
/// enemies, loot, and containers remain server-authoritative.
/// </summary>
[ModManifest("romestead.map-file-redirect-test", "Map File Redirect Test", "0.1.2", SyncMode = MultiplayerSyncMode.ClientOnly)]
public sealed class MapFileRedirectTestMod : IRomesteadMod, IContentMod
{
    private string? _modDirectory;

    public void Initialize(IModContext context)
    {
        _modDirectory = context.ModDirectory;
        context.Logger.Info(
            "Map File Redirect Test: Insula uses mod maps/insula_1.tmx for visuals; vanilla insula_1.cmx still loads for entry nodes. " +
            "Edit romestead_modding/mods/Romestead.MapFileRedirectTestMod/maps/insula_1.tmx, run build.ps1, then restart the game.");
    }

    public void RegisterContent(IContentRegistry registry)
    {
        // Client geometry only — server still simulates the vanilla Insula instance (NPCs/loot unchanged).
        //
        // Discovery: search romestead-loader.log for [Romod] Map observed: lines on other maps.
        // To disable this redirect and only discover ids, comment out RegisterFile below.
        var mapCopy = Path.Combine(_modDirectory!, "maps", "insula_1.tmx");
        registry.Maps.RegisterFile(
            "maps/interiors_new/insula_1",
            mapCopy,
            MapFileFormat.Tmx);
    }
}
