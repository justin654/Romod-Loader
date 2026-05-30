using Tomlyn.Model;
using Romestead.RomodFormat.Internal;

namespace Romestead.RomodFormat.Content.Types;

public sealed record PlaceableTomlModel
{
    public required string Id { get; init; }
    public required string StationId { get; init; }
    public required string DisplayName { get; init; }
    public string? Description { get; init; }
    public required string IconId { get; init; }

    /// <summary>Archive-relative path to the world-art PNG (resolved to the extracted asset at load).</summary>
    public required string Texture { get; init; }

    public int SpriteWidth { get; init; }
    public int SpriteHeight { get; init; }
    public float? SpriteOffsetX { get; init; }
    public float? SpriteOffsetY { get; init; }
    public float? CollisionWidth { get; init; }
    public float? CollisionHeight { get; init; }
    public float? CollisionOffsetX { get; init; }
    public float? CollisionOffsetY { get; init; }

    /// <summary>Vanilla bench to clone: Cauldron | Campfire | WarTable.</summary>
    public string Template { get; init; } = "WarTable";
}

public sealed class PlaceableTomlParser : IRomodContentParser
{
    public RomodContentKind Kind => RomodContentKind.Placeable;

    private static readonly HashSet<string> KnownKeys = new(StringComparer.Ordinal)
    {
        "id", "stationId", "displayName", "description", "iconId", "texture",
        "spriteWidth", "spriteHeight", "spriteOffsetX", "spriteOffsetY",
        "collisionWidth", "collisionHeight", "collisionOffsetX", "collisionOffsetY",
        "template"
    };

    public object Parse(TomlTable root, RomodContentParseContext context, IRomodLog log)
    {
        var src = context.ArchiveRelativePath;
        TomlHelpers.WarnUnknownKeys(root, KnownKeys, src, context.PackageId, log);

        return new PlaceableTomlModel
        {
            Id = TomlHelpers.RequireString(root, "id", src),
            StationId = TomlHelpers.RequireString(root, "stationId", src),
            DisplayName = TomlHelpers.RequireString(root, "displayName", src),
            Description = TomlHelpers.GetStringOrNull(root, "description", src),
            IconId = TomlHelpers.RequireString(root, "iconId", src),
            Texture = TomlHelpers.RequireString(root, "texture", src),
            SpriteWidth = TomlHelpers.GetIntOrDefault(root, "spriteWidth", src, 0),
            SpriteHeight = TomlHelpers.GetIntOrDefault(root, "spriteHeight", src, 0),
            SpriteOffsetX = TomlHelpers.GetFloatOrNull(root, "spriteOffsetX", src),
            SpriteOffsetY = TomlHelpers.GetFloatOrNull(root, "spriteOffsetY", src),
            CollisionWidth = TomlHelpers.GetFloatOrNull(root, "collisionWidth", src),
            CollisionHeight = TomlHelpers.GetFloatOrNull(root, "collisionHeight", src),
            CollisionOffsetX = TomlHelpers.GetFloatOrNull(root, "collisionOffsetX", src),
            CollisionOffsetY = TomlHelpers.GetFloatOrNull(root, "collisionOffsetY", src),
            Template = TomlHelpers.GetStringOrNull(root, "template", src) ?? "WarTable"
        };
    }
}
