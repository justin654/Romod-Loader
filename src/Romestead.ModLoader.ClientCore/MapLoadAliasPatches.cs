using HarmonyLib;
using Candide.World;
using Romestead.ModLoader;

namespace Romestead.ModLoader.ClientCore;

/// <summary>
/// File redirects run during <see cref="WorldLoader.LoadWorld"/> only. The logical map id is preserved
/// so vanilla .cmx (transport nodes) still loads from Content; only .tmx I/O is redirected to the mod file.
/// </summary>
[HarmonyPatch(typeof(WorldLoader), nameof(WorldLoader.LoadWorld))]
internal static class WorldLoaderLoadWorldPatch
{
    private static void Prefix(ref string mapName) => MapLoadPatchLogic.ApplyWorldLoaderLoad(ref mapName);

    private static void Postfix() => MapFileRedirectLoadContext.Exit();
}

[HarmonyPatch(typeof(OldInteriorWorldHandler), nameof(OldInteriorWorldHandler.Load))]
internal static class OldInteriorWorldHandlerLoadDiscoveryPatch
{
    private static void Prefix(ref string mapName) => MapLoadPatchLogic.ApplyDiscoveryAndAlias(ref mapName);
}

internal static class MapLoadPatchLogic
{
    internal static void ApplyWorldLoaderLoad(ref string mapName)
    {
        ApplyDiscoveryAndAlias(ref mapName);
        BeginFileRedirect(mapName);
    }

    internal static void ApplyDiscoveryAndAlias(ref string mapName)
    {
        if (IsFilesystemLoadPath(mapName))
        {
            return;
        }

        var normalized = MapKeyNormalizer.Normalize(mapName);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return;
        }

        mapName = normalized;

        if (ModRegistries.Maps is MapRegistry mapRegistry && mapRegistry.RecordObservedMapLoad(normalized))
        {
            CoreState.Logger?.Info($"[Romod] Map observed: {normalized}");
        }

        CoreState.Logger?.Info($"[Romod] Map load: {normalized}");

        if (ModRegistries.Maps.TryResolveAlias(normalized, out var replacement))
        {
            CoreState.Logger?.Info($"[Romod] Map alias: {normalized} -> {replacement}");
            mapName = replacement;
        }
    }

    private static void BeginFileRedirect(string logicalMapId)
    {
        if (!ModRegistries.Maps.TryResolveFile(logicalMapId, out var registration))
        {
            return;
        }

        if (!MapFileRedirectResolver.TryPrepareLoadPath(registration, out var redirectBasePath, out var failureReason))
        {
            if (string.Equals(failureReason, "source file does not exist", StringComparison.Ordinal))
            {
                CoreState.Logger?.Info(
                    $"[Romod] Map file redirect missing: {registration.MapId} -> {registration.SourcePath}");
            }
            else
            {
                CoreState.Logger?.Info(
                    $"[Romod] Map file redirect failed: {registration.MapId} ({failureReason})");
            }

            return;
        }

        var redirectTmxPath = redirectBasePath + ".tmx";
        if (!MapFileRedirectLoadContext.TryEnter(logicalMapId, redirectTmxPath))
        {
            CoreState.Logger?.Info(
                $"[Romod] Map file redirect failed: {registration.MapId} (could not scope redirect to game Content paths)");
            return;
        }

        CoreState.Logger?.Info(
            $"[Romod] Map file redirect: {registration.MapId} -> {registration.SourcePath} ({registration.Format}); vanilla .cmx preserved");
    }

    private static bool IsFilesystemLoadPath(string mapName)
    {
        if (string.IsNullOrWhiteSpace(mapName))
        {
            return false;
        }

        return Path.IsPathRooted(mapName) ||
               mapName.Contains(':', StringComparison.Ordinal) ||
               mapName.StartsWith(@"\\", StringComparison.Ordinal);
    }
}
