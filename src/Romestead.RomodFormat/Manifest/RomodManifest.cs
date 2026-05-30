namespace Romestead.RomodFormat.Manifest;

/// <summary>
/// Parsed representation of <c>romestead.mod.toml</c>. Field defaults
/// already account for omitted-but-allowed values (schemaVersion → 1,
/// syncMode → RequiredOnClient).
/// </summary>
public sealed record RomodManifest
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Version { get; init; }
    public int SchemaVersion { get; init; } = Schema.RomodSchema.CurrentVersion;
    public RomodSyncMode SyncMode { get; init; } = RomodSyncMode.RequiredOnClient;
    public string? Author { get; init; }
    public string? Description { get; init; }
    public string? Homepage { get; init; }
    public IReadOnlyList<RomodDependencyRequirement> Dependencies { get; init; } = [];
}
