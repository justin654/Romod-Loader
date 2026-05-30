namespace Romestead.ModLoader;

/// <summary>
/// Declarative description of a custom crafting station to register with the game.
/// A station gives mod recipes their own bench identity: recipes whose
/// <see cref="RecipeDefinition.Station"/> matches <see cref="Id"/> appear in a
/// crafting window opened for that station, under this station's name and icon.
/// </summary>
public sealed class CraftingStationDefinition
{
    /// <summary>
    /// Stable station id (e.g. "embercraft"). Match this in
    /// <see cref="RecipeDefinition.Station"/> and when opening a crafting window.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Display name shown in the crafting window header.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Icon id shown next to the station name in the window header.
    /// Use a vanilla icon id or one registered through the icon registry.
    /// </summary>
    public required string IconId { get; init; }
}
