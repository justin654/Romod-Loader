using Tomlyn.Model;
using Romestead.RomodFormat.Internal;

namespace Romestead.RomodFormat.Content.Types;

public sealed record StatTomlModel
{
    public required string Id { get; init; }
    public string? NameTextId { get; init; }
    public required string Name { get; init; }
    public string? DescriptionTextId { get; init; }
    public string Description { get; init; } = "";
    public string Icon { get; init; } = "";
    public string Type { get; init; } = "Entity";
    public string Flags { get; init; } = "All";
    public string StringFormat { get; init; } = "0.";
    public bool IsPercentage { get; init; }
    public bool IsNegativeStat { get; init; }
    public float MinValue { get; init; }
    public float MaxValue { get; init; } = 999999f;
    public float DefaultValue { get; init; }
}

public sealed class StatTomlParser : IRomodContentParser
{
    public RomodContentKind Kind => RomodContentKind.Stat;

    private static readonly HashSet<string> KnownKeys = new(StringComparer.Ordinal)
    {
        "id", "nameTextId", "name", "descriptionTextId", "description",
        "icon", "type", "flags", "stringFormat",
        "isPercentage", "isNegativeStat",
        "minValue", "maxValue", "defaultValue"
    };

    public object Parse(TomlTable root, RomodContentParseContext context, IRomodLog log)
    {
        var src = context.ArchiveRelativePath;
        TomlHelpers.WarnUnknownKeys(root, KnownKeys, src, context.PackageId, log);

        var isPercentage = TomlHelpers.GetBoolOrDefault(root, "isPercentage", src, false);
        var explicitFormat = TomlHelpers.GetStringOrNull(root, "stringFormat", src);
        // For percentage stats, the right ToString format is "P0" (e.g. "12%").
        // Falling back to "0." would render 0.12 as "0" and lose the percent
        // sign in the inventory UI.
        var stringFormat = explicitFormat ?? (isPercentage ? "P0" : "0.");

        return new StatTomlModel
        {
            Id = TomlHelpers.RequireString(root, "id", src),
            NameTextId = TomlHelpers.GetStringOrNull(root, "nameTextId", src),
            Name = TomlHelpers.RequireString(root, "name", src),
            DescriptionTextId = TomlHelpers.GetStringOrNull(root, "descriptionTextId", src),
            Description = TomlHelpers.GetStringOrNull(root, "description", src) ?? "",
            Icon = TomlHelpers.GetStringOrNull(root, "icon", src) ?? "",
            Type = TomlHelpers.GetStringOrNull(root, "type", src) ?? "Entity",
            Flags = TomlHelpers.GetStringOrNull(root, "flags", src) ?? "All",
            StringFormat = stringFormat,
            IsPercentage = isPercentage,
            IsNegativeStat = TomlHelpers.GetBoolOrDefault(root, "isNegativeStat", src, false),
            MinValue = TomlHelpers.GetFloatOrDefault(root, "minValue", src, 0f),
            MaxValue = TomlHelpers.GetFloatOrDefault(root, "maxValue", src, 999999f),
            DefaultValue = TomlHelpers.GetFloatOrDefault(root, "defaultValue", src, 0f)
        };
    }
}
