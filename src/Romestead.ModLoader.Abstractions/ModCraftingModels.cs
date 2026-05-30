namespace Romestead.ModLoader;

/// <summary>
/// Requests the game's native crafting window for one or more crafting stations. The host opens the
/// real <c>SecondaryCraftingWindow</c> bound to the local player's inventory, so recipes registered
/// through <see cref="IRecipeRegistry"/> for these stations appear automatically with the game's own
/// look (recipe grid, requirement counts, Craft / +N buttons).
/// </summary>
public sealed class ModStationCraftingDefinition
{
    public required string Id { get; init; }

    /// <summary>
    /// Crafting station ids whose recipes the window lists (e.g. "campfire"). Recipes registered for
    /// any of these stations show up. Unknown ids render as a "missing station" header.
    /// </summary>
    public required IReadOnlyList<string> StationIds { get; init; }

    /// <summary>Initial screen position. Null lets the game place the window.</summary>
    public int? X { get; init; }
    public int? Y { get; init; }
}

/// <summary>Host-facing view of an open native crafting window request.</summary>
public sealed class ModStationCraftingInstance
{
    public required string Id { get; init; }
    public required IReadOnlyList<string> StationIds { get; init; }
    public int? X { get; init; }
    public int? Y { get; init; }
}
