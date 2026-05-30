using Tomlyn.Model;
using Romestead.RomodFormat.Internal;

namespace Romestead.RomodFormat.Content.Types;

public sealed record AggroTuningTomlModel
{
    public required string Id { get; init; }
    public required string Type { get; init; }
    public float Value { get; init; } = 1f;
    public bool ApplyToBosses { get; init; }
}

public sealed class AggroTuningTomlParser : IRomodContentParser
{
    public RomodContentKind Kind => RomodContentKind.AggroTuning;

    private static readonly HashSet<string> KnownKeys = new(StringComparer.Ordinal)
    {
        "id", "type", "value", "applyToBosses"
    };

    public object Parse(TomlTable root, RomodContentParseContext context, IRomodLog log)
    {
        var src = context.ArchiveRelativePath;
        TomlHelpers.WarnUnknownKeys(root, KnownKeys, src, context.PackageId, log);

        return new AggroTuningTomlModel
        {
            Id = TomlHelpers.RequireString(root, "id", src),
            Type = TomlHelpers.RequireString(root, "type", src),
            Value = TomlHelpers.GetFloatOrDefault(root, "value", src, 1f),
            ApplyToBosses = TomlHelpers.GetBoolOrDefault(root, "applyToBosses", src, false)
        };
    }
}
