namespace Romestead.RomodFormat.Manifest;

/// <summary>
/// One entry from the <c>[dependencies]</c> table on the manifest. The
/// constraint syntax is intentionally minimal: an optional <c>&gt;=X.Y.Z</c>
/// minimum-version requirement. Anything more elaborate (caret ranges,
/// upper bounds, alternates) is not yet supported.
/// </summary>
public sealed record RomodDependencyRequirement(string ModId, string? MinVersion)
{
    public string RawSpec => MinVersion is null ? "*" : $">={MinVersion}";
}
