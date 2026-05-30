namespace Romestead.ModLoader;

/// <summary>
/// Game-wide lifecycle events. Subscribe from a mod's Initialize method.
/// Handlers run on the game's main thread.
/// </summary>
public interface IModLifecycle
{
    /// <summary>
    /// Fires once when the game has finished loading core content
    /// (Candide.CandideEngine.LoadContent). At this point item / recipe
    /// registries are fully committed and the game is ready to render.
    /// </summary>
    event Action GameReady;

    /// <summary>
    /// Fires each time a top-level scene's LoadContent runs (main menu,
    /// loading screen, gameplay scene).
    /// </summary>
    event Action<SceneInfo> SceneChanged;
}

/// <summary>
/// Identifies the kind of scene currently activating.
/// </summary>
public sealed record SceneInfo(string Name)
{
    public static readonly SceneInfo MainMenu      = new("MainMenu");
    public static readonly SceneInfo LoadingScreen = new("LoadingScreen");
    public static readonly SceneInfo Gameplay     = new("Gameplay");
}
