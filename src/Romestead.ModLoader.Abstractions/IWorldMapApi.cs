namespace Romestead.ModLoader;

public interface IWorldMapApi
{
    bool IsReady { get; }
    void RevealAll(bool force = false);
}

