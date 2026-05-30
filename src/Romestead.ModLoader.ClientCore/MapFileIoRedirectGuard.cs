namespace Romestead.ModLoader.ClientCore;

/// <summary>
/// Prevents Harmony file I/O patches from re-entering when they call <see cref="File.Exists"/> /
/// <see cref="File.OpenRead"/> / <see cref="System.Xml.XmlDocument.Load(string)"/> on the resolved path.
/// </summary>
internal static class MapFileIoRedirectGuard
{
    [ThreadStatic]
    private static int _depth;

    internal static bool IsReentrant => _depth > 0;

    internal static ReentrantScope Enter() => new();

    internal readonly struct ReentrantScope : IDisposable
    {
        public ReentrantScope() => _depth++;

        public void Dispose() => _depth--;
    }
}
