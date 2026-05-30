namespace Romestead.ModLoader;

public sealed record PatchGroupInstallResult(
    string Id,
    ModLoaderHostKind HostKind,
    bool Success,
    string Message);
