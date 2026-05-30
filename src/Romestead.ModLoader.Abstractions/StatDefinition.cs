namespace Romestead.ModLoader;

/// <summary>
/// What kind of entity a stat lives on. Mirrors the game's
/// <c>Shared.Models.Stats.StatType</c>.
/// </summary>
public enum ModStatType
{
    /// <summary>Per-player / per-NPC stat (Health, Energy, Armor, etc.).</summary>
    Entity = 0,
    /// <summary>Per-citizen settlement stat.</summary>
    Citizen = 1,
    /// <summary>World-level stat.</summary>
    World = 2
}

/// <summary>
/// Which modification kinds an item's <see cref="StatBonusDefinition"/> is
/// allowed to apply to this stat. <see cref="All"/> matches the vanilla
/// "everything goes" default — leave it unless you have a specific reason
/// to restrict.
/// </summary>
[System.Flags]
public enum ModStatFlags
{
    None = 0,
    Additive = 1,
    AdditiveMultiplier = 2,
    BaseMultiplier = 4,
    BonusMultiplier = 8,
    Multiplier = 16,
    All = 31
}

/// <summary>
/// Declarative definition of a new entity / citizen / world stat to register
/// with the game. Once registered, items and player classes can grant bonuses
/// to this stat via <see cref="StatBonusDefinition.StatId"/> and the stat
/// shows up wherever the game queries the stat registry.
/// </summary>
public sealed class StatDefinition
{
    public required string Id { get; init; }
    public string? NameTextId { get; init; }
    public required string Name { get; init; }
    public string? DescriptionTextId { get; init; }
    public string Description { get; init; } = "";

    /// <summary>
    /// Icon ID for HUDs and tooltips. Use a vanilla icon (<c>ui:energy</c>,
    /// <c>ui:health</c>, etc.) or register a new one with <c>registry.Icons</c>.
    /// </summary>
    public string Icon { get; init; } = "";

    public ModStatType Type { get; init; } = ModStatType.Entity;
    public ModStatFlags Flags { get; init; } = ModStatFlags.All;

    /// <summary>
    /// Numeric format passed to ToString when the stat is rendered. Vanilla
    /// uses <c>"0."</c> for whole numbers, <c>"P0"</c> for percentages.
    /// </summary>
    public string StringFormat { get; init; } = "0.";

    public float MinValue { get; init; }
    public float MaxValue { get; init; } = 999999f;
    public float DefaultValue { get; init; }
    public bool IsPercentage { get; init; }

    /// <summary>
    /// True for stats where lower is better (e.g. damage taken modifier).
    /// </summary>
    public bool IsNegativeStat { get; init; }
}
