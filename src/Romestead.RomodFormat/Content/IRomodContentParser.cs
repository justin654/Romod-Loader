using Tomlyn.Model;

namespace Romestead.RomodFormat.Content;

/// <summary>
/// Parses one TOML document into a typed model. One parser per content
/// kind; new kinds plug in by registering with
/// <see cref="RomodContentParserRegistry"/>.
/// </summary>
public interface IRomodContentParser
{
    RomodContentKind Kind { get; }

    /// <summary>
    /// Parse the root table of a single <c>*.{kind}.toml</c> file.
    /// </summary>
    /// <param name="root">Root TOML table.</param>
    /// <param name="context">Where this came from + the package id (for error messages).</param>
    /// <param name="log">Warning sink for unknown-field warnings.</param>
    object Parse(TomlTable root, RomodContentParseContext context, IRomodLog log);
}

/// <summary>
/// Context attached to every parser invocation so error messages can name
/// the package and the file path inside the archive.
/// </summary>
public sealed record RomodContentParseContext(string PackageId, string ArchiveRelativePath);
