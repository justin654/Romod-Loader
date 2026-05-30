namespace Romestead.ModLoader;

public interface IPlaceableRegistry
{
    /// <summary>
    /// Queues a placeable custom crafting bench to generate and register when
    /// content load fires. The loader generates the placeable item, placement
    /// construction, save-backed decoration record, and spawned bench entity
    /// wiring for the supplied definition. Registering the same id twice has
    /// no effect.
    /// </summary>
    void Register(ModPlaceableStation placeable);

    IReadOnlyList<ModPlaceableStation> Pending { get; }
}
