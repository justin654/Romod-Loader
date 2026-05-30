using Tomlyn.Model;
using Romestead.RomodFormat.Internal;

namespace Romestead.RomodFormat.Content.Types;

public sealed record MapAliasTomlModel
{
    public required string Original { get; init; }
    public required string Replacement { get; init; }
}

public sealed record MapFileTomlModel
{
    public required string MapId { get; init; }

    /// <summary>Archive-relative path to the replacement map file (resolved to the extracted asset at load).</summary>
    public required string Source { get; init; }

    /// <summary>Map file format: Cmx | Tmx.</summary>
    public string Format { get; init; } = "Tmx";
}

/// <summary>
/// A <c>*.map.toml</c> declares any number of <c>[[aliases]]</c> (redirect one
/// map id to another) and <c>[[files]]</c> (replace a map's geometry with a
/// packaged file). Unlike most kinds this is a collection, not a single entry.
/// </summary>
public sealed record MapTomlModel
{
    public IReadOnlyList<MapAliasTomlModel> Aliases { get; init; } = [];
    public IReadOnlyList<MapFileTomlModel> Files { get; init; } = [];
}

public sealed class MapTomlParser : IRomodContentParser
{
    public RomodContentKind Kind => RomodContentKind.Map;

    private static readonly HashSet<string> KnownKeys = new(StringComparer.Ordinal)
    {
        "aliases", "files"
    };

    public object Parse(TomlTable root, RomodContentParseContext context, IRomodLog log)
    {
        var src = context.ArchiveRelativePath;
        TomlHelpers.WarnUnknownKeys(root, KnownKeys, src, context.PackageId, log);

        var aliases = new List<MapAliasTomlModel>();
        var aliasArray = TomlHelpers.GetTableArrayOrNull(root, "aliases", src);
        if (aliasArray is not null)
        {
            foreach (var t in aliasArray)
            {
                aliases.Add(new MapAliasTomlModel
                {
                    Original = TomlHelpers.RequireString(t, "original", src),
                    Replacement = TomlHelpers.RequireString(t, "replacement", src)
                });
            }
        }

        var files = new List<MapFileTomlModel>();
        var fileArray = TomlHelpers.GetTableArrayOrNull(root, "files", src);
        if (fileArray is not null)
        {
            foreach (var t in fileArray)
            {
                files.Add(new MapFileTomlModel
                {
                    MapId = TomlHelpers.RequireString(t, "mapId", src),
                    Source = TomlHelpers.RequireString(t, "source", src),
                    Format = TomlHelpers.GetStringOrNull(t, "format", src) ?? "Tmx"
                });
            }
        }

        if (aliases.Count == 0 && files.Count == 0)
        {
            throw new RomodFormatException(
                $"{src}: a *.map.toml must define at least one [[aliases]] or [[files]] entry.");
        }

        return new MapTomlModel { Aliases = aliases, Files = files };
    }
}
