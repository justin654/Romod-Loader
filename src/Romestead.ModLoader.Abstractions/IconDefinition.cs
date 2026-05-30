namespace Romestead.ModLoader;

/// <summary>
/// Declarative description of an icon that should be available through IconDataBase.
/// TexturePath can be absolute or relative to the game's working directory.
/// </summary>
public sealed class IconDefinition
{
    public required string Id { get; init; }
    public required string TexturePath { get; init; }
    public string? ContentPath { get; init; }
    public int SpriteWidth { get; init; } = 32;
    public int SpriteHeight { get; init; } = 32;
    public int Frame { get; init; }
    public bool ReplaceExisting { get; init; }
}
