namespace Romestead.RomodFormat.Schema;

/// <summary>
/// Version constants and helpers for the <c>schemaVersion</c> field on
/// the package manifest. Bumping the current version means existing
/// packages must be migrated forward by a registered
/// <see cref="IRomodSchemaMigrator"/>; the migration code can be added
/// without touching any parser or mapper.
/// </summary>
public static class RomodSchema
{
    public const int CurrentVersion = 1;
    public const int OldestSupportedVersion = 1;
}
