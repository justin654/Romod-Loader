using HarmonyLib;
using Romestead.ModLoader;

namespace Romestead.StartupHook;

public sealed record CapabilityPatchState(
    string Id,
    ModCapabilityState State,
    string Summary);

public sealed class PatchGroupExecutionResult
{
    public PatchGroupExecutionResult(bool success, string message, IReadOnlyList<CapabilityPatchState>? capabilityStates = null)
    {
        Success = success;
        Message = message;
        CapabilityStates = capabilityStates ?? [];
    }

    public bool Success { get; }
    public string Message { get; }
    public IReadOnlyList<CapabilityPatchState> CapabilityStates { get; }

    public static PatchGroupExecutionResult SuccessResult(string message, params CapabilityPatchState[] capabilityStates) =>
        new(true, message, capabilityStates);

    public static PatchGroupExecutionResult FailureResult(string message, params CapabilityPatchState[] capabilityStates) =>
        new(false, message, capabilityStates);
}

public sealed class PatchGroupDefinition
{
    public PatchGroupDefinition(
        string id,
        ModLoaderHostKind hostKind,
        Func<Harmony, PatchGroupExecutionResult> install,
        string? capabilityId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(install);
        Id = id;
        HostKind = hostKind;
        Install = install;
        CapabilityId = capabilityId;
    }

    public string Id { get; }
    public ModLoaderHostKind HostKind { get; }
    public Func<Harmony, PatchGroupExecutionResult> Install { get; }
    public string? CapabilityId { get; }
}

public static class PatchGroupInstaller
{
    private const string SkipPatchGroupsEnvVar = "ROMESTEAD_SKIP_PATCH_GROUPS";

    public static void Install(IModLogger logger, Harmony harmony, IEnumerable<PatchGroupDefinition> groups)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(harmony);
        ArgumentNullException.ThrowIfNull(groups);

        var skippedGroups = ParseSkippedGroups();
        foreach (var group in groups)
        {
            if (skippedGroups.Contains(group.Id))
            {
                var skippedMessage = $"Skipped by {SkipPatchGroupsEnvVar}.";
                RecordResult(logger, group, PatchGroupExecutionResult.FailureResult(skippedMessage));
                continue;
            }

            try
            {
                var result = group.Install(harmony) ?? PatchGroupExecutionResult.FailureResult("Installer returned no result.");
                RecordResult(logger, group, result);
            }
            catch (Exception ex)
            {
                RecordResult(
                    logger,
                    group,
                    PatchGroupExecutionResult.FailureResult(ex.Message));
                logger.Error($"[patches] Group '{group.Id}' threw during install.", ex);
            }
        }
    }

    public static void PatchClasses(Harmony harmony, params Type[] patchTypes)
    {
        ArgumentNullException.ThrowIfNull(harmony);
        ArgumentNullException.ThrowIfNull(patchTypes);

        foreach (var patchType in patchTypes)
        {
            harmony.CreateClassProcessor(patchType).Patch();
        }
    }

    private static HashSet<string> ParseSkippedGroups()
    {
        var raw = Environment.GetEnvironmentVariable(SkipPatchGroupsEnvVar);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }

        return raw
            .Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.Ordinal);
    }

    private static void RecordResult(IModLogger logger, PatchGroupDefinition group, PatchGroupExecutionResult result)
    {
        var resultRecord = new PatchGroupInstallResult(group.Id, group.HostKind, result.Success, result.Message);
        ModRegistries.Diagnostics.RegisterPatchGroupResult(resultRecord);

        var states = result.CapabilityStates;
        if (states.Count == 0 && !string.IsNullOrWhiteSpace(group.CapabilityId))
        {
            states =
            [
                new CapabilityPatchState(
                    group.CapabilityId,
                    result.Success ? ModCapabilityState.Available : ModCapabilityState.Unavailable,
                    result.Message)
            ];
        }

        foreach (var state in states)
        {
            ModRegistries.SetCapabilityState(state.Id, state.State, state.Summary);
        }

        if (result.Success)
        {
            logger.Info($"[patches] Installed '{group.Id}' on {group.HostKind}: {result.Message}");
            return;
        }

        logger.Warn($"[patches] '{group.Id}' on {group.HostKind} did not install cleanly: {result.Message}");
    }
}
