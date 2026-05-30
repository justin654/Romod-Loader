namespace Romestead.ModLoader;

/// <summary>
/// A text entry that can be resolved by the game's StringId translation system.
/// </summary>
public sealed class TextDefinition
{
    public required string Id { get; init; }
    public required string Text { get; init; }
}
