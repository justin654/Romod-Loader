namespace Romestead.ModLoader;

public sealed record LoadedModInfo(
    string Id,
    string Name,
    string Version,
    string AssemblyPath,
    MultiplayerSyncMode SyncMode);
