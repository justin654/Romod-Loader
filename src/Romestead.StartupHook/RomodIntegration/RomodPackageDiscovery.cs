using Romestead.ModLoader;
using Romestead.RomodFormat;
using Romestead.RomodFormat.Package;
using Romestead.RomodFormat.Validation;

namespace Romestead.StartupHook.RomodIntegration;

/// <summary>
/// Walks <c>artifacts/mods/</c> looking for <c>*.romod</c> archives, runs each
/// through the read+migrate+validate pipeline, and yields one
/// <see cref="DiscoveredRomodMod"/> per successfully parsed package. Failures
/// are reported as diagnostics so the user can see them in
/// <c>romestead-loader.log</c>, but they don't crash the loader — other mods
/// keep loading.
/// </summary>
internal static class RomodPackageDiscovery
{
    public sealed record Result(
        IReadOnlyList<DiscoveredRomodMod> Discovered,
        IReadOnlyList<(string Path, string Reason)> FailedPackages);

    public static Result Discover(string modsDirectory, IModLogger log)
    {
        ArgumentNullException.ThrowIfNull(modsDirectory);
        ArgumentNullException.ThrowIfNull(log);

        var discovered = new List<DiscoveredRomodMod>();
        var failed = new List<(string Path, string Reason)>();

        if (!Directory.Exists(modsDirectory))
        {
            return new Result(discovered, failed);
        }

        var bridgeLog = new ModLoggerBridge(log);
        var pipeline = new RomodPackagePipeline();

        foreach (var path in Directory.EnumerateFiles(modsDirectory, "*.romod", SearchOption.TopDirectoryOnly)
                     .OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var result = pipeline.Run(path, bridgeLog);

                foreach (var diag in result.Validation.Diagnostics)
                {
                    switch (diag.Severity)
                    {
                        case RomodValidationSeverity.Error: log.Error(diag.ToString()); break;
                        case RomodValidationSeverity.Warning: log.Warn(diag.ToString()); break;
                        default: log.Info(diag.ToString()); break;
                    }
                }

                if (result.Validation.HasErrors)
                {
                    failed.Add((path, $"Validation failed: {result.Validation.Errors.First().Message}"));
                    continue;
                }

                discovered.Add(new DiscoveredRomodMod
                {
                    Id = result.Document.Manifest.Id,
                    Name = result.Document.Manifest.Name,
                    Version = result.Document.Manifest.Version,
                    SyncMode = RomodToDefinitionMapper.MapSyncMode(result.Document.Manifest.SyncMode),
                    SourcePath = path,
                    Dependencies = result.Document.Manifest.Dependencies
                        .Select(d => new DependencyRequirement(d.ModId, d.MinVersion))
                        .ToArray(),
                    Document = result.Document
                });
            }
            catch (RomodFormatException ex)
            {
                log.Error($"Failed to read .romod package {path}: {ex.Message}");
                failed.Add((path, ex.Message));
            }
            catch (Exception ex)
            {
                log.Error($"Unexpected error reading .romod package {path}: {ex.Message}", ex);
                failed.Add((path, $"Unexpected: {ex.Message}"));
            }
        }

        return new Result(discovered, failed);
    }

    private sealed class ModLoggerBridge : IRomodLog
    {
        private readonly IModLogger _inner;
        public ModLoggerBridge(IModLogger inner) { _inner = inner; }
        public void Info(string message) => _inner.Info(message);
        public void Warn(string message) => _inner.Warn(message);
        public void Error(string message) => _inner.Error(message);
    }
}

internal sealed class DiscoveredRomodMod : DiscoveredMod
{
    public required RomodPackageDocument Document { get; init; }
    public override DiscoveredModKind Kind => DiscoveredModKind.Romod;
}
