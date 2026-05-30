namespace Romestead.ModLoader;

public sealed record FailedModInfo(
    string Id,
    string Name,
    string Version,
    string AssemblyPath,
    MultiplayerSyncMode SyncMode,
    string Reason);
