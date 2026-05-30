using Romestead.RomodFormat.Content;
using Romestead.RomodFormat.Manifest;

namespace Romestead.RomodFormat.Package;

/// <summary>
/// In-memory representation of a parsed package, BEFORE assets are extracted
/// to disk and BEFORE TOML models are mapped to game-side definitions.
/// Migrators operate on this document.
/// </summary>
public sealed record RomodPackageDocument
{
    public required string ArchivePath { get; init; }
    public required RomodManifest Manifest { get; init; }

    /// <summary>
    /// Every content entry parsed from <c>content/*.{kind}.toml</c>. The
    /// <c>Model</c> is the typed TOML model from
    /// <c>Romestead.RomodFormat.Content.Types.*</c>.
    /// </summary>
    public required IReadOnlyList<RomodContentEntry> ContentEntries { get; init; }

    /// <summary>
    /// Asset entries (anything under <c>assets/</c>) that authors can refer
    /// to from icons or future content kinds. Paths are forward-slash,
    /// relative to the package root.
    /// </summary>
    public required IReadOnlyList<RomodAssetEntry> Assets { get; init; }

    public RomodPackageDocument WithManifest(RomodManifest manifest) => this with { Manifest = manifest };
    public RomodPackageDocument WithContentEntries(IReadOnlyList<RomodContentEntry> entries) =>
        this with { ContentEntries = entries };
}

public sealed record RomodContentEntry(RomodContentKind Kind, string ArchiveRelativePath, object Model);

public sealed record RomodAssetEntry(string ArchiveRelativePath, long UncompressedSize);
