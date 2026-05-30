using Tomlyn.Model;
using Romestead.RomodFormat.Internal;

namespace Romestead.RomodFormat.Content.Types;

public sealed record TextTomlModel
{
    public required string Id { get; init; }
    public required string Text { get; init; }
}

public sealed class TextTomlParser : IRomodContentParser
{
    public RomodContentKind Kind => RomodContentKind.Text;

    private static readonly HashSet<string> KnownKeys = new(StringComparer.Ordinal)
    {
        "id", "text"
    };

    public object Parse(TomlTable root, RomodContentParseContext context, IRomodLog log)
    {
        var src = context.ArchiveRelativePath;
        TomlHelpers.WarnUnknownKeys(root, KnownKeys, src, context.PackageId, log);

        return new TextTomlModel
        {
            Id = TomlHelpers.RequireString(root, "id", src),
            Text = TomlHelpers.RequireString(root, "text", src)
        };
    }
}
