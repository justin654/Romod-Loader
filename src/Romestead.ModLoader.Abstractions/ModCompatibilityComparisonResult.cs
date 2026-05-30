namespace Romestead.ModLoader;

public sealed record ModCompatibilityComparisonResult(
    bool Compatible,
    IReadOnlyList<ModCompatibilityIssue> Issues);

public sealed record ModCompatibilityIssue(
    ModCompatibilityIssueKind Kind,
    string ModId,
    string Message);

public enum ModCompatibilityIssueKind
{
    MissingRequiredOnClient,
    HostRequiredModNotLoaded,
    ClientRequiredModNotLoaded,
    VersionMismatch,
    IncompatiblePresent
}
