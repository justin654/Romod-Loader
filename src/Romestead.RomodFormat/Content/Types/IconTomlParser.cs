using Tomlyn.Model;
using Romestead.RomodFormat.Internal;

namespace Romestead.RomodFormat.Content.Types;

public sealed record IconTomlModel
{
    public required string Id { get; init; }
    /// <summary>Path inside the package (relative to package root, forward-slash separated).</summary>
    public required string Texture { get; init; }
    public int SpriteWidth { get; init; } = 32;
    public int SpriteHeight { get; init; } = 32;
    public int Frame { get; init; }
    public bool ReplaceExisting { get; init; }
}

public sealed class IconTomlParser : IRomodContentParser
{
    public RomodContentKind Kind => RomodContentKind.Icon;

    private static readonly HashSet<string> KnownKeys = new(StringComparer.Ordinal)
    {
        "id", "texture", "spriteWidth", "spriteHeight", "frame", "replaceExisting"
    };

    public object Parse(TomlTable root, RomodContentParseContext context, IRomodLog log)
    {
        var src = context.ArchiveRelativePath;
        TomlHelpers.WarnUnknownKeys(root, KnownKeys, src, context.PackageId, log);

        return new IconTomlModel
        {
            Id = TomlHelpers.RequireString(root, "id", src),
            Texture = TomlHelpers.RequireString(root, "texture", src),
            SpriteWidth = TomlHelpers.GetIntOrDefault(root, "spriteWidth", src, 32, 1, 8192),
            SpriteHeight = TomlHelpers.GetIntOrDefault(root, "spriteHeight", src, 32, 1, 8192),
            Frame = TomlHelpers.GetIntOrDefault(root, "frame", src, 0, 0, int.MaxValue),
            ReplaceExisting = TomlHelpers.GetBoolOrDefault(root, "replaceExisting", src, false)
        };
    }
}
