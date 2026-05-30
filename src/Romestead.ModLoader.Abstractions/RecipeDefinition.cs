namespace Romestead.ModLoader;

/// <summary>
/// Declarative description of a crafting recipe to register with the game.
/// </summary>
public sealed class RecipeDefinition
{
    public required string ResultItemId { get; init; }
    public int ResultAmount { get; init; } = 1;
    public required string Station { get; init; }
    public required IReadOnlyList<RecipeIngredient> Ingredients { get; init; }
}

public sealed record RecipeIngredient(string ItemId, int Amount);
