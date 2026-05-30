using Romestead.RomodFormat.Package;

namespace Romestead.RomodFormat.Schema;

public sealed class RomodSchemaMigratorRegistry
{
    private readonly Dictionary<int, IRomodSchemaMigrator> _byFromVersion = new();

    public static RomodSchemaMigratorRegistry CreateDefault() => new();

    public void Register(IRomodSchemaMigrator migrator)
    {
        ArgumentNullException.ThrowIfNull(migrator);
        if (migrator.ToVersion != migrator.FromVersion + 1)
        {
            throw new ArgumentException(
                $"Migrator {migrator.GetType().Name} must step exactly one version " +
                $"(got {migrator.FromVersion} -> {migrator.ToVersion}).",
                nameof(migrator));
        }

        if (!_byFromVersion.TryAdd(migrator.FromVersion, migrator))
        {
            throw new InvalidOperationException(
                $"A migrator from schemaVersion {migrator.FromVersion} is already registered.");
        }
    }

    public RomodPackageDocument MigrateToCurrent(RomodPackageDocument document, IRomodLog log)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(log);

        var current = document;
        while (current.Manifest.SchemaVersion < RomodSchema.CurrentVersion)
        {
            var from = current.Manifest.SchemaVersion;
            if (!_byFromVersion.TryGetValue(from, out var migrator))
            {
                throw new RomodFormatException(
                    $"[{current.Manifest.Id}] No migrator registered from schemaVersion " +
                    $"{from} to {from + 1}. Cannot load this package on schemaVersion " +
                    $"{RomodSchema.CurrentVersion}.");
            }

            log.Info($"[{current.Manifest.Id}] Migrating schemaVersion {from} -> {migrator.ToVersion}");
            current = migrator.Migrate(current, log);
        }

        if (current.Manifest.SchemaVersion > RomodSchema.CurrentVersion)
        {
            throw new RomodFormatException(
                $"[{current.Manifest.Id}] Package was built for schemaVersion " +
                $"{current.Manifest.SchemaVersion}, but this loader only supports up to " +
                $"{RomodSchema.CurrentVersion}. Upgrade the loader.");
        }

        return current;
    }
}
