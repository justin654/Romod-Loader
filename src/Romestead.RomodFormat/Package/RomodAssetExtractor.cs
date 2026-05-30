using System.IO.Compression;
using System.Security.Cryptography;

namespace Romestead.RomodFormat.Package;

/// <summary>
/// Extracts the <c>assets/</c> tree from a .romod archive to a deterministic
/// cache folder. Skips extraction if the cache is already up to date (a
/// stamp file matches the archive contents hash).
/// </summary>
public static class RomodAssetExtractor
{
    private const string StampFileName = ".romod-cache-stamp";

    /// <summary>
    /// Extracts <paramref name="document"/>'s assets into
    /// <c>cacheRoot/&lt;manifestId&gt;/&lt;version&gt;/assets/...</c> and
    /// returns the package's cache root (parent of the <c>assets/</c> tree).
    /// Existing extracted files outside the asset set are NOT touched.
    /// </summary>
    public static string Extract(RomodPackageDocument document, string cacheRoot, IRomodLog log)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(cacheRoot);
        ArgumentNullException.ThrowIfNull(log);

        var packageCacheRoot = Path.Combine(
            cacheRoot,
            SanitizeForPath(document.Manifest.Id),
            SanitizeForPath(document.Manifest.Version));
        Directory.CreateDirectory(packageCacheRoot);

        if (document.Assets.Count == 0)
        {
            return packageCacheRoot;
        }

        var stampFile = Path.Combine(packageCacheRoot, StampFileName);
        var currentStamp = ComputeStamp(document);

        if (File.Exists(stampFile))
        {
            try
            {
                var existing = File.ReadAllText(stampFile).Trim();
                if (string.Equals(existing, currentStamp, StringComparison.Ordinal))
                {
                    log.Info($"[{document.Manifest.Id}] Asset cache is up to date at {packageCacheRoot}.");
                    return packageCacheRoot;
                }
            }
            catch
            {
                // fall through and re-extract
            }
        }

        // stale cache; wipe assets/ subtree then re-extract
        var assetsRoot = Path.Combine(packageCacheRoot, "assets");
        if (Directory.Exists(assetsRoot))
        {
            try { Directory.Delete(assetsRoot, recursive: true); }
            catch (Exception ex)
            {
                log.Warn($"[{document.Manifest.Id}] Could not clear stale asset cache {assetsRoot}: {ex.Message}");
            }
        }

        using var stream = File.OpenRead(document.ArchivePath);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

        // Compute the canonical cache root once so we can rebuff any entry that
        // tries to walk out of it via `..` or absolute paths.
        var canonicalRoot = Path.GetFullPath(packageCacheRoot);
        var rootWithSep = canonicalRoot.EndsWith(Path.DirectorySeparatorChar)
            ? canonicalRoot
            : canonicalRoot + Path.DirectorySeparatorChar;

        var seenDestinations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var extracted = 0;
        foreach (var asset in document.Assets)
        {
            if (!IsSafeArchivePath(asset.ArchiveRelativePath))
            {
                throw new RomodFormatException(
                    $"[{document.Manifest.Id}] Refused to extract unsafe asset path '{asset.ArchiveRelativePath}'. " +
                    $"Absolute paths and '..' segments are not allowed.");
            }

            var entry = archive.GetEntry(asset.ArchiveRelativePath)
                ?? archive.GetEntry(asset.ArchiveRelativePath.Replace('/', '\\'));
            if (entry is null)
            {
                throw new RomodFormatException(
                    $"[{document.Manifest.Id}] Asset {asset.ArchiveRelativePath} disappeared between read and extract. " +
                    $"Was the package archive replaced mid-load?");
            }

            var destination = Path.GetFullPath(
                Path.Combine(packageCacheRoot, asset.ArchiveRelativePath.Replace('/', Path.DirectorySeparatorChar)));
            if (!destination.StartsWith(rootWithSep, StringComparison.OrdinalIgnoreCase))
            {
                throw new RomodFormatException(
                    $"[{document.Manifest.Id}] Asset '{asset.ArchiveRelativePath}' would extract outside the cache root.");
            }

            if (!seenDestinations.Add(destination))
            {
                throw new RomodFormatException(
                    $"[{document.Manifest.Id}] Multiple archive entries map to the same destination path '{destination}'. " +
                    $"Asset paths must be unique (case-insensitive).");
            }

            var destinationDir = Path.GetDirectoryName(destination);
            if (!string.IsNullOrEmpty(destinationDir))
            {
                Directory.CreateDirectory(destinationDir);
            }

            using var destStream = File.Create(destination);
            using var entryStream = entry.Open();
            entryStream.CopyTo(destStream);
            extracted++;
        }

        File.WriteAllText(stampFile, currentStamp);
        log.Info($"[{document.Manifest.Id}] Extracted {extracted} asset(s) to {packageCacheRoot}.");
        return packageCacheRoot;
    }

    /// <summary>
    /// Returns the absolute on-disk path of an asset given the package's
    /// extracted cache root.
    /// </summary>
    public static string ResolveAssetPath(string packageCacheRoot, string archiveRelativePath)
    {
        return Path.Combine(packageCacheRoot, archiveRelativePath.Replace('/', Path.DirectorySeparatorChar));
    }

    private static string ComputeStamp(RomodPackageDocument document)
    {
        // Stamp covers archive path + last write time + asset list + version + schemaVersion.
        // Cheap, deterministic, no need to hash file contents.
        var sb = new System.Text.StringBuilder();
        sb.Append(document.Manifest.SchemaVersion).Append('|');
        sb.Append(document.Manifest.Version).Append('|');
        try
        {
            sb.Append(new FileInfo(document.ArchivePath).LastWriteTimeUtc.Ticks).Append('|');
        }
        catch { /* path may not exist in tests */ }
        foreach (var asset in document.Assets.OrderBy(a => a.ArchiveRelativePath, StringComparer.Ordinal))
        {
            sb.Append(asset.ArchiveRelativePath).Append(':').Append(asset.UncompressedSize).Append(';');
        }

        var bytes = System.Text.Encoding.UTF8.GetBytes(sb.ToString());
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }

    private static bool IsSafeArchivePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var normalized = path.Replace('\\', '/');
        if (Path.IsPathRooted(normalized) || normalized.StartsWith('/'))
        {
            return false;
        }

        foreach (var segment in normalized.Split('/'))
        {
            if (segment == "..")
            {
                return false;
            }
        }

        return true;
    }

    private static string SanitizeForPath(string raw)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new System.Text.StringBuilder(raw.Length);
        foreach (var c in raw)
        {
            sb.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
        }

        var result = sb.ToString().Trim();
        return result.Length == 0 ? "_" : result;
    }
}
