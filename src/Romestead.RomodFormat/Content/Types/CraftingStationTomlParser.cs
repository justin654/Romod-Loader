using Tomlyn.Model;
using Romestead.RomodFormat.Internal;

namespace Romestead.RomodFormat.Content.Types;

public sealed record CraftingStationTomlModel
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string IconId { get; init; }
}

public sealed class CraftingStationTomlParser : IRomodContentParser
{
    public RomodContentKind Kind => RomodContentKind.CraftingStation;

    private static readonly HashSet<string> KnownKeys = new(StringComparer.Ordinal)
    {
        "id", "name", "iconId"
    };

    public object Parse(TomlTable root, RomodContentParseContext context, IRomodLog log)
    {
        var src = context.ArchiveRelativePath;
        TomlHelpers.WarnUnknownKeys(root, KnownKeys, src, context.PackageId, log);

        return new CraftingStationTomlModel
        {
            Id = TomlHelpers.RequireString(root, "id", src),
            Name = TomlHelpers.RequireString(root, "name", src),
            IconId = TomlHelpers.RequireString(root, "iconId", src)
        };
    }
}
