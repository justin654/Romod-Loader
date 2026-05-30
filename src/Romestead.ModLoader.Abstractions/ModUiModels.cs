namespace Romestead.ModLoader;

public sealed class ModSettingsPageDefinition
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public string? Icon { get; init; }
    public int Order { get; init; }
    public Func<ModSettingsBuildContext, ModSettingsPage> Build { get; init; } = _ => new ModSettingsPage();
}

public sealed class ModSidebarEntryDefinition
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public string? Icon { get; init; }
    public int Order { get; init; }
    public required string TargetPageId { get; init; }
}

public sealed class ModSettingsBuildContext
{
    public required string GameRoot { get; init; }
    public required string ModRoot { get; init; }
    public required string ModDirectory { get; init; }
    public required IModLogger Logger { get; init; }
    public required IModApiResolver Apis { get; init; }
}

public sealed class ModUiActionContext
{
    public required IModLogger Logger { get; init; }
    public required Action<string> NavigateToPage { get; init; }
    public required Action RefreshCurrentPage { get; init; }
}

public sealed class ModSettingsPage
{
    public List<ModSection> Sections { get; init; } = [];
}

public sealed class ModSection
{
    public required string Title { get; init; }
    public List<ModUiRow> Rows { get; init; } = [];
}

public enum ModUiTextStyle
{
    Body,
    BodyStrong,
    Title
}

public abstract class ModUiRow;

public sealed class ModLabelRow : ModUiRow
{
    public required string Text { get; init; }
    public ModUiTextStyle Style { get; init; } = ModUiTextStyle.Body;
}

public sealed class ModInfoRow : ModUiRow
{
    public required string Label { get; init; }
    public required string Value { get; init; }
    public ModUiTextStyle Style { get; init; } = ModUiTextStyle.Body;
}

public sealed class ModListRow : ModUiRow
{
    public required string Label { get; init; }
    public IReadOnlyList<string> Values { get; init; } = [];
    public string EmptyText { get; init; } = "None";
}

public sealed class ModToggleRow : ModUiRow
{
    public required string Label { get; init; }
    public string? Description { get; init; }
    public required bool Value { get; init; }
    public required Action<ModUiActionContext, bool> OnChanged { get; init; }
}

public sealed class ModButtonRow : ModUiRow
{
    public required string Label { get; init; }
    public required Action<ModUiActionContext> OnClick { get; init; }
}

public sealed class ModNavigateRow : ModUiRow
{
    public required string Label { get; init; }
    public required string TargetPageId { get; init; }
}

public sealed class ModProgressRow : ModUiRow
{
    public string? Label { get; init; }

    /// <summary>
    /// Completion in the range 0..1. Values outside the range are clamped by the host.
    /// A null value renders an indeterminate (no fill) bar.
    /// </summary>
    public double? Fraction { get; init; }
}

public enum ModUiIconSize
{
    Small,
    Medium,
    Large,
    Huge
}

/// <summary>
/// Renders a game icon (resolved by id against the icon database, e.g. "ui:energy" or a modded
/// icon id) with an optional caption beneath it.
/// </summary>
public sealed class ModImageRow : ModUiRow
{
    public required string IconId { get; init; }
    public string? Caption { get; init; }
    public ModUiIconSize Size { get; init; } = ModUiIconSize.Medium;
}

public sealed class ModBarButton
{
    public required string Label { get; init; }
    public required Action<ModUiActionContext> OnClick { get; init; }
    public int Width { get; init; } = 160;
}

/// <summary>Lays out several buttons horizontally in a single row (e.g. "Craft" / "Cancel").</summary>
public sealed class ModButtonBarRow : ModUiRow
{
    public IReadOnlyList<ModBarButton> Buttons { get; init; } = [];
}

public sealed class ModItemSlot
{
    public required string IconId { get; init; }
    public int Count { get; init; } = 1;
    public string? Caption { get; init; }
}

/// <summary>
/// A read-only grid of item cells (icon + count), wrapped into <see cref="Columns"/> columns.
/// Intended for recipe ingredients / outputs. No drag/drop — display only.
/// </summary>
public sealed class ModItemSlotGridRow : ModUiRow
{
    public string? Label { get; init; }
    public int Columns { get; init; } = 5;
    public IReadOnlyList<ModItemSlot> Slots { get; init; } = [];
    public string EmptyText { get; init; } = "Empty";
}

/// <summary>
/// A single-line text entry field. <see cref="OnChanged"/> fires as the user edits, with the
/// current text. Use <see cref="Numeric"/> to hint a numeric value (e.g. craft quantity).
/// </summary>
public sealed class ModTextInputRow : ModUiRow
{
    public string? Label { get; init; }
    public string Value { get; init; } = "";
    public string? Placeholder { get; init; }
    public int? MaxLength { get; init; }
    public bool Numeric { get; init; }
    public required Action<ModUiActionContext, string> OnChanged { get; init; }
}
