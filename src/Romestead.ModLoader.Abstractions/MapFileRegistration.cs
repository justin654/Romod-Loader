namespace Romestead.ModLoader;

public sealed class MapFileRegistration
{
    public MapFileRegistration(string mapId, string sourcePath, MapFileFormat format)
    {
        MapId = mapId;
        SourcePath = sourcePath;
        Format = format;
    }

    public string MapId { get; }

    public string SourcePath { get; }

    public MapFileFormat Format { get; }
}
