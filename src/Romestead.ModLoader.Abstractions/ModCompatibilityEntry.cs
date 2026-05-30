namespace Romestead.ModLoader;

public sealed record ModCompatibilityEntry(
    string Id,
    string Name,
    string Version,
    MultiplayerSyncMode SyncMode,
    bool Present,
    ModCompatibilityLoadState LoadState,
    string? Detail);
