namespace Romestead.ModLoader;

public interface IMapRegistry
{
    IReadOnlyDictionary<string, string> Aliases { get; }

    IReadOnlyDictionary<string, MapFileRegistration> Files { get; }

    IReadOnlyCollection<string> ObservedMapLoads { get; }

    void RegisterAlias(string originalMapId, string replacementMapId);

    bool TryResolveAlias(string mapId, out string replacementMapId);

    void RegisterFile(string mapId, string sourcePath, MapFileFormat format);

    bool TryResolveFile(string mapId, out string sourcePath, out MapFileFormat format);

    bool TryResolveFile(string mapId, out MapFileRegistration registration);
}
