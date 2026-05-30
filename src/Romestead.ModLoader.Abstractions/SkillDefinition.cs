namespace Romestead.ModLoader;

/// <summary>
/// Declarative description of a skill shown in character progression UI.
/// </summary>
public sealed class SkillDefinition
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
