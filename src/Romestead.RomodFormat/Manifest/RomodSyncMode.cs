namespace Romestead.RomodFormat.Manifest;

/// <summary>
/// Mirrors <c>Romestead.ModLoader.MultiplayerSyncMode</c> but lives in a
/// game-agnostic assembly so the package format and the CLI tool don't
/// drag in Abstractions. The runtime mapper converts between this and
/// the loader enum value.
/// </summary>
public enum RomodSyncMode
{
    ClientOnly,
    ServerOnly,
    RequiredOnClient,
    Incompatible
}
