namespace Romestead.ModLoader;

public sealed record ModCompatibilityReport(IReadOnlyList<ModCompatibilityEntry> Entries);
