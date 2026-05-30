using System.IO.Compression;
using Romestead.RomodFormat.Validation;

namespace Romestead.RomodFormat.Package;

/// <summary>
/// Builds a <c>.romod</c> zip from a source folder, after running the same
/// read + validate pipeline used at runtime. Refusing to pack an invalid
/// package keeps obvious mistakes out of distribution.
/// </summary>
public static class RomodPackager
{
    private const string ManifestFileName = "romestead.mod.toml";
    private const string ContentDirectoryName = "content";
    private const string AssetsDirectoryName = "assets";
    private const string RomodExtension = ".romod";

    public sealed record PackResult(
        string OutputPath,
        RomodValidationResult Validation,
        int FilesIncluded,
        long BytesWritten);

    public static PackResult Pack(string sourceFolder, string outputPath, IRomodLog log, bool validateOnly = false)
    {
        ArgumentNullException.ThrowIfNull(sourceFolder);
        ArgumentNullException.ThrowIfNull(outputPath);
        ArgumentNullException.ThrowIfNull(log);

        sourceFolder = Path.GetFullPath(sourceFolder);
        outputPath = Path.GetFullPath(outputPath);

        if (!Directory.Exists(sourceFolder))
        {
            throw new RomodFormatException($"Source folder not found: {sourceFolder}");
        }

        var manifestPath = Path.Combine(sourceFolder, ManifestFileName);
        if (!File.Exists(manifestPath))
        {
            throw new RomodFormatException(
                $"Missing {ManifestFileName} at the source folder root: {sourceFolder}");
        }

        if (!string.Equals(Path.GetExtension(outputPath), RomodExtension, StringComparison.OrdinalIgnoreCase))
        {
            log.Warn($"Output path '{outputPath}' does not use the '.romod' extension. " +
                     $"The loader only discovers files ending in '.romod'.");
        }

        // Guard against packing into the source tree — the next pack would
        // include the previous .romod as a content entry and recurse forever.
        var sourceWithSep = sourceFolder.EndsWith(Path.DirectorySeparatorChar)
            ? sourceFolder
            : sourceFolder + Path.DirectorySeparatorChar;
        if (outputPath.StartsWith(sourceWithSep, StringComparison.OrdinalIgnoreCase))
        {
            throw new RomodFormatException(
                $"Output path '{outputPath}' is inside the source folder '{sourceFolder}'. " +
                $"Choose an output path outside the source tree.");
        }

        var destDir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(destDir))
        {
            Directory.CreateDirectory(destDir);
        }

        // Pack to a unique temp archive next to the output so we can re-validate
        // it through the same read pipeline the runtime uses; only move it into
        // place if validation passes. Using a GUID avoids colliding with a
        // concurrent pack of the same target (e.g. CI matrix).
        var tempPath = Path.Combine(
            destDir is { Length: > 0 } ? destDir : ".",
            Path.GetFileNameWithoutExtension(outputPath) + ".pack-" + Guid.NewGuid().ToString("N") + ".tmp");

