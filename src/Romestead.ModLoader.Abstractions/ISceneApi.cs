namespace Romestead.ModLoader;

public interface ISceneApi
{
    bool IsGameReady { get; }
    SceneInfo? CurrentScene { get; }
    event Action GameReady;
    event Action<SceneInfo> SceneChanged;
}

