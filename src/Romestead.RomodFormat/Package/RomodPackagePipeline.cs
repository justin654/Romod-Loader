using Romestead.RomodFormat.Schema;
using Romestead.RomodFormat.Validation;

namespace Romestead.RomodFormat.Package;

/// <summary>
/// Convenience top-level: read → migrate → validate. Used by both the
/// runtime loader and the CLI <c>validate</c> command so they share the
/// exact same pipeline.
/// </summary>
public sealed class RomodPackagePipeline
{
    private readonly RomodPackageReader _reader;
    private readonly RomodSchemaMigratorRegistry _migrators;
    private readonly RomodPackageValidator _validator;

    public RomodPackagePipeline()
        : this(new RomodPackageReader(), RomodSchemaMigratorRegistry.CreateDefault(), new RomodPackageValidator())
    { }

    public RomodPackagePipeline(
        RomodPackageReader reader,
        RomodSchemaMigratorRegistry migrators,
        RomodPackageValidator validator)
    {
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        _migrators = migrators ?? throw new ArgumentNullException(nameof(migrators));
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
    }

    public sealed record Result(RomodPackageDocument Document, RomodValidationResult Validation);

    public Result Run(string archivePath, IRomodLog log)
    {
        ArgumentNullException.ThrowIfNull(archivePath);
        ArgumentNullException.ThrowIfNull(log);

        var raw = _reader.ReadFromFile(archivePath, log);
        var migrated = _migrators.MigrateToCurrent(raw, log);
        var validation = _validator.Validate(migrated);
        return new Result(migrated, validation);
    }
}
