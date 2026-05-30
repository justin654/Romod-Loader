using Romestead.ModLoader;

namespace Romestead.MapAliasTestMod;

/// <summary>
/// Phase 1 / 1.5 map-alias helper. Enables discovery logging; register aliases only after
/// you confirm map IDs from romestead-loader.log.
/// </summary>
[ModManifest("romestead.map-alias-test", "Map Alias Test", "0.1.1", SyncMode = MultiplayerSyncMode.ClientOnly)]
public sealed class MapAliasTestMod : IRomesteadMod, IContentMod
{
    public void Initialize(IModContext context)
    {
        context.Logger.Info(
            "Map Alias Test: play the game and enter interiors, dungeons, and buildings. " +
            "Search romestead-loader.log for [Romod] Map observed: lines, then uncomment an alias in RegisterContent.");
    }

    public void RegisterContent(IContentRegistry registry)
    {
        // Discovery workflow:
        // 1. Run the game with this mod enabled (no alias required).
        // 2. Enter interiors, dungeons, buildings, and transports.
        // 3. Copy map IDs from log lines: [Romod] Map observed: <id>
        // 4. Uncomment and edit the alias below using confirmed ids only.
        //
        // Example (only enable after you see the source id in the log):
        // registry.Maps.RegisterAlias(
        //     "maps/interiors_new/insula_1",
        //     "maps/dungeons/plains/plains_crypt_ruin");
    }
}
