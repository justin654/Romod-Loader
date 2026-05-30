using Romestead.RomodFormat.Package;

namespace Romestead.RomodFormat.Schema;

/// <summary>
/// Migrates a parsed package document from one <c>schemaVersion</c> to the
/// next. Migrators are chained one-step-at-a-time: a package at v1 going
/// to v3 runs the v1→v2 then the v2→v3 migrator. Add new migrators by
/// registering them with <see cref="RomodSchemaMigratorRegistry"/>; no
/// other code needs to change.
/// </summary>
public interface IRomodSchemaMigrator
{
    int FromVersion { get; }
    int ToVersion { get; }
    RomodPackageDocument Migrate(RomodPackageDocument package, IRomodLog log);
}
