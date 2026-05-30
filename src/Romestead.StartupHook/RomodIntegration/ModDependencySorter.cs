using Romestead.ModLoader;

namespace Romestead.StartupHook.RomodIntegration;

/// <summary>
/// Stable topological sort across the unified mod list. Mods without
/// dependencies fall through to id-alphabetical order for determinism.
/// Missing dependency ids and cycles are reported, not silently tolerated.
/// </summary>
internal static class ModDependencySorter
{
    public sealed record SortResult(
        IReadOnlyList<DiscoveredMod> Order,
        IReadOnlyList<string> MissingDependencyErrors,
        IReadOnlyList<string> VersionMismatchErrors,
        IReadOnlyList<string> CycleErrors);

    public static SortResult Sort(IReadOnlyList<DiscoveredMod> mods, IModLogger log)
    {
        ArgumentNullException.ThrowIfNull(mods);
        ArgumentNullException.ThrowIfNull(log);

        var byId = new Dictionary<string, DiscoveredMod>(StringComparer.OrdinalIgnoreCase);
        foreach (var mod in mods.OrderBy(m => m.Id, StringComparer.OrdinalIgnoreCase))
        {
            // Duplicate mod ids across sources should already be caught upstream as
            // an error; preserve the first occurrence for sort stability.
            byId.TryAdd(mod.Id, mod);
        }

        var missing = new List<string>();
        var versionErrors = new List<string>();

        // Mods whose own dependencies cannot be satisfied. They must be excluded
        // from the load order, and so must anything that (transitively) depends
        // on them.
        var directlyFailedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var mod in mods)
        {
            foreach (var dep in mod.Dependencies)
            {
                if (string.Equals(dep.ModId, "romestead.modloader.core", StringComparison.OrdinalIgnoreCase))
                {
                    // built-in pseudo-dependency on the loader itself; always satisfied.
                    continue;
                }

                if (!byId.TryGetValue(dep.ModId, out var dependee))
                {
                    missing.Add($"{mod.Id}: required dependency '{dep.ModId}' is not present.");
                    directlyFailedIds.Add(mod.Id);
                    continue;
                }

                if (dep.MinVersion is not null &&
                    VersionCompare(dependee.Version, dep.MinVersion) < 0)
                {
                    versionErrors.Add(
                        $"{mod.Id}: dependency '{dep.ModId}' requires >= {dep.MinVersion}, " +
                        $"but {dep.ModId} v{dependee.Version} is loaded.");
                    directlyFailedIds.Add(mod.Id);
                }
            }
        }

        // Kahn's algorithm: collect mods with zero unresolved deps, then peel layers.
        // We track ALL declared (non-loader) deps, even ones whose target failed —
        // so the failure cascade propagates naturally to dependents.
        var remainingDeps = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var dependents = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var mod in mods)
        {
            var depSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var dep in mod.Dependencies)
            {
                if (string.Equals(dep.ModId, "romestead.modloader.core", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                if (byId.ContainsKey(dep.ModId))
                {
                    depSet.Add(dep.ModId);
                    if (!dependents.TryGetValue(dep.ModId, out var dlist))
                    {
                        dlist = new List<string>();
                        dependents[dep.ModId] = dlist;
                    }
                    dlist.Add(mod.Id);
                }
            }
            remainingDeps[mod.Id] = depSet;
        }

        var order = new List<DiscoveredMod>(mods.Count);
        var ready = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (id, set) in remainingDeps)
        {
            if (set.Count == 0 && !directlyFailedIds.Contains(id))
            {
                ready.Add(id);
            }
        }

        while (ready.Count > 0)
        {
            var next = ready.Min!;
            ready.Remove(next);
            order.Add(byId[next]);

            if (dependents.TryGetValue(next, out var children))
            {
                foreach (var child in children)
                {
                    var set = remainingDeps[child];
                    set.Remove(next);
                    if (set.Count == 0 && !directlyFailedIds.Contains(child))
                    {
                        ready.Add(child);
                    }
                }
            }
        }

        // Anything not in `order` is either (a) directly failed (missing/version
        // mismatch), (b) part of a cycle, or (c) transitively depending on (a) or (b).
        // Cycle reporting distinguishes (b) from the cascade — a cycle id still
        // has unresolved deps; a cascade-skipped id has only failed-id deps removed.
        var orderedIds = new HashSet<string>(order.Select(m => m.Id), StringComparer.OrdinalIgnoreCase);
        var cycleErrors = new List<string>();
        var stillUnresolved = byId.Keys
            .Where(id => !orderedIds.Contains(id) && !directlyFailedIds.Contains(id))
            .Where(id => remainingDeps[id].Count > 0)
            .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (stillUnresolved.Length > 0)
        {
            cycleErrors.Add(
                "Dependency cycle detected among the following mod ids: " +
                string.Join(", ", stillUnresolved) +
                ". These mods will be skipped.");
            // Don't append cycle mods to the order — they'd cause initialization errors.
        }

        return new SortResult(order, missing, versionErrors, cycleErrors);
    }

    /// <summary>
    /// Lexicographic numeric compare on dotted versions. Non-numeric segments
    /// fall back to string compare. Good enough for "≥X.Y.Z" gating without
    /// pulling in SemVer.
    /// </summary>
    private static int VersionCompare(string left, string right)
    {
        var leftParts = left.Split('.');
        var rightParts = right.Split('.');
        var max = Math.Max(leftParts.Length, rightParts.Length);

        for (var i = 0; i < max; i++)
        {
            var l = i < leftParts.Length ? leftParts[i] : "0";
            var r = i < rightParts.Length ? rightParts[i] : "0";

            if (int.TryParse(l, out var li) && int.TryParse(r, out var ri))
            {
                var cmp = li.CompareTo(ri);
                if (cmp != 0)
                {
                    return cmp;
                }
            }
            else
            {
                var cmp = string.CompareOrdinal(l, r);
                if (cmp != 0)
                {
                    return cmp;
                }
            }
        }

        return 0;
    }
}
