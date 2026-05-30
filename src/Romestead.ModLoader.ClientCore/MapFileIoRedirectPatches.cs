using System.Xml;
using HarmonyLib;

namespace Romestead.ModLoader.ClientCore;

[HarmonyPatch(typeof(File), nameof(File.Exists), new[] { typeof(string) })]
internal static class FileExistsRedirectPatch
{
    private static bool Prefix(string path, ref bool __result)
    {
        if (MapFileIoRedirectGuard.IsReentrant ||
            !MapFileRedirectLoadContext.TryResolveIoPath(path, out var resolved))
        {
            return true;
        }

        using (MapFileIoRedirectGuard.Enter())
        {
            __result = File.Exists(resolved);
        }

        return false;
    }
}

[HarmonyPatch(typeof(File), nameof(File.OpenRead), new[] { typeof(string) })]
internal static class FileOpenReadRedirectPatch
{
    private static bool Prefix(string path, ref FileStream __result)
    {
        if (MapFileIoRedirectGuard.IsReentrant ||
            !MapFileRedirectLoadContext.TryResolveIoPath(path, out var resolved))
        {
            return true;
        }

        using (MapFileIoRedirectGuard.Enter())
        {
            __result = File.OpenRead(resolved);
        }

        return false;
    }
}

[HarmonyPatch(typeof(XmlDocument), nameof(XmlDocument.Load), new[] { typeof(string) })]
internal static class XmlDocumentLoadRedirectPatch
{
    private static bool Prefix(string filename, XmlDocument __instance)
    {
        if (MapFileIoRedirectGuard.IsReentrant ||
            !MapFileRedirectLoadContext.TryResolveIoPath(filename, out var resolved))
        {
            return true;
        }

        using (MapFileIoRedirectGuard.Enter())
        {
            __instance.Load(resolved);
        }

        return false;
    }
}
