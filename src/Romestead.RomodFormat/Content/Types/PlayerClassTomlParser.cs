using Tomlyn.Model;
using Romestead.RomodFormat.Internal;

namespace Romestead.RomodFormat.Content.Types;

public sealed record PlayerClassTomlModel
{
    public required string Id { get; init; }
    public string? NameTextId { get; init; }
    public required string Name { get; init; }
    public required string BonusSkill { get; init; }
    public IReadOnlyList<SkillBonusTomlModel> SkillBonuses { get; init; } = [];
    public IReadOnlyList<string> StartingClothes { get; init; } = [];
    public IReadOnlyList<RecipeIngredientTomlModel> StartingInventory { get; init; } = [];
    public int? StartingFavourPoints { get; init; }
}

public sealed record SkillBonusTomlModel(string SkillId, int Level);

public sealed class PlayerClassTomlParser : IRomodContentParser
{
    public RomodContentKind Kind => RomodContentKind.PlayerClass;

    private static readonly HashSet<string> KnownKeys = new(StringComparer.Ordinal)
    {
        "id", "nameTextId", "name", "bonusSkill",
        "skillBonuses", "startingClothes", "startingInventory",
        "startingFavourPoints"
    };

    private static readonly HashSet<string> KnownSkillBonusKeys = new(StringComparer.Ordinal)
    {
        "skillId", "level"
    };

    private static readonly HashSet<string> KnownInventoryKeys = new(StringComparer.Ordinal)
    {
        "itemId", "amount"
    };

    public object Parse(TomlTable root, RomodContentParseContext context, IRomodLog log)
    {
        var src = context.ArchiveRelativePath;
        TomlHelpers.WarnUnknownKeys(root, KnownKeys, src, context.PackageId, log);

        int? startingFavour = null;
        if (root.TryGetValue("startingFavourPoints", out var raw) && raw is not null)
        {
            startingFavour = TomlHelpers.GetIntOrDefault(root, "startingFavourPoints", src, 0, 0, int.MaxValue);
        }

        return new PlayerClassTomlModel
        {
            Id = TomlHelpers.RequireString(root, "id", src),
            NameTextId = TomlHelpers.GetStringOrNull(root, "nameTextId", src),
            Name = TomlHelpers.RequireString(root, "name", src),
            BonusSkill = TomlHelpers.RequireString(root, "bonusSkill", src),
            SkillBonuses = ParseSkillBonuses(root, context, log),
            StartingClothes = TomlHelpers.GetStringArrayOrEmpty(root, "startingClothes", src),
            StartingInventory = ParseStartingInventory(root, context, log),
            StartingFavourPoints = startingFavour
        };
    }

    private static IReadOnlyList<SkillBonusTomlModel> ParseSkillBonuses(
        TomlTable root, RomodContentParseContext context, IRomodLog log)
    {
        var array = TomlHelpers.GetTableArrayOrNull(root, "skillBonuses", context.ArchiveRelativePath);
        if (array is null)
        {
            return [];
        }

        var src = context.ArchiveRelativePath + " [[skillBonuses]]";
        var list = new List<SkillBonusTomlModel>(array.Count);
        for (var i = 0; i < array.Count; i++)
        {
            var entry = array[i];
            TomlHelpers.WarnUnknownKeys(entry, KnownSkillBonusKeys, src, context.PackageId, log);
            list.Add(new SkillBonusTomlModel(
                TomlHelpers.RequireString(entry, "skillId", src),
                TomlHelpers.GetIntOrDefault(entry, "level", src, 0)));
        }
        return list;
    }

    private static IReadOnlyList<RecipeIngredientTomlModel> ParseStartingInventory(
        TomlTable root, RomodContentParseContext context, IRomodLog log)
    {
        var array = TomlHelpers.GetTableArrayOrNull(root, "startingInventory", context.ArchiveRelativePath);
        if (array is null)
        {
            return [];
        }

        var src = context.ArchiveRelativePath + " [[startingInventory]]";
        var list = new List<RecipeIngredientTomlModel>(array.Count);
        for (var i = 0; i < array.Count; i++)
        {
            var entry = array[i];
            TomlHelpers.WarnUnknownKeys(entry, KnownInventoryKeys, src, context.PackageId, log);
            list.Add(new RecipeIngredientTomlModel(
                TomlHelpers.RequireString(entry, "itemId", src),
                TomlHelpers.GetIntOrDefault(entry, "amount", src, 1, 1, int.MaxValue)));
        }
        return list;
    }
}
