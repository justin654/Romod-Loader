namespace Romestead.ModLoader;

/// <summary>
/// Opens mod-owned draggable in-game windows (crafting panels, info dialogs, etc.) built from the
/// same declarative section/row model used by settings pages and overlays. The host renders each
/// window into a real game window on the active gameplay desktop.
/// </summary>
public interface IModWindowRegistry
{
    IReadOnlyList<ModWindowInstance> ActiveWindows { get; }
    event Action? WindowsChanged;

    /// <summary>
    /// Opens (or re-opens, replacing content) a window with the given definition. The returned
    /// handle lets the mod update content or close the window later. Opening a window whose id is
    /// already open updates its content in place.
    /// </summary>
    IModWindowHandle Open(ModWindowDefinition definition);

    /// <summary>
    /// Closes the window with the given id if open. Used by the render host when the user clicks a
    /// window's close button; mods normally close via their <see cref="IModWindowHandle"/>.
    /// </summary>
    void Close(string id);
}

public interface IModWindowHandle
{
    string Id { get; }
    bool IsOpen { get; }

    /// <summary>Replaces the window title and content.</summary>
    void Update(string? title, IReadOnlyList<ModSection> sections);

    /// <summary>Replaces the window content, leaving the title unchanged.</summary>
    void Update(IReadOnlyList<ModSection> sections);

    void Close();
}
