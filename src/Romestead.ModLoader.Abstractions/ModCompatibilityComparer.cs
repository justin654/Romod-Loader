namespace Romestead.ModLoader;

public static class ModCompatibilityComparer
{
    public static ModCompatibilityComparisonResult CompareLocalToRemote(
        ModCompatibilityReport localReport,
        ModCompatibilitySnapshot remoteSnapshot)
    {
        ArgumentNullException.ThrowIfNull(localReport);
        ArgumentNullException.ThrowIfNull(remoteSnapshot);

        var issues = new List<ModCompatibilityIssue>();
        var localById = localReport.Entries.ToDictionary(entry => entry.Id, StringComparer.OrdinalIgnoreCase);
        var remoteById = remoteSnapshot.Entries.ToDictionary(entry => entry.Id, StringComparer.OrdinalIgnoreCase);

        foreach (var remote in remoteSnapshot.Entries.OrderBy(entry => entry.Id, StringComparer.OrdinalIgnoreCase))
        {
            if (remote.SyncMode == MultiplayerSyncMode.ClientOnly)
            {
                continue;
            }

            if (remote.SyncMode == MultiplayerSyncMode.Incompatible && IsLoaded(remote))
            {
                issues.Add(new ModCompatibilityIssue(
                    ModCompatibilityIssueKind.IncompatiblePresent,
                    remote.Id,
                    $"Host mod {remote.Id} is marked Incompatible and is loaded on the host."));
                continue;
            }

            if (remote.SyncMode != MultiplayerSyncMode.RequiredOnClient)
            {
                continue;
            }

            if (!IsLoaded(remote))
            {
                issues.Add(new ModCompatibilityIssue(
                    ModCompatibilityIssueKind.HostRequiredModNotLoaded,
                    remote.Id,
                    $"Host mod {remote.Id} is RequiredOnClient but the host load state is {remote.LoadState}."));
                continue;
            }

            if (!localById.TryGetValue(remote.Id, out var local))
            {
                issues.Add(new ModCompatibilityIssue(
                    ModCompatibilityIssueKind.MissingRequiredOnClient,
                    remote.Id,
                    $"Host mod {remote.Id} v{remote.Version} is RequiredOnClient but is missing locally."));
                continue;
            }

            if (!IsLoaded(local))
            {
                issues.Add(new ModCompatibilityIssue(
                    ModCompatibilityIssueKind.ClientRequiredModNotLoaded,
                    remote.Id,
                    $"Host mod {remote.Id} is RequiredOnClient, but the local load state is {local.LoadState}."));
                continue;
            }

            if (!string.Equals(local.Version, remote.Version, StringComparison.OrdinalIgnoreCase))
            {
                issues.Add(new ModCompatibilityIssue(
                    ModCompatibilityIssueKind.VersionMismatch,
                    remote.Id,
                    $"Host mod {remote.Id} version mismatch. Local v{local.Version}, host v{remote.Version}."));
            }
        }

        foreach (var local in localReport.Entries.OrderBy(entry => entry.Id, StringComparer.OrdinalIgnoreCase))
        {
            if (local.SyncMode == MultiplayerSyncMode.ClientOnly)
            {
                continue;
            }

            if (local.SyncMode == MultiplayerSyncMode.Incompatible && IsLoaded(local))
            {
                issues.Add(new ModCompatibilityIssue(
                    ModCompatibilityIssueKind.IncompatiblePresent,
                    local.Id,
                    $"Local mod {local.Id} is marked Incompatible and is loaded for multiplayer."));
                continue;
            }

            if (local.SyncMode != MultiplayerSyncMode.RequiredOnClient || !IsLoaded(local))
            {
                continue;
            }

            if (!remoteById.TryGetValue(local.Id, out var remote))
            {
                issues.Add(new ModCompatibilityIssue(
                    ModCompatibilityIssueKind.MissingRequiredOnClient,
                    local.Id,
                    $"Local mod {local.Id} v{local.Version} is RequiredOnClient but is missing on the host."));
                continue;
            }

            if (!IsLoaded(remote))
            {
                issues.Add(new ModCompatibilityIssue(
                    ModCompatibilityIssueKind.HostRequiredModNotLoaded,
                    local.Id,
                    $"Local mod {local.Id} is RequiredOnClient, but the host load state is {remote.LoadState}."));
            }
        }

        return new ModCompatibilityComparisonResult(issues.Count == 0, issues);
    }

    private static bool IsLoaded(ModCompatibilityEntry entry) =>
        entry.Present && entry.LoadState == ModCompatibilityLoadState.Loaded;

    private static bool IsLoaded(ModCompatibilitySnapshotEntry entry) =>
        entry.Present && entry.LoadState == ModCompatibilityLoadState.Loaded;
}
