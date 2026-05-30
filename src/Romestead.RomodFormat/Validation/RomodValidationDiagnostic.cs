namespace Romestead.RomodFormat.Validation;

public enum RomodValidationSeverity { Info, Warning, Error }

public sealed record RomodValidationDiagnostic(
    RomodValidationSeverity Severity,
    string PackageId,
    string? ArchiveRelativePath,
    string Message)
{
    public override string ToString() =>
        ArchiveRelativePath is null
            ? $"[{Severity}] [{PackageId}] {Message}"
            : $"[{Severity}] [{PackageId}] {ArchiveRelativePath}: {Message}";
}
