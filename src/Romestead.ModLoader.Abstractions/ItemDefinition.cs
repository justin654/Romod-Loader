namespace Romestead.ModLoader;

/// <summary>
/// Declarative description of an item to register with the game.
/// Mods do not need to know about the engine's internal item types.
/// </summary>
public sealed class ItemDefinition
{
    public required string Id { get; init; }
    public string? NameTextId { get; init; }
    public required string Name { get; init; }
    public string? DescriptionTextId { get; init; }
    public required string Description { get; init; }
    public required string Icon { get; init; }
    public int MaxStackSize { get; init; } = 99;
    public int Tier { get; init; } = 1;

    /// <summary>
    /// Optional equipment data. When set, the item becomes equippable in
    /// the slot specified by <see cref="EquipmentDefinition.Slot"/> and
    /// (for weapons/shields) gets the matching combat stats. Leave null
    /// for plain materials, consumables, and other non-equippable items.
    /// </summary>
    public EquipmentDefinition? Equipment { get; init; }
}
