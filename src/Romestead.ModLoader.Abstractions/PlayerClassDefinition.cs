namespace Romestead.ModLoader;

/// <summary>
/// Declarative description of a character creation class.
/// </summary>
public sealed class PlayerClassDefinition
{
    public required string Id { get; init; }
    public string? NameTextId { get; init; }
    public required string Name { get; init; }
    public required string BonusSkill { get; init; }
    public IReadOnlyList<SkillBonusDefinition> SkillBonuses { get; init; } = [];
    public IReadOnlyList<string> StartingClothes { get; init; } = [];
    public IReadOnlyList<RecipeIngredient> StartingInventory { get; init; } = [];
    public int? StartingFavourPoints { get; init; }
}

public sealed record SkillBonusDefinition(string SkillId, int Level);
