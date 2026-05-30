namespace Romestead.ModLoader;

public sealed record ModMetadataInfo(
    string ModId,
    MultiplayerSyncMode SyncMode,
    string? Author,
    string? Description,
    string? Homepage,
    IReadOnlyList<string> Dependencies);
