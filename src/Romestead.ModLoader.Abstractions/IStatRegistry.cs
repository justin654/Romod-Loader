namespace Romestead.ModLoader;

public interface IStatRegistry
{
    /// <summary>
    /// Queues a stat to be registered with the game's entity stat database
    /// when content load fires. Registering the same id twice has no effect.
    /// </summary>
    void Register(StatDefinition stat);

    /// <summary>
    /// All stats that have been queued but not yet committed. Mostly intended
    /// for the core framework; user mods don't need to read this.
    /// </summary>
    IReadOnlyList<StatDefinition> Pending { get; }
}
