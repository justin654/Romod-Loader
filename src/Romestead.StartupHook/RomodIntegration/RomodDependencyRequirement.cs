using Romestead.ModLoader;

namespace Romestead.StartupHook.RomodIntegration;

/// <summary>
/// One mod-source-agnostic dependency requirement: "this mod needs another
/// mod with id X (and optionally version &gt;= Y) loaded first."
/// </summary>
internal sealed record DependencyRequirement(string ModId, string? MinVersion);

/// <summary>
/// A discovered mod from either a C# DLL or a .romod package. The loader
/// works with these uniformly: same identity, same sync mode, same
/// dependency graph, same duplicate-id checks. Only <see cref="Load"/>
/// branches on kind.
/// </summary>
internal abstract class DiscoveredMod
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Version { get; init; }
    public required MultiplayerSyncMode SyncMode { get; init; }

    /// <summary>The .romod path or the DLL path on disk.</summary>
    public required string SourcePath { get; init; }

    public required IReadOnlyList<DependencyRequirement> Dependencies { get; init; }

    public abstract DiscoveredModKind Kind { get; }
}

internal enum DiscoveredModKind { Dll, Romod }
