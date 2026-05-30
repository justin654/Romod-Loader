namespace Romestead.ModLoader;

/// <summary>
/// Registers mod-owned settings pages that the loader can render inside the
/// game's settings UI.
/// </summary>
public interface IModUiRegistry
{
    IReadOnlyList<ModSettingsPageDefinition> Pages { get; }
    IReadOnlyList<ModSidebarEntryDefinition> SidebarEntries { get; }

    void RegisterSettingsPage(ModSettingsPageDefinition page);
    void RegisterSidebarEntry(ModSidebarEntryDefinition entry);
}
