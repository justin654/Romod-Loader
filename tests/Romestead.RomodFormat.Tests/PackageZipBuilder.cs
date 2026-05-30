using System.IO.Compression;
using System.Text;

namespace Romestead.RomodFormat.Tests;

/// <summary>
/// Tiny in-memory zip builder used by the tests. Each test produces a
/// freshly-named .romod file in a per-process temp folder so failures
/// don't pollute later tests.
/// </summary>
internal static class PackageZipBuilder
{
    private static readonly string TempRoot = Path.Combine(Path.GetTempPath(), "romod-tests");

    public static string CreateArchive(params (string Path, string Content)[] entries)
    {
        Directory.CreateDirectory(TempRoot);
        var path = Path.Combine(TempRoot, $"pkg-{Guid.NewGuid():N}.romod");

        using var fs = File.Create(path);
        using var archive = new ZipArchive(fs, ZipArchiveMode.Create, leaveOpen: false);
        foreach (var (entryPath, content) in entries)
        {
            var entry = archive.CreateEntry(entryPath);
            using var stream = entry.Open();
            using var writer = new StreamWriter(stream, Encoding.UTF8);
            writer.Write(content);
        }
        return path;
    }

    public static (string Path, string Content) BinaryEntry(string path, byte[] bytes)
    {
        // Encode bytes as Base64-disguised utf8 — tests don't actually need real PNGs,
        // they just need an asset entry to exist in the archive.
        return (path, Convert.ToBase64String(bytes));
    }
}
