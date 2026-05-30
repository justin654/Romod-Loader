namespace Romestead.ModLoader;

public sealed class ModCompatibilitySnapshot
{
    public string Source = "";
    public List<ModCompatibilitySnapshotEntry> Entries = [];

    public static ModCompatibilitySnapshot FromReport(string source, ModCompatibilityReport report)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(report);

        return new ModCompatibilitySnapshot
        {
            Source = source,
            Entries = report.Entries
                .Select(entry => new ModCompatibilitySnapshotEntry
                {
                    Id = entry.Id,
                    Name = entry.Name,
                    Version = entry.Version,
                    SyncMode = entry.SyncMode,
                    Present = entry.Present,
                    LoadState = entry.LoadState,
                    Detail = entry.Detail
                })
                .ToList()
        };
    }
}

public sealed class ModCompatibilitySnapshotEntry
{
    public string Id = "";
    public string Name = "";
    public string Version = "";
    public MultiplayerSyncMode SyncMode;
    public bool Present;
    public ModCompatibilityLoadState LoadState;
    public string? Detail;
}
