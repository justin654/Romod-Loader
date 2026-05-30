using HarmonyLib;
using Romestead.ModLoader;

namespace Romestead.MapMagic;

[ModManifest("romestead.map-magic", "MapMagic", "0.1.0", SyncMode = MultiplayerSyncMode.ClientOnly)]
public sealed class MapMagicMod : IRomesteadMod
{
    public void Initialize(IModContext context)
    {
        MapMagicHost.ModLogger = context.Logger;
        MapMagicIntegration.Host = new MapMagicEditorBridge();
        var harmony = new Harmony("romestead.map-magic");
        harmony.PatchAll(typeof(MapMagicMod).Assembly);
        context.Logger.Info("MapMagic ready. F8 toggles object editor, F9 toggles tile mode.");
    }
}
