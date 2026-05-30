namespace Romestead.ModLoader;

public interface ICraftingStationRegistry
{
    /// <summary>
    /// Queues a custom crafting station to register with the game's station
    /// database when content load fires. Registering the same id twice has no effect.
    /// </summary>
    void Register(CraftingStationDefinition station);

    IReadOnlyList<CraftingStationDefinition> Pending { get; }
}
