namespace Romestead.ModLoader.ClientCore;

internal static class MapFileRedirectResolver
{
    internal static bool TryPrepareLoadPath(
        MapFileRegistration registration,
        out string loadPathWithoutExtension,
        out string? failureReason)
    {
        loadPathWithoutExtension = string.Empty;
        failureReason = null;

        var sourcePath = Path.GetFullPath(registration.SourcePath);
        if (!File.Exists(sourcePath))
        {
            failureReason = "source file does not exist";
            return false;
        }

        var extension = registration.Format switch
        {
            MapFileFormat.Cmx => ".cmx",
            MapFileFormat.Tmx => ".tmx",
            _ => throw new ArgumentOutOfRangeException(nameof(registration), registration.Format, null),
        };

        var expectedExtension = Path.GetExtension(sourcePath);
        if (!string.Equals(expectedExtension, extension, StringComparison.OrdinalIgnoreCase))
        {
            failureReason = $"source extension '{expectedExtension}' does not match format {registration.Format}";
            return false;
        }

        loadPathWithoutExtension = Path.ChangeExtension(sourcePath, null);
        return true;
    }
}
