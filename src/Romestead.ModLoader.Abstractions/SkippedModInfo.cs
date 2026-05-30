namespace Romestead.ModLoader;

public sealed record SkippedModInfo(
    string Id,
    string Name,
    string Version,
    string Reason,
    string AssemblyPath,
    MultiplayerSyncMode SyncMode);
