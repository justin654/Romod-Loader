namespace Romestead.ModLoader;

public static class MapKeyNormalizer
{
    public static string Normalize(string mapId)
    {
        if (string.IsNullOrWhiteSpace(mapId))
        {
            return mapId;
        }

        return mapId.Replace('\\', '/').ToLowerInvariant();
    }
}