        var filesIncluded = 0;
        try
        {
            filesIncluded = WriteZip(sourceFolder, tempPath);

            var pipeline = new RomodPackagePipeline();
            var result = pipeline.Run(tempPath, log);

            foreach (var diag in result.Validation.Diagnostics)
            {
                switch (diag.Severity)
                {
                    case RomodValidationSeverity.Error: log.Error(diag.ToString()); break;
                    case RomodValidationSeverity.Warning: log.Warn(diag.ToString()); break;
                    default: log.Info(diag.ToString()); break;
                }
            }

            if (result.Validation.HasErrors)
            {
                throw new RomodFormatException(
                    $"Package validation failed with " +
                    $"{result.Validation.Errors.Count()} error(s). See log for details.");
            }

            if (validateOnly)
            {
                return new PackResult(outputPath, result.Validation, filesIncluded, BytesWritten: 0);
            }

            if (File.Exists(outputPath))
            {
                File.Delete(outputPath);
            }

            File.Move(tempPath, outputPath);
            tempPath = null!;  // ownership transferred; do not delete in finally

            var bytesWritten = new FileInfo(outputPath).Length;
            log.Info($"Packed {filesIncluded} file(s) ({bytesWritten} bytes) into {outputPath}.");
            return new PackResult(outputPath, result.Validation, filesIncluded, bytesWritten);
        }
        finally
        {
            if (tempPath is not null)
            {
                try { File.Delete(tempPath); } catch { /* best effort */ }
            }
        }
    }

    private static int WriteZip(string sourceFolder, string outputPath)
    {
        // Collect everything first so we can sort deterministically and detect
        // path collisions before opening the archive for writing.
        var entries = new List<(string SourcePath, string ArchivePath)>();

        entries.Add((Path.Combine(sourceFolder, ManifestFileName), ManifestFileName));

        var contentDir = Path.Combine(sourceFolder, ContentDirectoryName);
        if (Directory.Exists(contentDir))
        {
            foreach (var file in Directory.EnumerateFiles(contentDir, "*.toml", SearchOption.AllDirectories))
            {
                entries.Add((file, ToArchivePath(sourceFolder, file)));
            }
        }

        var assetsDir = Path.Combine(sourceFolder, AssetsDirectoryName);
        if (Directory.Exists(assetsDir))
        {
            foreach (var file in Directory.EnumerateFiles(assetsDir, "*", SearchOption.AllDirectories))
            {
                entries.Add((file, ToArchivePath(sourceFolder, file)));
            }
        }

        // Deterministic ordering: archive entries sorted ordinally by archive-relative path.
        // Makes zip output byte-identical across runs for unchanged sources, which is
        // handy for `diff`-able CI artifacts and reproducible builds.
        entries.Sort((a, b) => string.CompareOrdinal(a.ArchivePath, b.ArchivePath));

        // Case-insensitive duplicate detection — Windows readers treat
        // "Assets/foo.png" and "assets/foo.png" as the same file, so refuse
        // to build a package that would behave differently on Windows vs Linux.
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (_, archivePath) in entries)
        {
            if (IsUnsafeArchivePath(archivePath))
            {
                throw new RomodFormatException(
                    $"Refused to pack unsafe archive path '{archivePath}' " +
                    $"(absolute paths and '..' segments are rejected).");
            }
            if (!seen.Add(archivePath))
            {
                throw new RomodFormatException(
                    $"Duplicate archive path '{archivePath}' (case-insensitive) — " +
                    $"two source files map to the same archive entry.");
            }
        }

        using var fs = File.Create(outputPath);
        using var archive = new ZipArchive(fs, ZipArchiveMode.Create, leaveOpen: false);

        foreach (var (sourcePath, archivePath) in entries)
        {
            AddFile(archive, sourcePath, archivePath);
        }

        return entries.Count;
    }

    private static void AddFile(ZipArchive archive, string sourcePath, string archiveRelativePath)
    {
        var entry = archive.CreateEntry(archiveRelativePath, CompressionLevel.Optimal);
        using var src = File.OpenRead(sourcePath);
        using var dst = entry.Open();
        src.CopyTo(dst);
    }

    private static string ToArchivePath(string sourceFolder, string filePath)
    {
        var rel = Path.GetRelativePath(sourceFolder, filePath);
        return rel.Replace(Path.DirectorySeparatorChar, '/').Replace('\\', '/');
    }

    private static bool IsUnsafeArchivePath(string archivePath)
    {
        if (string.IsNullOrWhiteSpace(archivePath))
        {
            return true;
        }

        if (archivePath.StartsWith('/') || (archivePath.Length >= 2 && archivePath[1] == ':'))
        {
            return true;
        }

        foreach (var segment in archivePath.Split('/'))
        {
            if (segment == "..")
            {
                return true;
            }
        }

        return false;
    }
}
