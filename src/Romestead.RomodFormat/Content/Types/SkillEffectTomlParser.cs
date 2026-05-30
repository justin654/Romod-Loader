using Tomlyn.Model;
using Romestead.RomodFormat.Internal;

namespace Romestead.RomodFormat.Content.Types;

public sealed record SkillEffectTomlModel
{
    public required string SkillId { get; init; }
    public required string Type { get; init; }
    public required string TargetSkillId { get; init; }
    public float ValuePerLevel { get; init; } = 0.05f;
}

public sealed class SkillEffectTomlParser : IRomodContentParser
{
    public RomodContentKind Kind => RomodContentKind.SkillEffect;

    private static readonly HashSet<string> KnownKeys = new(StringComparer.Ordinal)
    {
        "skillId", "type", "targetSkillId", "valuePerLevel"
    };

    public object Parse(TomlTable root, RomodContentParseContext context, IRomodLog log)
    {
        var src = context.ArchiveRelativePath;
        TomlHelpers.WarnUnknownKeys(root, KnownKeys, src, context.PackageId, log);

        return new SkillEffectTomlModel
        {
            SkillId = TomlHelpers.RequireString(root, "skillId", src),
            Type = TomlHelpers.RequireString(root, "type", src),
            TargetSkillId = TomlHelpers.RequireString(root, "targetSkillId", src),
            ValuePerLevel = TomlHelpers.GetFloatOrDefault(root, "valuePerLevel", src, 0.05f)
        };
    }
}
