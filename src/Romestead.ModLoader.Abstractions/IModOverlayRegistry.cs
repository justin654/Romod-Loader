namespace Romestead.ModLoader;

/// <summary>
/// Lets mods render their own declarative overlays (HUD panels, loading notices, status
/// readouts) on top of the active game scene. Overlays reuse the same <see cref="ModSection"/>
/// and <see cref="ModUiRow"/> primitives as settings pages, so a mod describes content rather
/// than touching the Candide UI types directly.
///
/// The loader hosts the rendering: calling <see cref="Show"/> hands back a live handle the mod
/// can update or hide, and the host repaints whenever the active set changes. Calls are safe
/// from any thread (content injection runs off the main thread); the host marshals rendering
/// onto the UI thread.
/// </summary>
public interface IModOverlayRegistry
{
    /// <summary>Snapshot of the overlays that should currently be drawn, in show order.</summary>
    IReadOnlyList<ModOverlayInstance> ActiveOverlays { get; }

    /// <summary>Raised whenever an overlay is shown, updated, or hidden.</summary>
    event Action? OverlaysChanged;

    /// <summary>
    /// Shows an overlay and returns a handle for live updates. Re-showing an overlay with an id
    /// that is already visible replaces its content in place rather than stacking a duplicate.
    /// </summary>
    IModOverlayHandle Show(ModOverlayDefinition definition);
}

/// <summary>
/// A live reference to a shown overlay. The mod keeps this to update progress/content or to
/// hide the overlay when its work finishes. Updates are no-ops once <see cref="Hide"/> has run.
/// </summary>
public interface IModOverlayHandle
{
    string Id { get; }
    bool IsVisible { get; }

    /// <summary>Replaces the overlay's title (null leaves it unchanged).</summary>
    void Update(string? title, IReadOnlyList<ModSection> sections);

    /// <summary>Replaces the overlay's content, keeping the current title.</summary>
    void Update(IReadOnlyList<ModSection> sections);

    void Hide();
}
