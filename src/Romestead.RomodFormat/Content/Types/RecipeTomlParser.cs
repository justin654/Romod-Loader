using Tomlyn.Model;
using Romestead.RomodFormat.Internal;

namespace Romestead.RomodFormat.Content.Types;

public sealed record RecipeTomlModel
{
    public required string ResultItemId { get; init; }
    public int ResultAmount { get; init; } = 1;
    public required string Station { get; init; }
    public required IReadOnlyList<RecipeIngredientTomlModel> Ingredients { get; init; }
}

public sealed record RecipeIngredientTomlModel(string ItemId, int Amount);

public sealed class RecipeTomlParser : IRomodContentParser
{
    public RomodContentKind Kind => RomodContentKind.Recipe;

    private static readonly HashSet<string> KnownKeys = new(StringComparer.Ordinal)
    {
        "resultItemId", "resultAmount", "station", "ingredients"
    };

    private static readonly HashSet<string> KnownIngredientKeys = new(StringComparer.Ordinal)
    {
        "itemId", "amount"
    };

    public object Parse(TomlTable root, RomodContentParseContext context, IRomodLog log)
    {
        var src = context.ArchiveRelativePath;
        TomlHelpers.WarnUnknownKeys(root, KnownKeys, src, context.PackageId, log);

        var ingredients = ParseIngredients(root, context, log);
        if (ingredients.Count == 0)
        {
            throw new RomodFormatException(
                $"[{context.PackageId}] {src}: recipe must have at least one ingredient ([[ingredients]] table array).");
        }

        return new RecipeTomlModel
        {
            ResultItemId = TomlHelpers.RequireString(root, "resultItemId", src),
            ResultAmount = TomlHelpers.GetIntOrDefault(root, "resultAmount", src, 1, 1, int.MaxValue),
            Station = TomlHelpers.RequireString(root, "station", src),
            Ingredients = ingredients
        };
    }

    private static IReadOnlyList<RecipeIngredientTomlModel> ParseIngredients(
        TomlTable root, RomodContentParseContext context, IRomodLog log)
    {
        var array = TomlHelpers.GetTableArrayOrNull(root, "ingredients", context.ArchiveRelativePath);
        if (array is null)
        {
            return [];
        }

        var src = context.ArchiveRelativePath + " [[ingredients]]";
        var list = new List<RecipeIngredientTomlModel>(array.Count);
        for (var i = 0; i < array.Count; i++)
        {
            var entry = array[i];
            TomlHelpers.WarnUnknownKeys(entry, KnownIngredientKeys, src, context.PackageId, log);
            list.Add(new RecipeIngredientTomlModel(
                TomlHelpers.RequireString(entry, "itemId", src),
                TomlHelpers.GetIntOrDefault(entry, "amount", src, 1, 1, int.MaxValue)));
        }
        return list;
    }
}
