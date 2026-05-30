namespace Romestead.ModLoader.ClientCore;

/// <summary>
/// Thread-local scope for redirecting only .tmx reads during <see cref="Candide.World.WorldLoader.LoadWorld"/>.
/// Vanilla .cmx beside the logical map id is still loaded from game Content (transport nodes, etc.).
/// </summary>
internal static class MapFileRedirectLoadContext
{
    [ThreadStatic]
    private static Scope? _current;

    internal static bool IsActive => _current is not null;

    internal static bool TryEnter(string logicalMapId, string redirectTmxPath)
    {
        if (string.IsNullOrWhiteSpace(logicalMapId) || string.IsNullOrWhiteSpace(redirectTmxPath))
        {
            return false;
        }

        var gameRoot = AppContext.BaseDirectory;
        var contentRoot = Path.GetFullPath(Path.Combine(gameRoot, "Content"));
        var vanillaCmxPath = Path.GetFullPath(Path.Combine(contentRoot, logicalMapId + ".cmx"));
        var vanillaTmxPath = Path.GetFullPath(Path.Combine(contentRoot, logicalMapId + ".tmx"));

        if (!IsUnderRoot(contentRoot, vanillaCmxPath) || !IsUnderRoot(contentRoot, vanillaTmxPath))
        {
            return false;
        }

        _current = new Scope(logicalMapId, redirectTmxPath, vanillaCmxPath, vanillaTmxPath);
        return true;
    }

    internal static void Exit()
    {
        _current = null;
    }

    internal static bool TryResolveIoPath(string requestedPath, out string resolvedPath)
    {
        resolvedPath = requestedPath;
        var scope = _current;
        if (scope is null || string.IsNullOrWhiteSpace(requestedPath))
        {
            return false;
        }

        var normalizedRequested = Path.GetFullPath(requestedPath);

        if (PathsEqual(normalizedRequested, scope.VanillaCmxPath))
        {
            resolvedPath = scope.VanillaCmxPath;
            return true;
        }

        if (PathsEqual(normalizedRequested, scope.VanillaTmxPath))
        {
            resolvedPath = scope.RedirectTmxPath;
            return true;
        }

        // Companion .tmx path emitted from .cmx may differ in casing but should end with the same map id.
        if (normalizedRequested.EndsWith(".tmx", StringComparison.OrdinalIgnoreCase) &&
            normalizedRequested.Contains(scope.LogicalMapId, StringComparison.OrdinalIgnoreCase))
        {
            resolvedPath = scope.RedirectTmxPath;
            return true;
        }

        return false;
    }

    private static bool PathsEqual(string left, string right) =>
        string.Equals(
            Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);

    private static bool IsUnderRoot(string rootPath, string candidatePath)
    {
        var root = Path.GetFullPath(rootPath);
        var candidate = Path.GetFullPath(candidatePath);
        if (!root.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal))
        {
            root += Path.DirectorySeparatorChar;
        }

        return candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }

    private sealed record Scope(
        string LogicalMapId,
        string RedirectTmxPath,
        string VanillaCmxPath,
        string VanillaTmxPath);
}
