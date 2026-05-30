using Tomlyn.Model;
using Romestead.RomodFormat.Internal;

namespace Romestead.RomodFormat.Content.Types;

public sealed record SkillTomlModel
{
    public required string Id { get; init; }
    public string? NameTextId { get; init; }
    public required string Name { get; init; }
    public string? DescriptionTextId { get; init; }
    public required string Description { get; init; }
    public required string Icon { get; init; }
    public float Value { get; init; } = 0.05f;
    public float ExperienceGainFactor { get; init; } = 1.0f;
}

public sealed class SkillTomlParser : IRomodContentParser
{
    public RomodContentKind Kind => RomodContentKind.Skill;

    private static readonly HashSet<string> KnownKeys = new(StringComparer.Ordinal)
    {
        "id", "nameTextId", "name", "descriptionTextId", "description",
        "icon", "value", "experienceGainFactor"
    };

    public object Parse(TomlTable root, RomodContentParseContext context, IRomodLog log)
    {
        var src = context.ArchiveRelativePath;
        TomlHelpers.WarnUnknownKeys(root, KnownKeys, src, context.PackageId, log);

        return new SkillTomlModel
        {
            Id = TomlHelpers.RequireString(root, "id", src),
            NameTextId = TomlHelpers.GetStringOrNull(root, "nameTextId", src),
            Name = TomlHelpers.RequireString(root, "name", src),
            DescriptionTextId = TomlHelpers.GetStringOrNull(root, "descriptionTextId", src),
            Description = TomlHelpers.RequireString(root, "description", src),
            Icon = TomlHelpers.RequireString(root, "icon", src),
            Value = TomlHelpers.GetFloatOrDefault(root, "value", src, 0.05f),
            ExperienceGainFactor = TomlHelpers.GetFloatOrDefault(root, "experienceGainFactor", src, 1.0f)
        };
    }
}
