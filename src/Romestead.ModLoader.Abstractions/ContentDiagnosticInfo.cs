namespace Romestead.ModLoader;

public sealed record ContentDiagnosticInfo(
    string ModId,
    string ContentType,
    string ContentId,
    string Status,
    string Detail);
