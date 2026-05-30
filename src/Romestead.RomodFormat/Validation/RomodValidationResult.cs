namespace Romestead.RomodFormat.Validation;

public sealed class RomodValidationResult
{
    public RomodValidationResult(IReadOnlyList<RomodValidationDiagnostic> diagnostics)
    {
        Diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
    }

    public IReadOnlyList<RomodValidationDiagnostic> Diagnostics { get; }

    public bool HasErrors => Diagnostics.Any(d => d.Severity == RomodValidationSeverity.Error);

    public IEnumerable<RomodValidationDiagnostic> Errors =>
        Diagnostics.Where(d => d.Severity == RomodValidationSeverity.Error);

    public IEnumerable<RomodValidationDiagnostic> Warnings =>
        Diagnostics.Where(d => d.Severity == RomodValidationSeverity.Warning);
}
