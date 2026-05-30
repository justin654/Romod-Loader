using System.IO.Compression;
using System.Text;
using Tomlyn;
using Tomlyn.Model;
using Romestead.RomodFormat.Content;
using Romestead.RomodFormat.Manifest;

namespace Romestead.RomodFormat.Package;

/// <summary>
/// Reads a <c>.romod</c> archive into a <see cref="RomodPackageDocument"/>.
/// Pure: it does not extract assets to disk and does not register anything
/// with the runtime — that's the loader's job. Reused by the CLI packer's
/// <c>validate</c> command and by the unit tests.
/// </summary>
public sealed class RomodPackageReader
{
    private const string ManifestFileName = "romestead.mod.toml";
    private const string ContentDirectoryPrefix = "content/";
    private const string AssetsDirectoryPrefix = "assets/";

    private readonly RomodContentParserRegistry _parsers;

    public RomodPackageReader() : this(RomodContentParserRegistry.CreateDefault()) { }

    public RomodPackageReader(RomodContentParserRegistry parsers)
    {
        ArgumentNullException.ThrowIfNull(parsers);
        _parsers = parsers;
    }

    public RomodPackageDocument ReadFromFile(string archivePath, IRomodLog log)
    {
        ArgumentNullException.ThrowIfNull(archivePath);
        ArgumentNullException.ThrowIfNull(log);

        if (!File.Exists(archivePath))
        {
            throw new RomodFormatException($"Package file not found: {archivePath}");
        }

        using var stream = File.OpenRead(archivePath);
        return Read(stream, archivePath, log);
    }

    public RomodPackageDocument Read(Stream zipStream, string archivePath, IRomodLog log)
    {
        ArgumentNullException.ThrowIfNull(zipStream);
        ArgumentNullException.ThrowIfNull(archivePath);
        ArgumentNullException.ThrowIfNull(log);

        ZipArchive archive;
        try
        {
            archive = new ZipArchive(zipStream, ZipArchiveMode.Read, leaveOpen: true);
        }
        catch (InvalidDataException ex)
        {
            throw new RomodFormatException($"{archivePath}: not a valid zip archive ({ex.Message}).", ex);
        }

        using (archive)
        {
            var manifestEntry = archive.GetEntry(ManifestFileName)
                ?? throw new RomodFormatException(
                    $"{archivePath}: missing {ManifestFileName} at the package root.");

            var manifest = ReadManifest(manifestEntry, log);
            var packageId = manifest.Id;

            var contentEntries = new List<RomodContentEntry>();
            var assets = new List<RomodAssetEntry>();

            foreach (var entry in archive.Entries)
            {
                var name = NormalizeEntryPath(entry.FullName);
                if (entry.Length == 0 && name.EndsWith('/'))
                {
                    continue;
                }

                // Reject zip slip / traversal eagerly so the rest of the
                // pipeline (extractor, mapper, asset resolution) never sees
                // an entry that could escape the package's cache root.
                if (IsUnsafeArchivePath(name))
                {
                    throw new RomodFormatException(
                        $"{archivePath}: refused entry '{entry.FullName}' — absolute paths and '..' segments are not allowed.");
                }

                if (string.Equals(name, ManifestFileName, StringComparison.Ordinal))
                {
                    continue;
                }

                if (name.StartsWith(ContentDirectoryPrefix, StringComparison.Ordinal))
                {
                    var parsed = TryParseContentEntry(entry, name, packageId, log);
                    if (parsed is not null)
                    {
                        contentEntries.Add(parsed);
                    }
                    continue;
                }

                if (name.StartsWith(AssetsDirectoryPrefix, StringComparison.Ordinal))
                {
                    assets.Add(new RomodAssetEntry(name, entry.Length));
                    continue;
                }

                log.Warn($"[{packageId}] Unrecognized archive entry '{name}'. Ignoring.");
            }

            return new RomodPackageDocument
            {
                ArchivePath = archivePath,
                Manifest = manifest,
                ContentEntries = contentEntries,
                Assets = assets
            };
        }
    }

    private static RomodManifest ReadManifest(ZipArchiveEntry entry, IRomodLog log)
    {
        using var stream = entry.Open();
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var text = reader.ReadToEnd();
        return RomodManifestParser.Parse(text, log);
    }

    private RomodContentEntry? TryParseContentEntry(
        ZipArchiveEntry entry, string archiveRelativePath, string packageId, IRomodLog log)
    {
        var fileName = Path.GetFileName(archiveRelativePath);
        if (!fileName.EndsWith(".toml", StringComparison.OrdinalIgnoreCase))
        {
            log.Warn($"[{packageId}] Non-TOML file in content/: '{archiveRelativePath}'. Ignoring.");
            return null;
        }

        // pattern: anything.{kind}.toml
        var withoutToml = fileName[..^".toml".Length];
        var dot = withoutToml.LastIndexOf('.');
        if (dot <= 0 || dot == withoutToml.Length - 1)
        {
            log.Warn($"[{packageId}] Content file '{archiveRelativePath}' does not match pattern '*.{{kind}}.toml'. Ignoring.");
            return null;
        }

        var suffix = withoutToml[(dot + 1)..];
        if (!RomodContentKindExtensions.TryFromFileSuffix(suffix, out var kind))
        {
            log.Warn($"[{packageId}] Unknown content type '*.{suffix}.toml' in '{archiveRelativePath}'. Ignoring.");
            return null;
        }

        if (!_parsers.TryGet(kind, out var parser))
        {
            log.Warn($"[{packageId}] No parser registered for content kind '{kind}'. Ignoring '{archiveRelativePath}'.");
            return null;
        }

        TomlTable root;
        using (var stream = entry.Open())
        using (var reader = new StreamReader(stream, Encoding.UTF8))
        {
            var text = reader.ReadToEnd();
            try
            {
                root = Toml.ToModel(text, sourcePath: archiveRelativePath);
            }
            catch (Exception ex)
            {
                throw new RomodFormatException(
                    $"[{packageId}] Failed to parse TOML in {archiveRelativePath}: {ex.Message}", ex);
            }
        }

        var context = new RomodContentParseContext(packageId, archiveRelativePath);
        var model = parser.Parse(root, context, log);
        return new RomodContentEntry(kind, archiveRelativePath, model);
    }

    private static string NormalizeEntryPath(string entryFullName) =>
        entryFullName.Replace('\\', '/');

    private static bool IsUnsafeArchivePath(string normalized)
    {
        if (normalized.StartsWith('/') || (normalized.Length >= 2 && normalized[1] == ':'))
        {
            return true;
        }

        foreach (var segment in normalized.Split('/'))
        {
            if (segment == "..")
            {
                return true;
            }
        }

        return false;
    }
}
