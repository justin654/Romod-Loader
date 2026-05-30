namespace Romestead.ModLoader;

public interface IRecipeRegistry
{
    /// <summary>
    /// Queues a recipe to be registered with the game's recipe database when
    /// content load fires. Registering the same id twice has no effect.
    /// </summary>
    void Register(RecipeDefinition recipe);

    IReadOnlyList<RecipeDefinition> Pending { get; }
}
