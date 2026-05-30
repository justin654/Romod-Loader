namespace Romestead.ModLoader;

public interface IMultiplayerApi
{
    bool IsMultiplayer { get; }
    bool IsHost { get; }
    bool IsClient { get; }
    bool IsServerAuthority { get; }
}
