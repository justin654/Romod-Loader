namespace Romestead.ModLoader;

public enum ModWindowStyle
{
    Standard,
    Dark,
    None
}

/// <summary>
/// Describes a draggable in-game window. Content uses the same section/row model as overlays and
/// settings pages, including the crafting-oriented rows (<see cref="ModImageRow"/>,
/// <see cref="ModButtonBarRow"/>, <see cref="ModItemSlotGridRow"/>, <see cref="ModTextInputRow"/>).
/// </summary>
public sealed class ModWindowDefinition
{
    public required string Id { get; init; }
    public string? Title { get; init; }
    public ModWindowStyle Style { get; init; } = ModWindowStyle.Standard;

    /// <summary>Fixed content width in pixels. Null lets the window size to its content.</summary>
    public int? Width { get; init; }

    /// <summary>Initial screen position. Null centers the window on the desktop.</summary>
    public int? X { get; init; }
    public int? Y { get; init; }

    /// <summary>Adds a built-in "Close" button at the bottom of the window.</summary>
    public bool ShowCloseButton { get; init; } = true;

    public IReadOnlyList<ModSection> Sections { get; init; } = [];
}

/// <summary>
/// Host-facing mutable view of an open window. The registry updates <see cref="Title"/> and
/// <see cref="Sections"/> in place; layout/style/position are fixed at open time.
/// </summary>
public sealed class ModWindowInstance
{
    public required string Id { get; init; }
    public string? Title { get; set; }
    public ModWindowStyle Style { get; init; }
    public int? Width { get; init; }
    public int? X { get; init; }
    public int? Y { get; init; }
    public bool ShowCloseButton { get; init; }
    public IReadOnlyList<ModSection> Sections { get; set; } = [];
}
