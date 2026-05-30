namespace Romestead.ModLoader;

public enum ModOverlayPlacement
{
    Center,
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight
}

/// <summary>
/// Describes an overlay to show. Content is the same section/row model used by settings pages,
/// with <see cref="ModProgressRow"/> available for progress readouts.
/// </summary>
public sealed class ModOverlayDefinition
{
    public required string Id { get; init; }
    public string? Title { get; init; }
    public ModOverlayPlacement Placement { get; init; } = ModOverlayPlacement.Center;
    public IReadOnlyList<ModSection> Sections { get; init; } = [];
}

/// <summary>
/// The host-facing, mutable view of a shown overlay. The registry updates <see cref="Title"/> and
/// <see cref="Sections"/> in place as the owning mod calls into its handle; placement is fixed at
/// show time.
/// </summary>
public sealed class ModOverlayInstance
{
    public required string Id { get; init; }
    public string? Title { get; set; }
    public ModOverlayPlacement Placement { get; init; }
    public IReadOnlyList<ModSection> Sections { get; set; } = [];
}
