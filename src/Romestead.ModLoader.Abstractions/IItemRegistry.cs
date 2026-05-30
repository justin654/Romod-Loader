namespace Romestead.ModLoader;

public interface IItemRegistry
{
    /// <summary>
    /// Queues an item to be registered with the game's item database when
    /// content load fires. Registering the same id twice has no effect.
    /// </summary>
    void Register(ItemDefinition item);

    /// <summary>
    /// All items that have been queued but not yet committed. Mostly intended
    /// for the core framework mod; user mods don't need to read this.
    /// </summary>
    IReadOnlyList<ItemDefinition> Pending { get; }
}
