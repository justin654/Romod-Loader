namespace Romestead.ModLoader;

/// <summary>
/// Opens the game's native crafting windows for mods. Unlike <see cref="IModWindowRegistry"/> (which
/// builds windows from the declarative section/row model), this drives the real in-game crafting UI
/// so it looks and behaves exactly like the campfire/workbench windows and reflects the live player
/// inventory. Client-only; accessing this on a dedicated server throws.
/// </summary>
public interface IModCraftingRegistry
{
    IReadOnlyList<ModStationCraftingInstance> ActiveStationWindows { get; }
    event Action? Changed;

    /// <summary>
    /// Opens (or re-targets, if the id is already open) the native crafting window for the given
    /// stations. The returned handle lets the mod close it later.
    /// </summary>
    IModCraftingWindowHandle OpenStation(ModStationCraftingDefinition definition);

    /// <summary>Closes the crafting window with the given id if open.</summary>
    void Close(string id);
}

public interface IModCraftingWindowHandle
{
    string Id { get; }
    bool IsOpen { get; }
    void Close();
}
